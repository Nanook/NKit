using System;
using System.IO;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Wraps another IAsIso class and populates the Cache with files in new positions. Gaps are set as Removed sections to be added back in
    /// </summary>
    internal class TmdAppAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private IndexFile _indexFile;
        private ContainerType _format;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public NKitHeader NKitHeader { get; private set; }
        public static IAsIso Create(byte[] id, IndexFile index)
        {
            if (index != null && index.FileType == IndexFileType.TmdApp)
                return new TmdAppAsIso(id, index);
            return null;
        }

        public TmdAppAsIso(byte[] id, IndexFile index)
        {
            _indexFile = index;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.TmdApp;
            _position = 0;
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position != _stream.Position)
                _stream.SafeSeek(_position, SeekOrigin.Begin);

            int bytesRead = _stream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        public long Size => _stream.Length;

        public bool SeekRequired => false;

        public Checksums Checksums { get; private set; }

        public Checksums CustomChecksums() => null;
        public void Complete()
        {
        }

        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
        }

        public override void Flush() => _stream.Flush();

        public override long Position { get => _position; set => _position = value; }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current)
                _position += offset;
            else if (origin == SeekOrigin.End)
                throw new NotSupportedException();
            else
                _position = offset;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => this.Size;
    }
}