using Nanook.NKit.Configuration;
using System.Collections.Generic;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests config file location detection and fallback logic.
    /// Verifies that the config file is found in the correct priority order:
    /// 1. Command line specified (-cfg parameter)
    /// 2. Local config (portable mode - next to executable or bundle)
    /// 3. User config directory (system mode)
    /// 4. Built-in defaults
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class ConfigFileLocationTests
    {
        #region Test Data

        public static IEnumerable<object[]> AllCombinations =>
            PlatformModeDetectionTests.AllCombinations;

        #endregion

        #region Local Config Detection Tests

        [Theory(DisplayName = "Local config file is detected for portable mode")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.AppType.CLI)]
        public void LocalConfigFile_IsDetected_ForPortableMode(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up local config file
            string localConfigPath = mockFileSystem.CombinePath(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(localConfigPath, true);
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedExecutableDirectory, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should detect local config
            Assert.True(info.IsPortableMode, "Should be in portable mode when local config exists");
            Assert.Equal(scenario.ExpectedConfigFileName, info.ConfigFileName);
            Assert.Equal(scenario.ExpectedExecutableDirectory, info.ConfigDirectory);
        }

        [Theory(DisplayName = "Missing local config falls back to system mode")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.AppType.CLI)]
        public void MissingLocalConfig_FallsBackTo_SystemMode(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // No local config file
            string localConfigPath = mockFileSystem.CombinePath(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(localConfigPath, false);
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedExecutableDirectory, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should fall back to system mode
            Assert.False(info.IsPortableMode, "Should be in system mode when local config doesn't exist");
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.NotEqual(scenario.ExpectedExecutableDirectory, info.ConfigDirectory);
        }

        #endregion

        #region macOS Bundle Config Location Tests

        [Fact(DisplayName = "macOS bundle: Config next to .app enables portable mode")]
        public void MacOSBundle_ConfigNextToApp_EnablesPortableMode()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundlePortable();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);

            // Config exists next to .app bundle (not inside it)
            // Use cross-platform path manipulation - mockFileSystem.GetParentDirectory works with Unix paths
            string bundleParent = mockFileSystem.GetParentDirectory(scenario.BundlePath);
            string configPath = mockFileSystem.CombinePath(bundleParent, scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configPath, true);
            mockFileSystem.SetDirectoryWritable(bundleParent, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should be portable mode with config next to bundle
            Assert.True(info.IsPortableMode, "Config next to bundle should enable portable mode");
            Assert.Equal(bundleParent, info.ConfigDirectory);
            Assert.DoesNotContain(".app", info.ConfigDirectory);
        }

        [Fact(DisplayName = "macOS bundle: No config next to .app uses system mode")]
        public void MacOSBundle_NoConfigNextToApp_UsesSystemMode()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundle();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);

            // No config next to bundle - use mockFileSystem.GetParentDirectory for cross-platform support
            string bundleParent = mockFileSystem.GetParentDirectory(scenario.BundlePath);
            string configPath = mockFileSystem.CombinePath(bundleParent, scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configPath, false);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should use system mode
            Assert.False(info.IsPortableMode, "No config next to bundle should use system mode");
            Assert.Contains("Documents", info.ConfigDirectory);
        }

        [Fact(DisplayName = "macOS bundle: Config inside .app is ignored")]
        public void MacOSBundle_ConfigInsideApp_IsIgnored()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundle();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);

            // Config inside bundle (should be ignored - wrong location)
            string configInsideBundle = mockFileSystem.CombinePath(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configInsideBundle, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should still use system mode (config inside bundle is ignored)
            Assert.False(info.IsPortableMode, "Config inside bundle should be ignored");
            Assert.Contains("Documents", info.ConfigDirectory);
        }

        #endregion

        #region Config File Name Tests

        [Theory(DisplayName = "CLI apps use nkit.yaml")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void CLI_Apps_UseCorrectConfigFileName(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameCLI, info.ConfigFileName);
        }

        [Theory(DisplayName = "UI apps use nkit-ui.yaml")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void UI_Apps_UseCorrectConfigFileName(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.UI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.UI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameUI, info.ConfigFileName);
        }

        #endregion

        #region System Config Directory Tests

        [Fact(DisplayName = "Windows system mode uses correct config directory")]
        public void Windows_SystemMode_UsesCorrectConfigDirectory()
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should use AppData\Roaming\nkit
            Assert.Contains("AppData", info.ConfigDirectory);
            Assert.Contains("Roaming", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        [Fact(DisplayName = "Linux system mode uses correct config directory")]
        public void Linux_SystemMode_UsesCorrectConfigDirectory()
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(
                PlatformModeDetectionTests.Platform.Linux,
                PlatformModeDetectionTests.AppType.CLI,
                PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Linux,
                PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should use ~/.config/nkit
            Assert.Contains(".config", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        [Fact(DisplayName = "macOS system mode uses correct config directory")]
        public void MacOS_SystemMode_UsesCorrectConfigDirectory()
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.CLI,
                PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should use ~/Documents/nkit
            Assert.Contains("Documents", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        #endregion

        #region Config Source Detection Tests

        [Fact(DisplayName = "Portable mode reports correct config source")]
        public void PortableMode_ReportsCorrectConfigSource()
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Config source should be App (local/portable)
            Assert.Equal(ConfigSource.App, info.ConfigSource);
        }

        [Fact(DisplayName = "System mode reports correct config source")]
        public void SystemMode_ReportsCorrectConfigSource()
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Config source should be UserConfig (system mode)
            // Note: Actual value may be UserConfig or None depending on if file exists
            Assert.True(
                info.ConfigSource == ConfigSource.UserConfig ||
                info.ConfigSource == ConfigSource.None,
                $"Config source should be UserConfig or None, got: {info.ConfigSource}");
        }

        #endregion

        #region Cross-Platform Consistency Tests

        [Theory(DisplayName = "All platforms use consistent config file names")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void AllPlatforms_UseConsistentConfigFileNames(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange - CLI app
            TestScenario cliScenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService cliMockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService cliMockFileSystem = new MockFileSystemService();
            cliMockFileSystem.SetupPortableMode(cliScenario.ExpectedExecutableDirectory, cliScenario.ExpectedConfigFileName);

            // Arrange - UI app
            TestScenario uiScenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.UI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService uiMockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.UI);
            MockFileSystemService uiMockFileSystem = new MockFileSystemService();
            uiMockFileSystem.SetupPortableMode(uiScenario.ExpectedExecutableDirectory, uiScenario.ExpectedConfigFileName);

            // Act
            using ConfigManager cliConfigManager = new ConfigManager(cliMockPlatform, cliMockFileSystem);
            using ConfigManager uiConfigManager = new ConfigManager(uiMockPlatform, uiMockFileSystem);

            ConfigurationInfo cliInfo = cliConfigManager.GetConfigurationInfo();
            ConfigurationInfo uiInfo = uiConfigManager.GetConfigurationInfo();

            // Assert - Config file names should be consistent across platforms
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameCLI, cliInfo.ConfigFileName);
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameUI, uiInfo.ConfigFileName);
        }

        #endregion

        #region Comprehensive All-Combination Tests

        [Theory(DisplayName = "All combinations: Config file name matches app type")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_ConfigFileName_MatchesAppType(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            if (mode == PlatformModeDetectionTests.Mode.Portable)
                mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);
            else
                mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            string expectedFileName = appType == PlatformModeDetectionTests.AppType.CLI
                ? ConfigSettingsConstants.ConfigFileNameCLI
                : ConfigSettingsConstants.ConfigFileNameUI;
            Assert.Equal(expectedFileName, info.ConfigFileName);
        }

        [Theory(DisplayName = "All combinations: Config directory matches mode expectations")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_ConfigDirectory_MatchesModeExpectations(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            if (mode == PlatformModeDetectionTests.Mode.Portable)
                mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);
            else
                mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);

            if (mode == PlatformModeDetectionTests.Mode.Portable)
            {
                // In portable mode, directories should be the same
                Assert.Equal(info.ConfigDirectory, info.UserDataDirectory);
            }
            else
            {
                // In system mode, directories should be different
                Assert.NotEqual(info.ConfigDirectory, info.UserDataDirectory);
            }
        }

        [Theory(DisplayName = "All combinations: Platform-specific system directories are correct")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_SystemDirectories_ArePlatformSpecific(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Skip portable mode - only test system mode directories
            if (mode == PlatformModeDetectionTests.Mode.Portable)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Verify platform-specific system directories
            switch (platform)
            {
                case PlatformModeDetectionTests.Platform.Windows:
                    Assert.Contains("AppData", info.ConfigDirectory);
                    Assert.Contains("Roaming", info.ConfigDirectory);
                    break;
                case PlatformModeDetectionTests.Platform.Linux:
                    Assert.Contains(".config", info.ConfigDirectory);
                    break;
                case PlatformModeDetectionTests.Platform.macOS:
                    Assert.Contains("Documents", info.ConfigDirectory);
                    break;
            }

            // All platforms should include app directory name
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        #endregion
    }
}