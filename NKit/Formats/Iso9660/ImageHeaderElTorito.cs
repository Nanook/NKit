using System;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class ImageHeaderElTorito
    {


        public ImageHeaderElTorito()
        {
        }

        public long ImageOffset { get; set; }
        public int PvdIndex { get; set; }
        public long SectionEntryOffset { get; set; }

        internal bool EnumEntries(byte[] data, int offset, int size, int blockFsSize, Action<long, long> entry, out int fileSizeOut)
        {
            // Section Header indicators (El Torito 1.0 / EDK2 ElTorito.h): 0x90 = header with more
            // headers to follow, 0x91 = final header. This walk handles the multi-header chain and
            // returns incomplete only if the last header seen was 0x90 (promised a header that never
            // arrived) — i.e. it already matches the spec. (Only lightly exercised against multi-section
            // catalogs, e.g. UEFI+BIOS hybrid ISOs — verified by inspection, sample confirmation pending.)
            fileSizeOut = size;

            int sz = 0x60 + (data.ReadUInt16L(offset + 0x42) * 0x20); //0x20 for Validation Entry + 0x20 for Initial/Default Entry + 0x20 for Section Header Entry
            int cnt = 1;
            int lastHeaderType = int.MaxValue; //not set
            for (int i = 0x20; i < size; i += 0x20)
            {
                if (cnt == 0) // read header
                {
                    lastHeaderType = data.Read8(offset + i);
                    cnt = data.ReadUInt16L(offset + i + 0x2);
                    if (lastHeaderType == 0 && cnt == 0)
                    {
                        fileSizeOut = i;
                        break;
                    }
                }
                else
                {
                    entry((long)data.ReadUInt16L(offset + i + 0x8) * (long)blockFsSize, (long)data.ReadUInt16L(offset + i + 0x6) * 0x200L);
                    cnt--;
                }
            }

            return cnt == 0 && lastHeaderType != 0x90; //is complete
        }
    }

}