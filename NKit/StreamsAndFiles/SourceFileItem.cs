namespace Nanook.NKit
{
    public class SourceFileItem
    {
        internal SourceFileItem(string path, string fileName, string extension, string postFix, long offset, long size, long crc, bool isArchived, bool isTemp)
        {
            this.Path = path;
            this.FileName = fileName;
            this.Extension = isTemp ? extension.TrimEnd(ResultOutFiles.TempChar[0]) : extension;
            this.Postfix = postFix ?? ""; //all chars after the name '_1' for example
            this.Offset = offset;
            this.Size = size;
            this.Crc = crc;
            this.IsArchived = isArchived;
            this.IsTemp = isTemp;
        }

        public string NameOnly => FileName.Substring(0, this.FileName.Length - this.Postfix.Length);
        public string Path { get; }
        public string FileName { get; }
        public string Extension { get; }
        internal string Postfix { get; }
        public long Offset { get; }
        public long Size { get; }
        public long Crc { get; }
        public bool IsArchived { get; }
        public bool IsTemp { get; }

        public override string ToString() => string.Format("Name:{0}, PostFix(Ext):{1}({2}{3}), Offset:{4}, Size:{5}, Crc:{6}", this.NameOnly, this.Postfix, this.Extension, this.IsTemp ? ResultOutFiles.TempChar : "", this.Offset.ToString("X8"), this.Size.ToString("X8"), this.Crc.ToString("X8"));

    }
}