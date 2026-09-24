namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Wraps an NkFsEntry directory as an IFsFolder for VFS navigation.
    /// Children are enumerated lazily via NkFs.GetChildren() — no tree materialization.
    /// </summary>
    internal class NkFsFolderItem : IFsFolder
    {
        private readonly NKitDataStore.NkFs _nkfs;
        private readonly int _entryIndex;
        private readonly NKitDataStore.NkFsEntry _entry;

        public NkFsFolderItem(NKitDataStore.NkFs nkfs, int entryIndex, NKitDataStore.NkFsEntry entry)
        {
            _nkfs = nkfs;
            _entryIndex = entryIndex;
            _entry = entry;
        }

        /// <summary>The entry index in the NkFs entry table. Used by handlers for child navigation.</summary>
        public int EntryIndex => _entryIndex;

        public string Name
        {
            get
            {
                string name = _nkfs.GetEntryName(_entryIndex);
                return name;
            }
        }

        public bool IsSystem => _entry.SystemFlag;
        public IFsFolder Parent { get; set; }
        public string Path => "";

        public List<IFsFile> Files
        {
            get
            {
                List<IFsFile> files = new List<IFsFile>();
                foreach ((int idx, NKitDataStore.NkFsEntry child) in _nkfs.GetChildren(_entryIndex))
                {
                    if (child.IsFile && !child.IsImageFile)
                        files.Add(new NkFsFileItem(_nkfs, idx, child));
                }
                return files;
            }
        }

        public List<IFsFolder> Folders
        {
            get
            {
                List<IFsFolder> folders = new List<IFsFolder>();
                foreach ((int idx, NKitDataStore.NkFsEntry child) in _nkfs.GetChildren(_entryIndex))
                {
                    if (child.IsDirectory)
                        folders.Add(new NkFsFolderItem(_nkfs, idx, child));
                }
                return folders;
            }
        }

        public override string ToString() => Name ?? "";
    }
}