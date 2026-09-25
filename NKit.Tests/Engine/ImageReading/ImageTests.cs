using Nanook.NKit;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    [Trait("Speed", "Slow")] // requires real disc image files in NKitExternalTestFiles — excluded from the fast CI gate
    public class ImageTests
    {

        [Theory]

        [InlineData(@"v5-cdlz_cdzl_cdfl-99bottles.chd", "ChdAsIso", "Default", 1, false, false)]
        [InlineData(@"Wii_MarioAllStars_PAL.ciso", "CisoAsIso", "wii", 1, false, false)]
        [InlineData(@"Ps3_Demo_Trailer_Collection_Asia.cso", "CsoZsoAsIso", "ps3", 1, false, false)]
        [InlineData(@"Psp_007FromRussiaWithLove_UK.zso", "CsoZsoAsIso", "psp", 1, false, false)]
        [InlineData(@"Psp_007FromRussiaWithLove_UK.dax", "DaxAsIso", "psp", 1, false, false)]
        [InlineData(@"Dc_Namco_USA.zip", "DefaultAsIso", "dreamcast", 1, false, false)]
        [InlineData(@"Wii_MarioAllStars_PAL_NoUpdate.nkit.gcz", "NKitAsIso", "wii", 1, false, false)]
        [InlineData(@"Wii_DiscUpdateRvtR.iso.dec", "IsoDecAsIso", "wii", 1, false, false)]
        [InlineData(@"Psp_007FromRussiaWithLove_UK.jso", "JsoAsIso", "psp", 1, false, false)]
        [InlineData(@"Wii_RvlDiagVer4_4RvtH.rvz", "RvzAsIso", "wii", 1, false, false)]
        [InlineData(@"WiiU_Cdn_FZeroX_USA.zip", "TmdAppAsIso", "wiiu", 1, false, false)]
        [InlineData(@"Wii_MarioAllStars_PAL_Split.wbfs", "WbfsAsIso", "wii", 1, false, false)]
        [InlineData(@"Wii_MarioAllStars_PAL.wia", "WiaAsIso", "wii", 1, false, false)]
        [InlineData(@"WiiU_CAT-I.wux", "WuxAsIso", "wiiu", 1, false, false)]
        void DetectTest(string path, string container, string systemType, int tracks, bool isGdRom, bool isFolderIndex)
        {
            path = Path.GetFullPath(Path.Combine(
                "..", "..", "..", "..", "..", "NKitExternalTestFiles", path));
            SourceFile sf = SourceFiles.Scan(new string[] { path }, true, true, true, null, null).FirstOrDefault();

            IImageContext context = new imageContext() { SourceFile = sf };

            using (Stream s = sf.OpenFileStream())
            {
                IAsIso iso = NKitInput.DetectImage(s, context);
                Assert.Equal(container, iso.GetType().Name);

                iso.Construct(s, true);
            }
        }

        private class imageContext : IImageContext
        {
            public IDataProvider Settings { get; set; }

            public IImageHeader Header { get; set; }
            public IImageInfo ImageInfo { get; set; }

            public ILogScope Log { get; set; }

            public Scan Scan { get; set; }

            public SourceFile SourceFile { get; set; }

            public SkipType SkipType { get; set; }

            public SystemType SystemType { get; set; }

            public TaskType TaskType { get; set; }

            public IStepInfo StepInfo { get; set; }

            public void SetSystemType(SystemType systemType) => this.SystemType = systemType;

            public long SkipToImageOffsetGet(long imageOffset) => 0;
        }
    }
}