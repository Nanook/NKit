using Nanook.NKit;
using Nanook.NKit.Nintendo.WiiGc;
using Xunit;

namespace NKit.Tests.Settings
{
    /// <summary>
    /// Unit tests for CalculateConfig behavior in the Expand task type.
    /// Validates that CalculateConfig produces the correct config value for various
    /// system types and format strings when processing DataStore Expand tasks.
    ///
    /// _Requirements: 8.6, 10.8_
    /// </summary>
    [Trait("Area", "Settings")]
    public class CalculateConfigUnitTests
    {
        /// <summary>
        /// Helper to create an NKitTaskContext configured for Expand with the given system
        /// and source type, then call CalculateConfig.
        /// </summary>
        private static string CallCalculateConfig(SystemType system, string configString, bool isFolderIndex, bool isGdRom)
        {
            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = TaskType.Expand,
                System = system,
                V = Verify.N,
                Convert = "",
                Extract = "",
                Out = ""
            };

            AppSettings settings = new AppSettings(presets);
            SourceFile file = TaskStepsShared.CreateSourceFile(
                isFolderIndex ? ".cue" : ".iso", isFolderIndex);
            NKitTaskContext task = new NKitTaskContext(settings, file, null);
            task.Steps[0].ImageInfo = new ImageInfo() { IsFolderIndex = file.IndexFile != null };
            task.Initialise(presets.System);

            return task.CalculateConfig(configString, isGdRom);
        }

        #region Default system tests

        [Fact]
        public void Expand_Default_ConfigStringCue_FolderIndex_ReturnsCue()
        {
            // For Default system with folderindex source and configString="cue",
            // CalculateConfig should return "cue" (passes through CUE-related values for folderindex)
            string config = CallCalculateConfig(SystemType.Default, "cue", isFolderIndex: true, isGdRom: false);
            Assert.Equal("cue", config);
        }

        [Fact]
        public void Expand_Default_ConfigStringCue_ImageSource_ReturnsCue()
        {
            // For Default system with image source and configString="cue",
            // CalculateConfig should return "cue" (passes through format string for image sources)
            string config = CallCalculateConfig(SystemType.Default, "cue", isFolderIndex: false, isGdRom: false);
            Assert.Equal("cue", config);
        }

        [Fact]
        public void Expand_Default_EmptyConfigString_FolderIndex_ReturnsEmpty()
        {
            // For Default system with folderindex source and configString="",
            // CalculateConfig should return "" (empty matches "cue/" entries via prefix)
            string config = CallCalculateConfig(SystemType.Default, "", isFolderIndex: true, isGdRom: false);
            Assert.Equal("", config);
        }

        [Fact]
        public void Expand_Default_EmptyConfigString_ImageSource_ReturnsEmpty()
        {
            // For Default system with image source and configString="",
            // CalculateConfig should return "" (empty matches "iso/" entries via prefix)
            string config = CallCalculateConfig(SystemType.Default, "", isFolderIndex: false, isGdRom: false);
            Assert.Equal("", config);
        }

        [Fact]
        public void Expand_Default_ConfigStringIso_ImageSource_ReturnsIso()
        {
            // For Default system with image source and configString="iso",
            // CalculateConfig should return "iso"
            string config = CallCalculateConfig(SystemType.Default, "iso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("iso", config);
        }

        #endregion

        #region Xbox/Xbox360 tests

        [Fact]
        public void Expand_Xbox_ConfigStringXiso_ReturnsXiso()
        {
            // For Xbox system with configString="xiso",
            // CalculateConfig should return "xiso"
            string config = CallCalculateConfig(SystemType.XBox, "xiso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("xiso", config);
        }

        [Fact]
        public void Expand_Xbox_ConfigStringIso_ReturnsIso()
        {
            // For Xbox system with configString="iso",
            // CalculateConfig should return "iso"
            string config = CallCalculateConfig(SystemType.XBox, "iso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("iso", config);
        }

        [Fact]
        public void Expand_Xbox_EmptyConfigString_ReturnsEmpty()
        {
            // For Xbox system with configString="",
            // CalculateConfig should return ""
            string config = CallCalculateConfig(SystemType.XBox, "", isFolderIndex: false, isGdRom: false);
            Assert.Equal("", config);
        }

        [Fact]
        public void Expand_Xbox360_ConfigStringXiso_ReturnsXiso()
        {
            // For Xbox360 system with configString="xiso",
            // CalculateConfig should return "xiso"
            string config = CallCalculateConfig(SystemType.XBox360, "xiso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("xiso", config);
        }

        [Fact]
        public void Expand_Xbox360_ConfigStringIso_ReturnsIso()
        {
            // For Xbox360 system with configString="iso",
            // CalculateConfig should return "iso"
            string config = CallCalculateConfig(SystemType.XBox360, "iso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("iso", config);
        }

        [Fact]
        public void Expand_Xbox360_EmptyConfigString_ReturnsEmpty()
        {
            // For Xbox360 system with configString="",
            // CalculateConfig should return ""
            string config = CallCalculateConfig(SystemType.XBox360, "", isFolderIndex: false, isGdRom: false);
            Assert.Equal("", config);
        }

        #endregion

        #region Dreamcast GdRom logic unchanged

        [Fact]
        public void Expand_Dreamcast_GdRom_ConfigStringCue_ReturnsGdRomcue()
        {
            // For Dreamcast system with isGdRom=true and configString="cue",
            // CalculateConfig should return "GdRomcue" (existing GdRom logic)
            string config = CallCalculateConfig(SystemType.Dreamcast, "cue", isFolderIndex: true, isGdRom: true);
            Assert.Equal("GdRomcue", config);
        }

        [Fact]
        public void Expand_Dreamcast_GdRom_ConfigStringGdi_ReturnsGdRomgdi()
        {
            // For Dreamcast system with isGdRom=true and configString="gdi",
            // CalculateConfig should return "GdRomgdi" (existing GdRom logic)
            string config = CallCalculateConfig(SystemType.Dreamcast, "gdi", isFolderIndex: true, isGdRom: true);
            Assert.Equal("GdRomgdi", config);
        }

        [Fact]
        public void Expand_Dreamcast_NonGdRom_ConfigStringCue_ReturnsEmpty()
        {
            // For Dreamcast system with isGdRom=false (non-GD-ROM CUE),
            // the Dreamcast case only handles isGdRom=true, so config remains ""
            string config = CallCalculateConfig(SystemType.Dreamcast, "cue", isFolderIndex: true, isGdRom: false);
            Assert.Equal("", config);
        }

        [Fact]
        public void Expand_Dreamcast_NonGdRom_EmptyConfigString_ReturnsEmpty()
        {
            // For Dreamcast system with isGdRom=false and configString="",
            // CalculateConfig should return "" (non-GdRom Dreamcast doesn't set config)
            string config = CallCalculateConfig(SystemType.Dreamcast, "", isFolderIndex: true, isGdRom: false);
            Assert.Equal("", config);
        }

        #endregion

        #region WiiU logic unchanged

        [Fact]
        public void Expand_WiiU_ConfigStringApp_ReturnsApptmd()
        {
            // For WiiU system with configString="app",
            // CalculateConfig should return "apptmd" (existing WiiU logic)
            string config = CallCalculateConfig(SystemType.WiiU, "app", isFolderIndex: true, isGdRom: false);
            Assert.Equal("apptmd", config);
        }

        [Fact]
        public void Expand_WiiU_ConfigStringTmd_ReturnsApptmd()
        {
            // For WiiU system with configString="tmd",
            // CalculateConfig should return "apptmd" (existing WiiU logic)
            string config = CallCalculateConfig(SystemType.WiiU, "tmd", isFolderIndex: true, isGdRom: false);
            Assert.Equal("apptmd", config);
        }

        [Fact]
        public void Expand_WiiU_ConfigStringIso_ReturnsIso()
        {
            // For WiiU system with configString="iso",
            // CalculateConfig should return "iso" (WiiU non-app/tmd passes through)
            string config = CallCalculateConfig(SystemType.WiiU, "iso", isFolderIndex: false, isGdRom: false);
            Assert.Equal("iso", config);
        }

        #endregion
    }
}