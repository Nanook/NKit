using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Comprehensive tests for NKitConfigurationProvider covering all validation, parsing, and generation methods.
    /// Organized into logical test groups for clarity and maintainability.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class NKitConfigurationProviderTests
    {
        #region Format Support Tests

        [Theory(DisplayName = "Nintendo systems support expected formats")]
        [InlineData(SystemType.GameCube, new[] { "iso", "rvz", "wbfs", "ciso" })]
        [InlineData(SystemType.Wii, new[] { "iso", "rvz", "wbfs", "ciso" })]
        public void GetSupportedFormats_NintendoSystems_ReturnsExpectedFormats(SystemType systemType, string[] expectedFormats)
        {
            // Act
            IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(expectedFormats, formats);
        }

        [Theory(DisplayName = "Sony systems support expected formats")]
        [InlineData(SystemType.PS3, new[] { "cue", "cso", "cso2", "zso", "deciso", "iso" })]
        [InlineData(SystemType.PSP, new[] { "cso", "cso2", "zso", "iso" })]
        public void GetSupportedFormats_SonySystems_ReturnsExpectedFormats(SystemType systemType, string[] expectedFormats)
        {
            // Act
            IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(expectedFormats, formats);
        }

        [Theory(DisplayName = "Other systems support expected formats")]
        [InlineData(SystemType.WiiU, new[] { "app", "tmd", "iso", "wux" })]
        [InlineData(SystemType.Dreamcast, new[] { "cue", "gdi" })]
        [InlineData(SystemType.PcEngine, new[] { "cue", "cso", "cso2", "zso", "iso" })]
        public void GetSupportedFormats_OtherSystems_ReturnsExpectedFormats(SystemType systemType, string[] expectedFormats)
        {
            // Act
            IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(expectedFormats, formats);
        }

        [Fact(DisplayName = "Unknown system returns empty list")]
        public void GetSupportedFormats_UnknownSystem_ReturnsEmpty()
        {
            // Act
            IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats((SystemType)999);

            // Assert
            Assert.Empty(formats);
        }

        [Fact(DisplayName = "GetAllSupportedFormats returns all unique formats")]
        public void GetAllSupportedFormats_ReturnsAllUniqueFormats()
        {
            // Act
            IReadOnlyList<string> allFormats = ConfigSettingsRanges.GetAllSupportedFormats();

            // Assert - Should contain major formats
            Assert.Contains("rvz", allFormats, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("cso", allFormats, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("cue", allFormats, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("iso", allFormats, StringComparer.OrdinalIgnoreCase);

            // Should be unique and sorted
            Assert.Equal(allFormats.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList(), allFormats);
        }

        #endregion

        #region Default Format Tests

        [Theory(DisplayName = "Systems return expected default formats")]
        [InlineData(SystemType.GameCube, "rvz")]
        [InlineData(SystemType.Wii, "rvz")]
        [InlineData(SystemType.PS3, "deciso")] // Updated: PS3 uses DecISO per nkit.yaml
        [InlineData(SystemType.PSP, "cso")]
        [InlineData(SystemType.WiiU, "wux")]
        [InlineData(SystemType.Dreamcast, "cue")]
        [InlineData(SystemType.PcEngine, "iso")] // Fixed: PcEngine defaults to ISO, not CUE
        public void GetDefaultFormat_ReturnsExpectedDefault(SystemType systemType, string expectedFormat)
        {
            // Act
            string format = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            // Assert
            Assert.Equal(expectedFormat, format);
        }

        #endregion

        #region Format Validation Tests

        [Theory(DisplayName = "Valid format strings pass validation")]
        [InlineData("rvz", true)]
        [InlineData("rvz:zstd", true)]
        [InlineData("rvz:zstd:19:128kb:16", true)]
        [InlineData("cue", true)]
        [InlineData("cue:split:bin:bin:sub", true)] // Using bin for audio
        [InlineData("cso", true)]
        [InlineData("cso:9:2kb:4", true)]
        [InlineData("iso", true)]
        [InlineData("wbfs", true)]
        [InlineData("wbfs:y", true)]
        [InlineData("ciso", true)]
        [InlineData("ciso:n", true)]
        public void ValidateFormatString_ValidFormats_ReturnsSuccess(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateFormatString(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
            if (!expectedValid)
            {
                Assert.NotNull(result.ErrorMessage);
            }
        }

        [Theory(DisplayName = "Invalid format strings fail validation")]
        [InlineData("unknownformat", false)]
        [InlineData("rvz:invalidencoding", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("cso:10:2kb:4", false)] // Level too high
        [InlineData("wbfs:maybe", false)] // Invalid lossless param
        public void ValidateFormatString_InvalidFormats_ReturnsError(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateFormatString(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
            if (!expectedValid)
            {
                Assert.NotNull(result.ErrorMessage);
            }
        }

        [Theory(DisplayName = "Format validation respects system compatibility")]
        [InlineData(SystemType.GameCube, "rvz", true)]
        [InlineData(SystemType.GameCube, "cso", false)] // CSO not supported on GameCube
        [InlineData(SystemType.PS3, "cso", true)]
        [InlineData(SystemType.PS3, "rvz", false)] // RVZ not supported on PS3
        [InlineData(SystemType.Dreamcast, "cue", true)]
        [InlineData(SystemType.Dreamcast, "rvz", false)] // RVZ not supported on Dreamcast
        public void ValidateFormatString_SystemCompatibility_ReturnsExpected(
            SystemType systemType, string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateFormatString(systemType, formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
            if (!expectedValid)
            {
                Assert.Contains("not supported by system", result.ErrorMessage);
            }
        }

        #endregion

        #region RVZ Format Validation Tests

        [Theory(DisplayName = "Valid RVZ formats pass validation")]
        [InlineData("rvz", true)]
        [InlineData("rvz:zstd", true)]
        [InlineData("rvz:zstd:19", true)]
        [InlineData("rvz:zstd:19:128kb", true)]
        [InlineData("rvz:zstd:19:128kb:16", true)]
        [InlineData("rvz:zstd:0:32kb:1", false)]
        [InlineData("rvz:zstd:22:2mb:32", true)]
        [InlineData("rvz:lzma", true)]
        [InlineData("rvz:lzma:5:128kb:16", true)]
        [InlineData("rvz:lzma:1:32kb:1", true)]
        [InlineData("rvz:lzma:9:2mb:32", true)]
        [InlineData("rvz:none", true)]
        [InlineData("rvz:none:128kb:4", true)]
        public void ValidateRvzFormat_ValidFormats_ReturnSuccess(string format, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateRvzFormat(format);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory(DisplayName = "Invalid RVZ formats fail validation")]
        [InlineData("rvz:zstd:23:128kb:16")] // Level too high
        [InlineData("rvz:zstd:19:3kb:16")]  // Invalid block size
        [InlineData("rvz:zstd:19:128kb:100")] // Parallelism too high
        [InlineData("rvz:lzma:0:128kb:16")]  // Level too low for LZMA
        [InlineData("rvz:lzma:10:128kb:16")] // Level too high for LZMA
        [InlineData("rvz:invalid:5:128kb:16")] // Invalid encoding
        [InlineData("")] // Empty string
        public void ValidateRvzFormat_InvalidFormats_ReturnError(string format)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateRvzFormat(format);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
        }

        #endregion

        #region CUE Format Validation Tests

        [Theory(DisplayName = "Valid CUE formats pass validation")]
        [InlineData("cue", true)]
        [InlineData("cue:split", true)]
        [InlineData("cue:split:bin", true)]
        [InlineData("cue:split:bin:bin", true)]
        [InlineData("cue:split:bin:bin:sub", true)]
        [InlineData("cue:joined:img:flac:sub", true)] // Using flac instead of raw
        [InlineData("cue:split:iso:bin:sub", true)]
        public void ValidateCueFormat_ValidFormats_ReturnSuccess(string format, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCueFormat(format);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory(DisplayName = "Invalid CUE formats fail validation")]
        [InlineData("cue:invalid:bin:bin:sub")] // Invalid cue type
        [InlineData("cue:split:invalid:bin:sub")] // Invalid binary type
        [InlineData("cue:split:bin:raw:sub")] // Invalid audio type - "raw" is not supported
        [InlineData("")] // Empty string
        public void ValidateCueFormat_InvalidFormats_ReturnError(string format)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCueFormat(format);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
        }

        #endregion

        #region CSO Format Validation Tests

        [Theory(DisplayName = "Valid CSO formats pass validation")]
        [InlineData("cso", "cso", true)]
        [InlineData("cso:9", "cso", true)]
        [InlineData("cso:9:2kb", "cso", true)]
        [InlineData("cso:9:2kb:16", "cso", true)]
        [InlineData("cso:1:128kb:1", "cso", true)]
        [InlineData("cso:9:64kb:32", "cso", true)]
        [InlineData("cso2", "cso2", true)]
        [InlineData("cso2:5:16kb:8", "cso2", true)]
        [InlineData("zso", "zso", true)]
        [InlineData("zso::2kb:4", "zso", true)]
        [InlineData("zso:ignored:2kb:4", "zso", true)]
        public void ValidateCsoFormat_ValidFormats_ReturnSuccess(string format, string formatType, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCsoFormat(format, formatType);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory(DisplayName = "Invalid CSO formats fail validation")]
        [InlineData("cso:0:2kb:16", "cso")] // Level too low
        [InlineData("cso:10:2kb:16", "cso")] // Level too high
        [InlineData("cso:5:1kb:16", "cso")] // Invalid block size
        [InlineData("cso:5:2kb:100", "cso")] // Parallelism too high
        public void ValidateCsoFormat_InvalidFormats_ReturnError(string format, string formatType)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCsoFormat(format, formatType);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
        }

        [Theory(DisplayName = "CSO format auto-detects format type")]
        [InlineData("cso", true)]
        [InlineData("cso:9:2kb:4", true)]
        [InlineData("cso2", true)]
        [InlineData("cso2:5:16kb:8", true)]
        [InlineData("zso", true)]
        [InlineData("zso::2kb:4", true)]
        public void ValidateCsoFormat_AutoDetectFormatType_ReturnsExpected(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCsoFormat(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        #endregion

        #region WBFS Format Validation Tests

        [Theory(DisplayName = "WBFS formats validate correctly")]
        [InlineData("wbfs", true)]
        [InlineData("wbfs:y", true)]
        [InlineData("wbfs:n", true)]
        [InlineData("wbfs:true", false)]
        [InlineData("wbfs:false", false)]
        [InlineData("wbfs:maybe", false)] // Invalid lossless parameter
        [InlineData("wbfs:y:extra", false)] // Too many parameters
        public void ValidateWbfsFormat_VariousInputs_ReturnsExpected(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateWbfsFormat(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        #endregion

        #region CISO Format Validation Tests

        [Theory(DisplayName = "CISO formats validate correctly")]
        [InlineData("ciso", true)]
        [InlineData("ciso:y", true)]
        [InlineData("ciso:n", true)]
        [InlineData("ciso:true", false)]
        [InlineData("ciso:false", false)]
        [InlineData("ciso:maybe", false)] // Invalid lossless parameter
        [InlineData("ciso:y:extra", false)] // Too many parameters
        public void ValidateCisoFormat_VariousInputs_ReturnsExpected(string formatString, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateCisoFormat(formatString);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        #endregion

        #region Extract Format Validation Tests

        [Theory(DisplayName = "Extract formats validate correctly")]
        [InlineData("", true)]  // Empty should be valid (uses defaults)
        [InlineData("f", true)] // Forensic only
        [InlineData("ri", true)] // Recursive + case insensitive
        [InlineData("f:*.iso", true)] // Forensic with pattern
        [InlineData("ri:*.dat", true)] // Flags with pattern
        [InlineData("m:*", true)] // Default extract type
        [InlineData("mi:*.iso", true)] // Mask with case insensitive
        public void ValidateExtractFormat_VariousConfigs_ReturnsExpected(string extractConfig, bool expectedValid)
        {
            // Act
            ValidationResult result = ConfigSettingsFormatValidator.ValidateExtractFormat(extractConfig);

            // Assert
            Assert.Equal(expectedValid, result.IsValid);
        }

        #endregion

        #region Block Size Tests

        [Theory(DisplayName = "RVZ formats return RVZ block sizes")]
        [InlineData("rvz")]
        [InlineData("wbfs")]
        [InlineData("ciso")]
        public void GetBlockSizes_RvzFormats_ReturnsRvzSizes(string format)
        {
            // Arrange
            string[] expectedSizes = new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" };

            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Equal(expectedSizes, blockSizes);
        }

        [Theory(DisplayName = "CSO formats return CSO block sizes")]
        [InlineData("cso")]
        [InlineData("cso2")]
        [InlineData("zso")]
        public void GetBlockSizes_CsoFormats_ReturnsCsoSizes(string format)
        {
            // Arrange
            string[] expectedSizes = new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" };

            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Equal(expectedSizes, blockSizes);
        }

        [Fact(DisplayName = "Unknown format returns RVZ default")]
        public void GetBlockSizes_UnknownFormat_ReturnsRvzDefault()
        {
            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes("unknown");

            // Assert
            Assert.Equal(ConfigSettingsRanges.GetRvzBlockSizes(), blockSizes);
        }

        [Theory(DisplayName = "Null or empty format returns RVZ default")]
        [InlineData(null)]
        [InlineData("")]
        public void GetBlockSizes_NullOrEmpty_ReturnsRvzDefault(string format)
        {
            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Equal(ConfigSettingsRanges.GetRvzBlockSizes(), blockSizes);
        }

        [Theory(DisplayName = "Systems return correct default block sizes")]
        [InlineData(SystemType.GameCube, "rvz", "128kb")]
        [InlineData(SystemType.Wii, "wbfs", "128kb")]
        [InlineData(SystemType.PS3, "cso", "16kb")]
        [InlineData(SystemType.PSP, "cso2", "2kb")]
        [InlineData(SystemType.GameCube, null, "128kb")] // No format specified
        [InlineData(SystemType.PS3, null, "2kb")] // No format specified
        public void GetDefaultBlockSize_WithFormat_ReturnsCorrectDefault(
            SystemType systemType, string format, string expectedBlockSize)
        {
            // Act
            string blockSize = ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format);

            // Assert
            Assert.Equal(expectedBlockSize, blockSize);
        }

        [Fact(DisplayName = "Unknown system returns 2kb default")]
        public void GetDefaultBlockSize_UnknownSystem_Returns2kb()
        {
            // Act
            string blockSize = ConfigSettingsDefaults.GetDefaultBlockSize((SystemType)999);

            // Assert
            Assert.Equal("2kb", blockSize);
        }

        #endregion

        #region Compression Level Tests

        [Fact(DisplayName = "ZStd levels return expected range")]
        public void GetZStdLevels_ReturnsExpectedRange()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetZStdLevels();

            // Assert
            Assert.Equal(22, levels.Count); // 1-22
            Assert.Equal(1, levels.First());
            Assert.Equal(22, levels.Last());
        }

        [Fact(DisplayName = "LZMA/Zlib levels return expected range")]
        public void GetLzmaZlibLevels_ReturnsExpectedRange()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetLzmaLevels();

            // Assert
            Assert.Equal(9, levels.Count); // 1-9
            Assert.Equal(1, levels.First());
            Assert.Equal(9, levels.Last());
        }

        [Theory(DisplayName = "Default compression levels match encoding type")]
        [InlineData(RvzEncodingType.ZStd, 19)]
        [InlineData(RvzEncodingType.Lzma, 5)]
        [InlineData(RvzEncodingType.None, 0)]
        public void GetDefaultCompressionLevel_ReturnsExpectedDefault(RvzEncodingType encoding, int expectedLevel)
        {
            // Act
            int level = ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding);

            // Assert
            Assert.Equal(expectedLevel, level);
        }

        #endregion

        #region Parallelism Tests

        [Theory(DisplayName = "Systems return expected parallelism ranges")]
        [InlineData(SystemType.GameCube, 32)]
        [InlineData(SystemType.Wii, 32)]
        [InlineData(SystemType.PS3, 32)]
        [InlineData(SystemType.PSP, 32)]
        public void GetParallelismValues_ReturnsExpectedRange(SystemType systemType, int expectedMax)
        {
            // Act
            IReadOnlyList<int> values = ConfigSettingsRanges.GetParallelismValues(systemType);

            // Assert
            Assert.Equal(expectedMax, values.Count);
            Assert.Equal(1, values.First());
            Assert.Equal(expectedMax, values.Last());
        }

        [Theory(DisplayName = "Systems return expected default parallelism")]
        [InlineData(SystemType.GameCube, 16)]
        [InlineData(SystemType.Wii, 16)]
        [InlineData(SystemType.PS3, 4)]
        [InlineData(SystemType.PSP, 4)]
        public void GetDefaultParallelism_ReturnsExpectedDefault(SystemType systemType, int expectedParallelism)
        {
            // Act
            int parallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);

            // Assert
            Assert.Equal(expectedParallelism, parallelism);
        }

        #endregion

        #region CUE/Audio Configuration Tests

        [Fact(DisplayName = "CUE types return expected values")]
        public void GetCueTypes_ReturnsExpectedTypes()
        {
            // Arrange
            string[] expectedTypes = new[] { "split", "joined" };

            // Act
            IReadOnlyList<string> cueTypes = ConfigSettingsRanges.GetCueTypes();

            // Assert
            Assert.Equal(expectedTypes, cueTypes);
        }

        [Fact(DisplayName = "Binary extensions return expected values")]
        public void GetBinaryExtensions_ReturnsExpectedExtensions()
        {
            // Arrange
            string[] expectedExtensions = new[] { "bin", "img", "iso" };

            // Act
            IReadOnlyList<string> extensions = ConfigSettingsRanges.GetBinaryExtensions();

            // Assert
            Assert.Equal(expectedExtensions, extensions);
        }

        [Fact(DisplayName = "Audio extensions return expected values")]
        public void GetAudioExtensions_ReturnsExpectedExtensions()
        {
            // Arrange
            string[] expectedExtensions = new[] { "bin", "flac", "wav", "raw" }; // Added 'raw'

            // Act
            IReadOnlyList<string> extensions = ConfigSettingsRanges.GetAudioExtensions();

            // Assert
            Assert.Equal(expectedExtensions, extensions);
        }

        [Fact(DisplayName = "Extract types return expected values")]
        public void GetExtractTypes_ReturnsExpectedValues()
        {
            // Arrange
            string[] expectedTypes = new[] { "m", "f", "i" };

            // Act
            IReadOnlyList<string> extractTypes = ConfigSettingsRanges.GetExtractTypes();

            // Assert
            Assert.Equal(expectedTypes, extractTypes);
        }

        #endregion

        #region Configuration Warning Tests

        [Theory(DisplayName = "Ultra compression levels generate warnings")]
        [InlineData("rvz", "20", "128kb", 1)] // Ultra level 20
        [InlineData("rvz", "21", "128kb", 1)] // Ultra level 21
        [InlineData("rvz", "22", "64kb", 1)]  // Ultra level 22
        [InlineData("rvz", "19", "128kb", 0)] // Normal level
        public void GetConfigurationWarnings_UltraLevels_ReturnsExpectedWarningCount(
            string format, string level, string blockSize, int expectedWarningCount)
        {
            // Act
            IEnumerable<string> warnings = ConfigSettingsFormatValidator.GetConfigurationWarnings(SystemType.GameCube, format, level, blockSize);

            // Assert
            Assert.Equal(expectedWarningCount, warnings.Count());
            if (expectedWarningCount > 0)
            {
                Assert.Contains("ZStd ultra levels 20+ require more memory", warnings.First());
            }
        }

        [Theory(DisplayName = "CSO non-2kb block sizes generate warnings")]
        [InlineData("cso", "5", "2kb", 0)]   // Standard block size
        [InlineData("cso", "5", "4kb", 1)]   // Non-standard block size
        [InlineData("cso", "5", "16kb", 1)]  // Non-standard block size
        public void GetConfigurationWarnings_CsoBlockSizes_ReturnsExpectedWarningCount(
            string format, string level, string blockSize, int expectedWarningCount)
        {
            // Act
            IEnumerable<string> warnings = ConfigSettingsFormatValidator.GetConfigurationWarnings(SystemType.PS3, format, level, blockSize);

            // Assert
            Assert.Equal(expectedWarningCount, warnings.Count());
            if (expectedWarningCount > 0)
            {
                Assert.Contains("Some tools/apps may not support block sizes other than 2kb", warnings.First());
            }
        }

        #endregion

        #region Format Generation Tests

        [Theory(DisplayName = "RVZ format generation with defaults")]
        [InlineData(RvzEncodingType.ZStd, "rvz:zstd:19:128kb:16")]
        [InlineData(RvzEncodingType.Lzma, "rvz:lzma:5:128kb:16")]
        [InlineData(RvzEncodingType.None, "rvz:none:128kb:16")]
        public void GenerateRvzFormatString_WithDefaults_ReturnsExpectedFormat(
            RvzEncodingType encoding, string expectedFormat)
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateRvzFormatString(encoding);

            // Assert
            Assert.Equal(expectedFormat, format);
        }

        [Fact(DisplayName = "RVZ format generation with custom values")]
        public void GenerateRvzFormatString_WithCustomValues_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateRvzFormatString(RvzEncodingType.ZStd, 15, "64kb", 8);

            // Assert
            Assert.Equal("rvz:zstd:15:64kb:8", format);
        }

        [Fact(DisplayName = "CUE format generation with defaults")]
        public void GenerateCueFormatString_WithDefaults_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateCueFormatString();

            // Assert
            Assert.Equal("cue:split:bin:bin:sub", format);
        }

        [Fact(DisplayName = "CUE format generation with custom values")]
        public void GenerateCueFormatString_WithCustomValues_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateCueFormatString("joined", "img", "flac"); // Using flac instead of raw

            // Assert
            Assert.Equal("cue:joined:img:flac:sub", format);
        }

        [Fact(DisplayName = "Extract format generation with defaults")]
        public void GenerateExtractFormatString_WithDefaults_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateExtractFormatString();

            // Assert
            Assert.Equal("mi:*", format);
        }

        [Fact(DisplayName = "Extract forensic format returns forensic flag only")]
        public void GenerateExtractForensic_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateExtractFormatString("f", true, true, "*.iso");

            // Assert - Forensic mode overrides everything
            Assert.Equal("f", format);
        }

        [Fact(DisplayName = "Extract format generation with custom values")]
        public void GenerateExtractFormatString_WithCustomValues_ReturnsExpectedFormat()
        {
            // Act
            string format = ConfigSettingsFormatGenerator.GenerateExtractFormatString("m", true, false, "*.iso");

            // Assert - Should be "m" + "i" (case insensitive) + ":*.iso"
            Assert.Equal("m:*.iso", format);
        }

        #endregion

        #region Format Parsing Tests

        [Theory(DisplayName = "RVZ format parsing returns correct configuration")]
        [InlineData("rvz", SystemType.GameCube, RvzEncodingType.ZStd, 19, 0x20000, 16)]
        [InlineData("rvz:zstd", SystemType.Wii, RvzEncodingType.ZStd, 19, 0x20000, 16)]
        [InlineData("rvz:zstd:15", SystemType.GameCube, RvzEncodingType.ZStd, 15, 0x20000, 16)]
        [InlineData("rvz:zstd:19:64kb", SystemType.Wii, RvzEncodingType.ZStd, 19, 0x10000, 16)]
        [InlineData("rvz:zstd:19:128kb:8", SystemType.GameCube, RvzEncodingType.ZStd, 19, 0x20000, 8)]
        [InlineData("rvz:lzma", SystemType.Wii, RvzEncodingType.Lzma, 5, 0x20000, 16)]
        [InlineData("rvz:lzma:3:256kb:4", SystemType.GameCube, RvzEncodingType.Lzma, 3, 0x40000, 4)]
        [InlineData("rvz:none", SystemType.Wii, RvzEncodingType.None, 0, 0x20000, 16)]
        [InlineData("rvz:none:64kb:8", SystemType.GameCube, RvzEncodingType.None, 0, 0x10000, 8)]
        public void ParseRvzFormatConfiguration_ValidFormats_ReturnsExpectedConfig(
            string formatString, SystemType systemType, RvzEncodingType expectedEncoding,
            int expectedLevel, int expectedBlockSizeBytes, int expectedParallelism)
        {
            // Act
            RvzFormatConfiguration config = ConfigSettingsFormatParser.ParseFormatConfiguration(formatString, systemType) as RvzFormatConfiguration;

            // Assert
            Assert.Equal(expectedEncoding, config.Encoding);
            Assert.Equal(expectedLevel, config.CompressionLevel);
            Assert.Equal(expectedBlockSizeBytes, config.BlockSizeBytes);
            Assert.Equal(expectedParallelism, config.Parallelism);
        }

        [Theory(DisplayName = "RVZ ultra levels generate warnings during parsing")]
        [InlineData("rvz:zstd:20:128kb:16")]
        [InlineData("rvz:zstd:21:64kb:8")]
        public void ParseRvzFormatConfiguration_UltraLevels_ReturnsWarnings(string formatString)
        {
            // Act
            RvzFormatConfiguration config = ConfigSettingsFormatParser.ParseFormatConfiguration(formatString, SystemType.GameCube) as RvzFormatConfiguration;

            // Assert
            Assert.True(config.Warnings.Count > 0, "Should have warnings for ultra compression levels");
            Assert.Contains("ZStd ultra levels 20+ require more memory", config.Warnings.First());
        }

        [Theory(DisplayName = "Invalid RVZ formats throw exceptions")]
        [InlineData("rvz:invalid:19:128kb:16")] // Invalid encoding
        [InlineData("rvz:zstd:23:128kb:16")]   // Level too high
        [InlineData("rvz:lzma:0:128kb:16")]    // Level too low for LZMA
        public void ParseRvzFormatConfiguration_InvalidFormats_ThrowsException(string formatString)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                ConfigSettingsFormatParser.ParseFormatConfiguration(formatString, SystemType.GameCube));
        }

        [Theory(DisplayName = "CUE format parsing returns correct configuration")]
        [InlineData("cue", SystemType.Dreamcast, "split", "bin", "bin", "", "")]
        [InlineData("cue:split", SystemType.PS1, "split", "bin", "bin", "sub", "")]
        [InlineData("cue:split:bin", SystemType.Saturn, "split", "bin", "bin", "sub", "")]
        [InlineData("cue:split:bin:flac", SystemType.SegaCD, "split", "bin", "flac", "sub", "")] // Using flac instead of raw
        [InlineData("cue:split:bin:wav:sub", SystemType.PcEngine, "split", "bin", "wav", "sub", "")] // Using wav instead of raw
        [InlineData("cue:joined:img:flac", SystemType.PS1, "joined", "img", "flac", "sub", "")] // Using flac instead of raw
        public void ParseCueFormatConfiguration_ValidFormats_ReturnsExpectedConfig(
            string formatString, SystemType systemType, string expectedCueType,
            string expectedBinary, string expectedAudio, string expectedSub, string expectedDataSize)
        {
            // Act
            CueFormatConfiguration config = ConfigSettingsFormatParser.ParseFormatConfiguration(formatString, systemType) as CueFormatConfiguration;

            // Assert
            Assert.Equal(expectedCueType, config.CueType);
            Assert.Equal(expectedBinary, config.BinaryExtension);
            Assert.Equal(expectedAudio, config.AudioExtension);
            Assert.Equal(expectedSub, config.SubType);
            Assert.Equal(expectedDataSize, config.DataSize);
        }

        [Fact(DisplayName = "Dreamcast system forces special CUE values")]
        public void ParseCueFormatConfiguration_DreamcastSystem_ForcesSpecialValues()
        {
            // Act - Dreamcast should preserve explicitly provided values but override sub/data size
            CueFormatConfiguration config = ConfigSettingsFormatParser.ParseFormatConfiguration(
                "cue:joined:img:flac:other", SystemType.Dreamcast) as CueFormatConfiguration; // Using flac instead of raw

            // Assert - Dreamcast preserves explicit values but forces sub/data to empty
            Assert.Equal("joined", config.CueType); // Preserves explicit 'joined'
            Assert.Equal("img", config.BinaryExtension); // Preserves explicit 'img' 
            Assert.Equal("flac", config.AudioExtension); // Preserves explicit 'flac'
            Assert.Equal("", config.SubType); // Forced to empty
            Assert.Equal("", config.DataSize); // Forced to empty
        }

        [Theory(DisplayName = "Extract configuration parsing returns correct values")]
        [InlineData("", false, true, false, true, ".*")]
        [InlineData("f", true, false, false, false, ".*")]
        [InlineData("ri", false, true, false, true, ".*")]
        [InlineData("mi:*.iso", false, true, true, false, "*.iso")]
        [InlineData("r:test.dat", false, false, false, true, "test.dat")]
        public void ParseExtractConfiguration_VariousInputs_ReturnsExpectedConfig(
            string input, bool expectedForensic, bool expectedCaseInsensitive,
            bool expectedMaskToRegex, bool expectedRecursive, string expectedPattern)
        {
            // Act
            ExtractConfiguration config = ConfigSettingsFormatParser.ParseExtractConfiguration(input);

            // Assert
            Assert.Equal(expectedForensic, config.IsForensic);
            Assert.Equal(expectedCaseInsensitive, config.IsCaseInsensitive);
            Assert.Equal(expectedMaskToRegex, config.IsMaskToRegex);
            Assert.Equal(expectedRecursive, config.IsRecursive);
            Assert.Equal(expectedPattern, config.Pattern);
        }

        #endregion

        #region RVZ Encoding Tests

        [Fact(DisplayName = "RVZ encoding types return expected values")]
        public void GetRvzEncodingTypes_ReturnsExpectedTypes()
        {
            // Act
            IReadOnlyList<RvzEncodingType> encodings = ConfigSettingsRanges.GetRvzEncodingTypes();

            // Assert
            Assert.Contains(RvzEncodingType.None, encodings);
            Assert.Contains(RvzEncodingType.ZStd, encodings);
            Assert.Contains(RvzEncodingType.Lzma, encodings);
            Assert.Equal(3, encodings.Count);
        }

        #endregion

        #region Block Size Parsing Tests

        [Theory(DisplayName = "Block size parsing returns expected strings")]
        [InlineData(0x800, "2kb")]      // 2KB
        [InlineData(0x8000, "32kb")]    // 32KB
        [InlineData(0x20000, "128kb")]  // 128KB
        [InlineData(0x100000, "1mb")]   // 1MB
        [InlineData(0x200000, "2mb")]   // 2MB
        public void ParseBlockSizeToString_ValidSizes_ReturnsExpectedStrings(int bytes, string expected)
        {
            // Act
            string result = ConfigSettingsFormatParser.ParseBlockSizeToString(bytes);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact(DisplayName = "Invalid block size returns default")]
        public void ParseBlockSizeToString_InvalidSize_ReturnsDefault()
        {
            // Act
            string result = ConfigSettingsFormatParser.ParseBlockSizeToString(0x3000); // Invalid size

            // Assert
            Assert.Equal("128kb", result); // Should return default
        }

        #endregion

        #region Performance Tests

        [Fact(DisplayName = "Format validation performs within reasonable time")]
        public void FormatValidation_PerformsWithinReasonableTime()
        {
            // Arrange
            Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string[] testFormats = {
                "rvz:zstd:19:128kb:16",
                "cue:split:bin:flac:sub", // Using flac instead of raw
                "cso:9:2kb:4",
                "zso::2kb:8",
                "wbfs:y",
                "iso"
            };

            // Act
            for (int i = 0; i < 1000; i++)
            {
                foreach (string format in testFormats)
                {
                    ValidationResult result = ConfigSettingsFormatValidator.ValidateFormatString(format);
                    Assert.NotNull(result);
                }
            }

            stopwatch.Stop();

            // Assert - 6000 validations should complete in under 5 seconds
            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                $"Format validation took {stopwatch.ElapsedMilliseconds}ms for 6000 validations, should be under 5 seconds");
        }

        [Fact(DisplayName = "Format parsing performs within reasonable time")]
        public void FormatParsing_PerformsWithinReasonableTime()
        {
            // Arrange
            Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (int i = 0; i < 1000; i++)
            {
                object rvzConfig = ConfigSettingsFormatParser.ParseFormatConfiguration("rvz:zstd:19:128kb:16", SystemType.GameCube);
                object cueConfig = ConfigSettingsFormatParser.ParseFormatConfiguration("cue:split:bin:flac:sub", SystemType.Dreamcast); // Using flac instead of raw
                ExtractConfiguration extractConfig = ConfigSettingsFormatParser.ParseExtractConfiguration("mi:*.iso");

                Assert.NotNull(rvzConfig);
                Assert.NotNull(cueConfig);
                Assert.NotNull(extractConfig);
            }

            stopwatch.Stop();

            // Assert - 3000 parses should complete in under 3 seconds
            Assert.True(stopwatch.ElapsedMilliseconds < 3000,
                $"Format parsing took {stopwatch.ElapsedMilliseconds}ms for 3000 parses, should be under 3 seconds");
        }

        #endregion

        #region Edge Cases Tests

        [Fact(DisplayName = "Unknown system block sizes fall back to RVZ default")]
        public void GetBlockSizes_UnknownSystem_ReturnsRvzDefault()
        {
            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes((SystemType)999);

            // Assert
            Assert.Equal(ConfigSettingsRanges.GetRvzBlockSizes(), blockSizes);
        }

        [Fact(DisplayName = "Unknown format with system uses system default")]
        public void GetDefaultBlockSize_UnknownFormat_UsesSystemDefault()
        {
            // Act
            string blockSize = ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.PS3, "unknownformat");

            // Assert - Should fall back to system default for PS3
            Assert.Equal("2kb", blockSize);
        }

        #endregion
    }
}