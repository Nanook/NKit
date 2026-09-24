using System;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FileSystemDirectoryEntry
    {
        public FileSystemDirectoryEntry()
        {
        }

        public static uint GetSize(byte[] data, int offset) => Math.Max(data.ReadUInt32L(offset + 0x0a), data.ReadUInt32B(offset + 0x0e));

        public static FileSystemDirectoryEntry Parse(long fsOffset, int blockFsSize, byte[] data, int offset, long baseOffset)
        {
            int sz = data.Read8(offset + 0x00);
            if (sz == 0)
                return null;

            FileSystemDirectoryEntry dr = new FileSystemDirectoryEntry();
            dr.FsOffset = fsOffset;
            dr.FsSize = sz;
            dr.ExtendedAttrSize = data.Read8(offset + 0x01);
            dr.ExtentLocation = Math.Max(data.ReadUInt32L(offset + 0x02), data.ReadUInt32B(offset + 0x06));
            dr.DataSize = Math.Max(data.ReadUInt32L(offset + 0x0a), data.ReadUInt32B(offset + 0x0e));
            dr.Date = data.Read(offset + 0x12, 7);
            dr.Flags = (FileFlags)data.Read8(offset + 0x19);
            dr.FileUnitSize = data.Read8(offset + 0x1a);
            dr.Interleave = data.Read8(offset + 0x1b);
            dr.VolumeSequenceNumber = Math.Max(data.ReadUInt16L(offset + 0x1c), data.ReadUInt16B(offset + 0x1e));
            dr.NameSize = data.Read8(offset + 0x20);

            //sz = dr.NameSize + (dr.NameSize % 2 == 0 ? 0 : 1);
            dr.Name = data.Read(offset + 0x21, dr.NameSize);

            dr.ExtentOffset = (dr.ExtentLocation * (long)blockFsSize) - baseOffset;

            int exOffset = 0x21 + dr.NameSize;
            if (data.Length > offset + exOffset + (dr.FsSize - exOffset))
            {
                dr.RockRidge = SuspRockRidge.Parse(data, offset + exOffset, dr.FsSize - exOffset);
                dr.Cdxa = SuspRockRidge.IsCdxa(data, offset + exOffset, dr.FsSize - exOffset);
            }
            return dr;
        }

        public SuspRockRidge RockRidge { get; internal set; }
        public bool Cdxa { get; private set; }
        public int FsSize { get; internal set; }
        public byte ExtendedAttrSize { get; internal set; }
        public uint ExtentLocation { get; internal set; }
        public uint DataSize { get; internal set; }
        public byte[] Date { get; internal set; }
        public FileFlags Flags { get; internal set; }
        public byte FileUnitSize { get; internal set; }
        public byte Interleave { get; internal set; }
        public ushort VolumeSequenceNumber { get; internal set; }
        public byte NameSize { get; internal set; }
        public byte[] Name { get; internal set; }

        public long FsOffset { get; internal set; }
        public long ExtentOffset { get; internal set; }
        public int ExtentSize { get; internal set; }
        public int ExtentBlockSize { get; internal set; }
    }

}