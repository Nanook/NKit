namespace Nanook.NKit
{

    /// <summary>
    /// Used to mark areas as missing (removed, scrubbed, junk) by formats. Rather than insert the data, just mark it for recovery where it can be paralleled
    /// Instances may use the offset for fs or etc. The code using them must be away and convert between them if required (nkit fs to disc removed areas etc)
    /// </summary>
    internal class MetaData
    {
        internal MetaData(long offset, long size, MetaDataType type, byte scrubByte, byte[] data)
        {
            Offset = offset;
            Size = size;
            Type = type;
            BlockByte = scrubByte;
            Data = data;
        }
        internal MetaData(long offset, long size, MetaDataType type, byte blockFillByte, byte[] data, bool isFs, int blockSize, int blockFsOffset, int blockFsSize, bool blockPin)
        {
            long fsOffset = offset;
            long fsSize = size;

            if (blockSize != blockFsSize)
            {
                if (isFs)
                {
                    size = Buffer.FsOffsetToOffset(offset + size, blockSize, blockFsOffset, blockFsSize, blockPin);
                    offset = Buffer.FsOffsetToOffset(fsOffset, blockSize, blockFsOffset, blockFsSize, blockPin);
                    size -= offset; //turn back in to size from end offset (correctly spanning hash blocks)
                }
                else
                {
                    fsSize = Buffer.OffsetToFsOffset(fsOffset + fsSize, blockSize, blockFsOffset, blockFsSize);
                    fsOffset = Buffer.OffsetToFsOffset(fsOffset, blockSize, blockFsOffset, blockFsSize);
                    fsSize -= fsOffset;
                }
            }

            Offset = offset;
            Size = size;
            Type = type;
            BlockByte = blockFillByte;
            Data = data;

            FsOffset = fsOffset;
            FsSize = fsSize;
        }

        public long Offset { get; internal set; }
        public long Size { get; set; }
        public long FsOffset { get; internal set; }
        public long FsSize { get; set; }
        public MetaDataType Type { get; internal set; }
        public byte BlockByte { get; internal set; }
        public byte[] Data { get; set; }

    }
}