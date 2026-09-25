using Nanook.GrindCore.XXHash;
using System.Buffers;

namespace NKitDataStore
{
    /// <summary>
    /// A specialized Stream that writes data to an image, automatically chunking into blocks
    /// and handling all block deduplication and offset record creation.
    /// This stream provides a safe, easy-to-use API that prevents consumer errors.
    /// Uses ArrayPool for efficient buffer management.
    /// </summary>
    public class ImageWriteStream : Stream
    {
        private readonly ImageWriter _writer;
        private readonly long _startOffset;
        private readonly long _offsetStart;
        private readonly BlockType _blockType;
        private readonly int _blockSize;
        private readonly DataStride? _stride;
        private readonly long? _strideOriginOffset;
        private readonly List<BlockKey> _blockKeys = new();

        private byte[] _buffer;
        private int _bufferPosition = 1; // Start at 1 to reserve space for compression prefix at index 0
        private long _totalBytesWritten = 0;
        private bool _isDisposed = false;

        internal ImageWriteStream(ImageWriter writer, long startOffset, BlockType blockType, long offsetStart, int blockSize, DataStride? stride = null, long? strideOriginOffset = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _startOffset = startOffset;
            _offsetStart = offsetStart;
            _blockType = blockType;
            _blockSize = blockSize;
            _stride = stride;
            _strideOriginOffset = strideOriginOffset;

            // Rent buffer from ArrayPool with extra byte at start for compression type prefix
            // This allows PrepareBlockForStorage to use the space efficiently
            _buffer = ArrayPool<byte>.Shared.Rent(blockSize + 1);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_isDisposed;
        public override long Length => _totalBytesWritten;
        public override long Position
        {
            get => _totalBytesWritten;
            set => throw new NotSupportedException("ImageWriteStream does not support seeking");
        }

        /// <summary>
        /// Writes data to the stream, automatically chunking into blocks when the buffer fills.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ImageWriteStream));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            int bytesRemaining = count;
            int sourcePosition = offset;

            while (bytesRemaining > 0)
            {
                // How much space is left in the current block buffer?
                // Remember: buffer[0] is reserved for compression prefix, data starts at buffer[1]
                int spaceInBuffer = 1 + _blockSize - _bufferPosition;

                // How much can we write in this iteration?
                int bytesToWrite = Math.Min(bytesRemaining, spaceInBuffer);

                // Copy data into the block buffer
                Array.Copy(buffer, sourcePosition, _buffer, _bufferPosition, bytesToWrite);

                _bufferPosition += bytesToWrite;
                sourcePosition += bytesToWrite;
                bytesRemaining -= bytesToWrite;
                _totalBytesWritten += bytesToWrite;

                // If the buffer is full, flush it as a complete block
                if (_bufferPosition == 1 + _blockSize)
                    flushCurrentBlock();
            }
        }

        /// <summary>
        /// Writes a single byte to the stream.
        /// </summary>
        public override void WriteByte(byte value)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ImageWriteStream));

            _buffer[_bufferPosition++] = value;
            _totalBytesWritten++;

            if (_bufferPosition == 1 + _blockSize)
                flushCurrentBlock();
        }

        /// <summary>
        /// Flushes the current buffer as a complete block.
        /// This is called automatically when the buffer fills to block size.
        /// Passes the buffer directly to WriteBlock with offset 1 to avoid allocations.
        /// Buffer[0] is reserved for the compression prefix.
        /// </summary>
        private void flushCurrentBlock()
        {
            // Defensive check: ensure we haven't been disposed
            if (_isDisposed || _buffer == null)
                return;

            if (_bufferPosition <= 1)
                return; // Nothing to flush (only the reserved byte)

            // Defensive check: validate buffer position is within bounds
            if (_bufferPosition < 1 || _bufferPosition > _buffer.Length)
            {
                throw new InvalidOperationException(
                    $"Buffer position {_bufferPosition} is invalid (buffer length: {_buffer?.Length ?? 0})");
            }

            // Calculate length of actual data (exclude reserved byte at position 0)
            int dataLength = _bufferPosition - 1;

            // Validate data length
            if (dataLength < 0 || dataLength > _blockSize)
            {
                throw new InvalidOperationException(
                    $"Data length {dataLength} is invalid (block size: {_blockSize}, buffer pos: {_bufferPosition})");
            }

            // Calculate hashes on the data range (starting at offset 1)
            uint crc32 = Crc.Compute(_buffer, 1, dataLength);
            ulong xxhash64 = XXHash64.Compute(_buffer, 1, dataLength);
            BlockKey blockKey = new BlockKey(xxhash64, crc32);

            // Store the block (with deduplication)
            // Pass buffer starting at offset 1 (data), length excludes reserved byte
            _writer.WriteBlock(blockKey, _buffer, 1, dataLength);
            //File.AppendAllBytes(@$"c:\temp\block{_startOffset:x9}.bin", _buffer.AsSpan(1, dataLength).ToArray()); // DEBUG

            // Remember the block key for offset record creation
            _blockKeys.Add(blockKey);

            // Reset buffer position for next block (start at 1 again)
            _bufferPosition = 1;
        }

        /// <summary>
        /// Ensures all buffered data is written as blocks.
        /// This is called automatically on Dispose, but can be called explicitly.
        /// </summary>
        public override void Flush()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ImageWriteStream));

            // Flush any partial block
            if (_bufferPosition > 0)
                flushCurrentBlock();
        }

        /// <summary>
        /// Closes the stream, flushes any remaining data, and creates the offset record(s).
        /// Returns the rented buffer to the ArrayPool.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                try
                {
                    // Flush any remaining buffered data
                    if (_bufferPosition > 0)
                        flushCurrentBlock();

                    // Create offset record(s) for all the blocks we wrote
                    //if (_blockKeys.Count > 0) // ALLOW 0 byte files
                    //{
                    //long startForOffsets = _stride != null ? _startOffset + _stride.DataOffset : _startOffset;
                    // Note: ImageWriter.CreateOffsetRecords expects totalSize to be the clean data size.
                    // _totalBytesWritten already represents clean data written by the stream.
                    _writer.CreateOffsetRecords(_startOffset, _totalBytesWritten, _blockType, _blockKeys, _offsetStart, _stride, _strideOriginOffset);
                    //}
                }
                finally
                {
                    // Return rented buffer to pool
                    if (_buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(_buffer);
                        _buffer = null!;
                    }

                    _isDisposed = true;
                }
            }

            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("ImageWriteStream does not support reading");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("ImageWriteStream does not support seeking");

        public override void SetLength(long value) => throw new NotSupportedException("ImageWriteStream does not support setting length");
    }
}