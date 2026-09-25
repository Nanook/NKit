namespace Nanook.NKit
{
    public enum DataType { Data, NJunk, Fill, Mode2Fm2, Other }

    public enum MetaDataType { Data, NJunk, Fill, NJunkFile }

    internal interface ISectionData
    {
        long ImageOffset { get; }
        long AreaOffset { get; }

        DataType DataType { get; }
        byte FillByte { get; }
        int DataNulls { get; }

        long OffsetInItem { get; }
        long Offset { get; }
        long FsOffset { get; }
        long FsSize { get; }
        uint Crc { get; }
        ulong XxHash { get; }

        bool IsFile { get; }

        void SetParseType(string type, bool isInfo);
        string ToString();
    }
}