namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Provides write operations for a single, new image.
    /// An IImageWriter instance represents a single, atomic transaction.
    /// The transaction is committed when the writer is disposed, but only if FinalizeImage has been called.
    /// If disposed without FinalizeImage being called, or if an exception occurs, the transaction is rolled back.
    /// </summary>
    public interface IImageWriter : IImageReader
    {
        /// <summary>
        /// Begins writing data to the image at the specified offset using a streaming approach.
        /// Returns a Stream that automatically handles block chunking, deduplication, and offset record creation.
        /// This is the recommended API as it prevents consumer errors with block boundaries.
        /// </summary>
        /// <param name="offset">The byte offset within the image where data will be written.</param>
        /// <param name="type">The type of data being written (default: File).</param>
        /// <param name="offsetStart">The starting offset for grouping. All chunks with the same offsetStart belong to the same logical group. Defaults to offset.</param>
        /// <param name="stride">Optional stride configuration for partitioned formats (Wii, WiiU, CD). When provided, the stream will create strided offset records.</param>
        /// <returns>A writable stream that handles all block management automatically. Dispose to finalize.</returns>
        /// <example>
        /// <code>
        /// // Write complete file in one stream
        /// using (var stream = writer.BeginWriteStream(0, BlockType.File))
        /// {
        ///     sourceData.CopyTo(stream);
        /// } // Blocks and offsets created automatically on dispose
        /// 
        /// // Write multi-chunk file with explicit grouping
        /// using (var stream1 = writer.BeginWriteStream(0, BlockType.File, offsetStart: 0))
        ///     stream1.Write(chunk1); // offsetStart = 0
        /// using (var stream2 = writer.BeginWriteStream(50000, BlockType.File, offsetStart: 0))
        ///     stream2.Write(chunk2); // offsetStart = 0 (same group as chunk1)
        /// </code>
        /// </example>
        Stream BeginWriteStream(long offset, BlockType type = BlockType.File, long? offsetStart = null, DataStride? stride = null, long? strideOriginOffset = null);

        /// <summary>
        /// Writes data from a stream to the image at the specified offset.
        /// Data is automatically chunked into blocks based on the set's block size.
        /// This is a one-shot write - all data is processed immediately.
        /// </summary>
        /// <param name="offset">The byte offset within the image.</param>
        /// <param name="data">The source data stream.</param>
        /// <param name="length">The number of bytes to read from the stream.</param>
        /// <param name="type">The type of blocks to create (default: File).</param>
        /// <param name="stride">Optional stride configuration for reading interleaved/padded data.</param>
        /// <param name="offsetStart">The starting offset for grouping. Defaults to offset.</param>
        void WriteData(long offset, Stream data, long length, BlockType type = BlockType.File, DataStride? stride = null, long? offsetStart = null);

        /// <summary>
        /// Writes data from a byte array to the image at the specified offset.
        /// Data is automatically chunked into blocks based on the set's block size.
        /// This is a one-shot write - all data is processed immediately.
        /// </summary>
        /// <param name="offset">The byte offset within the image.</param>
        /// <param name="data">The data to write.</param>
        /// <param name="type">The type of blocks to create (default: File).</param>
        /// <param name="offsetStart">The starting offset for grouping. Defaults to offset.</param>
        void WriteData(long offset, byte[] data, BlockType type = BlockType.File, long? offsetStart = null);

        /// <summary>
        /// Writes a subset of data from a byte array to the image at the specified offset.
        /// Data is automatically chunked into blocks based on the set's block size.
        /// This is a one-shot write - all data is processed immediately.
        /// </summary>
        /// <param name="offset">The byte offset within the image.</param>
        /// <param name="data">The source data array.</param>
        /// <param name="dataOffset">The offset within the data array to start reading from.</param>
        /// <param name="length">The number of bytes to write from the data array.</param>
        /// <param name="type">The type of blocks to create (default: File).</param>
        /// <param name="stride">Optional stride configuration for handling interleaved/padded data.</param>
        /// <param name="offsetStart">The starting offset for grouping. Defaults to offset.</param>
        void WriteData(long offset, byte[] data, int dataOffset, int length, BlockType type = BlockType.File, DataStride? stride = null, long? offsetStart = null);

        /// <summary>
        /// Records a verifiable virtual data region (data that can be regenerated algorithmically).
        /// The data is read to calculate checksums but is not physically stored.
        /// </summary>
        /// <param name="offset">The byte offset within the image.</param>
        /// <param name="data">The source data stream (read for checksums only).</param>
        /// <param name="length">The number of bytes.</param>
        /// <param name="type">The type of virtual data (e.g., NJunk, Other).</param>
        void WriteVerifiableVirtualData(long offset, Stream data, long length, BlockType type);

        /// <summary>
        /// Records a verifiable virtual data region from a byte array.
        /// </summary>
        /// <param name="offset">The byte offset within the image.</param>
        /// <param name="data">The data (read for checksums only).</param>
        /// <param name="type">The type of virtual data.</param>
        void WriteVerifiableVirtualData(long offset, byte[] data, BlockType type);

        /// <summary>
        /// Creates a new area record for a logical region of the disc image.
        /// Areas define checksummed regions for data validation.
        /// </summary>
        /// <param name="offset">The byte offset where the area starts.</param>
        /// <param name="size">The size of the area in bytes.</param>
        /// <param name="crc32">The CRC32 hash of the area's data.</param>
        /// <param name="xxhash64">The XXHash64 hash of the area's data.</param>
        /// <param name="sectionSize">The section size used for offset management (required).</param>
        /// <param name="metadata">Optional metadata for the area.</param>
        /// <returns>The ID of the newly created area.</returns>
        long CreateArea(long offset, long size, uint crc32, ulong xxhash64, int sectionSize, AreaMetadata? metadata = null);

        /// <summary>
        /// Creates a new area record with stride information for reconstructing block structure with hashes.
        /// This is used for Wii/WiiU partitions where data is stored clean but must be reconstructed with hashes.
        /// </summary>
        /// <param name="offset">The byte offset where the area starts.</param>
        /// <param name="size">The size of the area in bytes (with stride applied).</param>
        /// <param name="crc32">The CRC32 hash of the area's data.</param>
        /// <param name="xxhash64">The XXHash64 hash of the area's data.</param>
        /// <param name="strideBlockSize">The size of each strided block (e.g., 0x8000 for Wii).</param>
        /// <param name="strideDataOffset">Offset within each block where clean data starts (e.g., 0x400 for Wii hash area).</param>
        /// <param name="strideDataLength">Length of clean data within each block (e.g., 0x7C00 for Wii data area).</param>
        /// <param name="sectionSize">The section size used for offset management (required).</param>
        /// <param name="metadata">Optional metadata for the area.</param>
        /// <returns>The ID of the newly created area.</returns>
        long CreateArea(long offset, long size, uint crc32, ulong xxhash64,
            int strideBlockSize, int strideDataOffset, int strideDataLength,
            int sectionSize, AreaMetadata? metadata = null);

        /// <summary>
        /// Updates the metadata for an existing area.
        /// </summary>
        /// <param name="areaId">The ID of the area to update.</param>
        /// <param name="metadata">The new metadata.</param>
        void UpdateAreaMetadata(long areaId, AreaMetadata metadata);

        /// <summary>
        /// Finalizes the image by setting its final size and checksums.
        /// This must be called before disposing the writer to commit the transaction.
        /// </summary>
        /// <param name="size">The total size of the image in bytes.</param>
        /// <param name="crc32">The CRC32 hash of the entire image.</param>
        /// <param name="xxhash64">The XXHash64 hash of the entire image.</param>
        void FinalizeImage(long size, uint crc32, ulong xxhash64);

        /// <summary>
        /// True when the last FinalizeImage detected that an identical image (same name + CRC32 +
        /// XXHash64) already exists in the set and therefore did NOT finalize — the pending image
        /// will be rolled back on Dispose. Lets callers distinguish a duplicate (already stored)
        /// from a genuine failure so they can report it appropriately instead of appearing stuck.
        /// </summary>
        bool AlreadyExists { get; }

        /// <summary>
        /// Writes a named file to the data store, compressed and stored alongside block data in shard files.
        /// The file is zstd compressed as a whole and tracked by the file table (independent of blocks/offsets).
        /// </summary>
        /// <param name="name">The logical name for the file.</param>
        /// <param name="data">The uncompressed file data.</param>
        void WriteFile(string name, byte[] data, bool isSystem = false);

        /// <summary>
        /// Controls the maximum parallel background compression operations the writer will allow.
        /// Default is 8 when not changed.
        /// </summary>
        int CompressionParallelism { get; set; }
    }
}