using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class ImageHeader : IImageHeader
    {
        public ImageHeader(byte[] header)
        {
            this.Data = header;
            this.KeyCommon = WiiUConsts.KeyCommon;
            this.KeyCommonDev = WiiUConsts.KeyCommonDev;
            //set defaults to be used before decrypting the WipePartition table
            this.BlockSize = WiiUConsts.DefaultSectorSize;
            this.BlockSizeHashed = this.BlockSize * 2;
            this.H0BlockSize = this.BlockSizeHashed;
            this.H1BlockSize = this.H0BlockSize * WiiUConsts.H0Count;
            this.H2BlockSize = this.H1BlockSize * WiiUConsts.H1Count;
            this.H3BlockSize = this.H2BlockSize * WiiUConsts.H2Count;
            this.HashesSize = WiiUConsts.HashSize;
            this.BlockFsSizeHashed = this.BlockSizeHashed - this.HashesSize;
            this.SiData = new List<SiData>();
            this.EncryptedNoKeyMode = false;
        }

        public List<PartitionInfo> Partitions { get; private set; }
        public byte[] Data { get; }
        public byte[] Hash { get; private set; }
        public uint Id { get; private set; }
        public int BlockSize { get; private set; }
        public int BlockSizeHashed { get; private set; }
        public int BlockFsSizeHashed { get; private set; }
        public int HashesSize { get; private set; }
        public List<SiData> SiData { get; internal set; }
        public byte[] KeyCommon { get; internal set; }
        public byte[] KeyCommonDev { get; internal set; }
        public byte[] Key { get; internal set; }
        public byte[] InitialKey { get; internal set; }
        public int H0BlockSize { get; private set; }
        public int H1BlockSize { get; private set; }
        public int H2BlockSize { get; private set; }
        public int H3BlockSize { get; private set; }
        public bool EncryptedNoKeyMode { get; internal set; }

        public byte[] GetKeyToStore()
        {
            //if the key is different to that from a key file then store it if required
            if (Key != null && (InitialKey == null || !Key.Equals(InitialKey)))
                return Key;
            return null;
        }

        internal PartitionInfo GetPartition(long imageOffset) => Partitions?.FirstOrDefault(a => imageOffset == a.ImageOffset);

        internal PartitionInfo GetNextPartition(long imageOffset) => Partitions?.Where(a => a.ImageOffset >= imageOffset)?.OrderBy(a => a.ImageOffset)?.FirstOrDefault();

        internal long GetPartitionSize(long imageOffset, long imageSize)
        {
            PartitionInfo next = Partitions?.Where(a => a.ImageOffset >= imageOffset)?.OrderBy(a => a.ImageOffset)?.FirstOrDefault(a => a.ImageOffset > imageOffset);
            return (next?.ImageOffset ?? imageSize) - imageOffset;
        }

        public void Update(int offset, SiData si)
        {
            this.BlockSizeHashed = this.BlockSize * 2;
            this.H0BlockSize = this.BlockSizeHashed;
            this.H1BlockSize = this.H0BlockSize * WiiUConsts.H0Count;
            this.H2BlockSize = this.H1BlockSize * WiiUConsts.H1Count;
            this.H3BlockSize = this.H2BlockSize * WiiUConsts.H2Count;
            this.HashesSize = WiiUConsts.HashSize;
            this.BlockFsSizeHashed = this.BlockSizeHashed - this.HashesSize;

            List<PartitionInfo> parts = new List<PartitionInfo>();

            parts.Add(new PartitionInfo(PartitionType.Game, offset, 0, 0) { WiiUSiData = si });

            this.Partitions = parts;
        }

        public void Update(byte[] partitionTableData)
        {
            this.Id = partitionTableData.ReadUInt32B(WiiUConsts.DiscContentIdOffset); //CCA6E67B
            this.Hash = partitionTableData.Read(WiiUConsts.DiscContentHashOffset, 0x20);
            int volumes = (int)partitionTableData.ReadUInt32B(WiiUConsts.DiscContentVolumeCountOffset);
            this.BlockSize = (int)partitionTableData.ReadUInt32B(WiiUConsts.DiscContentLengthOffset);
            this.BlockSizeHashed = this.BlockSize * 2;
            this.H0BlockSize = this.BlockSizeHashed;
            this.H1BlockSize = this.H0BlockSize * WiiUConsts.H0Count;
            this.H2BlockSize = this.H1BlockSize * WiiUConsts.H1Count;
            this.H3BlockSize = this.H2BlockSize * WiiUConsts.H2Count;
            this.HashesSize = WiiUConsts.HashSize;
            this.BlockFsSizeHashed = this.BlockSizeHashed - this.HashesSize;

            List<PartitionInfo> parts = new List<PartitionInfo>();

            for (int i = 0; i < volumes; i++)
            {
                int tableOffset = WiiUConsts.DiscContentVolumesOffset + (i * WiiUConsts.DiscContentVolumeSize);
                string volumeId = partitionTableData.ReadStringToNull(tableOffset + WiiUConsts.DiscContentPartitionVolumeOffset);
                int count = partitionTableData.Read8(tableOffset + WiiUConsts.DiscContentPartitionCountOffset);
                for (int c = 0; c < count; c++)
                {
                    long offset = partitionTableData.ReadUInt32B(tableOffset + WiiUConsts.DiscContentPartitionsOffsets + (c * 4)) * BlockSize;
                    PartitionType type;
                    switch (volumeId.Substring(0, 2))
                    {
                        case "SI":
                            type = PartitionType.Si;
                            break;
                        case "UP":
                            type = PartitionType.Update;
                            break;
                        case "GM":
                            type = PartitionType.Game;
                            break;
                        case "GI":
                            type = PartitionType.GameUpdate;
                            break;
                        default:
                            type = PartitionType.Other;
                            break;
                    }
                    PartitionInfo part = new PartitionInfo(type, offset, i, tableOffset)
                    {
                        Id = volumeId
                    };
                    ulong tid;
                    if (part.Id.Length >= 18 && ulong.TryParse(part.Id.Substring(2, 16), System.Globalization.NumberStyles.HexNumber, null, out tid))
                        part.WiiUTitleId = tid;

                    parts.Add(part);
                }
            }

            PartitionInfo ptn = parts.FirstOrDefault(a => a.Type == PartitionType.Game && a.Id.StartsWith(WiiUConsts.GameTitlePrefix))
                             ?? parts.FirstOrDefault(a => a.Type == PartitionType.Game && a.Id.StartsWith(WiiUConsts.GameTitlePrefix.Rot3Hex()))
                             ?? parts.FirstOrDefault(a => a.Type == PartitionType.Update);
            if (ptn != null)
                ptn.IsMainContent = true;

            Partitions = parts;
        }
    }

}