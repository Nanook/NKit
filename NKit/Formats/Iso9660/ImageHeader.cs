using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{

    internal class ImageHeader : IImageHeader
    {
        //private Dictionary<FsType, ImageHeaderPvd> _pvd;
        private FstContext _ctx;

        //http://www.dubeyko.com/development/FileSystems/ISO9960/ISO9960.html
        public ImageHeader(byte[] header, AreaInfo areaInfo)
        {
            this.Data = header;
            _ctx = new FstContext(this, areaInfo, null);
        }

        public FstContext FstContext => _ctx;
        public byte[] Data { get; }
        internal PlayStation3 Ps3 { get; set; }

        public ImageHeaderElTorito ElTorito { get; private set; }

        public long FsSize { get; private set; }

        public Dictionary<FsType, ImageHeaderPvd> Pvds => _ctx.Pvd;

        public long PvdEndFsOffset { get; private set; }
        public uint PvdHeaderCrc { get; internal set; }
        public ulong PvdHeaderXxHash { get; internal set; }
        public long PvdSectorCount => this.Pvds?.Select(a => a.Value.Blocks).OrderByDescending(a => a).FirstOrDefault() ?? 0;

        public FsType MainFsType
        {
            get
            {
                if (this.Pvds != null && this.Pvds.Count != 0)
                {
                    if (this.Pvds.ContainsKey(FsType.Udf))
                        return FsType.Udf;
                    else if (this.Pvds.ContainsKey(FsType.Joliet))
                        return FsType.Joliet;
                    else if (this.Pvds.ContainsKey(FsType.Iso9660))
                        return FsType.Iso9660;
                }
                return FsType.Other;
            }
        }

        public ImageHeaderPvd MainPvd => this.Pvds[this.MainFsType];

        internal bool SetPvd(long imageOffset, long areaOffset, byte[] data, int blockSize, int blockFsOffset, int blockFsSize, long baseOffset)
        {
            long areaFsOffset = Buffer.OffsetToFsOffset(areaOffset, blockSize, blockFsOffset, blockFsSize);
            if (data.Read8(0x0000) == 0xff && data.ReadString(1, 5) == "CD001")
            {
                _ctx.AddFile(_ctx.Pvd[FsType.System].RootFolder, $"__pvdEnd_{areaFsOffset:X}", FsType.System, areaFsOffset, blockFsSize, FsItemType.Pvd, true);
                this.PvdEndFsOffset = areaFsOffset;
            }
            else
            {
                ImageHeaderPvd pvd = ImageHeaderPvd.Parse(_ctx, imageOffset, areaOffset, data, blockSize, blockFsOffset, blockFsSize, baseOffset);
                if (pvd != null)
                {
                    if (pvd.Type == FsType.Udf)
                    {
                    }
                    else if (pvd.Type == FsType.ElTorito)
                        this.ElTorito = new ImageHeaderElTorito() { ImageOffset = imageOffset, PvdIndex = _ctx.Pvd.Count, SectionEntryOffset = data.ReadUInt32L(0x47) };
                    else //Valid PVD (ElTorito is just a pointer to the boot info)
                        FsSize = Math.Max(pvd.Blocks * (long)pvd.BlocksSize, FsSize);

                    if (!_ctx.Pvd.ContainsKey(pvd.Type))
                        _ctx.Pvd.Add(pvd.Type, pvd);
                }
                return pvd != null;
            }
            return true;
        }
    }
}