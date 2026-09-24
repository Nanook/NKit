namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents a single entry in the Image Directory.
    /// Contains all metadata needed for image lookups and file offset pointers
    /// to both the Image_Metadata_Section and Image_BlockMap_Section.
    /// </summary>
    internal class ImageDirectoryEntry
    {
        /// <summary>
        /// Unique identifier for the image within the set.
        /// </summary>
        public long ImageId;

        /// <summary>
        /// Image name (UTF-8, up to 512 characters).
        /// </summary>
        public string Name = string.Empty;

        /// <summary>
        /// Total uncompressed size of the image in bytes.
        /// </summary>
        public long Size;

        /// <summary>
        /// CRC32 checksum of the image content.
        /// </summary>
        public uint Crc32;

        /// <summary>
        /// XXHash64 of the image content.
        /// </summary>
        public ulong XxHash64;

        /// <summary>
        /// System identifier (e.g., "Wii", "GameCube"). Null if not specified.
        /// </summary>
        public string? System;

        /// <summary>
        /// The output format of the image.
        /// </summary>
        public ImageFormat Format;

        /// <summary>
        /// Shard file ID at time of image insertion for rollback support. Null if not applicable.
        /// </summary>
        public int? RollbackFileId;

        /// <summary>
        /// Shard file offset at time of image insertion for rollback support. Null if not applicable.
        /// </summary>
        public long? RollbackOffset;

        /// <summary>
        /// Whether this image has been soft-deleted.
        /// </summary>
        public bool Removed;

        /// <summary>
        /// File offset of the Image_Metadata_Section within the .nkds file.
        /// </summary>
        public long MetadataSectionOffset;

        /// <summary>
        /// Compressed size of the Image_Metadata_Section in bytes.
        /// </summary>
        public int MetadataSectionCompressedSize;

        /// <summary>
        /// File offset of the Image_BlockMap_Section within the .nkds file.
        /// </summary>
        public long BlockMapSectionOffset;

        /// <summary>
        /// Compressed size of the Image_BlockMap_Section in bytes.
        /// </summary>
        public int BlockMapSectionCompressedSize;

        /// <summary>
        /// Uncompressed size of the Image_Metadata_Section in bytes.
        /// Used for pre-allocating decompression buffers.
        /// </summary>
        public int MetadataSectionUncompressedSize;

        /// <summary>
        /// Uncompressed size of the Image_BlockMap_Section in bytes.
        /// Used for pre-allocating decompression buffers.
        /// </summary>
        public int BlockMapSectionUncompressedSize;
    }
}