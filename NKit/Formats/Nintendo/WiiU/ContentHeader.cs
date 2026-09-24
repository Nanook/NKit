namespace Nanook.NKit.Nintendo.WiiU
{
    internal enum AppType { Fst = 0, Code = 1, Files = 2 }

    internal class ContentHeader
    {
        public ContentHeader()
        {
        }

        public long ImageOffset { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public long FsSize { get; set; }
        public int BlockSize { get; set; }
        public int BlockFsOffset { get; set; }
        public int BlockFsSize { get; set; }
        public bool RepeatedApp { get; set; }
        public long TitleId { get; set; }
        public int GroupId { get; set; }
        public AppType Type { get; set; }
        public bool HasHashes { get; set; }
        public bool HasEncryption { get; set; }
        public int Index { get; set; }

        public byte[][] H2Hashes { get; set; } //store here in prebuffer so they can be fully validated
        public int H3Offset { get; internal set; }
        public byte[] H3Hashes { get; internal set; }
        public bool IsValid { get; internal set; }
        public int H3HashCount { get; internal set; }
        public Content Content { get; internal set; }
        public long SizePaddedToBlock { get; internal set; }
        public long FsSizePaddedToBlock { get; internal set; }
        public ContentHeader Clone() => (ContentHeader)this.MemberwiseClone();


        public override string ToString() => string.Format("[{0}] ImageOffset:{1}, Offset:{2}, Size:{3}, FsSize:{4}, Type:{5}, GroupId:{6}, Content:{7}", Index.ToString("X8"), ImageOffset.ToString("X8"), Offset.ToString("X8"), Size.ToString("X8"), FsSize.ToString("X8"), Type.ToString(), GroupId.ToString("X8"), Content?.ToString() ?? "");
    }
}