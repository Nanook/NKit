namespace Nanook.NKit.Microsoft.XBox
{
    internal class XDvdFsFileSystemInfo : IFileSystemInfo
    {
        private readonly long _imageSize;

        public AreaInfo AreaInfo { get; }

        public XDvdFsFileSystemInfo(long imageOffset, long imageSize, XDvdFsHeader header, AreaInfo areaInfo, long sessionOffsetBase)
        {
            this.FileSystem = new Fst(header);
            this.ImageOffset = imageOffset;
            _imageSize = imageSize;
            Header = header;
            this.AreaInfo = areaInfo;
            this.SessionOffsetBase = sessionOffsetBase;
            this.FsSize = header.FsSize;
            this.Size = Buffer.FsOffsetToOffset(this.FsSize, areaInfo.BlockSize, areaInfo.BlockFsOffset, areaInfo.BlockFsSize, true);
            if (Size == 0)
                this.Size = imageSize;
            this.InvalidFileSystem = (header?.FstContext?.FileSystem?.Count ?? 0) == 0;
        }
        public XDvdFsHeader Header { get; }
        public PartitionType Type => PartitionType.Other;

        public long ImageOffset { get; private set; }
        public long FsSize { get; private set; }
        public long Size { get; private set; }
        public IFileSystem FileSystem { get; private set; }

        public bool InvalidFileSystem { get; set; }

        public long SessionOffsetBase { get; internal set; }

        public bool AllFoldersParsed => ((Fst)this.FileSystem).AllFolderRecordsParsed;
        public FidelityFileList FidelityFiles { get; internal set; }

        private IAreaFileSystemView _areaView;
        public IAreaFileSystemView AreaView => _areaView ??= AreaFileSystemView.TryBuild(this);

        public void ProcessBlock(IBuffer buffer) => ((Fst)this.FileSystem).SetFsData(buffer);

        public void Complete() => FidelityFiles = Header?.FstContext?.FidelityFiles;

        internal void DebugFiles()
        {
            //StringBuilder sb = new StringBuilder();
            //foreach (FstFile f in FileSystem?.Files)
            //    sb.AppendLine(f.ToString());

            //Trace.WriteLine(sb.ToString());
        }

    }
}