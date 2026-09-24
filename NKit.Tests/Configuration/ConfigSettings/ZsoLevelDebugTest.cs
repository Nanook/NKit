using Nanook.NKit.Configuration;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    /// <summary>
    /// Quick debug test to verify ZSO level binding issue
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class ZsoLevelDebugTest
    {
        private readonly ITestOutputHelper _output;

        public ZsoLevelDebugTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Debug_ZsoLevels_ShouldReturn1To12()
        {
            // Test ConfigSettingsRanges.GetCompressionLevels("zso")
            IReadOnlyList<int> zsoLevels = ConfigSettingsRanges.GetCompressionLevels("zso");
            _output.WriteLine($"ZSO levels from GetCompressionLevels('zso'): [{string.Join(", ", zsoLevels)}]");

            // Test ConfigSettingsRanges.GetLz4Levels()
            IReadOnlyList<int> lz4Levels = ConfigSettingsRanges.GetLz4Levels();
            _output.WriteLine($"LZ4 levels from GetLz4Levels(): [{string.Join(", ", lz4Levels)}]");

            // Test the constant
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatZso);
            _output.WriteLine($"ZSO default level: {defaultLevel}");

            // Verify they match
            Assert.Equal(lz4Levels.Count, zsoLevels.Count);
            Assert.Equal(12, defaultLevel);
            Assert.Contains(12, zsoLevels);
        }

        [Fact]
        public void Debug_CsoLevels_ShouldReturn1To9()
        {
            // Test ConfigSettingsRanges.GetCompressionLevels("cso") 
            IReadOnlyList<int> csoLevels = ConfigSettingsRanges.GetCompressionLevels("cso");
            _output.WriteLine($"CSO levels from GetCompressionLevels('cso'): [{string.Join(", ", csoLevels)}]");

            // Test ConfigSettingsRanges.GetZlibLevels()
            IReadOnlyList<int> zlibLevels = ConfigSettingsRanges.GetZlibLevels();
            _output.WriteLine($"ZLib levels from GetZlibLevels(): [{string.Join(", ", zlibLevels)}]");

            // Test the constant
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(ConfigSettingsConstants.FormatCso);
            _output.WriteLine($"CSO default level: {defaultLevel}");

            // Verify they match
            Assert.Equal(zlibLevels.Count, csoLevels.Count);
            Assert.Equal(9, defaultLevel);
            Assert.Contains(9, csoLevels);
        }
    }
}