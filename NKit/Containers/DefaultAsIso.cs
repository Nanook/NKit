using System;
using System.IO;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Wraps another IAsIso class and populates the Cache with files in new positions. Gaps are set as Removed sections to be added back in
    /// </summary>
    internal class DefaultAsIso : Stream, IAsIso, IReleasable
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private byte[] _id;
        private IndexFileType _indexFileType;
        private ContainerType _format;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public NKitHeader NKitHeader { get; private set; }
        public static IAsIso Create(byte[] id, IndexFileType type) => new DefaultAsIso(id, type);

        public DefaultAsIso(byte[] id, IndexFileType type)
        {
            _id = id;
            _indexFileType = type;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = _indexFileType == IndexFileType.Cue ? ContainerType.Cue
                    : (_indexFileType == IndexFileType.Gdi ? ContainerType.Gdi
                    : (_id.ReadString(0, 4) == ".SFB" ? ContainerType.Ps3Jb
                    : ContainerType.Iso));
            _position = 0;
            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position != _stream.Position)
                _stream.SafeSeek(_position, SeekOrigin.Begin);

            int bytesRead = _stream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        // Bound the shared source cache. DefaultAsIso reads the raw source (a BufferStream over a
        // possibly forward-only archive entry) directly at its own _position, so the stream's
        // absolute offset equals _position. A consumer reading through this container (e.g. the PS3
        // Fix path) calls ReleaseTo(position) after each forward read to free cached blocks below
        // it — otherwise a 40+ GiB PS3 image inside an archive caches whole and OOMs.
        // A no-op for a seekable source (nothing is cached) or a non-BufferStream source.
        //
        // Clamp the release to this container's OWN read position: as a pass-through the raw source
        // is read strictly forward through every byte, so it must never release past what it has
        // physically read. The consuming Image's release mark is its IMAGE-space position, which
        // can run AHEAD of the raw read — e.g. Iso9660 skip() advances the image position over a
        // skipped filesystem region and releases to the skip target, but the source must still be
        // read forward THROUGH that region to reach it. Releasing those not-yet-read blocks would
        // free data the source has to traverse next, throwing on the catch-up read. (This clamp is
        // pass-through specific and does not affect a decoder container, whose decoded output
        // position legitimately runs ahead of its raw source frontier.)
        public void ReleaseTo(long position)
        {
            if (_stream is BufferStream bs)
                bs.ReleaseTo(Math.Min(position, _position));
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