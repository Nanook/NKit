using Nanook.NKit;
using Nanook.NKit.Iso.Iso9660;
using Xunit;

namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for the pure SourceProfile normalisation extracted from NKitTaskContext.
    /// These lock the former "look-up-then-patch" config hacks (#1,#2,#6-#10) independently of the
    /// exhaustive Step* golden-master matrix.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Routing")]
    public class SourceProfileTests
    {
        private static SourceFile img(string ext = ".iso") => TaskStepsShared.CreateSourceFile(ext, false, false);
        private static SourceFile idx() => TaskStepsShared.CreateSourceFile(".cue", true, false);
        private static SourceFile ds(bool folderIndex) => TaskStepsShared.CreateSourceFile(folderIndex ? ".cue" : ".iso", folderIndex, true);

        private static IImageInfo chdFolderIndex() => new ImageInfo() { IsFolderIndex = true };
        private static IImageInfo plain() => new ImageInfo() { IsFolderIndex = false };

        // ---- CalculateConfig (pure #6-#10) ----

        [Theory]
        // #6 Wii/GC lossless nkit wrapping vs iso/lossy passthrough
        [InlineData(TaskType.Convert, SystemType.Wii, "rvz", "rvz[nkit]")]
        [InlineData(TaskType.Convert, SystemType.GameCube, "wbfs", "wbfs[nkit]")]
        [InlineData(TaskType.Convert, SystemType.Wii, "iso", "iso")]
        [InlineData(TaskType.Convert, SystemType.Wii, "wbfs:n", "wbfs")] // lossy → passthrough
        [InlineData(TaskType.Convert, SystemType.WiiU, "app", "apptmd")]
        [InlineData(TaskType.Convert, SystemType.PS2, "cso", "cso")]
        public void CalculateConfig_Convert(TaskType task, SystemType system, string configString, string expected) => Assert.Equal(expected, SourceProfile.CalculateConfig(task, system, configString, false, false, img()));

        [Fact] // #6 Wii lossy ciso:n stays ciso (not ciso[nkit])
        public void CalculateConfig_Convert_WiiCisoLossy() => Assert.Equal("ciso", SourceProfile.CalculateConfig(TaskType.Convert, SystemType.Wii, "ciso:n", false, false, img()));

        [Fact] // #8 Dreamcast GD-ROM CHD scan → ChdGdRomcue (real CHD source: ImageType==Chd)
        public void CalculateConfig_Scan_DreamcastChdGd() => Assert.Equal("ChdGdRomcue", SourceProfile.CalculateConfig(TaskType.Scan, SystemType.Dreamcast, "scan", true, true, chdSource()));

        // A real CD/GD CHD source has ImageType == Chd and NO external IndexFile (its layout lives in
        // ChdMetaData). Build one directly: a single .chd ImageFiles entry + Initialised().
        private static SourceFile chdSource()
        {
            SourceFile f = new SourceFile();
            f.ImageFiles = new[] { new SourceFileItem("", "Game.chd", ".chd", ".chd", 0, 0, 0, false, false) };
            f.Initialised();
            return f;
        }

        [Fact] // #8 Verify variant
        public void CalculateConfig_Verify_DreamcastChdGd() => Assert.Equal("ChdGdRomnone", SourceProfile.CalculateConfig(TaskType.Verify, SystemType.Dreamcast, "none", true, true, chdSource()));

        [Fact] // Verify non-CHD dreamcast GD → plain none
        public void CalculateConfig_Verify_DreamcastNonChd() => Assert.Equal("none", SourceProfile.CalculateConfig(TaskType.Verify, SystemType.Dreamcast, "none", true, true, idx()));

        [Fact] // #9 Fix PS3 .sfb → sfb
        public void CalculateConfig_Fix_Ps3Sfb()
        {
            SourceFile sfb = TaskStepsShared.CreateSourceFile(".sfb", false, false);
            Assert.Equal("sfb", SourceProfile.CalculateConfig(TaskType.Fix, SystemType.PS3, "", false, false, sfb));
        }

        [Fact] // #9 Fix PS3 non-sfb → empty
        public void CalculateConfig_Fix_Ps3Iso() => Assert.Equal("", SourceProfile.CalculateConfig(TaskType.Fix, SystemType.PS3, "iso", false, false, img()));

        [Theory] // #10 Expand default: folderindex only passes cue/toc, else ""
        [InlineData("cue", true, "cue")]
        [InlineData("iso", true, "")]
        [InlineData("iso", false, "iso")]
        public void CalculateConfig_Expand_Default(string configString, bool folderIndex, string expected)
        {
            SourceFile src = folderIndex ? idx() : img();
            Assert.Equal(expected, SourceProfile.CalculateConfig(TaskType.Expand, SystemType.PS2, configString, false, folderIndex, src));
        }

        // #7 (Expand TmdApp → apptmd) requires a genuine TmdApp IndexFile which the harness cannot
        // fabricate cheaply; it is exercised end-to-end by the golden-master Expand WiiU AppTmd rows.

        [Fact] // Scan default → scan
        public void CalculateConfig_Scan_Default() => Assert.Equal("scan", SourceProfile.CalculateConfig(TaskType.Scan, SystemType.PS2, "scan", false, false, img()));

        // ---- From (#1 config-string selection, #2 dual-format split, SrcType) ----

        private static SystemSettings settingsFor(SystemType system, string convert = "", string extract = "")
        {
            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = TaskType.Convert,
                System = system,
                V = Verify.N,
                Convert = convert,
                Extract = extract,
                Out = ""
            };
            return new AppSettings(presets)[system];
        }

        [Fact] // SrcType from folder-index fact (IndexFile)
        public void From_SrcType_FolderIndex_FromIndexFile()
        {
            SourceProfile p = SourceProfile.From(TaskType.Scan, SystemType.PS2, idx(), plain(), settingsFor(SystemType.PS2));
            Assert.Equal("folderindex", p.SrcType);
            Assert.True(p.IsFolderIndex);
        }

        [Fact] // SrcType folder-index from ImageInfo.IsFolderIndex (CHD-CD, IndexFile == null)
        public void From_SrcType_FolderIndex_FromImageInfo()
        {
            SourceProfile p = SourceProfile.From(TaskType.Scan, SystemType.PS2, img(".chd"), chdFolderIndex(), settingsFor(SystemType.PS2));
            Assert.Equal("folderindex", p.SrcType);
        }

        [Fact] // SrcType image for a plain DVD source
        public void From_SrcType_Image()
        {
            SourceProfile p = SourceProfile.From(TaskType.Scan, SystemType.PS3, img(), plain(), settingsFor(SystemType.PS3));
            Assert.Equal("image", p.SrcType);
        }

        [Fact] // #2 dual-format split picks folder-index half for a folder-index source
        public void From_DualFormat_PicksFolderIndexHalf()
        {
            SourceProfile p = SourceProfile.From(TaskType.Convert, SystemType.PS2, idx(), plain(), settingsFor(SystemType.PS2, "iso/cue"));
            Assert.Equal("cue", p.TaskConfig);
            Assert.Equal("cue", p.Config);
        }

        [Fact] // #2 dual-format split picks image half for an image source
        public void From_DualFormat_PicksImageHalf()
        {
            SourceProfile p = SourceProfile.From(TaskType.Convert, SystemType.PS2, img(), plain(), settingsFor(SystemType.PS2, "iso/cue"));
            Assert.Equal("iso", p.TaskConfig);
            Assert.Equal("iso", p.Config);
        }

        [Fact] // #1 DataStore Expand uses Settings.Convert
        public void From_DataStore_IsFlagged()
        {
            SourceProfile p = SourceProfile.From(TaskType.Expand, SystemType.PS2, ds(false), plain(), settingsFor(SystemType.PS2, "iso"));
            Assert.True(p.IsDataStore);
        }

        // ---- DualFormat (#2, typed) ----

        [Theory]
        [InlineData("iso/cue", false, "iso")]   // image shape -> left half
        [InlineData("iso/cue", true, "cue")]    // folder-index shape -> right half
        [InlineData("iso", false, "iso")]       // single format -> same for both
        [InlineData("iso", true, "iso")]
        [InlineData("", false, "")]
        [InlineData("iso/", true, "")]          // trailing slash: folder-index half is empty (historical)
        [InlineData("a/b/c", false, "a")]       // >1 slash: image = part[0]
        [InlineData("a/b/c", true, "b")]        // >1 slash: folder-index = part[1] (matches old Split[1])
        public void DualFormat_For_TargetsCorrectHalf(string configString, bool isFolderIndex, string expected) => Assert.Equal(expected, DualFormat.Parse(configString).For(isFolderIndex));

        // ---- resolveVerifyMethod (#3, derived not substituted) ----

        [Theory]
        [InlineData(VerifyMethod.InChecksums, true, VerifyMethod.DataStore)]   // DataStore source maps InChecksums -> DataStore
        [InlineData(VerifyMethod.InChecksums, false, VerifyMethod.InChecksums)] // non-DataStore leaves it
        [InlineData(VerifyMethod.DatLookup, true, VerifyMethod.DatLookup)]      // other methods pass through even for DataStore
        [InlineData(VerifyMethod.DatMatch, true, VerifyMethod.DatMatch)]
        [InlineData(VerifyMethod.ScanCompare, true, VerifyMethod.ScanCompare)]
        [InlineData(VerifyMethod.NoVerify, true, VerifyMethod.NoVerify)]
        [InlineData(VerifyMethod.DataStore, true, VerifyMethod.DataStore)]
        public void ResolveVerifyMethod_MapsOnlyInChecksumsForDataStore(VerifyMethod table, bool isDataStore, VerifyMethod expected) => Assert.Equal(expected, NKitTaskContext.resolveVerifyMethod(table, isDataStore));
    }
}
