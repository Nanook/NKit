using Nanook.NKit;
using Nanook.NKit.Container;
using Nanook.NKit.Nintendo.WiiGc;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Validates WiiBlockLayout against the proven Buffer.OffsetToFsOffset / FsOffsetToOffset
    /// math, and checks hash-gap insertion for copy/fill operations.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class WiiBlockLayoutTests
    {
        private static readonly WiiBlockLayout Wii = WiiBlockLayout.Wii;

        [Theory]
        [InlineData(0)]
        [InlineData(0x100)]
        [InlineData(0x7c00)]     // exactly one sector's data
        [InlineData(0x7c01)]     // into second sector
        [InlineData(0xf800)]     // two sectors' data
        [InlineData(0x1F0000)]   // one full group of data
        [InlineData(0x123456)]
        public void FsToBlock_MatchesBuffer(long fsOffset)
        {
            long expected = Buffer.FsOffsetToOffset(fsOffset, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize, false);
            long actual = Wii.FsToBlock(fsOffset);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0x400)]      // start of first data region
        [InlineData(0x8000)]     // start of second sector
        [InlineData(0x8400)]     // second sector's data
        [InlineData(0x200000)]   // one full group (block space)
        public void BlockToFs_MatchesBuffer(long blockOffset)
        {
            long expected = Buffer.OffsetToFsOffset(blockOffset, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);
            long actual = Wii.BlockToFs(blockOffset);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Flat_IsIdentity()
        {
            WiiBlockLayout flat = WiiBlockLayout.Flat;
            Assert.True(flat.IsFlat);
            Assert.Equal(0x1234L, flat.FsToBlock(0x1234));
            Assert.Equal(0x1234L, flat.BlockToFs(0x1234));
        }

        [Fact]
        public void CopyFsToBlock_InsertsHashGaps()
        {
            // Fill two sectors of data (0xf800 FS bytes) into a block buffer.
            int fsLen = WiiConsts.WiiSectorFsSize * 2; // 0xf800
            byte[] src = new byte[fsLen];
            for (int i = 0; i < fsLen; i++)
                src[i] = (byte)(i & 0xff);

            byte[] dst = new byte[WiiConsts.WiiSectorSize * 2]; // 0x10000
            Wii.CopyFsToBlock(src, 0, dst, 0, fsLen);

            // Sector 0: hash area [0..0x400) should be zero, data [0x400..0x8000) should match src[0..0x7c00)
            for (int i = 0; i < WiiConsts.WiiSectorHashSize; i++)
                Assert.Equal(0, dst[i]);
            for (int i = 0; i < WiiConsts.WiiSectorFsSize; i++)
                Assert.Equal(src[i], dst[WiiConsts.WiiSectorHashSize + i]);

            // Sector 1: hash area [0x8000..0x8400) zero, data [0x8400..0x10000) matches src[0x7c00..0xf800)
            for (int i = 0; i < WiiConsts.WiiSectorHashSize; i++)
                Assert.Equal(0, dst[WiiConsts.WiiSectorSize + i]);
            for (int i = 0; i < WiiConsts.WiiSectorFsSize; i++)
                Assert.Equal(src[WiiConsts.WiiSectorFsSize + i], dst[WiiConsts.WiiSectorSize + WiiConsts.WiiSectorHashSize + i]);
        }

        [Fact]
        public void FillFsInBlock_InsertsHashGaps()
        {
            int fsLen = WiiConsts.WiiSectorFsSize + 0x100; // spans into second sector
            byte[] dst = new byte[WiiConsts.WiiSectorSize * 2];
            Wii.FillFsInBlock(dst, 0, fsLen, 0x55);

            // First sector data region all 0x55
            for (int i = 0; i < WiiConsts.WiiSectorFsSize; i++)
                Assert.Equal(0x55, dst[WiiConsts.WiiSectorHashSize + i]);

            // Second sector: first 0x100 data bytes are 0x55, hash area untouched (0)
            for (int i = 0; i < WiiConsts.WiiSectorHashSize; i++)
                Assert.Equal(0, dst[WiiConsts.WiiSectorSize + i]);
            for (int i = 0; i < 0x100; i++)
                Assert.Equal(0x55, dst[WiiConsts.WiiSectorSize + WiiConsts.WiiSectorHashSize + i]);
        }

        [Fact]
        public void FsLengthToBlockLength_AccountsForHashes()
        {
            // One sector of FS data expands to one full sector in block space
            Assert.Equal(WiiConsts.WiiSectorSize, Wii.FsLengthToBlockLength(0, WiiConsts.WiiSectorFsSize));
            // Half a sector of FS data stays within one sector's data region (no extra hash gap yet)
            long half = WiiConsts.WiiSectorFsSize / 2;
            Assert.Equal(half, Wii.FsLengthToBlockLength(0, half));
        }
    }
}