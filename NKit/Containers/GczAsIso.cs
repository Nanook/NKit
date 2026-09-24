using Nanook.GrindCore;
using Nanook.GrindCore.DeflateZLib;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class GczAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private ContainerType _format;
        private long _size;
        private uint _sumBlockSize;   // captured for FormatSummary
        private uint _sumBlocks;

        private ZLibBlock _zlib;
        private ImageBlockReader<object> _blockReader;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;
        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadUInt32L(0) == 0xB10BC001)
                return new GczAsIso();
            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.Gcz;
            byte[] data = _stream.ReadBytes(0x20);

            long compSize = (long)data.ReadUInt64L(0x8);
            _size = (long)data.ReadUInt64L(0x10);
            uint blockSize = data.ReadUInt32L(0x18);
            uint blocks = data.ReadUInt32L(0x1C);
            _sumBlockSize = blockSize;
            _sumBlocks = blocks;

            byte[] pnt = _stream.ReadBytes(blocks * 8);
            byte[] hsh = _stream.ReadBytes(blocks * 4);
            ulong fsOffset = (ulong)(pnt.Length + hsh.Length + 0x20);

            _zlib = new ZLibBlock(new CompressionOptions() { BlockSize = blockSize });
            _blockReader = new ImageBlockReader<object>(true, false, false, 10, 10);
            for (int i = 0; i < blocks; i++)
            {
                ulong offset = pnt.ReadUInt64L(i * 8); //offset not including lookup table
                _blockReader.AddItem((offset & 0x7ffffffffffffffful) + fsOffset, blockSize, (offset & 0x8000000000000000ul) == 0 ? ImageBlockType.Compressed : ImageBlockType.Raw, null);
            }
            _blockReader.CompletedAddItems(true, (ulong)compSize + fsOffset, (int)blockSize, (ulong)this.Size, false);

            _position = 0;
            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count, null,
                (blockData, threadId) =>
                {
                    int blockFullSize = blockData.Info.FullSize;
                    if (blockData.Info.Type == ImageBlockType.Compressed)
                        _zlib.Decompress(blockData.Buff, 0, blockData.BuffSize, blockData.Result, 0, ref blockFullSize);
                    else
                        Array.Copy(blockData.Buff, blockData.Result, blockData.Info.FullSize);
                },
                out _
            );
            _position += (long)bytesRead;
            return bytesRead;
        }

        public long Size => _size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Gcz)}size 0x{_size:X} block 0x{_sumBlockSize:X} blocks {_sumBlocks} table@0x20 (ptr {_sumBlocks * 8}B + hash {_sumBlocks * 4}B)";

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

        protected override void Dispose(bool disposing)
        {
            try { _zlib?.Dispose(disposing); } catch { }
            // Release the per-image block reader's buffers (per-block Items + cached decompressed
            // blocks + per-thread temp/result buffers). It is not IDisposable and GczAsIso.Dispose
            // previously left it rooted, so a .gcz image's block buffers lingered until GC — a large
            // native/LOH residual that never dropped between images in a long-lived process (the UI).
            try { _blockReader?.Release(); } catch { }
            _blockReader = null;
            base.Dispose(disposing);
        }

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