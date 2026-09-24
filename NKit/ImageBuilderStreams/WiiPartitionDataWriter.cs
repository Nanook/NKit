using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;

namespace Nanook.NKit.Builder
{
    internal class WiiPartitionDataWriter : Stream
    {
        private readonly Stream _output;
        private byte[] _group;
        private int _groupPos;
        private readonly byte[] _h3;
        private readonly byte[] _key;
        private readonly WiiSecurity _enc;
        private readonly byte[] _encData;
        private readonly long _areaOffset;
        private readonly long _length;
        private bool _flushed;

        public Stream BaseStream => _output;

        public WiiPartitionDataWriter(Stream output, byte[] key, byte[] h3Table, long length)
        {
            _output = output;
            _areaOffset = output.Position;
            _length = length;
            _groupPos = 0;
            _group = new byte[WiiConsts.WiiGroupSize];
            _h3 = h3Table;
            _key = key;
            _enc = new WiiSecurity(_group.Length);
            _encData = new byte[_group.Length];
        }

        public void Patch(byte[] buffer, int offset, int count)
        {
            long pos = Position;
            int groupIdx = (int)(Position / _group.Length);
            Position = groupIdx * _group.Length; //move to the start of the group
            long groupOffset = pos - Position;

            _groupPos = (int)Buffer.OffsetToFsOffset(groupOffset, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);

            while (count != 0)
            {
                int size = (int)Math.Min(_group.Length, Length - Position);
                _output.Read(_encData, 0, size);
                _enc.Populate(_key, _encData, _group, size, true, true, false, Position, _h3, null, false);
                _enc.Decrypt();

                int r = Math.Min((size / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorFsSize) - _groupPos, count);
                Write(buffer, offset, r); //uses _groupPos to write in to CiBuffer
                count -= r;

                _groupPos = (int)Buffer.HashedLenToFsLen(size, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize); //force all used group blocks to write
                Position = groupIdx * _group.Length; //move back to start of block
                Flush();
                groupIdx++;
            }
            DataPosition = pos + count; //set the position at the end of the file
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            int i = _groupPos / WiiConsts.WiiSectorFsSize * WiiConsts.WiiSectorSize; //block index
            int p = _groupPos % WiiConsts.WiiSectorFsSize; //pos in block
            int c;

            while (count > 0)
            {
                c = Math.Min(WiiConsts.WiiSectorFsSize - p, count); //is the remaining count < block remaining
                Array.Copy(buffer, offset, _group, i + WiiConsts.WiiSectorHashSize + p, c);
                _flushed = false;
                offset += c;
                _groupPos += c;
                p += c;
                count -= c;
                if (p == WiiConsts.WiiSectorFsSize) //at end of block
                {
                    i += WiiConsts.WiiSectorSize;
                    p = 0;
                    if (i == _group.Length)
                    {
                        _groupPos = 0; //reset before flush to indicate complete block
                        Flush();
                        i = 0;
                    }
                }
            }
        }

        public override void Flush()
        {
            if (!_flushed)
            {
                int blocks = WiiConsts.WiiSectors;
                if (_groupPos != 0)
                    blocks = (_groupPos / WiiConsts.WiiSectorFsSize) + (_groupPos % WiiConsts.WiiSectorFsSize == 0 ? 0 : 1);

                int size = blocks * WiiConsts.WiiSectorSize;

                _enc.Populate(_key, _encData, _group, size, false, false, false, Position, _h3, null, false);
                _enc.MarkDirty();

                _enc.IsValid(true, out bool creatable); //force new hashes
                _enc.UpdateH3Entry();
                _enc.Encrypt();
                _output.Write(_encData, 0, size);
                _flushed = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Flush();
            _output.Flush();
            base.Dispose(disposing);
        }

        public override bool CanRead => false;

        public override bool CanSeek => _output.CanSeek;

        public override bool CanWrite => _output.CanWrite;

        public override long Length => _length == 0 ? _output.Length - _areaOffset : _length;
        public long DataLength => Buffer.HashedLenToFsLen(_length == 0 ? _output.Length - _areaOffset : _length, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
        public override long Position { get => _output.Position - _areaOffset; set => _output.Position = value + _areaOffset; }
        public long DataPosition { get => Buffer.OffsetToFsOffset(Position, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize); set => Position = Buffer.FsOffsetToOffset(value, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, false); }

        public override long Seek(long offset, SeekOrigin origin) => _output.Seek(offset + _areaOffset, origin);

        public override void SetLength(long value) => _output.SetLength(value + _areaOffset);

    }
}