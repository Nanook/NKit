using Nanook.GrindCore;
using System.Diagnostics;

namespace NKitDataStore.Compression
{
    /// <summary>
    /// Zstandard (Zstd) compression codec implementation.
    /// Wraps the NKit GrindCore Zstd compressor for use with the block compression system.
    /// </summary>
    internal sealed class ZstdCodec : ICompressionCodec
    {
        private readonly CompressionBlock _compressor;
        private bool _disposed;

        /// <summary>
        /// Creates a new Zstandard codec.
        /// </summary>
        /// <param name="blockSize">The standard block size for compression.</param>
        public ZstdCodec(int blockSize)
        {
            _compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = blockSize,
                    Type = (Nanook.GrindCore.CompressionType)19  // Zstd compression level
                }
            );
        }

        /// <summary>
        /// Attempts to compress data using Zstandard.
        /// </summary>
        /// <returns>True if compression was successful and resulted in smaller size.</returns>
        public bool Compress(byte[] source, int sourceOffset, int sourceLength,
                            byte[] destination, int destinationOffset, ref int compressedSize)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ZstdCodec));

            try
            {
                // Attempt compression
                _compressor.Compress(source, sourceOffset, sourceLength,
                                    destination, destinationOffset, ref compressedSize);

                // Only use compression if it resulted in size reduction
                return compressedSize < sourceLength;
            }
            catch
            {
                // Compression failed
                return false;
            }
        }

        /// <summary>
        /// Decompresses Zstandard-compressed data.
        /// </summary>
        public int Decompress(byte[] source, int sourceOffset, int sourceLength,
                             byte[] destination, int destinationOffset)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ZstdCodec));

            int decompressedSize = destination.Length - destinationOffset;
            try
            {
                _compressor.Decompress(source, sourceOffset, sourceLength,
                                      destination, destinationOffset, ref decompressedSize);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ZstdCodec.Decompress] EXCEPTION: {ex.Message} srcOff={sourceOffset} srcLen={sourceLength} dstOff={destinationOffset} dstAvail={destination.Length - destinationOffset}");
                decompressedSize = 0;
            }
            return decompressedSize;
        }

        /// <summary>
        /// Gets the maximum possible compressed size for Zstandard.
        /// In the worst case, Zstd adds a small amount of overhead.
        /// </summary>
        public int GetMaxCompressedSize(int uncompressedSize) =>
            // Zstd worst-case: original size + compression overhead (~256 bytes is safe)
            uncompressedSize + 0x100;

        /// <summary>
        /// Disposes the underlying Zstandard compressor.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _compressor?.Dispose();
                _disposed = true;
            }
        }
    }
}