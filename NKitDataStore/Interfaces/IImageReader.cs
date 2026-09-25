namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Provides read-only operations for a single, specific image.
    /// Instances of this interface are created via IDataStore.OpenImageReader.
    /// 
    /// Thread-Safety:
    /// - Each ImageReader instance can be used from multiple threads, but operations
    ///   on the same database connection are serialized (no performance penalty for typical use).
    /// - For maximum concurrency, create separate ImageReader instances per thread.
    ///   Creating readers is fast (~0.1ms) due to connection pooling.
    /// - Example:
    ///   Parallel.For(0, 10, i => {
    ///       using var reader = store.OpenImageReader(key);  // Own connections per thread
    ///       // Fully concurrent access
    ///   });
    /// </summary>
    public interface IImageReader : IDisposable
    {
        /// <summary>
        /// Gets the metadata record for the image being read.
        /// </summary>
        ImageRecord Image { get; }

        /// <summary>
        /// Gets the set metadata from the info table.
        /// Contains schema version, block size, shard configuration, and storage parameters.
        /// </summary>
        InfoRecord Info { get; }

        /// <summary>
        /// Opens a readable stream for a specific logical group within the image.
        /// The stream reconstructs data from all offset records with the matching offsetStart value.
        /// Position 0 in the stream corresponds to the group's starting offset in the image.
        /// </summary>
        /// <param name="offsetStart">The starting offset that identifies the group.</param>
        /// <returns>A readable, seekable stream representing the grouped data (file, XFiller, etc.).</returns>
        /// <example>
        /// <code>
        /// // Extract a specific file/group
        /// using (var groupStream = reader.OpenStream(offsetStart: 0))
        /// using (var outputFile = File.Create("extracted_file.bin"))
        /// {
        ///     groupStream.CopyTo(outputFile);
        /// }
        /// </code>
        /// </example>
        Stream OpenStream(long offsetStart);

        /// <summary>
        /// Opens a readable stream that reconstructs the strided format from stored clean data.
        /// The stream reads clean data and re-applies padding/hashes according to the stride pattern.
        /// Use this to reconstruct original disc formats (CD sectors, Wii blocks, etc.) from stored data.
        /// 
        /// Note: This method requires an offsetStart to specify which file group to apply the stride to.
        /// You cannot apply stride to the entire image - stride is file-group specific.
        /// </summary>
        /// <param name="stride">The stride pattern to apply when reading data.</param>
        /// <param name="offsetStart">The starting offset that identifies which file group to read with stride applied.</param>
        /// <returns>A readable stream with strided format (clean data + padding/hashes).</returns>
        /// <example>
        /// <code>
        /// // Reconstruct a specific file in Wii disc format with hashes
        /// using (var wiiStream = reader.OpenStream(stride: DataStride.Wii, offsetStart: 0))
        /// using (var outputFile = File.Create("file_with_hashes.bin"))
        /// {
        ///     wiiStream.CopyTo(outputFile);
        /// }
        /// // Result: 0x400 byte hashes inserted every 0x7C00 bytes of data
        /// 
        /// // Reconstruct CD Mode 1 sectors for a specific file
        /// using (var cdStream = reader.OpenStream(stride: DataStride.CdMode1, offsetStart: 0))
        /// {
        ///     // Data is reconstructed as 2352-byte sectors with sync/header/ECC
        /// }
        /// </code>
        /// </example>
        Stream OpenStream(DataStride stride, long offsetStart);

        /// <summary>
        /// Retrieves all offset records that construct the complete image.
        /// Use this for advanced scenarios where you need fine-grained control over data reconstruction.
        /// </summary>
        IEnumerable<OffsetRecord> GetOffsets();

        /// <summary>
        /// Retrieves all offset records that belong to a specific logical group within the image.
        /// All offset records with the same offsetStart value belong to the same group.
        /// This is useful for extracting individual files, XFiller regions, or other grouped data.
        /// </summary>
        /// <param name="offsetStart">The starting offset that identifies the group.</param>
        /// <returns>All offset records for the specified group, ordered by offset.</returns>
        IEnumerable<OffsetRecord> GetOffsets(long offsetStart);

        /// <summary>
        /// Retrieves offset records that fall within a specified byte range of the image.
        /// </summary>
        IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length);

        /// <summary>
        /// Retrieves a data block as a byte array by its unique key.
        /// This method handles decompression automatically.
        /// </summary>
        BlockRecord? GetBlock(BlockKey key);

        /// <summary>
        /// Opens a readable stream for a single data block's content by its unique key.
        /// The returned stream provides decompressed data.
        /// </summary>
        Stream? OpenBlockStream(BlockKey key);

        /// <summary>
        /// Retrieves all area records for this image.
        /// Areas define logical regions within the disc image (e.g., partitions, headers).
        /// </summary>
        IEnumerable<AreaRecord> GetAreas();

        /// <summary>
        /// Reads a named file from the data store, decompressing it from the shard files.
        /// </summary>
        /// <param name="name">The logical name of the file.</param>
        /// <returns>The decompressed file data, or null if the file does not exist.</returns>
        byte[]? ReadFile(string name);

        /// <summary>
        /// Lists all file records stored for this image.
        /// </summary>
        IEnumerable<FileRecord> ListFiles();
    }
}