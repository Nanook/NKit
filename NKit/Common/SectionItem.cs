using System.Collections.Generic;

namespace Nanook.NKit
{

    internal class SectionItem : ISectionItem
    {
        public List<string> FileSystems { get; set; }
        public int FileIndex { get; set; }
        public IFsFile FsFile { get; internal set; }
        public long AreaBase { get; private set; }

        public long ImageOffset { get; internal set; }
        public long AreaOffset { get; internal set; }

        public ISectionData File { get; internal set; }
        public ISectionData Gap { get; internal set; }
        public List<ISectionData> GapInfo { get; }

        internal SectionItem(long areaBase)
        {
            GapInfo = new List<ISectionData>();
            FileSystems = new List<string>();
        }

        //files will be ordered and should only cover enough for the section
        internal SectionItem(long imageOffset, long areaOffset, long areaBase, IFsFile file) : this(areaBase)
        {
            ImageOffset = imageOffset;
            AreaOffset = areaOffset;
            AreaBase = areaBase;
            FsFile = file;
        }

    }
}