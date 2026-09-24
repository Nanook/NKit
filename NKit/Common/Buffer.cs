using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    /// <summary>
    /// The Buffer is central to the NKit Engine. Buffers are created when the engine is created and reused. A CiBuffer can hold multiple blocks/sectors. It will never span areas. 
    /// </summary>
    internal class Buffer : IBuffer
    {
        public AreaInfo AreaInfo { get; private set; }
        public AreaType Type => AreaInfo.Type;
        public bool SkippedTo { get; private set; }

        /// <summary>
        /// Size of blocks
        /// </summary>
        public int BlockSize => AreaInfo.BlockSize;
        /// <summary>
        /// Offset in a block where the Fs data is (0x400 for Wii etc)
        /// </summary>
        public int BlockFsOffset => AreaInfo.BlockFsOffset;
        /// <summary>
        /// Size of the FsData in a block. This allows for data to not fill the block. Iso Mode1/Mode2
        /// </summary>
        public int BlockFsSize => AreaInfo.BlockFsSize;

        /// <summary>
        /// ImageOffset of this CiBuffer
        /// </summary>
        public long ImageOffset { get; private set; }
        /// <summary>
        /// Offset of this CiBuffer in the current area
        /// </summary>
        public long AreaOffset { get; private set; }
        /// <summary>
        /// FileSystem Offset. Area Offset without any extra data like CRC or hashes etc
        /// </summary>
        public long FsOffset { get; private set; }
        public int Size { get; private set; }
        public int FsSize { get; private set; }
        public int FsIndex { get; private set; }
        public List<MetaData> MissingData { get; private set; }
        public byte[] Decrypted { get; private set; }
        public byte[] Encrypted => this.IsEncrypted ? _encrypted : Decrypted;  //this.AreaInfo.IsEncrypted
        private byte[] _encrypted;
        public bool IsEncrypted { get; set; }

        public IPatchInfo PatchInfo { get; internal set; }
        public int FileStartIndex { get; set; }
        public int FileEndIndex { get; set; }

        public Buffer(int size, bool isEncryptionSupported) : this(isEncryptionSupported, new byte[size])
        {

        }

        /// <summary>
        /// Set isEncryptionSupported to true if the image needs a second CiBuffer for encrypted data
        /// </summary>
        public Buffer(bool isEncryptionSupported, byte[] decrypted)
        {
            this.PatchInfo = new PatchInfo();
            this.Decrypted = decrypted;
            _encrypted = isEncryptionSupported ? new byte[Decrypted.Length] : null;
            this.MissingData = new List<MetaData>();
            this.FileStartIndex = -1;
            this.FileEndIndex = -1;
        }


        /// <summary>
        /// Set per read by image. Wii and WiiU change these values as they reuse the buffers for the various areas
        /// </summary>
        public void ReInitialise(AreaInfo areaInfo, bool clearMissingAndFiles)
        {
            if (areaInfo == null)
                throw new Exception("Buffer must have AreaInfo");
            this.AreaInfo = areaInfo;
            if (this.Encrypted == null && (areaInfo.IsEncryptionSupported || areaInfo.IsEncrypted))
                throw new Exception("Encryption is not supported by this buffer");

            if (clearMissingAndFiles)
            {
                this.FileStartIndex = -1;
                this.FileEndIndex = -1;
                this.MissingData.Clear();
            }
        }

        public void UpdateSize(int size) => this.Size = size;

        public IBuffer Clone()
        {
            Buffer buffer = new Buffer(this.Decrypted.Length, this.Encrypted != null);
            CopyTo(buffer); //copy this to the new CiBuffer
            return buffer;
        }

        public void CopyTo(IBuffer buffer)
        {
            Buffer b = (Buffer)buffer;
            b.ReInitialise(this.AreaInfo, true);
            b.Update(this.ImageOffset, this.AreaOffset, this.Size, this.FsIndex, this.AreaInfo.IsEncrypted, this.SkippedTo);

            b.FsIndex = this.FsIndex;
            b.PatchInfo.MarkForCalculatedData = this.PatchInfo.MarkForCalculatedData;
            b.PatchInfo.MarkForPatching = this.PatchInfo.MarkForPatching;
            b.MissingData = new List<MetaData>(MissingData);
            b.FileStartIndex = this.FileStartIndex;
            b.FileEndIndex = this.FileEndIndex;
            this.Decrypted.CopyTo(b.Decrypted, 0);
            if (AreaInfo.IsEncryptionSupported)
                _encrypted.CopyTo(b._encrypted, 0);
        }

        public void Update(long imageOffset, long areaOffset, int size, int fsIndex, bool isEncrypted, bool skippedTo)
        {
            this.FsIndex = fsIndex;
            this.ImageOffset = imageOffset;
            this.AreaOffset = areaOffset;
            this.Size = size;
            this.FsOffset = Buffer.OffsetToFsOffset(this.AreaOffset, this.BlockSize, this.BlockFsOffset, this.BlockFsSize);
            this.FsSize = (int)(Buffer.OffsetToFsOffset(this.AreaOffset + size, this.BlockSize, this.BlockFsOffset, this.BlockFsSize) - FsOffset);
            this.PatchInfo.MarkForCalculatedData = false;
            this.PatchInfo.MarkForPatching = false;
            this.IsEncrypted = isEncrypted;
            this.SkippedTo = skippedTo;

            if (this.IsEncrypted) //swap the buffers
            {
                if (this.Encrypted == null || !this.AreaInfo.IsEncryptionSupported)
                    throw new Exception("Encryption not supported");
                byte[] tmp = Decrypted;
                Decrypted = _encrypted;
                _encrypted = tmp;
            }
        }

        public void ReadComplete()
        {
        }

        public byte[] ReadFsBytes(int fsOffset, int fsSize)
        {
            byte[] b = new byte[fsSize];
            ReadFs(fsOffset, b, 0, b.Length, 0, b.Length, fsSize);
            return b;
        }

        public void ReadFs(int fsOffset, byte[] dst, int dstFsOffset, int dstBlockSize, int dstBlockFsOffset, int dstBlockFsSize, int fsSize) => Copy(Decrypted, fsOffset, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, dst, dstFsOffset, dstBlockSize, dstBlockFsOffset, dstBlockFsSize, fsSize);

        public void WriteFs(byte[] src, int srcFsOffset, int srcBlockSize, int srcBlockFsOffset, int srcBlockFsSize, int fsOffset, int fsSize) => Copy(src, srcFsOffset, srcBlockSize, srcBlockFsOffset, srcBlockFsSize, Decrypted, fsOffset, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, fsSize);

        public void ClearFs(int fsOffset, int fsSize) => ProcessFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, (data, off, fsOff, sz) => { data.Clear(off, sz, 0x00); return true; });

        public void ReadFsToStream(int fsOffset, Stream stream, int fsSize) => ProcessFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, (data, off, fsOff, sz) => { stream.Write(data, off, sz); return true; });

        public void WriteFsFromStream(int fsOffset, Stream stream, int fsSize) => ProcessFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, (data, off, fsOff, sz) => stream.Read(data, off, sz) != 0);

        public bool ProcessFsData(int fsOffset, int fsSize, Func<byte[], int, int, int, bool> process) => ProcessFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, process);

        public uint CrcFsData(int fsOffset, int fsSize) => CrcFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize);

        public ulong XxHashFsData(int fsOffset, int fsSize) => XxHashFsData(this.Decrypted, fsOffset, fsSize, this.BlockSize, this.BlockFsOffset, this.BlockFsSize);

        public void TestFsRange(long fsOffset, long size, RangeResult result) => TestRangeBounds(fsOffset, size, FsOffset, FsSize, result);

        public void TestRange(long offset, long size, RangeResult result) => TestRangeBounds(offset, size, ImageOffset, Size, result);

        public void TestRangeBounds(long offset, long size, long boundsOffset, long boundsSize, RangeResult result)
        {
            if (offset >= boundsOffset) //we have the start of the gap
            {
                result.IsMatch = (offset == boundsOffset && size == 0) || offset < boundsOffset + boundsSize; //offset is between boundsOffset and bounds end
                if (result.IsMatch)
                {
                    result.BufferOffset = (int)(offset - boundsOffset); //should match gap.IgnoreStart
                    result.Size = (int)(Math.Min(offset + size, boundsOffset + boundsSize) - offset);
                }
                result.RangeOffset = 0;

            }
            else
            {
                result.IsMatch = offset + size > boundsOffset; //end is between boundsOffset and bounds end
                if (result.IsMatch)
                {
                    result.RangeOffset = boundsOffset - offset;
                    result.Size = (int)(Math.Min(offset + size, boundsOffset + boundsSize) - offset);
                }
                result.BufferOffset = 0;
            }

            if (result.IsMatch)
            {
                result.Size = (int)Math.Max(Math.Min(offset + size - boundsOffset, boundsSize) - result.BufferOffset, 0L);
                result.RangeComplete = result.IsMatch && offset + size <= boundsOffset + boundsSize;
                result.BufferFull = result.IsMatch && result.BufferOffset + result.Size == boundsSize;
            }
            else
            {
                result.BufferOffset = 0;
                result.RangeOffset = 0;
                result.Size = 0;
                result.RangeComplete = false;
                result.BufferFull = false;
            }
        }

        public void SetFsMissingData(int fsOffset, int fsSize, MetaDataType type, byte scrubByte) => MissingData.Add(new MetaData(fsOffset, fsSize, type, scrubByte, null, true, this.BlockSize, this.BlockFsOffset, this.BlockFsSize, true));

        internal static long FsLenToHashedLen(long dataLen, int blockSize, int blockFsOffset, int blockFsSize)
        {
            if (blockSize == blockFsSize)
                return dataLen;

            return (dataLen / blockFsSize * blockSize) + (dataLen % blockFsSize);
        }
        internal static long HashedLenToFsLen(long dataLen, int blockSize, int blockFsOffset, int blockFsSize)
        {
            if (blockSize == blockFsSize)
                return dataLen;

            return (dataLen / blockSize * blockFsSize) + (dataLen % blockSize);
        }

        public static long OffsetToFsOffset(long o, int blockSize, int blockFsOffset, int blockFsSize)
        {
            if (blockSize == blockFsSize)
                return o;

            return (o / blockSize * blockFsSize) + ((o % blockSize) > blockFsOffset ? (o % blockSize) - blockFsOffset : 0L);
        }

        public static long FsOffsetToOffset(long o, int blockSize, int blockFsOffset, int blockFsSize, bool blockPin)
        {
            if (blockSize == blockFsSize)
                return o;

            long rm = o % blockFsSize;
            long offset = o / blockFsSize * blockSize;

            if (rm != 0)
                offset += blockFsOffset + rm;
            else if (!blockPin)//is it the start of the next sector (0x400 for wii)
                offset += blockFsOffset; //pull it back so we don't affect the following sector

            return offset;
        }

        public static uint CrcFsData(byte[] data, int fsOffset, int fsSize, int blockSize, int blockFsOffset, int blockFsSize)
        {
            Nanook.NKit.Crc crc = new Crc();

            ProcessFsData(data, fsOffset, fsSize, blockSize, blockFsOffset, blockFsSize,
                (src, rOff, fsOff, sz) =>
                {
                    crc.Sum(src, rOff, sz);
                    return true; //keep processing
                });
            return crc.Value;
        }

        public static ulong XxHashFsData(byte[] data, int fsOffset, int fsSize, int blockSize, int blockFsOffset, int blockFsSize)
        {
            XXHash64 xx = XXHash64.Create();
            using (Stream strm = new CryptoStream(Stream.Null, xx, CryptoStreamMode.Write))
            {
                ProcessFsData(data, fsOffset, fsSize, blockSize, blockFsOffset, blockFsSize,
                    (src, rOff, fsOff, sz) =>
                    {
                        strm.Write(src, rOff, sz);
                        return true; //keep processing
                    });
            }
            return xx.HashUInt64;
        }

        internal static bool ProcessFsData(byte[] data, int fsOffset, int fsSize, int blockSize, int blockFsOffset, int blockFsSize, Func<byte[], int, int, int, bool> process)
        {
            int sOff = (int)FsOffsetToOffset(fsOffset, blockSize, blockFsOffset, blockFsSize, false);
            int sBlk = blockFsSize;
            int sNxt = blockSize - sBlk;
            int sEnd = blockFsOffset + blockFsSize;
            int sRmn = sEnd - (sOff % blockSize);

            int sz;
            while (fsSize != 0)
            {
                sz = Math.Min(sRmn, fsSize); //which is least

                if (!process(data, sOff, fsOffset, sz))
                    return false;

                sOff += sz;
                fsOffset += sz;
                sRmn -= sz;
                if (sRmn == 0)
                {
                    sRmn = sBlk;
                    sOff += sNxt;
                }

                fsSize -= sz;
            }
            return true;
        }

        //handles copying data from one possible hashed array to another
        internal static void Copy(byte[] src, int srcOffset, int srcBlockSize, int srcBlockFsOffset, int srcBlockFsSize, byte[] dst, int dstOffset, int dstBlockSize, int dstBlockFsOffset, int dstBlockFsSize, int copyLen)
        {
            int sOff = (int)FsOffsetToOffset(srcOffset, srcBlockSize, srcBlockFsOffset, srcBlockFsSize, false);
            int sBlk = srcBlockFsSize;
            int sNxt = srcBlockSize - sBlk;
            int sEnd = srcBlockFsOffset + srcBlockFsSize;
            int sRmn = sEnd - (sOff % srcBlockSize);

            int dOff = (int)FsOffsetToOffset(dstOffset, dstBlockSize, dstBlockFsOffset, dstBlockFsSize, false);
            int dBlk = dstBlockFsSize;
            int dNxt = dstBlockSize - dBlk;
            int dEnd = dstBlockFsOffset + dstBlockFsSize;
            int dRmn = dEnd - (dOff % dstBlockSize);

            int sz;
            while (copyLen != 0)
            {
                sz = Math.Min((sRmn < dRmn) ? sRmn : dRmn, copyLen); //which is least

                Array.Copy(src, sOff, dst, dOff, sz);

                sOff += sz;
                sRmn -= sz;
                if (sRmn == 0)
                {
                    sRmn = sBlk;
                    sOff += sNxt;
                }

                dOff += sz;
                dRmn -= sz;
                if (dRmn == 0)
                {
                    dRmn = dBlk;
                    dOff += dNxt;
                }

                copyLen -= sz;
            }
        }

    }
}