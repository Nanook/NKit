using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    /// <summary>
    /// Comprehensive tests that validate the format support tables for NKit 2 convert functionality.
    /// These tests ensure the ConfigSettingsRanges class returns the correct formats, compression levels,
    /// block sizes, and dual format support for each system type.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class ConfigSettingsFormatSupportTests
    {
        private readonly ITestOutputHelper _output;

        public ConfigSettingsFormatSupportTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region System Format Support Tests

        [Theory]
        [InlineData(SystemType.GameCube, new[] { "iso", "rvz", "wbfs", "ciso" }, false)]
        [InlineData(SystemType.Wii, new[] { "iso", "rvz", "wbfs", "ciso" }, false)]
        public void Nintendo_Systems_SupportCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        [Theory]
        [InlineData(SystemType.PS3, new[] { "cue", "cso", "cso2", "zso", "deciso", "iso" }, true)]
        [InlineData(SystemType.PSP, new[] { "cso", "cso2", "zso", "iso" }, false)]
        [InlineData(SystemType.PS1, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        [InlineData(SystemType.PS2, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        public void Sony_Systems_SupportCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        [Theory]
        [InlineData(SystemType.XBox, new[] { "iso", "cso", "cso2", "zso" }, false)]
        [InlineData(SystemType.XBox360, new[] { "iso", "cso", "cso2", "zso" }, false)]
        public void Microsoft_Systems_SupportCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        [Theory]
        [InlineData(SystemType.WiiU, new[] { "app", "tmd", "iso", "wux" }, false)]
        public void WiiU_System_SupportsCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        [Theory]
        [InlineData(SystemType.Dreamcast, new[] { "cue", "gdi" }, false)]
        public void Dreamcast_System_SupportsCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        [Theory]
        [InlineData(SystemType.PcEngine, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        [InlineData(SystemType.CDi, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        [InlineData(SystemType.Saturn, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        [InlineData(SystemType.SegaCD, new[] { "cue", "cso", "cso2", "zso", "iso" }, true)]
        public void CD_Based_Systems_SupportCorrectFormats(SystemType system, string[] expectedFormats, bool supportsDualFormat)
        {
            // Act
            IReadOnlyList<string> actualFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Equal(expectedFormats.Length, actualFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, actualFormats);
            }

            _output.WriteLine($"{system}: [{string.Join(", ", actualFormats)}] - Dual Format: {supportsDualFormat}");
        }

        #endregion

        #region Format Compression Tests

        [Theory]
        [InlineData("rvz", RvzEncodingType.ZStd, 1, 22, 19)]
        [InlineData("rvz", RvzEncodingType.Lzma, 1, 9, 5)]
        [InlineData("rvz", RvzEncodingType.None, 0, 0, 0)]
        public void RVZ_Format_SupportsCorrectCompressionLevels(string format, RvzEncodingType encoding, int minLevel, int maxLevel, int defaultLevel)
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format, encoding);

            // Assert
            if (encoding == RvzEncodingType.None)
            {
                Assert.Single(levels);
                Assert.Equal(0, levels.First());
            }
            else
            {
                Assert.Equal(maxLevel - minLevel + 1, levels.Count);
                Assert.Equal(minLevel, levels.First());
                Assert.Equal(maxLevel, levels.Last());
                Assert.Contains(defaultLevel, levels);
            }

            _output.WriteLine($"RVZ {encoding}: [{string.Join(", ", levels.Take(3))}...{levels.LastOrDefault()}] (Default: {defaultLevel})");
        }

        [Theory]
        [InlineData("cso", 1, 9, 9)]
        [InlineData("cso2", 1, 9, 9)]
        public void CSO_Formats_SupportCorrectCompressionLevels(string format, int minLevel, int maxLevel, int defaultLevel)
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);

            // Assert
            Assert.Equal(maxLevel - minLevel + 1, levels.Count);
            Assert.Equal(minLevel, levels.First());
            Assert.Equal(maxLevel, levels.Last());
            Assert.Contains(defaultLevel, levels);

            _output.WriteLine($"{format.ToUpper()}: [{string.Join(", ", levels)}] (Default: {defaultLevel})");
        }

        [Theory]
        [InlineData("zso", 1, 12, 12)]
        public void ZSO_Format_SupportsCorrectCompressionLevels(string format, int minLevel, int maxLevel, int defaultLevel)
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);

            // Assert
            Assert.Equal(maxLevel - minLevel + 1, levels.Count);
            Assert.Equal(minLevel, levels.First());
            Assert.Equal(maxLevel, levels.Last());
            Assert.Contains(defaultLevel, levels);

            _output.WriteLine($"{format.ToUpper()}: [{string.Join(", ", levels)}] (Default: {defaultLevel})");
        }

        [Theory]
        [InlineData("iso")]
        [InlineData("cue")]
        [InlineData("gdi")]
        [InlineData("wbfs")]
        [InlineData("ciso")]
        [InlineData("deciso")]
        [InlineData("app")]
        [InlineData("wux")]
        public void Uncompressed_Formats_ReturnZeroLevel(string format)
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);

            // Assert
            Assert.Single(levels);
            Assert.Equal(0, levels.First());

            _output.WriteLine($"{format.ToUpper()}: No compression levels (returns [0])");
        }

        #endregion

        #region Block Size Tests

        [Theory]
        [InlineData("rvz", new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("wbfs", new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("ciso", new[] { "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        public void RVZ_Category_Formats_SupportCorrectBlockSizes(string format, string[] expectedSizes)
        {
            // Act
            IReadOnlyList<string> actualSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Equal(expectedSizes.Length, actualSizes.Count);
            foreach (string size in expectedSizes)
            {
                Assert.Contains(size, actualSizes);
            }

            _output.WriteLine($"{format.ToUpper()}: [{string.Join(", ", actualSizes)}]");
        }

        [Theory]
        [InlineData("cso", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("cso2", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        [InlineData("zso", new[] { "2kb", "4kb", "8kb", "16kb", "32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb" })]
        public void CSO_Category_Formats_SupportCorrectBlockSizes(string format, string[] expectedSizes)
        {
            // Act
            IReadOnlyList<string> actualSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Equal(expectedSizes.Length, actualSizes.Count);
            foreach (string size in expectedSizes)
            {
                Assert.Contains(size, actualSizes);
            }

            _output.WriteLine($"{format.ToUpper()}: [{string.Join(", ", actualSizes)}]");
        }

        [Theory]
        [InlineData("iso")]
        [InlineData("cue")]
        [InlineData("gdi")]
        [InlineData("deciso")]
        [InlineData("app")]
        [InlineData("wux")]
        public void Uncompressed_Formats_HaveNoBlockSizes(string format)
        {
            // Act
            IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);

            // Assert
            Assert.Empty(blockSizes);

            _output.WriteLine($"{format.ToUpper()}: No block sizes (uncompressed format)");
        }

        #endregion

        #region Parallelism Tests

        [Theory]
        [InlineData(SystemType.GameCube, 32, 16)]
        [InlineData(SystemType.Wii, 32, 16)]
        public void Nintendo_Systems_SupportCorrectParallelism(SystemType system, int maxThreads, int defaultThreads)
        {
            // Act
            IReadOnlyList<int> parallelismValues = ConfigSettingsRanges.GetParallelismValues(system);

            // Assert
            Assert.Equal(maxThreads, parallelismValues.Count);
            Assert.Equal(1, parallelismValues.First());
            Assert.Equal(maxThreads, parallelismValues.Last());
            Assert.Contains(defaultThreads, parallelismValues);

            _output.WriteLine($"{system}: 1-{maxThreads} threads (Default: {defaultThreads})");
        }

        [Theory]
        [InlineData(SystemType.PS3, 32, 4)]
        [InlineData(SystemType.PSP, 32, 4)]
        [InlineData(SystemType.XBox, 32, 4)]
        [InlineData(SystemType.XBox360, 32, 4)]
        public void Sony_Microsoft_Systems_SupportCorrectParallelism(SystemType system, int maxThreads, int defaultThreads)
        {
            // Act
            IReadOnlyList<int> parallelismValues = ConfigSettingsRanges.GetParallelismValues(system);

            // Assert
            Assert.Equal(maxThreads, parallelismValues.Count);
            Assert.Equal(1, parallelismValues.First());
            Assert.Equal(maxThreads, parallelismValues.Last());
            Assert.Contains(defaultThreads, parallelismValues);

            _output.WriteLine($"{system}: 1-{maxThreads} threads (Default: {defaultThreads})");
        }

        #endregion

        #region Dual Format CUE Tests

        [Fact]
        public void CUE_Configuration_SupportsCorrectOptions()
        {
            // Act
            IReadOnlyList<string> cueTypes = ConfigSettingsRanges.GetCueTypes();
            IReadOnlyList<string> binaryExtensions = ConfigSettingsRanges.GetBinaryExtensions();
            IReadOnlyList<string> audioExtensions = ConfigSettingsRanges.GetAudioExtensions();

            // Assert
            Assert.Equal(2, cueTypes.Count);
            Assert.Contains("split", cueTypes);
            Assert.Contains("joined", cueTypes);

            Assert.Equal(3, binaryExtensions.Count);
            Assert.Contains("bin", binaryExtensions);
            Assert.Contains("img", binaryExtensions);
            Assert.Contains("iso", binaryExtensions);

            Assert.Equal(4, audioExtensions.Count);
            Assert.Contains("bin", audioExtensions);
            Assert.Contains("wav", audioExtensions);
            Assert.Contains("flac", audioExtensions);
            Assert.Contains("raw", audioExtensions);

            _output.WriteLine($"CUE Types: [{string.Join(", ", cueTypes)}]");
            _output.WriteLine($"Binary Extensions: [{string.Join(", ", binaryExtensions)}]");
            _output.WriteLine($"Audio Extensions: [{string.Join(", ", audioExtensions)}]");
        }

        #endregion

        #region Dual Format Support Tests

        [Theory]
        [InlineData(SystemType.PS3, "cso", "cue")]
        [InlineData(SystemType.PS3, "zso", "cue")]
        [InlineData(SystemType.PS1, "cso", "cue")]
        [InlineData(SystemType.PS1, "zso", "cue")]
        [InlineData(SystemType.PS2, "cso", "cue")]
        [InlineData(SystemType.PS2, "zso", "cue")]
        public void Dual_Format_Systems_SupportBothFormats(SystemType system, string singleFormat, string indexedFormat)
        {
            // Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert
            Assert.Contains(singleFormat, supportedFormats);
            Assert.Contains(indexedFormat, supportedFormats);

            _output.WriteLine($"{system}: Supports dual format {singleFormat}/{indexedFormat}");
        }

        [Theory]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        [InlineData(SystemType.PSP)]
        [InlineData(SystemType.XBox)]
        [InlineData(SystemType.XBox360)]
        [InlineData(SystemType.WiiU)]
        [InlineData(SystemType.Dreamcast)]
        public void Single_Format_Systems_DoNotSupportDualFormats(SystemType system)
        {
            // Act
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(system);

            // Assert - These systems should not have both compression and cue formats
            bool hasCompressionFormat = supportedFormats.Any(f => f == "cso" || f == "zso" || f == "rvz");
            bool hasCueFormat = supportedFormats.Contains("cue");

            if (hasCompressionFormat && hasCueFormat)
            {
                Assert.Fail($"{system} should not support dual formats but has both compression and cue formats");
            }

            _output.WriteLine($"{system}: Single format system - {string.Join(", ", supportedFormats)}");
        }

        #endregion

        #region Comprehensive Format Matrix Test

        [Fact]
        public void Complete_Format_Support_Matrix_IsCorrect()
        {
            _output.WriteLine("=== Complete NKit 2 Convert Format Support Matrix ===");
            _output.WriteLine("");

            SystemType[] allSystems = new[]
            {
                SystemType.GameCube, SystemType.Wii, SystemType.PS3, SystemType.PSP,
                SystemType.PS1, SystemType.PS2, SystemType.XBox, SystemType.XBox360,
                SystemType.WiiU, SystemType.Dreamcast, SystemType.PcEngine, SystemType.CDi,
                SystemType.Saturn, SystemType.SegaCD
            };

            foreach (SystemType system in allSystems)
            {
                IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats(system);
                IReadOnlyList<int> parallelism = ConfigSettingsRanges.GetParallelismValues(system);

                bool isDualFormat = IsDualFormatSystem(system, formats);

                _output.WriteLine($"{system,-12}: {string.Join(", ", formats),-30} | Threads: 1-{parallelism.Count} | Dual: {(isDualFormat ? "YES" : "NO")}");
            }

            _output.WriteLine("");
            _output.WriteLine("=== Format Compression Details ===");

            string[] compressionFormats = new[] { "rvz", "cso", "cso2", "zso" };
            foreach (string format in compressionFormats)
            {
                IReadOnlyList<string> blockSizes = ConfigSettingsRanges.GetBlockSizes(format);
                string blockSizeRange = blockSizes.Count > 0 ? $"{blockSizes.First()}-{blockSizes.Last()}" : "N/A";

                if (format == "rvz")
                {
                    IReadOnlyList<int> zstdLevels = ConfigSettingsRanges.GetCompressionLevels(format, RvzEncodingType.ZStd);
                    IReadOnlyList<int> lzmaLevels = ConfigSettingsRanges.GetCompressionLevels(format, RvzEncodingType.Lzma);
                    _output.WriteLine($"{format.ToUpper(),-6}: ZStd(1-{zstdLevels.Last()}), LZMA(1-{lzmaLevels.Last()}), None | Blocks: {blockSizeRange}");
                }
                else
                {
                    IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels(format);
                    string levelRange = levels.Count > 1 ? $"1-{levels.Last()}" : "N/A";
                    string compressionType = format == "zso" ? "LZ4" : "ZLib";
                    _output.WriteLine($"{format.ToUpper(),-6}: {compressionType}({levelRange}) | Blocks: {blockSizeRange}");
                }
            }

            // Verify all systems have at least one format
            foreach (SystemType system in allSystems)
            {
                IReadOnlyList<string> formats = ConfigSettingsRanges.GetSupportedFormats(system);
                Assert.NotEmpty(formats);
            }
        }

        private bool IsDualFormatSystem(SystemType system, IReadOnlyList<string> formats)
        {
            // Dual format systems support both compression and cue formats
            return system == SystemType.PS3 || system == SystemType.PS1 || system == SystemType.PS2 ||
                   system == SystemType.PcEngine || system == SystemType.CDi || system == SystemType.Saturn || system == SystemType.SegaCD;
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void All_Supported_Formats_AreValid()
        {
            // Act
            IReadOnlyList<string> allFormats = ConfigSettingsRanges.GetAllSupportedFormats();

            // Assert
            string[] expectedFormats = new[]
            {
                "app", "tmd", "ciso", "cso", "cso2", "cue", "deciso", "gdi", "iso", "rvz", "wbfs", "wux", "zso"
            };

            Assert.Equal(expectedFormats.Length, allFormats.Count);
            foreach (string format in expectedFormats)
            {
                Assert.Contains(format, allFormats);
            }

            _output.WriteLine($"All supported formats: [{string.Join(", ", allFormats)}]");
        }

        [Fact]
        public void RVZ_Encoding_Types_AreComplete()
        {
            // Act
            IReadOnlyList<RvzEncodingType> encodingTypes = ConfigSettingsRanges.GetRvzEncodingTypes();

            // Assert
            Assert.Equal(3, encodingTypes.Count);
            Assert.Contains(RvzEncodingType.None, encodingTypes);
            Assert.Contains(RvzEncodingType.ZStd, encodingTypes);
            Assert.Contains(RvzEncodingType.Lzma, encodingTypes);

            _output.WriteLine($"RVZ encodings: [{string.Join(", ", encodingTypes)}]");
        }

        #endregion
    }
}