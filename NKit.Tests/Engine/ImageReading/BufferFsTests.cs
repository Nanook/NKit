using Nanook.NKit;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class BufferFsTests
    {

        //THIS TEST IS NOT LONGER USEFUL AS ADDRESS MODE IS NOT SET BY NextArea
        [Fact]
        public void AreaInfoNextArea()
        {
            long end = 0x46DB4880L;
            List<IImageArea> areas = new List<IImageArea>();
            areas.Add(new ImageArea(0L, AreaType.FileSystem));
            areas.Add(new ImageArea(0x15BFA0, AreaType.Audio));
            areas.Add(new ImageArea(0x2E0260L, AreaType.FileSystem));
            areas.Add(new ImageArea(0x46582E90L, AreaType.Audio));
            areas.Add(new ImageArea(0x466B0F30L, AreaType.FileSystem));

            AreaInfo ai;

            ai = AreaInfo.NextArea(-1, -1, -1, -1, -1, AreaType.None, areas, 0);
            Assert.Equal(0, ai.ImageOffset);
            Assert.Equal(AddressMode.Area, ai.FsAddressMode);
            Assert.Equal(AreaType.FileSystem, ai.Type);

            ai = AreaInfo.NextArea(0x15BFA0, 0x2E0260L, -1, -1, -1, AreaType.None, areas, 0);
            Assert.Equal(0x2E0260L, ai.ImageOffset);
            Assert.Equal(AddressMode.Area, ai.FsAddressMode);
            Assert.Equal(AreaType.FileSystem, ai.Type);

            ai = AreaInfo.NextArea(0x2E0260L, 0x2E0260L, end - 0x2E0260L, end, -1, AreaType.None, areas, 0);
            Assert.Equal(0x46582E90L, ai.ImageOffset);
            Assert.Equal(AddressMode.Area, ai.FsAddressMode);
            Assert.Equal(AreaType.Audio, ai.Type);

            ai = AreaInfo.NextArea(0x46582E90L, 0x2E0260L, end - 0x2E0260L, end, -1, AreaType.None, areas, 0);
            Assert.Equal(0x466B0F30L, ai.ImageOffset);
            Assert.Equal(AddressMode.Area, ai.FsAddressMode);
            Assert.Equal(AreaType.FileSystem, ai.Type);

            ai = AreaInfo.NextArea(0x466B0F30L, 0x2E0260L, end - 0x2E0260L, end, -1, AreaType.None, areas, 0);
            Assert.Equal(0x46DB4880L, ai.ImageOffset);
            Assert.Equal(AddressMode.Area, ai.FsAddressMode);
            Assert.Equal(AreaType.None, ai.Type);
        }

        /// <summary>
        /// Tests the calculations performed on a buffer to determine if the buffer contains data at a specific offset on a disc related to the file system offsets (the buffer might contain many sectors wit pre and post system data - e.g. hashes, ECC)
        /// </summary>
        [Theory]
        //             Disc Off+Size        Block Sz, Off, FsSz      Test Off+Sz          Result Off,RngOff,sz
        [InlineData(1, 0x0000L, 0x2000, 0x1000, 0x400, 0x800, 0x0000L, 0x0800L, true, false, true, 0x000, 0x0000L, 0x0800)] //First 0x800 in 0x1000 of fs data
        [InlineData(2, 0x0000L, 0x2000, 0x1000, 0x400, 0x800, 0x0800L, 0x0800L, true, true, true, 0x800, 0x0000L, 0x0800)] //Second 0x800 un 0x1000 of fs data
        [InlineData(3, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x1000L, 0x0800L, true, false, true, 0x000, 0x0000L, 0x0800)] //First 0x800 in 0x1000 of fs data - Buffer at offset 0x2000
        [InlineData(4, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x1800L, 0x0800L, true, true, true, 0x800, 0x0000L, 0x0800)] //Second 0x800 un 0x1000 of fs data - Buffer at offset 0x2000
        [InlineData(5, 0x0000L, 0x2000, 0x1000, 0x400, 0x800, 0x0000L, 0x1000L, true, true, true, 0x000, 0x0000L, 0x1000)] //match the full buffer
        [InlineData(6, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x0000L, 0x2000L, true, true, true, 0x000, 0x1000L, 0x1000)] //match the full buffer - Buffer at offset 0x2000

        [InlineData(7, 0x0000L, 0x2000, 0x1000, 0x400, 0x800, 0x1000L, 0x1000L, false, false, false, 0x000, 0x0000L, 0x0000)] //test fs data starts at the end of the buffer
        [InlineData(8, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x0F00L, 0x0100L, false, false, false, 0x000, 0x0000L, 0x0000)] //test fs data ends at the where the buffer starts

        [InlineData(9, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x0F00L, 0x0600L, true, false, true, 0x000, 0x0100L, 0x0500)] //test fs data ends at the where the buffer starts

        [InlineData(10, 0x2000L, 0x2000, 0x1000, 0x400, 0x800, 0x0100L, 0x3000L, true, true, false, 0x000, 0x0F00L, 0x1000)] //test fs data spans buffer


        [InlineData(11, 0x416900L, 0x100570, 0x930, 0x18, 0x800, 0x46e800L, 0xd8L, false, false, false, 0x000, 0x000L, 0x0)] //data in the first sector of the image
        [InlineData(12, 0x200000L, 0x0, 0x800, 0x00, 0x800, 0x200000L, 0x0L, true, true, true, 0x0, 0x000L, 0x0)] //data in the first sector of the image
        //[InlineData(7, 0x38f560L, 0xdf3d0, 0x930, 0x18, 0x800, 0x46e800L, 0xd8L, 0x000, 0x000L, 0x0)] //data in the first sector of the image

        public void TestFsRange(int id, long imageOffset, int bufferSize, int blockSize, int blockFsOffset, int blockFsSize, long testFsOffset, long testFsSize, bool isMatch, bool isBufferFull, bool isRangeComplete, int resultOffset, long resultRangeOffset, int resultSize)
        {
            Buffer buffer = new Buffer(bufferSize, false);
            AreaInfo ai = new AreaInfo(0, AreaType.FileSystem, 0);
            ai.SetBlock(blockSize, blockFsOffset, blockFsSize, 0x200000);
            buffer.ReInitialise(ai, false);
            buffer.Update(imageOffset, imageOffset, bufferSize, 0, false, false);

            RangeResult rr = new RangeResult();
            buffer.TestFsRange(testFsOffset, testFsSize, rr);

            Assert.Equal(isMatch, rr.IsMatch);
            Assert.Equal(isBufferFull, rr.BufferFull);
            Assert.Equal(isRangeComplete, rr.RangeComplete);
            Assert.Equal(resultOffset, rr.BufferOffset);
            Assert.Equal(resultRangeOffset, rr.RangeOffset);
            Assert.Equal(resultSize, rr.Size);
        }

        /// <summary>
        /// Genrates 4 byte offsets in to a src buffer then checks the results. Only use data on the 4 byte boundary (testing limitation)
        /// </summary>
        [Theory]
        //          ID    COPY fsSrc, fsDst, size   SOURCE BLOCKS                DEST BLOCKS
        [InlineData(101, 0, 0, 0x7c00 * 64, 0x8000, 0x400, 0x7c00, 64, 0x7c00, 0, 0x7c00, 64)] //Copy all data from a Wii Section skipping the hashes
        [InlineData(102, 0x10, 0, 0x100, 0x8000, 0x400, 0x7c00, 64, 0x7c00, 0, 0x7c00, 64)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes
        [InlineData(103, 0x7b00, 0, 0x200, 0x8000, 0x400, 0x7c00, 64, 0x7c00, 0, 0x7c00, 64)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes
        [InlineData(104, 0, 0, 0x7c00 * 64, 0x8000, 0x400, 0x7c00, 64, 0x7c00 * 64, 0, 0x7c00 * 64, 1)] //Copy all data from a Wii Section skipping the hashes (dest is 1 big block)
        [InlineData(105, 0x10, 0, 0x100, 0x8000, 0x400, 0x7c00, 64, 0x7c00 * 64, 0, 0x7c00 * 64, 1)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 1 big block)
        [InlineData(106, 0x7b00, 0, 0x200, 0x8000, 0x400, 0x7c00, 64, 0x7c00 * 64, 0, 0x7c00 * 64, 1)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 1 big block)
        [InlineData(107, 0, 0, 0xF0 * 10, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0xF0, 10)] //Copy all data from a Wii Section skipping the hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(108, 0x10, 0, 0x100, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0xF0, 10)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(109, 0x7b00, 0, 0x200, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0xF0, 10)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(110, 0, 0, 0x90 * 10, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0x90, 10)] //Copy all data from a Wii Section skipping the hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)
        [InlineData(111, 0x10, 0, 0x100, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0x90, 10)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)
        [InlineData(112, 0x7b00, 0, 0x200, 0x8000, 0x400, 0x7c00, 64, 0x100, 0x10, 0x90, 10)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)

        [InlineData(131, 0, 0, 0x7ba0 * 64, 0x8000, 0x400, 0x7ba0, 64, 0x7c00, 0, 0x7ba0, 64)] //Copy all data from a Wii Section skipping the hashes
        [InlineData(132, 0x10, 0, 0x100, 0x8000, 0x400, 0x7ba0, 64, 0x7c00, 0, 0x7ba0, 64)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes
        [InlineData(133, 0x7aa0, 0, 0x200, 0x8000, 0x400, 0x7ba0, 64, 0x7c00, 0, 0x7ba0, 64)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes
        [InlineData(134, 0, 0, 0x7ba0 * 64, 0x8000, 0x400, 0x7ba0, 64, 0x7c00 * 64, 0, 0x7ba0 * 64, 1)] //Copy all data from a Wii Section skipping the hashes (dest is 1 big block)
        [InlineData(135, 0x10, 0, 0x100, 0x8000, 0x400, 0x7ba0, 64, 0x7c00 * 64, 0, 0x7ba0 * 64, 1)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 1 big block)
        [InlineData(136, 0x7aa0, 0, 0x200, 0x8000, 0x400, 0x7ba0, 64, 0x7c00 * 64, 0, 0x7ba0 * 64, 1)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 1 big block)
        [InlineData(137, 0, 0, 0xF0 * 10, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0xF0, 10)] //Copy all data from a Wii Section skipping the hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(138, 0x10, 0, 0x100, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0xF0, 10)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(139, 0x7aa0, 0, 0x200, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0xF0, 10)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 0x10 padding, 0xF0 data)
        [InlineData(140, 0, 0, 0x90 * 10, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0x90, 10)] //Copy all data from a Wii Section skipping the hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)
        [InlineData(141, 0x10, 0, 0x100, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0x90, 10)] //Copy 0x100 bytes from src fs offset 0x20 to dest offset 0 within hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)
        [InlineData(142, 0x7aa0, 0, 0x200, 0x8000, 0x400, 0x7ba0, 64, 0x100, 0x10, 0x90, 10)] //Copy 0x200 bytes from src fs offset 0x20 to dest offset 0 skipping hashes (dest is 0x10 padding, 0x90 data, 0x60 blank at end)

        public void FsDataCopy(int id, int srcFsOffset, int dstFsOffset, int copyFsSize, int srcBlockSize, int srcBlockFsOffset, int srcBlockFsSize, int srcBlocks, int dstBlockSize, int dstBlockFsOffset, int dstBlockFsSize, int dstBlocks)
        {
            //set up source data
            Buffer b = new Buffer(srcBlockSize * srcBlocks, false);
            AreaInfo ai = new AreaInfo(0, AreaType.FileSystem, 0);
            ai.SetBlock(srcBlockSize, srcBlockFsOffset, srcBlockFsSize, 0x200000);

            b.ReInitialise(ai, true);
            b.Update(0x70000, 0, srcBlockSize * srcBlocks, 0, false, false);
            //write the fs data offset in to every 4 bytes of fs data
            TestUtils.FillBlock(0L, b.Decrypted, srcBlockSize, srcBlockFsOffset, srcBlockFsSize, srcBlocks);

            byte[] fsData = new byte[dstBlockSize * dstBlocks];
            Buffer.Copy(b.Decrypted, srcFsOffset, srcBlockSize, srcBlockFsOffset, srcBlockFsSize, fsData, dstFsOffset, dstBlockSize, dstBlockFsOffset, dstBlockFsSize, copyFsSize);

            //File.WriteAllBytes(@"c:\temp\" + id.ToString() + "_xx", b.Decrypted);
            //File.WriteAllBytes(@"c:\temp\" + id.ToString() + "_yy", fsData);

            uint v = 0;
            for (int o = 0; o < dstBlocks && v < copyFsSize; o++)
            {
                int offset = o * dstBlockSize;
                Assert.True(fsData.Equals(offset, dstBlockFsOffset, 0), "Gap before Fs Data in a block was not 00s");
                offset += dstBlockFsOffset;

                for (int i = 0; i < dstBlockFsSize && v < copyFsSize; i += 4)
                {
                    Assert.Equal((uint)(srcFsOffset + v), fsData.ReadUInt32B(offset + i));
                    v += 4;
                }
                offset += dstBlockFsSize;
                Assert.True(fsData.Equals(offset, ((o + 1) * dstBlockSize) - offset, 0), "Gap after Fs Data in a block was not 00s");

            }
        }
    }
}