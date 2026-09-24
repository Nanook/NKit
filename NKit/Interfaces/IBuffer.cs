using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    public class RangeResult
    {
        public int BufferOffset;
        public long RangeOffset;
        public int Size;
        public bool RangeComplete; //the match contains the end of the range
        public bool BufferFull; //the match goes to the end of the CiBuffer
        public bool IsMatch;
    }


    internal interface IBuffer
    {

        AreaInfo AreaInfo { get; }

        //////////////////////////////////////////////////
        //assigned from an IImage object when data is read from the iso
        long ImageOffset { get; }
        long AreaOffset { get; }
        int Size { get; }

        int BlockSize { get; }
        int BlockFsOffset { get; }
        int BlockFsSize { get; }

        byte[] Decrypted { get; }
        byte[] Encrypted { get; }
        bool IsEncrypted { get; }
        int FileEndIndex { get; set; }
        int FileStartIndex { get; set; }
        //////////////////////////////////////////////////

        IPatchInfo PatchInfo { get; }
        List<MetaData> MissingData { get; }
        AreaType Type { get; }
        bool SkippedTo { get; }
        IBuffer Clone();
        void CopyTo(IBuffer buffer);

        byte[] ReadFsBytes(int fsOffset, int fsSize);
        void ReadFs(int fsOffset, byte[] dst, int dstFsOffset, int dstBlockSize, int dstBlockFsOffset, int dstBlockFsSize, int fsSize);
        void WriteFs(byte[] src, int srcFsOffset, int srcBlockSize, int srcBlockFsOffset, int srcBlockFsSize, int fsOffset, int fsSize);
        bool ProcessFsData(int fsOffset, int fsSize, Func<byte[], int, int, int, bool> process);


        //////////////////////////////////////////////////
        //could move to fsinfo - or be centralised there
        long FsOffset { get; }
        int FsSize { get; }
        int FsIndex { get; }

        void ReadFsToStream(int fsOffset, Stream stream, int fsSize);
        void TestFsRange(long fsOffset, long size, RangeResult result);
        void TestRange(long offset, long size, RangeResult result);
        void TestRangeBounds(long offset, long size, long customOffset, long customSize, RangeResult result);
        void Update(long imageOffset, long areaOffset, int size, int fsIndex, bool isEncrypted, bool skippedTo);
        void ReInitialise(AreaInfo areaInfo, bool clearMissingAndFiles);
        void WriteFsFromStream(int fsOffset, Stream stream, int fsSize);
        void SetFsMissingData(int fsOffset, int fsSize, MetaDataType type, byte scrubByte);
        void ClearFs(int fsOffset, int fsSize);
        uint CrcFsData(int fsOffset, int fsSize);
        ulong XxHashFsData(int fsOffset, int fsSize);
        //////////////////////////////////////////////////
    }
}