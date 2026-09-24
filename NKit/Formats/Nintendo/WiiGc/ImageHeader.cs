using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class ImageHeader : IImageHeader
    {

        private List<PartitionInfo> _partitions;

        public string Id => Encoding.ASCII.GetString(Data, 0, 4);
        public string Id6 => Encoding.ASCII.GetString(Data, 0, 6);
        public string Id8 => string.Concat(Id6, Data[6].ToString("X2"), Data[7].ToString("X2"));

        public int Revision => Data[7];

        public int DiscNo => Data[6];

        public string Title { get; private set; }
        public bool HasUpdatePartition => _partitions != null && _partitions.Count != 0 && _partitions[0].Type == PartitionType.Update;
        internal PartitionInfo[] Partitions => _partitions.ToArray();

        public long ImageOffset { get; protected set; }
        public byte[] Data { get; protected set; }
        private bool _isWii;

        public bool IsDatel
        {
            get
            {
                if (!_isWii)
                    return this.Id6 == "DTLX01" || (this.Id6 == "GNHE5d" && this.Data.ReadUInt32B(WiiConsts.FstSizeOffset) == 0x24);
                else
                    return this.Id6 == "RFLP5D" && this.Title == "FREELOADER";
            }
        }
        internal ImageHeader(byte[] header, bool isWii)
        {
            ImageOffset = 0;
            _isWii = isWii;
            this.Update(header, _isWii);
        }

        internal void Update(byte[] header, bool isWii)
        {
            Data = header;
            Title = Data.ReadStringToNull(0x20, 0x60);

            if (isWii)
                _partitions = CreatePartitionInfos(header, WiiConsts.WiiDiscHdrPtnOffset)/*.OrderBy(a => a.ImageOffset)*/.ToList();
        }

        internal IEnumerable<PartitionInfo> CreatePartitionInfos(byte[] section, int offset)
        {
            for (int tableIdx = 0; tableIdx < 4; tableIdx++) //up to 4 partitions on the disk
            {
                uint c = section.ReadUInt32B(offset + (tableIdx * 8)); //count of partitions for tableIdx

                //_log.Message(string.Format("Table {0} - Partitions {1}", tableIdx.ToString(), c.ToString("X8")), 2);
                if (c == 0)
                    continue;

                int tableOffset = (int)section.ReadUInt32B(offset + (tableIdx * 8) + 4) * 4; //first WipePartition entry for tableIdx
                int adjustReadOffset = offset + (tableOffset - WiiConsts.WiiDiscHdrPtnOffset);
                for (int i = 0; i < c; i++)
                {
                    long partitionOffset = section.ReadUInt32B(adjustReadOffset + (i * 8)) * 4L;
                    PartitionType partitionType = (PartitionType)section.ReadUInt32B(adjustReadOffset + (i * 8) + 4);

                    //_log.Message(string.Format("  PartitionOffset Offset {0} - Type {1}", partitionOffset.ToString("X8"), partitionType.ToString()), 2);
                    yield return new PartitionInfo(partitionType, partitionOffset, tableIdx, tableOffset + (i * 8));
                    //_log.Message(string.Format("    ID {0}", partitions.Last().ReadStream.Id), 2);
                }


            }
        }

        internal PartitionInfo GetPartition(long imageOffset) => Partitions?.Where(a => a.ImageOffset >= imageOffset)?.OrderBy(a => a.ImageOffset)?.FirstOrDefault();

        internal long GetPartitionSize(long imageOffset, long imageSize)
        {
            PartitionInfo next = Partitions?.Where(a => a.ImageOffset >= imageOffset)?.OrderBy(a => a.ImageOffset)?.FirstOrDefault(a => a.ImageOffset > imageOffset);
            return (next?.ImageOffset ?? imageSize) - imageOffset;
        }

        internal void RemoveUpdatePartition(long baseAddress)
        {
            if (Partitions.Length == 0 || Partitions[0].Type != PartitionType.Update)
                return;

            _partitions.RemoveAt(0);

            Array.Clear(Data, 0x60, 2);
            Array.Clear(Data, WiiConsts.WiiDiscHdrPtnOffset, WiiConsts.WiiDiscHdrPtnSize);

            //write the WipePartition info
            PartitionInfo firstNonUpdate = _partitions.FirstOrDefault(a => a.Type != PartitionType.Update);

            foreach (IGrouping<int, PartitionInfo> grp in _partitions.GroupBy(a => a.Table))
            {
                int offset = (int)(WiiConsts.WiiDiscHdrPtnOffset + 0x20 + (grp.Key * 0x20L));

                Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (grp.Key * 0x8L)), (uint)grp.Count());
                Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (grp.Key * 0x8L) + 4), (uint)(offset / 4));

                offset -= 4; //adjust for the first calc
                foreach (PartitionInfo part in grp)
                {
                    Data.WriteUInt32B(offset += 4, (uint)(part.ImageOffset / 4L));
                    part.TableOffset = offset;
                    Data.WriteUInt32B(offset += 4, (uint)part.Type);
                }
            }
        }

        internal void AddPartitionPlaceHolder(PartitionInfo partition)
        {
            _partitions.Add(partition);
            UpdateRepair();
        }

        public void RemovePartitionChannels(long afterOffset)
        {
            _partitions.RemoveAll(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game && a.Type != PartitionType.GameData && a.ImageOffset >= afterOffset);
            UpdateRepair();
        }

        public void RemovePartitionChannels()
        {
            _partitions.RemoveAll(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game && a.Type != PartitionType.GameData);
            UpdateRepair();
        }


        private long offsetSortFix(PartitionInfo p)
        {
            if (p.ImageOffset == 0 && p.IsPlaceholder)
                return WiiConsts.WiiDefaultDataPtnOffset + (long)p.Type;

            return p.ImageOffset;
        }

        public void UpdateOffsets()
        {
            foreach (IGrouping<int, PartitionInfo> grp in _partitions.GroupBy(a => a.Table))
            {
                foreach (PartitionInfo part in grp)
                    Data.WriteUInt32B((int)part.TableOffset, (uint)(part.ImageOffset / 4L));
            }
        }

        public void UpdateRepair()
        {
            try
            {
                _partitions.Sort((a, b) => offsetSortFix(a) < offsetSortFix(b) ? -1 : (offsetSortFix(a) > offsetSortFix(b) ? 1 : 0));

                Array.Clear(Data, 0x60, 2);
                Array.Clear(Data, WiiConsts.WiiDiscHdrPtnOffset, WiiConsts.WiiDiscHdrPtnSize);

                //write the WipePartition info
                long partOffsetFix = 0;
                PartitionInfo firstNonUpdate = _partitions.FirstOrDefault(a => a.Type != PartitionType.Update);
                if (firstNonUpdate != null && firstNonUpdate.ImageOffset < WiiConsts.WiiDefaultDataPtnOffset)
                {
                    partOffsetFix = WiiConsts.WiiDefaultDataPtnOffset - firstNonUpdate.ImageOffset;
                    foreach (PartitionInfo pi in _partitions.Where(a => a.Type != PartitionType.Update))
                        UpdateImageOffset(pi, pi.ImageOffset + partOffsetFix);
                }

                foreach (IGrouping<int, PartitionInfo> grp in _partitions.GroupBy(a => a.Table))
                {
                    int offset = (int)(WiiConsts.WiiDiscHdrPtnOffset + 0x20 + (grp.Key * 0x20L));

                    Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (grp.Key * 0x8L)), (uint)grp.Count());
                    Data.WriteUInt32B((int)(WiiConsts.WiiDiscHdrPtnOffset + (grp.Key * 0x8L) + 4), (uint)(offset / 4));

                    offset -= 4; //adjust for the first calc
                    foreach (PartitionInfo part in grp)
                    {
                        Data.WriteUInt32B(offset += 4, (uint)(part.ImageOffset / 4L));
                        part.TableOffset = offset;
                        Data.WriteUInt32B(offset += 4, (uint)part.Type);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "ImageHeader.Update");
            }
        }

        internal void UpdateImageOffset(PartitionInfo partitionTable, long imageOffset) => partitionTable.ImageOffset = imageOffset;
    }
}