namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Represents an area within an APP-format image exposed as a readable file (e.g. "00000004.app").
    /// </summary>
    internal class FsImageAreaFile : IFsFile
    {
        public string Name { get; set; }
        public long FsSize { get; set; }
        public long FsOffset { get; set; }
        public IFsFolder Parent { get; set; }
        /// <summary>
        /// When the parent folder is a merged view of multiple images, this identifies
        /// the specific image record this area belongs to so that TryCreateAreaStream
        /// can open the correct reader. Null for non-merged items.
        /// </summary>
        public NKitDataStore.ImageRecord SourceImageRecord { get; set; }
        public string Path => "";
        public string FullName => Name;
        public bool IsMissing => false;
        public bool IsLastFile => false;
        public ulong XxHash { get => 0; set { } }
        public uint Crc { get => 0; set { } }
        public uint GapCrc { get => 0; set { } }
        public bool IsSystemFile => false;
        public long PostGapSize => 0;
        public long PostGapFsOffset => 0;
        public int SplitIndex => 0;
        public IFsFileParts SplitParts => null;

        public IFsFile Clone() => this;
        public override string ToString() => Name ?? "";
    }
}