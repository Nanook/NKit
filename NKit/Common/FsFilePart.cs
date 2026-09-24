using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{

    public class FsFileParts : IFsFileParts, IFsFileXxHashCalc
    {
        internal FsFileParts()
        {
            Parts = new List<IFsFilePart>();
        }
        public List<IFsFilePart> Parts { get; }
        public long Size { get; internal set; }
        public XXHash64 Object { get; set; }
        public Stream Stream { get; set; }
        public ulong XxHash { get; set; }
        public uint Crc { get; set; }
    }

    public class FsFilePart : IFsFilePart
    {
        public int Index { get; internal set; }

        public long OffsetInFile { get; internal set; }

        public IFsFile FsFile { get; internal set; }

        public override string ToString() => $"Index:{Index}, OffInFile:{OffsetInFile:x9}, Size:{FsFile.FsSize:x9}, Name:{FsFile.Name}, SplitIndex:{FsFile.SplitIndex}, SplitSize:{FsFile.SplitParts.Parts.Count}";
    }
}