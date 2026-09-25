using System;

namespace Nanook.NKit.Microsoft.XBox
{
    internal class XDvdFsVolume
    {
        public XDvdFsVolume(byte[] data, int blockSize) : this(data, 0, blockSize)
        {
        }

        public XDvdFsVolume(byte[] data, int offset, int blockSize)
        {
            this.Id = data.ReadString(offset + 0x0, Consts.VolumeId.Length);
            this.IdTrailer = data.ReadString(offset + blockSize - Consts.VolumeId.Length, Consts.VolumeId.Length);
            this.IsValid = this.Id == Consts.VolumeId && this.IdTrailer == Consts.VolumeId;

            if (this.IsValid)
            {
                offset += Consts.VolumeId.Length;
                this.RootSector = (int)data.ReadUInt32L(offset);
                this.RootFsOffset = this.RootSector * (long)blockSize;
                this.RootFsSize = data.ReadUInt32L(offset + 0x4);
                this.FsOffet = Consts.VolumeOffset;
                this.FsSize = blockSize;
                this.DateTime = DateTime.FromFileTimeUtc((long)data.ReadUInt64L(offset + 0x8));
                this.Version = data.ReadUInt16L(offset + 0x10);
            }
        }

        public long FsOffet { get; private set; }
        public long FsSize { get; private set; }
        public DateTime DateTime { get; private set; }
        public string Id { get; private set; }
        public string IdTrailer { get; private set; }
        public int RootSector { get; private set; }
        public long RootFsOffset { get; private set; }
        public long RootFsSize { get; private set; }
        public int Version { get; private set; }
        public bool IsValid { get; private set; }
    }
}