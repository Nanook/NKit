using NKitDataStore.Binary.Serialization;
using System.Buffers.Binary;
using System.Diagnostics;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Manages a single .nkds binary index file: reading/writing headers,
    /// image sections, block index, and image directory.
    /// </summary>
    internal class BinaryIndexFile : IDisposable
    {
        /// <summary>
        /// Current structure version for Block_Index_Delta serialization.
        /// </summary>
        private const ushort CurrentDeltaStructureVersion = 1;

        private readonly string _filePath;
        private readonly long _baseOffset;
        private FileStream _stream;
        private FileHeader _primaryHeader;
        private FileHeader _secondaryHeader;
        private ImageDirectory _directory;
        private InMemoryBlockIndex? _blockIndex; // Loaded only during write operations
        private readonly object _readLock = new object();

        /// <summary>
        /// Pending header accumulates changes during append operations.
        /// It is committed atomically via <see cref="AtomicCommit"/>.
        /// </summary>
        private FileHeader _pendingHeader;

        /// <summary>
        /// Pending directory that will become the committed directory after AtomicCommit.
        /// </summary>
        private ImageDirectory? _pendingDirectory;

        private BinaryIndexFile(string filePath, FileStream stream, FileHeader header, ImageDirectory directory, long baseOffset = 0)
        {
            _filePath = filePath;
            _baseOffset = baseOffset;
            _stream = stream;
            _primaryHeader = header;
            _secondaryHeader = header;
            _pendingHeader = header;
            _directory = directory;
        }

        /// <summary>
        /// Default directory region capacity for newly created files (4096 bytes).
        /// Provides space for ~50–100 images compressed before overflow.
        /// </summary>
        private const int DefaultDirectoryRegionCapacity = 4096;

        /// <summary>
        /// Offset where the directory region begins (immediately after both 256-byte headers).
        /// </summary>
        private const long DirectoryRegionStartOffset = FileHeader.HeaderSize * 2; // 0x200

        /// <summary>
        /// Creates a new binary index file with an initialized Header, Secondary_Header,
        /// empty compressed Image_Directory in the reserved directory region,
        /// and empty Block_Index at the start of the data region.
        /// 
        /// File layout:
        ///   0x000: Primary Header (256 bytes)
        ///   0x100: Secondary Header (256 bytes)
        ///   0x200: Directory Region (DirectoryRegionCapacity bytes, default 4096)
        ///   0x1200: Data Region start (Block_Index, image sections, deltas)
        /// </summary>
        /// <param name="path">The file path to create.</param>
        /// <param name="shardSize">Shard rotation size (0 = unlimited single shard).</param>
        /// <param name="blockSize">Block size in bytes.</param>
        /// <param name="maxOffsetBlocks">Max block keys per offset record.</param>
        /// <param name="baseOffset">Base offset within the file where the index starts (0 for separate mode).</param>
        /// <returns>An opened BinaryIndexFile instance.</returns>
        public static BinaryIndexFile Create(string path, long shardSize, int blockSize, int maxOffsetBlocks, long baseOffset = 0)
        {
            // Create the file stream
            FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            try
            {
                // Seek to baseOffset position before writing
                if (baseOffset > 0)
                {
                    stream.Position = baseOffset;
                }

                // Write placeholder headers (2 x 256 bytes)
                byte[] headerBuffer = new byte[FileHeader.HeaderSize];
                stream.Write(headerBuffer, 0, FileHeader.HeaderSize); // Primary header placeholder at 0x000
                stream.Write(headerBuffer, 0, FileHeader.HeaderSize); // Secondary header placeholder at 0x100

                // Write empty compressed directory at 0x200 (directory region start)
                byte[] emptyDirectorySerialized = new ImageDirectory().Serialize();
                byte[] compressedDirectory = ImageDirectorySerializer.CompressDirectory(emptyDirectorySerialized);
                int imageDirectoryUncompressedSize = emptyDirectorySerialized.Length;

                // Write compressed directory into the reserved region
                long imageDirectoryOffset = DirectoryRegionStartOffset; // 0x200
                stream.Write(compressedDirectory, 0, compressedDirectory.Length);

                // Pad the rest of the directory region with zeros
                int directoryRegionCapacity = DefaultDirectoryRegionCapacity;
                int paddingSize = directoryRegionCapacity - compressedDirectory.Length;
                if (paddingSize > 0)
                {
                    byte[] padding = new byte[paddingSize];
                    stream.Write(padding, 0, padding.Length);
                }

                // Data region starts at 0x200 + DirectoryRegionCapacity = 0x1200
                long dataStartOffset = DirectoryRegionStartOffset + directoryRegionCapacity;

                // Write empty Block_Index at data start (version + 0 sections = 6 bytes)
                long blockIndexOffset = dataStartOffset;
                byte[] emptyBlockIndex = new InMemoryBlockIndex().Serialize();
                stream.Write(emptyBlockIndex, 0, emptyBlockIndex.Length);

                long fileEndOffset = stream.Position - baseOffset;

                // Build the header with correct pointers (relative to index start)
                FileHeader header = new FileHeader
                {
                    Magic = FileHeader.MagicBytes,
                    MajorVersion = FileHeader.CurrentMajorVersion,
                    MinorVersion = FileHeader.CurrentMinorVersion,
                    ImageDirectoryOffset = imageDirectoryOffset,
                    ImageDirectorySize = compressedDirectory.Length,
                    ImageDirectoryUncompressedSize = imageDirectoryUncompressedSize,
                    BlockIndexOffset = blockIndexOffset,
                    BlockIndexSize = emptyBlockIndex.Length,
                    BlockIndexDeltaHeadOffset = 0,
                    BlockIndexDeltaCount = 0,
                    FileEndOffset = fileEndOffset,
                    ShardSize = shardSize,
                    BlockSize = blockSize,
                    MaxOffsetBlocks = maxOffsetBlocks,
                    ImageCount = 0,
                    DirectoryRegionCapacity = directoryRegionCapacity
                };

                // Write both headers with correct pointers
                byte[] headerArray = new byte[FileHeader.HeaderSize];
                FileHeaderSerializer.Write(headerArray, header);

                // Write primary header at baseOffset + 0x000
                stream.Position = baseOffset;
                stream.Write(headerArray, 0, FileHeader.HeaderSize);

                // Write secondary header at baseOffset + 0x100
                stream.Write(headerArray, 0, FileHeader.HeaderSize);

                stream.Flush(flushToDisk: true);

                BinaryIndexFile file = new BinaryIndexFile(path, stream, header, new ImageDirectory(), baseOffset);
                return file;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Opens an existing binary index file, validates the header, and loads the Image_Directory.
        /// Falls back to the secondary header if the primary header is corrupted.
        /// </summary>
        /// <param name="path">The file path to open.</param>
        /// <param name="baseOffset">Base offset within the file where the index starts (0 for separate mode).</param>
        /// <returns>An opened BinaryIndexFile instance.</returns>
        /// <exception cref="InvalidDataException">Thrown if the file is too small or both headers are corrupted.</exception>
        /// <exception cref="NotSupportedException">Thrown if the header version is not supported.</exception>
        public static BinaryIndexFile Open(string path, long baseOffset = 0)
        {
            FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            try
            {
                // Check file size >= baseOffset + 512 (two 256-byte headers)
                if (stream.Length < baseOffset + (FileHeader.HeaderSize * 2))
                {
                    throw new InvalidDataException(
                        $"Binary index file is too small ({stream.Length} bytes). " +
                        $"Minimum size is {baseOffset + (FileHeader.HeaderSize * 2)} bytes (BaseOffset + Header + Secondary_Header).");
                }

                // Read both headers
                byte[] headerBytes = new byte[FileHeader.HeaderSize];
                FileHeader? validHeader = null;
                bool usedSecondaryHeader = false;

                // Try primary header (at baseOffset + 0)
                stream.Position = baseOffset;
                ReadExactly(stream, headerBytes, 0, FileHeader.HeaderSize);

                try
                {
                    FileHeader primary = FileHeaderSerializer.Read(headerBytes);
                    if (IsHeaderValid(primary, stream.Length - baseOffset))
                    {
                        validHeader = primary;
                    }
                }
                catch (InvalidDataException)
                {
                    // Primary header checksum failed — try secondary
                }

                // If primary failed, try secondary header (at baseOffset + HeaderSize)
                if (validHeader == null)
                {
                    stream.Position = baseOffset + FileHeader.HeaderSize;
                    ReadExactly(stream, headerBytes, 0, FileHeader.HeaderSize);

                    try
                    {
                        FileHeader secondary = FileHeaderSerializer.Read(headerBytes);
                        if (IsHeaderValid(secondary, stream.Length - baseOffset))
                        {
                            validHeader = secondary;
                            usedSecondaryHeader = true;
                        }
                    }
                    catch (InvalidDataException)
                    {
                        // Secondary header checksum also failed
                    }
                }

                if (validHeader == null)
                {
                    throw new InvalidDataException(
                        "Binary index file is corrupted: both primary and secondary headers failed validation.");
                }

                if (usedSecondaryHeader)
                {
                    Trace.TraceWarning(
                        "BinaryIndexFile.Open: Primary header is invalid for '{0}'. " +
                        "Using secondary header as authoritative. " +
                        "This indicates a crash occurred during AtomicCommit (between writing secondary and primary headers).",
                        path);
                }

                FileHeader header = validHeader.Value;

                // Validate version compatibility
                if (header.MajorVersion != FileHeader.CurrentMajorVersion)
                {
                    throw new NotSupportedException(
                        $"Binary index file major version {header.MajorVersion} is not supported. " +
                        $"Expected major version {FileHeader.CurrentMajorVersion}.");
                }

                if (header.MinorVersion > FileHeader.CurrentMinorVersion)
                {
                    throw new NotSupportedException(
                        $"Binary index file minor version {header.MinorVersion} is not supported. " +
                        $"Maximum supported minor version is {FileHeader.CurrentMinorVersion}.");
                }

                // Load the Image_Directory from the offset in the header (adjusted by baseOffset)
                stream.Position = baseOffset + header.ImageDirectoryOffset;
                byte[] directoryBytes = new byte[header.ImageDirectorySize];
                ReadExactly(stream, directoryBytes, 0, header.ImageDirectorySize);

                ImageDirectory directory;
                if (header.ImageDirectoryUncompressedSize > 0)
                {
                    // Compressed directory: decompress using known uncompressed size
                    byte[] decompressed = ImageDirectorySerializer.DecompressDirectory(directoryBytes, header.ImageDirectoryUncompressedSize);
                    directory = ImageDirectory.Deserialize(decompressed);
                }
                else
                {
                    // Uncompressed directory (legacy or empty)
                    directory = ImageDirectory.Deserialize(directoryBytes);
                }

                return new BinaryIndexFile(path, stream, header, directory, baseOffset);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Returns the loaded Image_Directory.
        /// </summary>
        public ImageDirectory GetDirectory() => _directory;

        /// <summary>
        /// Reads and decompresses the Image_Metadata_Section for the specified image.
        /// </summary>
        /// <param name="imageId">The image ID to read metadata for.</param>
        /// <returns>A tuple of (AreaRecords, FileRecords) from the decompressed metadata section.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the image ID is not in the directory.</exception>
        public (List<AreaRecord> Areas, List<FileRecord> Files) ReadImageMetadata(long imageId)
        {
            ImageDirectoryEntry? entry = _directory.GetEntry(imageId);
            if (entry == null)
                throw new KeyNotFoundException($"Image ID {imageId} not found in the directory.");

            byte[] sectionBytes = new byte[entry.MetadataSectionCompressedSize];

#if NET6_0_OR_GREATER
            // Position-independent read — thread-safe without locking
            int totalRead = 0;
            while (totalRead < sectionBytes.Length)
            {
                int read = RandomAccess.Read(_stream.SafeFileHandle, sectionBytes.AsSpan(totalRead), _baseOffset + entry.MetadataSectionOffset + totalRead);
                if (read == 0) throw new EndOfStreamException($"Unexpected end of stream reading metadata section for image {imageId}. Expected {sectionBytes.Length} bytes but only read {totalRead}.");
                totalRead += read;
            }
#else
            // netstandard2.1: serialize seek+read per file
            lock (_readLock)
            {
                _stream.Position = _baseOffset + entry.MetadataSectionOffset;
                ReadExactly(_stream, sectionBytes, 0, entry.MetadataSectionCompressedSize);
            }
#endif

            // Deserialize (raw zstd compressed data, no size prefix)
            return ImageMetadataSectionSerializer.Deserialize(sectionBytes, entry.MetadataSectionUncompressedSize);
        }

        /// <summary>
        /// Reads and decompresses the Image_BlockMap_Section for the specified image.
        /// </summary>
        /// <param name="imageId">The image ID to read the block map for.</param>
        /// <returns>A tuple of (OffsetRecords, BlockLocations dictionary) from the decompressed block map section.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the image ID is not in the directory.</exception>
        public (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) ReadImageBlockMap(long imageId)
        {
            ImageDirectoryEntry? entry = _directory.GetEntry(imageId);
            if (entry == null)
                throw new KeyNotFoundException($"Image ID {imageId} not found in the directory.");

            byte[] sectionBytes = new byte[entry.BlockMapSectionCompressedSize];

#if NET6_0_OR_GREATER
            // Position-independent read — thread-safe without locking
            int totalRead = 0;
            while (totalRead < sectionBytes.Length)
            {
                int read = RandomAccess.Read(_stream.SafeFileHandle, sectionBytes.AsSpan(totalRead), _baseOffset + entry.BlockMapSectionOffset + totalRead);
                if (read == 0) throw new EndOfStreamException($"Unexpected end of stream reading block map section for image {imageId}. Expected {sectionBytes.Length} bytes but only read {totalRead}.");
                totalRead += read;
            }
#else
            // netstandard2.1: serialize seek+read per file
            lock (_readLock)
            {
                _stream.Position = _baseOffset + entry.BlockMapSectionOffset;
                ReadExactly(_stream, sectionBytes, 0, entry.BlockMapSectionCompressedSize);
            }
#endif

            // Deserialize (raw zstd compressed data, no size prefix)
            return ImageBlockMapSectionSerializer.Deserialize(sectionBytes, entry.BlockMapSectionUncompressedSize);
        }

        /// <summary>
        /// Loads the main Block_Index and all Block_Index_Deltas, merging them into a single InMemoryBlockIndex.
        /// Caches the result — subsequent calls return the cached instance.
        /// <summary>
        /// Tries to look up a block in the already-cached block index without triggering a load.
        /// Returns false if the block index hasn't been loaded yet (no I/O performed).
        /// </summary>
        internal bool TryGetBlockFromCachedIndex(BlockKey key, out int fileId, out long offset, out int size)
        {
            fileId = 0;
            offset = 0;
            size = 0;

            if (_blockIndex == null)
                return false;

            return _blockIndex.TryGetBlock(key, out fileId, out offset, out size);
        }

        /// <summary>
        /// Loads the main Block_Index and all Block_Index_Deltas, merging them into a single InMemoryBlockIndex.
        /// Caches the result — subsequent calls return the cached instance.
        /// </summary>
        /// <returns>The merged InMemoryBlockIndex.</returns>
        public InMemoryBlockIndex LoadBlockIndex()
        {
            // Return cached block index if already loaded
            if (_blockIndex != null)
                return _blockIndex;

            // Read the main Block_Index
            byte[] mainData = new byte[_primaryHeader.BlockIndexSize];

#if NET6_0_OR_GREATER
            // Position-independent read — thread-safe without locking
            {
                int totalRead = 0;
                while (totalRead < mainData.Length)
                {
                    int read = RandomAccess.Read(_stream.SafeFileHandle, mainData.AsSpan(totalRead), _baseOffset + _primaryHeader.BlockIndexOffset + totalRead);
                    if (read == 0) throw new EndOfStreamException($"Unexpected end of stream reading block index. Expected {mainData.Length} bytes but only read {totalRead}.");
                    totalRead += read;
                }
            }
#else
            lock (_readLock)
            {
                _stream.Position = _baseOffset + _primaryHeader.BlockIndexOffset;
                ReadExactly(_stream, mainData, 0, (int)_primaryHeader.BlockIndexSize);
            }
#endif

            // Deserialize the main index
            InMemoryBlockIndex index = InMemoryBlockIndex.Deserialize(mainData);

            // If there are deltas, follow the linked list and merge each one
            if (_primaryHeader.BlockIndexDeltaCount > 0 && _primaryHeader.BlockIndexDeltaHeadOffset != 0)
            {
                long nextDeltaOffset = _primaryHeader.BlockIndexDeltaHeadOffset;

                while (nextDeltaOffset != 0)
                {
                    // Read delta header: version(2) + count(4) + nextOffset(8) + compressedSize(4) + uncompressedSize(4) = 22 bytes
                    byte[] deltaHeader = new byte[22];

#if NET6_0_OR_GREATER
                    {
                        int totalRead = 0;
                        while (totalRead < 22)
                        {
                            int read = RandomAccess.Read(_stream.SafeFileHandle, deltaHeader.AsSpan(totalRead), _baseOffset + nextDeltaOffset + totalRead);
                            if (read == 0) throw new EndOfStreamException("Unexpected end of stream reading block index delta header.");
                            totalRead += read;
                        }
                    }
#else
                    lock (_readLock)
                    {
                        _stream.Position = _baseOffset + nextDeltaOffset;
                        ReadExactly(_stream, deltaHeader, 0, 22);
                    }
#endif

                    // Read and validate structure version
                    ushort deltaVersion = BinaryPrimitives.ReadUInt16BigEndian(deltaHeader.AsSpan(0));
                    if (deltaVersion > CurrentDeltaStructureVersion)
                        throw new NotSupportedException(
                            $"Block_Index_Delta structure version {deltaVersion} is not supported. Maximum supported version is {CurrentDeltaStructureVersion}.");

                    uint entryCount = BinaryPrimitives.ReadUInt32BigEndian(deltaHeader.AsSpan(2));
                    long nextOffset = BinaryPrimitives.ReadInt64BigEndian(deltaHeader.AsSpan(6));
                    uint compressedSize = BinaryPrimitives.ReadUInt32BigEndian(deltaHeader.AsSpan(14));
                    uint uncompressedSize = BinaryPrimitives.ReadUInt32BigEndian(deltaHeader.AsSpan(18));

                    // Read compressed entries
                    BlockIndexEntry[] entries = new BlockIndexEntry[entryCount];

                    if (compressedSize > 0 && entryCount > 0)
                    {
                        byte[] compressedData = new byte[compressedSize];

#if NET6_0_OR_GREATER
                        {
                            long entriesOffset = _baseOffset + nextDeltaOffset + 22;
                            int totalRead = 0;
                            while (totalRead < (int)compressedSize)
                            {
                                int read = RandomAccess.Read(_stream.SafeFileHandle, compressedData.AsSpan(totalRead), entriesOffset + totalRead);
                                if (read == 0) throw new EndOfStreamException("Unexpected end of stream reading block index delta entries.");
                                totalRead += read;
                            }
                        }
#else
                        lock (_readLock)
                        {
                            ReadExactly(_stream, compressedData, 0, (int)compressedSize);
                        }
#endif

                        // Decompress entries
                        byte[] entriesData = new byte[uncompressedSize];
                        using (MemoryStream compIn = new MemoryStream(compressedData))
                        using (Nanook.GrindCore.ZStd.ZStdStream zstd = new Nanook.GrindCore.ZStd.ZStdStream(compIn, new Nanook.GrindCore.CompressionOptions
                        {
                            Type = Nanook.GrindCore.CompressionType.Decompress,
                            LeaveOpen = true,
                            PositionFullSizeLimit = (int)uncompressedSize,
                            PositionLimit = (int)compressedSize
                        }))
                        {
                            int total = 0;
                            while (total < (int)uncompressedSize)
                            {
                                int read = zstd.Read(entriesData, total, (int)uncompressedSize - total);
                                if (read <= 0) break;
                                total += read;
                            }
                        }

                        for (int i = 0; i < (int)entryCount; i++)
                        {
                            int pos = i * 28;
                            entries[i] = new BlockIndexEntry
                            {
                                Key = new BlockKey(
                                    BinaryPrimitives.ReadUInt64BigEndian(entriesData.AsSpan(pos)),
                                    BinaryPrimitives.ReadUInt32BigEndian(entriesData.AsSpan(pos + 8))),
                                FileId = BinaryPrimitives.ReadInt32BigEndian(entriesData.AsSpan(pos + 12)),
                                Offset = BinaryPrimitives.ReadInt64BigEndian(entriesData.AsSpan(pos + 16)),
                                Size = BinaryPrimitives.ReadInt32BigEndian(entriesData.AsSpan(pos + 24))
                            };
                        }
                    }

                    // Merge this delta into the index
                    BlockIndexDelta delta = new BlockIndexDelta { Entries = entries };
                    index.MergeDelta(delta);

                    // Follow the linked list to the next (older) delta
                    nextDeltaOffset = nextOffset;
                }
            }

            _blockIndex = index;
            return _blockIndex;
        }

        /// <summary>
        /// Replaces the block index in the file with the given entries.
        /// Writes the new block index at the end of the file and updates the pending header.
        /// Call <see cref="AtomicCommit"/> after this to persist the header changes.
        /// </summary>
        /// <param name="sortedEntries">The new block index entries, sorted by BlockKey.</param>
        public void ReplaceBlockIndex(BlockIndexEntry[] sortedEntries)
        {
            // Write the new block index at the current end of file
            _stream.Position = _stream.Length;
            long newBlockIndexOffset = _stream.Position - _baseOffset;

            long blockIndexSize = BlockIndexSerializer.SerializeTo(_stream, sortedEntries);

            // Update pending header
            _pendingHeader.BlockIndexOffset = newBlockIndexOffset;
            _pendingHeader.BlockIndexSize = blockIndexSize;
            _pendingHeader.BlockIndexDeltaHeadOffset = 0;
            _pendingHeader.BlockIndexDeltaCount = 0;

            // Update cached block index
            _blockIndex = new InMemoryBlockIndex(sortedEntries);
        }

        /// <summary>
        /// Updates the Image_Metadata_Section for the specified image with new area and file records.
        /// Serializes and compresses the new metadata, writes it at the end of the file,
        /// and updates the directory entry with the new offset and sizes.
        /// Call <see cref="AtomicCommit"/> after all updates to persist the header changes.
        /// </summary>
        /// <param name="imageId">The image ID whose metadata to update.</param>
        /// <param name="areas">The area records for the image.</param>
        /// <param name="files">The file records for the image (with updated offsets).</param>
        public void UpdateImageMetadata(long imageId, List<AreaRecord> areas, List<FileRecord> files)
        {
            ImageDirectoryEntry? entry = _directory.GetEntry(imageId);
            if (entry == null)
                throw new KeyNotFoundException($"Image ID {imageId} not found in the directory.");

            // Serialize and compress the updated metadata
            (byte[]? compressedData, int uncompressedSize) = ImageMetadataSectionSerializer.Serialize(areas, files);

            // Write at end of file
            _stream.Position = _stream.Length;
            long newOffset = _stream.Position - _baseOffset;
            _stream.Write(compressedData, 0, compressedData.Length);

            // Update directory entry
            entry.MetadataSectionOffset = newOffset;
            entry.MetadataSectionCompressedSize = compressedData.Length;
            entry.MetadataSectionUncompressedSize = uncompressedSize;

            // Mark directory as pending (will be written on next UpdateDirectory or AtomicCommit)
            _pendingDirectory = _directory;
            UpdateDirectory(_directory);
        }

        /// <summary>
        /// Updates the Image_BlockMap_Section for an existing image with new block locations.
        /// Writes the updated section at the end of the file and updates the directory entry.
        /// </summary>
        /// <param name="imageId">The image ID to update.</param>
        /// <param name="offsets">The offset records for the image.</param>
        /// <param name="blockLocations">The updated block locations dictionary.</param>
        public void UpdateImageBlockMap(long imageId, List<OffsetRecord> offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations)
        {
            ImageDirectoryEntry? entry = _directory.GetEntry(imageId);
            if (entry == null)
                throw new KeyNotFoundException($"Image ID {imageId} not found in the directory.");

            // Serialize and compress the updated block map
            (byte[]? compressedData, int uncompressedSize) = ImageBlockMapSectionSerializer.Serialize(offsets, blockLocations);

            // Write at end of file
            _stream.Position = _stream.Length;
            long newOffset = _stream.Position - _baseOffset;
            _stream.Write(compressedData, 0, compressedData.Length);

            // Update directory entry
            entry.BlockMapSectionOffset = newOffset;
            entry.BlockMapSectionCompressedSize = compressedData.Length;
            entry.BlockMapSectionUncompressedSize = uncompressedSize;

            // Mark directory as pending (will be written on next UpdateDirectory or AtomicCommit)
            _pendingDirectory = _directory;
            UpdateDirectory(_directory);
        }

        /// <summary>
        /// Gets the current primary header.
        /// </summary>
        internal FileHeader Header => _primaryHeader;

        /// <summary>
        /// Gets the file path of this binary index file.
        /// </summary>
        public string FilePath => _filePath;

        /// <summary>
        /// Gets the base offset within the file where the index region starts.
        /// 0 for separate mode, greater than 0 for embedded mode.
        /// </summary>
        public long BaseOffset => _baseOffset;

        /// <summary>
        /// Gets the logical size of the index region (from the primary header's FileEndOffset).
        /// </summary>
        public long IndexSize => _primaryHeader.FileEndOffset;

        /// <summary>
        /// Appends an image's metadata and block map sections at the end of the file.
        /// Records the offsets and sizes for the directory entry.
        /// Optionally appends a Block_Index_Delta if new blocks were introduced.
        /// </summary>
        /// <param name="imageId">The image ID being appended.</param>
        /// <param name="metadata">Already-serialized Image_Metadata_Section bytes.</param>
        /// <param name="blockMap">Already-serialized Image_BlockMap_Section bytes.</param>
        /// <param name="newBlocks">New blocks to add as a delta, or null if no new blocks.</param>
        /// <returns>A tuple of (metadataOffset, metadataSize, blockMapOffset, blockMapSize) for the directory entry.</returns>
        public (long MetadataOffset, int MetadataSize, long BlockMapOffset, int BlockMapSize) AppendImage(
            long imageId, byte[] metadata, byte[] blockMap, BlockIndexEntry[]? newBlocks)
        {
            // Seek to file end
            _stream.Position = _stream.Length;

            // Write Image_Metadata_Section — record offset relative to index start
            long metadataOffset = _stream.Position - _baseOffset;
            _stream.Write(metadata, 0, metadata.Length);
            int metadataSize = metadata.Length;

            // Write Image_BlockMap_Section — record offset relative to index start
            long blockMapOffset = _stream.Position - _baseOffset;
            _stream.Write(blockMap, 0, blockMap.Length);
            int blockMapSize = blockMap.Length;

            // Append Block_Index_Delta if there are new blocks
            if (newBlocks != null && newBlocks.Length > 0)
            {
                BlockIndexDelta delta = new BlockIndexDelta
                {
                    Entries = newBlocks,
                    NextDeltaOffset = 0 // Will be set in AppendBlockIndexDelta
                };
                AppendBlockIndexDelta(delta);
            }

            return (metadataOffset, metadataSize, blockMapOffset, blockMapSize);
        }

        /// <summary>
        /// Appends a Block_Index_Delta at the end of the file.
        /// The delta's NextDeltaOffset is set to the previous delta head (from the current pending header).
        /// Updates the pending header's BlockIndexDeltaHeadOffset and increments BlockIndexDeltaCount.
        /// </summary>
        /// <param name="delta">The delta to serialize and append.</param>
        public void AppendBlockIndexDelta(BlockIndexDelta delta)
        {
            // Seek to file end
            _stream.Position = _stream.Length;

            // Record offset relative to index start
            long deltaOffset = _stream.Position - _baseOffset;

            // The NextDeltaOffset in the serialized delta points to the PREVIOUS delta head
            // (already stored as relative offset in the pending header)
            long previousDeltaHead = _pendingHeader.BlockIndexDeltaHeadOffset;

            // Serialize the delta with zstd compression:
            // [StructureVersion: uint16]
            // [EntryCount: uint32]
            // [NextDeltaOffset: int64]
            // [CompressedSize: uint32]
            // [UncompressedSize: uint32]
            // [ZstdCompressedEntries]

            int entryCount = delta.Entries?.Length ?? 0;
            int headerSize = 2 + 4 + 8 + 4 + 4; // version + count + next offset + compressedSize + uncompressedSize = 22 bytes
            int entriesSize = entryCount * 28;

            // Serialize entries to flat buffer (28 bytes each, no RefCount)
            byte[] entriesRaw = new byte[entriesSize];
            if (delta.Entries != null)
            {
                for (int i = 0; i < delta.Entries.Length; i++)
                {
                    int pos = i * 28;
                    ref BlockIndexEntry entry = ref delta.Entries[i];

                    BinaryPrimitives.WriteUInt64BigEndian(entriesRaw.AsSpan(pos), entry.Key.XxHash64);
                    BinaryPrimitives.WriteUInt32BigEndian(entriesRaw.AsSpan(pos + 8), entry.Key.Crc32);
                    BinaryPrimitives.WriteInt32BigEndian(entriesRaw.AsSpan(pos + 12), entry.FileId);
                    BinaryPrimitives.WriteInt64BigEndian(entriesRaw.AsSpan(pos + 16), entry.Offset);
                    BinaryPrimitives.WriteInt32BigEndian(entriesRaw.AsSpan(pos + 24), entry.Size);
                }
            }

            // Compress entries with zstd
            byte[] compressedEntries;
            if (entriesSize > 0)
            {
                using MemoryStream compOut = new MemoryStream();
                using (Nanook.GrindCore.ZStd.ZStdStream zstd = new Nanook.GrindCore.ZStd.ZStdStream(compOut, new Nanook.GrindCore.CompressionOptions
                {
                    Type = (Nanook.GrindCore.CompressionType)19,
                    LeaveOpen = true
                }))
                {
                    zstd.Write(entriesRaw, 0, entriesRaw.Length);
                }
                compressedEntries = compOut.ToArray();
            }
            else
            {
                compressedEntries = Array.Empty<byte>();
            }

            // Write header + compressed data
            byte[] buffer = new byte[headerSize + compressedEntries.Length];

            // Write structure version
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0), CurrentDeltaStructureVersion);

            // Write entry count
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(2), (uint)entryCount);

            // Write NextDeltaOffset (points to previous delta head)
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(6), previousDeltaHead);

            // Write compressed size and uncompressed size (uint32)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(14), (uint)compressedEntries.Length);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(18), (uint)entriesSize);

            // Write compressed entries
            if (compressedEntries.Length > 0)
                Buffer.BlockCopy(compressedEntries, 0, buffer, headerSize, compressedEntries.Length);

            _stream.Write(buffer, 0, buffer.Length);

            // Update pending header
            _pendingHeader.BlockIndexDeltaHeadOffset = deltaOffset;
            _pendingHeader.BlockIndexDeltaCount++;
        }

        /// <summary>
        /// Serializes and compresses the Image_Directory, then writes it either in-place
        /// within the reserved directory region (if it fits) or appends at the end of the file.
        /// Updates the pending header's ImageDirectoryOffset, ImageDirectorySize, and ImageDirectoryUncompressedSize.
        /// </summary>
        /// <param name="newDirectory">The new directory to serialize and write.</param>
        public void UpdateDirectory(ImageDirectory newDirectory)
        {
            byte[] directoryBytes = newDirectory.Serialize();
            byte[] compressedDirectory = ImageDirectorySerializer.CompressDirectory(directoryBytes);

            long directoryOffset;
            int directoryRegionCapacity = _pendingHeader.DirectoryRegionCapacity;

            if (directoryRegionCapacity > 0 && compressedDirectory.Length <= directoryRegionCapacity)
            {
                // In-place update: write compressed directory at its current location
                directoryOffset = _pendingHeader.ImageDirectoryOffset;
                _stream.Position = _baseOffset + directoryOffset;
                _stream.Write(compressedDirectory, 0, compressedDirectory.Length);
            }
            else
            {
                // Overflow: append at end of file
                _stream.Position = _stream.Length;
                directoryOffset = _stream.Position - _baseOffset;
                _stream.Write(compressedDirectory, 0, compressedDirectory.Length);
            }

            // Update pending header with relative offset and sizes
            _pendingHeader.ImageDirectoryOffset = directoryOffset;
            _pendingHeader.ImageDirectorySize = compressedDirectory.Length;
            _pendingHeader.ImageDirectoryUncompressedSize = directoryBytes.Length;

            // Store the pending directory for commit
            _pendingDirectory = newDirectory;
        }

        /// <summary>
        /// Performs an atomic commit:
        /// 1. Updates pending header's FileEndOffset and ImageCount
        /// 2. Flushes all appended data to disk
        /// 3. Writes SecondaryHeader at offset 0x100 (256) → flush
        /// 4. Writes PrimaryHeader at offset 0 → flush
        /// 5. Updates internal state (_primaryHeader, _secondaryHeader, _directory)
        /// </summary>
        public void AtomicCommit()
        {
            // Update pending header's FileEndOffset relative to index start
            _pendingHeader.FileEndOffset = _stream.Length - _baseOffset;

            // Update pending header's ImageCount from directory
            if (_pendingDirectory != null)
            {
                _pendingHeader.ImageCount = _pendingDirectory.Count;
            }

            // Step 1: Flush all appended data to disk
            _stream.Flush(flushToDisk: true);

            // Serialize the header
            byte[] headerBuffer = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(headerBuffer, _pendingHeader);

            // Step 2: Write SecondaryHeader at _baseOffset + 0x100 → flush
            _stream.Position = _baseOffset + FileHeader.HeaderSize;
            _stream.Write(headerBuffer, 0, FileHeader.HeaderSize);
            _stream.Flush(flushToDisk: true);

            // Step 3: Write PrimaryHeader at _baseOffset + 0 → flush
            _stream.Position = _baseOffset;
            _stream.Write(headerBuffer, 0, FileHeader.HeaderSize);
            _stream.Flush(flushToDisk: true);

            // Step 4: Update internal state
            _primaryHeader = _pendingHeader;
            _secondaryHeader = _pendingHeader;

            if (_pendingDirectory != null)
            {
                _directory = _pendingDirectory;
                _pendingDirectory = null;
            }
        }

        /// <summary>
        /// Validates a header against the logical index region size and structural constraints.
        /// Checks: magic, version, and that all file-offset pointers are non-negative and within the index region.
        /// In separate mode, indexSize equals the total file size. In embedded mode, indexSize equals
        /// the byte length of the index region (file_size - 12 - baseOffset).
        /// </summary>
        private static bool IsHeaderValid(FileHeader header, long indexSize)
        {
            // Magic check
            if (header.Magic != FileHeader.MagicBytes)
                return false;

            // Version check
            if (header.MajorVersion != FileHeader.CurrentMajorVersion)
                return false;

            if (header.MinorVersion > FileHeader.CurrentMinorVersion)
                return false;

            // File-offset bounds checks: all offsets must be non-negative and within the index region
            if (header.ImageDirectoryOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.ImageDirectoryOffset + header.ImageDirectorySize > indexSize)
                return false;

            if (header.BlockIndexOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.BlockIndexOffset + header.BlockIndexSize > indexSize)
                return false;

            if (header.FileEndOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.FileEndOffset > indexSize)
                return false;

            // Delta head offset check (0 means no deltas, otherwise must be within index region)
            if (header.BlockIndexDeltaHeadOffset != 0 &&
                header.BlockIndexDeltaHeadOffset < FileHeader.HeaderSize * 2)
                return false;

            if (header.BlockIndexDeltaHeadOffset > indexSize)
                return false;

            // Configuration sanity checks
            if (header.BlockSize <= 0)
                return false;

            if (header.MaxOffsetBlocks <= 0)
                return false;

            if (header.ShardSize < 0)
                return false;

            if (header.ImageCount < 0)
                return false;

            if (header.ImageDirectorySize < 0)
                return false;

            if (header.BlockIndexSize < 0)
                return false;

            if (header.BlockIndexDeltaCount < 0)
                return false;

            return true;
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from the stream.
        /// Throws if the stream ends before all bytes are read.
        /// </summary>
        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream. Expected {count} bytes but only read {totalRead}.");
                totalRead += bytesRead;
            }
        }

        /// <summary>
        /// Compacts the current binary index file into a new clean file at the specified path.
        /// The compacted file contains:
        /// 1. Header + SecondaryHeader (placeholder, then final)
        /// 2. All non-removed image sections (metadata + blockmap) copied sequentially
        /// 3. A single merged Block_Index with only entries referenced by live images
        /// 4. A new Image_Directory
        /// 5. Final headers with correct pointers
        /// 
        /// In deterministic mode, images are processed in ascending ImageId order,
        /// the directory is sorted by ImageId, and all padding is zero-filled.
        /// </summary>
        /// <param name="tempPath">The path for the new compacted file.</param>
        /// <param name="deterministic">
        /// When true, produces a byte-deterministic output: images sorted by ID,
        /// directory sorted by ID, and all reserved/padding bytes zero-filled.
        /// </param>
        /// <param name="progress">Optional progress reporter (0-100 percentage).</param>
        public void CompactTo(string tempPath, bool deterministic = false, IProgress<(int Percentage, string Stage)>? progress = null)
        {
            // Step 1: Gather non-removed images from the current directory
            IEnumerable<ImageDirectoryEntry> nonRemovedImages = _directory.GetNonRemovedEntries();
            List<ImageDirectoryEntry> imagesToCopy = deterministic
                ? nonRemovedImages.OrderBy(e => e.ImageId).ToList()
                : nonRemovedImages.ToList();

            progress?.Report((5, "Gathering images"));

            // Step 2: Load the full block index (main + all deltas), then prune
            // entries not referenced by any live image's BlockLocations.
            InMemoryBlockIndex mergedIndex = LoadBlockIndex();

            progress?.Report((15, "Loading block index"));

            // Step 3: Build a set of all BlockKeys referenced by live images
            HashSet<BlockKey> liveBlockKeys = new HashSet<BlockKey>();
            int imagesScanned = 0;
            int totalToScan = imagesToCopy.Count;
            foreach (ImageDirectoryEntry liveImage in imagesToCopy)
            {
                // Read each live image's block map to get its referenced BlockKeys
                (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)>? blockLocations) = ReadImageBlockMap(liveImage.ImageId);
                foreach (BlockKey key in blockLocations.Keys)
                {
                    liveBlockKeys.Add(key);
                }
                imagesScanned++;
                // Report 15-40% during block map scanning
                progress?.Report((15 + (imagesScanned * 25 / totalToScan), "Scanning block maps"));
            }

            // Filter the block index to only retain entries referenced by live images
            BlockIndexEntry[] allEntries = mergedIndex.GetEntries();
            BlockIndexEntry[] compactedEntries = allEntries
                .Where(e => liveBlockKeys.Contains(e.Key))
                .ToArray();

            progress?.Report((42, "Pruning unreferenced blocks"));

            // Step 4: Pre-compute the directory region capacity.
            // We need to know the compressed directory size to determine the region capacity,
            // but the final directory contains offsets that depend on the region capacity.
            // Strategy: serialize with placeholder offsets to estimate compressed size,
            // then compute capacity, write image sections at the correct offset,
            // and re-serialize the directory with final offsets.
            ImageDirectory newDirectory = new ImageDirectory();
            foreach (ImageDirectoryEntry sourceEntry in imagesToCopy)
            {
                // Add entries with placeholder offsets (will be updated after writing sections)
                ImageDirectoryEntry newEntry = new ImageDirectoryEntry
                {
                    ImageId = sourceEntry.ImageId,
                    Name = sourceEntry.Name,
                    Size = sourceEntry.Size,
                    Crc32 = sourceEntry.Crc32,
                    XxHash64 = sourceEntry.XxHash64,
                    System = sourceEntry.System,
                    Format = sourceEntry.Format,
                    RollbackFileId = sourceEntry.RollbackFileId,
                    RollbackOffset = sourceEntry.RollbackOffset,
                    Removed = false,
                    MetadataSectionOffset = sourceEntry.MetadataSectionOffset, // placeholder
                    MetadataSectionCompressedSize = sourceEntry.MetadataSectionCompressedSize,
                    MetadataSectionUncompressedSize = sourceEntry.MetadataSectionUncompressedSize,
                    BlockMapSectionOffset = sourceEntry.BlockMapSectionOffset, // placeholder
                    BlockMapSectionCompressedSize = sourceEntry.BlockMapSectionCompressedSize,
                    BlockMapSectionUncompressedSize = sourceEntry.BlockMapSectionUncompressedSize
                };
                newDirectory.AddOrUpdate(newEntry);
            }

            // Estimate compressed directory size using placeholder offsets
            byte[] estimateDirectoryBytes;
            if (deterministic)
            {
                IOrderedEnumerable<ImageDirectoryEntry> sortedEntries = newDirectory.GetAllEntries().OrderBy(e => e.ImageId);
                estimateDirectoryBytes = ImageDirectorySerializer.Serialize(sortedEntries);
            }
            else
            {
                estimateDirectoryBytes = newDirectory.Serialize();
            }
            byte[] estimateCompressedDir = ImageDirectorySerializer.CompressDirectory(estimateDirectoryBytes);

            // Compute new DirectoryRegionCapacity = compressedDirSize + 4096 headroom
            int newDirectoryRegionCapacity = estimateCompressedDir.Length + DefaultDirectoryRegionCapacity;

            // Step 5: Create the new file and write the layout in a single pass
            using FileStream newStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            // Write placeholder headers (2 x 256 bytes)
            byte[] emptyHeader = new byte[FileHeader.HeaderSize];
            newStream.Write(emptyHeader, 0, FileHeader.HeaderSize); // Primary header placeholder at 0x000
            newStream.Write(emptyHeader, 0, FileHeader.HeaderSize); // Secondary header placeholder at 0x100

            // Write compressed directory placeholder into reserved region at 0x200
            // (will be overwritten with final directory after image sections are written)
            byte[] regionPadding = new byte[newDirectoryRegionCapacity];
            newStream.Write(regionPadding, 0, regionPadding.Length);

            // Step 6: Write image sections starting at 0x200 + newDirectoryRegionCapacity
            int imagesCopied = 0;
            int totalImages = imagesToCopy.Count;
            foreach (ImageDirectoryEntry sourceEntry in imagesToCopy)
            {
                // Read the raw metadata section bytes from the old file
                _stream.Position = _baseOffset + sourceEntry.MetadataSectionOffset;
                byte[] metadataBytes = new byte[sourceEntry.MetadataSectionCompressedSize];
                ReadExactly(_stream, metadataBytes, 0, sourceEntry.MetadataSectionCompressedSize);

                // Read the raw block map section bytes from the old file
                _stream.Position = _baseOffset + sourceEntry.BlockMapSectionOffset;
                byte[] blockMapBytes = new byte[sourceEntry.BlockMapSectionCompressedSize];
                ReadExactly(_stream, blockMapBytes, 0, sourceEntry.BlockMapSectionCompressedSize);

                // Write metadata section to new file — record offset
                long newMetadataOffset = newStream.Position;
                newStream.Write(metadataBytes, 0, metadataBytes.Length);

                // Write block map section to new file — record offset
                long newBlockMapOffset = newStream.Position;
                newStream.Write(blockMapBytes, 0, blockMapBytes.Length);

                // Update directory entry with correct offsets
                ImageDirectoryEntry? existingEntry = newDirectory.GetEntry(sourceEntry.ImageId);
                if (existingEntry != null)
                {
                    existingEntry.MetadataSectionOffset = newMetadataOffset;
                    existingEntry.BlockMapSectionOffset = newBlockMapOffset;
                }

                // Report progress (image copy is 42-70% of the work)
                imagesCopied++;
                progress?.Report((42 + (imagesCopied * 28 / totalImages), "Copying image sections"));
            }

            // Step 7: Write block index directly to output stream (70-95% — zstd compression)
            progress?.Report((70, "Compressing block index"));
            long blockIndexOffset = newStream.Position;
            IProgress<int>? serializerProgress = progress != null
                ? new Progress<int>(pct => progress.Report((70 + (pct * 25 / 100), "Compressing block index")))
                : null;
            long blockIndexSize = BlockIndexSerializer.SerializeTo(newStream, compactedEntries, progress: serializerProgress);
            progress?.Report((95, "Finalizing"));

            long fileEndOffset = newStream.Position;

            // Step 8: Re-serialize directory with final offsets and write into reserved region
            byte[] directoryBytes;
            if (deterministic)
            {
                IOrderedEnumerable<ImageDirectoryEntry> sortedEntries = newDirectory.GetAllEntries().OrderBy(e => e.ImageId);
                directoryBytes = ImageDirectorySerializer.Serialize(sortedEntries);
            }
            else
            {
                directoryBytes = newDirectory.Serialize();
            }
            byte[] compressedDir = ImageDirectorySerializer.CompressDirectory(directoryBytes);
            int imageDirectoryUncompressedSize = directoryBytes.Length;

            // The final compressed size should fit within the region capacity.
            // For idempotent compaction, the capacity must be based on the final compressed
            // directory size, not the estimate. If the final-based capacity differs from the
            // estimate-based capacity, we must rewrite the file with the correct capacity.
            // Use the maximum of estimate and final sizes to avoid oscillation and ensure
            // the capacity always provides at least 4096 bytes of headroom.
            int stableCapacity = Math.Max(compressedDir.Length, estimateCompressedDir.Length) + DefaultDirectoryRegionCapacity;
            if (stableCapacity != newDirectoryRegionCapacity)
            {
                // The estimate-based capacity differs from the stable capacity.
                // Rewrite the file with the correct capacity to ensure idempotent compaction.
                newDirectoryRegionCapacity = stableCapacity;
                newStream.Position = 0;
                newStream.SetLength(0);

                // Rewrite headers placeholder
                byte[] emptyHdr = new byte[FileHeader.HeaderSize];
                newStream.Write(emptyHdr, 0, FileHeader.HeaderSize);
                newStream.Write(emptyHdr, 0, FileHeader.HeaderSize);

                // Rewrite directory region placeholder
                byte[] regionPad = new byte[newDirectoryRegionCapacity];
                newStream.Write(regionPad, 0, regionPad.Length);

                // Rewrite image sections at new positions
                ImageDirectory rewriteDirectory = new ImageDirectory();
                foreach (ImageDirectoryEntry sourceEntry in imagesToCopy)
                {
                    _stream.Position = _baseOffset + sourceEntry.MetadataSectionOffset;
                    byte[] metaBytes = new byte[sourceEntry.MetadataSectionCompressedSize];
                    ReadExactly(_stream, metaBytes, 0, sourceEntry.MetadataSectionCompressedSize);

                    _stream.Position = _baseOffset + sourceEntry.BlockMapSectionOffset;
                    byte[] bmBytes = new byte[sourceEntry.BlockMapSectionCompressedSize];
                    ReadExactly(_stream, bmBytes, 0, sourceEntry.BlockMapSectionCompressedSize);

                    long newMetaOff = newStream.Position;
                    newStream.Write(metaBytes, 0, metaBytes.Length);
                    long newBmOff = newStream.Position;
                    newStream.Write(bmBytes, 0, bmBytes.Length);

                    ImageDirectoryEntry newEntry = new ImageDirectoryEntry
                    {
                        ImageId = sourceEntry.ImageId,
                        Name = sourceEntry.Name,
                        Size = sourceEntry.Size,
                        Crc32 = sourceEntry.Crc32,
                        XxHash64 = sourceEntry.XxHash64,
                        System = sourceEntry.System,
                        Format = sourceEntry.Format,
                        RollbackFileId = sourceEntry.RollbackFileId,
                        RollbackOffset = sourceEntry.RollbackOffset,
                        Removed = false,
                        MetadataSectionOffset = newMetaOff,
                        MetadataSectionCompressedSize = sourceEntry.MetadataSectionCompressedSize,
                        MetadataSectionUncompressedSize = sourceEntry.MetadataSectionUncompressedSize,
                        BlockMapSectionOffset = newBmOff,
                        BlockMapSectionCompressedSize = sourceEntry.BlockMapSectionCompressedSize,
                        BlockMapSectionUncompressedSize = sourceEntry.BlockMapSectionUncompressedSize
                    };
                    rewriteDirectory.AddOrUpdate(newEntry);
                }

                // Rewrite block index
                blockIndexOffset = newStream.Position;
                blockIndexSize = BlockIndexSerializer.SerializeTo(newStream, compactedEntries, progress: null);
                fileEndOffset = newStream.Position;

                // Re-serialize directory with corrected offsets
                if (deterministic)
                {
                    IOrderedEnumerable<ImageDirectoryEntry> sortedEntries2 = rewriteDirectory.GetAllEntries().OrderBy(e => e.ImageId);
                    directoryBytes = ImageDirectorySerializer.Serialize(sortedEntries2);
                }
                else
                {
                    directoryBytes = rewriteDirectory.Serialize();
                }
                compressedDir = ImageDirectorySerializer.CompressDirectory(directoryBytes);
                imageDirectoryUncompressedSize = directoryBytes.Length;
                newDirectory = rewriteDirectory;
            }

            // Write compressed directory at 0x200
            long imageDirectoryOffset = DirectoryRegionStartOffset; // 0x200
            newStream.Position = DirectoryRegionStartOffset;
            newStream.Write(compressedDir, 0, compressedDir.Length);

            // Zero-fill remaining region padding (already zeroed from initial write, but
            // ensure no stale data if the estimate was larger)
            int remainingPadding = newDirectoryRegionCapacity - compressedDir.Length;
            if (remainingPadding > 0)
            {
                byte[] zeroPad = new byte[remainingPadding];
                newStream.Write(zeroPad, 0, zeroPad.Length);
            }

            // Step 9: Finalize headers
            FileHeader newHeader = new FileHeader
            {
                Magic = FileHeader.MagicBytes,
                MajorVersion = FileHeader.CurrentMajorVersion,
                MinorVersion = FileHeader.CurrentMinorVersion,
                ImageDirectoryOffset = imageDirectoryOffset,
                ImageDirectorySize = compressedDir.Length,
                ImageDirectoryUncompressedSize = imageDirectoryUncompressedSize,
                BlockIndexOffset = blockIndexOffset,
                BlockIndexSize = blockIndexSize,
                BlockIndexDeltaHeadOffset = 0, // No deltas in compacted file
                BlockIndexDeltaCount = 0,
                FileEndOffset = fileEndOffset,
                ShardSize = _primaryHeader.ShardSize,
                BlockSize = _primaryHeader.BlockSize,
                MaxOffsetBlocks = _primaryHeader.MaxOffsetBlocks,
                ImageCount = newDirectory.Count,
                DirectoryRegionCapacity = newDirectoryRegionCapacity
            };

            byte[] headerBuffer = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(headerBuffer, newHeader);

            // Write primary header at offset 0x000
            newStream.Position = 0;
            newStream.Write(headerBuffer, 0, FileHeader.HeaderSize);

            // Write secondary header at offset 0x100
            newStream.Write(headerBuffer, 0, FileHeader.HeaderSize);

            // Step 10: Flush to disk
            newStream.Flush(flushToDisk: true);
        }

        /// <summary>
        /// Disposes the file stream.
        /// <summary>
        /// Returns true if the underlying file stream has been closed/disposed.
        /// </summary>
        internal bool IsStreamClosed => _stream == null;

        /// <summary>
        /// Disposes the file stream.
        /// </summary>
        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null!;
        }
    }
}