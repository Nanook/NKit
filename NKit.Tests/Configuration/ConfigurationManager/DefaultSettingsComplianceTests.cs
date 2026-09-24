using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests that verify ConfigurationManager creates defaults that comply with 
    /// NKitConfigurationProvider validation for all system types.
    /// Ensures that all generated configuration strings are valid and optimal.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class DefaultSettingsComplianceTests
    {
        #region Test Data

        public static IEnumerable<object[]> AllCombinations =>
            PlatformModeDetectionTests.AllCombinations;

        #endregion

        #region UI Defaults Validation Tests

        [Theory(DisplayName = "UI defaults pass provider validation for all systems")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        [InlineData(SystemType.PS3)]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.Dreamcast)]
        [InlineData(SystemType.Saturn)]
        [InlineData(SystemType.SegaCD)]
        [InlineData(SystemType.Default)]
        public void UiDefaults_PassProviderValidation_ForAllSystems(SystemType systemType)
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(systemType);

            // Assert - All defaults should pass validation

            // 1. Format validation
            ValidationResult formatValidation = ConfigSettingsFormatValidator.ValidateFormatString(uiDefaults.Conversion.Format);
            Assert.True(formatValidation.IsValid,
                $"Format '{uiDefaults.Conversion.Format}' for {systemType} should be valid: {formatValidation.ErrorMessage}");

            // 2. Format is supported by system
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);
            Assert.Contains(uiDefaults.Conversion.Format, supportedFormats);

            // 3. Block size validation (for formats that support it)
            string format = uiDefaults.Conversion.Format?.ToLowerInvariant();
            string[] formatsWithBlockSize = new[] {
               ConfigSettingsConstants.FormatRvz,
               ConfigSettingsConstants.FormatWbfs,
               ConfigSettingsConstants.FormatCiso,
               ConfigSettingsConstants.FormatCso,
               ConfigSettingsConstants.FormatCso2,
               ConfigSettingsConstants.FormatZso
            };

            if (formatsWithBlockSize.Contains(format))
            {
                IReadOnlyList<string> supportedBlockSizes = ConfigSettingsRanges.GetBlockSizes(format);
                Assert.Contains(uiDefaults.Conversion.BlockSize, supportedBlockSizes);
            }

            // 4. Parallelism validation
            int parallelism = int.Parse(uiDefaults.Conversion.Parallelism);
            IReadOnlyList<int> parallelismValues = ConfigSettingsRanges.GetParallelismValues(systemType);
            Assert.Contains(parallelism, parallelismValues);

            // 5. Extract type validation
            IReadOnlyList<string> extractTypes = ConfigSettingsRanges.GetExtractTypes();
            Assert.Contains(uiDefaults.Extraction.Type, extractTypes);
        }

        #endregion

        #region System-Specific Format Tests

        [Theory(DisplayName = "System defaults match expected formats")]
        [InlineData(SystemType.GameCube, ConfigSettingsConstants.FormatRvz)]
        [InlineData(SystemType.Wii, ConfigSettingsConstants.FormatRvz)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.FormatDecIso)]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.FormatCso)]
        [InlineData(SystemType.Dreamcast, ConfigSettingsConstants.FormatCue)]
        public void SystemDefaults_MatchExpectedFormats(SystemType systemType, string expectedFormat)
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(systemType);
            string providerDefault = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            // Assert - ConfigurationManager and Provider should agree
            Assert.Equal(expectedFormat, uiDefaults.Conversion.Format);
            Assert.Equal(expectedFormat, providerDefault);
        }

        #endregion

        #region Format-Specific Validation Tests

        [Fact(DisplayName = "RVZ defaults create valid format string")]
        public void RvzDefaults_CreateValidFormatString()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act - Get RVZ defaults (Wii uses RVZ)
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(SystemType.Wii);

            // Assert - Should be RVZ with valid configuration
            Assert.Equal(ConfigSettingsConstants.FormatRvz, uiDefaults.Conversion.Format);

            // Build complete RVZ format string
            string rvzString = $"{uiDefaults.Conversion.Format}:{uiDefaults.Conversion.Encoding}:{uiDefaults.Conversion.Level}:{uiDefaults.Conversion.BlockSize}:{uiDefaults.Conversion.Parallelism}";

            // Validate the complete string
            ValidationResult validation = ConfigSettingsFormatValidator.ValidateRvzFormat(rvzString);
            Assert.True(validation.IsValid,
                $"Generated RVZ string '{rvzString}' should be valid: {validation.ErrorMessage}");

            // Verify individual components
            string[] validEncodings = new[] {
               ConfigSettingsConstants.EncodingZStd,
               ConfigSettingsConstants.EncodingLzma,
               ConfigSettingsConstants.EncodingNone
            };
            Assert.Contains(uiDefaults.Conversion.Encoding, validEncodings);
            Assert.Contains(uiDefaults.Conversion.BlockSize, ConfigSettingsRanges.GetRvzBlockSizes());

            // Validate compression level based on encoding
            int level = int.Parse(uiDefaults.Conversion.Level);
            if (uiDefaults.Conversion.Encoding == ConfigSettingsConstants.EncodingZStd)
            {
                Assert.InRange(level,
                   ConfigSettingsConstants.MinZStdLevel,
                   ConfigSettingsConstants.MaxZStdLevel);
            }
            else if (uiDefaults.Conversion.Encoding == ConfigSettingsConstants.EncodingLzma)
            {
                Assert.InRange(level,
                   ConfigSettingsConstants.MinLzmaLevel,
                   ConfigSettingsConstants.MaxLzmaLevel);
            }
        }

        [Fact(DisplayName = "CSO defaults create valid format string")]
        public void CsoDefaults_CreateValidFormatString()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act - Get CSO defaults (PSP uses CSO)
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(SystemType.PSP);

            // Assert - Should be CSO with valid configuration
            Assert.Equal(ConfigSettingsConstants.FormatCso, uiDefaults.Conversion.Format);

            // Build complete CSO format string
            string csoString = $"{uiDefaults.Conversion.Format}:{uiDefaults.Conversion.Level}:{uiDefaults.Conversion.BlockSize}:{uiDefaults.Conversion.Parallelism}";

            // Validate the complete string
            ValidationResult validation = ConfigSettingsFormatValidator.ValidateCsoFormat(csoString);
            Assert.True(validation.IsValid,
                $"Generated CSO string '{csoString}' should be valid: {validation.ErrorMessage}");

            // Verify individual components
            Assert.Contains(uiDefaults.Conversion.BlockSize, ConfigSettingsRanges.GetCsoBlockSizes());

            int level = int.Parse(uiDefaults.Conversion.Level);
            Assert.InRange(level,
               ConfigSettingsConstants.MinZlibLevel,
               ConfigSettingsConstants.MaxZlibLevel);
        }

        [Fact(DisplayName = "CUE defaults create valid format string")]
        public void CueDefaults_CreateValidFormatString()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act - Get CUE defaults (Dreamcast uses CUE)
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(SystemType.Dreamcast);

            // Assert - Should be CUE with valid configuration
            Assert.Equal(ConfigSettingsConstants.FormatCue, uiDefaults.Conversion.Format);

            // Build complete CUE format string
            string cueString = $"{uiDefaults.Conversion.Format}:{uiDefaults.Conversion.CueType}:{uiDefaults.Conversion.Binary}:{uiDefaults.Conversion.Audio}";

            // Validate the complete string
            ValidationResult validation = ConfigSettingsFormatValidator.ValidateCueFormat(cueString);
            Assert.True(validation.IsValid,
                $"Generated CUE string '{cueString}' should be valid: {validation.ErrorMessage}");

            // Verify individual components
            string[] validCueTypes = new[] {
               ConfigSettingsConstants.CueTypeSplit,
               ConfigSettingsConstants.CueTypeJoined
            };
            Assert.Contains(uiDefaults.Conversion.CueType, validCueTypes);

            string[] validBinaryExt = new[] {
               ConfigSettingsConstants.BinaryExtensionBin,
               ConfigSettingsConstants.BinaryExtensionImg,
               ConfigSettingsConstants.BinaryExtensionIso
            };
            Assert.Contains(uiDefaults.Conversion.Binary, validBinaryExt);

            string[] validAudioExt = new[] {
               ConfigSettingsConstants.AudioExtensionBin,
               ConfigSettingsConstants.AudioExtensionRaw
            };
            Assert.Contains(uiDefaults.Conversion.Audio, validAudioExt);
        }

        #endregion
    }
}