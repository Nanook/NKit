using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using System;
using System.Diagnostics;
using System.IO;

namespace NKit.Ui.Helpers
{
    /// <summary>
    /// Centralized utility for converting between absolute and display paths.
    /// Handles both directions: absolute-to-display and display-to-absolute.
    /// </summary>
    public static class PathsHelper
    {
        /// <summary>
        /// Converts an absolute path to a relative display path if it starts with the user data directory.
        /// Always uses UserPath as the base since config paths are within user path in system mode.
        /// Example: "C:\Users\Username\nkit\logs\output.txt" -> "logs\output.txt"
        /// </summary>
        /// <param name="absolutePath">The absolute path to convert</param>
        /// <returns>A display-friendly path (relative if within user directory, otherwise absolute)</returns>
        public static string ToDisplayPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                ConfigurationInfo configInfo = configManager.GetConfigurationInfo();

                // Normalize paths for comparison
                string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
                string normalizedUserPath = Path.GetFullPath(configInfo.UserDataDirectory);

                // Check if path starts with user data directory (works for both portable and system mode)
                if (normalizedAbsolutePath.StartsWith(normalizedUserPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(normalizedUserPath, normalizedAbsolutePath);
                }

                // Return original path if it doesn't start with the user directory
                return absolutePath;
            }
            catch (Exception)
            {
                // Return original path if any error occurs
                return absolutePath;
            }
        }

        /// <summary>
        /// Converts a display path (which might be relative) back to an absolute path.
        /// Always uses UserPath as the base directory.
        /// Example: "logs\output.txt" -> "C:\Users\Username\nkit\logs\output.txt"
        /// </summary>
        /// <param name="displayPath">The display path that might be relative</param>
        /// <returns>An absolute path</returns>
        public static string ToAbsolutePath(string displayPath)
        {
            if (string.IsNullOrEmpty(displayPath))
                return displayPath;

            try
            {
                // If already absolute, return as-is
                if (Path.IsPathRooted(displayPath))
                    return Path.GetFullPath(displayPath);

                using ConfigurationManager configManager = new ConfigurationManager();
                ConfigurationInfo configInfo = configManager.GetConfigurationInfo();

                // Always combine with user directory (works for both portable and system mode)
                string candidatePath = Path.Combine(configInfo.UserDataDirectory, displayPath);
                return Path.GetFullPath(candidatePath);
            }
            catch (Exception)
            {
                // Fallback: try to make it absolute relative to current directory
                try
                {
                    return Path.GetFullPath(displayPath);
                }
                catch
                {
                    return displayPath;
                }
            }
        }

        /// <summary>
        /// Ensures the complete NKit folder structure exists in the user data directory.
        /// This creates dats, fix, keys, and logs folders that are used by NKit processing.
        /// </summary>
        /// <returns>True if any folders were created, false if they already existed</returns>
        public static bool EnsureNKitFoldersExist()
        {
            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                SetupResult setupResult = configManager.EnsureConfiguration();
                return setupResult.HasChanges;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Expands UI paths using $configPath$ and $userPath$ variables
        /// </summary>
        /// <param name="value">The value to expand</param>
        /// <param name="taskType">Current task type</param>
        /// <param name="systemType">Current system type</param>
        /// <returns>Expanded path string</returns>
        public static string ExpandUiPath(string value, string taskType, string systemType)
        {
            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                return configManager.ExpandConfigPath(value, taskType, systemType);
            }
            catch (Exception)
            {
                // Fallback to original value if expansion fails
                return value;
            }
        }

        private static string GetConfigDirectory()
        {
            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                ConfigurationInfo configInfo = configManager.GetConfigurationInfo();
                return configInfo.ConfigDirectory;
            }
            catch (Exception)
            {
                // Fallback to executable directory if configuration fails
                return GetExecutableDirectory();
            }
        }

        private static string GetExecutableDirectory()
        {
            using (Process p = Process.GetCurrentProcess())
                return Path.GetDirectoryName(p.MainModule.FileName);
        }

        private static string GetAppName()
        {
            using (Process p = Process.GetCurrentProcess())
                return Path.GetFileNameWithoutExtension(p.MainModule.FileName);
        }
    }
}