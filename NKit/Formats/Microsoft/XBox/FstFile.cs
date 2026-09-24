using System;
using System.IO;

namespace Nanook.NKit.Microsoft.XBox
{
    internal class FstFile : IFsFile, IFsFileXxHashCalc
    {
        public FstFile(FstFolder parent, string name, long fsOffset, long size)
        {
            this.FsSize = size;
            this.FsOffset = fsOffset;
            this.Parent = parent;
            if (parent != null)
                parent.Files.Add(this);
            this.Name = name;
        }

        public IFsFolder Parent { get; set; }
        public string Name { get; internal set; }
        public ushort LeftSector { get; internal set; }
        public ushort RightSector { get; internal set; }
        public byte Attributes { get; internal set; }

        public long FsOffset { get; internal set; }
        public long FsSize { get; internal set; }
        public FsItemType Type { get; internal set; }

        public ulong XxHash { get; set; }
        public uint Crc { get; set; }
        public uint GapCrc { get; set; }

        public bool IsLastFile { get; internal set; }

        public string FullName => this.Path.TrimEnd('/') + "/" + this.Name;

        public string Path => this.Parent?.Path ?? "";

        public bool IsSystemFile { get; set; }

        public long PostGapSize { get; set; }

        public long PostGapFsOffset { get; set; }
        public XXHash64 Object { get; set; }
        public Stream Stream { get; set; }

        public bool IsMissing { get; set; }

        public int SplitIndex => 0;

        public IFsFileParts SplitParts => null;

        public IFsFile Clone() => null;

        public static int CompareFiles(IFsFile a, IFsFile b) => (int)Math.Min(Math.Max(-1L, a.FsOffset - b.FsOffset), 1L);

        public override string ToString() => string.Format($"Offset:{FsOffset:X}, Size:{FsSize:X}, Name:\"{Name}\", Parent:{Parent?.Path ?? ""}");

    }

}