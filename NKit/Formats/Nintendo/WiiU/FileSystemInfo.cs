using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class FileSystemInfo : IFileSystemInfo
    {
        private readonly long _imageSize;
        private readonly PartitionInfo _partitionTable;

        public int BlockSize { get; }

        private readonly ImageHeader _header;
        private byte[] _partHeader;
        public AreaInfo AreaInfo { get; }

        public FileSystemInfo(long imageOffset, long imageSize, ImageHeader header, SiData siData, int fstSize)
        {
            ImageOffset = imageOffset;
            _imageSize = imageSize;
            _header = header;
            _partHeader = null;
            this.VolumeHeaderSize = 0;
            _partitionTable = null;
            this.SiData = siData;

            this.BlockSize = WiiUConsts.DefaultSectorSize;
            this.Size = 0;
            this.FstSize = fstSize;
            this.FstOffset = 0;
            this.FstHashMode = 0;
            this.EncryptType = 1;
            this.MajorVersion = 0;
            this.MinorVersion = 0;
            this.ExpiringMajorVersion = 0;

            Type = PartitionType.Game;

        }

        public FileSystemInfo(long imageOffset, long imageSize, ImageHeader header, byte[] partHeader)
        {
            ImageOffset = imageOffset;
            _imageSize = imageSize;
            _header = header;
            _partHeader = partHeader;
            this.VolumeHeaderSize = partHeader.Length;
            _partitionTable = header.GetPartition(imageOffset);
            this.SiData = _partitionTable?.WiiUSiData;

            this.BlockSize = (int)_partHeader.ReadUInt32B(0x4);
            this.Size = (long)_partHeader.ReadUInt32B(0x8) * (long)this.BlockSize;
            this.FstSize = (int)_partHeader.ReadUInt32B(0x14);
            this.FstOffset = (int)_partHeader.ReadUInt32B(0x18) * this.BlockSize;
            this.FstHashMode = _partHeader.Read8(0x24);
            this.EncryptType = _partHeader.Read8(0x25);
            this.MajorVersion = _partHeader.Read8(0x26);
            this.MinorVersion = _partHeader.Read8(0x27);
            this.ExpiringMajorVersion = _partHeader.Read8(0x28);

            Type = _partitionTable?.Type ?? PartitionType.Other;
        }


        public long ImageOffset { get; private set; }
        public long VolumeHeaderSize { get; private set; }
        public bool AllFoldersParsed => this.FileSystem != null;
        public FidelityFileList FidelityFiles { get; internal set; }

        private IAreaFileSystemView _areaView;
        public IAreaFileSystemView AreaView => _areaView ??= AreaFileSystemView.TryBuild(this);

        public long Size { get; private set; }
        public long FsSize { get; private set; }
        public PartitionType Type { get; private set; }

        internal FstBlock FstBlock { get; private set; }
        public SiData SiData { get; set; }

        public int FstSize { get; private set; }
        public int FstOffset { get; }
        public int FstHashMode { get; private set; }
        public byte EncryptType { get; }
        public byte MajorVersion { get; }
        public byte MinorVersion { get; }
        public byte ExpiringMajorVersion { get; }
        public IFileSystem FileSystem { get; private set; }
        public bool InvalidFileSystem { get; private set; }
        public void Complete()
        {

        }
        internal void DebugFiles()
        {
            StringBuilder sb = new StringBuilder();
            foreach (FstFile f in (IList<IFsFile>)FileSystem?.Files)
                sb.AppendLine(f.Analysis.ToString());

            //Trace.WriteLine(sb.ToString());
        }

        public void ProcessBlock(long imageOffset, byte[] data, int fstSize, bool parseFst, IndexFile index)
        {
            FstBlock = new FstBlock(_header, imageOffset, data, fstSize, this.BlockSize, this.ImageOffset, this.EncryptType != 0, _imageSize, index);

            if (index != null)
                index.WiiUFstMismatch |= this.FstBlock.TmdFstMismatch;

            this.Size = _header.GetPartitionSize(this.ImageOffset, _imageSize); // GetPartitionSize FstBlock.Apps.Max(a => a.AreaOffset + a.Size);

            if (parseFst)
            {
                FileSystem = Fst.ParseWiiU(data, FstBlock.FstFileOffset, FstBlock.Multiplier);

                List<IFsFile> files = FileSystem?.Files;
                for (int i = 0; i < files.Count; i++)
                {
                    FstFile f = (FstFile)files[i];
                    FstFile n = i + 1 < files.Count && ((FstFile)files[i + 1]).WiiUAppIndex == f.WiiUAppIndex ? (FstFile)files[i + 1] : null;

                    if (n == null)
                        f.Analysis.Initialise(false, this.FstBlock.ContentHeaders[f.WiiUAppIndex].FsSizePaddedToBlock);
                    else
                        f.Analysis.Initialise(n.Analysis.SharedOffset, n.FsOffset);
                }
            }

            if (this.SiData != null)
                parseH3Table(this.SiData.TmdInfo, index);
        }

        private void parseH3Table(TmdInfo tmd, IndexFile index)
        {
            int entries;
            int size = 0;

            if (_partHeader != null)
            {
                entries = (int)_partHeader.ReadUInt32B(0x10);
                size = (int)_partHeader.ReadUInt32B(0xc);

                if (entries == 0 || size == 0)
                    return;
            }
            else
                entries = this.FstBlock.ContentHeaders.Length;

            int off = 0x40;

            using (SHA1 sha1 = SHA1.Create())
            {
                foreach (Content c in tmd.Content)
                {
                    ContentHeader app = this.FstBlock.ContentHeaders[c.Index];
                    app.Content = c;

                    if (_partHeader != null)
                    {
                        app.H3Offset = (int)_partHeader.ReadUInt32B(off + ((app.Index + 1) * 4));
                        int hashesLen = (app.Index + 2 < entries ? (int)_partHeader.ReadUInt32B(off + ((app.Index + 2) * 4)) : size) - app.H3Offset;
                        app.H3HashCount = hashesLen / 20;
                        app.H3Hashes = _partHeader.Read(off + app.H3Offset, hashesLen);
                    }
                    else
                    {
                        app.H3Offset = 0;
                        app.H3Hashes = index.Additional.FirstOrDefault(a => a.Name.Equals(c.ContentId.ToString("X8"), StringComparison.OrdinalIgnoreCase))?.Data ?? new byte[0];
                        app.H3HashCount = app.H3Hashes.Length / 20;
                    }

                    app.IsValid = c.Hash.Equals(0, sha1.ComputeHash(app.H3Hashes), 0, 20);
                }
            }
        }


    }
}