using System;

namespace Nanook.NKit.Configuration.Models
{
    /// <summary>
    /// Immutable configuration context containing all resolved paths and settings
    /// </summary>
    public class ConfigurationContext
    {
        public string ExecutableDirectory { get; }
        public string ConfigDirectory { get; }
        public string UserDataDirectory { get; }
        public bool IsPortableMode { get; }
        public bool IsMacOSBundle { get; }
        public ConfigSource ConfigSource { get; }
        public string ConfigFile { get; }
        public string ConfigFileName { get; }

        public ConfigurationContext(
            string executableDirectory,
            string configDirectory,
            string userDataDirectory,
            bool isPortableMode,
            bool isMacOSBundle,
            ConfigSource configSource,
            string configFile,
            string configFileName)
        {
            // Normalize directories to avoid accidental duplicate separators when combined later
            ExecutableDirectory = trimTrailingDirectorySeparators(executableDirectory ?? throw new ArgumentNullException(nameof(executableDirectory)));
            ConfigDirectory = trimTrailingDirectorySeparators(configDirectory ?? throw new ArgumentNullException(nameof(configDirectory)));
            UserDataDirectory = trimTrailingDirectorySeparators(userDataDirectory ?? throw new ArgumentNullException(nameof(userDataDirectory)));
            IsPortableMode = isPortableMode;
            IsMacOSBundle = isMacOSBundle;
            ConfigSource = configSource;
            ConfigFile = configFile;
            ConfigFileName = configFileName ?? throw new ArgumentNullException(nameof(configFileName));
        }

        private static string trimTrailingDirectorySeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string root = System.IO.Path.GetPathRoot(path) ?? string.Empty;
            string res = path;
            while (res.Length > (root?.Length ?? 0) && (res.EndsWith(System.IO.Path.DirectorySeparatorChar) || res.EndsWith(System.IO.Path.AltDirectorySeparatorChar)))
                res = res.Substring(0, res.Length - 1);
            return res;
        }

        /// <summary>
        /// Creates a copy with updated config source and file information
        /// </summary>
        public ConfigurationContext WithConfigFile(ConfigSource configSource, string configFile)
        {
            return new ConfigurationContext(
                ExecutableDirectory,
                ConfigDirectory,
                UserDataDirectory,
                IsPortableMode,
                IsMacOSBundle,
                configSource,
                configFile,
                ConfigFileName);
        }
    }
}