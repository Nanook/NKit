namespace Nanook.NKit
{
    internal class Ecm
    {
        private static byte[] _eccBTable;
        private static byte[] _eccFTable;
        private static uint[] _edcTable;

        internal static void GetModeInfo(byte[] decrypted, int offset, int blockSize, out bool mode1, out bool mode2Form1, out bool mode2Form2)
        {
            mode2Form1 = false;
            mode2Form2 = false;
            mode1 = false;

            if (blockSize == 0x930)
            {
                int mode = decrypted[offset + 0xf];
                if (mode == 2)
                {
                    byte subMode = decrypted.Read8(offset + 0x12);
                    mode2Form2 = (subMode & 0x20) != 0; //form2
                    mode2Form1 = !mode2Form2;
                }
                mode1 = !mode2Form1 && !mode2Form2;
            }
        }

        internal static bool Validate(byte[] decrypted, int blockSize, int size, out uint mode1, out uint mode2Form1, out uint mode2Form2, out uint valid, out uint invalid)
        {
            mode1 = 0;
            mode2Form1 = 0;
            mode2Form2 = 0;
            valid = 0;
            invalid = 0;

            if (blockSize == 0x930)
            {
                for (int i = 0; i < size; i += blockSize)
                {
                    int mode = decrypted[i + 0xf];
                    //byte fileNumber = 0;
                    //byte channelNumber = 0;
                    byte subMode = 0;
                    //byte codingInfo = 0;
                    bool isMode2Form2 = false;
                    bool isMode2Data = false;
                    bool isMode2Audio = false;
                    if (mode == 2)
                    {
                        //fileNumber = decrypted.Read8(i + 0x10);
                        //channelNumber = decrypted.Read8(i + 0x11);
                        subMode = decrypted.Read8(i + 0x12);
                        //codingInfo = decrypted.Read8(i + 0x13);
                        isMode2Form2 = (subMode & 0x20) != 0; //form2
                        isMode2Data = (subMode & 0x08) != 0;
                        isMode2Audio = (subMode & 0x04) != 0;
                        if (isMode2Form2)
                            mode2Form2++;
                        else
                            mode2Form1++;
                        if (!isMode2Audio && !ValidateMode2(decrypted, i, isMode2Form2, isMode2Data))
                        {
                            if (isMode2Form2 && !isMode2Data && decrypted.ReadUInt32L(i + blockSize - 0x4) == 0) // if data and no EDC then this is a thing for CD-i and sometimes PS1 sectors 12 to 15
                                valid++;
                            else
                                invalid++;
                        }
                        else
                            valid++;
                    }
                    else
                    {
                        mode1++;
                        if (!ValidateMode1(decrypted, i))
                            invalid++;
                        else
                            valid++;
                    }
                }
            }
            return invalid == 0; //even if valid = 0 because of size = 0 or blocksize != 930
        }

        static Ecm()
        {
            _eccFTable = new byte[256];
            _eccBTable = new byte[256];
            _edcTable = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint edc = i;
                uint j = (uint)((i << 1) ^ ((i & 0x80) == 0x80 ? 0x11D : 0));
                _eccFTable[i] = (byte)j;
                _eccBTable[i ^ j] = (byte)i;

                for (j = 0; j < 8; j++)
                    edc = (edc >> 1) ^ ((edc & 1) > 0 ? 0xD8018001 : 0);

                _edcTable[i] = edc;
            }
        }

        public static bool ValidateMode1(byte[] sector, int offset)
        {
            if (sector.ReadUInt64L(0x814) != 0) // reserved (8 bytes)
                return false;

            if (!CheckEcc(sector, sector, 86, 24, 2, 86, sector, 0xc + offset, 0x10 + offset, 0x81C + offset)) //correctEccP
                return false;

            if (!CheckEcc(sector, sector, 52, 43, 86, 88, sector, 0xc + offset, 0x10 + offset, 0x81C + 0xAC + offset)) //correctEccQ
                return false;

            uint storedEdc = sector.ReadUInt32L(0x810 + offset);
            uint edc = 0;
            int pos = 0;

            for (int size = 0x810; size > 0; size--)
                edc = (edc >> 8) ^ _edcTable[(edc ^ sector[pos++ + offset]) & 0xFF];

            return edc == storedEdc;
        }

        public static bool ValidateMode2(byte[] sector, int offset, bool isForm2, bool isData)
        {
            int edcOff;
            uint storedEdc;

            if (!isForm2 || isData)
            {
                byte[] zeroAddress = new byte[4];

                if (!CheckEcc(zeroAddress, sector, 86, 24, 2, 86, sector, 0, 0x10 + offset, 0x81C + offset)) //correctEccP
                    return false;

                if (!CheckEcc(zeroAddress, sector, 52, 43, 86, 88, sector, 0, 0x10 + offset, 0x81C + 0xAC + offset)) //correctEccQ
                    return false;

                edcOff = 0x818;
            }
            else
                edcOff = 0x92c;

            storedEdc = sector.ReadUInt32L(edcOff + offset);
            uint edc = 0;
            int pos = 0x10;

            for (int size = edcOff - pos; size > 0; size--)
                edc = (edc >> 8) ^ _edcTable[(edc ^ sector[pos++ + offset]) & 0xFF];

            return edc == storedEdc;
        }

        public static bool CheckEcc(byte[] address, byte[] data, uint majorCount, uint minorCount, uint majorMult, uint minorInc, byte[] ecc, int addressOffset, int dataOffset, int eccOffset)
        {
            uint size = majorCount * minorCount;

            for (uint major = 0; major < majorCount; major++)
            {
                uint idx = ((major >> 1) * majorMult) + (major & 1);
                byte eccA = 0;
                byte eccB = 0;

                for (uint minor = 0; minor < minorCount; minor++)
                {
                    byte tmp = idx < 4 ? address[idx + addressOffset] : data[idx + dataOffset - 4];
                    idx += minorInc;

                    if (idx >= size)
                        idx -= size;

                    eccA ^= tmp;
                    eccB ^= tmp;
                    eccA = _eccFTable[eccA];
                }

                eccA = _eccBTable[_eccFTable[eccA] ^ eccB];

                if (ecc[major + eccOffset] != eccA || ecc[major + majorCount + eccOffset] != (eccA ^ eccB))
                    return false;
            }

            return true;
        }

        public static void WriteEcc(byte[] address, byte[] data, uint majorCount, uint minorCount, uint majorMult, uint minorInc, ref byte[] ecc, int addressOffset, int dataOffset, int eccOffset)
        {
            uint size = majorCount * minorCount;
            uint major;

            for (major = 0; major < majorCount; major++)
            {
                uint idx = ((major >> 1) * majorMult) + (major & 1);
                byte eccA = 0;
                byte eccB = 0;
                uint minor;

                for (minor = 0; minor < minorCount; minor++)
                {
                    byte temp = idx < 4 ? address[idx + addressOffset] : data[idx + dataOffset - 4];
                    idx += minorInc;

                    if (idx >= size)
                        idx -= size;

                    eccA ^= temp;
                    eccB ^= temp;
                    eccA = _eccFTable[eccA];
                }

                eccA = _eccBTable[_eccFTable[eccA] ^ eccB];
                ecc[major + eccOffset] = eccA;
                ecc[major + majorCount + eccOffset] = (byte)(eccA ^ eccB);
            }
        }

        public static void EccWriteSector(byte[] address, byte[] data, ref byte[] ecc, int addressOffset, int dataOffset, int eccOffset)
        {
            WriteEcc(address, data, 86, 24, 2, 86, ref ecc, addressOffset, dataOffset, eccOffset);         // P
            WriteEcc(address, data, 52, 43, 86, 88, ref ecc, addressOffset, dataOffset, eccOffset + 0xAC); // Q
        }

        public static (byte minute, byte second, byte frame) LbaToMsf(long pos) => ((byte)((pos + 150) / 75 / 60), (byte)((pos + 150) / 75 % 60), (byte)((pos + 150) % 75));

        public static long MsfToLba(byte minute, byte second, byte frame) => (((minute * 60) + second) * 75) + frame - 150;

        public static long SectorToLba(byte[] sector, int offset)
        {
            byte minute = (byte)((sector[offset + 0x00C] & 0xF) + ((sector[offset + 0x00C] >> 4) * 10));
            byte second = (byte)((sector[offset + 0x00D] & 0xF) + ((sector[offset + 0x00D] >> 4) * 10));
            byte frames = (byte)((sector[offset + 0x00E] & 0xF) + ((sector[offset + 0x00E] >> 4) * 10));

            return MsfToLba(minute, second, frames);
        }

        public static void ReconstructPrefix(byte[] sector, int offset, bool mode1, long lba)
        {
            sector.WriteUInt64B(offset + 0x0, 0x00FFFFFFFFFFFFFFul); //sync
            sector.WriteUInt32B(offset + 0x8, 0xFFFFFF00u);

            (byte minute, byte second, byte frame) msf = LbaToMsf(lba);

            sector[offset + 0x00C] = (byte)(((msf.minute / 10) << 4) + (msf.minute % 10));
            sector[offset + 0x00D] = (byte)(((msf.second / 10) << 4) + (msf.second % 10));
            sector[offset + 0x00E] = (byte)(((msf.frame / 10) << 4) + (msf.frame % 10));

            if (mode1)
                sector[offset + 0x00F] = 0x01; //mode
            else //mode2
            {
                sector[offset + 0x00F] = 0x02;
                sector.WriteUInt32L(offset + 0x010, sector.ReadUInt32L(offset + 0x014)); //flags
            }
        }

        public static void ReconstructEcc(byte[] sector, int offset, int blockSize) // sector must point to a full 2352-byte sector
        {
            GetModeInfo(sector, offset, blockSize, out bool mode1, out bool mode2Form1, out bool mode2Form2);
            ReconstructEcc(sector, offset, mode1, mode2Form1, mode2Form2);
        }

        public static void ReconstructEcc(byte[] sector, int offset, bool mode1, bool mode2Form1, bool mode2Form2) // sector must point to a full 2352-byte sector
        {
            if (mode1)
                sector.WriteUInt32L(offset + 0x810, ComputeEdc(0, sector, 0x810, offset + 0x0));
            else if (mode2Form1)
                sector.WriteUInt32L(offset + 0x818, ComputeEdc(0, sector, 0x808, offset + 0x010));
            else if (mode2Form2)
                sector.WriteUInt32L(offset + 0x92C, ComputeEdc(0, sector, 0x91C, offset + 0x010));

            byte[] zeroAddress = new byte[4];

            if (mode1)
            {
                sector.WriteUInt64L(offset + 0x814, 0); //reserved
                EccWriteSector(sector, sector, ref sector, offset + 0xC, offset + 0x10, offset + 0x81C);
            }
            else if (mode2Form1)
                EccWriteSector(zeroAddress, sector, ref sector, 0, offset + 0x10, offset + 0x81C);
        }

        public static uint ComputeEdc(uint edc, byte[] src, int size, int srcOffset)
        {
            int pos = srcOffset;

            for (; size > 0; size--)
                edc = (edc >> 8) ^ _edcTable[(edc ^ src[pos++]) & 0xFF];

            return edc;
        }
    }
}