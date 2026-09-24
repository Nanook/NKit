using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;

namespace Nanook.NKit.Container
{
    internal class CisoAsIso : Stream, IAsIso
    {
        private Stream _stream;

        private BitState _nkitState;
        private bool _allowSeek;
        private long _position;
        private long _size;
        private int _lastUsedIdx;
        private ContainerType _format;
        private ImageBlockReader<object> _blockReader;
        private Checksums _checksums;
        private Action<MetaData> _setBlock;

        private int _cisoSectorSize;
        private int _cisoSectors;

        private bool _nkitHeaderChecked;
        public bool IsNkit { get; private set; }
        public bool NkitHeaderNotUsed { get; private set; } //not used in when seek not available
        private long _nkitHeaderPos;

        private byte[] _cisoHdr;

        public NKitHeader NKitHeader { get; private set; }
        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;

        public long RealSize => _stream.Length;
        public bool SizeEstimated => !IsNkit;

        public static IAsIso Create(byte[] id)
        {
            if (id.ReadString(0, 4) == "CISO" && id.ReadUInt32L(0x4) >= 0x100) // < is cso
                return new CisoAsIso();
            return null;
        }

        public CisoAsIso()
        {
            _checksums = new Checksums();
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;
            _nkitHeaderChecked = false;

            _format = ContainerType.Ciso;
            _cisoHdr = _stream.ReadBytes(0x8);
            _cisoSectorSize = (int)_cisoHdr.ReadUInt32L(4);
            _cisoSectors = (int)(WiiConsts.WiiSectorSize - _stream.Position); //1 byte per cluster
            byte[] data = _stream.ReadBytes(_cisoSectors);
            //_sectorTable = new List<uint>();

            long maxPointer = 0;
            //long blank = 0;
            //long blankTmp = 0;
            long fileSize = _stream.Length;
            _size = WiiConsts.WiiSectorSize; //ciso header
            _lastUsedIdx = 0;

            _blockReader = new ImageBlockReader<object>(true, false, false, 10, 10);
            for (int i = 0; i < _cisoSectors; i++)
            {
                bool isMissing = data.Read8(i) != 1;

                _blockReader.AddItem((ulong)(isMissing ? 0 : _size), _cisoSectorSize, (uint)_cisoSectorSize, isMissing ? ImageBlockType.Missing : ImageBlockType.Raw, null);

                if (!isMissing)
                {
                    _lastUsedIdx = _blockReader.Items.Count - 1;
                    if (_size < fileSize)
                        maxPointer = _size;
                    _size += _cisoSectorSize;
                }
            }
            _size = _blockReader.Items.Count * (long)_cisoSectorSize;

            //test for nkit header on the end of the file
            _nkitHeaderPos = maxPointer + (long)_cisoSectorSize;
            byte[] nkitHdr = testNkit(_nkitHeaderPos, true);
            if (IsNkit)
            {
                NKitHeader hdr = new NKitHeader(nkitHdr, 0);
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
                _nkitState = new BitState(nkitHdr.Read(hdr.Length, nkitHdr.Length - hdr.Length));
                this.NKitHeader = hdr;

                //if the wbfs header sectors aren't enough to complete the image then add more (this happens with rvt images with extra space on the end) WBFS doesn't store the size
                while (_blockReader.Items.Count * (long)_cisoSectorSize < _size)
                    _blockReader.AddItem(0, _cisoSectorSize, (uint)_cisoSectorSize, ImageBlockType.Missing, null);
            }

            _blockReader.CompletedAddItems(false, 0, (int)_cisoSectorSize, (ulong)this.Size, false);

            _position = 0;
            return 0; //keep the default size
        }

        private byte[] testNkit(long headerPos, bool seek)
        {
            int size = 0x40 + (int)(WiiConsts.FullSizeWii9 / _cisoSectorSize / 8);
            if (!_nkitHeaderChecked && this.RealSize >= headerPos + (long)size && (!seek || _stream.CanSeek))
            {
                long p = _stream.Position;
                _stream.SafeSeek(headerPos, SeekOrigin.Begin);
                byte[] hdr = new byte[size];
                _stream.Read(hdr, 0, size);
                if (seek)
                    _stream.Position = p;
                this.IsNkit = hdr.ReadString(0, 4) == "NKIT";
                _nkitHeaderChecked = true;
                if (this.IsNkit)
                    return hdr;
            }
            return null;
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
                        if (_nkitState != null && _nkitState[(int)((_position + (long)off) / _cisoSectorSize)])
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


            if (!_nkitHeaderChecked && !this.IsNkit && _stream.Position >= (long)_blockReader.Items[_lastUsedIdx].Offset + (long)_blockReader.Items[_lastUsedIdx].Size)
            {
                testNkit(_nkitHeaderPos, false);
                if (this.IsNkit)
                    this.NkitHeaderNotUsed = true;
                _nkitHeaderChecked = true;
            }

            _position += (long)bytesRead;
            return bytesRead;
        }

        public long Size => _size;

        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.Ciso)}size 0x{_size:X} block 0x{_cisoSectorSize:X} blocks {_blockReader?.Items.Count ?? _cisoSectors} hdr@0x{_nkitHeaderPos:X}"
            + (IsNkit ? $" nkitHdr:y crc {_checksums?.Crc:X8}" : " nkitHdr:n");

        public bool SeekRequired => false;

        public Checksums Checksums => _checksums;

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