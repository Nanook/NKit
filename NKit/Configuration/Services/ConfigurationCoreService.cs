using Nanook.NKit.Configuration.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Platform context detector - determines platform type and bundle status
    /// </summary>
    internal interface IPlatformContextDetector
    {
        PlatformContext DetectPlatformContext(IPlatformService platformService);
    }

    /// <summary>
    /// Mode detector - determines whether to use portable or system mode
    /// </summary>
    internal interface IModeDetector
    {
        bool ShouldUsePortableMode(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem);
    }

    /// <summary>
    /// Service for determining config file names based on executable
    /// </summary>
    internal interface IConfigFileNameResolver
    {
        string GetConfigFileName(IPlatformService platformService);
    }

    /// <summary>
    /// Configuration source detector - finds existing config files
    /// </summary>
    internal interface IConfigSourceDetector
    {
        ConfigurationContext DetectConfigSource(ConfigurationContext context, IFileSystemService fileSystem);
    }

    /// <summary>
    /// Core configuration service that handles platform detection, mode detection, config file naming, and config source detection
    /// </summary>
    internal class ConfigurationCoreService : IPlatformContextDetector, IModeDetector, IConfigFileNameResolver, IConfigSourceDetector
    {
        // ======= Platform Context Detection =======

        public PlatformContext DetectPlatformContext(IPlatformService platformService)
        {
            string executableDirectory = platformService.GetExecutableDirectory();
            PlatformType platform = getPlatformType(platformService);
            (bool isBundle, string appType) = detectBundleAndAppType(platform, executableDirectory, platformService);

            return new PlatformContext(platform, executableDirectory, isBundle, appType);
        }

        private static PlatformType getPlatformType(IPlatformService platformService)
        {
            if (platformService.IsWindows()) return PlatformType.Windows;
            if (platformService.IsOSX()) return PlatformType.MacOS;
            if (platformService.IsLinux()) return PlatformType.Linux;

            // Default to Windows if unknown
            return PlatformType.Windows;
        }

        private static (bool isBundle, string appType) detectBundleAndAppType(
            PlatformType platform, string executableDirectory, IPlatformService platformService)
        {
            // Use centralized PathResolutionService for bundle detection (keeps obfuscation logic in one place)
            bool isBundle = false;
            try
            {
                IFileSystemService fileSystem = new FileSystemService();
                PathResolutionService pathResolution = new PathResolutionService(platformService, fileSystem);
                isBundle = pathResolution.IsWithinBundle(executableDirectory);
            }
            catch
            {
                isBundle = false;
            }

            // Determine app type from executable name
            string appType = determineAppType(platformService);

            return (isBundle, appType);
        }

        private static string determineAppType(IPlatformService platformService)
        {
            try
            {
                string executableDirectory = platformService.GetExecutableDirectory();

                // Check if executable path contains UI indicators
                if (executableDirectory.Contains("nkit-ui", StringComparison.OrdinalIgnoreCase) ||
                    executableDirectory.Contains("nkds-ui", StringComparison.OrdinalIgnoreCase) ||
                    executableDirectory.Contains("NKit.app", StringComparison.OrdinalIgnoreCase) ||
                    executableDirectory.Contains("NkdsUi.app", StringComparison.OrdinalIgnoreCase))
                    return "UI";

                // Default to CLI
                return "CLI";
            }
            catch
            {
                return "CLI";
            }
        }

        // ======= End Platform Context Detection =======

        // ======= Mode Detection =======

        public bool ShouldUsePortableMode(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem)
        {
            // Disable portable mode for macOS UI apps to avoid translocation/permission issues.
            // For macOS UI apps, allow portable mode only if running from a .app bundle with config next to bundle.
            // Otherwise disable portable mode to avoid translocation/permission issues.
            if (platformContext.Platform == PlatformType.MacOS &&
                string.Equals(platformContext.AppType, "UI", StringComparison.OrdinalIgnoreCase) &&
                !platformContext.IsBundle)
            {
                return false;
            }

            // Get the potential config directory for portable mode
            string portableConfigDirectory = getPortableConfigDirectory(platformContext, fileSystem);
            string configFileName = GetConfigFileName(platformService);
            string configPath = fileSystem.CombinePath(portableConfigDirectory, configFileName);

            // Check criteria for portable mode
            bool hasLocalConfig = fileSystem.FileExists(configPath);
            bool canWriteLocally = isDirectoryWritable(portableConfigDirectory, fileSystem);

            // Special logic for bundles (any platform)
            if (platformContext.IsBundle)
                // For bundles, require BOTH config file AND writable directory
                return hasLocalConfig && canWriteLocally;

            // **CORE FIX**: Only use portable mode when config file explicitly exists
            // This ensures UI defaults to system mode and creates config in $configPath$
            // Previously: return hasLocalConfig || canWriteLocally
            // Now: Only portable if config file exists
            return hasLocalConfig;
        }

        private static string getPortableConfigDirectory(PlatformContext platformContext, IFileSystemService fileSystem)
        {
            if (platformContext.IsBundle)
                return getBundleConfigDirectory(platformContext.ExecutableDirectory, fileSystem);

            return platformContext.ExecutableDirectory;
        }

        private static string getBundleConfigDirectory(string executableDirectory, IFileSystemService fileSystem)
        {
            try
            {
                string bundlePath = executableDirectory;

                while (bundlePath.Length > 1 && !bundlePath.EndsWith(".app"))
                {
                    bundlePath = fileSystem.GetParentDirectory(bundlePath);
                    if (bundlePath == null) break;
                }

                if (bundlePath != null && bundlePath.EndsWith(".app"))
                {
                    string parentDirectory = fileSystem.GetParentDirectory(bundlePath);
                    if (!string.IsNullOrEmpty(parentDirectory) && fileSystem.DirectoryExists(parentDirectory))
                        return parentDirectory.Replace('\\', '/');
                }
            }
            catch
            {
                // Fall back to executable directory
            }

            return executableDirectory;
        }

        private static bool isDirectoryWritable(string directoryPath, IFileSystemService fileSystem)
        {
            try
            {
                string testFile = fileSystem.CombinePath(directoryPath, $"test_write_{Guid.NewGuid()}.tmp");
                fileSystem.WriteAllText(testFile, "test");

                // Clean up if using real file system
                if (fileSystem.GetType().Name == "FileSystemService")
                    File.Delete(testFile);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ======= End Mode Detection =======

        // ======= Config File Name Resolution =======

        public string GetConfigFileName(IPlatformService platformService)
        {
            try
            {
                string executableName = getExecutableName(platformService);
                if (string.IsNullOrEmpty(executableName))
                    return ConfigSettingsConstants.ConfigFileNameCLI;

                // nkds shares the nkit pipeline configuration — always use nkit.yaml regardless of
                // the executable name so that a local nkit.yaml next to the nkds exe is detected as
                // portable mode and the user-area config is found in the standard nkit config dir.
                if (string.Equals(executableName, "nkds", StringComparison.OrdinalIgnoreCase))
                    return ConfigSettingsConstants.ConfigFileNameCLI;

                return $"{executableName}{ConfigSettingsConstants.ConfigFileExtension}";
            }
            catch
            {
                return ConfigSettingsConstants.ConfigFileNameCLI;
            }
        }

        private static string getExecutableName(IPlatformService platformService)
        {
            // Check if platform service provides a simulated executable name (for tests)
            string simulatedName = platformService.GetExecutableName();
            if (!string.IsNullOrEmpty(simulatedName))
                return cleanExecutableName(simulatedName);

            // Standard logic for real platform service
            return getExecutableNameFromAssemblyOrProcess();
        }

        private static string getExecutableNameFromAssemblyOrProcess()
        {
            try
            {
                string executableName = null;

                // Try getting from process main module first (most accurate on all platforms)
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                    {
                        if (process.MainModule != null)
                            executableName = Path.GetFileName(process.MainModule.FileName);
                    }
                }
                catch
                {
                    // MainModule can throw on some platforms, fall back to entry assembly
                }

                // Fallback to entry assembly
                if (string.IsNullOrEmpty(executableName))
                    executableName = Assembly.GetEntryAssembly()?.GetName().Name;

                // Fallback to process name
                if (string.IsNullOrEmpty(executableName))
                    executableName = Process.GetCurrentProcess().ProcessName;

                return cleanExecutableName(executableName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cleans the executable name by removing platform-specific extensions and handling special cases
        /// </summary>
        private static string cleanExecutableName(string executableName)
        {
            if (string.IsNullOrEmpty(executableName))
                return null;

            // Handle NixOS wrapped executables (e.g., ".nkit-wrapped" -> "nkit")
            if (executableName.Length > 0 && executableName[0] == '.' && executableName.EndsWith("-wrapped"))
                executableName = executableName.Substring(1, executableName.Length - "-wrapped".Length - 1);

            // Remove .exe extension if present (Windows)
            // Using case-insensitive check to handle .EXE, .Exe, etc.
            if (executableName.ToLowerInvariant().Contains(".exe"))
                executableName = Path.GetFileNameWithoutExtension(executableName);

            return executableName;
        }

        // ======= End Config File Name Resolution =======

        // ======= Config Source Detection =======

        public ConfigurationContext DetectConfigSource(ConfigurationContext context, IFileSystemService fileSystem)
        {
            // Priority 1: Check for config in the determined config directory
            string configPath = fileSystem.CombinePath(context.ConfigDirectory, context.ConfigFileName);
            if (fileSystem.FileExists(configPath))
            {
                ConfigSource source = context.IsPortableMode ? ConfigSource.App : ConfigSource.UserConfig;
                return context.WithConfigFile(source, configPath);
            }

            // Priority 2: Check for config in executable directory (backwards compatibility)
            if (!context.IsPortableMode)
            {
                string executableConfigPath = fileSystem.CombinePath(context.ExecutableDirectory, context.ConfigFileName);
                if (fileSystem.FileExists(executableConfigPath))
                    return context.WithConfigFile(ConfigSource.App, executableConfigPath);
            }

            // No config found
            return context.WithConfigFile(ConfigSource.None, null);
        }

        // ======= End Config Source Detection =======
    }
}
