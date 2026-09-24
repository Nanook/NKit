using NKitDataStore.Compression;
using NKitDataStore.Interfaces;
using System.Diagnostics;

namespace NKitDataStore
{
    /// <summary>
    /// Provides a concrete implementation for reading a single, specific image.
    /// 
    /// Thread-Safety: Each ImageReader instance has its own read session with
    /// pooled database connections, making it safe to use from multiple threads.
    /// Different ImageReader instances can be used concurrently without interference.
    /// </summary>
    public class ImageReader : IImageReader, IDisposable
    {
        // Per-image buffer cache owned by the reader to avoid churn from transient streams
        private readonly ImageBufferCache _imageBufferCache;
        /// <summary>
        /// Exposes the per-image buffer cache owned by this reader.
        /// Streams should use this cache instead of creating their own.
        /// </summary>
        internal ImageBufferCache ImageBufferCache => _imageBufferCache;

        internal readonly IDataStoreDataAccess _dataAccess;
        private readonly bool _ownsDataAccess;
        private readonly IBlockCompressor _compressor;
        private readonly object _compressorLock = new object();
        private readonly int _blockSize;
        private readonly byte[] _decompressionBuffer;


        // Cached areas to prevent repeated DB lookups
        private readonly List<AreaRecord> _areas;
        private List<OffsetRecord>? _offsets;
        private bool _offsetsLoaded;
        private readonly object _offsetsLock = new object();
        // Remember which offsetStart groups we've already refreshed from the DB to avoid
        // repeatedly querying the same file-group across multiple calls.
        private readonly HashSet<long> _refreshedOffsetStarts = new HashSet<long>();

        /// <summary>
        /// Gets the metadata record for the image being read.
        /// </summary>
        public ImageRecord Image { get; } // Property to access the image metadata

        /// <summary>
        /// Gets the set metadata from the info table.
        /// Contains schema version, block size, shard configuration, and storage parameters.
        /// </summary>
        public InfoRecord Info { get; }

        /// <summary>
        /// Internal constructor. Instances should be created via IDataStore.OpenImageReader.
        /// </summary>
        /// <param name="dataAccess">The low-level data access layer.</param>
        /// <param name="image">The metadata record of the image to be read.</param>
        /// <param name="compressor">The block compressor for decompression.</param>
        /// <param name="ownsDataAccess">Whether this reader should dispose the data access on disposal.</param>
        internal ImageReader(IDataStoreDataAccess dataAccess, ImageRecord image, IBlockCompressor compressor, bool ownsDataAccess = true, bool createReadSession = true)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
            Image = image ?? throw new ArgumentNullException(nameof(image));
            _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
            _ownsDataAccess = ownsDataAccess;

            // Load set info (single query for all metadata)
            Info = dataAccess.GetSetInfo(image.SetName);

            // Create read session (register reader for resource tracking)
            if (!createReadSession)
            {
            }
            else
            {
                try
                {
                    _dataAccess.RegisterReader(Image.SetName);
                }
                catch
                {
                }
            }

            // Initialize reusable decompression buffer
            _blockSize = Info.BlockSize;
            _decompressionBuffer = new byte[_blockSize];

            // Acquire per-image buffer cache with defaults matching ImageBuilder usage.
            // Using a shared cache per image prevents repeated re-population when clients
            // open/close streams frequently (e.g., hex editors that reopen handles).
            _imageBufferCache = ImageBufferCacheManager.Acquire(Image.Id, bufferSize: 0x200000, maxCachedBuffers: 0x10);

            // Load and cache all areas ordered by offset, populating the Index property
            _areas = createReadSession
                ? dataAccess.GetAreasForImage(image.SetName, image.Id)
                    .OrderBy(a => a.Offset)
                    .Select((area, index) =>
                    {
                        area.Index = index;
                        area.Id = index + 1; // Assign unique IDs (1-based) for buffer cache keying
                        return area;
                    })
                    .ToList()
                : new List<AreaRecord>();

            // Offsets are loaded lazily on first access (block map section is heavier than metadata)
            if (!createReadSession)
            {
                _offsets = new List<OffsetRecord>();
                _offsetsLoaded = true;
            }

        }

        /// <summary>
        /// Ensures the offset records are loaded from the data access layer.
        /// Lazy-loads on first access to avoid reading the block map section when only areas are needed.
        /// </summary>
        private List<OffsetRecord> EnsureOffsetsLoaded()
        {
            if (!_offsetsLoaded)
            {
                lock (_offsetsLock)
                {
                    if (!_offsetsLoaded)
                    {
                        _offsets = _dataAccess.GetOffsetsForImage(Image.SetName, Image.Id)
                            .OrderBy(o => o.Offset)
                            .ToList();
                        _offsetsLoaded = true;
                    }
                }
            }
            return _offsets!;
        }

        /// <summary>
        /// Opens a readable stream for a specific logical group within the image.
        /// The stream reconstructs data from all offset records with the matching offsetStart value.
        /// 
        /// For files that span multiple offsets, the stream concatenates all offset records
        /// into a continuous view (no gaps). Stream position 0 = start of first offset's data,
        /// position N = start of second offset's data (where N = size of first offset), etc.
        /// </summary>
        /// <param name="offsetStart">The starting offset that identifies the group.</param>
        /// <returns>A readable, seekable stream representing the grouped data.</returns>
        public Stream OpenStream(long offsetStart)
        {
            // Get all offsets for this group
            List<OffsetRecord> groupOffsets = GetOffsets(offsetStart)
                .Where(o => o.Offset >= 0) // Exclude auxiliary files
                .Where(o => o.Type != BlockType.BlockPadding) // Exclude internal padding
                .OrderBy(o => o.Offset)
                .ToList();

            if (groupOffsets.Count == 0)
                throw new ArgumentException($"No offset records found with offsetStart = {offsetStart}", nameof(offsetStart));

            // Calculate total size: sum of all offset sizes (continuous file data, no gaps)
            long totalSize = groupOffsets.Sum(o => o.Size);

            // Create a grouped stream that concatenates all offsets without gaps
            return new ImageReadStream(this, totalSize, _blockSize, groupOffsets, isGroupedStream: true);
        }

        /// <summary>
        /// Opens a readable stream that reconstructs the strided format from stored clean data.
        /// The stream reads clean data and re-applies padding/hashes according to the stride pattern.
        /// </summary>
        /// <param name="stride">The stride pattern to apply when reading data.</param>
        /// <param name="offsetStart">The starting offset that identifies which file group to read with stride applied.</param>
        /// <returns>A readable stream with strided format (clean data + padding/hashes).</returns>
        public Stream OpenStream(DataStride stride, long offsetStart)
        {
            if (stride == null)
                throw new ArgumentNullException(nameof(stride));

            // Validate stride parameters
            if (stride.SourceBlockSize <= 0)
                throw new ArgumentException("SourceBlockSize must be greater than zero", nameof(stride));
            if (stride.DataOffset < 0 || stride.DataOffset >= stride.SourceBlockSize)
                throw new ArgumentOutOfRangeException(nameof(stride), "DataOffset must be within SourceBlockSize");
            if (stride.DataLength <= 0 || stride.DataOffset + stride.DataLength > stride.SourceBlockSize)
                throw new ArgumentOutOfRangeException(nameof(stride), "DataOffset + DataLength must be within SourceBlockSize");

            // Get the underlying clean data stream for the specific file group
            Stream cleanDataStream = OpenStream(offsetStart);

            // Wrap in stride reconstruction stream. The StridedReadStream implementation
            // intentionally does NOT prepend an initial padding block; position 0 maps to
            // the first byte of the first clean-data block. This keeps offset calculations
            // simple and aligned with ImageBuilderWiiDataStoreStream expectations.
            return new StridedReadStream(cleanDataStream, stride, ownsStream: true);
        }

        /// <summary>
        /// Retrieves all offset records that construct the complete image.
        /// </summary>
        public IEnumerable<OffsetRecord> GetOffsets() => EnsureOffsetsLoaded();

        /// <summary>
        /// Retrieves all offset records that belong to a specific logical group within the image.
        /// All offset records with the same offsetStart value belong to the same group.
        /// </summary>
        /// <param name="offsetStart">The starting offset that identifies the group.</param>
        /// <returns>All offset records for the specified group, ordered by offset.</returns>
        public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => EnsureOffsetsLoaded().Where(o => o.OffsetStart == offsetStart).OrderBy(o => o.Offset);

        /// <summary>
        /// Retrieves offset records that fall within a specified byte range of the image.
        /// </summary>
        public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length)
        {
            long end = startOffset + length;
            return EnsureOffsetsLoaded().Where(o => o.Offset < end && (o.Offset + o.Size) > startOffset).OrderBy(o => o.Offset);
        }

        /// <summary>
        /// Retrieves a data block as a byte array by its unique key.
        /// This method handles decompression automatically.
        /// Returns a copy of the decompressed data.
        /// Uses the read session for connection management (session-based) or falls back to legacy DataAccess method.
        /// Connections are acquired and returned immediately - not held for the session lifetime.
        /// </summary>
        public BlockRecord? GetBlock(BlockKey key)
        {
            CompressionType compressionType;
            byte[]? compressedData = null;

            // Read block data via DataAccess
            (compressionType, compressedData) = _dataAccess.GetBlockData(Image.SetName, key);
            if (compressedData == null || compressedData.Length == 0)
            {
                // The read returned null — this could be because the block doesn't exist,
                // or because an IOException occurred due to an invalidated file handle
                // (shard replaced during compaction). If we can resolve the block's shard
                // location, invalidate the cached stream and retry once.
                if (_dataAccess is Binary.BinaryDataStoreDataAccess binaryAccess)
                {
                    (int fileId, long offset, int size)[] locations = binaryAccess.GetBlockLocations(Image.SetName, new[] { key });
                    if (locations != null && locations.Length > 0 && locations[0].size > 0)
                    {
                        Trace.TraceWarning(
                            $"ImageReader: GetBlock returned null for block in shard {locations[0].fileId}. " +
                            $"Invalidating cached handle and retrying.");
                        binaryAccess.InvalidateShardReadStream(Image.SetName, locations[0].fileId);
                        (compressionType, compressedData) = _dataAccess.GetBlockData(Image.SetName, key);
                    }
                }

                if (compressedData == null || compressedData.Length == 0)
                    return null;
            }

            int expectedBlockLength;
            if (tryGetExpectedBlockLength(key, out int resolvedBlockLength))
                expectedBlockLength = resolvedBlockLength;
            else
                expectedBlockLength = Math.Min(_blockSize, compressedData.Length);

            compressionType = compressedData.Length == expectedBlockLength
                ? CompressionType.None
                : CompressionType.Zstd;

            byte[] decompressedData;
            lock (_compressorLock)
            {
                int actualSize = _compressor.Decompress(compressedData, 0, compressedData.Length, expectedBlockLength, _decompressionBuffer, 0);
                decompressedData = new byte[actualSize];
                Array.Copy(_decompressionBuffer, 0, decompressedData, 0, actualSize);
            }

            return new BlockRecord(key, compressionType, decompressedData);
        }

        /// <summary>
        /// Returns raw (potentially compressed) block bytes via the DataAccess layer.
        /// Used by ImageReadStream to avoid per-block round-trips.
        /// If the read fails due to an invalidated shard file handle (e.g., shard replaced
        /// during compaction), the cached handle is invalidated and the read is retried once.
        /// </summary>
        internal byte[]? GetRawBlockData(BlockKey key)
        {
            (_, byte[]? data) = _dataAccess.GetBlockData(Image.SetName, key);
            if (data != null)
                return data;

            // The read returned null — this could be because the block doesn't exist,
            // or because an IOException occurred due to an invalidated file handle.
            // If we can resolve the block's shard location, invalidate the cached stream
            // and retry once.
            if (_dataAccess is Binary.BinaryDataStoreDataAccess binaryAccess)
            {
                (int fileId, long offset, int size)[] locations = binaryAccess.GetBlockLocations(Image.SetName, new[] { key });
                if (locations != null && locations.Length > 0 && locations[0].size > 0)
                {
                    Trace.TraceWarning(
                        $"ImageReader: GetRawBlockData returned null for block in shard {locations[0].fileId}. " +
                        $"Invalidating cached handle and retrying.");
                    binaryAccess.InvalidateShardReadStream(Image.SetName, locations[0].fileId);
                    (_, data) = _dataAccess.GetBlockData(Image.SetName, key);
                }
            }

            return data;
        }

        /// <summary>
        /// Decompresses a block from rawData[rawOffset..rawOffset+rawLength] into destBuffer,
        /// or returns a zero-copy slice of rawData when no decompression is needed.
        /// Each ImageReadStream must pass its own dedicated destBuffer so concurrent streams
        /// do not share or corrupt each other's decompression workspace.
        /// </summary>
        internal (byte[] buffer, int offset, int length) GetBlockDataInternal(int expectedBlockLength, byte[] rawData, int rawOffset, int rawLength, byte[] destBuffer)
        {
            if (rawLength == 0)
                return (Array.Empty<byte>(), 0, 0);

            if (rawLength == expectedBlockLength)
                return (rawData, rawOffset, rawLength); // uncompressed - zero-copy slice into rawData

            int actualDecompressedSize;
            lock (_compressorLock)
            {
                actualDecompressedSize = _compressor.Decompress(rawData, rawOffset, rawLength, expectedBlockLength, destBuffer, 0);
            }
            return (destBuffer, 0, actualDecompressedSize);
        }

        /// <summary>
        /// Decompresses rawData[rawOffset..rawOffset+rawLength] directly into dest[destOffset..].
        /// Used by ImageReadStream when reading from the start of a block into a caller-supplied
        /// output buffer that is large enough to hold the full decompressed block — eliminates
        /// the _decompressionBuffer intermediate and the subsequent Array.Copy.
        /// </summary>
        internal int DecompressBlockDirect(byte[] rawData, int rawOffset, int rawLength, int expectedBlockLength, byte[] dest, int destOffset)
        {
            lock (_compressorLock)
            {
                return _compressor.Decompress(rawData, rawOffset, rawLength, expectedBlockLength, dest, destOffset);
            }
        }

        internal const int _MaxReadAheadBlocks = 64;

        /// <summary>
        /// Detects a run of shard-adjacent blocks starting at startBlockIndex and reads them all
        /// in a single I/O call. Because dedupe appends each image's unique blocks contiguously,
        /// consecutive blocks in the same OffsetRecord are typically physically adjacent in the
        /// shard, so one large read replaces N individual reads.
        ///
        /// Returns default when the run length is ≤ 1 — caller should fall back to GetRawBlockData.
        /// </summary>
        internal (byte[]? rawData, int runLength, int[] blockOffsets, int[] blockSizes) ReadBlockRun(
            OffsetRecord offsetRecord, int startBlockIndex, int maxBlocks = _MaxReadAheadBlocks)
        {
            if (_dataAccess is not Binary.BinaryDataStoreDataAccess binaryAccess)
                return default;

            // Determine how many blocks remain in this offset record from startBlockIndex
            int totalBlocks = offsetRecord.BlockCount;
            int blocksRemaining = totalBlocks - startBlockIndex;
            if (blocksRemaining <= 1)
                return default;

            int runCount = Math.Min(blocksRemaining, maxBlocks);

            // Collect block keys for the candidate run
            BlockKey[] keys = new BlockKey[runCount];
            for (int i = 0; i < runCount; i++)
                keys[i] = offsetRecord.GetBlockAt(startBlockIndex + i);

            // Resolve shard locations for all blocks in one pass (in-memory index lookups)
            (int fileId, long offset, int size)[] locations = binaryAccess.GetBlockLocations(Image.SetName, keys);

            // Find the longest contiguous run: same fileId, each block starts immediately
            // after the previous one ends (offset[i+1] == offset[i] + size[i])
            int runLength = 0;
            if (locations[0].size <= 0)
                return default;

            int runFileId = locations[0].fileId;
            long runStartOffset = locations[0].offset;
            long expectedNext = locations[0].offset + locations[0].size;
            runLength = 1;

            for (int i = 1; i < runCount; i++)
            {
                (int fileId, long offset, int size) loc = locations[i];
                if (loc.size <= 0 || loc.fileId != runFileId || loc.offset != expectedNext)
                    break;

                expectedNext = loc.offset + loc.size;
                runLength++;
            }

            if (runLength <= 1)
                return default;

            // Calculate total contiguous range size
            int totalSize = 0;
            int[] blockOffsets = new int[runLength];
            int[] blockSizes = new int[runLength];
            for (int i = 0; i < runLength; i++)
            {
                blockOffsets[i] = totalSize;
                blockSizes[i] = locations[i].size;
                totalSize += locations[i].size;
            }

            // Single I/O read for the entire contiguous range
            byte[]? rawData = binaryAccess.ReadShardRange(Image.SetName, runFileId, runStartOffset, totalSize);
            if (rawData == null || rawData.Length != totalSize)
            {
                // The read may have failed because the shard file handle was invalidated
                // (e.g., shard replaced during compaction). Invalidate the cached handle and retry once.
                Trace.TraceWarning(
                    $"ImageReader: ReadBlockRun failed for shard {runFileId} at offset {runStartOffset}, " +
                    $"length {totalSize}. Invalidating cached handle and retrying.");
                binaryAccess.InvalidateShardReadStream(Image.SetName, runFileId);
                rawData = binaryAccess.ReadShardRange(Image.SetName, runFileId, runStartOffset, totalSize);
                if (rawData == null || rawData.Length != totalSize)
                    return default;
            }

            return (rawData, runLength, blockOffsets, blockSizes);
        }

        private bool tryGetExpectedBlockLength(BlockKey key, out int expectedBlockLength)
        {
            // Iterate over cached offsets; if blocks are missing for an offset, fetch the per-file offsets
            List<OffsetRecord> allOffsets = EnsureOffsetsLoaded().ToList();
            //foreach (OffsetRecord offset in allOffsets)
            // To avoid repeatedly querying the DB for the same file group within one scan,
            // remember which offsetStart groups we've already refreshed.
            //var allOffsets = GetOffsets().ToList();
            HashSet<long> refreshedOffsetStarts = new HashSet<long>();
            foreach (OffsetRecord offset in allOffsets)
            {
                if (offset.Blocks == null || offset.Blocks.Count == 0)
                {
                    // If we've already tried refreshing this offsetStart in this invocation,
                    // skip the repeated DB lookup to avoid tight loops that repeatedly query
                    // the same data without progress.
                    if (refreshedOffsetStarts.Contains(offset.OffsetStart))
                        continue;
                    try
                    {
                        // If we've already refreshed this offsetStart previously for this ImageReader,
                        // skip the DB call to avoid repeated work across multiple GetBlock invocations.
                        lock (_offsetsLock)
                        {
                            if (_refreshedOffsetStarts.Contains(offset.OffsetStart))
                                continue;
                            // mark early to avoid races where multiple threads attempt refresh
                            _refreshedOffsetStarts.Add(offset.OffsetStart);
                        }

                        // Attempt to fetch the detailed offsets for this offset's group which should include block lists
                        List<OffsetRecord>? fresh = _dataAccess.GetOffsetsForFile(Image.SetName, Image.Id, offset.OffsetStart)?.ToList();
                        if (fresh != null && fresh.Count > 0)
                        {
                            // Replace entries in the master cache with fresh versions
                            lock (_offsetsLock)
                            {
                                foreach (OffsetRecord fo in fresh)
                                {
                                    int idx = _offsets!.FindIndex(o => o.Offset == fo.Offset && o.OffsetStart == fo.OffsetStart);
                                    if (idx >= 0)
                                        _offsets[idx] = fo;
                                    else
                                        _offsets.Add(fo);
                                }
                                _offsets!.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                            }

                            // Use the updated offset reference for block lookup
                            OffsetRecord? updated = fresh.FirstOrDefault(o => o.Offset == offset.Offset && o.OffsetStart == offset.OffsetStart);
                            if (updated != null)
                            {
                                int foundBlockIndex = updated.Blocks?.IndexOf(key) ?? -1;
                                if (foundBlockIndex >= 0)
                                {
                                    long foundRemaining = updated.Size - ((long)foundBlockIndex * _blockSize);
                                    expectedBlockLength = (int)Math.Min(_blockSize, foundRemaining);
                                    return expectedBlockLength > 0;
                                }
                            }
                        }
                    }
                    catch { }

                    // continue to next offset if fresh lookup failed
                    continue;
                }

                int blockIndex = offset.Blocks.IndexOf(key);
                if (blockIndex < 0)
                    continue;

                long remaining = offset.Size - ((long)blockIndex * _blockSize);
                expectedBlockLength = (int)Math.Min(_blockSize, remaining);
                return expectedBlockLength > 0;
            }

            expectedBlockLength = 0;
            return false;
        }

        /// <summary>
        /// Opens a readable stream for a data block's content by its unique key.
        /// The returned stream provides decompressed data.
        /// </summary>
        public Stream? OpenBlockStream(BlockKey key)
        {
            BlockRecord? blockRecord = GetBlock(key);
            if (blockRecord == null)
                return null;

            return new MemoryStream(blockRecord.Data, false);
        }

        /// <summary>
        /// Retrieves all area records for this image.
        /// Areas define logical regions within the disc image (e.g., partitions, headers).
        /// Returns cached areas with Index property pre-populated, ordered by offset.
        /// </summary>
        public IEnumerable<AreaRecord> GetAreas() => _areas;

        /// <summary>
        /// Retrieves all area records for this image with navigation information.
        /// Each area includes its index and total count for easy prev/next navigation.
        /// Returns cached areas ordered by offset.
        /// </summary>
        /// <returns>Area records with their index and total count.</returns>
        public IEnumerable<(AreaRecord Area, int Index, int TotalCount)> GetAreasWithIndex()
        {
            int totalCount = _areas.Count;
            return _areas.Select(area => (area, area.Index, totalCount));
        }

        public byte[]? ReadFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            FileRecord? record = _dataAccess.GetFile(Image.SetName, Image.Id, name);
            if (record == null)
                return null;

            return _dataAccess.ReadFileData(Image.SetName, record);
        }

        public IEnumerable<FileRecord> ListFiles() => _dataAccess.GetFilesForImage(Image.SetName, Image.Id);

        /// <summary>
        /// Disposes of resources held by this ImageReader.
        /// Disposal is synchronous and deterministic:
        /// 1. Disposes read session (returns all borrowed connections to pool)
        /// 2. Disposes compressor
        /// 3. Releases per-image cache
        /// 4. Disposes DataAccess if owned
        /// </summary>
        public void Dispose()
        {
            if (DataStore.MountDebug)
                Console.WriteLine($"[MountDebug] ImageReader DISPOSE: set={Image?.SetName} id={Image?.Id} name={Image?.Name}");

            // Unregister reader
            try
            {
                if (_dataAccess != null && Image != null)
                    _dataAccess.UnregisterReader(Image.SetName);
            }
            catch { }

            // Ensure any background compression tasks for this set complete before disposing compressor
            if (_dataAccess != null && Image != null)
                try { _dataAccess?.WaitForCompressionTasks(Image.SetName, 30000); } catch { }

            _compressor?.Dispose();

            // Release per-image cache owned by this reader
            if (_dataAccess != null && Image != null)
                try { ImageBufferCacheManager.Release(Image.Id); } catch { }

            if (_ownsDataAccess)
                (_dataAccess as IDisposable)?.Dispose();
        }
    }
}