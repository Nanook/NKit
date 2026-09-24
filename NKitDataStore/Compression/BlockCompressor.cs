using System.Diagnostics;

namespace NKitDataStore.Compression
{
    /// <summary>
    /// Multi-format block compressor supporting Zstandard and uncompressed storage.
    /// Automatically selects the best compression format for each block.
    /// Uncompressed blocks are stored without a prefix byte.
    /// Uses buffer reuse for efficiency. Not thread-safe - use one instance per thread.
    /// </summary>
    public sealed class BlockCompressor : IBlockCompressor
    {
        private static readonly byte[] _ZstdFrameMagic = [0x28, 0xB5, 0x2F, 0xFD];

        private readonly int _blockSize;
        private readonly byte[] _compressionBuffer;
        private readonly byte[] _decompressionInputBuffer;
        private readonly Dictionary<CompressionType, ICompressionCodec> _codecs;
        private readonly IReadOnlyList<CompressionType> _supportedTypes;
        private bool _disposed;
        private readonly object _sync = new object();

        /// <summary>
        /// Creates a new block compressor with all available codecs.
        /// </summary>
        /// <param name="blockSize">The standard block size (e.g., 65536 bytes).</param>
        public BlockCompressor(int blockSize)
        {
            _blockSize = blockSize;
            _compressionBuffer = new byte[blockSize + 0x200]; // Block + max overhead + prefix
            _decompressionInputBuffer = new byte[blockSize + 0x200];

            // Register codecs in preference order (best compression first)
            _codecs = new Dictionary<CompressionType, ICompressionCodec>
            {
                { CompressionType.Zstd, new ZstdCodec(blockSize) },
                { CompressionType.None, new UncompressedCodec() }
            };

            // Try compression types in this order
            _supportedTypes = new List<CompressionType>
            {
                CompressionType.Zstd,
                CompressionType.None
            };
        }

        /// <summary>
        /// Gets the supported compression types in preference order.
        /// </summary>
        public IReadOnlyList<CompressionType> SupportedTypes => _supportedTypes;

        private void ensureActive()
        {
            if (!_disposed) return;
            lock (_sync)
            {
                if (!_disposed) return;
                try
                {
                    Trace.WriteLine($"BlockCompressor: reinitializing codecs after prior Dispose. StackTrace at reinit:\n{Environment.StackTrace}");
                }
                catch { }

                // Recreate codec instances (same as constructor)
                Dictionary<CompressionType, ICompressionCodec> codecs = new Dictionary<CompressionType, ICompressionCodec>
                {
                    { CompressionType.Zstd, new ZstdCodec(_blockSize) },
                    { CompressionType.None, new UncompressedCodec() }
                };

                // Assign back to fields
                // Note: swap the dictionary contents in-place to avoid changing references held elsewhere
                _codecs.Clear();
                foreach (KeyValuePair<CompressionType, ICompressionCodec> kv in codecs)
                    _codecs[kv.Key] = kv.Value;

                // supported types list
                // if original list reference exists and is mutable, update it; otherwise replace
                try
                {
                    // _supportedTypes was set to List<CompressionType> in ctor; try to cast and update
                    if (_supportedTypes is List<CompressionType> list)
                    {
                        list.Clear();
                        list.Add(CompressionType.Zstd);
                        list.Add(CompressionType.None);
                    }
                }
                catch { }

                _disposed = false;
            }
        }

        /// <summary>
        /// Compresses a block using the best available compression algorithm.
        /// Tries each codec in preference order and only stores compressed data when the
        /// compressed payload plus its type byte is smaller than the original source.
        /// </summary>
        public ReadOnlyMemory<byte> Compress(byte[] source, int sourceOffset, int sourceLength, out CompressionType compressionType)
        {
            if (_disposed)
                ensureActive();

            // Try compression algorithms in preference order
            foreach (CompressionType type in _supportedTypes)
            {
                if (type == CompressionType.None)
                    continue; // Skip uncompressed, try it last

                if (_codecs.TryGetValue(type, out ICompressionCodec? codec))
                {
                    try
                    {
                        int compressedSize = _compressionBuffer.Length - 1; // Reserve space for prefix
                        if (!codec.Compress(source, sourceOffset, sourceLength, _compressionBuffer, 1, ref compressedSize))
                            continue;

                        int storedCompressedSize = compressedSize;
                        if (type == CompressionType.Zstd)
                        {
                            if (compressedSize < _ZstdFrameMagic.Length)
                                continue;

                            Buffer.BlockCopy(_compressionBuffer, 1 + _ZstdFrameMagic.Length, _compressionBuffer, 1, compressedSize - _ZstdFrameMagic.Length);
                            storedCompressedSize -= _ZstdFrameMagic.Length;
                        }

                        if (storedCompressedSize + 1 < sourceLength)
                        {
                            // Compression successful and smaller than storing the raw data
                            _compressionBuffer[0] = (byte)type;
                            compressionType = type;
                            return new ReadOnlyMemory<byte>(_compressionBuffer, 0, storedCompressedSize + 1);
                        }
                    }
                    catch
                    {
                        // Compression failed, try next codec
                    }
                }
            }

            // Fall back to uncompressed storage
            compressionType = CompressionType.None;
            return new ReadOnlyMemory<byte>(source, sourceOffset, sourceLength);
        }

        /// <summary>
        /// Decompresses a stored block. Compressed blocks use a type prefix byte;
        /// uncompressed blocks are stored as raw bytes with no prefix.
        /// </summary>
        public int Decompress(byte[] source, int sourceOffset, int sourceLength, byte[] destination, int destinationOffset)
        {
            if (_disposed)
                ensureActive();

            if (sourceLength == 0)
                return 0;

            int destinationLength = destination.Length - destinationOffset;
            if (sourceLength == destinationLength)
            {
                Array.Copy(source, sourceOffset, destination, destinationOffset, sourceLength);
                return sourceLength;
            }

            CompressionType compressionType = (CompressionType)source[sourceOffset];

            if (!_codecs.TryGetValue(compressionType, out ICompressionCodec? codec))
                throw new NotSupportedException($"Compression type {compressionType} ({(byte)compressionType}) is not supported by this compressor.");

            if (compressionType == CompressionType.Zstd)
                return decompressZstdWithoutFrameMagic(source, sourceOffset, sourceLength, destination, destinationOffset, codec);

            // Decompress using the appropriate codec
            return codec.Decompress(source, sourceOffset + 1, sourceLength - 1, destination, destinationOffset);
        }

        public int Decompress(byte[] source, int sourceOffset, int sourceLength, int uncompressedLength, byte[] destination, int destinationOffset)
        {
            if (_disposed)
                ensureActive();

            if (sourceLength == 0)
                return 0;

            if (sourceLength == uncompressedLength)
            {
                Array.Copy(source, sourceOffset, destination, destinationOffset, sourceLength);
                return sourceLength;
            }

            CompressionType compressionType = (CompressionType)source[sourceOffset];
            if (!_codecs.TryGetValue(compressionType, out ICompressionCodec? codec))
                throw new NotSupportedException($"Compression type {compressionType} ({(byte)compressionType}) is not supported by this compressor.");

            if (compressionType == CompressionType.Zstd)
                return decompressZstdWithoutFrameMagic(source, sourceOffset, sourceLength, destination, destinationOffset, codec);

            return codec.Decompress(source, sourceOffset + 1, sourceLength - 1, destination, destinationOffset);
        }

        /// <summary>
        /// Gets the maximum compressed size across all codecs (worst case).
        /// </summary>
        public int GetMaxCompressedSize(int uncompressedSize)
        {
            if (_disposed)
                ensureActive();

            int maxSize = uncompressedSize;

            // Check each codec for worst-case size
            foreach (KeyValuePair<CompressionType, ICompressionCodec> codecEntry in _codecs)
            {
                if (codecEntry.Key == CompressionType.None)
                    continue;

                int codecMax = codecEntry.Value.GetMaxCompressedSize(uncompressedSize) + 1; // + prefix byte
                if (codecEntry.Key == CompressionType.Zstd)
                    codecMax -= _ZstdFrameMagic.Length;

                maxSize = Math.Max(maxSize, codecMax);
            }

            return maxSize;
        }

        private int decompressZstdWithoutFrameMagic(byte[] source, int sourceOffset, int sourceLength, byte[] destination, int destinationOffset, ICompressionCodec codec)
        {
            if (sourceLength <= 1)
                return 0;

            int restoredLength = sourceLength + _ZstdFrameMagic.Length;
            if (restoredLength > _decompressionInputBuffer.Length)
                throw new InvalidOperationException($"Compressed Zstd block ({restoredLength} bytes restored) exceeds decompression input buffer size ({_decompressionInputBuffer.Length} bytes).");

            _decompressionInputBuffer[0] = (byte)CompressionType.Zstd;
            Buffer.BlockCopy(_ZstdFrameMagic, 0, _decompressionInputBuffer, 1, _ZstdFrameMagic.Length);
            Buffer.BlockCopy(source, sourceOffset + 1, _decompressionInputBuffer, 1 + _ZstdFrameMagic.Length, sourceLength - 1);

            int result = codec.Decompress(_decompressionInputBuffer, 1, restoredLength - 1, destination, destinationOffset);
            if (result == 0)
                Trace.WriteLine($"[Decompress] FAILED: sourceLength={sourceLength} restoredLength={restoredLength} destLen={destination.Length} destOff={destinationOffset} firstBytes={_decompressionInputBuffer[1]:X2} {_decompressionInputBuffer[2]:X2} {_decompressionInputBuffer[3]:X2} {_decompressionInputBuffer[4]:X2} {_decompressionInputBuffer[5]:X2}");
            return result;
        }

        /// <summary>
        /// Disposes all registered codecs and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    foreach (ICompressionCodec codec in _codecs.Values)
                    {
                        codec.Dispose();
                    }
                }
                catch { }
                _disposed = true;
            }
        }
    }
}