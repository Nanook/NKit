namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// A simple read-only stream wrapper that exposes a window (start/length) of an underlying seekable stream.
    /// Disposing this stream will NOT dispose the underlying stream - the caller that provided
    /// the base stream is responsible for its lifetime (e.g., mounted readers managed by VfsModel).
    /// </summary>
    internal class BoundedStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _start;
        private readonly long _length;
        private readonly bool _ownsBaseStream;
        private long _position;
        private bool _disposed;

        public BoundedStream(Stream baseStream, long start, long length, bool ownsBaseStream = false)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if (!baseStream.CanSeek)
                throw new ArgumentException("Base stream must be seekable", nameof(baseStream));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _start = start;
            _length = length;
            _ownsBaseStream = ownsBaseStream;
            _position = 0;
            // Position base stream at start
            _baseStream.Position = _start;
        }

        public override bool CanRead => !_disposed && _baseStream.CanRead;
        public override bool CanSeek => !_disposed && _baseStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
                _baseStream.Position = _start + _position;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BoundedStream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
            if (_position >= _length) return 0;
            long remaining = _length - _position;
            int toRead = (int)Math.Min(count, remaining);
            _baseStream.Position = _start + _position;
            int read = _baseStream.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BoundedStream));
            long newPos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };
            if (newPos < 0 || newPos > _length) throw new ArgumentOutOfRangeException(nameof(offset));
            _position = newPos;
            _baseStream.Position = _start + _position;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing && _ownsBaseStream)
                {
                    try { _baseStream.Dispose(); } catch { }
                }
            }
            base.Dispose(disposing);
        }
    }
}