using Nanook.NKit;
using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ConfigManager = Nanook.NKit.Configuration.ConfigurationManager;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Comprehensive tests covering all 12 platform/app/mode combinations as mentioned in README
    /// Tests all combinations of: Windows/Linux/macOS � CLI/UI � Portable/System
    /// </summary>
    [Trait("Area", "Configuration")]
    [Trait("Group", "ConfigurationManager")]
    public class ConfigurationCombinationTests
    {
        #region Test Configuration Constants

        /// <summary>
        /// Expected configuration results for each platform/app/mode combination
        /// Generated programmatically to avoid duplication
        /// </summary>
        public static readonly Dictionary<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode), ExpectedConfiguration> ExpectedConfigurations =
            generateExpectedConfigurations();

        /// <summary>
        /// Generates all expected configurations programmatically
        /// This eliminates the need to manually define each combination
        /// </summary>
        private static Dictionary<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode), ExpectedConfiguration> generateExpectedConfigurations()
        {
            Dictionary<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode), ExpectedConfiguration> configs = new Dictionary<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode), ExpectedConfiguration>();

            PlatformType[] platforms = Enum.GetValues<PlatformType>();
            ApplicationType[] appTypes = Enum.GetValues<ApplicationType>();
            ConfigurationMode[] modes = Enum.GetValues<ConfigurationMode>();

            foreach (PlatformType platform in platforms)
            {
                foreach (ApplicationType appType in appTypes)
                {
                    foreach (ConfigurationMode mode in modes)
                    {
                        configs[(platform, appType, mode)] = createExpectedConfiguration(platform, appType, mode);
                    }
                }
            }

            return configs;
        }

        /// <summary>
        /// Creates expected configuration for a specific combination
        /// Centralizes the logic for determining paths based on platform/app/mode
        /// </summary>
        private static ExpectedConfiguration createExpectedConfiguration(
            PlatformType platform,
            ApplicationType appType,
            ConfigurationMode mode)
        {
            ExpectedConfiguration config = new ExpectedConfiguration
            {
                ConfigFileName = getConfigFileName(appType),
                IsPortableMode = mode == ConfigurationMode.Portable,
                ExecutableDirectory = getExecutableDirectory(platform),
                ConfigRelativeToExecutable = mode == ConfigurationMode.Portable,
                DirectoriesAreSame = mode == ConfigurationMode.Portable
            };

            // Set directories based on mode
            if (mode == ConfigurationMode.Portable)
            {
                // Portable: everything in executable directory
                config.ConfigDirectory = config.ExecutableDirectory;
                config.UserDataDirectory = config.ExecutableDirectory;
            }
            else
            {
                // System: separate directories
                config.ConfigDirectory = getSystemConfigDirectory(platform);
                config.UserDataDirectory = getSystemUserDirectory(platform);
            }

            return config;
        }

        /// <summary>
        /// Gets the config filename for the application type
        /// </summary>
        private static string getConfigFileName(ApplicationType appType) => appType switch
        {
            ApplicationType.CLI => "nkit.yaml",
            ApplicationType.UI => "nkit-ui.yaml",
            _ => "nkit.yaml"
        };

        /// <summary>
        /// Gets the default executable directory for each platform
        /// </summary>
        private static string getExecutableDirectory(PlatformType platform) => platform switch
        {
            PlatformType.Windows => "c:\\test\\app",
            PlatformType.Linux => "/test/app",
            PlatformType.OSX => "/test/app",
            _ => "/test/app"
        };

        /// <summary>
        /// Gets the system config directory for each platform
        /// </summary>
        private static string getSystemConfigDirectory(PlatformType platform) => platform switch
        {
            PlatformType.Windows => Path.Combine("C:\\Users\\TestUser\\AppData\\Roaming", "nkit"),
            PlatformType.Linux => "/home/testuser/.config/nkit",
            PlatformType.OSX => "/Users/testuser/Documents/nkit",
            _ => "/test/config"
        };

        /// <summary>
        /// Gets the system user data directory for each platform
        /// </summary>
        private static string getSystemUserDirectory(PlatformType platform) => platform switch
        {
            PlatformType.Windows => "C:\\Users\\TestUser",  // Expanded path to match what the real implementation produces
            PlatformType.Linux => "/home/testuser",
            PlatformType.OSX => "/Users/testuser/Documents",
            _ => "/test/user"
        };

        /// <summary>
        /// Special configuration for macOS bundle scenarios
        /// This shows the key difference: executable is 3 levels down from config in bundles
        /// </summary>
        public static readonly ExpectedConfiguration MacOSBundleConfiguration = new ExpectedConfiguration
        {
            ConfigFileName = "nkit-ui.yaml",
            IsPortableMode = false, // Usually system mode unless config exists next to bundle
            ConfigDirectory = "/Users/testuser/Documents/nkit",
            UserDataDirectory = "/Users/testuser/Documents",
            DirectoriesAreSame = true,
            ExecutableDirectory = "/Applications/NKit.app/Contents/MacOS", // 3 levels deep
            ConfigRelativeToExecutable = false,
            IsBundleScenario = true,
            BundlePath = "/Applications/NKit.app",
            ConfigNextToBundleInPortable = true // Key difference: config goes next to .app, not inside
        };

        #endregion

        /// <summary>
        /// Tests all 12 platform/app/mode combinations that the ConfigurationManager should support
        /// Now with exact path verification for each combination
        /// </summary>
        [Theory]
        [InlineData(PlatformType.Windows, ApplicationType.CLI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.Windows, ApplicationType.CLI, ConfigurationMode.System)]
        [InlineData(PlatformType.Windows, ApplicationType.UI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.Windows, ApplicationType.UI, ConfigurationMode.System)]
        [InlineData(PlatformType.Linux, ApplicationType.CLI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.Linux, ApplicationType.CLI, ConfigurationMode.System)]
        [InlineData(PlatformType.Linux, ApplicationType.UI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.Linux, ApplicationType.UI, ConfigurationMode.System)]
        [InlineData(PlatformType.OSX, ApplicationType.CLI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.OSX, ApplicationType.CLI, ConfigurationMode.System)]
        [InlineData(PlatformType.OSX, ApplicationType.UI, ConfigurationMode.Portable)]
        [InlineData(PlatformType.OSX, ApplicationType.UI, ConfigurationMode.System)]
        public void ConfigurationManager_AllPlatformAppModeCombinations_ProduceValidConfiguration(
            PlatformType platformType, ApplicationType appType, ConfigurationMode mode)
        {
            // Arrange
            ExpectedConfiguration expected = ExpectedConfigurations[(platformType, appType, mode)];

            // Use default platform-appropriate paths unless overridden in expected config
            MockPlatformService mockPlatformService = new MockPlatformService(platformType, appType);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up the mode conditions based on expected configuration
            setupModeConditions(mockFileSystem, mode, appType, expected, mockPlatformService);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
            ConfigurationInfo configInfo = configManager.GetConfigurationInfo();

            // Assert - Verify configuration matches EXACT expected results
            Assert.NotNull(configInfo);
            Assert.NotNull(configInfo.ConfigDirectory);
            Assert.NotNull(configInfo.UserDataDirectory);
            Assert.NotNull(configInfo.ConfigFileName);
            Assert.NotNull(configInfo.ExecutableDirectory);

            // Verify specific expected behaviors with exact path matching
            Assert.Equal(expected.ConfigFileName, configInfo.ConfigFileName);
            Assert.Equal(expected.IsPortableMode, configInfo.IsPortableMode);

            // EXACT PATH VERIFICATION - Normalize paths for cross-platform comparison
            // The implementation uses forward slashes internally for consistency
            Assert.Equal(normalizePath(expected.ConfigDirectory), normalizePath(configInfo.ConfigDirectory));
            Assert.Equal(normalizePath(expected.UserDataDirectory), normalizePath(configInfo.UserDataDirectory));

            // Verify directory relationship
            if (expected.DirectoriesAreSame)
            {
                Assert.Equal(configInfo.ConfigDirectory, configInfo.UserDataDirectory);
            }
            else
            {
                Assert.NotEqual(configInfo.ConfigDirectory, configInfo.UserDataDirectory);
            }

            // Output for debugging - shows exactly what the ConfigurationManager produced
            System.Diagnostics.Debug.WriteLine($"[{platformType}/{appType}/{mode}]");
            System.Diagnostics.Debug.WriteLine($"  Expected Portable: {expected.IsPortableMode}");
            System.Diagnostics.Debug.WriteLine($"  Actual Portable:   {configInfo.IsPortableMode}");
            System.Diagnostics.Debug.WriteLine($"  Expected Config:   {expected.ConfigDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Actual Config:     {configInfo.ConfigDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Expected User:     {expected.UserDataDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Actual User:       {configInfo.UserDataDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Executable:        {configInfo.ExecutableDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Config File:       {configInfo.ConfigFileName}");
            System.Diagnostics.Debug.WriteLine("");
        }

        [Fact]
        public void ConfigurationManager_MacOSBundleDetection_WorksCorrectly()
        {
            // Arrange - macOS app bundle structure (the special case)
            ExpectedConfiguration expected = MacOSBundleConfiguration;
            MockPlatformService mockPlatformService = new MockPlatformService(PlatformType.OSX, ApplicationType.UI, expected.ExecutableDirectory);
            MockFileSystemService mockFileSystem = new MockFileSystemService();

            // Set up bundle detection
            mockFileSystem.SetDirectoryExists(expected.BundlePath, true);
            mockFileSystem.SetDirectoryExists("/Applications", true);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
            ConfigurationInfo configInfo = configManager.GetConfigurationInfo();

            // Assert - Verify bundle handling matches EXACT expected behavior
            Assert.NotNull(configInfo);
            Assert.Equal(expected.ConfigFileName, configInfo.ConfigFileName);
            Assert.Equal(expected.ExecutableDirectory, configInfo.ExecutableDirectory);

            // EXACT PATH VERIFICATION for bundle scenario
            Assert.Equal(expected.ConfigDirectory, configInfo.ConfigDirectory);
            Assert.Equal(expected.UserDataDirectory, configInfo.UserDataDirectory);

            // Key bundle behavior: executable is 3 levels down from where config would go
            Assert.Contains("MacOS", configInfo.ExecutableDirectory);
            Assert.Contains("Contents", configInfo.ExecutableDirectory);
            Assert.Contains(".app", configInfo.ExecutableDirectory);

            // Verify mode
            Assert.Equal(expected.IsPortableMode, configInfo.IsPortableMode);

            // Output for debugging - shows bundle scenario paths
            System.Diagnostics.Debug.WriteLine($"[macOS Bundle Scenario]");
            System.Diagnostics.Debug.WriteLine($"  Config:     {configInfo.ConfigDirectory}");
            System.Diagnostics.Debug.WriteLine($"  User Data:  {configInfo.UserDataDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Executable: {configInfo.ExecutableDirectory}");
            System.Diagnostics.Debug.WriteLine($"  Bundle:     {expected.BundlePath}");
            System.Diagnostics.Debug.WriteLine($"  Portable:   {configInfo.IsPortableMode}");
        }

        [Theory]
        [InlineData(SystemType.GameCube, "rvz")]
        [InlineData(SystemType.Wii, "rvz")]
        [InlineData(SystemType.PS3, "deciso")] // Updated: PS3 uses DecISO per nkit.yaml
        [InlineData(SystemType.PSP, "cso")]
        [InlineData(SystemType.Dreamcast, "cue")]
        public void ConfigurationManager_SystemSpecificDefaults_AreConsistent(SystemType systemType, string expectedFormat)
        {
            // Test that defaults are consistent across all platform combinations
            IEnumerable<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode)> testCombinations = ExpectedConfigurations.Keys.Take(6); // Test subset for performance

            foreach ((PlatformType platform, ApplicationType appType, ConfigurationMode mode) in testCombinations)
            {
                // Arrange
                ExpectedConfiguration expected = ExpectedConfigurations[(platform, appType, mode)];
                MockPlatformService mockPlatformService = new MockPlatformService(platform, appType);
                MockFileSystemService mockFileSystem = new MockFileSystemService();
                setupModeConditions(mockFileSystem, mode, appType, expected, mockPlatformService);

                // Act
                using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
                CompleteUiDefaults defaults = configManager.GetUiDefaults(systemType);

                // Assert - Defaults should be consistent regardless of platform/app/mode
                Assert.Equal(expectedFormat, defaults.Conversion.Format);
                Assert.Equal(systemType, defaults.SystemType);

                // Verify the format is valid for this system
                IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(systemType);
                Assert.Contains(supportedFormats, f => string.Equals(f, expectedFormat, StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void ConfigurationManager_PathExpansion_WorksAcrossAllCombinations()
        {
            // Test path expansion across different combinations
            (PlatformType, ApplicationType, ConfigurationMode)[] testCombinations = new[]
            {
                (PlatformType.Windows, ApplicationType.CLI, ConfigurationMode.Portable),
                (PlatformType.Linux, ApplicationType.UI, ConfigurationMode.System),
                (PlatformType.OSX, ApplicationType.CLI, ConfigurationMode.Portable)
            };

            foreach ((PlatformType platform, ApplicationType appType, ConfigurationMode mode) in testCombinations)
            {
                // Arrange
                ExpectedConfiguration expected = ExpectedConfigurations[(platform, appType, mode)];
                MockPlatformService mockPlatformService = new MockPlatformService(platform, appType);
                MockFileSystemService mockFileSystem = new MockFileSystemService();
                setupModeConditions(mockFileSystem, mode, appType, expected, mockPlatformService);

                // Act
                using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
                string expandedConfigPath = configManager.ExpandConfigPath("$configPath$/test");
                string expandedUserPath = configManager.ExpandConfigPath("$userPath$/test");

                // Assert - Path expansion should work
                Assert.NotNull(expandedConfigPath);
                Assert.NotNull(expandedUserPath);
                Assert.DoesNotContain("$configPath$", expandedConfigPath);
                Assert.DoesNotContain("$userPath$", expandedUserPath);
                Assert.Contains("test", expandedConfigPath);
                Assert.Contains("test", expandedUserPath);
            }
        }

        [Fact]
        public void ConfigurationManager_EnsureConfiguration_WorksForAllCombinations()
        {
            // Test configuration creation across all combinations
            foreach (KeyValuePair<(PlatformType Platform, ApplicationType App, ConfigurationMode Mode), ExpectedConfiguration> kvp in ExpectedConfigurations)
            {
                (PlatformType platform, ApplicationType appType, ConfigurationMode mode) = kvp.Key;
                ExpectedConfiguration expected = kvp.Value;

                // Arrange
                MockPlatformService mockPlatformService = new MockPlatformService(platform, appType);
                MockFileSystemService mockFileSystem = new MockFileSystemService();
                setupModeConditions(mockFileSystem, mode, appType, expected, mockPlatformService);

                // Act
                using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
                Nanook.NKit.Configuration.Models.SetupResult result = configManager.EnsureConfiguration();

                // Assert - Should succeed for all combinations
                Assert.NotNull(result);
                Assert.True(result.Success, $"Configuration creation failed for {platform}/{appType}/{mode}");
            }
        }

        [Theory]
        [InlineData(PlatformType.Windows, ApplicationType.CLI)]
        [InlineData(PlatformType.Windows, ApplicationType.UI)]
        [InlineData(PlatformType.Linux, ApplicationType.CLI)]
        [InlineData(PlatformType.Linux, ApplicationType.UI)]
        [InlineData(PlatformType.OSX, ApplicationType.CLI)]
        [InlineData(PlatformType.OSX, ApplicationType.UI)]
        public void ConfigurationManager_PlatformAppCombinations_CreateValidDefaults(
            PlatformType platformType, ApplicationType appType)
        {
            // Use portable mode for this test (simpler setup)
            ExpectedConfiguration expected = ExpectedConfigurations[(platformType, appType, ConfigurationMode.Portable)];

            // Arrange
            MockPlatformService mockPlatformService = new MockPlatformService(platformType, appType);
            MockFileSystemService mockFileSystem = new MockFileSystemService();
            setupModeConditions(mockFileSystem, ConfigurationMode.Portable, appType, expected, mockPlatformService);

            // Act
            using ConfigManager configManager = new ConfigManager(mockPlatformService, mockFileSystem);
            CompleteUiDefaults uiDefaults = configManager.GetUiDefaults(SystemType.Wii);

            // Assert - Verify defaults are complete and valid
            Assert.NotNull(uiDefaults);
            Assert.NotNull(uiDefaults.Paths);
            Assert.NotNull(uiDefaults.Conversion);
            Assert.NotNull(uiDefaults.Extraction);
            Assert.NotNull(uiDefaults.General);

            // Verify all path defaults are set
            Assert.NotNull(uiDefaults.Paths.Out);
            Assert.NotNull(uiDefaults.Paths.Tmp);
            Assert.NotNull(uiDefaults.Paths.LogOut);
            Assert.NotNull(uiDefaults.Paths.Dat);
            Assert.NotNull(uiDefaults.Paths.Keys);

            // Verify conversion defaults are valid
            Assert.NotEmpty(uiDefaults.Conversion.Format);
            Assert.NotEmpty(uiDefaults.Conversion.BlockSize);
            Assert.True(int.Parse(uiDefaults.Conversion.Parallelism) > 0);

            // Verify defaults comply with NKitConfigurationProvider
            ValidationResult formatValidation = ConfigSettingsFormatValidator.ValidateFormatString(uiDefaults.Conversion.Format);
            Assert.True(formatValidation.IsValid, $"Default format should be valid: {formatValidation.ErrorMessage}");
        }

        #region Helper Methods and Test Setup

        /// <summary>
        /// Normalizes a path to use forward slashes for consistent comparison
        /// The implementation uses forward slashes internally
        /// </summary>
        private static string normalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.Replace('\\', '/');
        }

        private static void setupModeConditions(MockFileSystemService fileSystem, ConfigurationMode mode, ApplicationType appType, ExpectedConfiguration expected, MockPlatformService platformService)
        {
            // Use the actual executable directory from the platform service
            string executableDirectory = platformService.GetExecutableDirectory();
            string configPath = fileSystem.CombinePath(executableDirectory, expected.ConfigFileName);

            switch (mode)
            {
                case ConfigurationMode.Portable:
                    // Config file exists locally
                    fileSystem.SetFileExists(configPath, true);
                    fileSystem.SetDirectoryWritable(executableDirectory, true);
                    break;
                case ConfigurationMode.System:
                    // No local config file, but directory is writable
                    fileSystem.SetFileExists(configPath, false);
                    fileSystem.SetDirectoryWritable(executableDirectory, true);
                    break;
            }
        }

        #endregion

        #region Test Support Types

        /// <summary>
        /// Expected configuration results for a specific platform/app/mode combination
        /// This makes the test expectations crystal clear with EXACT paths
        /// </summary>
        [Trait("Area", "Configuration")]
        [Trait("Group", "ConfigurationManager")]
        public class ExpectedConfiguration
        {
            public string ConfigFileName { get; set; }
            public bool IsPortableMode { get; set; }
            public string ConfigDirectory { get; set; } // EXACT path expected
            public string UserDataDirectory { get; set; } // EXACT path expected
            public bool DirectoriesAreSame { get; set; }
            public string ExecutableDirectory { get; set; } // EXACT path expected
            public bool ConfigRelativeToExecutable { get; set; }

            // Bundle-specific properties
            public bool IsBundleScenario { get; set; }
            public string BundlePath { get; set; } // EXACT bundle path
            public bool ConfigNextToBundleInPortable { get; set; }
        }

        // Enums for test parameters
        public enum ConfigurationMode
        {
            Portable,
            System
        }

        public enum ApplicationType
        {
            CLI,
            UI
        }

        public enum PlatformType
        {
            Windows,
            Linux,
            OSX
        }

        #endregion

        #region Mock Services (Enhanced with better simulation)

        [Trait("Area", "Configuration")]
        [Trait("Group", "ConfigurationManager")]
        public class MockPlatformService : IPlatformService
        {
            private readonly PlatformType _platformType;
            private readonly ApplicationType _appType;
            private readonly string _executableDirectory;

            public MockPlatformService(PlatformType platformType, ApplicationType appType, string executableDirectory = null)
            {
                _platformType = platformType;
                _appType = appType;

                // Use platform-appropriate default executable directory if not specified
                _executableDirectory = executableDirectory ?? getDefaultExecutableDirectory(platformType);
            }

            private static string getDefaultExecutableDirectory(PlatformType platformType)
            {
                return platformType switch
                {
                    PlatformType.Windows => "c:\\test\\app",
                    PlatformType.Linux => "/test/app",
                    PlatformType.OSX => "/test/app",
                    _ => "/test/app"
                };
            }

            public bool IsWindows() => _platformType == PlatformType.Windows;
            public bool IsOSX() => _platformType == PlatformType.OSX;
            public bool IsLinux() => _platformType == PlatformType.Linux;

            public string GetExecutableDirectory() => _executableDirectory;

            /// <summary>
            /// Implements GetExecutableName from IPlatformService interface for test scenarios
            /// to simulate proper executable names without reflection
            /// </summary>
            public string GetExecutableName()
            {
                return _appType switch
                {
                    ApplicationType.CLI => "nkit",
                    ApplicationType.UI => "nkit-ui",
                    _ => "nkit"
                };
            }

            public string GetUserProfile()
            {
                return _platformType switch
                {
                    PlatformType.Windows => "C:\\Users\\TestUser",
                    PlatformType.Linux => "/home/testuser",
                    PlatformType.OSX => "/Users/testuser",
                    _ => "/test/user"
                };
            }

            public string GetSpecialFolder(Environment.SpecialFolder folder)
            {
                return _platformType switch
                {
                    PlatformType.Windows when folder == Environment.SpecialFolder.ApplicationData => "C:\\Users\\TestUser\\AppData\\Roaming",
                    PlatformType.Windows when folder == Environment.SpecialFolder.UserProfile => "C:\\Users\\TestUser",
                    PlatformType.Linux => "/home/testuser",
                    PlatformType.OSX when folder == Environment.SpecialFolder.MyDocuments => "/Users/testuser/Documents",
                    PlatformType.OSX => "/Users/testuser",
                    _ => "/test/special"
                };
            }

            public string GetEnvironmentVariable(string name)
            {
                return name switch
                {
                    "HOME" => GetUserProfile(),
                    "HOMEDRIVE" => "C:",
                    "HOMEPATH" => "\\Users\\TestUser",
                    "APPDATA" when _platformType == PlatformType.Windows => GetSpecialFolder(Environment.SpecialFolder.ApplicationData),
                    _ => null
                };
            }

            public string ExpandEnvironmentVariables(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return value;

                // Expand Windows environment variables
                if (_platformType == PlatformType.Windows)
                {
                    value = value.Replace("%HOMEDRIVE%", "C:");
                    value = value.Replace("%HOMEPATH%", "\\Users\\TestUser");
                    value = value.Replace("%USERPROFILE%", GetUserProfile());
                    value = value.Replace("%APPDATA%", GetSpecialFolder(Environment.SpecialFolder.ApplicationData));
                }

                return value;
            }
        }

        [Trait("Area", "Configuration")]
        [Trait("Group", "ConfigurationManager")]
        public class MockFileSystemService : IFileSystemService
        {
            private readonly Dictionary<string, bool> _fileExists = new();
            private readonly Dictionary<string, string> _fileContents = new();
            private readonly Dictionary<string, bool> _directoryExists = new();
            private readonly Dictionary<string, bool> _directoryWritable = new();

            public void SetFileExists(string path, bool exists) => _fileExists[path] = exists;
            public void SetFileContents(string path, string contents) => _fileContents[path] = contents;
            public void SetDirectoryExists(string path, bool exists) => _directoryExists[path] = exists;
            public void SetDirectoryWritable(string path, bool writable) => _directoryWritable[path] = writable;

            public bool FileExists(string path) => _fileExists.TryGetValue(path, out bool exists) && exists;
            public bool DirectoryExists(string path) => _directoryExists.TryGetValue(path, out bool exists) ? exists : true;
            public void CreateDirectory(string path) { }
            public void CopyFile(string sourcePath, string destinationPath) { }
            public void WriteAllText(string path, string content) => _fileContents[path] = content;
            public string ReadAllText(string path) => _fileContents.TryGetValue(path, out string content) ? content : string.Empty;
            public IEnumerable<string> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) => Array.Empty<string>();

            public string CombinePath(params string[] paths)
            {
                if (paths == null || paths.Length == 0) return string.Empty;
                return string.Join("/", paths.Where(p => !string.IsNullOrEmpty(p)));
            }

            public string NormalizePath(string path) => path;

            public string GetParentDirectory(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                char separator = path.Contains('/') ? '/' : '\\';
                int lastSeparatorIndex = path.LastIndexOf(separator);
                return lastSeparatorIndex <= 0 ? null : path.Substring(0, lastSeparatorIndex);
            }
        }

        #endregion
    }
}