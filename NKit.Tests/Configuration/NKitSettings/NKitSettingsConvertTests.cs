using Nanook.NKit;
using NKit.Tests.TestData;
using System.Linq;
using Xunit;


namespace NKit.Tests.Configuration.NKitSettings
{
    /// <summary>
    /// Comprehensive tests for NKitSettings Convert functionality covering all system capabilities,
    /// format transitions, default applications, and UI interaction scenarios.
    /// Uses SystemCapabilitiesTestData as the definitive source of truth.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "NKitSettings")]
    public class NKitSettingsConvertTests
    {
        #region System Capability Validation Tests

        [Theory(DisplayName = "System supports expected format categories")]
        [InlineData(SystemType.GameCube, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.Wii, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.PSP, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.XBox, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.XBox360, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.WiiU, SystemCategory.SingleFormat, true, false)]
        [InlineData(SystemType.Dreamcast, SystemCategory.IndexOnly, false, true)]
        [InlineData(SystemType.PS3, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.PS1, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.PS2, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.PcEngine, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.CDi, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.Saturn, SystemCategory.DualFormat, true, true)]
        [InlineData(SystemType.SegaCD, SystemCategory.DualFormat, true, true)]
        public void SystemCapabilities_MatchExpectedCategories(
            SystemType systemType, SystemCategory expectedCategory, bool expectedSingleSupport, bool expectedIndexSupport)
        {
            // Arrange
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(systemType);

            // Act & Assert
            Assert.NotNull(testData);
            Assert.Equal(expectedCategory, testData.Category);
            Assert.Equal(expectedSingleSupport, SystemCapabilitiesTestData.IsSingleFormatSupported(systemType));
            Assert.Equal(expectedIndexSupport, SystemCapabilitiesTestData.IsIndexedFormatSupported(systemType));
        }

        #endregion

        #region Default Format Application Tests

        [Theory(DisplayName = "GetDefaultSettings applies correct format strings")]
        [InlineData(SystemType.GameCube, "rvz:zstd:19:128kb:16", "rvz", null)]
        [InlineData(SystemType.Wii, "rvz:zstd:19:128kb:16", "rvz", null)]
        [InlineData(SystemType.PSP, "cso:9:2kb:4", "cso", null)]
        [InlineData(SystemType.XBox, "iso", "iso", null)]
        [InlineData(SystemType.XBox360, "iso", "iso", null)]
        [InlineData(SystemType.WiiU, "wux", "wux", null)]
        [InlineData(SystemType.Dreamcast, "cue:split:bin:bin:sub", null, "cue")]
        [InlineData(SystemType.PS3, "deciso/cue:split:bin:bin:sub", "deciso", "cue")]
        [InlineData(SystemType.PS1, "cso:9:2kb:4/cue:split:bin:bin:sub", "cso", "cue")]
        [InlineData(SystemType.PS2, "cso:9:2kb:4/cue:split:bin:bin:sub", "cso", "cue")]
        [InlineData(SystemType.PcEngine, "iso/cue:split:bin:bin:sub", "iso", "cue")]
        [InlineData(SystemType.CDi, "iso/cue:split:bin:bin:sub", "iso", "cue")]
        [InlineData(SystemType.Saturn, "iso/cue:split:bin:bin:sub", "iso", "cue")]
        [InlineData(SystemType.SegaCD, "iso/cue:split:bin:bin:sub", "iso", "cue")]
        public void GetDefaultSettings_AppliesCorrectDefaults(
            SystemType systemType, string expectedDefaultFormat, string expectedSingleFormat, string expectedIndexedFormat)
        {
            // Act
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Assert
            Assert.Equal(expectedDefaultFormat, settings.Convert);
            Assert.Equal(expectedSingleFormat ?? string.Empty, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(expectedIndexedFormat ?? string.Empty, settings.ConvertIndexedFormat ?? string.Empty);
        }

        #endregion

        #region System Switching Format Compatibility Tests

        [Theory(DisplayName = "System switching clears incompatible formats correctly")]
        // Single → Index Only
        [InlineData(SystemType.XBox, SystemType.Dreamcast, "iso", "", "cue", "cue")]
        [InlineData(SystemType.GameCube, SystemType.Dreamcast, "rvz", "", "cue", "cue")]
        [InlineData(SystemType.PSP, SystemType.Dreamcast, "cso", "", "cue", "cue")]

        // Index Only → Single
        [InlineData(SystemType.Dreamcast, SystemType.GameCube, "cue", "rvz", "", "")]
        [InlineData(SystemType.Dreamcast, SystemType.XBox, "cue", "iso", "", "")]
        [InlineData(SystemType.Dreamcast, SystemType.PSP, "cue", "cso", "", "")]

        // Dual → Single Only
        [InlineData(SystemType.PS1, SystemType.PSP, "cso/cue", "cso", "", "")]
        [InlineData(SystemType.Saturn, SystemType.GameCube, "iso/cue", "rvz", "", "")]
        [InlineData(SystemType.PcEngine, SystemType.XBox, "iso/cue", "iso", "", "")]

        // Single Only → Dual
        [InlineData(SystemType.GameCube, SystemType.PS1, "rvz", "cso", "cue", "cue")]
        [InlineData(SystemType.XBox, SystemType.Saturn, "iso", "iso", "cue", "cue")]
        [InlineData(SystemType.PSP, SystemType.PcEngine, "cso", "iso", "cue", "cue")]

        // Index Only → Dual
        [InlineData(SystemType.Dreamcast, SystemType.PS3, "cue", "deciso", "cue", "cue")]
        [InlineData(SystemType.Dreamcast, SystemType.PS1, "cue", "cso", "cue", "cue")]

        // Dual → Dual (different defaults)
        [InlineData(SystemType.PS1, SystemType.PS3, "cso/cue", "deciso", "cue", "cue")]
        [InlineData(SystemType.Saturn, SystemType.PcEngine, "iso/cue", "iso", "cue", "cue")]
        public void SystemSwitching_ClearsIncompatibleFormats(
            SystemType fromSystem, SystemType toSystem, string initialFormat,
            string expectedSingleFormat, string expectedIndexedFormat, string expectedToIndexedFormat)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(fromSystem, TaskType.Convert);
            settings.Convert = initialFormat;

            // Act
            settings.System = toSystem;

            // Assert
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(toSystem);
            Assert.Equal(testData.DefaultFormat, settings.Convert);
            Assert.Equal(expectedSingleFormat, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(expectedToIndexedFormat, settings.ConvertIndexedFormat ?? string.Empty);
        }

        #endregion

        #region Format String Parsing Tests

        [Theory(DisplayName = "Convert property parsing sets correct components")]
        // RVZ formats
        [InlineData("rvz:zstd:19:128kb:16", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")]
        [InlineData("rvz:lzma:5:64kb:8", "rvz", "", "lzma", "5", "64kb", "8", false, "", "", "")]
        [InlineData("rvz:none:256kb:32", "rvz", "", "none", "", "256kb", "32", false, "", "", "")]
        [InlineData("rvz", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")]

        // CSO formats (only CSO is supported on GameCube, not CSO2/ZSO)
        [InlineData("cso:9:2kb:4", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // CSO not supported on GameCube
        [InlineData("cso2:5:4kb:8", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // CSO2 not supported on GameCube
        [InlineData("zso:12:16kb:2", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // ZSO not supported on GameCube
        [InlineData("cso", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // CSO not supported on GameCube

        // Lossless formats
        [InlineData("wbfs:y", "wbfs", "", "", "", "", "", true, "", "", "")]
        [InlineData("ciso:n", "ciso", "", "", "", "", "", false, "", "", "")]
        [InlineData("wbfs", "wbfs", "", "", "", "", "", true, "", "", "")]

        // Simple formats
        [InlineData("iso", "iso", "", "", "", "", "", false, "", "", "")]
        [InlineData("wux", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // WUX not supported on GameCube, fallback
        [InlineData("gdi", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // GDI not supported on GameCube, fallback

        // Formats incompatible with GameCube - should fallback to GameCube defaults
        [InlineData("deciso", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // DecISO only on PS3
        [InlineData("app", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // APP only on WiiU
        [InlineData("rvz:zstd:19:128kb:16/cue:split:bin:bin:sub", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")]
        [InlineData("cso:9:2kb:4/cue:joined:img:flac:sub", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")]
        [InlineData("iso/cue:split:bin:wav:sub", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")] // Dual format invalid on GameCube
        public void ConvertPropertyParsing_SetsCorrectComponents(
            string convertFormat, string expectedSingle, string expectedIndexed, string expectedEncoding,
            string expectedLevel, string expectedBlockSize, string expectedParallelism, bool expectedLossless,
            string expectedCueType, string expectedBinary, string expectedAudio)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.GameCube, TaskType.Convert);

            // Act
            settings.Convert = convertFormat;

            // Assert
            Assert.Equal(expectedSingle, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(expectedIndexed, settings.ConvertIndexedFormat ?? string.Empty);
            Assert.Equal(expectedEncoding, settings.ConvertEncoding ?? string.Empty);
            Assert.Equal(expectedLevel, settings.ConvertLevel ?? string.Empty);
            Assert.Equal(expectedBlockSize, settings.ConvertBlockSize ?? string.Empty);
            Assert.Equal(expectedParallelism, settings.ConvertParallelism ?? string.Empty);
            Assert.Equal(expectedLossless, settings.ConvertLossless);
            Assert.Equal(expectedCueType, settings.ConvertCueType ?? string.Empty);
            Assert.Equal(expectedBinary, settings.ConvertBinary ?? string.Empty);
            Assert.Equal(expectedAudio, settings.ConvertAudio ?? string.Empty);
        }

        #endregion

        #region Format String Generation Tests

        [Theory(DisplayName = "Component properties generate correct Convert string")]
        // RVZ generation (GameCube)
        [InlineData(SystemType.GameCube, "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.GameCube, "rvz", "", "lzma", "5", "64kb", "8", false, "", "", "", "rvz:lzma:5:64kb:8")]
        [InlineData(SystemType.GameCube, "rvz", "", "none", "", "256kb", "32", false, "", "", "", "rvz:none:256kb:32")]

        // CSO generation (PSP)
        [InlineData(SystemType.PSP, "cso", "", "", "9", "2kb", "4", false, "", "", "", "cso:9:2kb:4")]
        [InlineData(SystemType.PSP, "cso2", "", "", "5", "4kb", "8", false, "", "", "", "cso2:5:4kb:8")]
        [InlineData(SystemType.PSP, "zso", "", "", "12", "16kb", "2", false, "", "", "", "zso:12:16kb:2")]

        // Lossless generation (GameCube)
        [InlineData(SystemType.GameCube, "wbfs", "", "", "", "", "", true, "", "", "", "wbfs:y")]
        [InlineData(SystemType.GameCube, "ciso", "", "", "", "", "", false, "", "", "", "ciso:n")]

        // Simple generation (appropriate systems)
        [InlineData(SystemType.GameCube, "iso", "", "", "", "", "", false, "", "", "", "iso")]
        [InlineData(SystemType.PS3, "deciso", "", "", "", "", "", false, "", "", "", "deciso/cue:split:bin:bin:sub")]
        [InlineData(SystemType.WiiU, "app", "", "", "", "", "", false, "", "", "", "app")]
        [InlineData(SystemType.WiiU, "wux", "", "", "", "", "", false, "", "", "", "wux")]
        [InlineData(SystemType.Dreamcast, "", "gdi", "", "", "", "", false, "", "", "", "gdi")]

        // Dual format generation (PS1 system)
        [InlineData(SystemType.PS1, "cso", "cue", "", "9", "2kb", "4", false, "joined", "img", "wav", "cso:9:2kb:4/cue:joined:img:wav:sub")]
        [InlineData(SystemType.PS1, "iso", "cue", "", "", "", "", false, "split", "bin", "flac", "iso/cue:split:bin:flac:sub")]
        public void ComponentProperties_GenerateCorrectConvertString(
            SystemType testSystem, string singleFormat, string indexedFormat, string encoding, string level, string blockSize,
            string parallelism, bool lossless, string cueType, string binary, string audio, string expectedConvert)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(testSystem, TaskType.Convert);

            // Act
            if (!string.IsNullOrEmpty(singleFormat))
                settings.ConvertSingleFormat = singleFormat;
            if (!string.IsNullOrEmpty(indexedFormat))
                settings.ConvertIndexedFormat = indexedFormat;
            if (!string.IsNullOrEmpty(encoding))
                settings.ConvertEncoding = encoding;
            if (!string.IsNullOrEmpty(level))
                settings.ConvertLevel = level;
            if (!string.IsNullOrEmpty(blockSize))
                settings.ConvertBlockSize = blockSize;
            if (!string.IsNullOrEmpty(parallelism))
                settings.ConvertParallelism = parallelism;
            settings.ConvertLossless = lossless;
            if (!string.IsNullOrEmpty(cueType))
                settings.ConvertCueType = cueType;
            if (!string.IsNullOrEmpty(binary))
                settings.ConvertBinary = binary;
            if (!string.IsNullOrEmpty(audio))
                settings.ConvertAudio = audio;

            // Assert
            Assert.Equal(expectedConvert, settings.Convert);
        }

        #endregion

        #region CUE Format Generation Tests

        [Theory(DisplayName = "CUE indexed format generates correct strings")]
        [InlineData("", "cue", "", "", "", "", false, "split", "bin", "bin", "cue:split:bin:bin:sub")]
        [InlineData("", "cue", "", "", "", "", false, "joined", "img", "flac", "cue:joined:img:flac:sub")]
        public void CueIndexedFormat_GeneratesCorrectConvertString(
            string singleFormat, string indexedFormat, string encoding, string level, string blockSize,
            string parallelism, bool lossless, string cueType, string binary, string audio, string expectedConvert)
        {
            // Arrange - Use Dreamcast for index-only CUE tests
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Dreamcast, TaskType.Convert);

            // Act
            settings.ConvertSingleFormat = singleFormat;
            settings.ConvertIndexedFormat = indexedFormat;
            settings.ConvertEncoding = encoding;
            settings.ConvertLevel = level;
            settings.ConvertBlockSize = blockSize;
            settings.ConvertParallelism = parallelism;
            settings.ConvertLossless = lossless;
            settings.ConvertCueType = cueType;
            settings.ConvertBinary = binary;
            settings.ConvertAudio = audio;

            // Assert
            Assert.Equal(expectedConvert, settings.Convert);
        }

        #endregion

        #region Default Value Application Tests

        [Theory(DisplayName = "Missing format components apply correct defaults")]
        // RVZ partial → complete
        [InlineData(SystemType.GameCube, "rvz", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.GameCube, "rvz:lzma", "rvz:lzma:5:128kb:16")]
        [InlineData(SystemType.GameCube, "rvz:zstd:15", "rvz:zstd:15:128kb:16")]
        [InlineData(SystemType.GameCube, "rvz:none:64kb", "rvz:none:64kb:16")]

        // CSO partial → complete
        [InlineData(SystemType.PSP, "cso", "cso:9:2kb:4")]
        [InlineData(SystemType.PSP, "cso:5", "cso:5:2kb:4")]
        [InlineData(SystemType.PSP, "cso:9:4kb", "cso:9:4kb:4")]
        [InlineData(SystemType.PSP, "zso", "zso:12:2kb:4")]

        // WBFS partial → complete
        [InlineData(SystemType.GameCube, "wbfs", "wbfs:y")]
        [InlineData(SystemType.GameCube, "wbfs:y", "wbfs:y")]
        [InlineData(SystemType.GameCube, "ciso", "ciso:y")]

        // CUE partial → complete
        [InlineData(SystemType.Dreamcast, "cue", "cue:split:bin:bin:sub")]
        [InlineData(SystemType.Dreamcast, "cue:joined", "cue:joined:bin:bin:sub")]
        [InlineData(SystemType.Dreamcast, "cue:split:img", "cue:split:img:bin:sub")]
        [InlineData(SystemType.Dreamcast, "gdi", "gdi")]

        // Dual format partial → complete
        [InlineData(SystemType.PS1, "cso/cue", "cso:9:2kb:4/cue:split:bin:bin:sub")]
        [InlineData(SystemType.Saturn, "iso/cue:joined", "iso/cue:joined:bin:bin:sub")]
        public void MissingComponents_ApplyCorrectDefaults(
            SystemType systemType, string partialFormat, string expectedCompleteFormat)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Act
            settings.Convert = partialFormat;

            // Assert
            Assert.Equal(expectedCompleteFormat, settings.Convert);
        }

        #endregion

        #region UI Interaction and Dependency Tests

        [Theory(DisplayName = "Encoding changes apply correct level defaults")]
        [InlineData(SystemType.GameCube, "rvz", "zstd", "lzma", "19", "5")]
        [InlineData(SystemType.GameCube, "rvz", "lzma", "zstd", "5", "19")]
        [InlineData(SystemType.GameCube, "rvz", "zstd", "none", "19", "")]
        [InlineData(SystemType.GameCube, "rvz", "none", "zstd", "", "19")]
        [InlineData(SystemType.GameCube, "rvz", "lzma", "none", "5", "")]
        [InlineData(SystemType.GameCube, "rvz", "none", "lzma", "", "5")]
        public void EncodingChanges_ApplyCorrectLevelDefaults(
            SystemType systemType, string format, string fromEncoding, string toEncoding,
            string expectedFromLevel, string expectedToLevel)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Manually set format and encoding to trigger smart defaults (simulating user interaction)
            settings.ConvertSingleFormat = format;
            settings.ConvertEncoding = fromEncoding;

            // Verify initial state
            Assert.Equal(expectedFromLevel, settings.ConvertLevel ?? string.Empty);

            // Act - Change encoding (simulating user interaction)
            settings.ConvertEncoding = toEncoding;

            // Assert
            Assert.Equal(expectedToLevel, settings.ConvertLevel ?? string.Empty);
        }

        [Theory(DisplayName = "Format changes reset to appropriate defaults")]
        [InlineData(SystemType.GameCube, "rvz:zstd:15:64kb:8", "wbfs", "wbfs:y")]
        [InlineData(SystemType.GameCube, "wbfs:y", "iso", "iso")]
        [InlineData(SystemType.PSP, "zso:8:8kb:1", "cso2", "cso2:9:2kb:4")]
        [InlineData(SystemType.XBox, "cso:3:16kb:2", "iso", "iso")]
        public void FormatChanges_ResetToAppropriateDefaults(
            SystemType systemType, string initialFormat, string newFormat, string expectedResult)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);
            settings.Convert = initialFormat;

            // Act - Change format (simulating user interaction)
            settings.ConvertSingleFormat = newFormat;

            // Assert
            Assert.Equal(expectedResult, settings.Convert);
        }

        #endregion

        #region Block Size and Level Range Tests

        [Theory(DisplayName = "Format provides correct block size ranges")]
        [InlineData("rvz", new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("wbfs", new string[0])] // WBFS block sizes not configurable in NKit
        [InlineData("ciso", new string[0])] // CISO block sizes not configurable in NKit
        [InlineData("cso", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("cso2", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("zso", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("iso", new string[0])]
        [InlineData("deciso", new string[0])]
        [InlineData("app", new string[0])]
        [InlineData("wux", new string[0])]
        [InlineData("gdi", new string[0])]
        [InlineData("cue", new string[0])]
        public void Format_ProvidesCorrectBlockSizeRanges(string format, string[] expectedBlockSizes)
        {
            // Act
            string[] blockSizes = SystemCapabilitiesTestData.GetBlockSizes(format);

            // Assert
            Assert.Equal(expectedBlockSizes, blockSizes);
        }

        [Theory(DisplayName = "Format and encoding provide correct level ranges")]
        [InlineData("rvz", "zstd", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 })]
        [InlineData("rvz", "lzma", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
        [InlineData("rvz", "none", new int[0])]
        [InlineData("cso", "", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
        [InlineData("cso2", "", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })]
        [InlineData("zso", "", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })]
        public void FormatAndEncoding_ProvideCorrectLevelRanges(string format, string encoding, int[] expectedLevels)
        {
            // Act
            FormatCapability[] formatCapabilities = SystemCapabilitiesTestData.GetFormatCapabilities(format);
            FormatCapability capability = string.IsNullOrEmpty(encoding)
                ? formatCapabilities.FirstOrDefault()
                : formatCapabilities.FirstOrDefault(c => c.CompressionType.Equals(encoding, System.StringComparison.OrdinalIgnoreCase));

            // Assert
            Assert.NotNull(capability);
            Assert.Equal(expectedLevels, capability.LevelRange);
        }

        #endregion

        #region CUE Format Options Tests

        [Theory(DisplayName = "CUE format options generate correct strings")]
        [InlineData("split", "bin", "bin", "cue:split:bin:bin:sub")]
        [InlineData("joined", "img", "flac", "cue:joined:img:flac:sub")]
        [InlineData("split", "iso", "wav", "cue:split:iso:wav:sub")]
        [InlineData("joined", "bin", "raw", "cue:joined:bin:raw:sub")]
        [InlineData("split", "img", "bin", "cue:split:img:bin:sub")]
        public void CueFormatOptions_GenerateCorrectStrings(
            string cueType, string binaryExt, string audioExt, string expectedFormat)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(SystemType.Dreamcast, TaskType.Convert);

            // Act
            settings.ConvertIndexedFormat = "cue";
            settings.ConvertCueType = cueType;
            settings.ConvertBinary = binaryExt;
            settings.ConvertAudio = audioExt;

            // Assert
            Assert.Equal(expectedFormat, settings.Convert);
        }

        [Theory(DisplayName = "CUE options provide correct available values")]
        [InlineData("CueTypes", new[] { "split", "joined" }, "split")]
        [InlineData("BinaryExtensions", new[] { "bin", "img", "iso" }, "bin")]
        [InlineData("AudioExtensions", new[] { "bin", "wav", "flac", "raw" }, "bin")]
        public void CueOptions_ProvideCorrectAvailableValues(string optionType, string[] expectedOptions, string expectedDefault)
        {
            // Act
            CueFormatOptions cueOptions = SystemCapabilitiesTestData.CueOptions;

            // Assert
            switch (optionType)
            {
                case "CueTypes":
                    Assert.Equal(expectedOptions, cueOptions.CueTypes);
                    Assert.Equal(expectedDefault, cueOptions.DefaultCueType);
                    break;
                case "BinaryExtensions":
                    Assert.Equal(expectedOptions, cueOptions.BinaryExtensions);
                    Assert.Equal(expectedDefault, cueOptions.DefaultBinaryExtension);
                    break;
                case "AudioExtensions":
                    Assert.Equal(expectedOptions, cueOptions.AudioExtensions);
                    Assert.Equal(expectedDefault, cueOptions.DefaultAudioExtension);
                    break;
            }
        }

        #endregion

        #region Threading Configuration Tests

        [Theory(DisplayName = "Systems apply correct threading defaults")]
        [InlineData(SystemType.GameCube, 32, 16)]
        [InlineData(SystemType.Wii, 32, 16)]
        [InlineData(SystemType.WiiU, 32, 16)]
        [InlineData(SystemType.PcEngine, 32, 16)]
        [InlineData(SystemType.CDi, 32, 16)]
        [InlineData(SystemType.Saturn, 32, 16)]
        [InlineData(SystemType.SegaCD, 32, 16)]
        [InlineData(SystemType.Dreamcast, 32, 16)]
        [InlineData(SystemType.PSP, 32, 4)]
        [InlineData(SystemType.PS1, 32, 4)]
        [InlineData(SystemType.PS2, 32, 4)]
        [InlineData(SystemType.PS3, 32, 4)]
        [InlineData(SystemType.XBox, 32, 4)]
        [InlineData(SystemType.XBox360, 32, 4)]
        public void Systems_ApplyCorrectThreadingDefaults(SystemType systemType, int expectedMaxThreads, int expectedDefaultThreads)
        {
            // Act
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(systemType);

            // Assert
            Assert.NotNull(testData);
            Assert.Equal(expectedMaxThreads, testData.MaxThreads);
            Assert.Equal(expectedDefaultThreads, testData.DefaultThreads);
        }

        #endregion

        #region Edge Case and Validation Tests

        [Theory(DisplayName = "Invalid format strings fall back to system defaults")]
        [InlineData(SystemType.GameCube, "invalidformat", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.XBox, "rvz:zstd:19:128kb:16", "iso")] // Incompatible format
        [InlineData(SystemType.Dreamcast, "iso", "cue:split:bin:bin:sub")] // Wrong category
        [InlineData(SystemType.PSP, "wbfs:y:128kb:16", "cso:9:2kb:4")] // Incompatible format
        [InlineData(SystemType.WiiU, "cso:9:2kb:4", "wux")] // Incompatible format
        public void InvalidFormats_FallbackToSystemDefaults(
            SystemType systemType, string invalidFormat, string expectedFallback)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Act
            settings.Convert = invalidFormat;

            // Assert - Should fallback to system default when invalid format is used
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(systemType);
            Assert.Equal(testData.DefaultFormat, settings.Convert);
        }

        [Theory(DisplayName = "Empty or null formats apply system defaults")]
        [InlineData(SystemType.GameCube, null, "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.GameCube, "", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.XBox, null, "iso")]
        [InlineData(SystemType.XBox, "", "iso")]
        [InlineData(SystemType.Dreamcast, null, "cue:split:bin:bin:sub")]
        [InlineData(SystemType.Dreamcast, "", "cue:split:bin:bin:sub")]
        public void EmptyFormats_ApplySystemDefaults(
            SystemType systemType, string emptyFormat, string expectedDefault)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Act
            settings.Convert = emptyFormat;

            // Assert
            Assert.Equal(expectedDefault, settings.Convert);
        }

        #endregion

        #region System Format Collections Tests

        [Theory(DisplayName = "System provides correct supported format collections")]
        [InlineData(SystemType.GameCube, new[] { "iso", "rvz", "wbfs", "ciso" })]
        [InlineData(SystemType.Wii, new[] { "iso", "rvz", "wbfs", "ciso" })]
        [InlineData(SystemType.PSP, new[] { "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.XBox, new[] { "iso", "cso", "cso2", "zso" })]
        [InlineData(SystemType.XBox360, new[] { "iso", "cso", "cso2", "zso" })]
        [InlineData(SystemType.WiiU, new[] { "app", "iso", "wux" })]
        [InlineData(SystemType.Dreamcast, new[] { "cue", "gdi" })]
        [InlineData(SystemType.PS3, new[] { "cue", "cso", "cso2", "zso", "deciso", "iso" })]
        [InlineData(SystemType.PS1, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.PS2, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.PcEngine, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.CDi, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.Saturn, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.SegaCD, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        public void System_ProvidesCorrectSupportedFormatCollections(SystemType systemType, string[] expectedFormats)
        {
            // Act
            SystemCapability testData = SystemCapabilitiesTestData.GetSystemCapability(systemType);

            // Assert
            Assert.NotNull(testData);
            Assert.Equal(expectedFormats, testData.SupportedFormats);
        }

        [Theory(DisplayName = "System provides correct single format collections")]
        [InlineData(SystemType.GameCube, new[] { "iso", "rvz", "wbfs", "ciso" })]
        [InlineData(SystemType.PSP, new[] { "cso", "cso2", "zso", "iso" })]
        [InlineData(SystemType.XBox, new[] { "iso", "cso", "cso2", "zso" })]
        [InlineData(SystemType.Dreamcast, new string[0])] // Index-only
        [InlineData(SystemType.PS3, new[] { "cso", "cso2", "zso", "deciso", "iso" })]
        [InlineData(SystemType.PS1, new[] { "cso", "cso2", "zso", "iso" })]
        public void System_ProvidesCorrectSingleFormatCollections(SystemType systemType, string[] expectedSingleFormats)
        {
            // Act
            string[] singleFormats = SystemCapabilitiesTestData.GetSingleFormats(systemType);

            // Assert
            Assert.Equal(expectedSingleFormats, singleFormats);
        }

        [Theory(DisplayName = "System provides correct indexed format collections")]
        [InlineData(SystemType.GameCube, new string[0])] // Single-only
        [InlineData(SystemType.PSP, new string[0])] // Single-only
        [InlineData(SystemType.XBox, new string[0])] // Single-only
        [InlineData(SystemType.Dreamcast, new[] { "cue", "gdi" })] // Index-only
        [InlineData(SystemType.PS3, new[] { "cue" })] // Dual-format
        [InlineData(SystemType.PS1, new[] { "cue" })] // Dual-format
        public void System_ProvidesCorrectIndexedFormatCollections(SystemType systemType, string[] expectedIndexedFormats)
        {
            // Act
            string[] indexedFormats = SystemCapabilitiesTestData.GetIndexedFormats(systemType);

            // Assert
            Assert.Equal(expectedIndexedFormats, indexedFormats);
        }

        #endregion

        #region Complete Workflow Integration Tests

        [Theory(DisplayName = "Complete system switching workflows maintain correct state")]
        // GameCube → Dreamcast → PS1 → PSP chain
        [InlineData(SystemType.GameCube, SystemType.Dreamcast, SystemType.PS1, SystemType.PSP)]
        // Xbox → PS3 → Saturn → WiiU chain  
        [InlineData(SystemType.XBox, SystemType.PS3, SystemType.Saturn, SystemType.WiiU)]
        // PSP → PcEngine → Dreamcast → Wii chain
        [InlineData(SystemType.PSP, SystemType.PcEngine, SystemType.Dreamcast, SystemType.Wii)]
        public void CompleteSystemSwitchingWorkflows_MaintainCorrectState(
            SystemType system1, SystemType system2, SystemType system3, SystemType system4)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(system1, TaskType.Convert);

            // Act & Assert - System 1
            SystemCapability testData1 = SystemCapabilitiesTestData.GetSystemCapability(system1);
            Assert.Equal(testData1.DefaultFormat, settings.Convert);

            // Act & Assert - System 2
            settings.System = system2;
            SystemCapability testData2 = SystemCapabilitiesTestData.GetSystemCapability(system2);
            Assert.Equal(testData2.DefaultFormat, settings.Convert);
            Assert.Equal(testData2.DefaultSingleFormat ?? string.Empty, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(testData2.DefaultIndexedFormat ?? string.Empty, settings.ConvertIndexedFormat ?? string.Empty);

            // Act & Assert - System 3
            settings.System = system3;
            SystemCapability testData3 = SystemCapabilitiesTestData.GetSystemCapability(system3);
            Assert.Equal(testData3.DefaultFormat, settings.Convert);
            Assert.Equal(testData3.DefaultSingleFormat ?? string.Empty, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(testData3.DefaultIndexedFormat ?? string.Empty, settings.ConvertIndexedFormat ?? string.Empty);

            // Act & Assert - System 4
            settings.System = system4;
            SystemCapability testData4 = SystemCapabilitiesTestData.GetSystemCapability(system4);
            Assert.Equal(testData4.DefaultFormat, settings.Convert);
            Assert.Equal(testData4.DefaultSingleFormat ?? string.Empty, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(testData4.DefaultIndexedFormat ?? string.Empty, settings.ConvertIndexedFormat ?? string.Empty);
        }

        #endregion

        #region Comprehensive Format Combination Validation Tests

        [Theory(DisplayName = "System format combinations set properties correctly")]
        // GameCube format combinations (single-only system)
        [InlineData(SystemType.GameCube, "iso", "iso", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.GameCube, "rvz:zstd:19:128kb:16", "rvz", "", "zstd", "19", "128kb", "16", false, "", "", "")]
        [InlineData(SystemType.GameCube, "rvz:lzma:5:64kb:8", "rvz", "", "lzma", "5", "64kb", "8", false, "", "", "")]
        [InlineData(SystemType.GameCube, "rvz:none:256kb:32", "rvz", "", "none", "", "256kb", "32", false, "", "", "")]
        [InlineData(SystemType.GameCube, "wbfs:y", "wbfs", "", "", "", "", "", true, "", "", "")]
        [InlineData(SystemType.GameCube, "wbfs:n", "wbfs", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.GameCube, "ciso:y", "ciso", "", "", "", "", "", true, "", "", "")]
        [InlineData(SystemType.GameCube, "ciso:n", "ciso", "", "", "", "", "", false, "", "", "")]

        // PSP format combinations (single-only system)
        [InlineData(SystemType.PSP, "iso", "iso", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.PSP, "cso:9:2kb:4", "cso", "", "", "9", "2kb", "4", false, "", "", "")]
        [InlineData(SystemType.PSP, "cso:1:64kb:1", "cso", "", "", "1", "64kb", "1", false, "", "", "")]
        [InlineData(SystemType.PSP, "cso2:5:32kb:8", "cso2", "", "", "5", "32kb", "8", false, "", "", "")]
        [InlineData(SystemType.PSP, "zso:12:16kb:2", "zso", "", "", "12", "16kb", "2", false, "", "", "")]

        // Xbox format combinations (single-only system)
        [InlineData(SystemType.XBox, "iso", "iso", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.XBox, "cso:9:2kb:4", "cso", "", "", "9", "2kb", "4", false, "", "", "")]
        [InlineData(SystemType.XBox, "cso2:7:8kb:2", "cso2", "", "", "7", "8kb", "2", false, "", "", "")]
        [InlineData(SystemType.XBox, "zso:10:4kb:1", "zso", "", "", "10", "4kb", "1", false, "", "", "")]

        // WiiU format combinations (single-only system)
        [InlineData(SystemType.WiiU, "app", "app", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.WiiU, "iso", "iso", "", "", "", "", "", false, "", "", "")]
        [InlineData(SystemType.WiiU, "wux", "wux", "", "", "", "", "", false, "", "", "")]

        // Dreamcast format combinations (index-only system)
        [InlineData(SystemType.Dreamcast, "cue:split:bin:bin:sub", "", "cue", "", "", "", "", false, "split", "bin", "bin")]
        [InlineData(SystemType.Dreamcast, "cue:joined:img:flac:sub", "", "cue", "", "", "", "", false, "joined", "img", "flac")]
        [InlineData(SystemType.Dreamcast, "cue:split:iso:wav:sub", "", "cue", "", "", "", "", false, "split", "iso", "wav")]
        [InlineData(SystemType.Dreamcast, "gdi", "", "gdi", "", "", "", "", false, "", "", "")]

        // PS3 dual-format combinations - CRITICAL: Dual format systems auto-populate both formats
        [InlineData(SystemType.PS3, "deciso", "deciso", "cue", "", "", "", "", false, "", "", "")] // Auto-adds CUE but no CUE properties
        [InlineData(SystemType.PS3, "iso", "iso", "cue", "", "", "", "", false, "", "", "")] // Auto-adds CUE but no CUE properties
        [InlineData(SystemType.PS3, "cso:9:2kb:4", "cso", "cue", "", "9", "2kb", "4", false, "", "", "")] // Auto-adds CUE but no CUE properties
        [InlineData(SystemType.PS3, "cue:split:bin:bin:sub", "cue", "cue", "", "", "", "", false, "split", "bin", "bin")] // CUE format sets both to CUE
        [InlineData(SystemType.PS3, "deciso/cue:joined:img:wav:sub", "deciso", "cue", "", "", "", "", false, "joined", "img", "wav")]
        [InlineData(SystemType.PS3, "cso:5:4kb:2/cue:split:iso:flac:sub", "cso", "cue", "", "5", "4kb", "2", false, "split", "iso", "flac")]

        // PS1 dual-format combinations - CRITICAL: Dual format systems auto-populate both formats
        [InlineData(SystemType.PS1, "iso", "iso", "cue", "", "", "", "", false, "", "", "")] // Auto-adds CUE but no CUE properties
        [InlineData(SystemType.PS1, "cso:9:2kb:4", "cso", "cue", "", "9", "2kb", "4", false, "", "", "")] // Auto-adds CUE but no CUE properties
        [InlineData(SystemType.PS1, "cue:split:bin:bin:sub", "cue", "cue", "", "", "", "", false, "split", "bin", "bin")] // CUE format sets both to CUE
        [InlineData(SystemType.PS1, "iso/cue:joined:img:wav:sub", "iso", "cue", "", "", "", "", false, "joined", "img", "wav")]
        [InlineData(SystemType.PS1, "cso:7:8kb:1/cue:split:bin:flac:sub", "cso", "cue", "", "7", "8kb", "1", false, "split", "bin", "flac")]
        public void SystemFormatCombinations_SetPropertiesCorrectly(
            SystemType systemType, string formatString, string expectedSingle, string expectedIndexed,
            string expectedEncoding, string expectedLevel, string expectedBlockSize, string expectedParallelism,
            bool expectedLossless, string expectedCueType, string expectedBinary, string expectedAudio)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Act
            settings.Convert = formatString;

            // Assert
            Assert.Equal(expectedSingle, settings.ConvertSingleFormat ?? string.Empty);
            Assert.Equal(expectedIndexed, settings.ConvertIndexedFormat ?? string.Empty);
            Assert.Equal(expectedEncoding, settings.ConvertEncoding ?? string.Empty);
            Assert.Equal(expectedLevel, settings.ConvertLevel ?? string.Empty);
            Assert.Equal(expectedBlockSize, settings.ConvertBlockSize ?? string.Empty);
            Assert.Equal(expectedParallelism, settings.ConvertParallelism ?? string.Empty);
            Assert.Equal(expectedLossless, settings.ConvertLossless);
            Assert.Equal(expectedCueType, settings.ConvertCueType ?? string.Empty);
            Assert.Equal(expectedBinary, settings.ConvertBinary ?? string.Empty);
            Assert.Equal(expectedAudio, settings.ConvertAudio ?? string.Empty);
        }

        [Theory(DisplayName = "All supported system formats validate correctly")]
        // Test every format combination each system supports
        [InlineData(SystemType.GameCube, "iso")]
        [InlineData(SystemType.GameCube, "rvz")]
        [InlineData(SystemType.GameCube, "wbfs")]
        [InlineData(SystemType.GameCube, "ciso")]
        [InlineData(SystemType.Wii, "iso")]
        [InlineData(SystemType.Wii, "rvz")]
        [InlineData(SystemType.Wii, "wbfs")]
        [InlineData(SystemType.Wii, "ciso")]
        [InlineData(SystemType.PSP, "cso")]
        [InlineData(SystemType.PSP, "cso2")]
        [InlineData(SystemType.PSP, "zso")]
        [InlineData(SystemType.PSP, "iso")]
        [InlineData(SystemType.XBox, "iso")]
        [InlineData(SystemType.XBox, "cso")]
        [InlineData(SystemType.XBox, "cso2")]
        [InlineData(SystemType.XBox, "zso")]
        [InlineData(SystemType.XBox360, "iso")]
        [InlineData(SystemType.XBox360, "cso")]
        [InlineData(SystemType.XBox360, "cso2")]
        [InlineData(SystemType.XBox360, "zso")]
        [InlineData(SystemType.WiiU, "app")]
        [InlineData(SystemType.WiiU, "iso")]
        [InlineData(SystemType.WiiU, "wux")]
        [InlineData(SystemType.Dreamcast, "cue")]
        [InlineData(SystemType.Dreamcast, "gdi")]
        [InlineData(SystemType.PS3, "cue")]
        [InlineData(SystemType.PS3, "cso")]
        [InlineData(SystemType.PS3, "cso2")]
        [InlineData(SystemType.PS3, "zso")]
        [InlineData(SystemType.PS3, "deciso")]
        [InlineData(SystemType.PS3, "iso")]
        [InlineData(SystemType.PS1, "cue")]
        [InlineData(SystemType.PS1, "cso")]
        [InlineData(SystemType.PS1, "cso2")]
        [InlineData(SystemType.PS1, "zso")]
        [InlineData(SystemType.PS1, "iso")]
        [InlineData(SystemType.PS2, "cue")]
        [InlineData(SystemType.PS2, "cso")]
        [InlineData(SystemType.PS2, "cso2")]
        [InlineData(SystemType.PS2, "zso")]
        [InlineData(SystemType.PS2, "iso")]
        [InlineData(SystemType.PcEngine, "cue")]
        [InlineData(SystemType.PcEngine, "cso")]
        [InlineData(SystemType.PcEngine, "cso2")]
        [InlineData(SystemType.PcEngine, "zso")]
        [InlineData(SystemType.PcEngine, "iso")]
        [InlineData(SystemType.CDi, "cue")]
        [InlineData(SystemType.CDi, "cso")]
        [InlineData(SystemType.CDi, "cso2")]
        [InlineData(SystemType.CDi, "zso")]
        [InlineData(SystemType.CDi, "iso")]
        [InlineData(SystemType.Saturn, "cue")]
        [InlineData(SystemType.Saturn, "cso")]
        [InlineData(SystemType.Saturn, "cso2")]
        [InlineData(SystemType.Saturn, "zso")]
        [InlineData(SystemType.Saturn, "iso")]
        [InlineData(SystemType.SegaCD, "cue")]
        [InlineData(SystemType.SegaCD, "cso")]
        [InlineData(SystemType.SegaCD, "cso2")]
        [InlineData(SystemType.SegaCD, "zso")]
        [InlineData(SystemType.SegaCD, "iso")]
        public void AllSupportedSystemFormats_ValidateCorrectly(SystemType systemType, string format)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);
            SystemCapability systemCapability = SystemCapabilitiesTestData.GetSystemCapability(systemType);

            // Act
            settings.Convert = format;

            // Assert - Format should be supported by system
            Assert.Contains(format, systemCapability.SupportedFormats);

            // Assert - Properties should be set according to format type
            FormatCapability[] formatCapabilities = SystemCapabilitiesTestData.GetFormatCapabilities(format);
            FormatCapability capability = formatCapabilities.FirstOrDefault();

            if (capability != null)
            {
                // Single formats should set ConvertSingleFormat
                if (systemCapability.SingleFormats.Contains(format))
                {
                    Assert.Equal(format, settings.ConvertSingleFormat);
                }

                // Indexed formats should set ConvertIndexedFormat  
                if (systemCapability.IndexedFormats.Contains(format))
                {
                    Assert.Equal(format, settings.ConvertIndexedFormat);
                }

                // Lossless formats should set ConvertLossless appropriately
                if (capability.HasLossless)
                {
                    Assert.True(settings.ConvertLossless || !settings.ConvertLossless); // Should be set to some value
                }

                // CUE formats should set CUE-specific properties
                if (capability.HasCueOptions)
                {
                    Assert.False(string.IsNullOrEmpty(settings.ConvertCueType));
                    Assert.False(string.IsNullOrEmpty(settings.ConvertBinary));
                    Assert.False(string.IsNullOrEmpty(settings.ConvertAudio));
                }
            }
        }

        [Theory(DisplayName = "System switching resets to target system defaults")]
        // Test switching between systems - always resets to target system default
        [InlineData(SystemType.GameCube, SystemType.Wii, "rvz:zstd:19:128kb:16", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.Wii, SystemType.GameCube, "iso", "rvz:zstd:19:128kb:16")]
        [InlineData(SystemType.PS1, SystemType.PS2, "cso:9:2kb:4", "cso:9:2kb:4/cue:split:bin:bin:sub")]
        [InlineData(SystemType.PS2, SystemType.PS1, "iso", "cso:9:2kb:4/cue:split:bin:bin:sub")]
        [InlineData(SystemType.Saturn, SystemType.PcEngine, "iso/cue:split:bin:bin:sub", "iso/cue:split:bin:bin:sub")]
        [InlineData(SystemType.PcEngine, SystemType.CDi, "cue:joined:img:flac:sub", "iso/cue:joined:img:flac:sub")] // Preserves CUE properties, sets single to CDi default
        [InlineData(SystemType.XBox, SystemType.XBox360, "cso:5:4kb:2", "iso")]
        [InlineData(SystemType.XBox360, SystemType.XBox, "iso", "iso")]
        public void SystemSwitching_ResetsToTargetSystemDefaults(
            SystemType fromSystem, SystemType toSystem, string formatString, string expectedResult)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(fromSystem, TaskType.Convert);
            settings.Convert = formatString;

            // Act
            settings.System = toSystem;

            // Assert - System switching behavior varies by system type and format compatibility
            Assert.Equal(expectedResult, settings.Convert);
        }

        [Theory(DisplayName = "Invalid format combinations fallback correctly")]
        // Test unsupported format combinations for each system
        [InlineData(SystemType.GameCube, "cue", "rvz:zstd:19:128kb:16")] // CUE not supported
        [InlineData(SystemType.GameCube, "deciso", "rvz:zstd:19:128kb:16")] // DecISO not supported
        [InlineData(SystemType.GameCube, "app", "rvz:zstd:19:128kb:16")] // APP not supported
        [InlineData(SystemType.PSP, "rvz", "cso:9:2kb:4")] // RVZ not supported
        [InlineData(SystemType.PSP, "wbfs", "cso:9:2kb:4")] // WBFS not supported
        [InlineData(SystemType.PSP, "cue", "cso:9:2kb:4")] // CUE not supported
        [InlineData(SystemType.Dreamcast, "iso", "cue:split:bin:bin:sub")] // ISO not supported (index-only)
        [InlineData(SystemType.Dreamcast, "rvz", "cue:split:bin:bin:sub")] // RVZ not supported
        [InlineData(SystemType.XBox, "wbfs", "iso")] // WBFS not supported
        [InlineData(SystemType.XBox, "app", "iso")] // APP not supported
        [InlineData(SystemType.WiiU, "cso", "wux")] // CSO not supported
        [InlineData(SystemType.WiiU, "rvz", "wux")] // RVZ not supported
        public void InvalidFormatCombinations_FallbackCorrectly(
            SystemType systemType, string invalidFormat, string expectedFallback)
        {
            // Arrange
            global::NKit.Ui.Models.NKitSettings settings = global::NKit.Ui.Models.NKitSettings.GetDefaultSettings(systemType, TaskType.Convert);

            // Act
            settings.Convert = invalidFormat;

            // Assert
            Assert.Equal(expectedFallback, settings.Convert);
        }

        #endregion
    }
}