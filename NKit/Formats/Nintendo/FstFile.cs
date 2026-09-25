using System.IO;
using System.Text;

namespace Nanook.NKit.Nintendo
{
    internal class FstFile : IFsFile, IFsFileXxHashCalc
    {
        internal FstFile(FstFolder parent, string name, long fsOffset, long size, int fstPtrOffset, bool isFst, bool isLastFile)
            : this(parent, name, fsOffset, size, fstPtrOffset, false, 0, 0, true, isFst, isLastFile) { }

        internal FstFile(FstFolder parent, string name, long fsOffset, long size, int fstPtrOffset, bool isWiiU, int wiiUPermission, int wiiUAppIndex, bool wiiUNotInNus, bool isFst, bool isLastFile)
        {
            Parent = parent;
            Name = name;
            FsOffset = fsOffset;
            FsSize = size;
            FstPtrOffset = fstPtrOffset;
            IsWiiU = isWiiU;
            WiiUPermission = wiiUPermission;
            WiiUAppIndex = wiiUAppIndex;
            WiiUNotInNus = wiiUNotInNus;
            Analysis = new FileAnalysis(this);
            IsFstBin = isFst;
            IsLastFile = isLastFile;
        }
        public IFsFile Clone()
        {
            FstFile f = new FstFile((FstFolder)Parent, Name, FsOffset, FsSize, FstPtrOffset, IsWiiU, WiiUPermission, WiiUAppIndex, WiiUNotInNus, IsFstBin, IsLastFile);
            f.Analysis.Initialise(Analysis.SharedOffset, PostGapFsOffset + PostGapSize);
            return f;
        }
        internal FileAnalysis Analysis { get; private set; }
        public IFsFolder Parent { get; private set; }
        public bool IsSystemFile { get; internal set; }
        public string Name { get; internal set; }
        public long FsOffset { get; internal set; }
        public long FsSize { get; internal set; }
        public bool IsFstBin { get; internal set; }
        public bool IsLastFile { get; internal set; }
        public bool IsMissing { get; internal set; } //set when identified
        public int SplitIndex { get; set; }
        public IFsFileParts SplitParts { get; set; }
        internal int FstPtrOffset { get; set; }
        public int WiiUPermission { get; internal set; }
        public bool IsWiiU { get; internal set; }
        public int WiiUAppIndex { get; internal set; }
        public bool WiiUNotInNus { get; internal set; }
        public long PostGapSize => Analysis?.Size ?? 0;
        public long PostGapFsOffset => Analysis?.FsOffset ?? 0;
        public string FullName => Path + "/" + Name;
        internal XXHash64 XxHashCalc { get; set; }
        internal Stream XxHashCalcStream { get; set; }
        public ulong XxHash { get; set; }
        public uint Crc { get; set; }
        public uint GapCrc { get; set; }
        public string Path
        {
            get
            {
                StringBuilder sb = new StringBuilder();
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

        public XXHash64 Object { get; set; }
        public Stream Stream { get; set; }

        public override string ToString() => string.Format("{0} : {1} : {2} : {3} : {4}", FstPtrOffset.ToString("X8"), FsOffset.ToString("X8"), FsSize.ToString("X8"), WiiUAppIndex.ToString(), Name);
    }
}