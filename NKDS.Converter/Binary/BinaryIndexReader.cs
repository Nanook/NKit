using NKitDataStore;
using NKitDataStore.Binary;

namespace NKDS.Converter.Binary
{
    /// <summary>
    /// Reads all data from a binary index file using NKitDataStore's BinaryIndexFile.
    /// Supports both separate-mode (standalone .nkds file) and embedded-mode
    /// (index appended after shard data with a 12-byte footer).
    /// </summary>
    internal class BinaryIndexReader : IDisposable
    {
        private readonly BinaryIndexFile _indexFile;
        private bool _disposed;

        /// <summary>
        /// Gets the file header containing format metadata and section pointers.
        /// </summary>
        public FileHeader Header { get; }

        /// <summary>
        /// Gets whether this binary index is in embedded mode (appended after shard data).
        /// </summary>
        public bool IsEmbedded { get; }

        /// <summary>
        /// Gets the base offset within the file where the index region starts.
        /// 0 for separate mode, greater than 0 for embedded mode.
        /// </summary>
        public long BaseOffset { get; }

        /// <summary>
        /// Opens a binary index file for reading.
        /// Automatically detects embedded mode by checking for the 12-byte footer.
        /// </summary>
        /// <param name="filePath">Path to the binary index file.</param>
        public BinaryIndexReader(string filePath)
        {
            long baseOffset = DetectEmbeddedMode(filePath);
            IsEmbedded = baseOffset > 0;
            BaseOffset = baseOffset;

            _indexFile = BinaryIndexFile.Open(filePath, baseOffset);
            Header = _indexFile.Header;
        }

        /// <summary>
        /// Reads the info record from the file header configuration.
        /// </summary>
        /// <returns>An InfoRecord populated from the header's storage parameters.</returns>
        public InfoRecord ReadInfo()
        {
            return new InfoRecord
            {
                ShardSize = Header.ShardSize,
                BlockSize = Header.BlockSize,
                MaxOffsetBlocks = Header.MaxOffsetBlocks
            };
        }

        /// <summary>
        /// Reads the image directory containing entries for all images.
        /// </summary>
        /// <returns>A read-only list of all image directory entries.</returns>
        public IReadOnlyList<ImageDirectoryEntry> ReadDirectory()
        {
            ImageDirectory directory = _indexFile.GetDirectory();
            return directory.GetAllEntries().OrderBy(e => e.ImageId).ToList().AsReadOnly();
        }

        /// <summary>
        /// Reads and decompresses the Image_Metadata_Section for the specified image.
        /// </summary>
        /// <param name="imageId">The image ID to read metadata for.</param>
        /// <returns>A tuple of (AreaRecords, FileRecords) from the decompressed metadata section.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the image ID is not in the directory.</exception>
        public (List<AreaRecord> Areas, List<FileRecord> Files) ReadImageMetadata(long imageId) => _indexFile.ReadImageMetadata(imageId);

        /// <summary>
        /// Reads and decompresses the Image_BlockMap_Section for the specified image.
        /// </summary>
        /// <param name="imageId">The image ID to read the block map for.</param>
        /// <returns>A tuple of (OffsetRecords, BlockLocations dictionary) from the decompressed block map section.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the image ID is not in the directory.</exception>
        public (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) ReadImageBlockMap(long imageId) => _indexFile.ReadImageBlockMap(imageId);

        /// <summary>
        /// Loads the complete block index, including any deltas merged in.
        /// </summary>
        /// <returns>The merged InMemoryBlockIndex containing all block entries.</returns>
        public InMemoryBlockIndex LoadBlockIndex() => _indexFile.LoadBlockIndex();

        /// <summary>
        /// Detects whether the file is in embedded mode by reading the last 12 bytes
        /// and checking for the NKDS footer magic.
        /// </summary>
        /// <param name="filePath">Path to the file to check.</param>
        /// <returns>The base offset (file length - index size - footer size) if embedded, 0 otherwise.</returns>
        private static long DetectEmbeddedMode(string filePath)
        {
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // File must be at least 12 bytes (footer) + 512 bytes (two headers) to be embedded
            if (fs.Length < EmbeddedFooter.FooterSize + (FileHeader.HeaderSize * 2))
                return 0;

            // Read the last 12 bytes
            byte[] footerBytes = new byte[EmbeddedFooter.FooterSize];
            fs.Position = fs.Length - EmbeddedFooter.FooterSize;
            int bytesRead = 0;
            while (bytesRead < EmbeddedFooter.FooterSize)
            {
                int read = fs.Read(footerBytes, bytesRead, EmbeddedFooter.FooterSize - bytesRead);
                if (read == 0) break;
                bytesRead += read;
            }

            if (bytesRead < EmbeddedFooter.FooterSize)
                return 0;

            // Try to deserialize the footer
            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBytes);
            if (footer == null)
            {
                // No valid footer magic — check if the file starts with NKDS magic directly
                return 0;
            }

            // Calculate base offset: file length - index size - footer size
            long indexSize = footer.Value.IndexSize;
            long baseOffset = fs.Length - indexSize - EmbeddedFooter.FooterSize;

            // Validate the base offset is reasonable
            if (baseOffset < 0 || baseOffset >= fs.Length)
                return 0;

            return baseOffset;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _indexFile.Dispose();
            }
        }
    }
}