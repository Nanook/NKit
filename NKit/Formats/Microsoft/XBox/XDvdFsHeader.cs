namespace Nanook.NKit.Microsoft.XBox
{

    internal class XDvdFsHeader : IImageHeader
    {
        //private Dictionary<FsType, ImageHeaderPvd> _pvd;
        private FstContext _ctx;

        //http://www.dubeyko.com/development/FileSystems/ISO9960/ISO9960.html
        public XDvdFsHeader(byte[] header, AreaInfo areaInfo, long fsSize)
        {
            this.Data = header;
            this.FsSize = fsSize;
            _ctx = new FstContext(this, areaInfo, null);
        }

        public void SetVolumeInfo(long imageOffset, long areaOffset, int volumeHeaderOffset, int size, int blockSize) => this.Volume = new XDvdFsVolume(this.Data.Read(volumeHeaderOffset, blockSize), blockSize);

        public XDvdFsVolume Volume { get; private set; }

        public FstContext FstContext => _ctx;
        public byte[] Data { get; }

        public uint PvdHeaderCrc { get; internal set; }
        public ulong PvdHeaderXxHash { get; internal set; }

        public long FsSize { get; private set; }

    }
}