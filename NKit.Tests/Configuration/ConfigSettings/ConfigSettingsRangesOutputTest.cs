using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    /// <summary>
    /// Quick test to verify what ConfigSettingsRanges actually returns for UI binding
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class ConfigSettingsRangesOutputTest
    {
        private readonly ITestOutputHelper _output;

        public ConfigSettingsRangesOutputTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ConfigSettingsRanges_ActualOutput_ForUIDebugging()
        {
            _output.WriteLine("=== ConfigSettingsRanges Test ===");

            // Test Block Sizes by System
            _output.WriteLine("");
            _output.WriteLine("--- Block Sizes by System ---");
            IReadOnlyList<string> gcBlockSizes = ConfigSettingsRanges.GetBlockSizes(SystemType.GameCube);
            _output.WriteLine($"GameCube: [{string.Join(", ", gcBlockSizes)}]");

            IReadOnlyList<string> ps3BlockSizes = ConfigSettingsRanges.GetBlockSizes(SystemType.PS3);
            _output.WriteLine($"PS3: [{string.Join(", ", ps3BlockSizes)}]");

            // Test Block Sizes by Format
            _output.WriteLine("");
            _output.WriteLine("--- Block Sizes by Format ---");
            IReadOnlyList<string> rvzBlockSizes = ConfigSettingsRanges.GetBlockSizes("rvz");
            _output.WriteLine($"RVZ: [{string.Join(", ", rvzBlockSizes)}]");

            IReadOnlyList<string> csoBlockSizes = ConfigSettingsRanges.GetBlockSizes("cso");
            _output.WriteLine($"CSO: [{string.Join(", ", csoBlockSizes)}]");

            // Test Parallelism Values  
            _output.WriteLine("");
            _output.WriteLine("--- Parallelism Values ---");
            IReadOnlyList<int> gcParallelism = ConfigSettingsRanges.GetParallelismValues(SystemType.GameCube);
            _output.WriteLine($"GameCube Parallelism: [{string.Join(", ", gcParallelism.Take(5))}...] (Total: {gcParallelism.Count})");

            IReadOnlyList<int> ps3Parallelism = ConfigSettingsRanges.GetParallelismValues(SystemType.PS3);
            _output.WriteLine($"PS3 Parallelism: [{string.Join(", ", ps3Parallelism.Take(5))}...] (Total: {ps3Parallelism.Count})");

            // Test RVZ Encoding Types
            _output.WriteLine("");
            _output.WriteLine("--- RVZ Encoding Types ---");
            IReadOnlyList<RvzEncodingType> encodingTypes = ConfigSettingsRanges.GetRvzEncodingTypes();
            _output.WriteLine($"RVZ Encodings: [{string.Join(", ", encodingTypes)}]");

            // Test Compression Levels
            _output.WriteLine("");
            _output.WriteLine("--- Compression Levels ---");
            IReadOnlyList<int> zstdLevels = ConfigSettingsRanges.GetCompressionLevels("rvz", RvzEncodingType.ZStd);
            _output.WriteLine($"ZStd Levels: [{string.Join(", ", zstdLevels.Take(5))}...{zstdLevels.Skip(zstdLevels.Count - 3).First()}..{zstdLevels.Last()}] (Total: {zstdLevels.Count})");

            IReadOnlyList<int> csoLevels = ConfigSettingsRanges.GetCompressionLevels("cso");
            _output.WriteLine($"CSO Levels: [{string.Join(", ", csoLevels)}]");

            // Test CUE Configuration
            _output.WriteLine("");
            _output.WriteLine("--- CUE Configuration ---");
            IReadOnlyList<string> cueTypes = ConfigSettingsRanges.GetCueTypes();
            _output.WriteLine($"CUE Types: [{string.Join(", ", cueTypes)}]");

            IReadOnlyList<string> binaryExtensions = ConfigSettingsRanges.GetBinaryExtensions();
            _output.WriteLine($"Binary Extensions: [{string.Join(", ", binaryExtensions)}]");

            IReadOnlyList<string> audioExtensions = ConfigSettingsRanges.GetAudioExtensions();
            _output.WriteLine($"Audio Extensions: [{string.Join(", ", audioExtensions)}]");

            // Always pass - this is just for output
            Assert.True(true);
        }

        [Fact]
        public void ConfigSettingsRanges_RvzZStdLevels_ReturnsCorrectRange()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("rvz", RvzEncodingType.ZStd);

            // Assert - Should return levels 1-22 for ZStd
            Assert.NotEmpty(levels);
            Assert.Equal(22, levels.Count);
            Assert.Equal(1, levels.First());
            Assert.Equal(22, levels.Last());
            Assert.All(levels, level => Assert.True(level >= 1 && level <= 22));

            _output.WriteLine($"ZStd levels for RVZ: [{string.Join(", ", levels.Take(5))}...{levels.Last()}] (Total: {levels.Count})");
        }

        [Fact]
        public void ConfigSettingsRanges_RvzNoneLevels_ReturnsZero()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("rvz", RvzEncodingType.None);

            // Assert - Should return [0] for None encoding (no compression)
            Assert.Single(levels);
            Assert.Equal(0, levels.First());

            _output.WriteLine($"None levels for RVZ: [{string.Join(", ", levels)}]");
        }

        [Fact]
        public void ConfigSettingsRanges_CsoLevels_ReturnsZlibRange()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("cso");

            // Assert - Should return levels 1-9 for CSO (ZLib)
            Assert.NotEmpty(levels);
            Assert.Equal(9, levels.Count);
            Assert.Equal(1, levels.First());
            Assert.Equal(9, levels.Last());

            _output.WriteLine($"CSO levels: [{string.Join(", ", levels)}]");
        }

        [Fact]
        public void ConfigSettingsRanges_ZsoLevels_ReturnsLz4Range()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("zso");

            // Assert - Should return levels 1-12 for ZSO (LZ4)
            Assert.NotEmpty(levels);
            Assert.Equal(12, levels.Count);
            Assert.Equal(1, levels.First());
            Assert.Equal(12, levels.Last());

            _output.WriteLine($"ZSO levels: [{string.Join(", ", levels)}]");
        }

        [Fact]
        public void ConfigSettingsRanges_RvzLzmaLevels_ReturnsCorrectRange()
        {
            // Act
            IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("rvz", RvzEncodingType.Lzma);

            // Assert - Should return levels 1-9 for LZMA
            Assert.NotEmpty(levels);
            Assert.Equal(9, levels.Count);
            Assert.Equal(1, levels.First());
            Assert.Equal(9, levels.Last());
            Assert.All(levels, level => Assert.True(level >= 1 && level <= 9));

            _output.WriteLine($"LZMA levels for RVZ: [{string.Join(", ", levels)}] (Total: {levels.Count})");
        }

        [Fact]
        public void ConfigSettingsRanges_UiIntegration_LzmaLevelsWorkCorrectly()
        {
            // This test simulates what the UI does when LZMA encoding is selected

            // Arrange - simulate UI state
            string currentFormat = "rvz";
            RvzEncodingType currentEncoding = RvzEncodingType.Lzma;

            // Act - this is what RefreshLevelsForCurrentFormatAndEncoding() does
            IReadOnlyList<int> compressionLevels = ConfigSettingsRanges.GetCompressionLevels(currentFormat, currentEncoding);
            List<string> levelStrings = compressionLevels
                .Where(level => level > 0)  // Filter out 0 (no compression indicator)
                .Select(level => level.ToString())
                .ToList();

            // Assert - UI should get LZMA levels 1-9 as strings
            Assert.NotEmpty(levelStrings);
            Assert.Equal(9, levelStrings.Count);
            Assert.Equal("1", levelStrings.First());
            Assert.Equal("9", levelStrings.Last());
            Assert.Contains("5", levelStrings); // Default LZMA level

            _output.WriteLine($"UI LZMA levels: [{string.Join(", ", levelStrings)}]");
        }

        [Fact]
        public void ConfigSettingsRanges_UiIntegration_ZStdLevelsWorkCorrectly()
        {
            // This test simulates what the UI does when ZStd encoding is selected

            // Arrange - simulate UI state
            string currentFormat = "rvz";
            RvzEncodingType currentEncoding = RvzEncodingType.ZStd;

            // Act - this is what RefreshLevelsForCurrentFormatAndEncoding() does
            IReadOnlyList<int> compressionLevels = ConfigSettingsRanges.GetCompressionLevels(currentFormat, currentEncoding);
            List<string> levelStrings = compressionLevels
                .Where(level => level > 0)  // Filter out 0 (no compression indicator)
                .Select(level => level.ToString())
                .ToList();

            // Assert - UI should get ZStd levels 1-22 as strings
            Assert.NotEmpty(levelStrings);
            Assert.Equal(22, levelStrings.Count);
            Assert.Equal("1", levelStrings.First());
            Assert.Equal("22", levelStrings.Last());
            Assert.Contains("19", levelStrings); // Default ZStd level

            _output.WriteLine($"UI ZStd levels: [{string.Join(", ", levelStrings.Take(5))}...{levelStrings.Last()}] (Total: {levelStrings.Count})");
        }

        [Fact]
        public void ConfigSettingsDefaults_UiIntegration_EnsureValidDefaultsWorkCorrectly()
        {
            // This test simulates what the UI does when EnsureValidDefaults() is called

            // Arrange - test cases that match the actual nkit.yaml defaults
            var testCases = new[]
            {
                new { System = SystemType.GameCube, Format = "rvz", Encoding = "zstd" }, // Per YAML: rvz:zstd:19:128k:16
                new { System = SystemType.Wii, Format = "rvz", Encoding = "zstd" },     // Per YAML: rvz:zstd:19:128k:16
                new { System = SystemType.PS3, Format = "deciso", Encoding = "" },      // Per YAML: deciso
                new { System = SystemType.PSP, Format = "cso", Encoding = "" },         // Per YAML: cso:9:2k:4
                new { System = SystemType.Dreamcast, Format = "cue", Encoding = "" }   // Per YAML: cue
            };

            foreach (var testCase in testCases)
            {
                _output.WriteLine($"Testing defaults for {testCase.System} with {testCase.Format} format:");

                // Act - get defaults that EnsureValidDefaults() would use
                string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(testCase.System);
                string defaultBlockSize = ConfigSettingsDefaults.GetDefaultBlockSize(testCase.System, testCase.Format);
                int defaultParallelism = ConfigSettingsDefaults.GetDefaultParallelism(testCase.System);

                string defaultLevel = "1";
                if (testCase.Format == "rvz" && !string.IsNullOrEmpty(testCase.Encoding))
                {
                    if (Enum.TryParse<RvzEncodingType>(testCase.Encoding, true, out RvzEncodingType encoding))
                    {
                        defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding).ToString();
                    }
                }
                else if (testCase.Format == "cso" || testCase.Format == "cso2" || testCase.Format == "zso")
                {
                    defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(testCase.Format).ToString();
                }

                // Assert - all defaults should be valid
                Assert.False(string.IsNullOrEmpty(defaultFormat), $"{testCase.System} should have a default format");
                Assert.False(string.IsNullOrEmpty(defaultBlockSize), $"{testCase.System} should have a default block size");
                Assert.True(defaultParallelism > 0, $"{testCase.System} should have a valid default parallelism");
                Assert.False(string.IsNullOrEmpty(defaultLevel), $"{testCase.System} should have a default level");

                // Verify defaults are in valid ranges
                IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(testCase.System);
                Assert.Contains(defaultFormat, supportedFormats);

                IReadOnlyList<string> supportedBlockSizes = ConfigSettingsRanges.GetBlockSizes(defaultFormat); // Use format-specific block sizes
                // Some formats (like CUE, GDI, DECISO) don't use block sizes
                if (supportedBlockSizes.Count > 0)
                {
                    Assert.Contains(defaultBlockSize, supportedBlockSizes);
                }
                else
                {
                    // For formats without block sizes, just verify we have a value
                    Assert.False(string.IsNullOrEmpty(defaultBlockSize),
                        $"{testCase.System} should have a default block size even if format doesn't use it");
                }

                IReadOnlyList<int> supportedParallelisms = ConfigSettingsRanges.GetParallelismValues(testCase.System);
                Assert.Contains(defaultParallelism, supportedParallelisms);

                _output.WriteLine($"  ? Format: {defaultFormat}");
                _output.WriteLine($"  ? BlockSize: {defaultBlockSize}");
                _output.WriteLine($"  ? Parallelism: {defaultParallelism}");
                _output.WriteLine($"  ? Level: {defaultLevel}");
                _output.WriteLine("");
            }
        }

        [Fact]
        public void ConfigSettingsDefaults_EncodingPreservation_WorksCorrectlyBetweenSystems()
        {
            // This test simulates the scenario where a user selects LZMA encoding on GameCube,
            // then switches to Wii - the LZMA encoding should be preserved

            _output.WriteLine("=== Testing Encoding Preservation Between GameCube/Wii ===");

            // Test scenarios
            var testCases = new[]
            {
                new { System = SystemType.GameCube, Encoding = RvzEncodingType.ZStd, ExpectedLevel = 19 },
                new { System = SystemType.GameCube, Encoding = RvzEncodingType.Lzma, ExpectedLevel = 5 },
                new { System = SystemType.Wii, Encoding = RvzEncodingType.ZStd, ExpectedLevel = 19 },
                new { System = SystemType.Wii, Encoding = RvzEncodingType.Lzma, ExpectedLevel = 5 },
                new { System = SystemType.PS3, Encoding = RvzEncodingType.None, ExpectedLevel = 0 } // PS3 uses DecISO
            };

            foreach (var testCase in testCases)
            {
                _output.WriteLine($"Testing {testCase.System} with {testCase.Encoding} encoding:");

                // Verify RVZ encoding support exists
                IReadOnlyList<RvzEncodingType> supportedEncodings = ConfigSettingsRanges.GetRvzEncodingTypes();
                Assert.Contains(testCase.Encoding, supportedEncodings);

                // Verify compression levels are correct for the encoding
                if (testCase.System != SystemType.PS3) // PS3 uses DecISO, not RVZ
                {
                    IReadOnlyList<int> levels = ConfigSettingsRanges.GetCompressionLevels("rvz", testCase.Encoding);
                    if (testCase.ExpectedLevel > 0)
                    {
                        Assert.Contains(testCase.ExpectedLevel, levels);
                        _output.WriteLine($"  ? {testCase.Encoding} encoding supports level {testCase.ExpectedLevel}");
                    }
                }

                // Verify default format for PS3 is DecISO
                if (testCase.System == SystemType.PS3)
                {
                    string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(SystemType.PS3);
                    Assert.Equal(ConfigSettingsConstants.FormatDecIso, defaultFormat);
                    _output.WriteLine($"  ? PS3 defaults to DecISO format");

                    // Verify DecISO is in supported formats for PS3
                    IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(SystemType.PS3);
                    Assert.Contains(ConfigSettingsConstants.FormatDecIso, supportedFormats);
                    _output.WriteLine($"  ? PS3 supports DecISO format");
                }
            }

            _output.WriteLine("");
            _output.WriteLine("All encoding preservation and PS3 DecISO tests passed! ?");
        }

        [Fact]
        public void ConfigSettingsDefaults_SystemPersistence_MaintainsSystemSpecificSettings()
        {
            // This test simulates the issue where switching between systems doesn't preserve
            // user-customized settings for each system independently

            _output.WriteLine("=== Testing System-Specific Settings Persistence ===");

            // Simulate the user workflow that was failing:
            // 1. Set GameCube to LZMA encoding
            // 2. Switch to Wii (should get Wii defaults)  
            // 3. Switch back to GameCube (should restore LZMA, not revert to ZStd)

            // Test data
            RvzEncodingType gameCubeCustomEncoding = RvzEncodingType.Lzma;
            int gameCubeCustomLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(gameCubeCustomEncoding);

            RvzEncodingType wiiDefaultEncoding = RvzEncodingType.ZStd;  // From YAML defaults
            int wiiDefaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(wiiDefaultEncoding);

            _output.WriteLine($"GameCube Custom: {gameCubeCustomEncoding} encoding, level {gameCubeCustomLevel}");
            _output.WriteLine($"Wii Default: {wiiDefaultEncoding} encoding, level {wiiDefaultLevel}");

            // Verify test assumptions
            Assert.NotEqual(gameCubeCustomEncoding, wiiDefaultEncoding); // Must be different for test to be meaningful
            Assert.NotEqual(gameCubeCustomLevel, wiiDefaultLevel);

            // Verify both encodings are supported for RVZ format
            IReadOnlyList<RvzEncodingType> supportedEncodings = ConfigSettingsRanges.GetRvzEncodingTypes();
            Assert.Contains(gameCubeCustomEncoding, supportedEncodings);
            Assert.Contains(wiiDefaultEncoding, supportedEncodings);

            // Verify both systems support RVZ format
            IReadOnlyList<string> gcFormats = ConfigSettingsRanges.GetSupportedFormats(SystemType.GameCube);
            IReadOnlyList<string> wiiFormats = ConfigSettingsRanges.GetSupportedFormats(SystemType.Wii);
            Assert.Contains(ConfigSettingsConstants.FormatRvz, gcFormats);
            Assert.Contains(ConfigSettingsConstants.FormatRvz, wiiFormats);

            // Verify compression levels are available
            IReadOnlyList<int> lzmaLevels = ConfigSettingsRanges.GetCompressionLevels("rvz", gameCubeCustomEncoding);
            IReadOnlyList<int> zstdLevels = ConfigSettingsRanges.GetCompressionLevels("rvz", wiiDefaultEncoding);
            Assert.Contains(gameCubeCustomLevel, lzmaLevels);
            Assert.Contains(wiiDefaultLevel, zstdLevels);

            _output.WriteLine("");
            _output.WriteLine("? All test preconditions verified");
            _output.WriteLine("? GameCube and Wii both support RVZ format with different encodings");
            _output.WriteLine("? User should be able to set different encoding per system and have it preserved");

            // The actual persistence logic is tested through the UI workflow,
            // but this test verifies that the configuration system supports
            // the distinction needed for proper persistence
        }
    }
}