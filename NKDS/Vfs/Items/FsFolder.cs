namespace Nanook.NKit.Vfs
{
    internal class FsFolder : IFsFolder
    {
        public FsFolder()
        {
            Files = new List<IFsFile>();
            Folders = new List<IFsFolder>();
        }
        public List<IFsFile> Files { get; set; }

        public List<IFsFolder> Folders { get; set; }

        public string Name { get; set; }

        public IFsFolder Parent { get; set; }

        public string Path
        {
            get
            {
                if (this.Parent == null || this.Parent.Parent == null)
                    return "/" + this.Name;
                return this.Parent?.Path + "/" + this.Name;
            }
        }
        public override string ToString() => Name ?? "";
    }
}