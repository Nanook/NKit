using Nanook.NKit.Configuration.Models;
using System;
using System.Collections.Generic;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Centralized service for all path resolution operations including environment variables and base paths.
    /// This class consolidates all string constants and environment variable access to enable obfuscation/scrambling.
    /// Uses enums for type safety and to avoid hardcoded string literals in the executable.
    /// </summary>
    internal class PathResolutionService
    {
        // Static dictionaries for potential obfuscation in static constructor
        private static Dictionary<EnvironmentVariableType, string> _environmentVariables;
        private static Dictionary<PathComponentType, string> _pathComponents;
        private static Dictionary<SpecialFolderType, Environment.SpecialFolder> _specialFolders;

        static PathResolutionService()
        {
            // Initialize all string constants - these can be obfuscated/scrambled later
            initializePathMappings();
        }

        /// <summary>
        /// Initialize all path-related mappings in one place for potential obfuscation
        /// </summary>
        private static void initializePathMappings()
        {
            // Use obfuscated mappings instead of hardcoded strings
            _environmentVariables = StringObfuscation.GetDeobfuscatedEnvironmentVariables();
            _pathComponents = StringObfuscation.GetDeobfuscatedPathComponents();

            // Special folder mappings (these are enum values, not strings, so they're safe)
            _specialFolders = new Dictionary<SpecialFolderType, Environment.SpecialFolder>
            {
                [SpecialFolderType.UserProfile] = Environment.SpecialFolder.UserProfile,
                [SpecialFolderType.ApplicationData] = Environment.SpecialFolder.ApplicationData,
                [SpecialFolderType.MyDocuments] = Environment.SpecialFolder.MyDocuments
            };
        }

        private readonly IPlatformService _platformService;
        private readonly IFileSystemService _fileSystem;

        /// <summary>
        /// Creates a new path resolution service
        /// </summary>
        /// <param name="platformService">Platform service for environment access</param>
        /// <param name="fileSystem">File system service for path operations</param>
        public PathResolutionService(IPlatformService platformService, IFileSystemService fileSystem)
        {
            _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        // ======= Environment Variable Access =======

        /// <summary>
        /// Gets an environment variable value using enum-based lookup
        /// </summary>
        /// <param name="variableType">Type of environment variable to retrieve</param>
        /// <returns>Environment variable value or null if not found</returns>
        private string getEnvironmentVariable(EnvironmentVariableType variableType)
        {
            if (_environmentVariables.TryGetValue(variableType, out string variableName))
                return _platformService.GetEnvironmentVariable(variableName);

            return null;
        }

        /// <summary>
        /// Gets a path component string using enum-based lookup
        /// </summary>
        /// <param name="componentType">Type of path component to retrieve</param>
        /// <returns>Path component string</returns>
        private string getPathComponent(PathComponentType componentType) => _pathComponents.TryGetValue(componentType, out string component) ? component : string.Empty;

        /// <summary>
        /// Gets a special folder using enum-based lookup
        /// </summary>
        /// <param name="folderType">Type of special folder to retrieve</param>
        /// <returns>Special folder path</returns>
        private string getSpecialFolder(SpecialFolderType folderType)
        {
            if (_specialFolders.TryGetValue(folderType, out Environment.SpecialFolder folder))
                return _platformService.GetSpecialFolder(folder);

            return string.Empty;
        }

        /// <summary>
        /// Gets the user's home directory using platform-appropriate methods
        /// </summary>
        public string GetUserHomeDirectory()
        {
            if (_platformService.IsWindows())
                return getWindowsUserHome();
            // For macOS, treat the effective user data base as the user's Documents folder
            // so that user-data paths resolve to ~/Documents on macOS by default.
            if (_platformService.IsOSX())
                return GetUserDocumentsDirectory();

            return getUnixUserHome();
        }

        /// <summary>
        /// Gets the user's Documents folder (cross-platform mapping: Windows MyDocuments, macOS ~/Documents, Linux ~/Documents)
        /// </summary>
        public string GetUserDocumentsDirectory()
        {
            // Prefer platform service first
            string docs = _platformService.GetSpecialFolder(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                return docs;

            // Fallback to combining home + Documents
            string home = GetUserHomeDirectory();
            return _fileSystem.CombinePath(home, "Documents");
        }

        /// <summary>
        /// Gets Windows user home directory
        /// </summary>
        private string getWindowsUserHome()
        {
            // Try HOMEDRIVE + HOMEPATH first
            string homeDrive = getEnvironmentVariable(EnvironmentVariableType.HomeDrive);
            string homePath = getEnvironmentVariable(EnvironmentVariableType.HomePath);

            if (!string.IsNullOrEmpty(homeDrive) && !string.IsNullOrEmpty(homePath))
                return homeDrive + homePath;

            // Fallback to USERPROFILE
            string userProfile = getEnvironmentVariable(EnvironmentVariableType.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
                return userProfile;

            // Final fallback to SpecialFolder
            return getSpecialFolder(SpecialFolderType.UserProfile);
        }

        /// <summary>
        /// Gets Unix/Linux/macOS user home directory
        /// </summary>
        private string getUnixUserHome()
        {
            string home = getEnvironmentVariable(EnvironmentVariableType.Home);
            if (!string.IsNullOrEmpty(home))
                return home;

            // Fallback to SpecialFolder
            return getSpecialFolder(SpecialFolderType.UserProfile);
        }

        /// <summary>
        /// Gets the XDG config home directory for Linux
        /// </summary>
        public string GetXdgConfigHome() => getEnvironmentVariable(EnvironmentVariableType.XdgConfigHome);

        // ======= End Environment Variable Access =======

        // ======= System Directory Resolution =======

        /// <summary>
        /// Gets the system configuration directory for the given platform
        /// </summary>
        /// <param name="platform">Target platform</param>
        /// <returns>System configuration directory path</returns>
        public string GetSystemConfigDirectory(PlatformType platform)
        {
            return platform switch
            {
                PlatformType.Windows => getWindowsConfigDirectory(),
                PlatformType.MacOS => getMacOSConfigDirectory(),
                PlatformType.Linux => getLinuxConfigDirectory(),
                _ => throw new NotSupportedException($"Platform {platform} is not supported")
            };
        }

        /// <summary>
        /// Gets Windows configuration directory (%APPDATA%/nkit)
        /// </summary>
        private string getWindowsConfigDirectory()
        {
            string appDataPath = getSpecialFolder(SpecialFolderType.ApplicationData);
            string appDirName = getPathComponent(PathComponentType.ApplicationDirectoryName);
            return _fileSystem.CombinePath(appDataPath, appDirName);
        }

        /// <summary>
        /// Gets macOS configuration directory (~/Documents/nkit)
        /// </summary>
        private string getMacOSConfigDirectory()
        {
            // Per request: use the user's Documents folder for macOS config directory
            // Prefer platform service SpecialFolder if available, fallback to GetUserDocumentsDirectory()
            string appDirName = getPathComponent(PathComponentType.ApplicationDirectoryName);
            string docs = _platformService.GetSpecialFolder(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                return _fileSystem.CombinePath(docs, appDirName);

            string userDocs = GetUserDocumentsDirectory();
            return _fileSystem.CombinePath(userDocs, appDirName);
        }

        /// <summary>
        /// Gets Linux configuration directory (~/.config/nkit or $XDG_CONFIG_HOME/nkit)
        /// </summary>
        private string getLinuxConfigDirectory()
        {
            string xdgConfigHome = GetXdgConfigHome();
            string baseConfigDir = !string.IsNullOrEmpty(xdgConfigHome)
                ? xdgConfigHome
                : _fileSystem.CombinePath(GetUserHomeDirectory(), getPathComponent(PathComponentType.Config));

            string appDirName = getPathComponent(PathComponentType.ApplicationDirectoryName);
            return _fileSystem.CombinePath(baseConfigDir, appDirName);
        }

        // ======= End System Directory Resolution =======

        // ======= Bundle Detection =======

        /// <summary>
        /// Checks if the given path is within a macOS-style bundle
        /// </summary>
        /// <param name="executablePath">Path to check</param>
        /// <returns>True if path is within a bundle structure</returns>
        public bool IsWithinBundle(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
                return false;

            // Build bundle path patterns using enum lookups
            string appExt = getPathComponent(PathComponentType.AppExtension);
            string contents = getPathComponent(PathComponentType.Contents);
            string macOS = getPathComponent(PathComponentType.MacOS);

            // Check for .app/Contents/MacOS structure (forward-slash form only)
            string bundlePattern = $"{appExt}/{contents}/{macOS}";
            return executablePath.Contains(bundlePattern, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the bundle configuration directory (parent of .app bundle)
        /// </summary>
        /// <param name="executableDirectory">Executable directory path</param>
        /// <returns>Configuration directory next to bundle</returns>
        public string GetBundleConfigDirectory(string executableDirectory)
        {
            try
            {
                string bundlePath = executableDirectory;
                string appExtension = getPathComponent(PathComponentType.AppExtension);

                // Navigate up to find the .app bundle
                while (bundlePath.Length > 1 && !bundlePath.EndsWith(appExtension))
                {
                    bundlePath = _fileSystem.GetParentDirectory(bundlePath);
                    if (bundlePath == null) break;
                }

                if (bundlePath != null && bundlePath.EndsWith(appExtension))
                {
                    // Get the parent directory of the .app bundle
                    string parentDirectory = _fileSystem.GetParentDirectory(bundlePath);

                    if (!string.IsNullOrEmpty(parentDirectory) && _fileSystem.DirectoryExists(parentDirectory))
                        return parentDirectory.Replace('\\', '/'); // Normalize path separators
                }
            }
            catch
            {
                // Fall back to executable directory if bundle detection fails
            }

            return executableDirectory;
        }

        // ======= End Bundle Detection =======

        // ======= Path Expansion Helpers =======

        /// <summary>
        /// Gets the tilde expansion directory (~)
        /// </summary>
        /// <returns>User home directory for tilde expansion</returns>
        public string GetTildeExpansionDirectory() => GetUserHomeDirectory();

        /// <summary>
        /// Expands environment variables in a path string
        /// </summary>
        /// <param name="path">Path containing environment variables</param>
        /// <returns>Expanded path</returns>
        public string ExpandEnvironmentVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return _platformService.ExpandEnvironmentVariables(path);
        }

        // ======= End Path Expansion Helpers =======

        // ======= Future Obfuscation Support =======

        /// <summary>
        /// Method for future obfuscation/scrambling of path strings
        /// Call this from static constructor to initialize obfuscated strings
        /// </summary>
        /// <param name="obfuscatedEnvironmentVars">Obfuscated environment variable mappings</param>
        /// <param name="obfuscatedPathComponents">Obfuscated path component mappings</param>
        public static void InitializeFromObfuscatedMappings(
            Dictionary<EnvironmentVariableType, string> obfuscatedEnvironmentVars,
            Dictionary<PathComponentType, string> obfuscatedPathComponents)
        {
            if (obfuscatedEnvironmentVars != null)
                _environmentVariables = obfuscatedEnvironmentVars;

            if (obfuscatedPathComponents != null)
                _pathComponents = obfuscatedPathComponents;
        }

        /// <summary>
        /// Gets current path mappings for diagnostic purposes
        /// </summary>
        /// <returns>Dictionary containing all current mappings</returns>
        public static Dictionary<string, object> GetCurrentMappings()
        {
            return new Dictionary<string, object>
            {
                ["EnvironmentVariables"] = _environmentVariables,
                ["PathComponents"] = _pathComponents,
                ["SpecialFolders"] = _specialFolders
            };
        }

        // ======= End Future Obfuscation Support =======
    }
}
