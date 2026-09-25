using Nanook.NKit;
using Nanook.NKit.Configuration;
using Xunit;


namespace NKit.Tests.Configuration.ConfigSettings
{
    /// <summary>
    /// Tests to verify RVZ compression level defaults are correct for different encoding types
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigSettings")]
    public class RvzCompressionLevelDefaultTests
    {
        [Fact(DisplayName = "RVZ ZStd encoding defaults to level 19, not 5")]
        public void RvzZStdEncoding_DefaultsToLevel19()
        {
            // Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd);

            // Assert
            Assert.Equal(19, defaultLevel);
            Assert.NotEqual(5, defaultLevel); // Ensure it's not using LZMA default
        }

        [Fact(DisplayName = "RVZ LZMA encoding defaults to level 5")]
        public void RvzLzmaEncoding_DefaultsToLevel5()
        {
            // Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.Lzma);

            // Assert
            Assert.Equal(5, defaultLevel);
        }

        [Fact(DisplayName = "RVZ None encoding defaults to level 0")]
        public void RvzNoneEncoding_DefaultsToLevel0()
        {
            // Act
            int defaultLevel = ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.None);

            // Assert
            Assert.Equal(0, defaultLevel);
        }

        [Theory(DisplayName = "UI conversion defaults use correct RVZ ZStd level for Nintendo systems")]
        [InlineData(SystemType.GameCube)]
        [InlineData(SystemType.Wii)]
        public void UiConversionDefaults_UseCorrectRvzZStdLevel_ForNintendoSystems(SystemType systemType)
        {
            // Act
            UiConversionDefaults uiDefaults = ConfigSettingsDefaults.GetUiConversionDefaults(systemType);

            // Assert
            Assert.Equal(ConfigSettingsConstants.FormatRvz, uiDefaults.Format);
            Assert.Equal(ConfigSettingsConstants.EncodingZStd, uiDefaults.Encoding);
            Assert.Equal("19", uiDefaults.Level); // Should be 19, not 5
        }

        [Fact(DisplayName = "Constants define correct default ZStd level")]
        public void Constants_DefineCorrectDefaultZStdLevel()
        {
            // Assert
            Assert.Equal(19, ConfigSettingsConstants.DefaultZStdLevel);
            Assert.Equal(5, ConfigSettingsConstants.DefaultLzmaLevel);
        }
    }
}