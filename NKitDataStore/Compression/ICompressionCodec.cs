namespace NKitDataStore.Compression
{
    /// <summary>
    /// Internal interface for compression codec implementations.
    /// Codecs are registered with BlockCompressor and invoked as needed.
    /// </summary>
    internal interface ICompressionCodec : IDisposable
    {
        /// <summary>
        /// Attempts to compress data.
        /// </summary>
        /// <param name="source">Source data buffer.</param>
        /// <param name="sourceOffset">Offset in source buffer.</param>
        /// <param name="sourceLength">Length of data to compress.</param>
        /// <param name="destination">Destination buffer for compressed data.</param>
        /// <param name="destinationOffset">Offset in destination buffer.</param>
        /// <param name="compressedSize">Size of compressed data (input/output parameter).</param>
        /// <returns>True if compression was successful and beneficial (output size &lt; input size).</returns>
        bool Compress(byte[] source, int sourceOffset, int sourceLength,
                      byte[] destination, int destinationOffset, ref int compressedSize);

        /// <summary>
        /// Decompresses data.
        /// </summary>
        /// <param name="source">Compressed data buffer.</param>
        /// <param name="sourceOffset">Offset in source buffer.</param>
        /// <param name="sourceLength">Length of compressed data.</param>
        /// <param name="destination">Destination buffer for decompressed data.</param>
        /// <param name="destinationOffset">Offset in destination buffer.</param>
        /// <returns>Number of bytes written to destination.</returns>
        int Decompress(byte[] source, int sourceOffset, int sourceLength,
                       byte[] destination, int destinationOffset);

        /// <summary>
        /// Gets the maximum possible compressed size for this codec.
        /// </summary>
        /// <param name="uncompressedSize">Size of uncompressed data.</param>
        /// <returns>Maximum compressed size (worst case scenario).</returns>
        int GetMaxCompressedSize(int uncompressedSize);
    }
}