using Nanook.GrindCore.DeflateZLib;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class DaxAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private ContainerType _format;
        private long _size;

        private ZLibBlock _zlib;
        private ImageBlockReader<object> _blockReader;

        private int _sectorSize;
        private int _sectors;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "DAX\0")
                return new DaxAsIso();
            return null;
        }

        public DaxAsIso()
        {
            this.Checksums = new Checksums();
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.Dax;

            byte[] hdr = _stream.ReadBytes(0x20);
            _sectorSize = 0x2000;
            int frameShift = 13;
            _size = hdr.ReadUInt32L(0x4);
            uint v = hdr.ReadUInt32L(0x8);
            if (v > 1)
                throw new HandledException($"{_format.ToString().ToUpper()} version {v} is not supported. Raise an issue to have it added.");
            int daxNc = v == 1 ? (int)hdr.ReadUInt32L(0xc) : 0;
            _sectors = (int)((_size + _sectorSize - 1) >> frameShift);
            int daxSzOffset = _sectors * 4; //4=block offset
            int daxNcOffset = daxSzOffset + (_sectors * 2); //2=block size)
            byte[] table = _stream.ReadBytes(daxNcOffset + (daxNc * 0x8)); //Non Compressible Frames 4 index 4 count
            uint offset = 0; //4 byte offsets
            uint sz = 0;

            //read block information
            _zlib = new ZLibBlock(new GrindCore.CompressionOptions() { Type = GrindCore.CompressionType.Optimal, BlockSize = _sectorSize });
            _blockReader = new ImageBlockReader<object>(true, false, false, 10, 10);
            for (int i = 0; i < _sectors; i++)
            {
                offset = table.ReadUInt32L(i << 2); //4 byte offsets
                sz = table.ReadUInt16L(daxSzOffset + (i << 1));
                _blockReader.AddItem(offset, (int)sz, (uint)_sectorSize, ImageBlockType.Compressed, null);
            }
            for (int ncIdx = daxNcOffset; ncIdx < table.Length; ncIdx += 8) //4 index, 4 count
            {
                int ncStart = (int)table.ReadUInt32L(ncIdx);
                int ncCount = (int)table.ReadUInt32L(ncIdx + 4);
                for (int c = 0; c < ncCount; c++)
                    _blockReader.Items[ncStart + c].Type = ImageBlockType.Raw;
            }
            _blockReader.CompletedAddItems(false, 0, (int)_sectorSize, (ulong)this.Size, false); //use temp if headerless zlib

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
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Dax)}size 0x{_size:X} block 0x{_sectorSize:X} blocks {_blockReader?.Items.Count ?? _sectors}";

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

        public Checksums CustomChecksums() => null;

        public void Complete()
        {
        }

        public void SetRemovedBlock(Action<MetaData> setBlock)
        {
        }

        public override void Flush() => _stream.Flush();

        protected override void Dispose(bool disposing)
        {
            try { _zlib?.Dispose(disposing); } catch { }
            // Release the per-image block reader buffers (see ImageBlockReader.Release) so a .dax
            // image's block/cache buffers do not linger past image completion.
            try { _blockReader?.Release(); } catch { }
            _blockReader = null;
            base.Dispose(disposing);
        }

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