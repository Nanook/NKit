using System.Collections.Generic;

namespace Nanook.NKit.Nintendo
{
    internal class FstFolder : IFsFolder
    {
        //A gap that follows a file. This is updated as the image is processed
        internal FstFolder(FstFolder parent, string name) : this(parent, name, 0, 0, true) { }
        internal FstFolder(FstFolder parent, string name, int wiiUFlags, int wiiUSectionNo, bool wiiUPermission)
        {
            Folders = new List<IFsFolder>();
            Name = name;
            Files = new List<IFsFile>();
            Parent = parent;
            WiiUPermission = wiiUFlags;
            WiiUSectionNo = wiiUSectionNo;
            WiiUNotInNus = wiiUPermission;
        }
        public IFsFolder Parent { get; private set; }
        public List<IFsFolder> Folders { get; private set; }
        public List<IFsFile> Files { get; }
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
        public int WiiUPermission { get; internal set; }
        public int WiiUSectionNo { get; internal set; }
        public bool WiiUNotInNus { get; internal set; }

        public override string ToString() => Name ?? "";

    }
}