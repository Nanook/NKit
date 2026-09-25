using System.Collections.Generic;

namespace Nanook.NKit.Microsoft.XBox
{
    internal class FstFolder : IFsFolder
    {
        public FstFolder(string name, FstFolder parent)
        {
            this.Folders = new List<IFsFolder>();
            this.Files = new List<IFsFile>();
            this.Parent = parent;
            if (parent != null) //!root
                parent.Folders.Add(this);
            this.Name = name;
        }

        public IFsFolder Parent { get; private set; }
        public List<IFsFolder> Folders { get; private set; }
        public List<IFsFile> Files { get; internal set; }
        public string Name { get; set; }
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