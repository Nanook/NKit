namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents the 256-byte header at the start of a binary index file.
    /// Contains format identification, version, and pointers to all major structures.
    /// </summary>
    internal struct FileHeader
    {
        /// <summary>
        /// Fixed size of the header in bytes.
        /// </summary>
        public const int HeaderSize = 256;

        /// <summary>
        /// Magic bytes identifying the file as an NKitDataStore binary index ("NKDS").
        /// </summary>
        public const uint MagicBytes = 0x4E4B4453;

        /// <summary>
        /// Current major version of the binary index format.
        /// </summary>
        public const ushort CurrentMajorVersion = 1;

        /// <summary>
        /// Current minor version of the binary index format.
        /// </summary>
        public const ushort CurrentMinorVersion = 0;

        // Offset 0x00: Magic (4 bytes)
        public uint Magic;

        // Offset 0x04: Major version (2 bytes)
        public ushort MajorVersion;

        // Offset 0x06: Minor version (2 bytes)
        public ushort MinorVersion;

        // Offset 0x08: Image_Directory file offset (8 bytes)
        public long ImageDirectoryOffset;

        // Offset 0x10: Image_Directory compressed size (4 bytes)
        public int ImageDirectorySize;

        // Offset 0x14: Image_Directory uncompressed size (4 bytes)
        public int ImageDirectoryUncompressedSize;

        // Offset 0x18: Block_Index file offset (8 bytes)
        public long BlockIndexOffset;

        // Offset 0x20: Block_Index total size in bytes (8 bytes) — supports > 4 GB block indexes
        public long BlockIndexSize;

        // Offset 0x28: Number of Block_Index_Deltas (4 bytes)
        public int BlockIndexDeltaCount;

        // Offset 0x2C: Block_Index_Delta chain head offset (8 bytes, 0 = no deltas)
        public long BlockIndexDeltaHeadOffset;

        // Offset 0x34: File end offset (8 bytes) - logical end of valid data
        public long FileEndOffset;

        // Offset 0x3C: Shard rotation size (8 bytes, 0 = unlimited single shard)
        public long ShardSize;

        // Offset 0x44: Block size in bytes (4 bytes)
        public int BlockSize;

        // Offset 0x48: Max block keys per offset record (4 bytes)
        public int MaxOffsetBlocks;

        // Offset 0x4C: Total image count including removed (4 bytes)
        public int ImageCount;

        // Offset 0x50: Directory region capacity in bytes (4 bytes)
        public int DirectoryRegionCapacity;

        // Offset 0x54: HeaderChecksum (8 bytes) - XXHash64 of bytes 0x00–0x53
        // Offset 0x5C–0xFF: Reserved (zero-filled)

        /// <summary>
        /// Validates the header has correct magic bytes, a supported version,
        /// and structurally valid pointer values.
        /// </summary>
        public bool IsValid()
        {
            if (Magic != MagicBytes)
                return false;

            if (MajorVersion != CurrentMajorVersion)
                return false;

            if (MinorVersion > CurrentMinorVersion)
                return false;

            // Minimum file size is 512 bytes (two 256-byte headers)
            if (ImageDirectoryOffset < HeaderSize * 2)
                return false;

            if (BlockIndexOffset < HeaderSize * 2)
                return false;

            if (FileEndOffset < HeaderSize * 2)
                return false;

            if (ImageDirectorySize < 0)
                return false;

            if (ImageDirectoryUncompressedSize < 0)
                return false;

            if (BlockIndexSize < 0)
                return false;

            if (BlockIndexDeltaCount < 0)
                return false;

            if (BlockSize <= 0)
                return false;

            if (MaxOffsetBlocks <= 0)
                return false;

            if (ImageCount < 0)
                return false;

            if (ShardSize < 0)
                return false;

            if (DirectoryRegionCapacity < 0)
                return false;

            return true;
        }
    }
}