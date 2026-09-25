using Nanook.NKit.Configuration.Models;
using Nanook.NKit.Configuration.Services;
using System;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Utility class for expanding path variables in configuration strings
    /// </summary>
    internal static class PathExpansion
    {
        /// <summary>
        /// Expands configuration path variables in the given string
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="context">Configuration context with resolved paths</param>
        /// <param name="taskType">Optional task type for $task$ variable</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <param name="expandDates">Whether to expand $date$ and $timestamp$ variables</param>
        /// <param name="pathResolution">Optional path resolution service for tilde expansion</param>
        /// <returns>Expanded path string</returns>
        public static string ExpandPath(string path, ConfigurationContext context, string taskType = null,
            string systemType = null, bool expandDates = true, PathResolutionService pathResolution = null)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string expanded = path;

            // Handle home directory expansion (~) using centralized service
            if (expanded.StartsWith("~/"))
            {
                string homeDir;
                if (pathResolution != null)
                {
                    homeDir = pathResolution.GetTildeExpansionDirectory();
                }
                else
                {
                    // Fallback to direct Environment access for backward compatibility
                    homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                expanded = System.IO.Path.Combine(homeDir, expanded.Substring(2));
            }

            // Replace configuration variables (always expand these)
            expanded = expanded.Replace(ConfigSettingsConstants.PathVariableConfig, context.ConfigDirectory);
            expanded = expanded.Replace(ConfigSettingsConstants.PathVariableUser, context.UserDataDirectory);
            expanded = expanded.Replace(ConfigSettingsConstants.PathVariableApp, context.ExecutableDirectory);

            // Replace date and timestamp variables (only if expandDates is true)
            if (expandDates)
            {
                expanded = expanded.Replace(ConfigSettingsConstants.PathVariableDate, DateTime.Now.ToString("yyyyMMdd"));
                expanded = expanded.Replace(ConfigSettingsConstants.PathVariableTimestamp, DateTime.Now.ToString("yyyyMMddHHmmss"));
            }

            // Replace task and system variables if provided.
            // Both are lowercased so config paths (keys/$system$, dats/$system$, fix/$system$)
            // resolve to lowercase directory names on all platforms — consistent with the
            // all-lowercase folder structure EnsureConfiguration creates, and safe on Linux
            // where the filesystem is case-sensitive.
            if (!string.IsNullOrEmpty(taskType))
                expanded = expanded.Replace(ConfigSettingsConstants.PathVariableTask, taskType.ToLowerInvariant());
            if (!string.IsNullOrEmpty(systemType))
                expanded = expanded.Replace(ConfigSettingsConstants.PathVariableSystem, systemType.ToLowerInvariant());

            return expanded;
        }

        /// <summary>
        /// Expands configuration path variables excluding date, task, and timestamp variables.
        /// Useful for configuration storage where dynamic values should be preserved as variables.
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="context">Configuration context with resolved paths</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <param name="pathResolution">Optional path resolution service for tilde expansion</param>
        /// <returns>Expanded path string with static variables only</returns>
        public static string ExpandPathSystemOnly(string path, ConfigurationContext context,
            string systemType = null, PathResolutionService pathResolution = null)
        {
            // Call the main ExpandPath method with expandDates = false and no taskType
            return ExpandPath(path, context, taskType: null, systemType: systemType,
                expandDates: false, pathResolution: pathResolution);
        }
    }
}
