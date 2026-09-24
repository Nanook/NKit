using System;

namespace Nanook.NKit.Chd
{
    internal class CRC
    {
        public static readonly uint[] CRC32Lookup;
        private uint _crc;
        private long _totalBytesRead;

        static CRC()
        {
            const uint polynomial = 0xEDB88320;
            const int crcNumTables = 8;

            unchecked
            {
                CRC32Lookup = new uint[256 * crcNumTables];
                int i;
                for (i = 0; i < 256; i++)
                {
                    uint r = (uint)i;
                    for (int j = 0; j < 8; j++)
                    {
                        r = (r >> 1) ^ (polynomial & ~((r & 1) - 1));
                    }

                    CRC32Lookup[i] = r;
                }

                for (; i < 256 * crcNumTables; i++)
                {
                    uint r = CRC32Lookup[i - 256];
                    CRC32Lookup[i] = CRC32Lookup[r & 0xFF] ^ (r >> 8);
                }
            }
        }


        public CRC()
        {
            Reset();
        }

        public void Reset()
        {
            _totalBytesRead = 0;
            _crc = 0xffffffffu;
        }


        internal void UpdateCRC(int inCh) => _crc = (_crc >> 8) ^ CRC32Lookup[(byte)_crc ^ ((byte)inCh)];

        public void SlurpBlock(byte[] block, int offset, int count)
        {
            _totalBytesRead += count;
            uint crc = _crc;

            for (; (offset & 7) != 0 && count != 0; count--)
                crc = (crc >> 8) ^ CRC32Lookup[(byte)crc ^ block[offset++]];

            if (count >= 8)
            {
                int end = (count - 8) & ~7;
                count -= end;
                end += offset;

                while (offset != end)
                {
                    crc ^= (uint)(block[offset] + (block[offset + 1] << 8) + (block[offset + 2] << 16) + (block[offset + 3] << 24));
                    uint high = (uint)(block[offset + 4] + (block[offset + 5] << 8) + (block[offset + 6] << 16) + (block[offset + 7] << 24));
                    offset += 8;

                    crc = CRC32Lookup[(byte)crc + 0x700]
                          ^ CRC32Lookup[(byte)(crc >>= 8) + 0x600]
                          ^ CRC32Lookup[(byte)(crc >>= 8) + 0x500]
                          ^ CRC32Lookup[ /*(byte)*/(crc >> 8) + 0x400]
                          ^ CRC32Lookup[(byte)high + 0x300]
                          ^ CRC32Lookup[(byte)(high >>= 8) + 0x200]
                          ^ CRC32Lookup[(byte)(high >>= 8) + 0x100]
                          ^ CRC32Lookup[ /*(byte)*/(high >> 8) + 0x000];
                }
            }

            while (count-- != 0)
            {
                crc = (crc >> 8) ^ CRC32Lookup[(byte)crc ^ block[offset++]];
            }

            _crc = crc;

        }

        public byte[] Crc32ResultB
        {
            get
            {
                byte[] result = BitConverter.GetBytes(~_crc);
                Array.Reverse(result);
                return result;
            }
        }
        public Int32 Crc32Result => unchecked((Int32)~_crc);

        public uint Crc32ResultU => ~_crc;

        public Int64 TotalBytesRead => _totalBytesRead;

        public static uint CalculateDigest(byte[] data, uint offset, uint size)
        {
            CRC crc = new CRC();
            // crc.Init();
            crc.SlurpBlock(data, (int)offset, (int)size);
            return crc.Crc32ResultU;
        }

        public static bool VerifyDigest(uint digest, byte[] data, uint offset, uint size) => CalculateDigest(data, offset, size) == digest;
    }
}
