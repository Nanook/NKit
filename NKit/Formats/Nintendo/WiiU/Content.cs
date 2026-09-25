using System;

namespace Nanook.NKit.Nintendo.WiiU
{
    [Flags]
    internal enum AppContentType
    {
        Encrypted = 0x0001,
        Hashed = 0x0002,
        Cfm = 0x0400,
        SHA1 = 0x2000,
        Optional = 0x4000,
        Shared = 0x8000
    }

    internal class ContentGroup
    {
        public ContentGroup(byte[] hash, Content[] content, int groupOffset, int cntOffset, int cntSize) : base()
        {
            this.Hash = hash;
            this.GroupOffset = groupOffset;
            this.Contents = content;
            this.ContentOffset = cntOffset;
            this.ContentSize = cntSize;
        }

        public byte[] Hash { get; }
        public Content[] Contents { get; }
        public int GroupOffset { get; }
        public int ContentOffset { get; }
        public int ContentSize { get; }

        public override string ToString() => string.Format("Hash:{0}, Offset:{1}, Count:{2}", Hash == null ? "" : BitConverter.ToString(Hash), this.GroupOffset.ToString(), this.Contents.Length.ToString());
    }

    internal class Content
    {
        public Content(long contentId, int index, AppContentType type, long size, byte[] hash)
        {
            this.ContentId = contentId;
            this.Index = index;
            this.Type = type;
            this.Size = size;
            this.Hash = hash;
        }

        public long ContentId { get; }
        public int Index { get; }
        public AppContentType Type { get; }
        public long Size { get; }
        public byte[] Hash { get; }

        public override string ToString() => string.Format("ID:{0}, Index:{1}, Size:{2}, Type:{3}", ContentId.ToString("X"), Index.ToString(), Size.ToString("X9"), Type.ToString());
    }
}