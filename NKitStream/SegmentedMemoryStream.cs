namespace NKitStream
{
    /// <summary>
    /// A growable, seekable, in-memory <see cref="Stream"/> that is not limited by the ~2 GiB
    /// single-array ceiling of the .NET runtime.
    /// <para>
    /// The backing store is a list of fixed-size chunks (default 100 MiB each). Logical
    /// positions and lengths are addressed with <see cref="long"/>; reads and writes that
    /// straddle a chunk boundary are split internally into per-chunk copies. No single copy
    /// (and, in practice, no single caller read/write) exceeds one chunk, and individual
    /// requests are never required to exceed 2 GiB.
    /// </para>
    /// <para>
    /// Chunks are allocated on demand as the logical length grows, so a stream that only ever
    /// holds a few MiB costs a few MiB. This makes it suitable for buffering content whose size
    /// ranges from tiny to many gigabytes (e.g. a decoded Wii disc image) while still allowing
    /// arbitrary random re-reads.
    /// </para>
    /// </summary>
    public sealed class SegmentedMemoryStream : Stream
    {
        /// <summary>Default chunk size: 100 MiB.</summary>
        public const int DefaultChunkSize = 100 * 1024 * 1024;

        private readonly int _chunkSize;
        private readonly List<byte[]> _chunks = new();
        private long _length;
        private long _position;
        private bool _disposed;

        /// <summary>
        /// Create an empty stream with the given chunk size.
        /// </summary>
        /// <param name="chunkSize">Size of each backing chunk in bytes. Must be positive. Default 100 MiB.</param>
        public SegmentedMemoryStream(int chunkSize = DefaultChunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be a positive integer.");
            _chunkSize = chunkSize;
        }

        /// <summary>
        /// Create a stream pre-sized to <paramref name="length"/> bytes (chunks allocated and
        /// zero-initialised up front). Useful when the total size is known and the caller will
        /// fill it via <see cref="Write"/> or <see cref="FillFrom"/>.
        /// </summary>
        public SegmentedMemoryStream(long length, int chunkSize = DefaultChunkSize)
            : this(chunkSize)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
            SetLength(length);
        }

        /// <summary>Size of each backing chunk in bytes.</summary>
        public int ChunkSize => _chunkSize;

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => !_disposed;
        public override long Length { get { throwIfDisposed(); return _length; } }

        public override long Position
        {
            get { throwIfDisposed(); return _position; }
            set
            {
                throwIfDisposed();
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Position cannot be negative.");
                _position = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throwIfDisposed();
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0)
                throw new IOException("Attempted to seek before the start of the stream.");
            _position = target;
            return _position;
        }

        public override void SetLength(long value)
        {
            throwIfDisposed();
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (value > _length)
                ensureCapacity(value);
            // Shrinking keeps allocated chunks (cheap; avoids churn). Trim only whole
            // chunks that are now entirely beyond the new length.
            else if (value < _length)
            {
                int neededChunks = (int)((value + _chunkSize - 1) / _chunkSize);
                while (_chunks.Count > neededChunks)
                    _chunks.RemoveAt(_chunks.Count - 1);
            }

            _length = value;
            if (_position > _length)
                _position = _length;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throwIfDisposed();
            validateArgs(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            throwIfDisposed();

            long available = _length - _position;
            if (available <= 0 || buffer.Length == 0)
                return 0;

            int toRead = (int)Math.Min(buffer.Length, available);
            int copied = 0;
            long pos = _position;

            while (copied < toRead)
            {
                int chunkIdx = (int)(pos / _chunkSize);
                int inChunk = (int)(pos % _chunkSize);
                int chunkAvail = _chunkSize - inChunk;
                int n = Math.Min(chunkAvail, toRead - copied);

                _chunks[chunkIdx].AsSpan(inChunk, n).CopyTo(buffer.Slice(copied, n));
                copied += n;
                pos += n;
            }

            _position += copied;
            return copied;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throwIfDisposed();
            validateArgs(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throwIfDisposed();
            if (buffer.Length == 0)
                return;

            long end = _position + buffer.Length;
            ensureCapacity(end);

            int written = 0;
            long pos = _position;

            while (written < buffer.Length)
            {
                int chunkIdx = (int)(pos / _chunkSize);
                int inChunk = (int)(pos % _chunkSize);
                int chunkAvail = _chunkSize - inChunk;
                int n = Math.Min(chunkAvail, buffer.Length - written);

                buffer.Slice(written, n).CopyTo(_chunks[chunkIdx].AsSpan(inChunk, n));
                written += n;
                pos += n;
            }

            _position += buffer.Length;
            if (_position > _length)
                _length = _position;
        }

        /// <summary>
        /// Fill this stream from a source stream, appending exactly <paramref name="count"/>
        /// bytes at the current position. Reads from the source in chunk-sized pieces so no
        /// intermediate buffer exceeds the chunk size. Advances <see cref="Position"/>.
        /// </summary>
        public void FillFrom(Stream source, long count)
        {
            throwIfDisposed();
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            ensureCapacity(_position + count);

            long remaining = count;
            while (remaining > 0)
            {
                int chunkIdx = (int)(_position / _chunkSize);
                int inChunk = (int)(_position % _chunkSize);
                int chunkAvail = _chunkSize - inChunk;
                int want = (int)Math.Min(chunkAvail, remaining);

                int got = 0;
                while (got < want)
                {
                    int r = source.Read(_chunks[chunkIdx], inChunk + got, want - got);
                    if (r <= 0)
                        throw new EndOfStreamException(
                            $"Source stream ended after {count - remaining + got} of {count} requested bytes.");
                    got += r;
                }

                _position += want;
                remaining -= want;
            }

            if (_position > _length)
                _length = _position;
        }

        public override void Flush() { /* in-memory: nothing to flush */ }

        // ÔöÇÔöÇ internals ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

        private void ensureCapacity(long requiredLength)
        {
            int neededChunks = (int)((requiredLength + _chunkSize - 1) / _chunkSize);
            while (_chunks.Count < neededChunks)
                _chunks.Add(new byte[_chunkSize]);
        }

        private static void validateArgs(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("The sum of offset and count is larger than the buffer length.");
        }

        private void throwIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SegmentedMemoryStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                _chunks.Clear();
                _length = 0;
                _position = 0;
            }
            base.Dispose(disposing);
        }
    }
}