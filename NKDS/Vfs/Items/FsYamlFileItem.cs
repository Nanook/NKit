namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Wraps a FsYamlNode file as an IFsFile for VFS navigation.
    /// </summary>
    internal class FsYamlFileItem : IFsFile
    {
        private readonly NKitDataStore.FsYamlNode _node;

        public FsYamlFileItem(NKitDataStore.FsYamlNode node)
        {
            _node = node;
        }

        public string Name => _node.Name;
        public long FsSize => _node.Size;
        public IFsFolder Parent { get; set; }
        public string Path => "";
        public string FullName => Name;
        public bool IsMissing => false;
        public bool IsLastFile => false;
        public ulong XxHash { get => _node.XxHash64; set { } }
        public uint Crc { get => _node.Crc32; set { } }
        public uint GapCrc { get => 0; set { } }
        public bool IsSystemFile => _node.IsSystem;
        public long FsOffset => _node.Offset;
        public long PostGapSize => 0;
        public long PostGapFsOffset => 0;
        public int SplitIndex => 0;
        public IFsFileParts SplitParts => null;

        public IFsFile Clone() => this;
        public override string ToString() => Name ?? "";
    }
}