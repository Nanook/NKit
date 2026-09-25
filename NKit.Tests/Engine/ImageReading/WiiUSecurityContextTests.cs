using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiU;
using NKitDataStore;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class WiiUSecurityContextTests
    {
        [Fact]
        public void GetActiveEncryptionKey_GamePartition_UsesTitleKey()
        {
            byte[] headerKey = new byte[] { 1, 2, 3 };
            byte[] titleKey = new byte[] { 9, 8, 7 };

            byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(PartitionType.Game, false, headerKey, titleKey);

            Assert.Equal(titleKey, key);
        }

        [Fact]
        public void GetActiveEncryptionKey_GamePartition_FallsBackToHeaderKey()
        {
            byte[] headerKey = new byte[] { 1, 2, 3 };

            byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(PartitionType.Game, false, headerKey, null);

            Assert.Equal(headerKey, key);
        }

        [Fact]
        public void GetActiveEncryptionKey_NonGamePartition_UsesHeaderKey()
        {
            byte[] headerKey = new byte[] { 1, 2, 3 };
            byte[] titleKey = new byte[] { 9, 8, 7 };

            byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(PartitionType.Update, false, headerKey, titleKey);

            Assert.Equal(headerKey, key);
        }

        [Fact]
        public void GetActiveEncryptionKey_GamePartition_PartitionHeader_UsesHeaderKey()
        {
            byte[] headerKey = new byte[] { 1, 2, 3 };
            byte[] titleKey = new byte[] { 9, 8, 7 };

            byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(PartitionType.Game, AreaType.PartitionHeader, false, headerKey, titleKey);

            Assert.Equal(headerKey, key);
        }

        [Fact]
        public void GetActiveEncryptionKey_FlatContainer_UsesTitleKey()
        {
            byte[] headerKey = new byte[] { 1, 2, 3 };
            byte[] titleKey = new byte[] { 9, 8, 7 };

            byte[] key = WiiUSecurityContext.GetActiveEncryptionKey(PartitionType.Update, true, headerKey, titleKey);

            Assert.Equal(titleKey, key);
        }

        [Fact]
        public void CreateSyntheticForApp_WithHashes_SetsPropertiesCorrectly()
        {
            ImageHeader header = new ImageHeader(null);

            long size = 0x100000L;
            int contentIndex = 5;

            ContentHeader cnt = WiiUSecurityContext.CreateSyntheticForApp(contentIndex, size, true, header);

            Assert.True(cnt.HasHashes);
            Assert.True(cnt.HasEncryption);
            Assert.Equal(contentIndex, cnt.Index);
            Assert.Equal(size, cnt.Size);
            Assert.Equal(header.BlockSizeHashed, cnt.BlockSize);
            Assert.Equal(header.HashesSize, cnt.BlockFsOffset);
            Assert.Equal(header.BlockFsSizeHashed, cnt.BlockFsSize);
        }

        [Fact]
        public void CreateSyntheticForApp_WithoutHashes_SetsPropertiesCorrectly()
        {
            ImageHeader header = new ImageHeader(null);

            long size = 0x100000L;
            int contentIndex = 2;

            ContentHeader cnt = WiiUSecurityContext.CreateSyntheticForApp(contentIndex, size, false, header);

            Assert.False(cnt.HasHashes);
            Assert.True(cnt.HasEncryption);
            Assert.Equal(contentIndex, cnt.Index);
            Assert.Equal(size, cnt.Size);
            Assert.Equal(header.BlockSize, cnt.BlockSize);
            Assert.Equal(0, cnt.BlockFsOffset);
            Assert.Equal(header.BlockSize, cnt.BlockFsSize);
        }

        [Fact]
        public void BuildSiData_NullInputs_ReturnsNull()
        {
            byte[] key = new byte[16];
            SiData si = WiiUSecurityContext.BuildSiData(0, null, null, null, ImageFormat.Iso, new ImageHeader(null), ref key);
            Assert.Null(si);
        }
    }
}