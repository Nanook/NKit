using System;
using System.IO;

namespace Nanook.NKit.Container.ZArchive
{
    /// <summary>
    /// Seekable, read-only stream over a single file entry within a <see cref="ZArchiveReader"/>.
    /// Used to expose individual .app / .tmd / .cetk files to the NKit CDN processing path.
    /// </summary>
    internal class WuaFileStream : Stream
    {
        private readonly ZArchiveReader _reader;
        private readonly int _entryIndex;
        private readonly long _fileSize;
        private long _position;

        internal WuaFileStream(ZArchiveReader reader, int entryIndex, long fileSize)
        {
            _reader     = reader;
            _entryIndex = entryIndex;
            _fileSize   = fileSize;
            _position   = 0;
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => true;
        public override bool CanWrite => false;
        public override long Length   => _fileSize;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _fileSize)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _fileSize || count <= 0) return 0;
            int read = _reader.ReadByEntry(_entryIndex, _position, buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos = origin switch
            {
                SeekOrigin.Begin   => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End     => _fileSize + offset,
                _                  => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (newPos < 0) throw new IOException("Seek before beginning of stream.");
            _position = newPos;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value)  => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
