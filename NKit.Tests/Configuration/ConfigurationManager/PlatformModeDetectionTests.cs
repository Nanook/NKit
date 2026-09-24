using Nanook.NKit.Configuration;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests for platform and mode detection across all 12 combinations.
    /// Verifies that portable vs system mode is detected correctly for each platform/app combination.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class PlatformModeDetectionTests
    {
        #region Test Data - All 12 Platform/App/Mode Combinations

        public static IEnumerable<object[]> AllCombinations =>
            new List<object[]>
            {
                // Windows CLI
                new object[] { Platform.Windows, AppType.CLI, Mode.Portable },
                new object[] { Platform.Windows, AppType.CLI, Mode.System },
                
                // Windows UI
                new object[] { Platform.Windows, AppType.UI, Mode.Portable },
                new object[] { Platform.Windows, AppType.UI, Mode.System },
                
                // Linux CLI
                new object[] { Platform.Linux, AppType.CLI, Mode.Portable },
                new object[] { Platform.Linux, AppType.CLI, Mode.System },
                
                // Linux UI
                new object[] { Platform.Linux, AppType.UI, Mode.Portable },
                new object[] { Platform.Linux, AppType.UI, Mode.System },
                
                // macOS CLI
                new object[] { Platform.macOS, AppType.CLI, Mode.Portable },
                new object[] { Platform.macOS, AppType.CLI, Mode.System },
                
                // macOS UI (non-bundle)
                new object[] { Platform.macOS, AppType.UI, Mode.Portable },
                new object[] { Platform.macOS, AppType.UI, Mode.System },
            };

        #endregion

        #region Portable Mode Detection Tests

        [Theory(DisplayName = "Portable mode is detected when config file exists locally")]
        [MemberData(nameof(AllCombinations))]
        public void PortableMode_DetectedWhen_ConfigFileExistsLocally(
            Platform platform, AppType appType, Mode expectedMode)
        {
            // Skip if this combination should be system mode
            if (expectedMode == Mode.System)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up portable mode conditions: config file exists locally
            mockFileSystem.SetupPortableMode(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should detect portable mode
            Assert.True(info.IsPortableMode,
                $"{scenario.Description} should detect portable mode when config exists locally");

            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);
            Assert.Equal(scenario.ExpectedConfigFileName, info.ConfigFileName);
        }

        [Theory(DisplayName = "System mode is detected when config file does not exist locally")]
        [MemberData(nameof(AllCombinations))]
        public void SystemMode_DetectedWhen_ConfigFileDoesNotExistLocally(
            Platform platform, AppType appType, Mode expectedMode)
        {
            // Skip if this combination should be portable mode
            if (expectedMode == Mode.Portable)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up system mode conditions: no local config file
            mockFileSystem.SetupSystemMode(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should detect system mode
            Assert.False(info.IsPortableMode,
                $"{scenario.Description} should detect system mode when config does not exist locally");

            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);
            Assert.Equal(scenario.ExpectedConfigFileName, info.ConfigFileName);
        }

        #endregion

        #region macOS Bundle Detection Tests

        [Fact(DisplayName = "macOS bundle is detected correctly for UI apps")]
        public void MacOSBundle_DetectedCorrectly_ForUIApps()
        {
            // Arrange - macOS UI app in bundle structure
            TestScenario scenario = TestScenario.CreateMacOSBundle();
            MockPlatformService mockPlatform = new MockPlatformService(
                Platform.macOS,
                AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle detection
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);
            mockFileSystem.SetupSystemMode(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Bundle should be detected
            Assert.Equal(scenario.ExpectedExecutableDirectory, info.ExecutableDirectory);
            Assert.Contains(".app/Contents/MacOS", info.ExecutableDirectory);

            // Config should be in system directory (not inside bundle)
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.DoesNotContain(".app", info.ConfigDirectory);
        }

        [Fact(DisplayName = "macOS bundle portable mode when config exists next to bundle")]
        public void MacOSBundle_PortableMode_WhenConfigNextToBundle()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundlePortable();
            MockPlatformService mockPlatform = new MockPlatformService(
                Platform.macOS,
                AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Config file exists next to the .app bundle
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);

            string configPath = mockFileSystem.CombinePath(
                Path.GetDirectoryName(scenario.BundlePath),
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configPath, true);
            mockFileSystem.SetDirectoryWritable(Path.GetDirectoryName(scenario.BundlePath), true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should be portable with config next to bundle
            Assert.True(info.IsPortableMode);
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);

            // Config directory should be next to .app, not inside it
            Assert.DoesNotContain("Contents", info.ConfigDirectory);
        }

        #endregion

        #region Config File Name Tests

        [Theory(DisplayName = "Config file name matches app type")]
        [InlineData(AppType.CLI, ConfigSettingsConstants.ConfigFileNameCLI)]
        [InlineData(AppType.UI, ConfigSettingsConstants.ConfigFileNameUI)]
        public void ConfigFileName_MatchesAppType(AppType appType, string expectedFileName)
        {
            // Arrange - Test on Windows for simplicity
            TestScenario scenario = TestScenario.Create(Platform.Windows, appType, Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(Platform.Windows, appType);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.Equal(expectedFileName, info.ConfigFileName);
        }

        #endregion

        #region Enums

        public enum Platform { Windows, Linux, macOS }
        public enum AppType { CLI, UI }
        public enum Mode { Portable, System }

        #endregion
    }
}