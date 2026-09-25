namespace Nanook.NKit.Vfs
{
    internal class FsImage : IFsFile
    {
        public bool IsMissing => throw new NotImplementedException();
        public bool IsLastFile => throw new NotImplementedException();

        public string FullName => throw new NotImplementedException();

        public long FsSize { get; set; }

        public ulong XxHash { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public uint Crc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public uint GapCrc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public bool IsSystemFile => throw new NotImplementedException();

        public long FsOffset => throw new NotImplementedException();

        public long PostGapSize => throw new NotImplementedException();

        public long PostGapFsOffset => throw new NotImplementedException();

        public int SplitIndex => throw new NotImplementedException();

        public IFsFileParts SplitParts => throw new NotImplementedException();

        public string Name { get; set; }

        public IFsFolder Parent { get; set; }

        public string Path { get; set; }

        public IFsFile Clone() => throw new NotImplementedException();
    }
}