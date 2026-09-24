using System;
using System.Linq;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class FstBlock
    {
        public FstBlock(ImageHeader header, long imageOffset, byte[] data, int fstFileSize, int blockSize, long partImageOffset, bool encrypted, long imageSize, IndexFile indexFile)
        {
            //tmd info provides the correct information when system mode is detected

            int fstRecLen = 0x20;
            long prtHeaderSize = imageOffset - partImageOffset;
            this.ImageOffset = partImageOffset;
            this.Multiplier = (int)data.ReadUInt32B(0x4);
            this.ContentHeaders = new ContentHeader[(int)data.ReadUInt32B(0x8)];
            this.HashDisabled = data.Read8(0xc);
            this.FstFileSize = fstFileSize;
            this.TmdFstMismatch = false;

            int offset;
            for (int i = 0; i < ContentHeaders.Length; i++)
            {
                offset = (i + 1) * fstRecLen;
                ContentHeader s = new ContentHeader
                {
                    Index = i,
                    Offset = blockSize * data.ReadUInt32B(offset),
                    FsSize = blockSize * data.ReadUInt32B(offset + 0x4),
                    TitleId = (long)data.ReadUInt64B(offset + 0x8),
                    GroupId = (int)data.ReadUInt32B(offset + 0x10),
                    Type = (AppType)data.Read8(offset + 0x14),
                };
                ContentHeaders[i] = s;
            }
            long adjust = ContentHeaders.FirstOrDefault(a => a.Offset != 0 && a.FsSize != 0)?.Offset ?? 0;

            long nextImageOffset = 0;
            int maxCount = this.ContentHeaders.Length;
            if (indexFile != null && indexFile.Items.Length != this.ContentHeaders.Length)
            {
                this.TmdFstMismatch = true;
                maxCount = Math.Min(indexFile.Items.Length, this.ContentHeaders.Length); //Todo: warn on this. Only seen with Nintendo TVii (Japan) (DLC)
            }

            for (int i = 0; i < maxCount; i++)
            {
                ContentHeader s = this.ContentHeaders[i];

                if (indexFile != null) //app mode
                    s.ImageOffset = nextImageOffset;
                else if (s.Offset <= adjust && s.FsSize == 0) //fix what seem to be bad offsets - set to FST block
                    s.ImageOffset = imageOffset;
                else
                    s.ImageOffset = imageOffset + fstFileSize - adjust + s.Offset;
                s.HasEncryption = encrypted;
                s.HasHashes = this.HashDisabled == 0 && s.Type == AppType.Files; //are there hashes or not? This is the real indicator

                if (s.HasHashes)
                {
                    s.BlockSize = header.BlockSize << 1; //double size
                    s.BlockFsOffset = header.HashesSize;
                    s.BlockFsSize = s.BlockSize - s.BlockFsOffset;
                    s.Size = Buffer.FsOffsetToOffset(s.FsSize, s.BlockSize, s.BlockFsOffset, s.BlockFsSize, false);
                    if (s.Size % s.BlockSize == s.BlockFsOffset)
                        s.Size -= s.BlockFsOffset;

                    if (indexFile != null && indexFile.Items[s.Index].Size < s.Size) //fixed oversized entry in indexed read
                        s.Size = indexFile.Items[s.Index].Size;

                    s.SizePaddedToBlock = s.Size + (s.Size % s.BlockSize == 0 ? 0 : s.BlockSize - (s.Size % s.BlockSize));
                    s.FsSizePaddedToBlock = Buffer.OffsetToFsOffset(s.SizePaddedToBlock, s.BlockSize, s.BlockFsOffset, s.BlockFsSize);
                    long h2Size = WiiUConsts.H2Full * s.BlockSize;
                    s.H2Hashes = new byte[(s.SizePaddedToBlock / h2Size) + (s.SizePaddedToBlock % h2Size == 0 ? 0 : 1)][];
                }
                else
                {
                    s.BlockSize = header.BlockSize;
                    s.BlockFsOffset = 0;
                    s.BlockFsSize = s.BlockSize - s.BlockFsOffset;
                    if (indexFile != null)
                        s.Size = indexFile.Items[s.Index].Size;
                    else
                        s.Size = s.FsSize;
                    s.SizePaddedToBlock = s.Size; //support non block aligned sizes when no hashes (used by system titles)
                    s.FsSizePaddedToBlock = s.Size;
                }

                if (indexFile != null)
                    nextImageOffset += indexFile.Items[s.Index].Size; //use the file sizes so that image reading is reliable
                else
                    nextImageOffset += s.Type == AppType.Fst ? fstFileSize : s.Size;
            }

            ContentHeader last = this.ContentHeaders.LastOrDefault();
            if (last != null)
            {
                long endOffset = this.ImageOffset + header.GetPartitionSize(this.ImageOffset, imageSize);
                for (int i = 0; i < this.ContentHeaders.Length; i++)
                {
                    ContentHeader app = this.ContentHeaders[i];
                    long next = app == last ? endOffset : this.ContentHeaders[i + 1].ImageOffset;
                    if (indexFile == null && app.ImageOffset > imageOffset && next > app.ImageOffset + app.SizePaddedToBlock) //items after fst entry
                        app.RepeatedApp = true; //this content has a following gap which is always this content repeated
                }
            }
            this.FstFileOffset = (ContentHeaders.Length + 1) * fstRecLen; //+1 for header
        }

        public bool TmdFstMismatch { get; internal set; }
        public int FstFileOffset { get; internal set; }
        public int FstFileSize { get; internal set; }
        public int Multiplier { get; internal set; }
        public byte HashDisabled { get; internal set; }
        public long ImageOffset { get; internal set; }

        public ContentHeader[] ContentHeaders { get; private set; }

        internal ContentHeader GetContentHeader(long imageOffset) => this.ContentHeaders.FirstOrDefault(a => imageOffset >= a.ImageOffset && imageOffset < a.ImageOffset + a.Size);
        internal ContentHeader GetNextContentHeader(long imageOffset) => this.ContentHeaders.Where(a => imageOffset < a.ImageOffset && a.Size != 0).OrderBy(a => a.ImageOffset).FirstOrDefault();

    }

}