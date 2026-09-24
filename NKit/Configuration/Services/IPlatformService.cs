using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Abstraction for platform-specific operations to enable cross-platform testing
    /// </summary>
    public interface IPlatformService
    {
        bool IsWindows();
        bool IsOSX();
        bool IsLinux();
        string GetEnvironmentVariable(string variable);
        string ExpandEnvironmentVariables(string name);
        string GetSpecialFolder(Environment.SpecialFolder folder);
        string GetUserProfile();
        string GetExecutableDirectory();

        /// <summary>
        /// Gets the executable name for config file resolution.
        /// Real implementations should return null to use standard detection.
        /// Mock implementations can override to simulate different executable names for testing.
        /// </summary>
        string GetExecutableName() => null;
    }

    /// <summary>
    /// Default implementation of platform service
    /// </summary>
    internal class PlatformService : IPlatformService
    {
        public bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public bool IsOSX() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public string GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
        public string ExpandEnvironmentVariables(string name) => Environment.ExpandEnvironmentVariables(name);
        public string GetSpecialFolder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
        public string GetUserProfile() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public string GetExecutableDirectory()
        {
            try
            {
                // Use AppContext.BaseDirectory for AOT/single-file compatibility
                string baseDir = AppContext.BaseDirectory;

                // Validation - ensure the path exists
                if (!string.IsNullOrEmpty(baseDir) && System.IO.Directory.Exists(baseDir))
                    return baseDir;

                // If AppContext.BaseDirectory is invalid, try alternative methods
                throw new System.IO.DirectoryNotFoundException($"AppContext.BaseDirectory returned invalid path: {baseDir}");
            }
            catch (Exception)
            {
                try
                {
                    // Fallback to AppDomain method
                    string appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (!string.IsNullOrEmpty(appDomainDir) && System.IO.Directory.Exists(appDomainDir))
                        return appDomainDir;

                    // If that fails, try to get from process
                    using (Process process = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        string processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath))
                        {
                            string processDir = System.IO.Path.GetDirectoryName(processPath);
                            if (!string.IsNullOrEmpty(processDir) && System.IO.Directory.Exists(processDir))
                                return processDir;
                        }
                    }
                }
                catch
                {
                    // All methods failed - use current directory as last resort
                }

                // Final fallback - current working directory
                return System.IO.Directory.GetCurrentDirectory();
            }
        }
    }
}
