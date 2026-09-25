using System.Collections.Generic;

namespace Nanook.NKit
{
    internal class SectionData : ISectionData
    {
        public SectionData(long sectionImageOffset, AreaInfo areaInfo, long fsOffset, long fsSize)
        {
            this.FsOffset = fsOffset;
            this.Offset = Buffer.FsOffsetToOffset(fsOffset, areaInfo.BlockSize, areaInfo.BlockFsOffset, areaInfo.BlockFsSize, false);
            this.ImageOffset = sectionImageOffset + this.Offset;
            this.AreaOffset = sectionImageOffset - areaInfo.ImageOffset + this.Offset;
            this.FsSize = fsSize;
        }

        public SectionData()
        {

        }

        public long ImageOffset { get; internal set; }
        public long AreaOffset { get; internal set; }
        public long AreaFsOffset { get; internal set; }

        public DataType DataType { get; internal set; }
        public byte FillByte { get; internal set; }
        public int DataNulls { get; internal set; }

        public long OffsetInItem { get; internal set; }
        public long Offset { get; internal set; }
        public long FsOffset { get; internal set; }
        public long FsSize { get; internal set; }
        public uint Crc { get; internal set; }
        public bool IsFile { get; internal set; }
        public List<string> FileSystems { get; set; }

        public long FullSize { get; internal set; }
        public uint FullCrc { get; internal set; }
        public ulong XxHash { get; internal set; }
        public bool SystemFile { get; internal set; }

        /// <summary>
        /// Sets the DataType to the enum.
        /// </summary>
        /// <param name="type"></param>
        /// <returns>True if System Data</returns>
        public void SetParseType(string type, bool isInfo)
        {
            if (type.StartsWith("Sys"))
                this.SystemFile = true;
            if (type.EndsWith("Data"))
                this.DataType = DataType.Data;
            else if (type == "Nulls")
            {
                this.DataType = DataType.Fill;
                this.FillByte = 0;
            }
            else if (type == "Mode2Fm2")
                this.DataType = DataType.Mode2Fm2;
            else if (type == "Other") //there will be Gap Data
                this.DataType = DataType.Other;
            else if (type.StartsWith("Fill"))
            {
                this.DataType = DataType.Fill;
                if (type.Length > 4)
                    this.FillByte = byte.Parse(type.Substring(5), System.Globalization.NumberStyles.HexNumber);
            }
            else if (type.StartsWith("NJunk"))
            {
                this.DataType = DataType.NJunk;
                if (type.Length > 5)
                    this.DataNulls = int.Parse(type.Substring(6), System.Globalization.NumberStyles.HexNumber);
            }
        }

        public override string ToString() => string.Format("ImageOffset:{0}, AreaFsOffset:{1}, FsOffset:{2}, Size:{3}, CRC:{4}, IsFile:{5}, DataType:{6}, FillByte:{7}", ImageOffset.ToString("X9"), AreaFsOffset.ToString("X9"), FsOffset.ToString("X8"), FsSize.ToString("X8"), Crc.ToString("X8"), IsFile ? "File" : "Gap", DataType.ToString(), FillByte.ToString("X2"));
    }
}