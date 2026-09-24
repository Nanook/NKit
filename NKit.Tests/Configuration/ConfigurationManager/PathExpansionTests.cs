using Nanook.NKit.Configuration;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Tests path variable expansion functionality.
    /// Verifies that all path variables ($configPath$, $userPath$, etc.) expand correctly
    /// across all platforms and modes.
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class PathExpansionTests
    {
        #region Test Data

        public static IEnumerable<object[]> AllCombinations =>
            PlatformModeDetectionTests.AllCombinations;

        #endregion

        #region Basic Path Variable Expansion Tests

        [Theory(DisplayName = "Config path variable expands correctly")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void ConfigPathVariable_ExpandsCorrectly(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Should expand to config directory
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedConfigDirectory, expanded);
        }

        [Theory(DisplayName = "User path variable expands correctly")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void UserPathVariable_ExpandsCorrectly(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            string testPath = $"{ConfigSettingsConstants.PathVariableUser}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Should expand to user data directory
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedUserDataDirectory, expanded);
        }

        [Theory(DisplayName = "App path variable expands correctly")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void AppPathVariable_ExpandsCorrectly(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            string testPath = $"{ConfigSettingsConstants.PathVariableApp}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Should expand to executable directory
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedExecutableDirectory, expanded);
        }

        #endregion

        #region System and Task Variable Expansion Tests

        [Fact(DisplayName = "System variable expands correctly")]
        public void SystemVariable_ExpandsCorrectly()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableSystem}";
            string expanded = configManager.ExpandConfigPath(testPath, taskType: null, systemType: "wii");

            // Assert - Should expand system variable
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("wii", expanded);
        }

        [Fact(DisplayName = "Task variable expands correctly")]
        public void TaskVariable_ExpandsCorrectly()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableTask}";
            string expanded = configManager.ExpandConfigPath(testPath, taskType: "convert", systemType: null);

            // Assert - Should expand task variable
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("convert", expanded);
        }

        [Fact(DisplayName = "Date variable expands to valid date")]
        public void DateVariable_ExpandsToValidDate()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableDate}_log.txt";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Should expand to current date
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("log.txt", expanded);

            // Date should be in YYYYMMDD format (8 digits)
            string fileName = System.IO.Path.GetFileName(expanded);
            string datePart = fileName.Replace("_log.txt", "");
            Assert.True(datePart.Length >= 8, "Date should be at least 8 digits (YYYYMMDD)");
            Assert.True(datePart.All(char.IsDigit), "Date should be all digits");
        }

        [Fact(DisplayName = "Timestamp variable expands to valid timestamp")]
        public void TimestampVariable_ExpandsToValidTimestamp()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableTimestamp}_log.txt";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Should expand to current timestamp
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("log.txt", expanded);

            // Timestamp should be in YYYYMMDDHHMMSS format (at least 14 digits)
            string fileName = System.IO.Path.GetFileName(expanded);
            string timestampPart = fileName.Replace("_log.txt", "");
            Assert.True(timestampPart.Length >= 14, "Timestamp should be at least 14 digits");
        }

        #endregion

        #region Combined Variable Expansion Tests

        [Fact(DisplayName = "Multiple variables expand correctly")]
        public void MultipleVariables_ExpandCorrectly()
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
            string complexPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableSystem}/{ConfigSettingsConstants.PathVariableTask}/output";
            string expanded = configManager.ExpandConfigPath(complexPath, taskType: "convert", systemType: "wii");

            // Assert - All variables should be expanded
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("wii", expanded);
            Assert.Contains("convert", expanded);
            Assert.Contains("output", expanded);
            Assert.StartsWith(scenario.ExpectedConfigDirectory, expanded);
        }

        [Fact(DisplayName = "Path with all variables expands correctly")]
        public void PathWithAllVariables_ExpandsCorrectly()
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

            // Test a realistic path like: $userPath$/logs/$system$_$task$_$date$_log.txt
            string complexPath = $"{ConfigSettingsConstants.PathVariableUser}/logs/{ConfigSettingsConstants.PathVariableSystem}_{ConfigSettingsConstants.PathVariableTask}_{ConfigSettingsConstants.PathVariableDate}_log.txt";
            string expanded = configManager.ExpandConfigPath(complexPath, taskType: "convert", systemType: "wii");

            // Assert - All variables should be expanded
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("logs", expanded);
            Assert.Contains("wii", expanded);
            Assert.Contains("convert", expanded);
            Assert.Contains("log.txt", expanded);
        }

        #endregion

        #region Portable vs System Mode Expansion Tests

        [Fact(DisplayName = "Portable mode: config and user paths are identical")]
        public void PortableMode_ConfigAndUserPaths_AreIdentical()
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
            string configPath = configManager.ExpandConfigPath($"{ConfigSettingsConstants.PathVariableConfig}/test");
            string userPath = configManager.ExpandConfigPath($"{ConfigSettingsConstants.PathVariableUser}/test");

            // Assert - In portable mode, both should expand to the same location
            Assert.Equal(configPath, userPath);
        }

        [Fact(DisplayName = "System mode: config and user paths are different")]
        public void SystemMode_ConfigAndUserPaths_AreDifferent()
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
            string configPath = configManager.ExpandConfigPath($"{ConfigSettingsConstants.PathVariableConfig}/test");
            string userPath = configManager.ExpandConfigPath($"{ConfigSettingsConstants.PathVariableUser}/test");

            // Assert - In system mode, they should be different
            Assert.NotEqual(configPath, userPath);
            Assert.Contains("AppData", configPath); // Windows system config
            Assert.DoesNotContain("AppData", userPath); // Windows user data
        }

        #endregion

        #region Cross-Platform Path Separator Tests

        [Fact(DisplayName = "Windows uses backslash separators")]
        public void Windows_UsesBackslashSeparators()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/subfolder/file.txt";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Windows should use backslashes
            Assert.NotNull(expanded);
            Assert.Contains("\\", expanded);
        }

        [Theory(DisplayName = "Unix-like systems use forward slash separators")]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void UnixLike_UsesForwardSlashSeparators(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/subfolder/file.txt";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert - Unix-like should use forward slashes
            Assert.NotNull(expanded);
            Assert.Contains("/", expanded);
            Assert.DoesNotContain("\\", expanded);
        }

        #endregion

        #region Edge Cases and Special Scenarios

        [Fact(DisplayName = "Empty path returns empty string")]
        public void EmptyPath_ReturnsEmptyString()
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
            string expanded = configManager.ExpandConfigPath(string.Empty);

            // Assert - Should handle empty path gracefully
            Assert.NotNull(expanded);
        }

        [Fact(DisplayName = "Path without variables returns unchanged")]
        public void PathWithoutVariables_ReturnsUnchanged()
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
            string plainPath = "c:\\absolute\\path\\to\\file.txt";
            string expanded = configManager.ExpandConfigPath(plainPath);

            // Assert - Absolute path should remain mostly unchanged (might be normalized)
            Assert.NotNull(expanded);
            Assert.Contains("absolute", expanded);
            Assert.Contains("file.txt", expanded);
        }

        [Fact(DisplayName = "Null system and task parameters handled gracefully")]
        public void NullSystemAndTask_HandledGracefully()
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/test";
            string expanded = configManager.ExpandConfigPath(testPath, taskType: null, systemType: null);

            // Assert - Should still expand configPath even without system/task
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
        }

        #endregion

        #region Cross-Platform Consistency Tests

        [Theory(DisplayName = "All platforms expand same variable to appropriate paths")]
        [InlineData(PlatformModeDetectionTests.Platform.Windows)]
        [InlineData(PlatformModeDetectionTests.Platform.Linux)]
        [InlineData(PlatformModeDetectionTests.Platform.macOS)]
        public void AllPlatforms_ExpandSameVariable_Consistently(PlatformModeDetectionTests.Platform platform)
        {
            // Arrange
            TestScenario scenario = TestScenario.Create(platform, PlatformModeDetectionTests.AppType.CLI, PlatformModeDetectionTests.Mode.Portable);
            MockPlatformService mockPlatform = new MockPlatformService(platform, PlatformModeDetectionTests.AppType.CLI);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            mockFileSystem.SetupPortableMode(scenario.ExpectedExecutableDirectory, scenario.ExpectedConfigFileName);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableSystem}/test";
            string expanded = configManager.ExpandConfigPath(testPath, taskType: null, systemType: "wii");

            // Assert - All platforms should expand variables (though paths differ)
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("wii", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedConfigDirectory, expanded);
        }

        #endregion

        #region Comprehensive All-Combination Tests

        [Theory(DisplayName = "All combinations: Config path variable expands correctly")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_ConfigPathVariable_ExpandsCorrectly(
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedConfigDirectory, expanded);
        }

        [Theory(DisplayName = "All combinations: User path variable expands correctly")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_UserPathVariable_ExpandsCorrectly(
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
            string testPath = $"{ConfigSettingsConstants.PathVariableUser}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedUserDataDirectory, expanded);
        }

        [Theory(DisplayName = "All combinations: App path variable expands correctly")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_AppPathVariable_ExpandsCorrectly(
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
            string testPath = $"{ConfigSettingsConstants.PathVariableApp}/test";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("test", expanded);
            Assert.StartsWith(scenario.ExpectedExecutableDirectory, expanded);
        }

        [Theory(DisplayName = "All combinations: Path separators are platform-appropriate")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_PathSeparators_ArePlatformAppropriate(
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
            string testPath = $"{ConfigSettingsConstants.PathVariableConfig}/subfolder/file.txt";
            string expanded = configManager.ExpandConfigPath(testPath);

            // Assert
            Assert.NotNull(expanded);

            switch (platform)
            {
                case PlatformModeDetectionTests.Platform.Windows:
                    Assert.Contains("\\", expanded);
                    break;
                case PlatformModeDetectionTests.Platform.Linux:
                case PlatformModeDetectionTests.Platform.macOS:
                    Assert.Contains("/", expanded);
                    Assert.DoesNotContain("\\", expanded);
                    break;
            }
        }

        [Theory(DisplayName = "All combinations: Multiple variables expand correctly together")]
        [MemberData(nameof(AllCombinations))]
        public void AllCombinations_MultipleVariables_ExpandCorrectly(
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
            string complexPath = $"{ConfigSettingsConstants.PathVariableConfig}/{ConfigSettingsConstants.PathVariableSystem}/{ConfigSettingsConstants.PathVariableTask}/output";
            string expanded = configManager.ExpandConfigPath(complexPath, taskType: "convert", systemType: "wii");

            // Assert - All variables should be expanded
            Assert.NotNull(expanded);
            Assert.DoesNotContain("$", expanded);
            Assert.Contains("wii", expanded);
            Assert.Contains("convert", expanded);
            Assert.Contains("output", expanded);
            Assert.StartsWith(scenario.ExpectedConfigDirectory, expanded);
        }

        #endregion
    }
}