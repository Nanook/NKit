using Nanook.NKit.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Shared mock services for all configuration tests.
    /// Provides consistent mocking infrastructure across test classes.
    /// </summary>

    #region Mock Platform Service

    /// <summary>
    /// Mock implementation of IPlatformService for testing configuration behavior
    /// across different platforms
    /// </summary>
    public class MockPlatformService : IPlatformService
    {
        private readonly PlatformModeDetectionTests.Platform _platformType;
        private readonly PlatformModeDetectionTests.AppType _appType;
        private readonly string _executableDirectory;

        public MockPlatformService(
            PlatformModeDetectionTests.Platform platformType,
            PlatformModeDetectionTests.AppType appType,
            string executableDirectory = null)
        {
            _platformType = platformType;
            _appType = appType;
            _executableDirectory = executableDirectory ?? GetDefaultExecutableDirectory(platformType);
        }

        private static string GetDefaultExecutableDirectory(PlatformModeDetectionTests.Platform platformType)
        {
            return platformType switch
            {
                PlatformModeDetectionTests.Platform.Windows => "c:\\test\\app",
                PlatformModeDetectionTests.Platform.Linux => "/test/app",
                PlatformModeDetectionTests.Platform.macOS => "/test/app",
                _ => "/test/app"
            };
        }

        public bool IsWindows() => _platformType == PlatformModeDetectionTests.Platform.Windows;
        public bool IsOSX() => _platformType == PlatformModeDetectionTests.Platform.macOS;
        public bool IsLinux() => _platformType == PlatformModeDetectionTests.Platform.Linux;

        public string GetExecutableDirectory() => _executableDirectory;

        public string GetUserProfile()
        {
            return _platformType switch
            {
                PlatformModeDetectionTests.Platform.Windows => "C:\\Users\\TestUser",
                PlatformModeDetectionTests.Platform.Linux => "/home/testuser",
                PlatformModeDetectionTests.Platform.macOS => "/Users/testuser",
                _ => "/test/user"
            };
        }

        public string GetSpecialFolder(Environment.SpecialFolder folder)
        {
            return _platformType switch
            {
                PlatformModeDetectionTests.Platform.Windows when folder == Environment.SpecialFolder.ApplicationData =>
                    "C:\\Users\\TestUser\\AppData\\Roaming",
                PlatformModeDetectionTests.Platform.Windows when folder == Environment.SpecialFolder.UserProfile =>
                    "C:\\Users\\TestUser",
                PlatformModeDetectionTests.Platform.Linux => "/home/testuser",
                PlatformModeDetectionTests.Platform.macOS => GetMacSpecialFolder(folder),
                _ => "/test/special"
            };
        }

        private string GetMacSpecialFolder(Environment.SpecialFolder folder)
        {
            return folder switch
            {
                Environment.SpecialFolder.ApplicationData => "/Users/testuser/Documents",
                Environment.SpecialFolder.MyDocuments => "/Users/testuser/Documents",
                Environment.SpecialFolder.UserProfile => "/Users/testuser",
                _ => "/Users/testuser"
            };
        }

        public string GetEnvironmentVariable(string name)
        {
            return name switch
            {
                "HOME" => GetUserProfile(),
                "HOMEDRIVE" => "C:",
                "HOMEPATH" => "\\Users\\TestUser",
                "XDG_CONFIG_HOME" => null, // Return null to use default ~/.config
                _ => null
            };
        }

        public string ExpandEnvironmentVariables(string value)
        {
            if (_platformType == PlatformModeDetectionTests.Platform.Windows)
            {
                value = value.Replace("%HOMEDRIVE%", "C:");
                value = value.Replace("%HOMEPATH%", "\\Users\\TestUser");
                value = value.Replace("%USERPROFILE%", GetUserProfile());
            }
            return value;
        }

        /// <summary>
        /// Implements GetExecutableName from IPlatformService interface for test scenarios
        /// to simulate proper executable names without reflection
        /// </summary>
        public string GetExecutableName()
        {
            return _appType switch
            {
                PlatformModeDetectionTests.AppType.CLI => "nkit",
                PlatformModeDetectionTests.AppType.UI => "nkit-ui",
                _ => "nkit"
            };
        }
    }

    #endregion

    #region Mock File System Service

    /// <summary>
    /// Mock implementation of IFileSystemService for testing file system operations
    /// without touching the actual file system
    /// </summary>
    public class MockFileSystemService : IFileSystemService
    {
        private readonly Dictionary<string, bool> _fileExists = new();
        private readonly Dictionary<string, bool> _directoryExists = new();
        private readonly Dictionary<string, bool> _directoryWritable = new();
        private readonly Dictionary<string, string> _fileContents = new();

        #region Configuration Methods

        public void SetFileExists(string path, bool exists) => _fileExists[path] = exists;
        public void SetDirectoryExists(string path, bool exists) => _directoryExists[path] = exists;
        public void SetDirectoryWritable(string path, bool writable) => _directoryWritable[path] = writable;
        public void SetFileContents(string path, string contents) => _fileContents[path] = contents;

        #endregion

        #region IFileSystemService Implementation

        public bool FileExists(string path)
        {
            // Try exact match first
            if (_fileExists.TryGetValue(path, out bool exists) && exists)
                return true;

            // Try normalized version
            if (!string.IsNullOrEmpty(path))
            {
                string normalized = path.Replace('/', System.IO.Path.DirectorySeparatorChar)
                                    .Replace('\\', System.IO.Path.DirectorySeparatorChar);
                if (_fileExists.TryGetValue(normalized, out bool normalizedExists) && normalizedExists)
                    return true;

                // Try the opposite separator
                char oppositeSeparator = System.IO.Path.DirectorySeparatorChar == '\\' ? '/' : '\\';
                string opposite = path.Replace(System.IO.Path.DirectorySeparatorChar, oppositeSeparator);
                if (_fileExists.TryGetValue(opposite, out bool oppositeExists) && oppositeExists)
                    return true;
            }

            return false;
        }

        public bool DirectoryExists(string path) =>
            _directoryExists.TryGetValue(path, out bool exists) ? exists : true; // Default to true for simplicity

        public string CombinePath(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return string.Empty;

            if (paths.Length == 1)
                return paths[0];

            // Use the first path to determine the separator style
            string result = paths[0];
            char separator = DetermineSeparator(result);

            for (int i = 1; i < paths.Length; i++)
            {
                if (string.IsNullOrEmpty(paths[i]))
                    continue;

                // Ensure proper separator between path segments
                if (!result.EndsWith(separator.ToString()) && !paths[i].StartsWith(separator.ToString()))
                {
                    result += separator;
                }
                else if (result.EndsWith(separator.ToString()) && paths[i].StartsWith(separator.ToString()))
                {
                    // Remove duplicate separator
                    result = result.TrimEnd(separator);
                }

                result += paths[i];
            }

            return result;
        }

        public string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // For mock, just return the path as-is
            return path;
        }

        public void WriteAllText(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);

            // Check if we have explicit writability info for this directory
            if (!string.IsNullOrEmpty(directory))
            {
                // Normalize directory path for lookup
                string normalizedDir = directory.Replace('/', System.IO.Path.DirectorySeparatorChar)
                                            .Replace('\\', System.IO.Path.DirectorySeparatorChar);

                // Check both original and normalized paths
                bool hasWritabilityInfo = _directoryWritable.ContainsKey(directory) ||
                                         _directoryWritable.ContainsKey(normalizedDir);

                if (hasWritabilityInfo)
                {
                    bool isWritable = (_directoryWritable.TryGetValue(directory, out bool w1) && w1) ||
                                     (_directoryWritable.TryGetValue(normalizedDir, out bool w2) && w2);

                    if (!isWritable)
                    {
                        throw new UnauthorizedAccessException($"Directory not writable: {directory}");
                    }
                }
            }

            // Always allow the write if we get here (either directory is writable or no writability info)
            _fileContents[path] = content;
            _fileExists[path] = true;
        }

        public string ReadAllText(string path)
        {
            if (_fileContents.TryGetValue(path, out string contents))
                return contents;

            return "";
        }

        public void CreateDirectory(string path) => _directoryExists[path] = true;

        public void CopyFile(string sourcePath, string destinationPath)
        {
            if (_fileContents.TryGetValue(sourcePath, out string contents))
            {
                _fileContents[destinationPath] = contents;
                _fileExists[destinationPath] = true;
            }
        }

        public IEnumerable<string> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) => Array.Empty<string>();

        /// <summary>
        /// Gets the parent directory of a path in a cross-platform manner.
        /// This implementation correctly handles both Unix and Windows paths regardless of host OS.
        /// </summary>
        public string GetParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Determine which separator to use based on the path format
            char separator = path.Contains('/') ? '/' : '\\';

            // Find the last separator
            int lastSeparatorIndex = path.LastIndexOf(separator);

            // If no separator found or path is root, return null
            if (lastSeparatorIndex <= 0)
                return null;

            // Return the parent directory
            return path.Substring(0, lastSeparatorIndex);
        }

        #endregion

        #region Helper Methods

        private static char DetermineSeparator(string path)
        {
            // Check all path segments for Windows indicators
            bool isWindows = path.Contains('\\') ||
                            (path.Length >= 2 && path[1] == ':') || // Drive letter
                            path.Contains("%HOMEDRIVE%") ||         // Windows environment variable
                            path.Contains("%HOMEPATH%") ||          // Windows environment variable
                            path.Contains("AppData") ||             // Windows-specific folder
                            path.StartsWith("C:");                  // Windows drive

            return isWindows ? '\\' : '/';
        }

        #endregion
    }

    #endregion

    #region Test Scenario Builder

    /// <summary>
    /// Fluent builder for creating test scenarios with custom configurations
    /// </summary>
    public class TestScenarioBuilder
    {
        private PlatformModeDetectionTests.Platform _platform = PlatformModeDetectionTests.Platform.Windows;
        private PlatformModeDetectionTests.AppType _appType = PlatformModeDetectionTests.AppType.CLI;
        private PlatformModeDetectionTests.Mode _mode = PlatformModeDetectionTests.Mode.Portable;
        private string _customExecutableDirectory;
        private bool _isBundle;
        private string _bundlePath;

        public TestScenarioBuilder ForPlatform(PlatformModeDetectionTests.Platform platform)
        {
            _platform = platform;
            return this;
        }

        public TestScenarioBuilder ForAppType(PlatformModeDetectionTests.AppType appType)
        {
            _appType = appType;
            return this;
        }

        public TestScenarioBuilder InMode(PlatformModeDetectionTests.Mode mode)
        {
            _mode = mode;
            return this;
        }

        public TestScenarioBuilder WithExecutableDirectory(string directory)
        {
            _customExecutableDirectory = directory;
            return this;
        }

        public TestScenarioBuilder AsMacOSBundle(string bundlePath = null)
        {
            _isBundle = true;
            _platform = PlatformModeDetectionTests.Platform.macOS;
            _appType = PlatformModeDetectionTests.AppType.UI;
            _bundlePath = bundlePath ?? "/Applications/NKit.app";
            return this;
        }

        public TestScenario Build()
        {
            if (_isBundle && _mode == PlatformModeDetectionTests.Mode.Portable)
            {
                return TestScenario.CreateMacOSBundlePortable();
            }
            else if (_isBundle)
            {
                return TestScenario.CreateMacOSBundle();
            }

            TestScenario scenario = TestScenario.Create(_platform, _appType, _mode);

            // Apply custom executable directory if specified
            if (!string.IsNullOrEmpty(_customExecutableDirectory))
            {
                // Create a new scenario with the custom directory
                // This is a workaround since TestScenario properties are init-only
                return new TestScenario
                {
                    Platform = scenario.Platform,
                    AppType = scenario.AppType,
                    Mode = scenario.Mode,
                    ExpectedConfigFileName = scenario.ExpectedConfigFileName,
                    ExpectedExecutableDirectory = _customExecutableDirectory,
                    ExpectedConfigDirectory = _mode == PlatformModeDetectionTests.Mode.Portable
                        ? _customExecutableDirectory
                        : scenario.ExpectedConfigDirectory,
                    ExpectedUserDataDirectory = _mode == PlatformModeDetectionTests.Mode.Portable
                        ? _customExecutableDirectory
                        : scenario.ExpectedUserDataDirectory,
                    IsBundle = scenario.IsBundle,
                    BundlePath = scenario.BundlePath
                };
            }

            return scenario;
        }
    }

    #endregion

    #region Extension Methods

    /// <summary>
    /// Extension methods for easier test setup
    /// </summary>
    public static class MockServiceExtensions
    {
        /// <summary>
        /// Sets up portable mode conditions: config file exists locally and directory is writable
        /// </summary>
        public static void SetupPortableMode(
            this MockFileSystemService fileSystem,
            string executableDirectory,
            string configFileName)
        {
            // Register directory as existing and writable with multiple path variations
            string windowsStyle = executableDirectory.Replace('/', '\\');
            string unixStyle = executableDirectory.Replace('\\', '/');

            fileSystem.SetDirectoryExists(executableDirectory, true);
            fileSystem.SetDirectoryExists(windowsStyle, true);
            fileSystem.SetDirectoryExists(unixStyle, true);
            fileSystem.SetDirectoryWritable(executableDirectory, true);
            fileSystem.SetDirectoryWritable(windowsStyle, true);
            fileSystem.SetDirectoryWritable(unixStyle, true);

            // Create config file path with ALL possible separator combinations
            string[] configPaths = new[]
            {
                fileSystem.CombinePath(executableDirectory, configFileName),
                fileSystem.CombinePath(windowsStyle, configFileName),
                fileSystem.CombinePath(unixStyle, configFileName),
                Path.Combine(executableDirectory, configFileName).Replace('\\', '/'),
                Path.Combine(executableDirectory, configFileName).Replace('/', '\\'),
                Path.Combine(windowsStyle, configFileName),
                Path.Combine(unixStyle, configFileName),
                $"{executableDirectory}/{configFileName}",
                $"{executableDirectory}\\{configFileName}",
                $"{windowsStyle}/{configFileName}",
                $"{windowsStyle}\\{configFileName}",
                $"{unixStyle}/{configFileName}",
                $"{unixStyle}\\{configFileName}"
            };

            // Register config file at ALL possible paths
            foreach (string configPath in configPaths.Distinct())
            {
                fileSystem.SetFileExists(configPath, true);
                fileSystem.SetFileContents(configPath, "# NKit configuration");
            }
        }

        /// <summary>
        /// Sets up system mode conditions: no local config file
        /// </summary>
        public static void SetupSystemMode(
            this MockFileSystemService fileSystem,
            string executableDirectory,
            string configFileName)
        {
            // Normalize the executable directory path
            string normalizedDir = executableDirectory?.Replace('/', System.IO.Path.DirectorySeparatorChar)
                                                    .Replace('\\', System.IO.Path.DirectorySeparatorChar);

            // Set directory to exist and be writable
            fileSystem.SetDirectoryExists(normalizedDir, true);
            fileSystem.SetDirectoryExists(executableDirectory, true);
            fileSystem.SetDirectoryWritable(normalizedDir, true);
            fileSystem.SetDirectoryWritable(executableDirectory, true);

            // Create the config file path using the mock's CombinePath
            string configPath = fileSystem.CombinePath(executableDirectory, configFileName);
            string normalizedConfigPath = configPath?.Replace('/', System.IO.Path.DirectorySeparatorChar)
                                                  .Replace('\\', System.IO.Path.DirectorySeparatorChar);

            // Ensure config file does NOT exist (system mode)
            fileSystem.SetFileExists(configPath, false);
            fileSystem.SetFileExists(normalizedConfigPath, false);

            // Also try with the normalized directory
            string configPathNormalized = fileSystem.CombinePath(normalizedDir, configFileName);
            fileSystem.SetFileExists(configPathNormalized, false);
        }

        /// <summary>
        /// Sets up macOS bundle detection
        /// </summary>
        public static void SetupBundleDetection(
            this MockFileSystemService fileSystem,
            string bundlePath)
        {
            fileSystem.SetDirectoryExists(bundlePath, true);
            fileSystem.SetDirectoryExists(System.IO.Path.GetDirectoryName(bundlePath), true);
        }
    }

    #endregion
}