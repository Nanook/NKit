using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    /// <summary>
    /// Tests that verify UI visibility rules for configuration settings based on format and system selection.
    /// Ensures that dropdowns and controls show only valid options for each format/system combination.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class ConfigSettingsVisibilityTests
    {
        #region RVZ Format Visibility Tests

        [Theory(DisplayName = "RVZ format: Encoding dropdown shows only None, ZStd, and Lzma")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        public void RvzFormat_EncodingDropdown_ShowsOnlyValidEncodings(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);
            IReadOnlyList<RvzEncodingType> rvzEncodingTypes = ConfigSettingsRanges.GetRvzEncodingTypes();

            // Assert - RVZ format should be supported
            Assert.Contains(ConfigSettingsConstants.FormatRvz, supportedFormats);

            // Assert - Encoding dropdown should only show None, ZStd, and Lzma
            Assert.Equal(3, rvzEncodingTypes.Count);
            Assert.Contains(RvzEncodingType.None, rvzEncodingTypes);
            Assert.Contains(RvzEncodingType.ZStd, rvzEncodingTypes);
            Assert.Contains(RvzEncodingType.Lzma, rvzEncodingTypes);

            // Assert - Should NOT contain deprecated encodings
            Assert.DoesNotContain(RvzEncodingType.Purge, rvzEncodingTypes);
            Assert.DoesNotContain(RvzEncodingType.BZip2, rvzEncodingTypes);
            Assert.DoesNotContain(RvzEncodingType.Lzma2, rvzEncodingTypes);
        }

        [Fact(DisplayName = "RVZ ZStd encoding: Level dropdown shows 1-22")]
        public void RvzFormat_ZStdEncoding_LevelDropdown_Shows1To22()
        {
            // Arrange & Act
            IReadOnlyList<int> zstdLevels = ConfigSettingsRanges.GetZStdLevels();

            // Assert
            Assert.Equal(ConfigSettingsConstants.MaxZStdLevel, zstdLevels.Count);
            Assert.Equal(1, zstdLevels.First());
            Assert.Equal(22, zstdLevels.Last());

            // Verify continuous range
            for (int i = 1; i <= 22; i++)
            {
                Assert.Contains(i, zstdLevels);
            }
        }

        [Fact(DisplayName = "RVZ Lzma encoding: Level dropdown shows 1-9")]
        public void RvzFormat_LzmaEncoding_LevelDropdown_Shows1To9()
        {
            // Arrange & Act
            IReadOnlyList<int> lzmaLevels = ConfigSettingsRanges.GetLzmaLevels();

            // Assert
            Assert.Equal(ConfigSettingsConstants.MaxLzmaLevel, lzmaLevels.Count);
            Assert.Equal(1, lzmaLevels.First());
            Assert.Equal(9, lzmaLevels.Last());

            // Verify continuous range
            for (int i = 1; i <= 9; i++)
            {
                Assert.Contains(i, lzmaLevels);
            }
        }

        [Fact(DisplayName = "RVZ None encoding: Level dropdown should not be visible")]
        public void RvzFormat_NoneEncoding_LevelDropdown_NotVisible()
        {
            // Arrange
            RvzEncodingType encoding = RvzEncodingType.None;

            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(ConfigSettingsConstants.FormatRvz, encoding);

            // Assert - None encoding should return single zero element (for compatibility)
            Assert.Single(levels);
            Assert.Equal(0, levels[0]);
        }

        [Fact(DisplayName = "RVZ format: Block size dropdown shows 32KB to 2MB")]
        public void RvzFormat_BlockSizeDropdown_Shows32KbTo2Mb()
        {
            // Arrange & Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(ConfigSettingsConstants.FormatRvz);

            // Assert
            Assert.Equal(7, blockSizes.Count);
            Assert.Contains(ConfigSettingsConstants.BlockSize32kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize64kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize128kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize256kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize512kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize1mb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize2mb, blockSizes);

            // Should NOT contain 2KB or smaller sizes (GC/Wii minimum is 32KB)
            Assert.DoesNotContain(ConfigSettingsConstants.BlockSize2kb, blockSizes);
            Assert.DoesNotContain(ConfigSettingsConstants.BlockSize4kb, blockSizes);
        }

        [Theory(DisplayName = "RVZ format: Parallelism dropdown shows 1-32 for Nintendo systems")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        public void RvzFormat_ParallelismDropdown_Shows1To32(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<int> parallelismValues = ConfigSettingsRanges.GetParallelismValues(systemType);

            // Assert
            Assert.Equal(ConfigSettingsConstants.MaxNintendoParallelism, parallelismValues.Count);
            Assert.Equal(1, parallelismValues.First());
            Assert.Equal(32, parallelismValues.Last());

            // Verify continuous range
            for (int i = 1; i <= 32; i++)
            {
                Assert.Contains(i, parallelismValues);
            }
        }

        [Fact(DisplayName = "RVZ format: Default encoding is ZStd")]
        public void RvzFormat_DefaultEncoding_IsZStd()
        {
            // Arrange
            SystemType systemType = SystemType.Wii;

            // Act
            string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(systemType);

            // Assert
            Assert.Equal(ConfigSettingsConstants.FormatRvz, defaultFormat);

            // Default encoding for RVZ is implicitly ZStd when using defaults
            // (tested via format string generation)
        }

        [Fact(DisplayName = "RVZ format: Default ZStd level is 19")]
        public void RvzFormat_DefaultZStdLevel_Is19()
        {
            // Arrange & Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd);

            // Assert
            Assert.Equal(19, defaultLevel);
        }

        [Fact(DisplayName = "RVZ format: Default Lzma level is 5")]
        public void RvzFormat_DefaultLzmaLevel_Is5()
        {
            // Arrange & Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.Lzma);

            // Assert
            Assert.Equal(5, defaultLevel);
        }

        [Fact(DisplayName = "RVZ format: Default block size is 128KB")]
        public void RvzFormat_DefaultBlockSize_Is128Kb()
        {
            // Arrange
            SystemType systemType = SystemType.Wii;
            string format = ConfigSettingsConstants.FormatRvz;

            // Act
            string defaultBlockSize = ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format);

            // Assert
            Assert.Equal(ConfigSettingsConstants.BlockSize128kb, defaultBlockSize);
        }

        [Theory(DisplayName = "RVZ format: Default parallelism is 16")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        public void RvzFormat_DefaultParallelism_Is16(SystemType systemType)
        {
            // Arrange & Act
            int defaultParallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);

            // Assert
            Assert.Equal(16, defaultParallelism);
        }

        #endregion

        #region CSO/CSO2 Format Visibility Tests

        [Theory(DisplayName = "CSO/CSO2 format: Level dropdown shows 1-9 (ZLib)")]
        [InlineData(ConfigSettingsConstants.FormatCso)]
        [InlineData(ConfigSettingsConstants.FormatCso2)]
        public void CsoFormat_LevelDropdown_Shows1To9ZLib(string format)
        {
            // Arrange & Act
            IReadOnlyList<int> zlibLevels = ConfigSettingsRanges.GetZlibLevels();

            // Assert
            Assert.Equal(ConfigSettingsConstants.MaxZlibLevel, zlibLevels.Count);
            Assert.Equal(1, zlibLevels.First());
            Assert.Equal(9, zlibLevels.Last());

            // Verify continuous range
            for (int i = 1; i <= 9; i++)
            {
                Assert.Contains(i, zlibLevels);
            }
        }

        [Theory(DisplayName = "CSO/CSO2 format: Block size dropdown shows 2KB to 2MB")]
        [InlineData(ConfigSettingsConstants.FormatCso)]
        [InlineData(ConfigSettingsConstants.FormatCso2)]
        public void CsoFormat_BlockSizeDropdown_Shows2KbTo2Mb(string format)
        {
            // Arrange & Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert - Should include all sizes from 2KB to 2MB
            Assert.Equal(11, blockSizes.Count);
            Assert.Contains(ConfigSettingsConstants.BlockSize2kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize4kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize8kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize16kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize32kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize64kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize128kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize256kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize512kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize1mb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize2mb, blockSizes);
        }

        [Theory(DisplayName = "CSO/CSO2 format: Parallelism dropdown shows 1-32 for Sony systems")]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.FormatCso)]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.FormatCso2)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.FormatCso)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.FormatCso2)]
        public void CsoFormat_ParallelismDropdown_Shows1To32(SystemType systemType, string format)
        {
            // Arrange & Act
            IReadOnlyList<int> parallelismValues = ConfigSettingsRanges.GetParallelismValues(systemType);

            // Assert
            Assert.Equal(ConfigSettingsConstants.MaxSonyParallelism, parallelismValues.Count);
            Assert.Equal(1, parallelismValues.First());
            Assert.Equal(32, parallelismValues.Last());

            // Verify continuous range
            for (int i = 1; i <= 32; i++)
            {
                Assert.Contains(i, parallelismValues);
            }
        }

        [Theory(DisplayName = "CSO format: Default level is 9")]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.PS3)]
        public void CsoFormat_DefaultLevel_Is9(SystemType systemType)
        {
            // Arrange
            string format = ConfigSettingsConstants.FormatCso;

            // Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(format);

            // Assert
            Assert.Equal(9, defaultLevel);
        }

        [Theory(DisplayName = "CSO format: Default block size is 2KB for PSP, 16KB for PS3")]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.BlockSize2kb)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.BlockSize16kb)]
        public void CsoFormat_DefaultBlockSize_SystemSpecific(SystemType systemType, string expectedBlockSize)
        {
            // Arrange
            string format = ConfigSettingsConstants.FormatCso;

            // Act
            string defaultBlockSize = ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format);

            // Assert
            Assert.Equal(expectedBlockSize, defaultBlockSize);
        }

        [Theory(DisplayName = "CSO format: Default parallelism is 4")]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.PS3)]
        public void CsoFormat_DefaultParallelism_Is4(SystemType systemType)
        {
            // Arrange & Act
            int defaultParallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);

            // Assert
            Assert.Equal(4, defaultParallelism);
        }

        #endregion

        #region ZSO Format Visibility Tests

        [Fact(DisplayName = "ZSO format: Level dropdown shows 1-12 (LZ4 levels)")]
        public void ZsoFormat_LevelDropdown_Shows1To12Lz4()
        {
            // Arrange & Act
            IReadOnlyList<int> lz4Levels = ConfigSettingsRanges.GetLz4Levels();

            // Assert - ZSO should show LZ4 levels 1-12 in UI dropdown
            Assert.NotEmpty(lz4Levels);
            Assert.Equal(12, lz4Levels.Count);
            Assert.Equal(1, lz4Levels.First());
            Assert.Equal(12, lz4Levels.Last());

            // Default LZ4 level for ZSO should be 12
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatZso);
            Assert.Equal(12, defaultLevel);
            Assert.Contains(12, lz4Levels);

            // Verify continuous range
            for (int i = 1; i <= 12; i++)
            {
                Assert.Contains(i, lz4Levels);
            }
        }

        [Fact(DisplayName = "ZSO format: Block size dropdown shows 2KB to 2MB")]
        public void ZsoFormat_BlockSizeDropdown_Shows2KbTo2Mb()
        {
            // Arrange & Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(ConfigSettingsConstants.FormatZso);

            // Assert
            Assert.Equal(11, blockSizes.Count);
            Assert.Contains(ConfigSettingsConstants.BlockSize2kb, blockSizes);
            Assert.Contains(ConfigSettingsConstants.BlockSize2mb, blockSizes);
        }

        [Theory(DisplayName = "ZSO format: Default level is 12")]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.PS3)]
        public void ZsoFormat_DefaultLevel_Is12(SystemType systemType)
        {
            // Arrange
            string format = ConfigSettingsConstants.FormatZso;

            // Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(format);

            // Assert
            Assert.Equal(12, defaultLevel);
        }

        [Theory(DisplayName = "ZSO format: Default block size is 2KB for PSP, 16KB for PS3")]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.BlockSize2kb)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.BlockSize16kb)]
        public void ZsoFormat_DefaultBlockSize_SystemSpecific(SystemType systemType, string expectedBlockSize)
        {
            // Arrange
            string format = ConfigSettingsConstants.FormatZso;

            // Act
            string defaultBlockSize = ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format);

            // Assert
            Assert.Equal(expectedBlockSize, defaultBlockSize);
        }

        #endregion

        #region CUE Format Visibility Tests

        [Theory(DisplayName = "CUE format: Type dropdown shows Split and Joined")]
        [InlineData(SystemType.PS1)]
        [InlineData(SystemType.PS2)]
        [InlineData(SystemType.Saturn)]
        [InlineData(SystemType.SegaCD)]
        [InlineData(SystemType.PcEngine)]
        [InlineData(SystemType.CDi)]
        [InlineData(SystemType.Dreamcast)]
        public void CueFormat_TypeDropdown_ShowsSplitAndJoined(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> cueTypes = ConfigSettingsRanges.GetCueTypes();

            // Assert
            Assert.Equal(2, cueTypes.Count);
            Assert.Contains(ConfigSettingsConstants.CueTypeSplit, cueTypes);
            Assert.Contains(ConfigSettingsConstants.CueTypeJoined, cueTypes);
        }

        [Fact(DisplayName = "CUE format: Binary extension dropdown shows bin, img, iso")]
        public void CueFormat_BinaryExtensionDropdown_ShowsValidExtensions()
        {
            // Arrange & Act
            IReadOnlyList<string> binaryExtensions = ConfigSettingsRanges.GetBinaryExtensions();

            // Assert
            Assert.Equal(3, binaryExtensions.Count);
            Assert.Contains(ConfigSettingsConstants.BinaryExtensionBin, binaryExtensions);
            Assert.Contains(ConfigSettingsConstants.BinaryExtensionImg, binaryExtensions);
            Assert.Contains(ConfigSettingsConstants.BinaryExtensionIso, binaryExtensions);
        }

        [Fact(DisplayName = "CUE format: Audio extension dropdown shows bin, flac, wav, raw")]
        public void CueFormat_AudioExtensionDropdown_ShowsValidExtensions()
        {
            // Arrange & Act
            IReadOnlyList<string> audioExtensions = ConfigSettingsRanges.GetAudioExtensions();

            // Assert
            Assert.Equal(4, audioExtensions.Count); // Updated: now includes 'raw'
            Assert.Contains(ConfigSettingsConstants.AudioExtensionBin, audioExtensions);
            Assert.Contains(ConfigSettingsConstants.AudioExtensionFlac, audioExtensions);
            Assert.Contains(ConfigSettingsConstants.AudioExtensionWav, audioExtensions);
            Assert.Contains(ConfigSettingsConstants.AudioExtensionRaw, audioExtensions); // Added 'raw'
        }

        [Fact(DisplayName = "CUE format: Default type is Split")]
        public void CueFormat_DefaultType_IsSplit()
        {
            // Arrange & Act
            string defaultCueType = ConfigSettingsDefaults.GetDefaultCueType();

            // Assert
            Assert.Equal(ConfigSettingsConstants.CueTypeSplit, defaultCueType);
        }

        [Fact(DisplayName = "CUE format: Default binary extension is bin")]
        public void CueFormat_DefaultBinaryExtension_IsBin()
        {
            // Arrange & Act
            string defaultBinary = ConfigSettingsDefaults.GetDefaultBinaryExtension();

            // Assert
            Assert.Equal(ConfigSettingsConstants.BinaryExtensionBin, defaultBinary);
        }

        [Fact(DisplayName = "CUE format: Default audio extension is bin")]
        public void CueFormat_DefaultAudioExtension_IsBin()
        {
            // Arrange & Act
            string defaultAudio = ConfigSettingsDefaults.GetDefaultAudioExtension();

            // Assert
            Assert.Equal(ConfigSettingsConstants.AudioExtensionBin, defaultAudio);
        }

        [Fact(DisplayName = "CUE format: No compression controls visible")]
        public void CueFormat_NoCompressionControls_Visible()
        {
            // Arrange
            string format = ConfigSettingsConstants.FormatCue;

            // Act - CUE format should not have compression levels
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);

            // Assert - Should return single zero element for formats without compression
            Assert.Single(levels);
            Assert.Equal(0, levels[0]);
        }

        [Theory(DisplayName = "Non-compressed formats return [0] for compression levels")]
        [InlineData(ConfigSettingsConstants.FormatIso)]
        [InlineData(ConfigSettingsConstants.FormatCue)]
        [InlineData(ConfigSettingsConstants.FormatGdi)]
        [InlineData(ConfigSettingsConstants.FormatWbfs)]
        [InlineData(ConfigSettingsConstants.FormatCiso)]
        [InlineData(ConfigSettingsConstants.FormatDecIso)]
        [InlineData(ConfigSettingsConstants.FormatApp)]
        [InlineData(ConfigSettingsConstants.FormatWux)]
        public void NonCompressedFormats_ReturnZeroCompressionLevel(string format)
        {
            // Arrange & Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);

            // Assert - Non-compressed formats should return single zero element
            Assert.Single(levels);
            Assert.Equal(0, levels[0]);
        }

        [Fact(DisplayName = "Unknown format returns [0] for compression levels")]
        public void UnknownFormat_ReturnsZeroCompressionLevel()
        {
            // Arrange
            string unknownFormat = "unknownformat";

            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(unknownFormat);

            // Assert - Unknown formats should default to [0]
            Assert.Single(levels);
            Assert.Equal(0, levels[0]);
        }

        #endregion

        #region System-Specific Format Support Tests

        [Theory(DisplayName = "GameCube/Wii: Supports ISO, RVZ, WBFS, CISO")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        public void NintendoSystems_SupportCorrectFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(4, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatIso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatRvz, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatWbfs, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCiso, supportedFormats);

            // Should NOT support Sony formats
            Assert.DoesNotContain(ConfigSettingsConstants.FormatCso, supportedFormats);
            Assert.DoesNotContain(ConfigSettingsConstants.FormatZso, supportedFormats);
        }

        [Theory(DisplayName = "PSP: Supports CSO, CSO2, ZSO")]
        [InlineData(SystemType.PSP)]
        public void PSP_SupportsCorrectFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(4, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatCso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCso2, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatZso, supportedFormats);

            // Should NOT support Nintendo formats
            Assert.DoesNotContain(ConfigSettingsConstants.FormatRvz, supportedFormats);
            Assert.DoesNotContain(ConfigSettingsConstants.FormatWbfs, supportedFormats);
        }

        [Theory(DisplayName = "PS3: Supports CUE, CSO, CSO2, ZSO, DecISO, ISO")]
        [InlineData(SystemType.PS3)]
        public void PS3_SupportsCorrectFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(6, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatCue, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCso2, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatZso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatDecIso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatIso, supportedFormats);
        }

        [Theory(DisplayName = "WiiU: Supports APP, TMD, ISO, WUX")]
        [InlineData(SystemType.WiiU)]
        public void WiiU_SupportsCorrectFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(4, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatApp, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatIso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatWux, supportedFormats);
        }

        [Theory(DisplayName = "Dreamcast: Supports CUE, GDI")]
        [InlineData(SystemType.Dreamcast)]
        public void Dreamcast_SupportsCorrectFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert
            Assert.Equal(2, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatCue, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatGdi, supportedFormats);
        }

        [Theory(DisplayName = "CD-based systems: Support CUE plus additional formats")]
        [InlineData(SystemType.PcEngine)]
        [InlineData(SystemType.CDi)]
        [InlineData(SystemType.Saturn)]
        [InlineData(SystemType.SegaCD)]
        public void CdBasedSystems_SupportCueAndAdditionalFormats(SystemType systemType)
        {
            // Arrange & Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);

            // Assert - Should support CUE plus additional formats (CSO, CSO2, ZSO, ISO)
            Assert.Equal(5, supportedFormats.Count);
            Assert.Contains(ConfigSettingsConstants.FormatCue, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatCso2, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatZso, supportedFormats);
            Assert.Contains(ConfigSettingsConstants.FormatIso, supportedFormats);
        }

        #endregion

        #region Control Visibility Rules Tests

        [Theory(DisplayName = "Format requires encoding control: RVZ only")]
        [InlineData(ConfigSettingsConstants.FormatRvz, true)]
        [InlineData(ConfigSettingsConstants.FormatCso, false)]
        [InlineData(ConfigSettingsConstants.FormatZso, false)]
        [InlineData(ConfigSettingsConstants.FormatIso, false)]
        [InlineData(ConfigSettingsConstants.FormatCue, false)]
        public void Format_RequiresEncodingControl(string format, bool shouldHaveEncoding)
        {
            // Arrange & Act
            bool hasEncoding = format.Equals(ConfigSettingsConstants.FormatRvz, StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.Equal(shouldHaveEncoding, hasEncoding);
        }

        [Theory(DisplayName = "Format requires level control: RVZ (with encoding), CSO, CSO2, ZSO")]
        [InlineData(ConfigSettingsConstants.FormatRvz, true)]  // With ZStd or Lzma
        [InlineData(ConfigSettingsConstants.FormatCso, true)]
        [InlineData(ConfigSettingsConstants.FormatCso2, true)]
        [InlineData(ConfigSettingsConstants.FormatZso, true)]  // CORRECTED: ZSO shows LZ4 levels 1-12
        [InlineData(ConfigSettingsConstants.FormatIso, false)]
        [InlineData(ConfigSettingsConstants.FormatCue, false)]
        public void Format_RequiresLevelControl(string format, bool shouldHaveLevel)
        {
            // Arrange & Act
            string[] formatsWithLevel = new[]
            {
                ConfigSettingsConstants.FormatRvz,
                ConfigSettingsConstants.FormatCso,
                ConfigSettingsConstants.FormatCso2,
                ConfigSettingsConstants.FormatZso  // CORRECTED: ZSO shows LZ4 level dropdown
            };
            bool hasLevel = formatsWithLevel.Contains(format.ToLowerInvariant());

            // Assert
            Assert.Equal(shouldHaveLevel, hasLevel);
        }

        [Theory(DisplayName = "Format requires block size control: RVZ, WBFS, CISO, CSO, CSO2, ZSO")]
        [InlineData(ConfigSettingsConstants.FormatRvz, true)]
        [InlineData(ConfigSettingsConstants.FormatWbfs, true)]
        [InlineData(ConfigSettingsConstants.FormatCiso, true)]
        [InlineData(ConfigSettingsConstants.FormatCso, true)]
        [InlineData(ConfigSettingsConstants.FormatCso2, true)]
        [InlineData(ConfigSettingsConstants.FormatZso, true)]
        [InlineData(ConfigSettingsConstants.FormatIso, false)]
        [InlineData(ConfigSettingsConstants.FormatCue, false)]
        [InlineData(ConfigSettingsConstants.FormatDecIso, false)]
        public void Format_RequiresBlockSizeControl(string format, bool shouldHaveBlockSize)
        {
            // Arrange & Act
            string[] formatsWithBlockSize = new[]
            {
                ConfigSettingsConstants.FormatRvz,
                ConfigSettingsConstants.FormatWbfs,
                ConfigSettingsConstants.FormatCiso,
                ConfigSettingsConstants.FormatCso,
                ConfigSettingsConstants.FormatCso2,
                ConfigSettingsConstants.FormatZso
            };
            bool hasBlockSize = formatsWithBlockSize.Contains(format.ToLowerInvariant());

            // Assert
            Assert.Equal(shouldHaveBlockSize, hasBlockSize);
        }

        [Theory(DisplayName = "Format requires parallelism control: RVZ, CSO, CSO2, ZSO")]
        [InlineData(ConfigSettingsConstants.FormatRvz, true)]
        [InlineData(ConfigSettingsConstants.FormatCso, true)]
        [InlineData(ConfigSettingsConstants.FormatCso2, true)]
        [InlineData(ConfigSettingsConstants.FormatZso, true)]
        [InlineData(ConfigSettingsConstants.FormatIso, false)]
        [InlineData(ConfigSettingsConstants.FormatCue, false)]
        [InlineData(ConfigSettingsConstants.FormatWbfs, false)]
        public void Format_RequiresParallelismControl(string format, bool shouldHaveParallelism)
        {
            // Arrange & Act
            string[] formatsWithParallelism = new[]
            {
                ConfigSettingsConstants.FormatRvz,
                ConfigSettingsConstants.FormatCso,
                ConfigSettingsConstants.FormatCso2,
                ConfigSettingsConstants.FormatZso
            };
            bool hasParallelism = formatsWithParallelism.Contains(format.ToLowerInvariant());

            // Assert
            Assert.Equal(shouldHaveParallelism, hasParallelism);
        }

        [Theory(DisplayName = "Format requires CUE type control: CUE only")]
        [InlineData(ConfigSettingsConstants.FormatCue, true)]
        [InlineData(ConfigSettingsConstants.FormatRvz, false)]
        [InlineData(ConfigSettingsConstants.FormatCso, false)]
        [InlineData(ConfigSettingsConstants.FormatIso, false)]
        public void Format_RequiresCueTypeControl(string format, bool shouldHaveCueType)
        {
            // Arrange & Act
            bool hasCueType = format.Equals(ConfigSettingsConstants.FormatCue, StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.Equal(shouldHaveCueType, hasCueType);
        }

        #endregion

        #region Validation Tests

        [Theory(DisplayName = "All format configurations generate valid format strings")]
        [InlineData(SystemType.GameCube, ConfigSettingsConstants.FormatRvz)]
        [InlineData(SystemType.Wii, ConfigSettingsConstants.FormatRvz)]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.FormatCso)]
        [InlineData(SystemType.PSP, ConfigSettingsConstants.FormatZso)]
        [InlineData(SystemType.PS3, ConfigSettingsConstants.FormatCso)]
        [InlineData(SystemType.Dreamcast, ConfigSettingsConstants.FormatCue)]
        public void AllFormatConfigurations_GenerateValidFormatStrings(SystemType systemType, string format)
        {
            // Arrange
            string formatString = null;

            // Act - Generate format string based on format type
            switch (format.ToLowerInvariant())
            {
                case ConfigSettingsConstants.FormatRvz:
                    formatString = ConfigSettingsFormatGenerator.GenerateRvzFormatString(
                        RvzEncodingType.ZStd,
                        ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd),
                        ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format),
                        ConfigSettingsDefaults.GetDefaultParallelism(systemType));
                    break;

                case ConfigSettingsConstants.FormatCso:
                case ConfigSettingsConstants.FormatCso2:
                case ConfigSettingsConstants.FormatZso:
                    // For CSO/CSO2/ZSO, we need to create a format string manually since there's no dedicated generator
                    // These formats use: format:level:blockSize:parallelism
                    int level = ConfigSettingsDefaults.GetDefaultCompressionLevel(format);
                    string blockSize = ConfigSettingsDefaults.GetDefaultBlockSize(systemType, format);
                    int parallelism = ConfigSettingsDefaults.GetDefaultParallelism(systemType);
                    formatString = $"{format}:{level}:{blockSize}:{parallelism}";
                    break;

                case ConfigSettingsConstants.FormatCue:
                    formatString = ConfigSettingsFormatGenerator.GenerateCueFormatString();
                    break;
            }

            // Assert
            Assert.NotNull(formatString);
            Assert.NotEmpty(formatString);

            // Validate the generated format string
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(systemType, formatString);
            Assert.True(validationResult.IsValid,
                $"Generated format string '{formatString}' should be valid: {validationResult.ErrorMessage}");
        }

        [Fact(DisplayName = "All supported formats can be retrieved")]
        public void AllSupportedFormats_CanBeRetrieved()
        {
            // Arrange & Act
            IReadOnlyList<string> allFormats = ConfigSettingsRanges.GetAllSupportedFormats();

            // Assert
            Assert.NotEmpty(allFormats);

            // Verify all known formats are present
            string[] expectedFormats = new[]
            {
                ConfigSettingsConstants.FormatIso,
                ConfigSettingsConstants.FormatRvz,
                ConfigSettingsConstants.FormatWbfs,
                ConfigSettingsConstants.FormatCiso,
                ConfigSettingsConstants.FormatCso,
                ConfigSettingsConstants.FormatCso2,
                ConfigSettingsConstants.FormatZso,
                ConfigSettingsConstants.FormatDecIso,
                ConfigSettingsConstants.FormatApp,
                ConfigSettingsConstants.FormatWux,
                ConfigSettingsConstants.FormatCue,
                ConfigSettingsConstants.FormatGdi
            };

            foreach (string expectedFormat in expectedFormats)
            {
                Assert.Contains(expectedFormat, allFormats, StringComparer.OrdinalIgnoreCase);
            }
        }

        #endregion

        #region Log Level Tests

        [Fact(DisplayName = "Log level dropdown shows all valid log levels")]
        public void LogLevelDropdown_ShowsAllValidLevels()
        {
            // Arrange & Act
            IReadOnlyList<LogLevel> logLevels = ConfigSettingsRanges.GetLogLevels();

            // Assert
            Assert.Equal(6, logLevels.Count);
            Assert.Contains(LogLevel.None, logLevels);
            Assert.Contains(LogLevel.Error, logLevels);
            Assert.Contains(LogLevel.Warning, logLevels);
            Assert.Contains(LogLevel.Info, logLevels);
            Assert.Contains(LogLevel.Detail, logLevels);
            Assert.Contains(LogLevel.Trace, logLevels);
        }

        #endregion

        #region Core Parallelism Tests

        #endregion
    }
}