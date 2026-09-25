using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Container
{
    internal class IsoDecAsIso : Stream, IAsIso
    {
        private Stream _stream;
        private bool _allowSeek;
        private long _position;
        private long _size;
        private ContainerType _format;
        private ImageBlockReader<object> _blockReader;
        private Action<MetaData> _setBlock;

        private int _sectorSize;
        private int _sectors;
        private List<uint> _sectorTable;

        private long _isoDecMultiply;
        private int _isoDecShift;

        private long[] _partitionOffsets;
        private long[] _partitionFsOffsets;

        public ContainerType Format => _format;
        public bool Seekable => _stream.CanSeek;

        public long RealPosition => _stream.Position;
        public NKitHeader NKitHeader { get; private set; }

        public long RealSize => _stream.Length;
        public bool SizeEstimated => false;

        public static IAsIso Create(byte[] id)
        {
            if ((new[] { "WII5", "WII9", "GCML" }).Contains(id.ReadString(0, 4)))
                return new IsoDecAsIso();

            return null;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            _stream = stream;
            _allowSeek = allowSeek;

            _format = ContainerType.IsoDec;
            _sectorTable = new List<uint>();

            byte[] temp = _stream.ReadBytes(0x4 + 0x4 + 0x10); //disc ID, MD5
            _sectorTable = new List<uint>();

            this.Checksums = new Checksums();
            this.Checksums[ChecksumType.Md5] = temp.Read(8, 0x10);
            string sid = temp.ReadString(0, 4);

            if (sid == "WII5" || sid == "WII9") //IsoDec
            {
                _isoDecMultiply = 4L;
                _isoDecShift = 8;

                _size = sid == "WII5" ? WiiConsts.FullSizeWii5 : WiiConsts.FullSizeWii9;
                int sectorSize = sid == "WII5" ? 0x1182800 : 0x1FB5000;
                _stream.Read(temp, 0, 4);
                int partitions = (int)temp.ReadUInt32L(0);
                byte[] partition = new byte[32];
                _partitionOffsets = new long[partitions];
                _partitionFsOffsets = new long[partitions];

                for (int i = 0; i < partitions; i++)
                {
                    _stream.Read(partition, 0, partition.Length);
                    _partitionOffsets[i] = partition.ReadUInt32L(8) * _isoDecMultiply;
                    _partitionFsOffsets[i] = _partitionOffsets[i] + (partition.ReadUInt32L(0) * _isoDecMultiply);
                    //var part = new
                    //{
                    //    FsOffset = WipePartition.ReadUInt32L(0)  * _isoDecMultiply,
                    //    //FsSize = WipePartition.ReadUInt32L(4)  * _isoDecMultiply,
                    //    PartitionOffset = WipePartition.ReadUInt32L(8)  * _isoDecMultiply,
                    //    PartitionEndOffset = WipePartition.ReadUInt32L(12) * _isoDecMultiply,
                    //    PartitionKey = WipePartition.Read(16, 16)
                    //};
                }
                ;

                _sectorSize = WiiConsts.WiiSectorBlockSize; //1k
                _sectors = (int)Math.Min(_size / _sectorSize, (sectorSize - (28 + (partitions * 32))) << 2);
            }
            else //if (sid == "GCML") //GC IsoDec
            {
                _isoDecMultiply = 1L;
                _isoDecShift = 0x0;
                _size = WiiConsts.FullSizeGameCube;
                _sectorSize = 0x800; //2k
                _sectors = _sectors = (int)Math.Min(_size / _sectorSize, (0x2B8800 - 24) << 2);
                _partitionOffsets = null;
            }

            _blockReader = new ImageBlockReader<object>(true, false, false, 10, 10);
            byte[] data = _stream.ReadBytes(4 * _sectors);
            ImageBlockType type;
            for (int i = 0; i < _sectors; i++)
            {
                uint off = data.ReadUInt32L(i * 4);
                if (off == 0xffffffffu)
                {
                    type = ImageBlockType.Process; //junk
                    off = 0;
                }
                else if (i != 0 && off == 0)
                    type = ImageBlockType.Missing;
                else
                    type = ImageBlockType.Raw; //decrypted if wii WipePartition
                _blockReader.AddItem(off << _isoDecShift, _sectorSize, (uint)_sectorSize, type, null);
            }

            _blockReader.CompletedAddItems(false, 0, (int)_sectorSize, (ulong)this.Size, false);

            return 0; //keep the default size
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // The block reader reports `off` as a position within the DESTINATION buffer (based at
            // `offset`). Under BufferStream that buffer is the manager's cache block, not the
            // Image's section, so a buffer-relative offset is meaningless downstream. Convert to an
            // ABSOLUTE image offset (_position + (off - offset)) so the removed-block mark is
            // invariant of which physical buffer received the bytes; the Image rebases it to
            // section-relative when building MissingData.
            int callOffset = offset;
            long callPos = _position;
            int bytesRead = _blockReader.Read(_position, _stream, buffer, offset, count,
                (off, size, type) =>
                {
                    long absOff = callPos + (off - callOffset);
                    if (type == ImageBlockType.Missing)
                        _setBlock(new MetaData(absOff, size, MetaDataType.Fill, 0x0, null));
                    else if (type == ImageBlockType.Process)
                        _setBlock(new MetaData(absOff, size, MetaDataType.NJunk, 0, null));
                },
                (blockData, threadId) =>
                {
                    if (blockData.Info.Type == ImageBlockType.Missing || blockData.Info.Type == ImageBlockType.Process) //blank
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
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.IsoDec)}size 0x{_size:X} block 0x{_sectorSize:X} blocks {_blockReader?.Items.Count ?? _sectors}"
            + (this.Checksums?.Md5 != null ? $" md5 {this.Checksums.Md5.ToHexString().Substring(0, 8)}…" : "");

        public bool SeekRequired => false;

        public Checksums Checksums { get; private set; }

        public Checksums CustomChecksums() => null;
        public void Complete()
        {
        }

        public bool HasCustomChecksum => false;

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