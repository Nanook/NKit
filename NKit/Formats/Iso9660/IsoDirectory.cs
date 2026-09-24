using System;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class IsoDirectory
    {
        public static IsoDirectory Parse(byte[] data, int offset)
        {
            IsoDirectory d = new IsoDirectory()
            {
                Length = data.Read8(offset + 0x00),
                XAttrLen = data.Read8(offset + 0x01),
                Extent = Math.Max(data.ReadUInt32L(offset + 0x02), data.ReadUInt32B(offset + 0x06)),
                Size = Math.Max(data.ReadUInt32L(offset + 0x0a), data.ReadUInt32B(offset + 0x0e)),
                RawDate = data.Read(offset + 0x12, 7),
                Date = DateTime.MinValue,
                Flags = (FileFlags)data.Read8(offset + 0x19),
                FileUnitSize = data.Read8(offset + 0x1a),
                Interleave = data.Read8(offset + 0x1b),
                VolumeSequenceNumber = Math.Max(data.ReadUInt16L(offset + 0x1c), data.ReadUInt16B(offset + 0x1e)),
                NameLen = data.Read8(offset + 0x20),
                Name = data.Read8(offset + 0x20) <= 0 ? "" : data.ReadString(offset + 0x21, data.Read8(offset + 0x20) - 1)
            };
            if (d.RawDate[0] != 0)
            {
                try
                {
                    d.Date = new DateTime(1900 + d.RawDate[0], d.RawDate[1], d.RawDate[2], d.RawDate[3], d.RawDate[4], d.RawDate[5]);  //d.RawDate[6] //GmtOffset
                }
                catch
                {
                    d.Date = new DateTime(1900, 1, 1, 0, 0, 0);
                }
            }

            return d;
        }

        public byte Length { get; private set; }
        public byte XAttrLen { get; private set; }
        public uint Extent { get; private set; }
        public uint Size { get; private set; }
        public byte[] RawDate { get; private set; }
        public DateTime Date { get; private set; }
        public FileFlags Flags { get; private set; }
        public byte FileUnitSize { get; private set; }
        public byte Interleave { get; private set; }
        public ushort VolumeSequenceNumber { get; private set; }
        public byte NameLen { get; private set; }
        public string Name { get; private set; }
    }
}