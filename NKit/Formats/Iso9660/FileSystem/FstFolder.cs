using System.Collections.Generic;
using System.Text;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FstFolder : IFsFolder
    {
        private string _name;

        public FstFolder(FsType type) : this("", type, null)
        {
        }

        public FstFolder(string name, FsType type, FstFolder parent)
        {
            this.Folders = new List<IFsFolder>();
            this.Files = new List<IFsFile>();
            this.FsType = type;
            this.Parent = parent;
            if (parent != null) //!root
                parent.Folders.Add(this);
            _name = name;
        }

        public OrderedList<FstLink> Links { get; }

        public string Name
        {
            get
            {
                if (this.RockRidge != null)
                    return this.RockRidge.AlternativeName ?? "";
                return _name ?? "";
            }
        }
        public SuspRockRidge RockRidge { get; internal set; }
        public IFsFolder Parent { get; set; }
        public List<IFsFolder> Folders { get; }
        public List<IFsFile> Files { get; }
        public FsType FsType { get; }
        public bool Romeo { get; internal set; }

        public string Path
        {
            get
            {
                StringBuilder sb = new StringBuilder(this.Name);
                IFsFolder f = Parent;
                while (f != null)
                {
                    if (sb.Length != 0)
                        sb.Insert(0, "/");

                    sb.Insert(0, f.Name);
                    f = (FstFolder)f.Parent;
                }
                return sb.ToString();
            }
        }

        public override string ToString() => string.Format($"Name:{Name}, Path:{Path}, FsType:{FsType}");
    }

}