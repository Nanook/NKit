namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Wraps a FsYamlNode directory as an IFsFolder for VFS navigation.
    /// </summary>
    internal class FsYamlFolderItem : IFsFolder
    {
        private readonly NKitDataStore.FsYamlNode _node;

        public FsYamlFolderItem(NKitDataStore.FsYamlNode node)
        {
            _node = node;
        }

        public string Name => _node.Name;
        public IFsFolder Parent { get; set; }
        public string Path => "";

        public List<IFsFile> Files => _node.Children?
            .Where(c => c.IsFile)
            .Select(c => (IFsFile)new FsYamlFileItem(c))
            .ToList() ?? new List<IFsFile>();

        public List<IFsFolder> Folders => _node.Children?
            .Where(c => c.IsDirectory)
            .Select(c => (IFsFolder)new FsYamlFolderItem(c))
            .ToList() ?? new List<IFsFolder>();

        public override string ToString() => Name ?? "";
    }
}