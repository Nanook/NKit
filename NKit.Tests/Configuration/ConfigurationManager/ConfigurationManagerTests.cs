using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Basic tests for ConfigurationManager to verify it works correctly
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class ConfigurationManagerTests
    {
        [Fact]
        public void ConfigurationManager_Initializes_Successfully()
        {
            // Act & Assert - Should not throw
            using ConfigManager configManager = new ConfigManager();

            Assert.NotNull(configManager);
            Assert.NotNull(configManager.Context);
            Assert.NotNull(configManager.Context.ExecutableDirectory);
            Assert.NotNull(configManager.Context.ConfigDirectory);
            Assert.NotNull(configManager.Context.UserDataDirectory);
        }

        [Fact]
        public void ConfigurationManager_EnsureConfiguration_Works()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act
            SetupResult result = configManager.EnsureConfiguration();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);
        }

        [Fact]
        public void ConfigurationManager_ExpandConfigPath_Works()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act
            string expanded = configManager.ExpandConfigPath("$configPath$/test");

            // Assert
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
        }

        [Fact]
        public void ConfigurationManager_GetConfigurationInfo_Works()
        {
            // Arrange
            using ConfigManager configManager = new ConfigManager();

            // Act
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.NotNull(info);
            Assert.NotNull(info.ExecutableDirectory);
            Assert.NotNull(info.ConfigDirectory);
            Assert.NotNull(info.UserDataDirectory);
        }
    }
}