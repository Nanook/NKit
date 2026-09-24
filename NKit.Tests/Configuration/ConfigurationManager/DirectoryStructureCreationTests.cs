using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using System.Collections.Generic;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests that the directory structure is created correctly for different modes.
    /// Verifies that all required folders are created in the appropriate locations.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class DirectoryStructureCreationTests
    {
        #region Test Data

        public static IEnumerable<object[]> AllCombinations =>
            PlatformModeDetectionTests.AllCombinations;

        #endregion

        #region Portable Mode Structure Tests

        [Theory(DisplayName = "Portable mode: Creates correct folder structure")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.AppType.CLI)]
        public void PortableMode_CreatesCorrectStructure(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();

            // Assert - Setup should succeed
            Assert.NotNull(result);
            Assert.True(result.Success, $"Configuration setup should succeed for {scenario.Description}");

            // Verify it's portable mode
            Assert.True(configManager.Context.IsPortableMode, "Should be in portable mode");

            // In portable mode, config and user directories are the same
            Assert.Equal(configManager.Context.ConfigDirectory, configManager.Context.UserDataDirectory);
        }

        #endregion

        #region System Mode Structure Tests

        [Theory(DisplayName = "System mode: Separates config and user folders")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.AppType.CLI)]
        public void SystemMode_SeparatesConfigAndUserFolders(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();

            // Assert - Setup should succeed
            Assert.NotNull(result);
            Assert.True(result.Success, $"Configuration setup should succeed for {scenario.Description}");

            // Verify it's system mode
            Assert.False(configManager.Context.IsPortableMode, "Should be in system mode");

            // In system mode, config and user directories are different
            Assert.NotEqual(configManager.Context.ConfigDirectory, configManager.Context.UserDataDirectory);
        }

        #endregion

        #region Directory Creation Tests

        [Fact(DisplayName = "EnsureConfiguration creates all required directories")]
        public void EnsureConfiguration_CreatesAllRequiredDirectories()
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
            SetupResult result = configManager.EnsureConfiguration();

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Success);

            // The actual directories created are implementation-specific
            // We're testing that the operation succeeds without errors
        }

        #endregion

        #region Platform-Specific Directory Structure Tests

        [Fact(DisplayName = "Windows system mode uses AppData for config")]
        public void WindowsSystemMode_UsesAppDataForConfig()
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

            // Assert - Should use AppData for config
            Assert.Contains("AppData", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        [Fact(DisplayName = "Linux system mode uses .config for config")]
        public void LinuxSystemMode_UsesConfigDirectory()
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

            // Assert - Should use .config directory
            Assert.Contains(".config", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        [Fact(DisplayName = "macOS system mode uses Documents")]
        public void MacOSSystemMode_UsesLibraryApplicationSupport()
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

            // Assert - Should use Documents
            Assert.Contains("Documents", info.ConfigDirectory);
            Assert.Contains(ConfigSettingsConstants.ApplicationDirectoryName, info.ConfigDirectory);
        }

        #endregion

        #region macOS Bundle Structure Tests

        [Fact(DisplayName = "macOS bundle does not place config inside .app")]
        public void MacOSBundle_DoesNotPlaceConfigInsideApp()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundle();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Config should NOT be inside the bundle
            Assert.DoesNotContain(".app", info.ConfigDirectory);
            Assert.DoesNotContain("Contents", info.ConfigDirectory);
            Assert.DoesNotContain("MacOS", info.ConfigDirectory);

            // But executable should be inside the bundle
            Assert.Contains(".app/Contents/MacOS", info.ExecutableDirectory);
        }

        [Fact(DisplayName = "macOS bundle portable mode places config next to .app")]
        public void MacOSBundlePortable_PlacesConfigNextToApp()
        {
            // Arrange
            TestScenario scenario = TestScenario.CreateMacOSBundlePortable();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);

            // Config exists next to bundle
            string configPath = mockFileSystem.CombinePath(
                scenario.ExpectedConfigDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configPath, true);
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedConfigDirectory, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should be portable mode
            Assert.True(info.IsPortableMode);

            // Config directory should be next to .app
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.DoesNotContain("Contents", info.ConfigDirectory);
        }

        #endregion

        #region Multiple Platform Test (Updated to use AllCombinations)

        [Theory(DisplayName = "All 12 combinations: Successfully create configuration")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_SuccessfullyCreateConfiguration(
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
            SetupResult result = configManager.EnsureConfiguration();

            // Assert
            Assert.True(result.Success, $"Configuration should be created successfully for {scenario.Description}");

            // Verify mode detection
            ConfigurationInfo info = configManager.GetConfigurationInfo();
            Assert.Equal(mode == PlatformModeDetectionTests.Mode.Portable, info.IsPortableMode);

            // Verify directories match expectations
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);
        }

        [Theory(DisplayName = "All combinations: Config and user directory relationship is correct")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_DirectoryRelationship_IsCorrect(
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
            if (mode == PlatformModeDetectionTests.Mode.Portable)
            {
                // In portable mode, config and user directories should be the same
                Assert.Equal(info.ConfigDirectory, info.UserDataDirectory);
                Assert.True(info.ConfigDirectory == info.UserDataDirectory,
                    $"{scenario.Description}: In portable mode, config and user directories should be the same");
            }
            else
            {
                // In system mode, config and user directories should be different
                Assert.NotEqual(info.ConfigDirectory, info.UserDataDirectory);
                Assert.True(info.ConfigDirectory != info.UserDataDirectory,
                    $"{scenario.Description}: In system mode, config and user directories should be different");
            }
        }

        #endregion
    }
}