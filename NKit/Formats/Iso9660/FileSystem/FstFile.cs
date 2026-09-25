using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FstFile : IFsFile, IFsFileXxHashCalc
    {
        public FstFile(FstFolder parent, string name, FsType fsType, long fsOffset, long size, FsItemType type)
        {
            this.Links = new OrderedList<FstLink>(a => (int)a.FsType, true);
            if (parent != null) //null when creating the directory DstFile
                this.Links.Add(new FstLink(parent, name, fsType, size));

            this.FsSize = size;
            this.Type = type;
            this.FsOffset = fsOffset;
            if (parent != null)
                parent.Files.Add(this);
        }

        internal FstFile TempStash { get; set; } //UDF uses this when building the filesystem, fileentry+file and fileentry+extendedfileentry
        public FsBlockEndian Endian { get; internal set; }
        public IFsFolder Parent => this.Links?.FirstOrDefault()?.Parent;
        public OrderedList<FstLink> Links { get; internal set; }
        public string Name
        {
            get
            {
                FstLink l = this.Links.FirstOrDefault();
                if (l == null)
                    return this.RockRidge?.AlternativeName ?? "";
                else
                {
                    if (this.RockRidge?.AlternativeName != null && l.FsType <= FsType.RockRidge)
                        return this.RockRidge.AlternativeName;
                    else
                        return this.Links.FirstOrDefault()?.EncodedChildName ?? "";
                }
            }
        }
        public SuspRockRidge RockRidge { get; internal set; }

        public int SplitIndex { get; set; }
        public IFsFileParts SplitParts { get; set; }

        public bool Cdxa { get; internal set; }
        public bool Romeo { get; internal set; }
        public long FsOffset { get; internal set; }
        public long FsSize { get; internal set; }
        public FsItemType Type { get; internal set; }

        public ulong XxHash { get; set; }
        public uint Crc { get; set; }
        public uint GapCrc { get; set; }

        public bool IsLastFile { get; internal set; }

        public string FullName => this.Path + "/" + this.Name;

        public string Path => this.Parent?.Path ?? "";

        public bool IsSystemFile { get; set; }

        public long PostGapSize { get; set; }

        public long PostGapFsOffset { get; set; }
        public bool Mode2Form2Sectors { get; internal set; }
        public XXHash64 Object { get; set; }
        public Stream Stream { get; set; }

        public bool IsMissing { get; set; }

        public IFsFile Clone() => null;

        public static int CompareFiles(IFsFile a, IFsFile b) => (int)Math.Min(Math.Max(-1L, a.FsOffset - b.FsOffset), 1L);

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (FstLink f in this.Links)
                sb.Append((sb.Length != 0 ? "|" : "") + f.FsType.ToString());
            return string.Format($"Offset:{FsOffset:X}, Size:{FsSize:X}, FileType:{Type}, Name:\"{Path}/{Name}\", FsTypes:<{(Links.Count == 0 ? "<None>" : sb)}>, Endian:{Endian}, Parent:{Parent?.Name ?? ""}, Type:{Type}");
        }

    }

}