namespace NKitDataStore.Compression
{
    /// <summary>
    /// Pass-through codec for uncompressed data storage.
    /// Used when compression is not beneficial or not desired.
    /// </summary>
    internal sealed class UncompressedCodec : ICompressionCodec
    {
        /// <summary>
        /// This codec never compresses (returns false to indicate compression not beneficial).
        /// The BlockCompressor will handle storing data uncompressed.
        /// </summary>
        public bool Compress(byte[] source, int sourceOffset, int sourceLength,
                            byte[] destination, int destinationOffset, ref int compressedSize) =>
            // Never compress - BlockCompressor will handle this case by storing raw data
            false;

        /// <summary>
        /// Decompresses uncompressed data (direct copy).
        /// </summary>
        public int Decompress(byte[] source, int sourceOffset, int sourceLength,
                             byte[] destination, int destinationOffset)
        {
            // Direct copy - no actual decompression needed
            Array.Copy(source, sourceOffset, destination, destinationOffset, sourceLength);
            return sourceLength;
        }

        /// <summary>
        /// Gets the maximum compressed size (same as input for uncompressed).
        /// </summary>
        public int GetMaxCompressedSize(int uncompressedSize) => uncompressedSize; // No overhead for uncompressed data

        /// <summary>
        /// No resources to dispose.
        /// </summary>
        public void Dispose()
        {
            // No-op
        }
    }
}