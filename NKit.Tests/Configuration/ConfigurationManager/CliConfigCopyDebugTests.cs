using Nanook.NKit.Configuration.Models;
using System;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Debug tests to understand why CLI config copy isn't working
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class CliConfigCopyDebugTests
    {
        private readonly ITestOutputHelper _output;

        public CliConfigCopyDebugTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(DisplayName = "Debug: Check ConfigurationContext state")]
        public void Debug_CheckConfigurationContextState()
        {
            // Arrange
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                "c:\\test\\app");
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            mockFileSystem.SetupSystemMode("c:\\test\\app", "nkit.yaml");

            // Set up defaults/nkit.yaml
            string defaultsDir = mockFileSystem.CombinePath("c:\\test\\app", "defaults");
            string defaultsConfigPath = mockFileSystem.CombinePath(defaultsDir, "nkit.yaml");
            mockFileSystem.SetDirectoryExists(defaultsDir, true);
            mockFileSystem.SetFileExists(defaultsConfigPath, true);
            mockFileSystem.SetFileContents(defaultsConfigPath, "# Test config");

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);

            // Output context state
            _output.WriteLine($"ExecutableDirectory: {configManager.Context.ExecutableDirectory}");
            _output.WriteLine($"ConfigDirectory: {configManager.Context.ConfigDirectory}");
            _output.WriteLine($"UserDataDirectory: {configManager.Context.UserDataDirectory}");
            _output.WriteLine($"IsPortableMode: {configManager.Context.IsPortableMode}");
            _output.WriteLine($"ConfigSource: {configManager.Context.ConfigSource}");
            _output.WriteLine($"ConfigFile: {configManager.Context.ConfigFile ?? "null"}");
            _output.WriteLine($"ConfigFileName: {configManager.Context.ConfigFileName}");

            // Check if defaults file exists from context
            string defaultsPath = mockFileSystem.CombinePath(configManager.Context.ExecutableDirectory, "defaults", "nkit.yaml");
            bool defaultsExists = mockFileSystem.FileExists(defaultsPath);
            _output.WriteLine($"Defaults file exists at '{defaultsPath}': {defaultsExists}");

            // Now call EnsureConfiguration
            SetupResult result = configManager.EnsureConfiguration();

            _output.WriteLine($"Result.Success: {result.Success}");
            _output.WriteLine($"Result.ConfigFileCreated: {result.ConfigFileCreated}");
            _output.WriteLine($"Result.DirectoriesCreated: {result.DirectoriesCreated}");

            // Check if target file was created
            string targetPath = mockFileSystem.CombinePath(configManager.Context.ConfigDirectory, "nkit.yaml");
            bool targetExists = mockFileSystem.FileExists(targetPath);
            _output.WriteLine($"Target config exists at '{targetPath}': {targetExists}");

            if (targetExists)
            {
                string content = mockFileSystem.ReadAllText(targetPath);
                _output.WriteLine($"Target content: {content.Substring(0, Math.Min(100, content.Length))}...");
            }

            // Assert
            Assert.True(result.Success);
        }
    }
}