using System;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal static class NJunk
    {
        public const int JunkBlockSize = 0x40000;
        public const int JunkBaseJunkInts = 17;
        public const int JunkBaseJunkIntsFull = 521;


        //nulls areas that junk is not required for e.g. when fsSize is not a multiple of 0x8000 (given that fs data blocks are 0x7c00)
        public static void Fill(byte[] id, int disc, long startOffset, long fullSize, long fsOffset, byte[] buffer)
        {
            if (buffer.Length != JunkBlockSize)
                throw new Exception(string.Format("Buffer must have a length of Junk.JunkSize - 0x{0}", JunkBlockSize.ToString("X8")));

            if (startOffset > fsOffset + buffer.Length)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return;
            }

            int junkSize = (int)Math.Min(Math.Max(0, fullSize - (fullSize % WiiConsts.WiiSectorSize) - fsOffset), buffer.Length);

            uint seed = Stage1Seed(id, (byte)disc);
            uint[] baseJunk = null;
            for (int i = 0; i < junkSize; i += WiiConsts.WiiSectorSize)
            {
                Stage2BaseJunk(seed, fsOffset + i, false, ref baseJunk);
                Stage3BaseJunkFill(baseJunk);
                Stage4Prep(baseJunk);
                Stage5WriteToBuffer(baseJunk, buffer, i, WiiConsts.WiiSectorSize);
            }

            if (buffer.Length - junkSize != 0)
                Array.Clear(buffer, junkSize, buffer.Length - junkSize);

            if (startOffset > fsOffset)
                Array.Clear(buffer, 0, (int)(startOffset - fsOffset));
        }

        public static uint Stage1Seed(byte[] id, byte disc) => (uint)((((id[2] << 8) | id[1]) << 16) | ((id[3] + id[2]) << 8) | (id[0] + id[1])) ^ disc;

        public static uint[] Stage2BaseJunk(uint seed, long fsOffset, bool baseJunkOnly, ref uint[] baseJunk)
        {
            if (baseJunk == null)
                baseJunk = new uint[baseJunkOnly ? JunkBaseJunkInts : JunkBaseJunkIntsFull];
            uint x = (seed * 0x260bcd5) ^ (((uint)(fsOffset / JunkBlockSize) * 8u * 0x1ef29123u) + (0x1ef29123u * (uint)(fsOffset % JunkBlockSize / WiiConsts.WiiSectorSize)));

            for (int i = 0; i < JunkBaseJunkInts; i++)
            {
                uint val = 0;
                for (int c = 0; c < 32; c++)
                {
                    x *= 0x5d588b65;
                    val = (val >> 1) | (++x & 0x80000000);
                }
                baseJunk[i] = val;
            }
            baseJunk[16] ^= (baseJunk[0] >> 9) ^ (baseJunk[16] << 23);
            return baseJunk;
        }

        public static void Stage3BaseJunkFill(uint[] baseJunk)
        {
            for (int i = 17; i < JunkBaseJunkIntsFull; i++)
                baseJunk[i] = (baseJunk[i - 17] << 23) ^ (baseJunk[i - 16] >> 9) ^ baseJunk[i - 1];
        }

        public static void Stage4Prep(uint[] baseJunk)
        {
            for (int c = 0; c < 4; c++)
                gen(baseJunk);
        }

        public static void Stage5WriteToBuffer(uint[] baseJunk, byte[] buffer, int offset, int size)
        {
            if (size > WiiConsts.WiiSectorSize)
                size = WiiConsts.WiiSectorSize;

            int end = offset + size;

            for (int idx = 0, i = offset; i < end; i += 4)
            {
                if (i != offset && ++idx == JunkBaseJunkIntsFull)
                {
                    gen(baseJunk);
                    idx = 0;
                }
                buffer[i] = (byte)(baseJunk[idx] >> 0x18);
                buffer[i + 1] = (byte)(baseJunk[idx] >> 0x12); //possibly a bug in the original (should possibly have been 0x10)
                buffer[i + 2] = (byte)(baseJunk[idx] >> 8);
                buffer[i + 3] = (byte)baseJunk[idx];
            }
        }

        public static void Expand(uint[] baseJunk, byte[] buffer, int offset, int size)
        {
            Stage3BaseJunkFill(baseJunk);
            Stage4Prep(baseJunk);
            Stage5WriteToBuffer(baseJunk, buffer, offset, size);
        }

        public static bool Shrink(byte[] buffer, int offset, long fsOffset, int size, ref uint[] baseJunk, bool allowNulls)
        {
            //find the first location % (521 << 2) where (17 << 2) bytes match bits. The junkBase can be derrived from there
            if (size < (JunkBaseJunkIntsFull << 2))
                return false;

            int m = size / (JunkBaseJunkIntsFull << 2);

            for (int gen = 0, off = offset; gen < m; gen++)
            {
                if (isJunkQuickPass(buffer, off, allowNulls))
                {
                    if (getUnprep(buffer, off, gen, ref baseJunk))
                        return true;
                }
                off += JunkBaseJunkIntsFull << 2;
            }
            return false;
        }

        private static bool isJunkQuickPass(byte[] buffer, int offset, bool allowNulls)
        {
            for (int i = 0; i < JunkBaseJunkIntsFull; i++, offset += 4)
            {
                uint v = buffer.ReadUInt32B(offset);
                if (!allowNulls && v == 0 && buffer.ReadUInt32B(offset + 4) == 0) //test first 2 ints.
                    return false;
                if ((byte)((v >> 0x18) & 0x3) != (byte)((v >> 0x16) & 0x3))
                    return false;
            }
            return true;
        }

        private static bool getUnprep(byte[] buffer, int offset, int gen, ref uint[] baseJunk)
        {
            if (baseJunk == null)
                baseJunk = new uint[JunkBaseJunkIntsFull];
            int off = gen * (JunkBaseJunkIntsFull << 2);
            for (int i = 0; i < JunkBaseJunkIntsFull; i++, off += 4)
                baseJunk[i] = buffer.ReadUInt32B(off);

            for (int i = 0; i < gen + 4; i++)
                ungen(baseJunk);

            //fix the base junk
            for (int i = 0; i < 17; i++)
                baseJunk[i] = (baseJunk[i] & 0xFF00FFFF) | ((baseJunk[i] << 2) & 0x00FC0000) | (((baseJunk[i + 16] ^ baseJunk[i + 15]) << 9) & 0x00030000);

            for (int i = 17; i < JunkBaseJunkIntsFull; i++)
            {
                uint x = (baseJunk[i - 17] << 23) ^ (baseJunk[i - 16] >> 9) ^ baseJunk[i - 1];
                if (baseJunk[i] != ((x & 0xFF00FFFF) | ((x >> 2) & 0x00FF0000))) //test the rest of the base junk and reproduce the bad >> 12 shift
                    return false;
                baseJunk[i] = x; //fix the bad data
            }

            return true; //in a state ready for Stage4Prep
        }

        private static void gen(uint[] junk)
        {
            int i = -1;
            while (++i != 32) //0-31
                junk[i] ^= junk[i + (521 - 32)];
            i--;
            while (++i != JunkBaseJunkIntsFull) //32-520
                junk[i] ^= junk[i - 32];
        }

        private static void ungen(uint[] junk)
        {
            int i = JunkBaseJunkIntsFull;
            while (--i != 31) //520-32
                junk[i] ^= junk[i - 32];
            i++;
            while (--i != -1) //31-0
                junk[i] ^= junk[i + (521 - 32)];
        }
    }
}