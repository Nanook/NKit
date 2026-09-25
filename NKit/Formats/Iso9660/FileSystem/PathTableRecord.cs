using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class PathTableRecord
    {
        public PathTableRecord(long imageOffset, bool bigEndian)
        {
            this.FsOffset = imageOffset;
            this.IsBigEndian = bigEndian;
        }

        public static PathTableRecord Parse(long fsOffset, int blockFsSize, byte[] data, int offset, bool bigEndian, long baseOffset)
        {
            PathTableRecord ptr = new PathTableRecord(fsOffset, bigEndian);

            ptr.DirectoryNameSize = data.Read8(offset + 0x00);
            ptr.ExtendedAttrSize = data.Read8(offset + 0x01);

            if (ptr.DirectoryNameSize == 0 && ptr.ExtendedAttrSize == 0) //DC Escapee Unl
                return ptr;

            ptr.ExtentLocation = bigEndian ? (int)data.ReadUInt32B(offset + 0x02) : (int)data.ReadUInt32L(offset + 0x02);
            ptr.ParentDirectoryNo = bigEndian ? (int)data.ReadUInt16B(offset + 0x06) : (int)data.ReadUInt16L(offset + 0x06);
            int sz = ptr.DirectoryNameSize + (ptr.DirectoryNameSize % 2 == 0 ? 0 : 1);
            ptr.DirectoryName = data.Read(offset + 0x08, ptr.DirectoryNameSize);

            ptr.FsOffset = fsOffset;
            ptr.ExtentOffset = (ptr.ExtentLocation * (long)blockFsSize) - baseOffset;

            ptr.Size = 0x08 + sz;

            return ptr;
        }

        public List<FileSystemDirectoryEntry> Directories { get; private set; }

        public byte DirectoryNameSize { get; private set; }
        public byte ExtendedAttrSize { get; private set; }
        public int ExtentLocation { get; private set; }
        public int ParentDirectoryNo { get; private set; }
        public byte[] DirectoryName { get; private set; }

        public long FsOffset { get; private set; }
        public int Size { get; private set; }
        public bool IsBigEndian { get; private set; }

        public long ExtentOffset { get; private set; }
    }

}