using Nanook.NKit;
using Nanook.NKit.Container;
using NKitDataStore;
using Xunit;


namespace NKit.Tests.NKDS
{
    [Trait("Area", "NKDS")]
    public class DataStoreAsIsoTests
    {
        [Theory]
        [InlineData(ImageFormat.Bin, ".bin")]
        [InlineData(ImageFormat.App, ".app")]
        [InlineData(ImageFormat.Gdi, ".gdi")]
        [InlineData(ImageFormat.Cdn, ".iso")]
        [InlineData(ImageFormat.Iso, ".iso")]
        [InlineData(ImageFormat.Unknown, ".iso")]
        [InlineData(ImageFormat.CueFolder, "")]
        [InlineData(ImageFormat.TmdAppFolder, "")]
        public void GetImageExtension_ReturnsExpectedExtension(ImageFormat format, string expectedExtension)
        {
            string extension = DataStoreAsIso.GetImageExtension(format);
            Assert.Equal(expectedExtension, extension);
        }

        [Theory]
        [InlineData("TestGame", ImageFormat.Bin, "TestGame.bin")]
        [InlineData("AppImage", ImageFormat.App, "AppImage.app")]
        [InlineData("MyGdi", ImageFormat.Gdi, "MyGdi.gdi")]
        [InlineData("CdnFolder", ImageFormat.Cdn, "CdnFolder.iso")]
        [InlineData("IsoImage", ImageFormat.Iso, "IsoImage.iso")]
        [InlineData("MultiDisc", ImageFormat.CueFolder, "MultiDisc")]
        [InlineData("WiiUApp", ImageFormat.TmdAppFolder, "WiiUApp")]
        public void GetImageFileName_ReturnsExpectedNameWithExtension(string imageName, ImageFormat format, string expectedFileName)
        {
            string fileName = DataStoreAsIso.GetImageFileName(imageName, format);
            Assert.Equal(expectedFileName, fileName);
        }

        [Fact]
        public void DataStoreAsIso_Create_WithNullContext_ReturnsNull()
        {
            IAsIso result = DataStoreAsIso.Create(new byte[] { }, null);
            Assert.Null(result);
        }
    }
}