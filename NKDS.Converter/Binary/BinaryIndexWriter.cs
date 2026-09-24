using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;

namespace NKDS.Converter.Binary
{
    /// <summary>
    /// Writes a complete binary index file using NKitDataStore serializers.
    /// Handles both separate and embedded modes.
    /// 
    /// File layout (separate mode):
    ///   0x000: Primary Header (256 bytes)
    ///   0x100: Secondary Header (256 bytes)
    ///   0x200: Directory Region (reserved, written last)
    ///   DataRegionStart: Image sections (metadata + block map per image)
    ///   After images: Block_Index (sectioned zstd)
    ///   Header finalized with correct offsets
    /// 
    /// Embedded mode:
    ///   Same layout but starting at shardBoundary offset within the file.
    ///   After finalization, a 12-byte footer is appended.
    /// </summary>
    internal class BinaryIndexWriter : IDisposable
    {
        private const int DefaultDirectoryRegionCapacity = 4096;
        private const long DirectoryRegionStartOffset = FileHeader.HeaderSize * 2; // 0x200

        private readonly string _filePath;
        private readonly bool _embedded;
        private readonly long _baseOffset;
        private readonly InfoRecord _info;
        private FileStream _stream;
        private bool _disposed;

        /// <summary>
        /// Creates a new BinaryIndexWriter.
        /// </summary>
        /// <param name="filePath">Path to the output file.</param>
        /// <param name="info">The info record containing shard_size, block_size, max_offset_blocks.</param>
        /// <param name="embedded">True if writing in embedded mode (appending to shard data).</param>
        /// <param name="shardBoundary">The byte offset where the index starts (0 for separate mode).</param>
        public BinaryIndexWriter(string filePath, InfoRecord info, bool embedded = false, long shardBoundary = 0)
        {
            _filePath = filePath;
            _info = info;
            _embedded = embedded;
            _baseOffset = embedded ? shardBoundary : 0;

            // Open or create the file
            if (embedded && File.Exists(filePath))
            {
                // Embedded mode: open existing shard file for appending
                _stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                _stream.Position = _baseOffset;
            }
            else
            {
                // Separate mode: create new file
                _stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            }

            // Write placeholder headers (2 x 256 bytes)
            byte[] headerPlaceholder = new byte[FileHeader.HeaderSize];
            _stream.Write(headerPlaceholder, 0, FileHeader.HeaderSize); // Primary header at baseOffset + 0x000
            _stream.Write(headerPlaceholder, 0, FileHeader.HeaderSize); // Secondary header at baseOffset + 0x100

            // Reserve directory region with zeros
            byte[] directoryRegion = new byte[DefaultDirectoryRegionCapacity];
            _stream.Write(directoryRegion, 0, directoryRegion.Length);
        }

        /// <summary>
        /// Writes an image's metadata and block map sections.
        /// Returns the offsets/sizes needed for the directory entry.
        /// </summary>
        /// <param name="areas">Area records for this image.</param>
        /// <param name="files">File records for this image.</param>
        /// <param name="offsets">Offset records for this image.</param>
        /// <param name="blockLocations">Block location dictionary for this image's blocks.</param>
        /// <returns>Tuple of metadata and block map section offsets and sizes.</returns>
        public (long MetaOffset, int MetaSize, int MetaUncompressedSize,
                long BlockMapOffset, int BlockMapSize, int BlockMapUncompressedSize)
            WriteImageSections(
                IEnumerable<AreaRecord> areas,
                IEnumerable<FileRecord> files,
                IEnumerable<OffsetRecord> offsets,
                Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations)
        {
            // Serialize Image_Metadata_Section (areas + files)
            (byte[]? metaCompressed, int metaUncompressedSize) = ImageMetadataSectionSerializer.Serialize(areas, files);

            // Write metadata section - record offset relative to index start
            long metaOffset = _stream.Position - _baseOffset;
            _stream.Write(metaCompressed, 0, metaCompressed.Length);
            int metaSize = metaCompressed.Length;

            // Serialize Image_BlockMap_Section (offsets + block locations)
            (byte[]? blockMapCompressed, int blockMapUncompressedSize) = ImageBlockMapSectionSerializer.Serialize(offsets, blockLocations);

            // Write block map section - record offset relative to index start
            long blockMapOffset = _stream.Position - _baseOffset;
            _stream.Write(blockMapCompressed, 0, blockMapCompressed.Length);
            int blockMapSize = blockMapCompressed.Length;

            return (metaOffset, metaSize, metaUncompressedSize,
                    blockMapOffset, blockMapSize, blockMapUncompressedSize);
        }

        /// <summary>
        /// Writes the complete block index from a sorted entry array.
        /// Uses sectioned zstd compression (64KB uncompressed sections).
        /// Must be called before WriteDirectoryAndFinalize.
        /// </summary>
        /// <param name="sortedEntries">Block index entries, will be sorted during serialization.</param>
        /// <returns>Tuple of (offset relative to index start, compressed size).</returns>
        public (long Offset, int Size) WriteBlockIndex(BlockIndexEntry[] sortedEntries)
        {
            // Serialize using BlockIndexSerializer with default 64KB section size
            byte[] blockIndexBytes = BlockIndexSerializer.Serialize(sortedEntries, sectionSize: 65536);

            // Write block index - record offset relative to index start
            long offset = _stream.Position - _baseOffset;
            _stream.Write(blockIndexBytes, 0, blockIndexBytes.Length);

            // Store for header finalization in WriteDirectoryAndFinalize
            _blockIndexOffset = offset;
            _blockIndexSize = blockIndexBytes.Length;

            return (offset, blockIndexBytes.Length);
        }

        /// <summary>
        /// Writes the image directory and finalizes the header.
        /// In embedded mode, also writes the 12-byte footer.
        /// </summary>
        /// <param name="entries">The image directory entries to write.</param>
        public void WriteDirectoryAndFinalize(IEnumerable<ImageDirectoryEntry> entries)
        {
            // Materialize entries to avoid multiple enumeration
            IList<ImageDirectoryEntry> entryList = entries as IList<ImageDirectoryEntry> ?? entries.ToList();
            int imageCount = entryList.Count;

            // Record file end offset (after block index, before directory write)
            long fileEndOffset = _stream.Position - _baseOffset;

            // Serialize the directory
            byte[] directoryBytes = ImageDirectorySerializer.Serialize(entryList);
            byte[] compressedDirectory = ImageDirectorySerializer.CompressDirectory(directoryBytes);

            long directoryOffset;
            int directoryRegionCapacity;

            if (compressedDirectory.Length <= DefaultDirectoryRegionCapacity)
            {
                // Small directory fits in the initial reserved region at 0x200
                directoryRegionCapacity = DefaultDirectoryRegionCapacity;
                directoryOffset = DirectoryRegionStartOffset;
                _stream.Position = _baseOffset + directoryOffset;
                _stream.Write(compressedDirectory, 0, compressedDirectory.Length);

                // Zero-fill remaining region
                int remaining = directoryRegionCapacity - compressedDirectory.Length;
                if (remaining > 0)
                {
                    byte[] padding = new byte[remaining];
                    _stream.Write(padding, 0, padding.Length);
                }
            }
            else
            {
                // Directory too large for initial 4 KiB region — append at end of file.
                // Set capacity = compressed size + 4096 so future in-place updates have headroom.
                directoryRegionCapacity = compressedDirectory.Length + DefaultDirectoryRegionCapacity;
                _stream.Position = _baseOffset + fileEndOffset;
                directoryOffset = _stream.Position - _baseOffset;
                _stream.Write(compressedDirectory, 0, compressedDirectory.Length);

                // Write padding for future in-place growth
                byte[] padding = new byte[DefaultDirectoryRegionCapacity];
                _stream.Write(padding, 0, padding.Length);

                fileEndOffset = _stream.Position - _baseOffset;
            }

            // Build the final header
            FileHeader header = new FileHeader
            {
                Magic = FileHeader.MagicBytes,
                MajorVersion = FileHeader.CurrentMajorVersion,
                MinorVersion = FileHeader.CurrentMinorVersion,
                ImageDirectoryOffset = directoryOffset,
                ImageDirectorySize = compressedDirectory.Length,
                ImageDirectoryUncompressedSize = directoryBytes.Length,
                BlockIndexOffset = _blockIndexOffset,
                BlockIndexSize = _blockIndexSize,
                BlockIndexDeltaHeadOffset = 0, // No deltas in converter output
                BlockIndexDeltaCount = 0,
                FileEndOffset = fileEndOffset,
                ShardSize = _info.ShardSize,
                BlockSize = _info.BlockSize,
                MaxOffsetBlocks = _info.MaxOffsetBlocks,
                ImageCount = imageCount,
                DirectoryRegionCapacity = directoryRegionCapacity
            };

            // Serialize the header
            byte[] headerBuffer = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(headerBuffer, header);

            // Write primary header at baseOffset + 0x000
            _stream.Position = _baseOffset;
            _stream.Write(headerBuffer, 0, FileHeader.HeaderSize);

            // Write secondary header at baseOffset + 0x100
            _stream.Write(headerBuffer, 0, FileHeader.HeaderSize);

            // Flush all data
            _stream.Flush(flushToDisk: true);

            // In embedded mode, write the 12-byte footer after all index data
            if (_embedded)
            {
                // Seek to end of file (after all index data)
                _stream.Position = _baseOffset + fileEndOffset;

                // Calculate index size (the entire index region, excluding the footer)
                long indexSize = fileEndOffset;

                // Write footer: [IndexSize: int64 BE][Magic: uint32 BE "NKDS"]
                EmbeddedFooter footer = new EmbeddedFooter(indexSize);
                byte[] footerBytes = footer.Serialize();
                _stream.Write(footerBytes, 0, footerBytes.Length);

                _stream.Flush(flushToDisk: true);
            }
        }

        private long _blockIndexOffset;
        private long _blockIndexSize;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _stream?.Dispose();
                _stream = null!;
            }
        }
    }
}