namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Wraps a stored file (from the files table) as an IFsFile for VFS navigation.
    /// </summary>
    internal class FsStoredFile : IFsFile
    {
        public string Name { get; set; }
        public long FsSize { get; set; }
        public string StoredFileName { get; set; }
        public IFsFolder Parent { get; set; }
        public string Path => "";
        public string FullName => Name;
        public bool IsMissing => false;
        public bool IsLastFile => false;
        public ulong XxHash { get => 0; set { } }
        public uint Crc { get => 0; set { } }
        public uint GapCrc { get => 0; set { } }
        public bool IsSystemFile => false;
        public long FsOffset => 0;
        public long PostGapSize => 0;
        public long PostGapFsOffset => 0;
        public int SplitIndex => 0;
        public IFsFileParts SplitParts => null;

        public IFsFile Clone() => this;
        public override string ToString() => Name ?? "";
    }
}