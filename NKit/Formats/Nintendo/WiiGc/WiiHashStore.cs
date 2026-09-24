using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class WiiHashStore
    {
        private byte[] _flags;
        private readonly MemoryStream _hashes;
        private long _partitionSize;

        public WiiHashStore()
        {
        }

        public WiiHashStore(long partitionFsSize)
        {
            _flags = new byte[intsCount(partitionFsSize) * 4];
            _hashes = new MemoryStream();
        }

        private int intsCount(long partitionFsSize)
        {
            long size = partitionFsSize / WiiConsts.WiiSectorFsSize * WiiConsts.WiiSectorSize;
            _partitionSize = size;
            long groups = (size / WiiConsts.WiiGroupSize) + (size % WiiConsts.WiiGroupSize == 0L ? 0L : 1L);
            return (int)((groups / 32L) + (groups % 32 == 0L ? 0L : 1L));
        }

        public long Preserve(long offset, byte[] decrypted, long size)
        {
            int x = (int)(offset / WiiConsts.WiiGroupSize);
            int byt = x / 8;
            int bit = 1 << (7 - (x % 8));
            _flags.Write8(byt, (byte)(_flags.Read8(byt) | bit));
            long written = 0;

            for (int i = 0; i < size; i += WiiConsts.WiiSectorSize)
            {
                _hashes.Write(decrypted, i, WiiConsts.WiiSectorBlockSize);
                written += WiiConsts.WiiSectorBlockSize;
            }

            return written;
        }

        public bool IsPreserved(long offset)
        {
            int x = (int)(offset / WiiConsts.WiiGroupSize);
            int byt = x / 8;
            if (_flags == null || _flags.Length <= byt)
                return false;

            int bit = 1 << (7 - (x % 8));
            return (_flags.Read8(byt) & bit) != 0;
        }

        public long ReadPatchData(FileSystemInfo fsInfo, Stream stream)
        {
            long hashesSize = 0;
            long hashOff = 0;
            Dictionary<long, long> map = new Dictionary<long, long>();
            for (long off = 0; off < _partitionSize; off += WiiConsts.WiiGroupSize)
            {
                int sz = (int)Math.Min(_partitionSize - off, WiiConsts.WiiGroupSize); //cater for partial last group
                if (IsPreserved(off))
                {
                    map.Add(off, hashOff);
                    hashesSize += sz;
                    hashOff += sz / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorHashSize;
                }
            }

            fsInfo.PreservedHashes = new byte[hashesSize / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorHashSize];
            fsInfo.PreservedHashMap = map;
            stream.Read(fsInfo.PreservedHashes, 0, fsInfo.PreservedHashes.Length);
            return fsInfo.PreservedHashes.Length;
        }

        public void WriteFlagsData(Stream readStream) => _flags = readStream.ReadBytes((int)FlagsLength);

        public byte[] FlagsToByteArray() => _flags;

        public long FlagsLength => _flags.Length;

        public long HashesToStream(Stream output)
        {
            _hashes.Position = 0;
            _hashes.Copy(output, (int)_hashes.Length);
            return _hashes.Length;
        }
    }
}