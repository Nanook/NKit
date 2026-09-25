using System.IO;

namespace Nanook.NKit
{
    //Class to provide various hacks to fix things
    internal class HackStream : Stream
    {
        private readonly Stream _stream;
        private long _length;
        private long _position;
        private readonly long _basePos;
        private readonly int _forceBlockSize;
        private readonly bool _leaveOpen;

        public HackStream(Stream stream, bool leaveOpen)
        {
            _leaveOpen = leaveOpen;
            _stream = stream;
            _length = 0;
            _basePos = stream.CanRead ? stream.Position : 0;
            _position = _basePos;
            _forceBlockSize = 0;
        }

        public HackStream(int forceGczReadBugFix, Stream stream, bool leaveOpen)
        {
            _leaveOpen = leaveOpen;
            _stream = stream;
            _length = 0;
            _basePos = stream.CanRead ? stream.Position : 0;
            _forceBlockSize = forceGczReadBugFix;
        }

        private void setPos(long p)
        {
            _position = p;
            if (_position > _length)
                _length = _position;
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _length;

        public override long Position { get => _position; set { _stream.Position = value; setPos(value); } }

        public override void Flush()
        {
            //_stream.Flush();
        }

        public override void Close()
        {
            if (!_leaveOpen)
                base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_leaveOpen)
                base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int c = 0;
            if (_forceBlockSize != 0) //blocked zlib - don't read more than the block in the base stream.
                c = _forceBlockSize;
            else
            {
                if (_stream.Position - _basePos + count > _length)
                    c = (int)(_stream.Position - _basePos + count - _length);
                else
                    c = count;
            }
            setPos(_position + (long)c);
            return _stream.Read(buffer, offset, c);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long p = _position;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    p = offset;
                    break;
                case SeekOrigin.Current:
                    p += offset;
                    break;
                case SeekOrigin.End:
                    p = Length + offset;
                    break;
            }
            setPos(p);
            return _stream.Seek(p, SeekOrigin.Begin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
            _length = value;
            setPos(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
            setPos(_position + count);
        }
    }
}