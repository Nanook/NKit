using Nanook.NKit.Configuration.Models;
using System;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Debug test to understand config file creation behavior
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class ConfigFileCreationDebugTest
    {
        private readonly ITestOutputHelper _output;

        public ConfigFileCreationDebugTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(DisplayName = "Debug: CLI with defaults - trace execution")]
        public void Debug_CLI_WithDefaults_TraceExecution()
        {
            // Arrange
            MockPlatformService mockPlatform = new MockPlatformService(
                PlatformModeDetectionTests.Platform.Windows,
                PlatformModeDetectionTests.AppType.CLI,
                "c:\\test\\app");
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // System mode (no local config)
            mockFileSystem.SetupSystemMode("c:\\test\\app", "nkit.yaml");

            // Defaults exist
            string defaultsDir = mockFileSystem.CombinePath("c:\\test\\app", "defaults");
            string defaultsConfigPath = mockFileSystem.CombinePath(defaultsDir, "nkit.yaml");
            _output.WriteLine($"Defaults dir: {defaultsDir}");
            _output.WriteLine($"Defaults config path: {defaultsConfigPath}");

            mockFileSystem.SetDirectoryExists(defaultsDir, true);
            mockFileSystem.SetFileExists(defaultsConfigPath, true);
            mockFileSystem.SetFileContents(defaultsConfigPath, "# Default config");

            _output.WriteLine($"Defaults file exists: {mockFileSystem.FileExists(defaultsConfigPath)}");

            string userConfigDir = mockFileSystem.CombinePath("C:\\Users\\TestUser\\AppData\\Roaming", "nkit");
            string targetConfigPath = mockFileSystem.CombinePath(userConfigDir, "nkit.yaml");
            _output.WriteLine($"Target config dir: {userConfigDir}");
            _output.WriteLine($"Target config path: {targetConfigPath}");

            mockFileSystem.SetDirectoryExists(userConfigDir, true);
            mockFileSystem.SetDirectoryWritable(userConfigDir, true);
            mockFileSystem.SetFileExists(targetConfigPath, false);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatform, mockFileSystem);

            _output.WriteLine($"Config directory: {configManager.Context.ConfigDirectory}");
            _output.WriteLine($"Config file name: {configManager.Context.ConfigFileName}");
            _output.WriteLine($"Is portable mode: {configManager.Context.IsPortableMode}");
            _output.WriteLine($"Config source: {configManager.Context.ConfigSource}");

            SetupResult result = configManager.EnsureConfiguration();

            _output.WriteLine($"Setup success: {result.Success}");
            _output.WriteLine($"Config file created: {result.ConfigFileCreated}");
            _output.WriteLine($"Directories created: {result.DirectoriesCreated}");
            _output.WriteLine($"Fix files copied: {result.FixFilesCopied}");

            _output.WriteLine($"Target file exists after setup: {mockFileSystem.FileExists(targetConfigPath)}");

            if (mockFileSystem.FileExists(targetConfigPath))
            {
                string content = mockFileSystem.ReadAllText(targetConfigPath);
                _output.WriteLine($"Target file content length: {content?.Length ?? 0}");
                _output.WriteLine($"Content preview: {content?.Substring(0, Math.Min(100, content.Length))}");
            }

            // Just output, no assertion
            Assert.True(true);
        }
    }
}