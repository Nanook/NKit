using Nanook.NKit.Configuration.Models;
using System;
using System.IO;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Provides default path configurations for CLI and UI applications across different platforms and modes.
    /// This service determines appropriate default paths based on the configuration context and application type.
    /// </summary>
    public class DefaultPathProvider
    {
        private readonly IFileSystemService _fileSystem;

        /// <summary>
        /// Creates a new instance of the default path provider
        /// </summary>
        /// <param name="fileSystem">File system service for path operations</param>
        public DefaultPathProvider(IFileSystemService fileSystem = null)
        {
            _fileSystem = fileSystem ?? new FileSystemService();
        }

        /// <summary>
        /// Gets default paths for CLI applications based on the configuration context.
        /// CLI applications typically use simpler path structures.
        /// Matches defaults specified in defaults/nkit.yaml
        /// </summary>
        /// <param name="context">Configuration context containing resolved paths</param>
        /// <returns>CLI-specific default paths</returns>
        public UiPathDefaults GetCliDefaults(ConfigurationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            string usrAppDir = context.IsPortableMode ? null : ConfigSettingsConstants.ApplicationDirectoryName;

            return new UiPathDefaults
            {
                // Output - always in user data directory for CLI
                Out = "", // joinPath(context.UserDataDirectory,  usrAppDir, ConfigSettingsConstants.DirectoryNameOut),

                // Temporary files - in user data for CLI (simpler structure)
                Tmp = "", //joinPath(context.UserDataDirectory,  usrAppDir, ConfigSettingsConstants.DirectoryNameTemp),

                // Logging - in user data directory with nkit subdirectory
                LogOut = joinPath(context.UserDataDirectory, usrAppDir, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableDate}_log.txt"),
                ResultsOut = joinPath(context.UserDataDirectory, usrAppDir, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableSystem}_{ConfigSettingsConstants.PathVariableTask}_results.txt"),

                // Configuration-based paths (dats, keys, fix data)
                ScanIn = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameScans),
                ScanOut = joinPath(context.UserDataDirectory, usrAppDir, ConfigSettingsConstants.DirectoryNameScans),
                FixInfo = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    $"fix_{ConfigSettingsConstants.PathVariableSystem}.yaml"),
                FixFiles = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    ConfigSettingsConstants.PathVariableSystem),
                Dat = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameDats,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip", "*.dat"),
                Keys = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameKeys,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip")
            };
        }

        /// <summary>
        /// Gets default paths for UI applications based on the configuration context.
        /// UI applications use the same structure as CLI.
        /// </summary>
        /// <param name="context">Configuration context containing resolved paths</param>
        /// <returns>UI-specific default paths</returns>
        public UiPathDefaults GetUiDefaults(ConfigurationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // UI applications use the same structure as CLI
            return GetCliDefaults(context);
        }

        /// <summary>
        /// Gets default paths based on application type and configuration context.
        /// This method automatically determines the appropriate defaults.
        /// </summary>
        /// <param name="context">Configuration context containing resolved paths</param>
        /// <param name="applicationType">Type of application (CLI or UI)</param>
        /// <returns>Application-appropriate default paths</returns>
        public UiPathDefaults GetDefaultPaths(ConfigurationContext context, ApplicationType applicationType = ApplicationType.CLI)
        {
            return applicationType switch
            {
                ApplicationType.UI => GetUiDefaults(context),
                ApplicationType.CLI => GetCliDefaults(context),
                _ => GetCliDefaults(context) // Default fallback
            };
        }

        /// <summary>
        /// Gets portable mode specific defaults where everything is relative to the executable
        /// </summary>
        /// <param name="context">Configuration context for portable mode</param>
        /// <returns>Portable mode default paths</returns>
        public UiPathDefaults GetPortableDefaults(ConfigurationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!context.IsPortableMode)
                // If not in portable mode, return regular defaults
                return GetDefaultPaths(context);

            // In portable mode, everything is relative to executable/config directory
            return new UiPathDefaults
            {
                // Everything in the same directory tree for portable mode
                Out = "", // joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameOut),
                Tmp = "", // joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameTemp),
                LogOut = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableDate}_log.txt"),
                ResultsOut = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableSystem}_{ConfigSettingsConstants.PathVariableTask}_results.txt"),

                // Portable mode keeps everything together
                ScanIn = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameScans),
                ScanOut = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameScans),
                FixInfo = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    $"fix_{ConfigSettingsConstants.PathVariableSystem}.yaml"),
                FixFiles = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    ConfigSettingsConstants.PathVariableSystem),
                Dat = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameDats,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip", "*.dat"),
                Keys = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameKeys,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip")
            };
        }

        /// <summary>
        /// Gets system mode specific defaults where config and user data are separated
        /// </summary>
        /// <param name="context">Configuration context for system mode</param>
        /// <returns>System mode default paths</returns>
        public UiPathDefaults GetSystemDefaults(ConfigurationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.IsPortableMode)
                // If in portable mode, return portable defaults
                return GetPortableDefaults(context);

            // In system mode, separate config data from user data
            // UserDataDirectory is ~/home or C:\Users\YourName, so we add nkit/ subdirectory
            return new UiPathDefaults
            {
                // User data goes in user directory with nkit subdirectory
                Out = "", // joinPath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName, ConfigSettingsConstants.DirectoryNameOut),
                Tmp = "", // joinPath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName, ConfigSettingsConstants.DirectoryNameTemp),
                LogOut = joinPath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableDate}_log.txt"),
                ResultsOut = joinPath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName, ConfigSettingsConstants.DirectoryNameLogs,
                    $"{ConfigSettingsConstants.PathVariableSystem}_{ConfigSettingsConstants.PathVariableTask}_results.txt"),
                ScanOut = joinPath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName, ConfigSettingsConstants.DirectoryNameScans,
                    ConfigSettingsConstants.PathVariableSystem, ConfigSettingsConstants.PathVariableTask),

                // Configuration data goes in config directory
                ScanIn = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameScans),
                FixInfo = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    $"fix_{ConfigSettingsConstants.PathVariableSystem}.yaml"),
                FixFiles = joinPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix,
                    ConfigSettingsConstants.PathVariableSystem),
                Dat = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameDats,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip", "*.dat"),
                Keys = createWildcardPath(context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameKeys,
                    ConfigSettingsConstants.PathVariableSystem, "*.zip")
            };
        }



        /// <summary>
        /// Gets default paths that are specifically optimized for the current configuration mode
        /// </summary>
        /// <param name="context">Configuration context</param>
        /// <returns>Mode-optimized default paths</returns>
        public UiPathDefaults GetModeOptimizedDefaults(ConfigurationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return context.IsPortableMode
                ? GetPortableDefaults(context)
                : GetSystemDefaults(context);
        }

        /// <summary>
        /// Helper method to safely join paths using the file system service
        /// </summary>
        private string joinPath(params string[] paths) => _fileSystem.CombinePath(paths);

        /// <summary>
        /// Creates a wildcard path pattern using platform-appropriate path separators.
        /// This replaces hard-coded forward slash patterns for cross-platform compatibility.
        /// </summary>
        /// <param name="basePath">Base directory path</param>
        /// <param name="subPath">Subdirectory path</param>
        /// <param name="systemPath">System-specific path component</param>
        /// <param name="wildcards">Wildcard patterns to append</param>
        /// <returns>Platform-appropriate wildcard path</returns>
        private string createWildcardPath(string basePath, string subPath, string systemPath, params string[] wildcards)
        {
            // Start with the base path structure using OS-appropriate separators
            string path = joinPath(basePath, subPath, systemPath);

            // Handle specific NKit archive patterns
            if (wildcards.Length == 2)
            {
                // This is the Dat pattern: *.zip//*.dat
                // Use NKit's archive separator format: // always uses forward slashes
                // The pattern means "any .dat file within any .zip archive"
                // Append a forward slash separator before the archive pattern so that the archive part uses forward slashes
                path += "/" + wildcards[0] + "//" + wildcards[1];
            }
            else if (wildcards.Length == 1)
            {
                // This is the Keys pattern: *.zip (simple file pattern)
                // For template/wildcard patterns keep forward-slash separators for consistency across platforms
                path += "/" + wildcards[0];
            }
            else
            {
                // Fallback for other patterns - use OS separator
                char separator = System.IO.Path.DirectorySeparatorChar;
                foreach (string wildcard in wildcards)
                {
                    if (!string.IsNullOrEmpty(wildcard))
                        path += separator + wildcard;
                }
            }

            return path;
        }

        /// <summary>
        /// Parses a wildcard path that may include an archive separator ("//") used by NKit
        /// to indicate an archive mask and an inner file mask (e.g. "C:/dats/*.zip//*.dat").
        /// Returns components with normalized directory separators suitable for display or further processing.
        /// </summary>
        /// <param name="wildcardPath">The combined wildcard path to parse</param>
        /// <returns>DatWildcardComponents with Directory, ArchiveMask and FileMask populated</returns>
        public DatWildcardComponents ParseDatWildcardPath(string wildcardPath)
        {
            DatWildcardComponents res = new DatWildcardComponents();
            if (string.IsNullOrWhiteSpace(wildcardPath))
                return res;

            string raw = wildcardPath.Trim();

            // archive pattern: <archivePart>//<innerMask>
            int idx = raw.IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0)
            {
                string archivePart = raw.Substring(0, idx).Trim();
                res.FileMask = raw.Substring(idx + 2).Trim();

                // Normalize and split archivePart into directory + archive mask
                archivePart = archivePart.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                res.Directory = Path.GetDirectoryName(archivePart) ?? string.Empty;
                res.ArchiveMask = Path.GetFileName(archivePart);
                return res;
            }

            // normalize
            raw = raw.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // trailing separator => directory
            if (raw.EndsWith(Path.DirectorySeparatorChar))
                res.Directory = raw.TrimEnd(Path.DirectorySeparatorChar);
            else
                res.Directory = raw;
            return res;
        }

        /// <summary>
        /// Simple container for parsed DAT wildcard components
        /// </summary>
        public class DatWildcardComponents
        {
            public string Directory { get; set; } = string.Empty;
            public string ArchiveMask { get; set; } = string.Empty;
            public string FileMask { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Application type enumeration for determining appropriate defaults
    /// </summary>
    public enum ApplicationType
    {
        /// <summary>
        /// Command-line interface application
        /// </summary>
        CLI,

        /// <summary>
        /// Graphical user interface application  
        /// </summary>
        UI
    }
}
