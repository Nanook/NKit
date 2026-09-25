using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Text;

namespace Nanook.NKit.Nintendo
{
    internal class FileAnalysis
    {
        public FstFile File { get; }
        public long FsOffset { get; internal set; } //Previous file FsOffset + Size
        public long Size { get; internal set; }
        public int ExpectedNulls { get; internal set; } //expected nulls count
        public int Aligning { get; internal set; } //0 to 3 bytes to align the files to 4 bytes
        public long JunkFileSize { get; internal set; } //if identified as junk - this is the actualy file size
        public int JunkNulls { get; internal set; } //junk files don't exist, previous gap nulls can flow in to this file's area
        internal byte[] CachedData { get; set; } //used to store file data when processing files out of order
        public bool Invalid { get; set; }
        public bool SharedOffset { get; set; }

        internal FileAnalysis(FstFile file)
        {
            File = file;
            FsOffset = File.FsOffset + File.FsSize;
            Aligning = (int)(File.FsSize % 4L == 0L ? 0L : (4L - (File.FsSize % 4L)));
        }

        internal void Initialise(bool sharedOffset, long nextOffset) //next file or end of disc
        {
            this.SharedOffset = sharedOffset;
            this.Invalid = !this.SharedOffset && nextOffset < FsOffset;
            this.Size = Math.Max(0, nextOffset - FsOffset);

            if (!File.IsWiiU)
            {
                //Expected nulls rules are intricate
                ExpectedNulls = this.MaxNullsSize;

                if (ExpectedNulls > Size)
                    ExpectedNulls = (int)Size;
            }
        }

        //Gets the max nulls size before reduced for gap size. WHen junk files are present this is handy to have access to
        internal int MaxNullsSize
        {
            get
            {
                if (Size > NJunk.JunkBlockSize && !File.IsFstBin && !File.IsLastFile)
                    return Aligning; // this.File.FsSize == 0 ? 4 : Aligning;
                else
                    return WiiConsts.DataNullsCount + Aligning;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(File.FsOffset.ToString("X8"));
            sb.Append(", ");
            sb.Append(File.FsSize.ToString("X8"));
            sb.Append(", ");
            sb.Append(File.Name);
            sb.Append(", ");
            sb.Append(FsOffset.ToString("X8"));
            sb.Append(", ");
            sb.Append(Size.ToString("X8"));
            sb.Append(", ");
            sb.Append(ExpectedNulls.ToString("X2"));
            return sb.ToString();
        }
    }
}