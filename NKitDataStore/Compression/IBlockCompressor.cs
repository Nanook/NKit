namespace NKitDataStore.Compression
{
    /// <summary>
    /// Abstracts block compression/decompression with support for multiple formats.
    /// Automatically selects the best compression algorithm and handles format detection.
    /// Thread-safety: Implementations are NOT thread-safe. Use one instance per thread for parallel operations.
    /// </summary>
    public interface IBlockCompressor : IDisposable
    {
        /// <summary>
        /// Compresses a block using the best available compression algorithm.
        /// </summary>
        /// <param name="source">Source data buffer.</param>
        /// <param name="sourceOffset">Offset in source buffer.</param>
        /// <param name="sourceLength">Length of data to compress.</param>
        /// <param name="compressionType">The compression type used (output parameter).</param>
        /// <returns>
        /// A memory segment containing the stored block bytes.
        /// Compressed blocks include a 1-byte compression type prefix; uncompressed blocks do not.
        /// WARNING: The returned buffer may be reused on the next call. Copy data immediately if needed.
        /// </returns>
        ReadOnlyMemory<byte> Compress(byte[] source, int sourceOffset, int sourceLength, out CompressionType compressionType);

        /// <summary>
        /// Decompresses stored block data.
        /// This overload is intended for cases where the caller does not know the expected
        /// uncompressed size. It can safely distinguish full-size raw blocks from prefixed
        /// compressed blocks, but callers with block length metadata should use the overload
        /// that accepts the expected uncompressed size.
        /// </summary>
        /// <param name="source">Stored block data.</param>
        /// <param name="sourceOffset">Offset in source buffer.</param>
        /// <param name="sourceLength">Length of stored block data.</param>
        /// <param name="destination">Destination buffer for decompressed data.</param>
        /// <param name="destinationOffset">Offset in destination buffer.</param>
        /// <returns>The number of bytes written to destination.</returns>
        /// <exception cref="NotSupportedException">Thrown if compression type is not supported.</exception>
        int Decompress(byte[] source, int sourceOffset, int sourceLength, byte[] destination, int destinationOffset);

        /// <summary>
        /// Decompresses stored block data using the caller's known uncompressed size.
        /// If the stored length matches <paramref name="uncompressedLength"/>, the data is raw.
        /// Otherwise, the block is treated as compressed and is decoded using its prefix byte.
        /// </summary>
        /// <param name="source">Stored block data.</param>
        /// <param name="sourceOffset">Offset in source buffer.</param>
        /// <param name="sourceLength">Length of stored block data.</param>
        /// <param name="uncompressedLength">Expected uncompressed block length from metadata.</param>
        /// <param name="destination">Destination buffer for decompressed data.</param>
        /// <param name="destinationOffset">Offset in destination buffer.</param>
        /// <returns>The number of bytes written to destination.</returns>
        /// <exception cref="NotSupportedException">Thrown if compression type is not supported.</exception>
        int Decompress(byte[] source, int sourceOffset, int sourceLength, int uncompressedLength, byte[] destination, int destinationOffset);

        /// <summary>
        /// Gets the maximum compressed size for a given uncompressed size.
        /// Used for buffer allocation.
        /// </summary>
        int GetMaxCompressedSize(int uncompressedSize);

        /// <summary>
        /// Gets the supported compression types in order of preference (best compression first).
        /// </summary>
        IReadOnlyList<CompressionType> SupportedTypes { get; }
    }
}