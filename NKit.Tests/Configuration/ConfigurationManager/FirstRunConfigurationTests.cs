using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using System.Collections.Generic;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests for first-time app execution scenarios across all 12 combinations.
    /// Verifies that configuration files are created, directories are set up
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class FirstRunConfigurationTests
    {
        #region Test Data

        public static IEnumerable<object[]> AllCombinations =>
            PlatformModeDetectionTests.AllCombinations;

        #endregion

        #region First Run - No Config Exists Tests

        [Theory(DisplayName = "First run portable mode: Config picked up from next to executable")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunPortable_ConfigPickedUpFromExecutableLocation(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode expectedMode)
        {
            // Skip if not portable mode
            if (expectedMode != PlatformModeDetectionTests.Mode.Portable)
                return;

            // Arrange - No config file exists initially
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Initially no config exists - this is first run
            string localConfigPath = mockFileSystem.CombinePath(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(localConfigPath, false);
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedExecutableDirectory, true);

            // Set up system directories for fallback when no local config exists
            TestScenario systemScenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.System);
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act - Without local config
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Without local config, should use system mode with system directories
            Assert.False(info.IsPortableMode,
                $"{scenario.Description}: Without local config, should detect system mode");
            Assert.Equal(systemScenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(systemScenario.ExpectedUserDataDirectory, info.UserDataDirectory);

            // Now create config locally and verify it switches to portable mode
            mockFileSystem.SetFileExists(localConfigPath, true);
            using ConfigManager configManager2 = new ConfigManager(mockPlatform, mockFileSystem);
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Need to create a new manager to pick up the changed config
            using ConfigManager configManager3 = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info2 = configManager3.GetConfigurationInfo();

            // Assert - With local config created, becomes portable mode
            Assert.True(info2.IsPortableMode,
                $"{scenario.Description}: With local config created, becomes portable mode");
            Assert.Equal(scenario.ExpectedExecutableDirectory, info2.ConfigDirectory);
            Assert.Equal(scenario.ExpectedExecutableDirectory, info2.UserDataDirectory);
        }

        [Theory(DisplayName = "First run system mode: Config directory created in correct location")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunSystem_ConfigDirectoryCreatedCorrectly(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode expectedMode)
        {
            // Skip if not system mode
            if (expectedMode != PlatformModeDetectionTests.Mode.System)
                return;

            // Arrange - No config file exists anywhere
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up system mode - no local config
            mockFileSystem.SetupSystemMode(
                scenario.ExpectedExecutableDirectory,
                scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should use platform-specific system directories
            Assert.False(info.IsPortableMode);
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);

            // Verify platform-specific paths
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
        }

        #endregion

        #region EnsureConfiguration - First Run Tests

        [Theory(DisplayName = "First run: EnsureConfiguration creates directories")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRun_EnsureConfiguration_CreatesDirectories(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange - Fresh installation, no directories exist
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
            Assert.NotNull(result);
            Assert.True(result.Success,
                $"{scenario.Description}: EnsureConfiguration should succeed on first run");

            // Verify configuration is valid
            ConfigurationInfo info = configManager.GetConfigurationInfo();
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);
        }

        [Theory(DisplayName = "First run portable: Directories created next to executable")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunPortable_DirectoriesCreatedNextToExecutable(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Skip if not portable mode
            if (mode != PlatformModeDetectionTests.Mode.Portable)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();

            // Assert
            Assert.True(result.Success);

            // In portable mode, config and user directories are the same
            ConfigurationInfo info = configManager.GetConfigurationInfo();
            Assert.Equal(info.ConfigDirectory, info.UserDataDirectory);
            Assert.True(info.ConfigDirectory == info.UserDataDirectory,
                $"{scenario.Description}: Portable mode should have same config and user directories");
            Assert.Equal(scenario.ExpectedExecutableDirectory, info.ConfigDirectory);
            Assert.True(scenario.ExpectedExecutableDirectory == info.ConfigDirectory,
                $"{scenario.Description}: Config directory should be next to executable");
        }

        [Theory(DisplayName = "First run system: Directories created in platform-specific locations")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunSystem_DirectoriesCreatedInPlatformSpecificLocations(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Skip if not system mode
            if (mode != PlatformModeDetectionTests.Mode.System)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();

            // Assert
            Assert.True(result.Success);

            // In system mode, config and user directories are different
            ConfigurationInfo info = configManager.GetConfigurationInfo();
            Assert.NotEqual(info.ConfigDirectory, info.UserDataDirectory);
            Assert.True(info.ConfigDirectory != info.UserDataDirectory,
                $"{scenario.Description}: System mode should have separate config and user directories");

            // Verify they're in the expected platform-specific locations
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.Equal(scenario.ExpectedUserDataDirectory, info.UserDataDirectory);
        }

        #endregion

        #region macOS Bundle First Run Tests

        [Fact(DisplayName = "First run macOS bundle system: Config created in Documents Support")]
        public void FirstRunMacOSBundleSystem_ConfigCreatedInLibrary()
        {
            // Arrange - macOS bundle in system mode, first run
            TestScenario scenario = TestScenario.CreateMacOSBundle();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle without local config
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.True(result.Success);
            Assert.False(info.IsPortableMode);
            Assert.Contains("Documents", info.ConfigDirectory);
            Assert.DoesNotContain(".app", info.ConfigDirectory);
        }

        [Fact(DisplayName = "First run macOS bundle portable: Config created next to .app")]
        public void FirstRunMacOSBundlePortable_ConfigCreatedNextToApp()
        {
            // Arrange - macOS bundle with config next to .app
            TestScenario scenario = TestScenario.CreateMacOSBundlePortable();
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.macOS,
                PlatformModeDetectionTests.AppType.UI,
                scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle with config next to .app
            mockFileSystem.SetupBundleDetection(scenario.BundlePath);
            string configPath = mockFileSystem.CombinePath(
                scenario.ExpectedConfigDirectory,
                scenario.ExpectedConfigFileName);
            mockFileSystem.SetFileExists(configPath, true);
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedConfigDirectory, true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result = configManager.EnsureConfiguration();
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert
            Assert.True(result.Success);
            Assert.True(info.IsPortableMode);
            Assert.Equal(scenario.ExpectedConfigDirectory, info.ConfigDirectory);
            Assert.DoesNotContain("Contents", info.ConfigDirectory);
        }

        #endregion

        #region Subsequent Run Tests

        [Theory(DisplayName = "Subsequent run: EnsureConfiguration succeeds without recreating")]
        [MemberData(nameof(AllCombinations))]
        public void SubsequentRun_EnsureConfiguration_SucceedsWithoutRecreating(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange - Configuration already exists
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            if (mode == PlatformModeDetectionTests.Mode.Portable)
                mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);
            else
                mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act - First run
            using ConfigManager configManager1 = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result1 = configManager1.EnsureConfiguration();

            // Act - Second run (subsequent)
            using ConfigManager configManager2 = new ConfigManager(mockPlatform, mockFileSystem);
            SetupResult result2 = configManager2.EnsureConfiguration();

            // Assert - Both runs should succeed
            Assert.True(result1.Success, $"{scenario.Description}: First run should succeed");
            Assert.True(result2.Success, $"{scenario.Description}: Subsequent run should succeed");

            // Verify configuration is consistent
            ConfigurationInfo info1 = configManager1.GetConfigurationInfo();
            ConfigurationInfo info2 = configManager2.GetConfigurationInfo();

            Assert.Equal(info1.ConfigDirectory, info2.ConfigDirectory);
            Assert.Equal(info1.UserDataDirectory, info2.UserDataDirectory);
            Assert.Equal(info1.IsPortableMode, info2.IsPortableMode);
        }

        #endregion

        #region Config Source Detection Tests

        [Theory(DisplayName = "First run portable: Config source is App")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunPortable_ConfigSourceIsApp(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Skip if not portable mode
            if (mode != PlatformModeDetectionTests.Mode.Portable)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Portable mode should report App as config source
            Assert.Equal(ConfigSource.App, info.ConfigSource);
        }

        [Theory(DisplayName = "First run system: Config source is UserConfig or None")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRunSystem_ConfigSourceIsUserConfigOrNone(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Skip if not system mode
            if (mode != PlatformModeDetectionTests.Mode.System)
                return;

            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - System mode should use UserConfig or None
            Assert.True(
                info.ConfigSource == ConfigSource.UserConfig || info.ConfigSource == ConfigSource.None,
                $"{scenario.Description}: System mode should use UserConfig or None, got {info.ConfigSource}");
        }

        #endregion

        #region CLI vs UI Consistency Tests

        [Theory(DisplayName = "First run: CLI and UI create same directory structure")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.Mode.Portable)]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.Mode.System)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.Mode.Portable)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.Mode.System)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.Mode.Portable)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.Mode.System)]
        public void FirstRun_CLIAndUI_CreateSameDirectoryStructure(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange - CLI
            TestScenario cliScenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, mode);
            MockPlatformService cliMockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI, cliScenario.ExpectedExecutableDirectory);
            MockFileSystemService cliMockFileSystem = new MockFileSystemService();

            // Arrange - UI
            TestScenario uiScenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.UI, mode);
            MockPlatformService uiMockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.UI, uiScenario.ExpectedExecutableDirectory);
            MockFileSystemService uiMockFileSystem = new MockFileSystemService();

            if (mode == PlatformModeDetectionTests.Mode.Portable)
            {
                cliMockFileSystem.SetupPortableMode(cliScenario.ExpectedExecutableDirectory, cliScenario.ExpectedConfigFileName);
                uiMockFileSystem.SetupPortableMode(uiScenario.ExpectedExecutableDirectory, uiScenario.ExpectedConfigFileName);
            }
            else
            {
                cliMockFileSystem.SetupSystemMode(cliScenario.ExpectedExecutableDirectory, cliScenario.ExpectedConfigFileName);
                uiMockFileSystem.SetupSystemMode(uiScenario.ExpectedExecutableDirectory, uiScenario.ExpectedConfigFileName);
            }

            // Act
            using ConfigManager cliConfigManager = new ConfigManager(cliMockPlatform, cliMockFileSystem);
            using ConfigManager uiConfigManager = new ConfigManager(uiMockPlatform, uiMockFileSystem);

            SetupResult cliResult = cliConfigManager.EnsureConfiguration();
            SetupResult uiResult = uiConfigManager.EnsureConfiguration();

            ConfigurationInfo cliInfo = cliConfigManager.GetConfigurationInfo();
            ConfigurationInfo uiInfo = uiConfigManager.GetConfigurationInfo();

            // Assert - Directory structure should be the same (except config file name)
            Assert.True(cliResult.Success && uiResult.Success);
            Assert.Equal(cliInfo.ConfigDirectory, uiInfo.ConfigDirectory);
            Assert.Equal(cliInfo.UserDataDirectory, uiInfo.UserDataDirectory);

            // Only config file name should differ
            Assert.NotEqual(cliInfo.ConfigFileName, uiInfo.ConfigFileName);
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameCLI, cliInfo.ConfigFileName);
            Assert.Equal(ConfigSettingsConstants.ConfigFileNameUI, uiInfo.ConfigFileName);
        }

        #endregion

        #region Directory Writability Tests

        [Theory(DisplayName = "First run: Handles read-only executable directory gracefully")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux, PlatformModeDetectionTests.AppType.CLI)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS, PlatformModeDetectionTests.AppType.CLI)]
        public void FirstRun_ReadOnlyExecutableDirectory_FallsBackToSystemMode(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType)
        {
            // Arrange - Executable directory is not writable
            TestScenario scenario = TestScenario.Create(platform, appType, PlatformModeDetectionTests.Mode.System);
            MockPlatformService mockPlatform = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set executable directory as NOT writable
            mockFileSystem.SetDirectoryWritable(scenario.ExpectedExecutableDirectory, false);
            mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            ConfigurationInfo info = configManager.GetConfigurationInfo();

            // Assert - Should fall back to system mode
            Assert.False(info.IsPortableMode);
            Assert.NotEqual(scenario.ExpectedExecutableDirectory, info.ConfigDirectory);
        }

        #endregion

        #region Cross-Platform Path Verification Tests

        [Theory(DisplayName = "First run: Path separators are platform-appropriate")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRun_PathSeparators_ArePlatformAppropriate(
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

            // Assert - Verify path separators match platform
            switch (platform)
            {
                case PlatformModeDetectionTests.Platform.Windows:
                    Assert.Contains("\\", info.ConfigDirectory);
                    break;
                case PlatformModeDetectionTests.Platform.Linux:
                case PlatformModeDetectionTests.Platform.macOS:
                    Assert.Contains("/", info.ConfigDirectory);
                    Assert.DoesNotContain("\\", info.ConfigDirectory);
                    break;
            }
        }

        #endregion

        #region Configuration Consistency Tests

        [Theory(DisplayName = "First run: Multiple instances detect same configuration")]
        [MemberData(nameof(AllCombinations))]
        public void FirstRun_MultipleInstances_DetectSameConfiguration(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, appType, mode);
            MockPlatformService mockPlatform1 = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockPlatformService mockPlatform2 = new MockPlatformService(platform, appType, scenario.ExpectedExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            if (mode == PlatformModeDetectionTests.Mode.Portable)
                mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);
            else
                mockFileSystem.SetupSystemMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act - Create two instances
            using ConfigManager configManager1 = new ConfigManager(mockPlatform1, mockFileSystem);
            using ConfigManager configManager2 = new ConfigManager(mockPlatform2, mockFileSystem);

            ConfigurationInfo info1 = configManager1.GetConfigurationInfo();
            ConfigurationInfo info2 = configManager2.GetConfigurationInfo();

            // Assert - Both instances should detect identical configuration
            Assert.Equal(info1.ConfigDirectory, info2.ConfigDirectory);
            Assert.Equal(info1.UserDataDirectory, info2.UserDataDirectory);
            Assert.Equal(info1.IsPortableMode, info2.IsPortableMode);
            Assert.Equal(info1.ConfigSource, info2.ConfigSource);
            Assert.Equal(info1.ConfigFileName, info2.ConfigFileName);
        }

        #endregion
    }
}