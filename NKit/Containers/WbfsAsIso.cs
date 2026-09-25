using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class WbfsAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private BitState _nkitState;
        private bool _allowSeek;
        private long _position;
        private long _size;
        private ContainerType _format;
        private ImageBlockReader<object> _blockReader;
        private Action<MetaData> _setBlock;

        private int _hdSectorSize;
        private int _wbfsSectorSize;
        private int _wbfsSectors;

        private byte[] _wbfsHdr;
        private byte[] _wbfsDiscHdr;
        private byte[] _wbfsBlocks;
        private bool _isNkit;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;
        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => !_isNkit;

        public WbfsAsIso()
        {
            this.Checksums = new Checksums();
        }

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "WBFS")
                return new WbfsAsIso();
            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.Wbfs;
            _wbfsHdr = new byte[0x200];
            _stream.Read(_wbfsHdr, 0, 0x10);

            _hdSectorSize = 1 << _wbfsHdr.Read8(0x8);
            int offsetShift = _wbfsHdr.Read8(0x9);
            _wbfsSectorSize = 1 << offsetShift;
            _wbfsSectors = (int)_wbfsHdr.ReadUInt32B(0x4) / (_wbfsSectorSize / _hdSectorSize);

            _stream.Read(_wbfsHdr, 0x10, _wbfsHdr.Length - 0x10);

            _wbfsDiscHdr = _stream.ReadBytes(0x100);
            _wbfsBlocks = _stream.ReadBytes(_wbfsSectorSize - (_wbfsHdr.Length + _wbfsDiscHdr.Length));

            int nkitOffset = 0x10000 - (_wbfsDiscHdr.Length + _wbfsHdr.Length);
            _isNkit = _wbfsBlocks.ReadString(nkitOffset, 4) == "NKIT";

            int maxBlocks = (_isNkit ? nkitOffset : _wbfsBlocks.Length) / 2; //amount of 2 byte offsets until end of first sector
            long pointer = 0;

            _blockReader = new ImageBlockReader<object>(true, false, false, 10, 10);
            for (int i = 0; i < maxBlocks && pointer + 1 != (uint)_wbfsSectors; i++)
            {
                pointer = _wbfsBlocks.ReadUInt16B(i << 1);
                _blockReader.AddItem((ulong)pointer << offsetShift, _wbfsSectorSize, (uint)_wbfsSectorSize, pointer == 0 ? ImageBlockType.Missing : ImageBlockType.Raw, null);
            }
            _size = _blockReader.Items.Count * (long)_wbfsSectorSize;

            //bool isWii = _wbfsDiscHdr.ReadUInt32B(0x18) == 0x5d1c9ea3;
            if (_isNkit)
            {
                NKitHeader hdr = new NKitHeader(_wbfsBlocks, nkitOffset);
                if (hdr.HasSize)
                    this.Checksums.Size = _size = hdr.Size;
                if (hdr.HasCrc32)
                    this.Checksums.Crc = hdr.Checksums.Crc;
                if (hdr.HasMd5)
                    this.Checksums.Md5 = hdr.Checksums.Md5;
                if (hdr.HasSha1)
                    this.Checksums.Sha1 = hdr.Checksums.Sha1;
                if (hdr.HasXxhash64)
                    this.Checksums.XxHash = hdr.Checksums.XxHash;
                long blocks = (WiiConsts.FullSizeWii9 / _wbfsSectorSize) + (WiiConsts.FullSizeWii9 % _wbfsSectorSize != 0 ? 1 : 0);
                int bytes = (int)((blocks / 8) + (blocks % 8 != 0 ? 1 : 0));

                _nkitState = new BitState(_wbfsBlocks.Read(nkitOffset + hdr.Length, bytes));

                //if the wbfs header sectors aren't enough to complete the image then add more (this happens with rvt images with extra space on the end) WBFS doesn't store the size
                while (_blockReader.Items.Count * (long)_wbfsSectorSize < _size)
                    _blockReader.AddItem(0, _wbfsSectorSize, (uint)_wbfsSectorSize, ImageBlockType.Missing, null);
            }

            _blockReader.CompletedAddItems(false, 0, (int)_wbfsSectorSize, (ulong)this.Size, false);

            _position = 0;
            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Emit removed-block marks as ABSOLUTE image offsets (invariant of whichever buffer the
            // block reader filled — under BufferStream that is the manager's cache block, not the
            // Image's section). The Image rebases to section-relative when building MissingData.
            // The _nkitState index keeps using the buffer-local `off` (its per-block mapping).
            int callOffset = offset;
            long callPos = _position;
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count,
                 (off, size, type) =>
                {
                    if (type == ImageBlockType.Missing)
                    {
                        long absOff = callPos + (off - callOffset);
                        if (_nkitState != null && _nkitState[(int)((_position + (long)off) / _wbfsSectorSize)])
                            _setBlock(new MetaData(absOff, size, MetaDataType.NJunk, 0, null));
                        else
                            _setBlock(new MetaData(absOff, size, MetaDataType.Fill, 0x0, null));
                    }
                },
                (blockData, threadId) =>
                {
                    if (blockData.Info.Type == ImageBlockType.Missing) //blank
                        Array.Clear(blockData.Result, 0, blockData.Info.FullSize);
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
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Wbfs)}size 0x{_size:X} block 0x{_wbfsSectorSize:X} blocks {_blockReader?.Items.Count ?? _wbfsSectors} hdSector 0x{_hdSectorSize:X}"
            + (_isNkit ? $" nkitHdr:y crc {this.Checksums?.Crc:X8}" : " nkitHdr:n");

        public bool SeekRequired => false;

        public Checksums Checksums { get; }

        public Checksums CustomChecksums() => null;
        public void Complete()
        {
        }

        public void SetRemovedBlock(Action<MetaData> setBlock) => _setBlock = setBlock;

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