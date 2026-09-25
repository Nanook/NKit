using Nanook.GrindCore;
using NKitDataStore.Binary.Serialization;
using NKitDataStore.Compression;
using NKitDataStore.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Binary index implementation of IDataStoreDataAccess.
    /// Manages per-set BinaryIndexFile and ShardFileManager instances.
    /// Drop-in replacement for SqliteDataStoreDataAccess.
    /// </summary>
    public class BinaryDataStoreDataAccess : IDataStoreDataAccess
    {
        private readonly string _baseDirectory;
        private readonly string? _sourceDirectory;
        private readonly ConcurrentDictionary<string, BinaryIndexFile> _indexFiles = new();

        // Tracks sets for which crash recovery has already run in this instance (once per set).
        // Cleared for a set when a compaction fails, so the next access re-runs recovery.
        private readonly ConcurrentDictionary<string, byte> _recoveredSets = new();
        private readonly ConcurrentDictionary<string, ShardFileManager> _shardFileManagers = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _setWriteLocks = new();
        private readonly ConcurrentDictionary<string, SetUsageInfo> _setUsage = new();
        private readonly ConcurrentDictionary<string, InfoRecord> _infoCache = new();

        // Embedded mode tracking: which sets are in embedded mode and their shard boundaries
        private readonly ConcurrentDictionary<string, bool> _embeddedMode = new();
        private readonly ConcurrentDictionary<string, long> _shardBoundary = new();

        // Parallel compression writers per set
        private readonly ConcurrentDictionary<string, BlockWriter> _blockWriters = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<BlockCompressor>> _compressorPools = new();
        private readonly ConcurrentDictionary<string, EmbeddedIndexCommitter> _committers = new();

        // Per-set caches for decompressed image sections (avoids repeated decompression during VFS mount)
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>> _metadataCache = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>> _blockMapCache = new();

        private bool _disposed;

        /// <summary>
        /// When true, CompactSetEmbedded writes detailed step logs to a temp file.
        /// Defaults to false. Set programmatically (no environment variable).
        /// </summary>
        internal static bool CompactDebugLogging { get; set; } = false;

        /// <summary>
        /// Per-set usage tracking for writer/reader lifecycle management.
        /// </summary>
        private class SetUsageInfo
        {
            public int ReaderCount;
            public int WriterCount;
            public readonly object Sync = new object();
        }

        /// <summary>
        /// Creates a new BinaryDataStoreDataAccess instance.
        /// </summary>
        /// <param name="baseDirectory">Base directory where .nkds binary index files and shard files are stored.</param>
        /// <param name="sourceDirectory">Optional source directory for read-only fallback (e.g., read-only media).</param>
        public BinaryDataStoreDataAccess(string baseDirectory, string? sourceDirectory = null)
        {
            _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
            _sourceDirectory = sourceDirectory;

            if (!Directory.Exists(_baseDirectory))
            {
                Directory.CreateDirectory(_baseDirectory);
            }
        }

        #region Core Methods

        /// <summary>
        /// Ensures that the binary index file for a given set exists.
        /// Creates a new file if it doesn't exist, or opens and validates the existing one.
        /// For embedded mode (shardSize=0): creates a single self-contained .nkds file
        /// with the binary index appended after block data, followed by an EmbeddedFooter.
        /// For separate mode (shardSize&gt;0): creates a standalone index file as before.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <param name="shardSize">The maximum size of each shard in bytes (0 = embedded mode, single file).</param>
        /// <param name="blockSize">Maximum size of each stored block in bytes (0 = use default 0x10000).</param>
        /// <returns>An InfoRecord with the set's configuration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if shardSize is negative.</exception>
        public InfoRecord EnsureSetExists(string setName, long shardSize, int blockSize = 0)
        {
            if (shardSize < 0)
                throw new ArgumentOutOfRangeException(nameof(shardSize), shardSize, "Shard size cannot be negative.");

            if (blockSize == 0)
                blockSize = 0x10000; // Default 64 KiB

            // Perform crash recovery before any file existence checks.
            // This ensures intermediate states from interrupted writes/compactions
            // are resolved before we attempt to open or create the set.
            // Recovery runs for both embedded (shardSize == 0) and separate (shardSize > 0) modes.
            // Recovery ordering: file-level (.compact.tmp, shard .tmp) → dual-header (in BinaryIndexFile.Open)
            //                    → orphaned tail truncation → header validation.
            recoverIfNeeded(setName);

            string filePath = GetIndexFilePath(setName);

            if (shardSize == 0)
            {
                // Embedded mode: single self-contained .nkds file
                BinaryIndexFile indexFile = _indexFiles.GetOrAdd(setName, _ =>
                {
                    if (File.Exists(filePath) && detectEmbeddedMode(filePath))
                    {
                        // Open existing embedded file: read footer, compute boundary, open with baseOffset
                        (long indexSize, bool _) = readEmbeddedFooter(filePath);
                        long boundary = computeShardBoundary(filePath, indexSize);
                        _embeddedMode[setName] = true;
                        _shardBoundary[setName] = boundary;
                        return BinaryIndexFile.Open(filePath, baseOffset: boundary);
                    }
                    else if (!File.Exists(filePath))
                    {
                        // Create new embedded file: empty index + EmbeddedFooter
                        string? dir = Path.GetDirectoryName(filePath);
                        if (dir != null && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        // Create the index at baseOffset=0 (no block data yet, Shard_Boundary=0)
                        BinaryIndexFile newIndex = BinaryIndexFile.Create(filePath, shardSize: 0, blockSize, maxOffsetBlocks: 336, baseOffset: 0);
                        long indexFileSize = newIndex.IndexSize;

                        // Close the index file before appending the footer (releases the file lock)
                        newIndex.Dispose();

                        // Append the EmbeddedFooter after the index content
                        // The file currently contains just the index; append footer at the end
                        using (FileStream appendStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.Read))
                        {
                            appendStream.Seek(0, SeekOrigin.End);
                            EmbeddedFooter footer = new EmbeddedFooter(indexFileSize);
                            byte[] footerBytes = footer.Serialize();
                            appendStream.Write(footerBytes, 0, footerBytes.Length);
                            appendStream.Flush(flushToDisk: true);
                        }

                        _embeddedMode[setName] = true;
                        _shardBoundary[setName] = 0;
                        return BinaryIndexFile.Open(filePath, baseOffset: 0);
                    }
                    else
                    {
                        // File exists but is not in embedded mode (e.g., a standalone index from
                        // a previous separate-mode configuration or a mid-write recovery state).
                        // Open as a regular index file.
                        return BinaryIndexFile.Open(filePath);
                    }
                });

                // Create a ShardFileManager that points to the embedded file for block reads
                // but does NOT create a _0000.nkds shard file.
                // In embedded mode, the ShardFileManager reads blocks from the main .nkds file.
                // The constructor's findCurrentShard() will not find any _0000.nkds file,
                // which is correct for embedded mode.
                _shardFileManagers.GetOrAdd(setName, _ =>
                {
                    ShardFileManager sfm = new ShardFileManager(_baseDirectory, setName, shardSize: 0, _sourceDirectory, blockSize);
                    if (_shardBoundary.TryGetValue(setName, out long boundary) && boundary > 0)
                        sfm.SetShardBoundary(boundary);
                    return sfm;
                });

                // Build and cache InfoRecord from header
                InfoRecord info = buildInfoRecord(indexFile.Header);
                _infoCache[setName] = info;
                return info;
            }
            else
            {
                // Separate mode (shardSize > 0): standalone index file, no footer
                BinaryIndexFile indexFile = _indexFiles.GetOrAdd(setName, _ =>
                {
                    if (File.Exists(filePath))
                    {
                        // Open and validate existing file
                        return BinaryIndexFile.Open(filePath);
                    }
                    else
                    {
                        // Ensure directory exists
                        string? dir = Path.GetDirectoryName(filePath);
                        if (dir != null && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        // Create new file with default max_offset_blocks = 336
                        return BinaryIndexFile.Create(filePath, shardSize, blockSize, 336);
                    }
                });

                // Build and cache InfoRecord from header
                InfoRecord info = buildInfoRecord(indexFile.Header);
                _infoCache[setName] = info;
                return info;
            }
        }

        /// <summary>
        /// Gets the set metadata from the binary index file header.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <returns>An InfoRecord with Version, ShardSize, BlockSize, and MaxOffsetBlocks.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the set has not been opened.</exception>
        public InfoRecord GetSetInfo(string setName)
        {
            if (_infoCache.TryGetValue(setName, out InfoRecord? cached))
                return cached;

            BinaryIndexFile indexFile = getIndexFile(setName);
            InfoRecord info = buildInfoRecord(indexFile.Header);
            _infoCache[setName] = info;
            return info;
        }

        /// <summary>
        /// Begins a new transaction for write operations on the specified set.
        /// Loads the block index into memory for deduplication lookups.
        /// For embedded mode: extracts to separate mode before creating the transaction,
        /// then re-embeds on commit.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <returns>A BinaryTransaction that must be disposed by the caller.</returns>
        public IDataStoreTransaction BeginTransaction(string setName)
        {
            // Check if this set is in embedded mode (cached or detect from header)
            if (!_embeddedMode.TryGetValue(setName, out bool isEmbedded))
            {
                // Authoritative check: header ShardSize == 0 means embedded mode
                BinaryIndexFile idx = getIndexFile(setName);
                isEmbedded = idx.Header.ShardSize == 0;

                _embeddedMode[setName] = isEmbedded;
                if (isEmbedded && !_shardBoundary.ContainsKey(setName))
                {
                    string filePath = GetIndexFilePath(setName);
                    if (File.Exists(filePath) && detectEmbeddedMode(filePath))
                    {
                        (long indexSize, bool _) = readEmbeddedFooter(filePath);
                        long boundary = computeShardBoundary(filePath, indexSize);
                        _shardBoundary[setName] = boundary;
                    }
                    else
                    {
                        // Separate mode with shardSize=0: shard boundary is the shard file size
                        string shardPath = EmbeddedIndexCommitter.GetShardPath(filePath);
                        _shardBoundary[setName] = File.Exists(shardPath) ? new FileInfo(shardPath).Length : 0;
                    }
                }
            }

            if (isEmbedded)
            {
                return beginEmbeddedTransaction(setName);
            }

            BinaryIndexFile indexFile = getIndexFile(setName);

            // Load block index for deduplication during writes
            InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();

            // Get shard file manager so the transaction can flush it on commit
            ShardFileManager shardManager = getShardFileManager(setName);

            return new BinaryTransaction(setName, indexFile, blockIndex, shardManager);
        }

        /// <summary>
        /// Begins a transaction for an embedded-mode set.
        /// Extracts to separate mode, swaps the cached index/shard managers to point at the
        /// standalone files, and sets up a post-commit callback that re-embeds.
        /// </summary>
        private IDataStoreTransaction beginEmbeddedTransaction(string setName)
        {
            string setBasePath = GetIndexFilePath(setName);

            // Get or create the committer for this set
            EmbeddedIndexCommitter committer = _committers.GetOrAdd(setName, _ => new EmbeddedIndexCommitter(setBasePath));

            // Get the current shard boundary
            long currentBoundary = _shardBoundary.TryGetValue(setName, out long b) ? b : 0;

            // Step 1: Close the current index file and shard manager
            if (_indexFiles.TryRemove(setName, out BinaryIndexFile? oldIndexFile))
                oldIndexFile.Dispose();
            if (_shardFileManagers.TryRemove(setName, out ShardFileManager? oldShardManager))
                oldShardManager.Dispose();

            string indexPath;
            string shardPath;

            // Check if already in separate mode (shard file exists alongside index)
            string expectedShardPath = EmbeddedIndexCommitter.GetShardPath(setBasePath);
            bool alreadySeparate = File.Exists(setBasePath) && File.Exists(expectedShardPath) && !detectEmbeddedMode(setBasePath);

            if (alreadySeparate)
            {
                // Already in separate mode — no extraction needed
                indexPath = setBasePath;
                shardPath = expectedShardPath;
            }
            else
            {
                // Step 2: Extract to separate mode (index + shard files)
                (indexPath, shardPath) = committer.ExtractToSeparateMode(currentBoundary);
            }

            // Step 3: Open BinaryIndexFile on the standalone index (baseOffset=0)
            BinaryIndexFile separateIndexFile = BinaryIndexFile.Open(indexPath, baseOffset: 0);
            _indexFiles[setName] = separateIndexFile;

            // Step 4: Open ShardFileManager on the plain shard
            InfoRecord info = buildInfoRecord(separateIndexFile.Header);
            ShardFileManager separateShardManager = new ShardFileManager(
                Path.GetDirectoryName(shardPath) ?? _baseDirectory,
                setName,
                shardSize: 0,
                _sourceDirectory,
                info.BlockSize);
            // No shard boundary during writes — blocks are appended freely to the plain shard
            _shardFileManagers[setName] = separateShardManager;

            // Step 5: Load block index for deduplication
            InMemoryBlockIndex blockIndex = separateIndexFile.LoadBlockIndex();

            // Step 6: Create post-commit action that re-embeds
            Action postCommitAction = () =>
            {
                // After AtomicCommit on the standalone index file, re-embed
                reEmbedAfterCommit(setName, committer, indexPath, shardPath);
            };

            return new BinaryTransaction(setName, separateIndexFile, blockIndex, separateShardManager, postCommitAction);
        }

        /// <summary>
        /// Re-embeds the index into the shard file after a successful commit in embedded mode.
        /// Closes the separate-mode files, calls ReEmbed, then reopens as embedded.
        /// </summary>
        private void reEmbedAfterCommit(string setName, EmbeddedIndexCommitter committer, string indexPath, string shardPath)
        {
            try
            {
                // Step 1: Close the separate-mode index file and shard manager
                if (_indexFiles.TryRemove(setName, out BinaryIndexFile? sepIndexFile))
                    sepIndexFile.Dispose();
                if (_shardFileManagers.TryRemove(setName, out ShardFileManager? sepShardManager))
                    sepShardManager.Dispose();
                // Step 2: Re-embed (appends index+footer to shard, renames to embedded file)
                committer.ReEmbed(indexPath, shardPath);
            }
            catch
            {
                // Don't rethrow — the commit succeeded, the data is safe in separate mode
                // Re-embed will be retried on next write
                return;
            }

            // Step 3: Reopen as embedded file (read footer, compute Base_Offset)
            string embeddedPath = committer.EmbeddedPath;
            (long indexSize, bool _) = readEmbeddedFooter(embeddedPath);
            long boundary = computeShardBoundary(embeddedPath, indexSize);

            // Step 4: Update embedded mode caches
            _embeddedMode[setName] = true;
            _shardBoundary[setName] = boundary;

            // Step 5: Reopen BinaryIndexFile with the correct baseOffset
            BinaryIndexFile embeddedIndexFile = BinaryIndexFile.Open(embeddedPath, baseOffset: boundary);
            _indexFiles[setName] = embeddedIndexFile;

            // Step 6: Reopen ShardFileManager pointing to the embedded file for block reads
            ShardFileManager embeddedShardManager = new ShardFileManager(
                _baseDirectory, setName, shardSize: 0, _sourceDirectory,
                embeddedIndexFile.Header.BlockSize);
            if (boundary > 0)
                embeddedShardManager.SetShardBoundary(boundary);
            _shardFileManagers[setName] = embeddedShardManager;

            // Step 7: Clear caches (directory/sections may have changed)
            _infoCache.TryRemove(setName, out _);
            invalidateSetCaches(setName);
        }

        /// <summary>
        /// Registers a writer for the specified set. Only one writer may be registered at a time per set.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <exception cref="InvalidOperationException">Thrown if a writer is already registered for this set.</exception>
        public void RegisterWriter(string setName)
        {
            SetUsageInfo info = _setUsage.GetOrAdd(setName, _ => new SetUsageInfo());
            lock (info.Sync)
            {
                if (info.WriterCount >= 1)
                    throw new InvalidOperationException($"A writer is already registered for set '{setName}'. Only one writer allowed.");

                info.WriterCount++;
            }
        }

        /// <summary>
        /// Unregisters a previously registered writer for the specified set.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        public void UnregisterWriter(string setName)
        {
            if (!_setUsage.TryGetValue(setName, out SetUsageInfo? info))
                return;

            lock (info.Sync)
            {
                info.WriterCount = Math.Max(0, info.WriterCount - 1);
            }
        }

        /// <summary>
        /// Registers a reader for the specified set.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        public void RegisterReader(string setName)
        {
            SetUsageInfo info = _setUsage.GetOrAdd(setName, _ => new SetUsageInfo());
            lock (info.Sync)
            {
                info.ReaderCount++;
            }
        }

        /// <summary>
        /// Unregisters a previously registered reader for the specified set.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        public void UnregisterReader(string setName)
        {
            if (!_setUsage.TryGetValue(setName, out SetUsageInfo? info))
                return;

            lock (info.Sync)
            {
                info.ReaderCount = Math.Max(0, info.ReaderCount - 1);
            }
        }

        #endregion

        #region Statistics

        public DataStoreStatistics GetSetStatistics(string setName, bool includePerImageStats, Action<int, int>? progress = null, CancellationToken cancellationToken = default)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();
            InfoRecord info = GetSetInfo(setName);
            int blockSize = info.BlockSize;

            // Get non-removed images for statistics
            List<ImageDirectoryEntry> images = directory.GetNonRemovedEntries().ToList();
            int imageCount = images.Count;
            long totalImageDataSize = images.Sum(e => e.Size);

            // Block size lookup built from per-image BlockLocations (avoids loading the global Block_Index)
            Dictionary<BlockKey, int> blockSizeLookup = new Dictionary<BlockKey, int>();

            // Compute total block references by scanning each image's BlockMap section
            long totalBlockReferences = 0;
            List<ImageStatistics>? imageDetails = includePerImageStats ? new List<ImageStatistics>() : null;

            // For per-image stats: track how many images reference each block (cross-image ref count)
            Dictionary<BlockKey, int>? crossImageRefCount = null;
            Dictionary<long, HashSet<BlockKey>>? imageBlockSets = null;

            if (includePerImageStats)
            {
                crossImageRefCount = new Dictionary<BlockKey, int>();
                imageBlockSets = new Dictionary<long, HashSet<BlockKey>>();
            }

            // Single pass: scan all images to count block references and collect block sizes
            int processed = 0;
            List<(long ImageId, string Name, long Size, long BlockRefs)> imageData = new();

            foreach (ImageDirectoryEntry entry in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long imageBlockRefs = 0;
                HashSet<BlockKey>? uniqueKeys = includePerImageStats ? new HashSet<BlockKey>() : null;

                try
                {
                    (List<OffsetRecord>? offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(entry.ImageId);

                    // Collect block sizes from this image's BlockLocations
                    foreach ((BlockKey key, (int _, long _, int size)) in blockLocations)
                    {
                        blockSizeLookup.TryAdd(key, size);
                    }

                    foreach (OffsetRecord offset in offsets)
                    {
                        if (offset.Blocks != null)
                        {
                            imageBlockRefs += offset.Blocks.Count;
                            if (uniqueKeys != null)
                            {
                                foreach (BlockKey key in offset.Blocks)
                                    uniqueKeys.Add(key);
                            }
                        }
                    }
                }
                catch
                {
                    // Skip corrupted image sections, log warning, continue with remaining
                    processed++;
                    progress?.Invoke(processed, imageCount);
                    continue;
                }

                totalBlockReferences += imageBlockRefs;
                imageData.Add((entry.ImageId, entry.Name, entry.Size, imageBlockRefs));

                if (includePerImageStats && uniqueKeys != null && crossImageRefCount != null && imageBlockSets != null)
                {
                    imageBlockSets[entry.ImageId] = uniqueKeys;
                    foreach (BlockKey key in uniqueKeys)
                    {
                        if (crossImageRefCount.ContainsKey(key))
                            crossImageRefCount[key]++;
                        else
                            crossImageRefCount[key] = 1;
                    }
                }

                processed++;
                progress?.Invoke(processed, imageCount);
            }

            // Compute block-level statistics from the collected block sizes
            long uniqueBlocksStored = blockSizeLookup.Count;
            long compressedBlocks = 0;
            long uncompressedBlocks = 0;
            long totalPhysicalStorage = 0;
            long totalCompressedSize = 0;
            long totalUncompressedSize = 0;

            foreach ((BlockKey _, int size) in blockSizeLookup)
            {
                totalPhysicalStorage += size;

                if (size < blockSize)
                {
                    compressedBlocks++;
                    totalCompressedSize += size;
                }
                else
                {
                    uncompressedBlocks++;
                    totalUncompressedSize += size;
                }
            }

            double avgCompressedSize = compressedBlocks > 0
                ? (double)totalCompressedSize / compressedBlocks
                : 0;
            double avgUncompressedSize = uncompressedBlocks > 0
                ? (double)totalUncompressedSize / uncompressedBlocks
                : 0;

            // Second pass: compute per-image statistics if requested
            if (includePerImageStats && imageBlockSets != null && crossImageRefCount != null)
            {
                foreach ((long imageId, string? imageName, long size, long blockRefs) in imageData)
                {
                    if (!imageBlockSets.TryGetValue(imageId, out HashSet<BlockKey>? uniqueKeys))
                        continue;

                    long uniqueBlocks = uniqueKeys.Count;
                    long storedSize = 0;
                    long sharedBlocks = 0;
                    long sharedBlocksSize = 0;
                    long apportionedStoredSize = 0;
                    long sharedBlocksPhysicalSize = 0;
                    long nonSharedCompressedSize = 0;
                    long nonSharedUncompressedSize = 0;
                    long sharedCompressedSize = 0;
                    long sharedUncompressedSize = 0;

                    foreach (BlockKey key in uniqueKeys)
                    {
                        int blockPhysicalSize = blockSizeLookup.TryGetValue(key, out int sz) ? sz : 0;
                        storedSize += blockPhysicalSize;

                        int refCount = crossImageRefCount.TryGetValue(key, out int rc) ? rc : 1;
                        apportionedStoredSize += refCount > 0 ? blockPhysicalSize / refCount : blockPhysicalSize;

                        if (refCount > 1)
                        {
                            sharedBlocks++;
                            sharedBlocksSize += blockPhysicalSize;
                            sharedBlocksPhysicalSize += blockPhysicalSize;
                            if (blockPhysicalSize < blockSize)
                                sharedCompressedSize += blockPhysicalSize;
                            else
                                sharedUncompressedSize += blockPhysicalSize;
                        }
                        else if (blockPhysicalSize < blockSize)
                        {
                            nonSharedCompressedSize += blockPhysicalSize;
                        }
                        else
                        {
                            nonSharedUncompressedSize += blockPhysicalSize;
                        }
                    }

                    imageDetails!.Add(new ImageStatistics
                    {
                        ImageId = imageId,
                        ImageName = imageName,
                        Size = size,
                        BlockReferences = blockRefs,
                        UniqueBlocks = uniqueBlocks,
                        StoredSize = storedSize,
                        SharedBlocks = sharedBlocks,
                        SharedBlocksSize = sharedBlocksSize,
                        ApportionedStoredSize = apportionedStoredSize,
                        SharedBlocksPhysicalSize = sharedBlocksPhysicalSize,
                        NonSharedCompressedSize = nonSharedCompressedSize,
                        NonSharedUncompressedSize = nonSharedUncompressedSize,
                        SharedCompressedSize = sharedCompressedSize,
                        SharedUncompressedSize = sharedUncompressedSize
                    });
                }
            }

            // Calculate database file sizes (index file + shard files)
            long totalDatabaseFileSize = 0;
            int databaseFileCount = 0;

            string indexFilePath = GetIndexFilePath(setName);
            if (File.Exists(indexFilePath))
            {
                totalDatabaseFileSize += new FileInfo(indexFilePath).Length;
                databaseFileCount++;
            }

            // Enumerate shard files: {setBaseName}_NNNN.nkds
            try
            {
                string setDirectory = Path.GetDirectoryName(indexFilePath) ?? _baseDirectory;
                string setBaseName = Path.GetFileNameWithoutExtension(setName);

                foreach (string shardFile in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds"))
                {
                    if (File.Exists(shardFile))
                    {
                        totalDatabaseFileSize += new FileInfo(shardFile).Length;
                        databaseFileCount++;
                    }
                }
            }
            catch { } // Ignore errors reading directory

            return new DataStoreStatistics
            {
                SetName = setName,
                ImageCount = imageCount,
                TotalImageDataSize = totalImageDataSize,
                UniqueBlocksStored = uniqueBlocksStored,
                TotalBlockReferences = totalBlockReferences,
                CompressedBlocks = compressedBlocks,
                UncompressedBlocks = uncompressedBlocks,
                TotalPhysicalBlockStorage = totalPhysicalStorage,
                AverageCompressedBlockSize = avgCompressedSize,
                AverageUncompressedBlockSize = avgUncompressedSize,
                BlockSize = blockSize,
                ImageDetails = imageDetails,
                TotalDatabaseFileSize = totalDatabaseFileSize,
                DatabaseFileCount = databaseFileCount
            };
        }

        #endregion

        #region Image and Data Operations

        public long InsertImage(string setName, IDataStoreTransaction transaction, string imageName, string? system, ImageFormat format)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();

            // Get next image ID from directory
            long imageId = directory.GetNextImageId();

            // Create ImageRecord with the new ID
            ImageRecord image = new ImageRecord
            {
                Id = imageId,
                Name = imageName,
                Size = 0,
                Crc32 = 0,
                XxHash64 = 0,
                SetName = setName,
                System = system,
                Format = format,
                RollbackFileId = null,
                RollbackOffset = null,
                Removed = false
            };

            // Set pending image on the transaction
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;
            binaryTransaction.SetPendingImage(image);

            return imageId;
        }

        public void UpdateImageMetadata(string setName, IDataStoreTransaction transaction, long imageId, long size, uint crc32, ulong xxhash64)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            // Update the pending image's metadata fields
            binaryTransaction.UpdatePendingImageMetadata(imageId, size, crc32, xxhash64);

            // Stamp rollback checkpoint: shard position after all data for this image has been written
            try
            {
                ShardFileManager shardManager = getShardFileManager(setName);
                int rollbackFileId = shardManager.CurrentFileId;
                long rollbackOffset = shardManager.CurrentShardSize;
                binaryTransaction.UpdatePendingImageRollback(imageId, rollbackFileId, rollbackOffset);
            }
            catch
            {
                // Best-effort — rollback info is optional
            }
        }

        public void UpdateImageName(string setName, IDataStoreTransaction transaction, long imageId, string imageName)
        {
            applyDirectoryMutation(setName, directory =>
            {
                ImageDirectoryEntry? entry = directory.GetEntry(imageId);
                if (entry == null)
                    throw new KeyNotFoundException($"Image ID {imageId} not found in set '{setName}'.");

                // Update only the name field
                entry.Name = imageName;
            });
        }

        public void UpdateImageFormat(string setName, IDataStoreTransaction transaction, long imageId, ImageFormat format)
        {
            applyDirectoryMutation(setName, directory =>
            {
                ImageDirectoryEntry? entry = directory.GetEntry(imageId);
                if (entry == null)
                    throw new KeyNotFoundException($"Image ID {imageId} not found in set '{setName}'.");

                entry.Format = format;
            });
        }

        public ImageRecord? GetImage(string setName, long imageId)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();
            ImageDirectoryEntry? entry = directory.GetEntry(imageId);
            if (entry == null)
                return null;

            return mapEntryToImageRecord(setName, entry);
        }

        public IEnumerable<ImageRecord> GetAllImagesInSet(string setName)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();
            return directory.GetNonRemovedEntries().Select(e => mapEntryToImageRecord(setName, e)).ToList();
        }

        public IEnumerable<ImageRecord> GetAllImagesInSetIncludingRemoved(string setName)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();
            return directory.GetAllEntries().Select(e => mapEntryToImageRecord(setName, e)).ToList();
        }

        public void Rollback(string setName, long imageId, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            // Determine if this set is in embedded mode using the authoritative header check.
            // This mirrors the pattern used in CompactSet and BeginTransaction.
            // The header's ShardSize field is the definitive source — the _embeddedMode cache may be stale.
            string setBasePath = GetIndexFilePath(setName);
            string expectedShardPath = EmbeddedIndexCommitter.GetShardPath(setBasePath);

            progress?.Report((0, "Detecting mode"));

            BinaryIndexFile idx = getIndexFile(setName);
            bool isEmbedded = idx.Header.ShardSize == 0;

            // Correct the _embeddedMode cache if it disagrees with the authoritative header value.
            if (_embeddedMode.TryGetValue(setName, out bool cachedMode) && cachedMode != isEmbedded)
            {
                _embeddedMode[setName] = isEmbedded;
            }
            else if (!_embeddedMode.ContainsKey(setName))
            {
                _embeddedMode[setName] = isEmbedded;
            }

            // For embedded-mode sets, also check if already in separated state
            // (from a previous interrupted operation). This only applies to genuinely
            // embedded sets (ShardSize == 0) that were extracted but not re-embedded.
            if (isEmbedded)
            {
                bool alreadySeparate = File.Exists(setBasePath) && File.Exists(expectedShardPath) && !detectEmbeddedMode(setBasePath);
                // alreadySeparate just means the embedded set is in an intermediate state;
                // rollbackEmbedded handles this case correctly.
            }

            if (isEmbedded)
                rollbackEmbedded(setName, imageId, progress);
            else
                rollbackSeparate(setName, imageId, progress);
        }

        /// <summary>
        /// Performs a physical rollback on a separate-mode set.
        /// Truncates shards, removes directory entries, and prunes the block index.
        /// </summary>
        private void rollbackSeparate(string setName, long imageId, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            progress?.Report((10, "Opening index"));
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();

            // Verify the target image exists in the directory
            ImageDirectoryEntry? targetEntry = directory.GetEntry(imageId);
            if (targetEntry == null)
                throw new ArgumentException($"Image with ID {imageId} not found in set '{setName}'.");

            int? rollbackFileId = targetEntry.RollbackFileId;
            long? rollbackOffset = targetEntry.RollbackOffset;

            if (rollbackFileId == null || rollbackOffset == null)
                throw new InvalidOperationException(
                    $"Image {imageId} in set '{setName}' does not have rollback checkpoint data. " +
                    "Cannot perform physical rollback without shard position information.");

            // Step 1: Physically truncate shards.
            progress?.Report((30, "Truncating shards"));
            ShardFileManager shardManager = getShardFileManager(setName);
            shardManager.RollbackToSize(rollbackFileId.Value, rollbackOffset.Value);

            // Step 2: Remove all directory entries with ImageId > target.
            progress?.Report((50, "Removing directory entries"));
            List<long> entriesToRemove = directory.GetAllEntries()
                .Where(e => e.ImageId > imageId)
                .Select(e => e.ImageId)
                .ToList();

            foreach (long id in entriesToRemove)
                directory.RemoveEntry(id);

            // Step 3: Prune the block index — remove entries pointing into truncated region.
            progress?.Report((60, "Pruning block index"));
            InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();
            BlockIndexEntry[] allEntries = blockIndex.GetEntries();

            BlockIndexEntry[] survivingEntries = allEntries
                .Where(e =>
                {
                    if (e.FileId > rollbackFileId.Value)
                        return false;
                    if (e.FileId == rollbackFileId.Value && e.Offset >= rollbackOffset.Value)
                        return false;
                    return true;
                })
                .ToArray();

            Array.Sort(survivingEntries);
            indexFile.ReplaceBlockIndex(survivingEntries);

            // Step 4: Update directory and atomic commit.
            progress?.Report((80, "Committing"));
            indexFile.UpdateDirectory(directory);
            indexFile.AtomicCommit();

            // Step 5: Compact to reclaim any interleaved dead blocks in the shard.
            progress?.Report((90, "Compacting"));
            // Close the ShardFileManager before compacting (releases file handles so compactSetSeparate can access shard files)
            if (_shardFileManagers.TryRemove(setName, out ShardFileManager? sfm))
                sfm.Dispose();
            compactSetSeparate(setName, progress);

            // Step 6: Invalidate caches.
            progress?.Report((100, "Complete"));
            invalidateSetCaches(setName);
        }

        /// <summary>
        /// Performs a physical rollback on an embedded-mode set.
        /// Follows the same extract → update → re-embed pattern as write operations:
        /// 1. Extract index from embedded file to separate mode
        /// 2. Update the index (remove images, prune block index)
        /// 3. Truncate the shard file to the rollback offset
        /// 4. Re-embed the index back into the shard
        /// </summary>
        private void rollbackEmbedded(string setName, long imageId, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            string setBasePath = GetIndexFilePath(setName);
            EmbeddedIndexCommitter committer = _committers.GetOrAdd(setName, _ => new EmbeddedIndexCommitter(setBasePath));

            // Get the current shard boundary
            if (!_shardBoundary.TryGetValue(setName, out long shardBoundary))
            {
                (long indexSize, bool _) = readEmbeddedFooter(setBasePath);
                shardBoundary = computeShardBoundary(setBasePath, indexSize);
                _shardBoundary[setName] = shardBoundary;
            }

            // Close the current embedded index file and shard manager
            if (_indexFiles.TryRemove(setName, out BinaryIndexFile? embeddedIndex))
                embeddedIndex.Dispose();
            if (_shardFileManagers.TryRemove(setName, out ShardFileManager? embeddedShardMgr))
                embeddedShardMgr.Dispose();

            // Step 1: Extract to separate mode (same as write operations)
            progress?.Report((5, "Extracting index"));
            string expectedShardPath = EmbeddedIndexCommitter.GetShardPath(setBasePath);
            bool alreadySeparate = File.Exists(setBasePath) && File.Exists(expectedShardPath) && !detectEmbeddedMode(setBasePath);

            string indexPath, shardPath;
            if (alreadySeparate)
            {
                indexPath = setBasePath;
                shardPath = expectedShardPath;
            }
            else
            {
                BinaryIndexFile tempIdx = BinaryIndexFile.Open(setBasePath, baseOffset: shardBoundary);
                long actualIndexSize = tempIdx.Header.FileEndOffset;
                tempIdx.Dispose();
                (indexPath, shardPath) = committer.ExtractToSeparateMode(shardBoundary, actualIndexSize);
            }

            // Step 2: Open the standalone index and find the rollback target
            progress?.Report((20, "Opening index"));
            BinaryIndexFile standaloneIndex = BinaryIndexFile.Open(indexPath);
            ImageDirectory directory = standaloneIndex.GetDirectory();

            ImageDirectoryEntry? targetEntry = directory.GetEntry(imageId);
            if (targetEntry == null)
            {
                standaloneIndex.Dispose();
                throw new ArgumentException($"Image with ID {imageId} not found in set '{setName}'.");
            }

            int? rollbackFileId = targetEntry.RollbackFileId;
            long? rollbackOffset = targetEntry.RollbackOffset;

            if (rollbackFileId == null || rollbackOffset == null)
            {
                standaloneIndex.Dispose();
                throw new InvalidOperationException(
                    $"Image {imageId} in set '{setName}' does not have rollback checkpoint data.");
            }

            // Step 2a: Remove directory entries for images after the target
            progress?.Report((30, "Removing directory entries"));
            List<long> entriesToRemove = directory.GetAllEntries()
                .Where(e => e.ImageId > imageId)
                .Select(e => e.ImageId)
                .ToList();

            foreach (long id in entriesToRemove)
                directory.RemoveEntry(id);

            // Step 2b: Prune block index — remove entries pointing beyond the rollback offset
            progress?.Report((40, "Pruning block index"));
            InMemoryBlockIndex blockIndex = standaloneIndex.LoadBlockIndex();
            BlockIndexEntry[] allEntries = blockIndex.GetEntries();

            BlockIndexEntry[] survivingEntries = allEntries
                .Where(e =>
                {
                    if (e.FileId > rollbackFileId.Value)
                        return false;
                    if (e.FileId == rollbackFileId.Value && e.Offset >= rollbackOffset.Value)
                        return false;
                    return true;
                })
                .ToArray();

            Array.Sort(survivingEntries);
            standaloneIndex.ReplaceBlockIndex(survivingEntries);

            // Step 2c: Commit the index changes
            progress?.Report((50, "Committing index"));
            standaloneIndex.UpdateDirectory(directory);
            standaloneIndex.AtomicCommit();

            // Step 2d: Compact the index to remove dead space (ReplaceBlockIndex appends,
            // leaving old data in the file). This ensures the re-embedded file shrinks.
            progress?.Report((60, "Compacting index"));
            string compactTmpPath = indexPath + ".compact.tmp";
            standaloneIndex.CompactTo(compactTmpPath, deterministic: false, progress: null);
            standaloneIndex.Dispose();
            File.Delete(indexPath);
            File.Move(compactTmpPath, indexPath);

            // Step 3: Truncate the shard file directly to the rollback offset
            progress?.Report((75, "Truncating shard"));
            long shardSizeBefore = new FileInfo(shardPath).Length;
            if (shardSizeBefore > rollbackOffset.Value)
            {
                using (FileStream shardStream = new FileStream(shardPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    shardStream.SetLength(rollbackOffset.Value);
                    shardStream.Flush(flushToDisk: true);
                }
            }

            // Step 4: Re-embed (appends index+footer to shard, renames to embedded file)
            progress?.Report((85, "Re-embedding"));
            committer.ReEmbed(indexPath, shardPath);

            // Reopen as embedded file
            (long newIndexSize, bool _) = readEmbeddedFooter(setBasePath);
            long newBoundary = computeShardBoundary(setBasePath, newIndexSize);
            _shardBoundary[setName] = newBoundary;
            _embeddedMode[setName] = true;

            BinaryIndexFile reopened = BinaryIndexFile.Open(setBasePath, baseOffset: newBoundary);
            _indexFiles[setName] = reopened;

            // Recreate shard manager for embedded mode
            InfoRecord reopenedInfo = buildInfoRecord(reopened.Header);
            _shardFileManagers[setName] = new ShardFileManager(_baseDirectory, setName, reopenedInfo.ShardSize, _sourceDirectory, reopenedInfo.BlockSize);

            // Step 5: Compact to reclaim any interleaved dead blocks in the shard
            progress?.Report((90, "Compacting"));
            compactSetEmbedded(setName, progress);

            progress?.Report((100, "Complete"));
            invalidateSetCaches(setName);
        }

        public bool ImageExists(string setName, string imageName, uint crc32, ulong xxhash64)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            ImageDirectory directory = indexFile.GetDirectory();
            return directory.GetNonRemovedEntries()
                .Any(e => e.Name == imageName && e.Crc32 == crc32 && e.XxHash64 == xxhash64);
        }

        public long InsertArea(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, uint crc32, ulong xxhash64, AreaMetadata? metadata = null, int? strideBlockSize = null, int? strideDataOffset = null, int? strideDataLength = null, int? sectionSize = null)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            AreaRecord area = new AreaRecord
            {
                ImageId = imageId,
                Offset = offset,
                Size = size,
                Crc32 = crc32,
                XxHash64 = xxhash64,
                Metadata = metadata ?? new AreaMetadata(),
                StrideBlockSize = strideBlockSize ?? 0,
                StrideDataOffset = strideDataOffset ?? 0,
                StrideDataLength = strideDataLength ?? 0,
                SectionSize = sectionSize ?? 0
            };

            binaryTransaction.AddPendingArea(area);

            // Return a synthetic area ID (count of pending areas)
            return binaryTransaction.PendingAreaCount;
        }

        public void UpdateAreaMetadata(string setName, IDataStoreTransaction transaction, long areaId, AreaMetadata metadata)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;
            binaryTransaction.UpdatePendingAreaMetadata(areaId, metadata);
        }

        public IEnumerable<AreaRecord> GetAreasForImage(string setName, long imageId)
        {
            ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)> setCache = _metadataCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>());
            if (setCache.TryGetValue(imageId, out (List<AreaRecord> Areas, List<FileRecord> Files) cached))
                return cached.Areas;

            BinaryIndexFile indexFile = getIndexFile(setName);
            (List<AreaRecord>? areas, List<FileRecord>? files) = indexFile.ReadImageMetadata(imageId);

            // Bound the cache: clear if it exceeds 100 entries
            if (setCache.Count >= 100)
                setCache.Clear();

            setCache[imageId] = (areas, files);
            return areas;
        }

        public void DeleteImage(string setName, long imageId)
        {
            applyDirectoryMutation(setName, directory => directory.MarkRemoved(imageId, true));

            // Invalidate caches for this image
            invalidateImageCaches(setName, imageId);
        }

        public void RestoreImage(string setName, long imageId)
        {
            applyDirectoryMutation(setName, directory => directory.MarkRemoved(imageId, false));

            // Invalidate caches for this image
            invalidateImageCaches(setName, imageId);
        }

        /// <summary>
        /// Applies a directory-only mutation (mark removed/restore, rename, format change) and
        /// commits it durably.
        ///
        /// For separate-mode sets the mutation is applied in place. For embedded-mode sets the
        /// mutation MUST NOT be applied in place: the embedded file ends with a 12-byte footer,
        /// and appending a new directory + rewriting the headers over an embedded file both
        /// over-counts <c>FileEndOffset</c> by the footer size and leaves the file no longer
        /// ending in the footer magic — which makes the set fail to reopen ("both primary and
        /// secondary headers failed validation"). Instead we follow the documented
        /// extract → mutate → re-embed cycle used by <see cref="compactSetEmbedded"/>, so the
        /// embedded file is only ever produced by a rename of a fully-formed shard.
        /// </summary>
        private void applyDirectoryMutation(string setName, Action<ImageDirectory> mutate)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);

            // Authoritative embedded check via the header (the _embeddedMode cache may be stale).
            bool isEmbedded = indexFile.Header.ShardSize == 0;

            if (!isEmbedded)
            {
                // Separate mode — safe to mutate in place.
                ImageDirectory directory = indexFile.GetDirectory();
                mutate(directory);
                indexFile.UpdateDirectory(directory);
                indexFile.AtomicCommit();
                return;
            }

            // Embedded mode — extract, mutate the standalone index, re-embed.
            string setBasePath = GetIndexFilePath(setName);
            EmbeddedIndexCommitter committer = _committers.GetOrAdd(setName, _ => new EmbeddedIndexCommitter(setBasePath));

            string expectedShardPath = EmbeddedIndexCommitter.GetShardPath(setBasePath);
            bool alreadySeparated = File.Exists(setBasePath) && File.Exists(expectedShardPath) && !detectEmbeddedMode(setBasePath);

            string indexPath;
            string shardPath;

            if (alreadySeparated)
            {
                indexFile.Dispose();
                _indexFiles.TryRemove(setName, out _);
                indexPath = setBasePath;
                shardPath = expectedShardPath;
            }
            else
            {
                if (!_shardBoundary.TryGetValue(setName, out long shardBoundary))
                {
                    (long fIndexSize, bool _) = readEmbeddedFooter(setBasePath);
                    shardBoundary = computeShardBoundary(setBasePath, fIndexSize);
                    _shardBoundary[setName] = shardBoundary;
                }

                // Use the header's FileEndOffset as the authoritative index size (the embedded
                // footer may be stale). Note: for a correctly committed embedded index this
                // equals the index region length excluding the footer.
                long actualIndexSize = indexFile.Header.FileEndOffset;
                indexFile.Dispose();
                _indexFiles.TryRemove(setName, out _);
                (indexPath, shardPath) = committer.ExtractToSeparateMode(shardBoundary, actualIndexSize);
            }

            // Mutate + commit on the standalone index (baseOffset=0 → FileEndOffset is correct).
            BinaryIndexFile standaloneIndex = BinaryIndexFile.Open(indexPath);
            try
            {
                ImageDirectory directory = standaloneIndex.GetDirectory();
                mutate(directory);
                standaloneIndex.UpdateDirectory(directory);
                standaloneIndex.AtomicCommit();
            }
            finally
            {
                standaloneIndex.Dispose();
            }

            // Re-embed: append index + footer to the shard, rename to the embedded file.
            committer.ReEmbed(indexPath, shardPath);

            // Reopen as embedded and refresh caches.
            (long newIndexSize, bool _) = readEmbeddedFooter(setBasePath);
            long newBoundary = computeShardBoundary(setBasePath, newIndexSize);
            BinaryIndexFile reopened = BinaryIndexFile.Open(setBasePath, baseOffset: newBoundary);
            _indexFiles[setName] = reopened;
            _shardBoundary[setName] = newBoundary;
            _embeddedMode[setName] = true;

            if (_shardFileManagers.TryGetValue(setName, out ShardFileManager? sfm) && newBoundary > 0)
                sfm.SetShardBoundary(newBoundary);

            _infoCache.TryRemove(setName, out _);
            invalidateSetCaches(setName);
        }

        public void CompactSet(string setName, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            // Ensure the set is loaded so embedded mode is detected
            BinaryIndexFile idx = getIndexFile(setName);

            // Close the ShardFileManager if it exists (releases file handles for embedded mode)
            if (_shardFileManagers.TryRemove(setName, out ShardFileManager? sfm))
                sfm.Dispose();

            // Authoritative check: use the header's ShardSize field to determine the true mode.
            // This mirrors the pattern used in BeginTransaction (idx.Header.ShardSize == 0).
            // The header is the definitive source — the _embeddedMode cache may be stale.
            bool isEmbedded = idx.Header.ShardSize == 0;

            // Correct the _embeddedMode cache if it disagrees with the authoritative header value.
            // This prevents future calls from hitting the same stale value.
            if (_embeddedMode.TryGetValue(setName, out bool cachedMode) && cachedMode != isEmbedded)
            {
                _embeddedMode[setName] = isEmbedded;
            }
            else if (!_embeddedMode.ContainsKey(setName))
            {
                _embeddedMode[setName] = isEmbedded;
            }

            if (isEmbedded)
            {
                compactSetEmbedded(setName, progress);
                return;
            }

            compactSetSeparate(setName, progress);
        }

        /// <summary>
        /// Compacts a set in separate mode (shardSize > 0).
        /// Readers continue using the old file during compaction via their open file handles.
        /// </summary>
        private void compactSetSeparate(string setName, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            string originalPath = GetIndexFilePath(setName);
            string tempPath = originalPath + ".compact.tmp";

            try
            {
                // Step 1: Compact shard files — remove orphaned blocks/files and reclaim space.
                // This updates the block index and metadata sections in the current index file.
                compactShards(setName, indexFile, progress);

                // Step 2: Compact the index file to a temporary file.
                // This removes the directory entries for removed images, their sections,
                // and prunes the block index (which is already updated with new shard offsets).
                indexFile.CompactTo(tempPath, deterministic: false, progress: progress);

                // Step 2b: Validate the compacted output before replacing the original.
                // Open the temp file to verify its header passes validation, then dispose immediately.
                // If validation fails, the temp file is deleted and the original is preserved.
                try
                {
                    using (BinaryIndexFile validationHandle = BinaryIndexFile.Open(tempPath))
                    {
                        // Header validation passed — file is valid
                    }
                }
                catch
                {
                    // Compacted output is corrupt — delete temp file and preserve original
                    try { File.Delete(tempPath); } catch { }
                    throw;
                }

                // Step 3: Close/dispose the old index file so we can replace it.
                indexFile.Dispose();

                // Step 4: Replace old file with the compacted temp file (atomic where supported).
                AtomicFileOps.ReplaceFile(tempPath, originalPath,
                    warning => Trace.TraceWarning(warning));

                // Step 5: Reopen the compacted file and update the dictionary.
                BinaryIndexFile newIndexFile = BinaryIndexFile.Open(originalPath);
                _indexFiles[setName] = newIndexFile;

                // Clear the info cache so it's rebuilt from the new header
                _infoCache.TryRemove(setName, out _);

                // Clear per-set section caches (directory/sections have changed)
                invalidateSetCaches(setName);
            }
            catch
            {
                // If anything fails, try to clean up the index temp file.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                // Drop any cached handle for this set and clear the recovery guard so the next
                // access re-runs crash recovery against authoritative disk state. Recovery then:
                //   * If the failure happened BEFORE the index commit — deletes leftover shard
                //     temps; the un-compacted originals remain authoritative.
                //   * If the failure happened AFTER the index commit but before the deferred shard
                //     renames finished (Stage 10) — PROMOTES the leftover compacted shard temps.
                // Either way the set is left internally consistent and surviving images are intact.
                if (_indexFiles.TryRemove(setName, out BinaryIndexFile? failedHandle))
                {
                    try { failedHandle.Dispose(); } catch { }
                }

                // Clear the recovery guard so the NEXT access re-runs crash recovery against
                // authoritative disk state. We deliberately do NOT recover here: at this moment
                // the files that failed the operation may still be locked/open by the failing
                // caller, and a half-completed promote could leave inconsistent state. Deferring
                // recovery to the next getIndexFile access (a clean entry point) is safe because
                // the committed index is the durable source of truth and recovery is idempotent.
                _recoveredSets.TryRemove(setName, out _);

                throw;
            }
        }

        /// <summary>
        /// Compacts a set in embedded mode (shardSize=0).
        /// Extracts to separate mode, compacts the standalone index, then re-embeds.
        /// Block data region [0, Shard_Boundary) remains byte-for-byte identical.
        /// Concurrent readers continue using their open file handles during the rename sequence.
        /// </summary>
        private void compactSetEmbedded(string setName, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);

            // Defensive guard: verify the header's ShardSize == 0 before proceeding.
            // This is a safety net in case routing logic is bypassed or called incorrectly.
            // If ShardSize > 0, this set should not be compacted via the embedded path.
            if (indexFile.Header.ShardSize > 0)
            {
                throw new InvalidOperationException(
                    $"compactSetEmbedded called on set '{setName}' with ShardSize={indexFile.Header.ShardSize}. " +
                    $"Only sets with ShardSize == 0 (embedded mode) should use this path.");
            }

            string setBasePath = GetIndexFilePath(setName);
            string? logPath = CompactDebugLogging ? Path.Combine(Path.GetTempPath(), "nkds-compact-debug.log") : null;

            EmbeddedIndexCommitter committer = new EmbeddedIndexCommitter(setBasePath);
            string indexPath;
            string shardPath;

            try
            {
                // Check if the file is already in separated mode (from a previous failed compact).
                // If so, skip extraction — the index and shard files already exist separately.
                string expectedShardPath = EmbeddedIndexCommitter.GetShardPath(setBasePath);
                bool alreadySeparated = File.Exists(setBasePath) && File.Exists(expectedShardPath) && !detectEmbeddedMode(setBasePath);

                if (alreadySeparated)
                {
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Set '{setName}' already in separated mode — skipping extraction\n");
                    indexFile.Dispose();
                    indexPath = setBasePath;
                    shardPath = expectedShardPath;
                }
                else
                {
                    if (!_shardBoundary.TryGetValue(setName, out long shardBoundary))
                        throw new InvalidOperationException($"Set '{setName}' is marked as embedded but has no recorded shard boundary.");

                    // Step 1: Extract from embedded mode to separate mode.
                    // Use the header's FileEndOffset (relative to baseOffset) as the true index size,
                    // since the embedded footer may be stale after Remove/Restore operations that
                    // grew the index without updating the footer.
                    long actualIndexSize = indexFile.Header.FileEndOffset;
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] START Set '{setName}': shardBoundary={shardBoundary}, actualIndexSize={actualIndexSize}, setBasePath='{setBasePath}'\n");
                    indexFile.Dispose();
                    (indexPath, shardPath) = committer.ExtractToSeparateMode(shardBoundary, actualIndexSize);
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Extracted: index={new FileInfo(indexPath).Length} bytes, shard={new FileInfo(shardPath).Length} bytes\n");
                }

                // Step 2: Open the standalone index file (baseOffset=0, it's a plain index now).
                BinaryIndexFile standaloneIndex = BinaryIndexFile.Open(indexPath);

                // Step 2b: Compact shard file — remove orphaned blocks/files and reclaim space.
                // This updates the block index and metadata sections in the standalone index.
                try
                {
                    compactShards(setName, standaloneIndex, progress);
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Shards compacted. Shard size: {new FileInfo(shardPath).Length} bytes\n");
                }
                catch (Exception shardEx)
                {
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] CompactShards FAILED: {shardEx}\n");
                    throw;
                }

                // Step 3: Compact the standalone index to a temporary file.
                string compactTempPath = indexPath + ".compact.tmp";
                try
                {
                    standaloneIndex.CompactTo(compactTempPath, deterministic: false, progress: progress);
                    standaloneIndex.Dispose();
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Compacted: {new FileInfo(compactTempPath).Length} bytes\n");

                    // Step 3b: Validate the compacted output before replacing the original.
                    // Open the temp file to verify its header passes validation, then dispose immediately.
                    // If validation fails, the temp file is deleted and the original is preserved.
                    try
                    {
                        using (BinaryIndexFile validationHandle = BinaryIndexFile.Open(compactTempPath))
                        {
                            // Header validation passed — file is valid
                        }
                    }
                    catch
                    {
                        // Compacted output is corrupt — delete temp file and preserve original
                        try { File.Delete(compactTempPath); } catch { }
                        throw;
                    }

                    // Step 4: Atomic rename — replace the standalone index with the compacted version.
                    AtomicFileOps.ReplaceFile(compactTempPath, indexPath, msg => Trace.TraceWarning(msg));
                    if (CompactDebugLogging)
                        File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Renamed to indexPath: {new FileInfo(indexPath).Length} bytes\n");
                }
                catch
                {
                    standaloneIndex.Dispose();
                    try { if (File.Exists(compactTempPath)) File.Delete(compactTempPath); } catch { }
                    throw;
                }

                // Step 5: Re-embed the compacted index into the shard.
                // Appends index + EmbeddedFooter to shard, then renames back to embedded file.
                if (CompactDebugLogging)
                    File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] Before ReEmbed: shard={new FileInfo(shardPath).Length} bytes, index={new FileInfo(indexPath).Length} bytes\n");
                committer.ReEmbed(indexPath, shardPath);
                if (CompactDebugLogging)
                    File.AppendAllText(logPath!, $"{DateTime.Now:O} [CompactEmbedded] After ReEmbed: embedded={new FileInfo(setBasePath).Length} bytes\n");

                // Step 6: Reopen as embedded file — read footer, compute new boundary, update caches.
                (long newIndexSize, bool _) = readEmbeddedFooter(setBasePath);
                long newBoundary = computeShardBoundary(setBasePath, newIndexSize);

                BinaryIndexFile newIndexFile = BinaryIndexFile.Open(setBasePath, baseOffset: newBoundary);
                _indexFiles[setName] = newIndexFile;
                _shardBoundary[setName] = newBoundary;

                // Update the ShardFileManager boundary if it exists
                if (_shardFileManagers.TryGetValue(setName, out ShardFileManager? sfm) && newBoundary > 0)
                    sfm.SetShardBoundary(newBoundary);

                // Clear the info cache so it's rebuilt from the new header
                _infoCache.TryRemove(setName, out _);

                // Clear per-set section caches (directory/sections have changed after compaction)
                invalidateSetCaches(setName);
            }
            catch
            {
                // Recovery: try to reopen whatever state the files are in.
                // The EmbeddedIndexCommitter's TryRecover can handle intermediate states on next open.
                try
                {
                    // Attempt recovery of the file state
                    recoverIfNeeded(setName);

                    string filePath = GetIndexFilePath(setName);
                    if (File.Exists(filePath) && detectEmbeddedMode(filePath))
                    {
                        (long indexSize, bool _) = readEmbeddedFooter(filePath);
                        long boundary = computeShardBoundary(filePath, indexSize);
                        BinaryIndexFile reopened = BinaryIndexFile.Open(filePath, baseOffset: boundary);
                        _indexFiles[setName] = reopened;
                        _shardBoundary[setName] = boundary;
                        _embeddedMode[setName] = true;
                    }
                    else if (File.Exists(filePath))
                    {
                        // Recovered to separate mode or file is a standalone index
                        BinaryIndexFile reopened = BinaryIndexFile.Open(filePath);
                        _indexFiles[setName] = reopened;
                    }
                }
                catch { }

                throw;
            }
        }

        /// <summary>
        /// Gathers all block keys referenced by live (non-removed) images.
        /// This is the first stage of the shard compaction pipeline: it determines which blocks
        /// are still in use and must be preserved during compaction.
        /// </summary>
        /// <param name="indexFile">The binary index file to read block maps from.</param>
        /// <param name="liveImages">The list of live (non-removed) image directory entries.</param>
        /// <returns>A HashSet containing all BlockKeys referenced by the live images.</returns>
        internal static HashSet<BlockKey> GatherReferencedKeys(BinaryIndexFile indexFile, List<ImageDirectoryEntry> liveImages)
        {
            HashSet<BlockKey> referencedBlockKeys = new HashSet<BlockKey>();
            foreach (ImageDirectoryEntry image in liveImages)
            {
                (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(image.ImageId);
                foreach (BlockKey key in blockLocations.Keys)
                    referencedBlockKeys.Add(key);
            }
            return referencedBlockKeys;
        }

        /// <summary>
        /// Classifies all data chunks (blocks and file records) as referenced or unreferenced.
        /// This is the second stage of the shard compaction pipeline: it builds the complete list
        /// of data chunks that exist in shard files and separates them into chunks that must be
        /// preserved (referenced by live images) and block indices that can be removed.
        /// </summary>
        /// <param name="allBlockEntries">All block index entries from the committed block index.</param>
        /// <param name="referencedKeys">The set of BlockKeys referenced by live images (from GatherReferencedKeys).</param>
        /// <param name="liveImages">The list of live (non-removed) image directory entries.</param>
        /// <param name="indexFile">The binary index file to read image metadata from.</param>
        /// <returns>A ChunkClassification containing referenced chunks and unreferenced block indices.</returns>
        internal static ChunkClassification BuildChunkList(BlockIndexEntry[] allBlockEntries, HashSet<BlockKey> referencedKeys, List<ImageDirectoryEntry> liveImages, BinaryIndexFile indexFile)
        {
            List<DataChunk> referencedChunks = new List<DataChunk>();
            List<int> unreferencedBlockIndices = new List<int>();

            // Classify each block entry as referenced or unreferenced
            for (int i = 0; i < allBlockEntries.Length; i++)
            {
                ref BlockIndexEntry entry = ref allBlockEntries[i];
                if (referencedKeys.Contains(entry.Key))
                {
                    referencedChunks.Add(new DataChunk(
                        IsBlock: true,
                        BlockKey: entry.Key,
                        BlockEntryIndex: i,
                        ImageId: 0,
                        FileName: "",
                        FileId: entry.FileId,
                        Offset: entry.Offset,
                        Size: entry.Size));
                }
                else
                {
                    unreferencedBlockIndices.Add(i);
                }
            }

            // Gather file records from live images (these are always referenced)
            foreach (ImageDirectoryEntry image in liveImages)
            {
                (List<AreaRecord> _, List<FileRecord>? files) = indexFile.ReadImageMetadata(image.ImageId);
                foreach (FileRecord file in files)
                {
                    referencedChunks.Add(new DataChunk(
                        IsBlock: false,
                        BlockKey: default,
                        BlockEntryIndex: -1,
                        ImageId: file.ImageId,
                        FileName: file.Name,
                        FileId: file.FileId,
                        Offset: file.Offset,
                        Size: file.Size));
                }
            }

            return new ChunkClassification
            {
                ReferencedChunks = referencedChunks,
                UnreferencedBlockIndices = unreferencedBlockIndices
            };
        }

        /// <summary>
        /// Updates block index entries with new offsets and removes unreferenced entries.
        /// This is a pipeline stage of shard compaction: after shards are compacted, the block index
        /// must reflect the new offsets and have unreferenced entries pruned.
        /// </summary>
        /// <param name="allBlockEntries">The current block index entries array.</param>
        /// <param name="blockOffsetUpdates">Dictionary mapping BlockKey to its new offset in the compacted shard.</param>
        /// <param name="unreferencedIndices">List of indices into allBlockEntries that should be removed (unreferenced blocks).</param>
        /// <returns>A new sorted array of BlockIndexEntry with updated offsets and unreferenced entries removed.</returns>
        internal static BlockIndexEntry[] UpdateBlockIndex(BlockIndexEntry[] allBlockEntries, Dictionary<BlockKey, long> blockOffsetUpdates, List<int> unreferencedIndices)
        {
            // Update offsets for entries whose keys are in the dictionary
            for (int i = 0; i < allBlockEntries.Length; i++)
            {
                if (blockOffsetUpdates.TryGetValue(allBlockEntries[i].Key, out long newOffset))
                {
                    allBlockEntries[i].Offset = newOffset;
                }
            }

            // Build a HashSet for O(1) lookup instead of O(n) List.Contains
            HashSet<int> unreferencedSet = new HashSet<int>(unreferencedIndices);

            // Remove unreferenced blocks from the index
            BlockIndexEntry[] liveEntries = allBlockEntries
                .Where((_, idx) => !unreferencedSet.Contains(idx))
                .ToArray();
            Array.Sort(liveEntries);
            return liveEntries;
        }

        /// <summary>
        /// Updates metadata sections (file records) for live images after shard compaction.
        /// For each live image, reads its metadata section, updates file record offsets based on
        /// the fileOffsetUpdates dictionary (keyed by (ImageId, FileName, FileId) tuple),
        /// and writes the updated metadata back to the index file.
        /// </summary>
        /// <param name="liveImages">The list of live (non-removed) image directory entries.</param>
        /// <param name="fileOffsetUpdates">Dictionary mapping (ImageId, FileName, FileId) tuples to their new offsets in the compacted shards.</param>
        /// <param name="indexFile">The binary index file to read/write image metadata from/to.</param>
        internal static void UpdateMetadataSections(List<ImageDirectoryEntry> liveImages, Dictionary<(long, string, int), long> fileOffsetUpdates, BinaryIndexFile indexFile)
        {
            if (fileOffsetUpdates.Count == 0)
                return;

            foreach (ImageDirectoryEntry image in liveImages)
            {
                (List<AreaRecord>? areas, List<FileRecord>? files) = indexFile.ReadImageMetadata(image.ImageId);
                bool modified = false;

                foreach (FileRecord file in files)
                {
                    if (fileOffsetUpdates.TryGetValue((file.ImageId, file.Name, file.FileId), out long newFileOffset))
                    {
                        file.Offset = newFileOffset;
                        modified = true;
                    }
                }

                if (modified)
                {
                    indexFile.UpdateImageMetadata(image.ImageId, areas, files);
                }
            }
        }

        /// <summary>
        /// Compacts shard files by removing orphaned blocks and file data, reclaiming disk space.
        /// Ported from the original SQLite ShardCompactor — uses copy-on-write per shard.
        /// For each affected shard: copies only live data to a temp file, then replaces the original.
        /// Updates the block index and metadata sections in the index file with corrected offsets.
        /// </summary>
        /// <param name="setName">The set name being compacted.</param>
        /// <param name="indexFile">The index file (will be updated with new offsets).</param>
        /// <param name="progress">Optional progress reporter.</param>
        private void compactShards(string setName, BinaryIndexFile indexFile, IProgress<(int Percentage, string Stage)>? progress)
        {
            ImageDirectory directory = indexFile.GetDirectory();
            List<ImageDirectoryEntry> liveImages = directory.GetNonRemovedEntries().ToList();

            if (liveImages.Count == 0 && directory.Count == 0)
                return; // Nothing to compact

            // Handle empty set case: all images removed but removed entries exist.
            // Delete all shard files and clear the block index so no orphaned data remains.
            if (liveImages.Count == 0)
            {
                string emptySetShardDir = Path.GetDirectoryName(GetIndexFilePath(setName)) ?? _baseDirectory;
                string emptySetBaseName = Path.GetFileName(setName);

                // Delete all shard files for this set
                foreach (string shardFile in Directory.EnumerateFiles(emptySetShardDir, $"{emptySetBaseName}_*.nkds"))
                {
                    if (File.Exists(shardFile))
                        File.Delete(shardFile);
                }

                // Clear the block index
                indexFile.ReplaceBlockIndex(Array.Empty<BlockIndexEntry>());

                // Commit changes atomically
                indexFile.AtomicCommit();
                return;
            }

            // --- Pipeline Stage 1: Gather referenced block keys from live images ---
            HashSet<BlockKey> referencedKeys = GatherReferencedKeys(indexFile, liveImages);

            // --- Pipeline Stage 2: Build chunk list and classify as referenced/unreferenced ---
            InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();
            BlockIndexEntry[] allBlockEntries = blockIndex.GetEntries();
            ChunkClassification classification = BuildChunkList(allBlockEntries, referencedKeys, liveImages, indexFile);

            // --- Early exit: if no unreferenced blocks and all chunks are already contiguous ---
            if (classification.UnreferencedBlockIndices.Count == 0 && AreChunksContiguous(classification.ReferencedChunks))
                return;

            // --- Pipeline Stage 3: Group referenced chunks by shard ---
            IGrouping<int, DataChunk>[] shardGroups = GroupChunksByShard(classification.ReferencedChunks);
            HashSet<int> processedShardIds = new HashSet<int>(shardGroups.Select(g => g.Key));

            // --- Pipeline Stage 4: Compact each shard ---
            string shardDirectory = Path.GetDirectoryName(GetIndexFilePath(setName)) ?? _baseDirectory;
            string setBaseName = Path.GetFileName(setName);

            Dictionary<BlockKey, long> blockOffsetUpdates = new Dictionary<BlockKey, long>();
            Dictionary<(long ImageId, string FileName, int FileId), long> fileOffsetUpdates = new Dictionary<(long ImageId, string FileName, int FileId), long>();

            // Pending shard promotions: temp files that have been fully written but NOT yet
            // renamed over their originals. Renames are deferred until AFTER the block index
            // is committed (Stage 9), so that a failure part-way through the loop leaves every
            // original shard intact and the (uncommitted) index still matching them. This is
            // the core of the crash-safety fix — physical shard layout only changes after the
            // durable index flip, and only via atomic renames.
            List<(string TempPath, string FinalPath)> pendingPromotions = new List<(string TempPath, string FinalPath)>();

            try
            {
                foreach (IGrouping<int, DataChunk> shardGroup in shardGroups)
                {
                    int fileId = shardGroup.Key;
                    string shardPath = Path.Combine(shardDirectory, $"{setBaseName}_{fileId:D4}.nkds");

                    progress?.Report((0, $"Compacting shard {fileId:D4}"));

                    // Write the compacted temp but DO NOT rename yet (promoteImmediately: false).
                    ShardCompactResult result = CompactShard(shardPath, shardGroup.ToList(), promoteImmediately: false);

                    if (result.WasCompacted && result.TempShardPath != null && result.ShardPath != null)
                        pendingPromotions.Add((result.TempShardPath, result.ShardPath));

                    // Merge offset updates from this shard into the combined dictionaries
                    foreach (KeyValuePair<BlockKey, long> kvp in result.BlockOffsetUpdates)
                        blockOffsetUpdates[kvp.Key] = kvp.Value;
                    foreach (KeyValuePair<(long ImageId, string FileName, int FileId), long> kvp in result.FileOffsetUpdates)
                        fileOffsetUpdates[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                // A shard failed to compact (e.g. a truncated/short shard, or an I/O error).
                // Nothing has been renamed and the index has not been committed, so the set is
                // still fully consistent. Discard any temp files we managed to write and abort.
                foreach ((string? tempPath, string _) in pendingPromotions)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
                throw;
            }

            // --- Pipeline Stage 5: Delete empty shards ---
            // NOTE: this physically deletes shards that have no live chunks. That is safe to do
            // before the index commit because those shards are, by definition, referenced by no
            // live image — losing them cannot corrupt a surviving image.
            HashSet<int> allShardFileIds = new HashSet<int>();
            foreach (BlockIndexEntry entry in allBlockEntries)
                allShardFileIds.Add(entry.FileId);
            foreach (DataChunk chunk in classification.ReferencedChunks)
            {
                if (!chunk.IsBlock)
                    allShardFileIds.Add(chunk.FileId);
            }

            DeleteEmptyShards(allShardFileIds, processedShardIds, shardDirectory, setBaseName);

            // --- Early exit: if no unreferenced blocks, no block offset updates, and no file offset updates ---
            if (classification.UnreferencedBlockIndices.Count == 0 && blockOffsetUpdates.Count == 0 && fileOffsetUpdates.Count == 0)
            {
                // Nothing changed in the index — discard any temps (there should be none, since
                // no offsets moved, but be defensive).
                foreach ((string? tempPath, string _) in pendingPromotions)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
                return;
            }

            // --- Pipeline Stage 6: Update block index with new offsets and remove unreferenced entries ---
            BlockIndexEntry[] updatedBlockEntries = UpdateBlockIndex(allBlockEntries, blockOffsetUpdates, classification.UnreferencedBlockIndices);
            indexFile.ReplaceBlockIndex(updatedBlockEntries);

            // --- Pipeline Stage 7: Update block maps for images with moved blocks ---
            UpdateBlockMaps(liveImages, blockOffsetUpdates, indexFile);

            // --- Pipeline Stage 8: Update metadata sections for images with moved file records ---
            UpdateMetadataSections(liveImages, fileOffsetUpdates, indexFile);

            // --- Pipeline Stage 9: Commit all changes atomically ---
            // This is the durable point of no return. Before this line the originals are still
            // authoritative; after it the committed index describes the compacted layout.
            indexFile.AtomicCommit();

            // --- Pipeline Stage 10: Promote compacted shards (deferred rename) ---
            // The index is now committed for the new layout. Rename each compacted temp over
            // its original. If the process is interrupted here, recovery detects that the
            // committed index expects the compacted (smaller) size and promotes any remaining
            // temps on next open (see recoverIfNeeded). Because each rename is atomic, a reader
            // always sees either the old or the new shard, never a partial file.
            foreach ((string? tempPath, string? finalPath) in pendingPromotions)
            {
                AtomicFileOps.ReplaceFile(tempPath, finalPath, warning => Trace.TraceWarning(warning));
            }
        }

        /// <summary>
        /// Determines whether all referenced chunks are already contiguous within their respective shards.
        /// Chunks are contiguous if, when sorted by offset within each shard, each chunk starts
        /// immediately after the previous one ends (no gaps between chunks).
        /// This is used as an early-exit optimization: if all chunks are contiguous and there are
        /// no unreferenced blocks, no compaction is needed.
        /// </summary>
        /// <param name="chunks">The list of referenced data chunks to check.</param>
        /// <returns>True if all chunks are contiguous within their shards; false if any gaps exist.</returns>
        internal static bool AreChunksContiguous(List<DataChunk> chunks)
        {
            if (chunks.Count == 0)
                return true;

            // Group by shard and check each shard independently
            foreach (IGrouping<int, DataChunk> group in chunks.GroupBy(c => c.FileId))
            {
                List<DataChunk> sorted = group.OrderBy(c => c.Offset).ToList();
                long expectedOffset = 0;
                foreach (DataChunk chunk in sorted)
                {
                    if (chunk.Offset != expectedOffset)
                        return false;
                    expectedOffset += chunk.Size;
                }
            }
            return true;
        }

        /// <summary>
        /// Groups a list of referenced data chunks by their shard file ID.
        /// Each group contains all chunks that reside in the same shard file,
        /// ordered by shard file ID for deterministic processing.
        /// </summary>
        /// <param name="chunks">The list of referenced data chunks to group.</param>
        /// <returns>An array of groupings, one per shard file ID, ordered by file ID.</returns>
        internal static IGrouping<int, DataChunk>[] GroupChunksByShard(List<DataChunk> chunks)
        {
            return chunks
                .GroupBy(c => c.FileId)
                .OrderBy(g => g.Key)
                .ToArray();
        }

        /// <summary>
        /// Compacts a single shard file by copying only the live chunks to a new file
        /// in contiguous order, then atomically replacing the original shard.
        /// Only performs compaction if there is actually dead data to remove (i.e., the
        /// total live chunk size differs from the actual file size, or chunks are not contiguous).
        /// </summary>
        /// <param name="shardPath">The full path to the shard file to compact.</param>
        /// <param name="liveChunks">The list of live DataChunks residing in this shard, in any order.</param>
        /// <returns>A ShardCompactResult with the FileId, offset mappings, and whether compaction occurred.</returns>
        /// <param name="promoteImmediately">
        /// When true (default, used by standalone/unit callers) the compacted temp file is
        /// atomically renamed over the original before returning. When false (used by the
        /// <see cref="compactShards"/> pipeline) the temp is written and flushed but left in
        /// place; the caller renames it ONLY after the block index has been durably committed.
        /// Deferring the rename is what makes multi-shard compaction crash-safe: if anything
        /// fails between shards, the originals are still authoritative and the committed index
        /// still matches them, so surviving images are never corrupted.
        /// </param>
        internal static ShardCompactResult CompactShard(string shardPath, List<DataChunk> liveChunks, bool promoteImmediately = true)
        {
            if (liveChunks.Count == 0)
            {
                // No live chunks — the shard is entirely dead data.
                // Return WasCompacted = false; the caller (DeleteEmptyShards) handles deletion.
                int emptyFileId = 0;
                // Try to parse the file ID from the shard path for the result
                string fileName = Path.GetFileNameWithoutExtension(shardPath);
                int lastUnderscore = fileName.LastIndexOf('_');
                if (lastUnderscore >= 0 && int.TryParse(fileName.Substring(lastUnderscore + 1), out int parsedId))
                    emptyFileId = parsedId;

                return new ShardCompactResult
                {
                    FileId = emptyFileId,
                    WasCompacted = false
                };
            }

            int fileId = liveChunks[0].FileId;

            // Sort chunks by their current offset for sequential reading
            List<DataChunk> sortedChunks = liveChunks.OrderBy(c => c.Offset).ToList();

            // Compute new contiguous offsets and determine if compaction is needed
            bool requiresCompaction = false;
            long currentOffset = 0;
            long[] newOffsets = new long[sortedChunks.Count];

            for (int i = 0; i < sortedChunks.Count; i++)
            {
                if (sortedChunks[i].Offset != currentOffset)
                    requiresCompaction = true;
                newOffsets[i] = currentOffset;
                currentOffset += sortedChunks[i].Size;
            }

            // Check if the file has trailing orphaned data beyond the live chunks
            if (File.Exists(shardPath))
            {
                long actualSize = new FileInfo(shardPath).Length;
                if (actualSize != currentOffset)
                    requiresCompaction = true;
            }
            else if (sortedChunks.Count > 0)
            {
                // Shard file missing — cannot compact, return no-op
                return new ShardCompactResult
                {
                    FileId = fileId,
                    WasCompacted = false
                };
            }

            if (!requiresCompaction)
            {
                return new ShardCompactResult
                {
                    FileId = fileId,
                    WasCompacted = false
                };
            }

            // Copy-on-Write: write the live data to a temp file. The temp is fully written
            // and flushed to disk here, but whether it is renamed over the original NOW or
            // LATER depends on promoteImmediately (see the parameter docs). The write itself
            // is wrapped so that a write/read failure never orphans a partial temp file.
            string tmpShardPath = shardPath + ".tmp";
            byte[] ioBuffer = new byte[1024 * 1024];
            bool written = false;

            try
            {
                using (FileStream oldStream = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
                using (FileStream newStream = new FileStream(tmpShardPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    for (int i = 0; i < sortedChunks.Count; i++)
                    {
                        DataChunk chunk = sortedChunks[i];
                        oldStream.Seek(chunk.Offset, SeekOrigin.Begin);
                        long remaining = chunk.Size;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(remaining, ioBuffer.Length);
                            int read = oldStream.Read(ioBuffer, 0, toRead);
                            if (read == 0)
                                throw new IOException($"Unexpected end of file while reading {shardPath}");
                            newStream.Write(ioBuffer, 0, read);
                            remaining -= read;
                        }
                    }
                    newStream.Flush(flushToDisk: true);
                }
                written = true;
            }
            finally
            {
                // If the temp was not fully written (a read/write failure mid-copy), delete the
                // partial temp so no stray ".tmp" shard is left behind.
                if (!written)
                {
                    try { if (File.Exists(tmpShardPath)) File.Delete(tmpShardPath); } catch { }
                }
            }

            // Build offset update dictionaries
            Dictionary<BlockKey, long> blockOffsetUpdates = new Dictionary<BlockKey, long>();
            Dictionary<(long ImageId, string FileName, int FileId), long> fileOffsetUpdates = new Dictionary<(long ImageId, string FileName, int FileId), long>();

            for (int i = 0; i < sortedChunks.Count; i++)
            {
                DataChunk chunk = sortedChunks[i];
                if (chunk.Offset == newOffsets[i])
                    continue; // No change

                if (chunk.IsBlock)
                    blockOffsetUpdates[chunk.BlockKey] = newOffsets[i];
                else
                    fileOffsetUpdates[(chunk.ImageId, chunk.FileName, chunk.FileId)] = newOffsets[i];
            }

            if (promoteImmediately)
            {
                // Standalone / unit contract: rename the temp over the original before returning.
                try
                {
                    AtomicFileOps.ReplaceFile(tmpShardPath, shardPath);
                }
                catch
                {
                    try { if (File.Exists(tmpShardPath)) File.Delete(tmpShardPath); } catch { }
                    throw;
                }

                return new ShardCompactResult
                {
                    FileId = fileId,
                    BlockOffsetUpdates = blockOffsetUpdates,
                    FileOffsetUpdates = fileOffsetUpdates,
                    WasCompacted = true
                };
            }

            // Deferred promotion: hand the temp path back to the pipeline. The original shard
            // is still intact and authoritative; the pipeline renames the temp into place only
            // after the block index has been durably committed.
            return new ShardCompactResult
            {
                FileId = fileId,
                BlockOffsetUpdates = blockOffsetUpdates,
                FileOffsetUpdates = fileOffsetUpdates,
                WasCompacted = true,
                ShardPath = shardPath,
                TempShardPath = tmpShardPath
            };
        }

        /// <summary>
        /// Deletes shard files that are completely empty (all their data was unreferenced).
        /// A shard is considered empty if its file ID appears in <paramref name="allShardFileIds"/>
        /// but not in <paramref name="processedShardIds"/> (i.e., no live chunks were written to it).
        /// </summary>
        /// <param name="allShardFileIds">The set of all shard file IDs referenced in the block index and file records.</param>
        /// <param name="processedShardIds">The set of shard file IDs that had live chunks during compaction.</param>
        /// <param name="shardDirectory">The directory containing the shard files.</param>
        /// <param name="setBaseName">The base name of the set (used to construct shard file paths).</param>
        internal static void DeleteEmptyShards(HashSet<int> allShardFileIds, HashSet<int> processedShardIds, string shardDirectory, string setBaseName)
        {
            foreach (int fileId in allShardFileIds)
            {
                if (processedShardIds.Contains(fileId))
                    continue;
                string shardPath = Path.Combine(shardDirectory, $"{setBaseName}_{fileId:D4}.nkds");
                if (File.Exists(shardPath))
                    File.Delete(shardPath);
            }
        }

        /// <summary>
        /// Updates block map sections for live images whose blocks have been relocated during shard compaction.
        /// For each live image, reads its block map, applies the offset updates from <paramref name="blockOffsetUpdates"/>,
        /// and writes the updated block map back to the index file.
        /// </summary>
        /// <param name="liveImages">The list of live (non-removed) image directory entries.</param>
        /// <param name="blockOffsetUpdates">Dictionary mapping BlockKey to its new offset in the compacted shard.</param>
        /// <param name="indexFile">The binary index file to read/write block maps from/to.</param>
        internal static void UpdateBlockMaps(List<ImageDirectoryEntry> liveImages, Dictionary<BlockKey, long> blockOffsetUpdates, BinaryIndexFile indexFile)
        {
            if (blockOffsetUpdates.Count == 0)
                return;

            foreach (ImageDirectoryEntry image in liveImages)
            {
                (List<OffsetRecord>? offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(image.ImageId);
                bool modified = false;

                // Update block locations with new offsets
                Dictionary<BlockKey, (int FileId, long Offset, int Size)> updatedLocations = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>(blockLocations.Count);
                foreach (KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> kvp in blockLocations)
                {
                    if (blockOffsetUpdates.TryGetValue(kvp.Key, out long newBlockOffset))
                    {
                        updatedLocations[kvp.Key] = (kvp.Value.FileId, newBlockOffset, kvp.Value.Size);
                        modified = true;
                    }
                    else
                    {
                        updatedLocations[kvp.Key] = kvp.Value;
                    }
                }

                if (modified)
                {
                    indexFile.UpdateImageBlockMap(image.ImageId, offsets, updatedLocations);
                }
            }
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from the stream into the buffer.
        /// </summary>
        private static void readExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    throw new EndOfStreamException($"Unexpected end of stream. Expected {count} bytes but only read {totalRead}.");
                totalRead += read;
            }
        }

        public void InsertOffset(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, BlockType type, BlockKey? blockKey, uint crc32, ulong xxhash64, long? offsetStart = null)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            long effectiveOffsetStart = offsetStart ?? offset;
            List<BlockKey>? blocks = blockKey.HasValue ? new List<BlockKey> { blockKey.Value } : null;

            OffsetRecord offsetRecord = new OffsetRecord
            {
                ImageId = imageId,
                Offset = offset,
                Size = size,
                Type = type,
                OffsetStart = effectiveOffsetStart,
                Blocks = blocks
            };

            binaryTransaction.AddPendingOffset(offsetRecord);
        }

        public void InsertOffset(string setName, IDataStoreTransaction transaction, long imageId, long offset, long size, BlockType type, List<BlockKey> blockKeys, long? offsetStart = null)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            long effectiveOffsetStart = offsetStart ?? offset;

            OffsetRecord offsetRecord = new OffsetRecord
            {
                ImageId = imageId,
                Offset = offset,
                Size = size,
                Type = type,
                OffsetStart = effectiveOffsetStart,
                Blocks = (blockKeys != null && blockKeys.Count > 0) ? blockKeys : null
            };

            binaryTransaction.AddPendingOffset(offsetRecord);
        }

        public IEnumerable<OffsetRecord> GetOffsetsForImage(string setName, long imageId)
        {
            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());
            if (setCache.TryGetValue(imageId, out (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) cached))
            {
                // Populate ImageId on each record (the serializer doesn't store it)
                foreach (OffsetRecord offset in cached.Offsets)
                    offset.ImageId = imageId;
                return cached.Offsets;
            }

            BinaryIndexFile indexFile = getIndexFile(setName);
            (List<OffsetRecord>? offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(imageId);

            // Populate ImageId on each record (the serializer doesn't store it)
            foreach (OffsetRecord offset in offsets)
                offset.ImageId = imageId;

            setCache[imageId] = (offsets, blockLocations);
            return offsets;
        }

        public IEnumerable<OffsetRecord> GetOffsetsForFile(string setName, long imageId, long offsetStart)
        {
            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());
            if (setCache.TryGetValue(imageId, out (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) cached))
            {
                List<OffsetRecord> result = new List<OffsetRecord>();
                foreach (OffsetRecord offset in cached.Offsets)
                {
                    if (offset.OffsetStart == offsetStart)
                    {
                        offset.ImageId = imageId;
                        result.Add(offset);
                    }
                }
                return result;
            }

            BinaryIndexFile indexFile = getIndexFile(setName);
            (List<OffsetRecord>? offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(imageId);

            setCache[imageId] = (offsets, blockLocations);

            List<OffsetRecord> filtered = new List<OffsetRecord>();
            foreach (OffsetRecord offset in offsets)
            {
                if (offset.OffsetStart == offsetStart)
                {
                    offset.ImageId = imageId;
                    filtered.Add(offset);
                }
            }

            return filtered;
        }

        public Dictionary<long, List<BlockKey>> GetOffsetBlockLists(string setName, long imageId)
        {
            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());
            List<OffsetRecord> offsets;

            if (setCache.TryGetValue(imageId, out (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) cached))
            {
                offsets = cached.Offsets;
            }
            else
            {
                BinaryIndexFile indexFile = getIndexFile(setName);
                (List<OffsetRecord>? readOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(imageId);

                setCache[imageId] = (readOffsets, blockLocations);
                offsets = readOffsets;
            }

            Dictionary<long, List<BlockKey>> result = new Dictionary<long, List<BlockKey>>();
            foreach (OffsetRecord offset in offsets)
            {
                if (offset.Blocks != null && offset.Blocks.Count > 0)
                {
                    result[offset.Offset] = offset.Blocks;
                }
            }

            return result;
        }

        public IEnumerable<OffsetRecord> GetOffsetsInRange(string setName, long imageId, long startOffset, long length)
        {
            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());
            List<OffsetRecord> offsets;

            if (setCache.TryGetValue(imageId, out (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) cached))
            {
                offsets = cached.Offsets;
            }
            else
            {
                BinaryIndexFile indexFile = getIndexFile(setName);
                (List<OffsetRecord>? readOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = indexFile.ReadImageBlockMap(imageId);

                setCache[imageId] = (readOffsets, blockLocations);
                offsets = readOffsets;
            }

            long endOffset = startOffset + length;
            List<OffsetRecord> result = new List<OffsetRecord>();
            foreach (OffsetRecord offset in offsets)
            {
                // Standard overlap check: offset < endOffset AND (offset + size) > startOffset
                if (offset.Offset < endOffset && (offset.Offset + offset.Size) > startOffset)
                {
                    offset.ImageId = imageId;
                    result.Add(offset);
                }
            }

            return result;
        }

        public bool BlockExists(string setName, BlockKey key)
        {
            BinaryIndexFile indexFile = getIndexFile(setName);
            InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();
            return blockIndex.TryGetBlock(key, out _, out _, out _);
        }

        public (CompressionType compressionType, byte[]? data) GetBlockData(string setName, BlockKey key)
        {
            try
            {
                // Use per-image BlockLocations cache (loaded with offsets from Image_BlockMap_Section)
                if (tryGetCachedBlockLocation(setName, key, out int fileId, out long offset, out int size))
                {
                    if (size <= 0)
                        return (CompressionType.None, null);

                    ShardFileManager shardManager = getShardFileManager(setName);
                    byte[] storedData = shardManager.ReadBlock(fileId, offset, size);

                    if (storedData == null || storedData.Length == 0)
                        return (CompressionType.None, null);

                    return (CompressionType.None, storedData);
                }

                // Fallback: check already-cached block index (e.g. after a write transaction).
                // If not cached yet but index file exists, load it (cold-start case for direct API usage).
                if (_indexFiles.TryGetValue(setName, out BinaryIndexFile? indexFile))
                {
                    if (indexFile.TryGetBlockFromCachedIndex(key, out int cachedFileId, out long cachedOffset, out int cachedSize))
                    {
                        if (cachedSize <= 0)
                            return (CompressionType.None, null);

                        ShardFileManager shardMgr = getShardFileManager(setName);
                        byte[] data = shardMgr.ReadBlock(cachedFileId, cachedOffset, cachedSize);

                        if (data == null || data.Length == 0)
                            return (CompressionType.None, null);

                        return (CompressionType.None, data);
                    }

                    // Cold start: block index not yet loaded — load it now.
                    // This only happens on direct GetBlockData calls without an ImageReader
                    // (e.g. test scenarios, aux block lookups). Normal read paths go through
                    // the per-image BlockLocations cache above.
                    InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();
                    if (blockIndex.TryGetBlock(key, out int idxFileId, out long idxOffset, out int idxSize))
                    {
                        if (idxSize <= 0)
                            return (CompressionType.None, null);

                        ShardFileManager shardMgr2 = getShardFileManager(setName);
                        byte[] data2 = shardMgr2.ReadBlock(idxFileId, idxOffset, idxSize);

                        if (data2 == null || data2.Length == 0)
                            return (CompressionType.None, null);

                        return (CompressionType.None, data2);
                    }
                }

                return (CompressionType.None, null);
            }
            catch (FileNotFoundException)
            {
                return (CompressionType.None, null);
            }
            catch (IOException)
            {
                return (CompressionType.None, null);
            }
        }

        /// <summary>
        /// Resolves the shard location (fileId, offset, size) for each block key in the given list.
        /// Uses per-image BlockLocations cache (no global Block_Index load).
        /// Returns an array of tuples parallel to the input keys. Entries with size=0 indicate missing blocks.
        /// </summary>
        internal (int fileId, long offset, int size)[] GetBlockLocations(string setName, BlockKey[] keys)
        {
            (int fileId, long offset, int size)[] result = new (int fileId, long offset, int size)[keys.Length];
            try
            {
                // Try per-image BlockLocations cache first
                ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());

                foreach ((long _, (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations)) in setCache)
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (result[i].size == 0 && blockLocations.TryGetValue(keys[i], out (int FileId, long Offset, int Size) loc))
                            result[i] = (loc.FileId, loc.Offset, loc.Size);
                    }
                }

                // For any unresolved keys, check already-cached block index (no load triggered)
                if (_indexFiles.TryGetValue(setName, out BinaryIndexFile? indexFile))
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (result[i].size == 0 && indexFile.TryGetBlockFromCachedIndex(keys[i], out int fileId, out long offset, out int size))
                            result[i] = (fileId, offset, size);
                    }
                }
            }
            catch
            {
                // On failure, all entries remain (0,0,0) — caller falls back to per-block reads
            }
            return result;
        }

        /// <summary>
        /// Tries to find a block's location in the per-image BlockLocations cache.
        /// </summary>
        private bool tryGetCachedBlockLocation(string setName, BlockKey key, out int fileId, out long offset, out int size)
        {
            fileId = 0;
            offset = 0;
            size = 0;

            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());

            foreach ((long _, (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations)) in setCache)
            {
                if (blockLocations.TryGetValue(key, out (int FileId, long Offset, int Size) loc))
                {
                    fileId = loc.FileId;
                    offset = loc.Offset;
                    size = loc.Size;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the cached BlockLocations dictionary for a specific image, or null if not cached.
        /// Used by ImageReader to store its own copy for self-contained block reads.
        /// </summary>
        internal Dictionary<BlockKey, (int FileId, long Offset, int Size)>? GetCachedBlockLocationsForImage(string setName, long imageId)
        {
            ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> setCache = _blockMapCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>());

            if (setCache.TryGetValue(imageId, out (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) cached))
                return cached.BlockLocations;

            return null;
        }

        /// <summary>
        /// Reads a contiguous byte range from a shard file. Used by ReadBlockRun to coalesce
        /// multiple adjacent blocks into a single I/O operation.
        /// </summary>
        internal byte[]? ReadShardRange(string setName, int fileId, long offset, int totalLength)
        {
            try
            {
                ShardFileManager shardManager = getShardFileManager(setName);
                return shardManager.ReadRange(fileId, offset, totalLength);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Invalidates the cached read stream for a specific shard file, forcing the next
        /// read to reopen the file. Used by ImageReader retry logic when a shard file handle
        /// is invalidated due to the shard being replaced during compaction.
        /// </summary>
        internal void InvalidateShardReadStream(string setName, int fileId)
        {
            if (_shardFileManagers.TryGetValue(setName, out ShardFileManager? shardManager))
            {
                shardManager.InvalidateReadStream(fileId);
            }
        }

        public void InsertBlock(string setName, IDataStoreTransaction transaction, BlockKey key, byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Block data cannot be null or empty", nameof(data));

            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            // Check deduplication: does this block already exist in the index?
            if (binaryTransaction.BlockIndex.TryGetBlock(key, out int existingFileId, out long existingOffset, out int existingSize))
            {
                // Block already exists — just record the existing location in pending block locations
                binaryTransaction.AddPendingBlockLocation(key, existingFileId, existingOffset, existingSize);
                return;
            }

            // Block is new — write to shard via ShardFileManager
            ShardFileManager shardManager = getShardFileManager(setName);
            (int fileId, long offset, int length) = shardManager.WriteBlock(data, 0, data.Length);

            // Record the new location in pending block locations
            binaryTransaction.AddPendingBlockLocation(key, fileId, offset, length);

            // Add to pending new blocks for the Block_Index_Delta
            BlockIndexEntry entry = new BlockIndexEntry
            {
                Key = key,
                FileId = fileId,
                Offset = offset,
                Size = length
            };
            binaryTransaction.AddPendingBlock(entry);

            // Also add to the in-memory block index so subsequent dedup checks within
            // the same transaction find this block
            binaryTransaction.BlockIndex.AddEntry(entry);
        }

        public void InsertBlock(string setName, IDataStoreTransaction transaction, BlockKey key, ReadOnlySpan<byte> data)
        {
            // Copy span to array and delegate
            byte[] dataArray = data.ToArray();
            InsertBlock(setName, transaction, key, dataArray);
        }

        public void InsertFile(string setName, IDataStoreTransaction transaction, long imageId, string name, byte[] data, bool isSystem)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("File data cannot be null or empty", nameof(data));

            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            // Zstd compress the file data
            byte[] zstdFrameMagic = { 0x28, 0xB5, 0x2F, 0xFD };
            int maxCompressed = data.Length + 0x100; // worst-case overhead
            byte[] compressedBuffer = new byte[maxCompressed];
            int compressedSize = maxCompressed;

            using (CompressionBlock compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = data.Length,
                    Type = (Nanook.GrindCore.CompressionType)19
                }))
            {
                compressor.Compress(data, 0, data.Length, compressedBuffer, 0, ref compressedSize);
            }

            // Strip the Zstd frame magic bytes (always the same 4-byte header)
            if (compressedSize >= zstdFrameMagic.Length)
            {
                Buffer.BlockCopy(compressedBuffer, zstdFrameMagic.Length, compressedBuffer, 0, compressedSize - zstdFrameMagic.Length);
                compressedSize -= zstdFrameMagic.Length;
            }

            // Write compressed data to shard via ShardFileManager
            ShardFileManager shardManager = getShardFileManager(setName);
            (int fileId, long offset, int length) = shardManager.WriteBlock(compressedBuffer, 0, compressedSize);

            // Create FileRecord with the shard location
            FileRecord fileRecord = new FileRecord
            {
                ImageId = imageId,
                Name = name,
                FileId = fileId,
                Offset = offset,
                Size = length,
                UncompressedSize = data.Length,
                IsSystem = isSystem
            };

            binaryTransaction.AddPendingFile(fileRecord);
        }

        public FileRecord? GetFile(string setName, long imageId, string name)
        {
            ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)> setCache = _metadataCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>());
            if (setCache.TryGetValue(imageId, out (List<AreaRecord> Areas, List<FileRecord> Files) cached))
                return cached.Files.FirstOrDefault(f => f.Name == name);

            BinaryIndexFile indexFile = getIndexFile(setName);
            (List<AreaRecord>? areas, List<FileRecord>? files) = indexFile.ReadImageMetadata(imageId);

            // Bound the cache: clear if it exceeds 100 entries
            if (setCache.Count >= 100)
                setCache.Clear();

            setCache[imageId] = (areas, files);
            return files.FirstOrDefault(f => f.Name == name);
        }

        public IEnumerable<FileRecord> GetFilesForImage(string setName, long imageId)
        {
            ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)> setCache = _metadataCache.GetOrAdd(setName, _ => new ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>());
            if (setCache.TryGetValue(imageId, out (List<AreaRecord> Areas, List<FileRecord> Files) cached))
                return cached.Files;

            BinaryIndexFile indexFile = getIndexFile(setName);
            (List<AreaRecord>? areas, List<FileRecord>? files) = indexFile.ReadImageMetadata(imageId);

            // Bound the cache: clear if it exceeds 100 entries
            if (setCache.Count >= 100)
                setCache.Clear();

            setCache[imageId] = (areas, files);
            return files;
        }

        public byte[]? ReadFileData(string setName, FileRecord record)
        {
            try
            {
                ShardFileManager shardManager = getShardFileManager(setName);
                byte[] compressedData = shardManager.ReadBlock(record.FileId, record.Offset, (int)record.Size);

                if (compressedData == null || compressedData.Length == 0)
                    return null;

                // Restore the Zstd frame magic bytes that were stripped during storage
                byte[] zstdFrameMagic = { 0x28, 0xB5, 0x2F, 0xFD };
                byte[] restoredData = new byte[compressedData.Length + zstdFrameMagic.Length];
                Buffer.BlockCopy(zstdFrameMagic, 0, restoredData, 0, zstdFrameMagic.Length);
                Buffer.BlockCopy(compressedData, 0, restoredData, zstdFrameMagic.Length, compressedData.Length);

                // Decompress
                byte[] decompressed = new byte[record.UncompressedSize];
                int decompressedSize = decompressed.Length;

                using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = (int)record.UncompressedSize,
                        Type = (Nanook.GrindCore.CompressionType)19
                    }))
                {
                    decompressor.Decompress(restoredData, 0, restoredData.Length, decompressed, 0, ref decompressedSize);
                }

                return decompressed;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void InsertBlockWithCompression(string setName, IDataStoreTransaction transaction, BlockKey key,
            byte[] uncompressedData, int offset, int length, IBlockCompressor compressor, int compressionParallelism)
        {
            BinaryTransaction binaryTransaction = (BinaryTransaction)transaction;

            // Check deduplication first — skip compression entirely if block exists
            if (binaryTransaction.BlockIndex.TryGetBlock(key, out int existingFileId, out long existingOffset, out int existingSize))
            {
                // Block already exists — just record the existing location
                binaryTransaction.AddPendingBlockLocation(key, existingFileId, existingOffset, existingSize);
                return;
            }

            // Get or create the BlockWriter for this set (handles parallel compression + buffered writes)
            BlockWriter blockWriter = _blockWriters.GetOrAdd(setName, _ =>
            {
                InfoRecord info = GetSetInfo(setName);
                return new BlockWriter(compressionParallelism, info.BlockSize);
            });

            (ulong xxHash, uint crc) blockKey = (key.XxHash64, key.Crc32);

            blockWriter.InsertBlockWithCompression(
                setName,
                blockKey,
                uncompressedData,
                offset,
                length,
                // existsFunc: check the in-memory block index
                (sn, k) => binaryTransaction.BlockIndex.TryGetBlock(new BlockKey(k.xxHash, k.crc), out _, out _, out _),
                // acquireShardWrite: the ShardFileManager already has internal locking, return a no-op disposable
                (sn, k) => new NoOpDisposable(),
                // compressFunc: compress using a thread-safe compressor pool (BlockCompressor is NOT thread-safe)
                (srcBuf, srcOffset, srcLength, dstBuf) =>
                {
                    InfoRecord info = GetSetInfo(setName);
                    ConcurrentBag<BlockCompressor> pool = _compressorPools.GetOrAdd(setName, _ => new ConcurrentBag<BlockCompressor>());
                    if (!pool.TryTake(out BlockCompressor? localCompressor))
                        localCompressor = new BlockCompressor(info.BlockSize);

                    try
                    {
                        ReadOnlyMemory<byte> compressed = localCompressor.Compress(srcBuf, srcOffset, srcLength, out CompressionType _);
                        int compressedLen = compressed.Length;
                        compressed.Span.CopyTo(new Span<byte>(dstBuf, 0, compressedLen));
                        return (compressedLen, (byte)0);
                    }
                    finally
                    {
                        pool.Add(localCompressor);
                    }
                },
                // writeFunc: write compressed data to shard and record in transaction
                (sn, k, dstBuf, compressedLength, compressionType) =>
                {
                    ShardFileManager shardManager = getShardFileManager(sn);
                    (int fileId, long shardOffset, int storedLength) = shardManager.WriteBlock(dstBuf, 0, compressedLength);

                    BlockKey bk = new BlockKey(k.xxHash, k.crc);
                    binaryTransaction.AddPendingBlockLocation(bk, fileId, shardOffset, storedLength);

                    BlockIndexEntry entry = new BlockIndexEntry
                    {
                        Key = bk,
                        FileId = fileId,
                        Offset = shardOffset,
                        Size = storedLength
                    };
                    binaryTransaction.AddPendingBlock(entry);
                    binaryTransaction.BlockIndex.AddEntry(entry);
                }
            );
        }

        public void WaitForCompressionTasks(string setName, int timeoutMs = 30000)
        {
            if (_blockWriters.TryGetValue(setName, out BlockWriter? writer))
            {
                writer.WaitAll(timeoutMs);
            }
        }

        #endregion


        #region Health Check

        /// <summary>
        /// Inspects the file state of a set and reports its health status.
        /// This is a read-only operation that does not modify any files.
        /// Detects intermediate crash states, orphaned tail data, and unrecoverable conditions.
        /// </summary>
        /// <param name="setName">The name of the set to inspect.</param>
        /// <returns>A RecoveryState indicating the set's health status and any detected issues.</returns>
        public RecoveryState CheckSetHealth(string setName)
        {
            List<string> issues = new List<string>();
            List<OrphanedShardInfo> orphanedShards = new List<OrphanedShardInfo>();
            SetHealthStatus status = SetHealthStatus.Clean;

            string indexFilePath = GetIndexFilePath(setName);
            string compactTmpPath = indexFilePath + ".compact.tmp";
            bool indexExists = File.Exists(indexFilePath);
            bool compactTmpExists = File.Exists(compactTmpPath);

            // --- Check 1: .compact.tmp files ---
            if (compactTmpExists && indexExists)
            {
                // Compact was interrupted but original is intact
                issues.Add($".compact.tmp file exists alongside original index: '{compactTmpPath}'. Recovery action: delete .compact.tmp.");
                status = SetHealthStatus.NeedsRecovery;
            }
            else if (compactTmpExists && !indexExists)
            {
                // Original was deleted but .compact.tmp not yet renamed
                // Validate the .compact.tmp file headers
                if (tryValidateIndexHeaders(compactTmpPath, baseOffset: 0))
                {
                    issues.Add($".compact.tmp file exists but original index is missing: '{compactTmpPath}'. Recovery action: rename .compact.tmp to original path.");
                    status = SetHealthStatus.NeedsRecovery;
                }
                else
                {
                    // .compact.tmp fails validation and no original — unrecoverable
                    issues.Add($".compact.tmp file exists but fails header validation, and original index is missing: '{compactTmpPath}'. State is unrecoverable.");
                    return new RecoveryState
                    {
                        Status = SetHealthStatus.Unrecoverable,
                        Description = "Compacted temp file fails validation and original index is missing.",
                        DetectedIssues = issues,
                        OrphanedShards = orphanedShards
                    };
                }
            }
            else if (!indexExists && !compactTmpExists)
            {
                // No index file and no .compact.tmp — check if this is an embedded set or truly missing
                // For embedded mode, the index is inside the shard file, so check for shard files
                string setDirectory = Path.GetDirectoryName(indexFilePath) ?? _baseDirectory;
                string setBaseName = Path.GetFileNameWithoutExtension(setName);
                bool anyShardExists = false;

                try
                {
                    foreach (string _ in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds"))
                    {
                        anyShardExists = true;
                        break;
                    }
                }
                catch { }

                if (!anyShardExists)
                {
                    // No index, no compact.tmp, no shards — unrecoverable (or set doesn't exist)
                    issues.Add($"No index file and no .compact.tmp file found for set '{setName}'. State is unrecoverable.");
                    return new RecoveryState
                    {
                        Status = SetHealthStatus.Unrecoverable,
                        Description = "No index file and no .compact.tmp file exist for this set.",
                        DetectedIssues = issues,
                        OrphanedShards = orphanedShards
                    };
                }
            }

            // --- Check 2: Shard .tmp files ---
            {
                string setDirectory = Path.GetDirectoryName(indexFilePath) ?? _baseDirectory;
                string setBaseName = Path.GetFileNameWithoutExtension(setName);

                try
                {
                    foreach (string shardTmpFile in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds.tmp"))
                    {
                        string originalShardPath = shardTmpFile.Substring(0, shardTmpFile.Length - 4); // Remove .tmp suffix
                        if (File.Exists(originalShardPath))
                        {
                            issues.Add($"Shard .tmp file exists alongside original: '{shardTmpFile}'. Recovery action: delete .tmp file.");
                        }
                        else
                        {
                            issues.Add($"Shard .tmp file exists without original: '{shardTmpFile}'. Recovery action: rename .tmp to original.");
                        }
                        status = SetHealthStatus.NeedsRecovery;
                    }
                }
                catch { }
            }

            // --- Check 3: Embedded intermediate states (via EmbeddedIndexCommitter file checks) ---
            {
                EmbeddedIndexCommitter committer = new EmbeddedIndexCommitter(indexFilePath);
                bool indexTmpExists = File.Exists(committer.IndexTmpPath);
                bool shardExists = File.Exists(committer.ShardPath);
                bool shardTmpExists = File.Exists(committer.ShardTmpPath);

                if (indexTmpExists && indexExists)
                {
                    // Extraction was interrupted: .tmp index exists alongside main file
                    issues.Add($"Embedded extraction intermediate: index .tmp file exists alongside main file: '{committer.IndexTmpPath}'. Recovery action: delete .tmp file.");
                    status = SetHealthStatus.NeedsRecovery;
                }
                else if (indexTmpExists && !indexExists)
                {
                    // Index .tmp exists but main file is gone — unusual state
                    issues.Add($"Embedded extraction intermediate: index .tmp file exists but main file is missing: '{committer.IndexTmpPath}'.");
                    status = SetHealthStatus.NeedsRecovery;
                }

                if (shardTmpExists)
                {
                    // Shard .tmp from embedded re-embed operation
                    issues.Add($"Embedded re-embed intermediate: shard .tmp file exists: '{committer.ShardTmpPath}'. Recovery action: EmbeddedIndexCommitter.TryRecover.");
                    status = SetHealthStatus.NeedsRecovery;
                }

                // Check for mid-write separate mode state (both embedded path and shard path exist, not in embedded mode)
                if (indexExists && shardExists && !detectEmbeddedMode(indexFilePath))
                {
                    // This is a valid mid-write state for embedded mode sets — the set was extracted to separate mode
                    // Only flag if the index header indicates embedded mode (shardSize == 0)
                    if (tryReadShardSizeFromHeader(indexFilePath, baseOffset: 0, out long shardSize) && shardSize == 0)
                    {
                        issues.Add($"Embedded set in separate mode (mid-write state): both index and shard file exist: '{committer.ShardPath}'. Recovery action: EmbeddedIndexCommitter.TryRecover.");
                        status = SetHealthStatus.NeedsRecovery;
                    }
                }
            }

            // --- Check 4: Validate index headers (detect corrupt headers) ---
            if (indexExists && status != SetHealthStatus.Unrecoverable)
            {
                long baseOffset = 0;
                if (detectEmbeddedMode(indexFilePath))
                {
                    try
                    {
                        (long indexSize, bool isEmbedded) = readEmbeddedFooter(indexFilePath);
                        if (isEmbedded)
                        {
                            baseOffset = new FileInfo(indexFilePath).Length - EmbeddedFooter.FooterSize - indexSize;
                        }
                    }
                    catch
                    {
                        issues.Add($"Embedded footer is corrupt or invalid in '{indexFilePath}'.");
                        return new RecoveryState
                        {
                            Status = SetHealthStatus.Unrecoverable,
                            Description = "Embedded footer is corrupt or invalid.",
                            DetectedIssues = issues,
                            OrphanedShards = orphanedShards
                        };
                    }
                }

                if (!tryValidateIndexHeaders(indexFilePath, baseOffset))
                {
                    issues.Add($"Both primary and secondary headers are corrupt in '{indexFilePath}'.");
                    return new RecoveryState
                    {
                        Status = SetHealthStatus.Unrecoverable,
                        Description = "Both primary and secondary headers are corrupt.",
                        DetectedIssues = issues,
                        OrphanedShards = orphanedShards
                    };
                }
            }

            // --- Check 5: Orphaned tail data detection ---
            // Only check if the index can be opened (exists and headers are valid)
            if (indexExists && status != SetHealthStatus.Unrecoverable)
            {
                try
                {
                    detectOrphanedTailData(setName, indexFilePath, issues, orphanedShards, ref status);
                }
                catch
                {
                    // If we can't load the block index, skip orphaned tail detection
                    // This is not an unrecoverable state — the index may still be usable
                }
            }

            // --- Build result ---
            string description = status switch
            {
                SetHealthStatus.Clean => "All files are consistent, no recovery needed.",
                SetHealthStatus.NeedsRecovery => $"Recovery needed: {issues.Count} issue(s) detected.",
                SetHealthStatus.Unrecoverable => "Critical files missing or corrupt, recovery is not possible.",
                _ => ""
            };

            return new RecoveryState
            {
                Status = status,
                Description = description,
                DetectedIssues = issues,
                OrphanedShards = orphanedShards
            };
        }

        /// <summary>
        /// Validates that at least one header (primary or secondary) in the index file is valid.
        /// Does NOT modify the file.
        /// </summary>
        private bool tryValidateIndexHeaders(string filePath, long baseOffset)
        {
            try
            {
                using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (stream.Length < baseOffset + (FileHeader.HeaderSize * 2))
                    return false;

                byte[] headerBytes = new byte[FileHeader.HeaderSize];
                long indexSize = stream.Length - baseOffset;

                // Try primary header
                stream.Position = baseOffset;
                int read = stream.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader primary = FileHeaderSerializer.Read(headerBytes);
                        if (isHeaderStructurallyValid(primary, indexSize))
                            return true;
                    }
                    catch (InvalidDataException) { }
                }

                // Try secondary header
                stream.Position = baseOffset + FileHeader.HeaderSize;
                read = stream.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader secondary = FileHeaderSerializer.Read(headerBytes);
                        if (isHeaderStructurallyValid(secondary, indexSize))
                            return true;
                    }
                    catch (InvalidDataException) { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tries to read the ShardSize field from the primary or secondary header.
        /// Does NOT modify the file.
        /// </summary>
        private bool tryReadShardSizeFromHeader(string filePath, long baseOffset, out long shardSize)
        {
            shardSize = -1;
            try
            {
                using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (stream.Length < baseOffset + (FileHeader.HeaderSize * 2))
                    return false;

                byte[] headerBytes = new byte[FileHeader.HeaderSize];

                // Try primary header
                stream.Position = baseOffset;
                int read = stream.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader primary = FileHeaderSerializer.Read(headerBytes);
                        shardSize = primary.ShardSize;
                        return true;
                    }
                    catch (InvalidDataException) { }
                }

                // Try secondary header
                stream.Position = baseOffset + FileHeader.HeaderSize;
                read = stream.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader secondary = FileHeaderSerializer.Read(headerBytes);
                        shardSize = secondary.ShardSize;
                        return true;
                    }
                    catch (InvalidDataException) { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates header structural fields without requiring the full BinaryIndexFile.IsHeaderValid logic.
        /// Checks magic, version, and basic offset sanity.
        /// </summary>
        private static bool isHeaderStructurallyValid(FileHeader header, long indexSize)
        {
            if (header.Magic != FileHeader.MagicBytes)
                return false;

            if (header.MajorVersion != FileHeader.CurrentMajorVersion)
                return false;

            if (header.MinorVersion > FileHeader.CurrentMinorVersion)
                return false;

            if (header.ImageDirectoryOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.BlockIndexOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.FileEndOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.BlockSize <= 0)
                return false;

            if (header.MaxOffsetBlocks <= 0)
                return false;

            if (header.ShardSize < 0)
                return false;

            return true;
        }

        /// <summary>
        /// Detects orphaned tail data by comparing shard file sizes to expected sizes
        /// computed from the committed block index. Does NOT modify any files.
        /// </summary>
        private void detectOrphanedTailData(string setName, string indexFilePath, List<string> issues, List<OrphanedShardInfo> orphanedShards, ref SetHealthStatus status)
        {
            // We need to open the index file to load the block index.
            // If the set is already open, use the cached index file; otherwise open temporarily.
            BinaryIndexFile? tempIndexFile = null;
            InMemoryBlockIndex blockIndex;

            try
            {
                if (_indexFiles.TryGetValue(setName, out BinaryIndexFile? cachedIndexFile) && !cachedIndexFile.IsStreamClosed)
                {
                    blockIndex = cachedIndexFile.LoadBlockIndex();
                }
                else
                {
                    // Open temporarily — BinaryIndexFile.Open uses ReadWrite access but we won't write.
                    // The health check is read-only in behavior (no modifications).
                    long baseOffset = 0;
                    if (detectEmbeddedMode(indexFilePath))
                    {
                        (long indexSize, bool _) = readEmbeddedFooter(indexFilePath);
                        baseOffset = new FileInfo(indexFilePath).Length - EmbeddedFooter.FooterSize - indexSize;
                    }

                    tempIndexFile = BinaryIndexFile.Open(indexFilePath, baseOffset);
                    blockIndex = tempIndexFile.LoadBlockIndex();
                }

                // Compute expected end offset for each shard from the block index
                BlockIndexEntry[] entries = blockIndex.GetEntries();
                Dictionary<int, long> expectedShardSizes = new Dictionary<int, long>();

                foreach (ref BlockIndexEntry entry in entries.AsSpan())
                {
                    long endOffset = entry.Offset + entry.Size;
                    if (!expectedShardSizes.TryGetValue(entry.FileId, out long currentMax) || endOffset > currentMax)
                    {
                        expectedShardSizes[entry.FileId] = endOffset;
                    }
                }

                // Check actual shard file sizes against expected sizes
                string setDirectory = Path.GetDirectoryName(indexFilePath) ?? _baseDirectory;
                string setBaseName = Path.GetFileNameWithoutExtension(setName);

                try
                {
                    foreach (string shardFile in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds"))
                    {
                        // Skip .tmp files (already handled in Check 2)
                        if (shardFile.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Parse the file ID from the shard file name (e.g., "test_0001.nkds" → fileId=1)
                        int fileId = parseShardFileId(shardFile, setBaseName);
                        if (fileId < 0)
                            continue;

                        long actualSize = new FileInfo(shardFile).Length;

                        if (expectedShardSizes.TryGetValue(fileId, out long expectedSize))
                        {
                            if (actualSize > expectedSize)
                            {
                                issues.Add($"Shard file '{shardFile}' has orphaned tail data: actual size {actualSize} bytes, expected {expectedSize} bytes ({actualSize - expectedSize} orphaned bytes). Recovery action: truncate to expected size.");
                                orphanedShards.Add(new OrphanedShardInfo
                                {
                                    ShardPath = shardFile,
                                    ExpectedSize = expectedSize,
                                    ActualSize = actualSize
                                });
                                status = SetHealthStatus.NeedsRecovery;
                            }
                        }
                        else
                        {
                            // Shard exists but no blocks reference it — entirely orphaned
                            if (actualSize > 0)
                            {
                                issues.Add($"Shard file '{shardFile}' exists with {actualSize} bytes but no blocks reference it. Recovery action: delete shard file.");
                                orphanedShards.Add(new OrphanedShardInfo
                                {
                                    ShardPath = shardFile,
                                    ExpectedSize = 0,
                                    ActualSize = actualSize
                                });
                                status = SetHealthStatus.NeedsRecovery;
                            }
                        }
                    }
                }
                catch { }
            }
            finally
            {
                tempIndexFile?.Dispose();
            }
        }

        /// <summary>
        /// Parses the shard file ID from a shard file name.
        /// Expected format: "{setBaseName}_{NNNN}.nkds" where NNNN is the zero-padded file ID.
        /// Returns -1 if the file name doesn't match the expected pattern.
        /// </summary>
        private static int parseShardFileId(string shardFilePath, string setBaseName)
        {
            string fileName = Path.GetFileNameWithoutExtension(shardFilePath);
            string prefix = setBaseName + "_";

            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;

            string idPart = fileName.Substring(prefix.Length);
            if (int.TryParse(idPart, out int fileId))
                return fileId;

            return -1;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Dispose all block writers (waits for pending compression)
            foreach (KeyValuePair<string, BlockWriter> kvp in _blockWriters)
            {
                try { kvp.Value.Dispose(); }
                catch { }
            }
            _blockWriters.Clear();

            // Dispose all open index files
            foreach (KeyValuePair<string, BinaryIndexFile> kvp in _indexFiles)
            {
                try { kvp.Value.Dispose(); }
                catch { }
            }
            _indexFiles.Clear();

            // Dispose all shard file managers
            foreach (KeyValuePair<string, ShardFileManager> kvp in _shardFileManagers)
            {
                try { kvp.Value.Dispose(); }
                catch { }
            }
            _shardFileManagers.Clear();

            // Dispose write lock semaphores
            foreach (KeyValuePair<string, SemaphoreSlim> kvp in _setWriteLocks)
            {
                try { kvp.Value.Dispose(); }
                catch { }
            }
            _setWriteLocks.Clear();

            // Clear remaining caches to release all references
            _compressorPools.Clear();
            _committers.Clear();
            _setUsage.Clear();
            _infoCache.Clear();
            _embeddedMode.Clear();
            _shardBoundary.Clear();
            _metadataCache.Clear();
            _blockMapCache.Clear();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Invalidates cached metadata and block map sections for a specific image.
        /// </summary>
        private void invalidateImageCaches(string setName, long imageId)
        {
            if (_metadataCache.TryGetValue(setName, out ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>? metaCache))
                metaCache.TryRemove(imageId, out _);
            if (_blockMapCache.TryGetValue(setName, out ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>? blockCache))
                blockCache.TryRemove(imageId, out _);
        }

        /// <summary>
        /// Invalidates all cached metadata and block map sections for an entire set.
        /// </summary>
        private void invalidateSetCaches(string setName)
        {
            if (_metadataCache.TryGetValue(setName, out ConcurrentDictionary<long, (List<AreaRecord> Areas, List<FileRecord> Files)>? metaCache))
                metaCache.Clear();
            if (_blockMapCache.TryGetValue(setName, out ConcurrentDictionary<long, (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)>? blockCache))
                blockCache.Clear();
        }

        /// <summary>
        /// Gets the file path for the binary index file for a given set name.
        /// Convention: {baseDirectory}/{setName}.nkds
        /// </summary>
        internal string GetIndexFilePath(string setName) => Path.Combine(_baseDirectory, $"{setName}.nkds");

        /// <summary>
        /// Detects whether a file is in embedded mode by checking the last 4 bytes for FooterMagic.
        /// Files smaller than 12 bytes are treated as not embedded.
        /// </summary>
        /// <param name="filePath">The path to the file to check.</param>
        /// <returns>True if the file contains an embedded index (last 4 bytes match FooterMagic), false otherwise.</returns>
        private bool detectEmbeddedMode(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < EmbeddedFooter.FooterSize)
                return false;

            Span<byte> magicBuffer = stackalloc byte[4];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-4, SeekOrigin.End);
            int bytesRead = stream.Read(magicBuffer);
            if (bytesRead < 4)
                return false;

            return EmbeddedFooter.IsMagicValid(magicBuffer);
        }

        /// <summary>
        /// Reads the Embedded_Footer from the end of a file.
        /// Returns the IndexSize and whether the file is in embedded mode.
        /// </summary>
        /// <param name="filePath">The path to the file to read the footer from.</param>
        /// <returns>A tuple of (indexSize, isEmbedded). If not embedded, indexSize is 0.</returns>
        /// <exception cref="InvalidDataException">Thrown if the footer data is invalid (IndexSize exceeds file bounds or produces a negative Base_Offset).</exception>
        private (long indexSize, bool isEmbedded) readEmbeddedFooter(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < EmbeddedFooter.FooterSize)
                return (0, false);

            Span<byte> footerBuffer = stackalloc byte[EmbeddedFooter.FooterSize];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
            int bytesRead = stream.Read(footerBuffer);
            if (bytesRead < EmbeddedFooter.FooterSize)
                return (0, false);

            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBuffer);
            if (footer == null)
                return (0, false);

            long indexSize = footer.Value.IndexSize;
            long fileSize = fileInfo.Length;

            // Validate: IndexSize must not exceed file_size - 12
            if (indexSize > fileSize - EmbeddedFooter.FooterSize)
                throw new InvalidDataException(
                    $"Invalid embedded footer in '{filePath}': IndexSize ({indexSize}) exceeds file size minus footer ({fileSize - EmbeddedFooter.FooterSize}).");

            // Validate: computed Base_Offset must not be negative
            long baseOffset = fileSize - EmbeddedFooter.FooterSize - indexSize;
            if (baseOffset < 0)
                throw new InvalidDataException(
                    $"Invalid embedded footer in '{filePath}': computed Base_Offset ({baseOffset}) is negative, indicating corrupted footer data.");

            return (indexSize, true);
        }

        /// <summary>
        /// Computes the Shard_Boundary (where block data ends and the index begins) from the footer data.
        /// The Shard_Boundary equals file_size - 12 - indexSize.
        /// </summary>
        /// <param name="filePath">The path to the embedded file.</param>
        /// <param name="indexSize">The IndexSize value from the Embedded_Footer.</param>
        /// <returns>The shard boundary offset (Base_Offset).</returns>
        /// <exception cref="InvalidDataException">Thrown if the computed boundary is negative or indexSize exceeds file bounds.</exception>
        private long computeShardBoundary(string filePath, long indexSize)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            // Validate: IndexSize must not exceed file_size - 12
            if (indexSize > fileSize - EmbeddedFooter.FooterSize)
                throw new InvalidDataException(
                    $"Invalid embedded footer in '{filePath}': IndexSize ({indexSize}) exceeds file size minus footer ({fileSize - EmbeddedFooter.FooterSize}).");

            long boundary = fileSize - EmbeddedFooter.FooterSize - indexSize;

            // Validate: computed boundary (Base_Offset) must not be negative
            if (boundary < 0)
                throw new InvalidDataException(
                    $"Invalid embedded footer in '{filePath}': computed Base_Offset ({boundary}) is negative, indicating corrupted footer data.");

            return boundary;
        }

        /// <summary>
        /// Performs crash recovery for a set before normal initialization.
        /// Detects intermediate file states left by interrupted write/compaction operations
        /// and resolves them so the set can be opened normally.
        /// Handles both embedded-mode recovery (via EmbeddedIndexCommitter) and separate-mode
        /// recovery for .compact.tmp files.
        /// </summary>
        /// <param name="setName">The name of the set to recover.</param>
        /// <summary>
        /// Cheap check for on-disk evidence that a previous operation (compaction / embedded
        /// commit) was interrupted for this set: a set-level ".compact.tmp" index temp, or any
        /// shard/index ".tmp" file. Used to gate recovery on the read path so a clean set is
        /// never disturbed. Returns false on any I/O error (treated as "no artifacts").
        /// </summary>
        private bool hasPendingRecoveryArtifacts(string setName)
        {
            try
            {
                string setBasePath = GetIndexFilePath(setName);
                if (File.Exists(setBasePath + ".compact.tmp"))
                    return true;

                string setDirectory = Path.GetDirectoryName(setBasePath) ?? _baseDirectory;
                string setBaseName = Path.GetFileNameWithoutExtension(setBasePath);
                if (!Directory.Exists(setDirectory))
                    return false;

                // Any "<set>_*.nkds.tmp" (shard temp) or "<set>.nkds.tmp" (embedded index temp).
                return Directory.EnumerateFiles(setDirectory, $"{setBaseName}*.tmp").Any();
            }
            catch
            {
                return false;
            }
        }

        private void recoverIfNeeded(string setName)
        {
            string setBasePath = GetIndexFilePath(setName);

            // --- Separate mode: .compact.tmp recovery ---
            string compactTmpPath = setBasePath + ".compact.tmp";
            bool indexExists = File.Exists(setBasePath);
            bool compactTmpExists = File.Exists(compactTmpPath);

            if (indexExists && compactTmpExists)
            {
                // Both original and .compact.tmp exist: the compact did not complete.
                // The original is authoritative — delete the incomplete .compact.tmp.
                Trace.TraceWarning(
                    $"[RecoverIfNeeded] Set '{setName}': .compact.tmp exists alongside original index. " +
                    $"Deleting incomplete .compact.tmp: {compactTmpPath}");
                try
                {
                    File.Delete(compactTmpPath);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        $"[RecoverIfNeeded] Set '{setName}': Failed to delete .compact.tmp '{compactTmpPath}': {ex.Message}");
                }
            }
            else if (!indexExists && compactTmpExists)
            {
                // Only .compact.tmp exists (original missing): the delete succeeded but move didn't complete.
                // Rename .compact.tmp to the original path and validate.
                Trace.TraceWarning(
                    $"[RecoverIfNeeded] Set '{setName}': Original index missing but .compact.tmp exists. " +
                    $"Renaming '{compactTmpPath}' to '{setBasePath}'");
                try
                {
                    File.Move(compactTmpPath, setBasePath);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        $"[RecoverIfNeeded] Set '{setName}': Failed to rename .compact.tmp to original path: {ex.Message}. " +
                        $"Set is unrecoverable.");
                    return;
                }

                // Validate the header of the renamed file
                try
                {
                    using (BinaryIndexFile.Open(setBasePath))
                    {
                        // Header validation passed — file is usable
                        Trace.TraceWarning(
                            $"[RecoverIfNeeded] Set '{setName}': Successfully recovered index from .compact.tmp. " +
                            $"Header validation passed.");
                    }
                }
                catch (Exception ex)
                {
                    // Validation failed — the .compact.tmp was corrupt. Set is unrecoverable.
                    Trace.TraceWarning(
                        $"[RecoverIfNeeded] Set '{setName}': Recovered .compact.tmp failed header validation: {ex.Message}. " +
                        $"Set is unrecoverable.");
                    return;
                }
            }

            // --- Embedded mode recovery ---
            EmbeddedIndexCommitter committer = new EmbeddedIndexCommitter(setBasePath);

            EmbeddedRecoveryResult result = committer.TryRecover();

            switch (result)
            {
                case EmbeddedRecoveryResult.AlreadyEmbedded:
                    // Normal embedded file, no recovery needed — proceed with embedded mode open
                    break;

                case EmbeddedRecoveryResult.RecoveredToEmbedded:
                    // Successfully recovered to embedded mode — proceed with embedded mode open
                    break;

                case EmbeddedRecoveryResult.InSeparateMode:
                    // Both files exist (mid-write state) — treat as separate-mode set, continue normally
                    break;

                case EmbeddedRecoveryResult.NoFileFound:
                    // No set files found — proceed to create new file
                    break;
            }

            // --- Shard .tmp file recovery (promote-or-delete) ---
            // A shard ".tmp" is a compacted shard that was fully written but whose rename over
            // the original was deferred until AFTER the block index commit. On recovery we must
            // decide, per temp, whether the compaction had reached its durable commit point:
            //
            //   * If the committed index expects the compacted (smaller) size for this shard AND
            //     the temp file matches that expected size, the index was committed but one or
            //     more renames did not complete. The temp holds the CORRECT post-compact data,
            //     so we PROMOTE it (rename over the original). Deleting it here would corrupt
            //     every live image whose blocks moved in that shard.
            //   * Otherwise the compact did not reach commit (index still describes the old
            //     layout, or the temp is incomplete/mismatched). The original is authoritative,
            //     so we DELETE the temp to restore the pre-compact state.
            string setDirectory = Path.GetDirectoryName(setBasePath) ?? _baseDirectory;
            string setBaseName = Path.GetFileNameWithoutExtension(setBasePath);
            Dictionary<int, long>? committedShardSizes =
                File.Exists(setBasePath) ? computeExpectedShardSizes(setBasePath) : null;
            try
            {
                foreach (string tmpFile in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds.tmp"))
                {
                    // The original shard path is the .tmp path without the trailing ".tmp"
                    string originalShardPath = tmpFile[..^4]; // strip ".tmp"

                    int fileId = parseShardFileId(originalShardPath, setBaseName);
                    long tmpSize = -1;
                    try { tmpSize = new FileInfo(tmpFile).Length; } catch { }

                    bool indexExpectsCompacted =
                        committedShardSizes != null &&
                        fileId >= 0 &&
                        committedShardSizes.TryGetValue(fileId, out long expectedSize) &&
                        tmpSize == expectedSize &&
                        (!File.Exists(originalShardPath) || new FileInfo(originalShardPath).Length != expectedSize);

                    if (indexExpectsCompacted)
                    {
                        // PROMOTE: the committed index describes the compacted layout; the temp is
                        // the correct data. Rename it over the original (atomic where supported).
                        try
                        {
                            AtomicFileOps.ReplaceFile(tmpFile, originalShardPath,
                                warning => Trace.TraceWarning(warning));
                            Trace.TraceWarning(
                                $"[RecoverIfNeeded] Set '{setName}': Promoted compacted shard '{tmpFile}' " +
                                $"to '{originalShardPath}' (committed index expects the compacted size; " +
                                $"a deferred rename did not complete before shutdown).");
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceWarning(
                                $"[RecoverIfNeeded] Set '{setName}': Failed to promote shard .tmp '{tmpFile}': {ex.Message}");
                        }
                    }
                    else
                    {
                        // DELETE: compact did not reach commit — the original is authoritative.
                        try
                        {
                            if (File.Exists(tmpFile))
                                File.Delete(tmpFile);
                            Trace.TraceWarning(
                                $"[RecoverIfNeeded] Set '{setName}': Deleted shard .tmp file '{tmpFile}' " +
                                $"(compaction did not reach commit; original shard is authoritative).");
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceWarning(
                                $"[RecoverIfNeeded] Set '{setName}': Failed to delete shard .tmp file '{tmpFile}': {ex.Message}");
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException) { /* Set directory doesn't exist yet — nothing to recover */ }

            // --- Orphaned tail data detection and truncation ---
            // After dual-header recovery (which happens inside BinaryIndexFile.Open), compute
            // expected end offset for each shard from the committed block index. Truncate shards
            // that are larger than expected, and delete shards with no referenced blocks.
            if (File.Exists(setBasePath))
            {
                truncateOrphanedTailData(setName, setBasePath, setDirectory, setBaseName);
            }

            // --- Stale writer lock detection ---
            // If a writer lock exists for this set but no writer process is active (e.g., the
            // ImageWriter crashed after AtomicCommit but before UnregisterWriter, or a thread
            // was aborted), release the stale lock. Since we are in the open/recovery path,
            // no legitimate writer should be active for this set at this point.
            if (_setUsage.TryGetValue(setName, out SetUsageInfo? usageInfo))
            {
                lock (usageInfo.Sync)
                {
                    if (usageInfo.WriterCount > 0)
                    {
                        Trace.TraceWarning(
                            $"[RecoverIfNeeded] Set '{setName}': Stale writer lock detected (WriterCount={usageInfo.WriterCount}). " +
                            $"No active writer process found. Releasing stale lock.");
                        usageInfo.WriterCount = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Opens the committed index file and computes, for each shard FileId, the expected end
        /// offset (max of offset + size across all committed block entries for that shard). This
        /// is the authoritative "how big should this shard be" derived from the durable index.
        /// Returns null if the index cannot be opened/read (e.g. corrupt or inaccessible).
        /// </summary>
        private Dictionary<int, long>? computeExpectedShardSizes(string indexFilePath)
        {
            BinaryIndexFile? tempIndexFile = null;
            try
            {
                long baseOffset = 0;
                if (detectEmbeddedMode(indexFilePath))
                {
                    (long indexSize, bool _) = readEmbeddedFooter(indexFilePath);
                    baseOffset = new FileInfo(indexFilePath).Length - EmbeddedFooter.FooterSize - indexSize;
                }

                tempIndexFile = BinaryIndexFile.Open(indexFilePath, baseOffset);
                InMemoryBlockIndex blockIndex = tempIndexFile.LoadBlockIndex();
                BlockIndexEntry[] entries = blockIndex.GetEntries();

                Dictionary<int, long> expected = new Dictionary<int, long>();
                foreach (ref BlockIndexEntry entry in entries.AsSpan())
                {
                    long endOffset = entry.Offset + entry.Size;
                    if (!expected.TryGetValue(entry.FileId, out long currentMax) || endOffset > currentMax)
                        expected[entry.FileId] = endOffset;
                }
                return expected;
            }
            catch
            {
                return null;
            }
            finally
            {
                tempIndexFile?.Dispose();
            }
        }

        /// <summary>
        /// Detects and truncates orphaned tail data in shard files.
        /// Opens the index file temporarily to load the committed block index, computes the
        /// expected end offset for each shard, and truncates or deletes shards as needed.
        /// This runs AFTER dual-header recovery but BEFORE the set is made available for reads/writes.
        /// </summary>
        /// <param name="setName">The name of the set.</param>
        /// <param name="indexFilePath">The path to the index file.</param>
        /// <param name="setDirectory">The directory containing shard files.</param>
        /// <param name="setBaseName">The base name of the set (used to construct shard file paths).</param>
        private void truncateOrphanedTailData(string setName, string indexFilePath, string setDirectory, string setBaseName)
        {
            BinaryIndexFile? tempIndexFile = null;
            try
            {
                // Determine base offset for embedded mode
                long baseOffset = 0;
                if (detectEmbeddedMode(indexFilePath))
                {
                    (long indexSize, bool _) = readEmbeddedFooter(indexFilePath);
                    baseOffset = new FileInfo(indexFilePath).Length - EmbeddedFooter.FooterSize - indexSize;
                }

                // Open the index file to load the committed block index
                tempIndexFile = BinaryIndexFile.Open(indexFilePath, baseOffset);
                InMemoryBlockIndex blockIndex = tempIndexFile.LoadBlockIndex();
                BlockIndexEntry[] entries = blockIndex.GetEntries();

                // Compute expected end offset for each shard (max of offset + size for all blocks in that shard)
                Dictionary<int, long> expectedShardSizes = new Dictionary<int, long>();
                foreach (ref BlockIndexEntry entry in entries.AsSpan())
                {
                    long endOffset = entry.Offset + entry.Size;
                    if (!expectedShardSizes.TryGetValue(entry.FileId, out long currentMax) || endOffset > currentMax)
                    {
                        expectedShardSizes[entry.FileId] = endOffset;
                    }
                }

                // Enumerate actual shard files on disk
                try
                {
                    foreach (string shardFile in Directory.EnumerateFiles(setDirectory, $"{setBaseName}_*.nkds"))
                    {
                        // Skip .tmp files (already handled above)
                        if (shardFile.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            continue;

                        int fileId = parseShardFileId(shardFile, setBaseName);
                        if (fileId < 0)
                            continue;

                        long actualSize = new FileInfo(shardFile).Length;

                        if (expectedShardSizes.TryGetValue(fileId, out long expectedSize))
                        {
                            // Shard has referenced blocks — truncate if larger than expected
                            if (actualSize > expectedSize)
                            {
                                Trace.TraceWarning(
                                    $"[RecoverIfNeeded] Set '{setName}': Shard '{shardFile}' has orphaned tail data. " +
                                    $"Expected size: {expectedSize} bytes, actual size: {actualSize} bytes. " +
                                    $"Truncating {actualSize - expectedSize} orphaned bytes.");
                                try
                                {
                                    using (FileStream fs = new FileStream(shardFile, FileMode.Open, FileAccess.Write, FileShare.None))
                                    {
                                        fs.SetLength(expectedSize);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceWarning(
                                        $"[RecoverIfNeeded] Set '{setName}': Failed to truncate shard '{shardFile}': {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            // Shard has no referenced blocks but exists with data — delete it
                            if (actualSize > 0)
                            {
                                Trace.TraceWarning(
                                    $"[RecoverIfNeeded] Set '{setName}': Shard '{shardFile}' exists with {actualSize} bytes " +
                                    $"but no blocks reference it (expected size: 0 bytes). Deleting orphaned shard file.");
                                try
                                {
                                    File.Delete(shardFile);
                                }
                                catch (Exception ex)
                                {
                                    Trace.TraceWarning(
                                        $"[RecoverIfNeeded] Set '{setName}': Failed to delete orphaned shard '{shardFile}': {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (DirectoryNotFoundException) { /* Set directory doesn't exist — nothing to truncate */ }
            }
            catch (Exception ex)
            {
                // If we can't open the index or load the block index, skip orphaned tail detection.
                // The index may be corrupt or the file may not be accessible — this is not fatal,
                // the set will fail to open later with a more specific error.
                Trace.TraceWarning(
                    $"[RecoverIfNeeded] Set '{setName}': Failed to perform orphaned tail detection: {ex.Message}");
            }
            finally
            {
                tempIndexFile?.Dispose();
            }
        }

        /// <summary>
        /// Gets the BinaryIndexFile for the specified set, opening it if necessary.
        /// </summary>
        /// <param name="setName">The set name.</param>
        /// <returns>The BinaryIndexFile instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the set does not exist.</exception>
        private BinaryIndexFile getIndexFile(string setName)
        {
            // Ensure crash recovery has run for this set before first access IF — and only if —
            // there is on-disk evidence of an interrupted operation (leftover ".tmp"/".compact.tmp"
            // files). Previously recovery ran only via openOrCreateSet (the create/write path);
            // read-only access (list/mount/verify) after a crash bypassed it, leaving a
            // post-commit/pre-rename compaction inconsistency unrepaired. We gate on temp-file
            // presence so a CLEAN set is never touched here — running recovery (which can truncate
            // orphaned tail data) unconditionally on every open is both wasteful and unsafe while
            // a set is otherwise healthy or mid-operation.
            if (_recoveredSets.TryAdd(setName, 0) && hasPendingRecoveryArtifacts(setName))
            {
                try { recoverIfNeeded(setName); } catch { /* recovery is best-effort; open will surface real errors */ }
            }

            if (_indexFiles.TryGetValue(setName, out BinaryIndexFile? indexFile))
            {
                // If the file was disposed (stream closed), reopen it
                if (indexFile.IsStreamClosed)
                {
                    _indexFiles.TryRemove(setName, out _);
                    // Fall through to reopen below
                }
                else
                {
                    return indexFile;
                }
            }

            // Try to open the file if it exists on disk
            string filePath = GetIndexFilePath(setName);
            if (File.Exists(filePath))
            {
                indexFile = _indexFiles.GetOrAdd(setName, _ =>
                {
                    // Check if the file is in embedded mode (has footer magic)
                    if (detectEmbeddedMode(filePath))
                    {
                        (long indexSize, bool _) = readEmbeddedFooter(filePath);
                        long boundary = computeShardBoundary(filePath, indexSize);
                        _embeddedMode[setName] = true;
                        _shardBoundary[setName] = boundary;
                        return BinaryIndexFile.Open(filePath, baseOffset: boundary);
                    }
                    // Not in embedded mode — explicitly set _embeddedMode to false
                    // to prevent stale 'true' values from persisting after a set is
                    // confirmed to be in separate mode.
                    _embeddedMode[setName] = false;
                    return BinaryIndexFile.Open(filePath);
                });
                return indexFile;
            }

            throw new InvalidOperationException($"Set '{setName}' does not exist. Call EnsureSetExists first.");
        }

        /// <summary>
        /// Gets or creates a ShardFileManager for the specified set.
        /// </summary>
        private ShardFileManager getShardFileManager(string setName)
        {
            return _shardFileManagers.GetOrAdd(setName, _ =>
            {
                // Try to get info from cache first (works even after disposal)
                if (_infoCache.TryGetValue(setName, out InfoRecord? cachedInfo))
                    return new ShardFileManager(_baseDirectory, setName, cachedInfo.ShardSize, _sourceDirectory, cachedInfo.BlockSize);

                InfoRecord info = GetSetInfo(setName);
                return new ShardFileManager(_baseDirectory, setName, info.ShardSize, _sourceDirectory, info.BlockSize);
            });
        }

        /// <summary>
        /// Builds an InfoRecord from a FileHeader.
        /// </summary>
        private static InfoRecord buildInfoRecord(FileHeader header)
        {
            // Pack version as (major << 16 | minor << 8) to match existing convention
            long version = ((long)header.MajorVersion << 16) | ((long)header.MinorVersion << 8);

            return new InfoRecord
            {
                Version = version,
                ShardSize = header.ShardSize,
                BlockSize = header.BlockSize,
                MaxOffsetBlocks = header.MaxOffsetBlocks
            };
        }

        /// <summary>
        /// Gets or creates a write lock semaphore for the specified set.
        /// </summary>
        private SemaphoreSlim getSetWriteLock(string setName) => _setWriteLocks.GetOrAdd(setName, _ => new SemaphoreSlim(1, 1));

        /// <summary>
        /// Maps an ImageDirectoryEntry to an ImageRecord.
        /// </summary>
        private static ImageRecord mapEntryToImageRecord(string setName, ImageDirectoryEntry entry)
        {
            return new ImageRecord
            {
                Id = entry.ImageId,
                Name = entry.Name,
                Size = entry.Size,
                Crc32 = entry.Crc32,
                XxHash64 = entry.XxHash64,
                SetName = setName,
                System = entry.System,
                Format = entry.Format,
                RollbackFileId = entry.RollbackFileId,
                RollbackOffset = entry.RollbackOffset,
                Removed = entry.Removed
            };
        }

        #endregion
    }

    /// <summary>
    /// A no-op IDisposable used when no actual lock acquisition is needed.
    /// The ShardFileManager already has internal write locking, so the BlockWriter's
    /// acquireShardWrite delegate returns this.
    /// </summary>
    internal sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}