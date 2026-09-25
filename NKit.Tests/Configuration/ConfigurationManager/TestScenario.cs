using Nanook.NKit.Configuration;
using System;


namespace NKit.Tests.Configuration.ConfigurationManager
{
    /// <summary>
    /// Represents a complete test scenario for a specific platform/app/mode combination.
    /// This class encapsulates all expected values for a configuration scenario,
    /// making test expectations crystal clear.
    /// </summary>
    public class TestScenario
    {
        #region Properties

        public PlatformModeDetectionTests.Platform Platform { get; init; }
        public PlatformModeDetectionTests.AppType AppType { get; init; }
        public PlatformModeDetectionTests.Mode Mode { get; init; }

        public string ExpectedConfigFileName { get; init; }
        public string ExpectedExecutableDirectory { get; init; }
        public string ExpectedConfigDirectory { get; init; }
        public string ExpectedUserDataDirectory { get; init; }

        public bool IsBundle { get; init; }
        public string BundlePath { get; init; }

        public string Description => $"{Platform} {AppType} {Mode}";

        #endregion

        #region Factory Methods

        public static TestScenario Create(
            PlatformModeDetectionTests.Platform platform,
            PlatformModeDetectionTests.AppType appType,
            PlatformModeDetectionTests.Mode mode)
        {
            string configFileName = getConfigFileName(appType);
            string executableDir = getExecutableDirectory(platform);

            return new TestScenario
            {
                Platform = platform,
                AppType = appType,
                Mode = mode,
                ExpectedConfigFileName = configFileName,
                ExpectedExecutableDirectory = executableDir,
                ExpectedConfigDirectory = mode == PlatformModeDetectionTests.Mode.Portable
                    ? executableDir
                    : getSystemConfigDirectory(platform),
                ExpectedUserDataDirectory = mode == PlatformModeDetectionTests.Mode.Portable
                    ? executableDir
                    : getSystemUserDataDirectory(platform),
                IsBundle = false,
                BundlePath = null
            };
        }

        public static TestScenario CreateMacOSBundle()
        {
            const string bundlePath = "/Applications/NKit.app";
            const string executableDir = "/Applications/NKit.app/Contents/MacOS";

            return new TestScenario
            {
                Platform = PlatformModeDetectionTests.Platform.macOS,
                AppType = PlatformModeDetectionTests.AppType.UI,
                Mode = PlatformModeDetectionTests.Mode.System,
                ExpectedConfigFileName = "nkit-ui.yaml",
                ExpectedExecutableDirectory = executableDir,
                ExpectedConfigDirectory = "/Users/testuser/Documents/nkit",
                ExpectedUserDataDirectory = "/Users/testuser/Documents",  // Home directory without nkit subdirectory
                IsBundle = true,
                BundlePath = bundlePath
            };
        }

        public static TestScenario CreateMacOSBundlePortable()
        {
            const string bundlePath = "/Applications/NKit.app";
            const string executableDir = "/Applications/NKit.app/Contents/MacOS";
            const string bundleParent = "/Applications";

            return new TestScenario
            {
                Platform = PlatformModeDetectionTests.Platform.macOS,
                AppType = PlatformModeDetectionTests.AppType.UI,
                Mode = PlatformModeDetectionTests.Mode.Portable,
                ExpectedConfigFileName = "nkit-ui.yaml",
                ExpectedExecutableDirectory = executableDir,
                ExpectedConfigDirectory = bundleParent, // Config next to .app
                ExpectedUserDataDirectory = bundleParent, // Same in portable mode
                IsBundle = true,
                BundlePath = bundlePath
            };
        }

        #endregion

        #region Private Helpers

        private static string getConfigFileName(PlatformModeDetectionTests.AppType appType)
        {
            return appType switch
            {
                PlatformModeDetectionTests.AppType.CLI => ConfigSettingsConstants.ConfigFileNameCLI,
                PlatformModeDetectionTests.AppType.UI => ConfigSettingsConstants.ConfigFileNameUI,
                _ => throw new ArgumentException($"Unknown app type: {appType}")
            };
        }

        private static string getExecutableDirectory(PlatformModeDetectionTests.Platform platform)
        {
            return platform switch
            {
                PlatformModeDetectionTests.Platform.Windows => "c:\\test\\app",
                PlatformModeDetectionTests.Platform.Linux => "/test/app",
                PlatformModeDetectionTests.Platform.macOS => "/test/app",
                _ => throw new ArgumentException($"Unknown platform: {platform}")
            };
        }

        private static string getSystemConfigDirectory(PlatformModeDetectionTests.Platform platform)
        {
            return platform switch
            {
                PlatformModeDetectionTests.Platform.Windows =>
                    "C:\\Users\\TestUser\\AppData\\Roaming\\nkit",
                PlatformModeDetectionTests.Platform.Linux =>
                    "/home/testuser/.config/nkit",
                PlatformModeDetectionTests.Platform.macOS =>
                    "/Users/testuser/Documents/nkit",
                _ => throw new ArgumentException($"Unknown platform: {platform}")
            };
        }

        private static string getSystemUserDataDirectory(PlatformModeDetectionTests.Platform platform)
        {
            // After the path fix, UserDataDirectory in system mode is the home directory WITHOUT the nkit subdirectory
            // The nkit subdirectory is added later by DefaultPathProvider and ConfigurationSetup
            return platform switch
            {
                PlatformModeDetectionTests.Platform.Windows =>
                    "C:\\Users\\TestUser",  // Home directory without nkit subdirectory
                PlatformModeDetectionTests.Platform.Linux =>
                    "/home/testuser",  // Home directory without nkit subdirectory
                PlatformModeDetectionTests.Platform.macOS =>
                    "/Users/testuser/Documents",  // Home directory without nkit subdirectory
                _ => throw new ArgumentException($"Unknown platform: {platform}")
            };
        }

        #endregion
    }
}