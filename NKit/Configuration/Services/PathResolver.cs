using Nanook.NKit.Configuration.Models;

namespace Nanook.NKit.Configuration.Services
{
    /// <summary>
    /// Unified path resolver that handles both portable and system mode path resolution
    /// </summary>
    internal class PathResolver : IPathResolutionStrategy
    {
        private readonly ConfigurationCoreService _coreService;
        private readonly bool _isPortableMode;

        public PathResolver(bool isPortableMode, ConfigurationCoreService coreService = null)
        {
            _isPortableMode = isPortableMode;
            _coreService = coreService ?? new ConfigurationCoreService();
        }

        public ConfigurationContext ResolveConfiguration(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem)
        {
            if (_isPortableMode)
                return resolvePortableConfiguration(platformContext, platformService, fileSystem);
            else
                return resolveSystemConfiguration(platformContext, platformService, fileSystem);
        }

        private ConfigurationContext resolvePortableConfiguration(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem)
        {
            string configDirectory = getPortableConfigDirectory(platformContext, platformService, fileSystem);
            string configFileName = _coreService.GetConfigFileName(platformService);

            return new ConfigurationContext(
                executableDirectory: platformContext.ExecutableDirectory,
                configDirectory: configDirectory,
                userDataDirectory: configDirectory, // Same as config in portable mode
                isPortableMode: true,
                isMacOSBundle: platformContext.IsBundle,
                configSource: ConfigSource.None, // Will be determined later
                configFile: null, // Will be determined later
                configFileName: configFileName);
        }

        private ConfigurationContext resolveSystemConfiguration(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem)
        {
            // Create centralized path resolution service
            PathResolutionService pathResolution = new PathResolutionService(platformService, fileSystem);

            string configDirectory = pathResolution.GetSystemConfigDirectory(platformContext.Platform);

            // On Linux, user data lives alongside config under ~/.config/nkit.
            // The system-mode path builder appends "nkit/" to userDataDirectory, so use the
            // parent of configDirectory (~/.config) to land everything in the same place.
            // On all other platforms use the home directory as before.
            string userDataDirectory = platformContext.Platform == PlatformType.Linux
                ? fileSystem.GetParentDirectory(configDirectory)
                : pathResolution.GetUserHomeDirectory();

            // For non-Windows system installs, supply the FHS-relative and XDG data dirs so
            // ConfigurationSetup can find the defaults template and fix files when the binary
            // lives in a read-only prefix (e.g. /usr/bin, /opt/homebrew/bin).
            string[] dataSearchPaths = pathResolution.GetSystemDataSearchPaths(
                platformContext.Platform, platformContext.ExecutableDirectory);

            string configFileName = _coreService.GetConfigFileName(platformService);

            return new ConfigurationContext(
                executableDirectory: platformContext.ExecutableDirectory,
                configDirectory: configDirectory,
                userDataDirectory: userDataDirectory,
                isPortableMode: false,
                isMacOSBundle: platformContext.IsBundle,
                configSource: ConfigSource.None, // Will be determined later
                configFile: null, // Will be determined later
                configFileName: configFileName,
                dataSearchPaths: dataSearchPaths);
        }

        private static string getPortableConfigDirectory(PlatformContext platformContext,
            IPlatformService platformService, IFileSystemService fileSystem)
        {
            if (platformContext.IsBundle)
            {
                // Use centralized path resolution service for bundle handling
                PathResolutionService pathResolution = new PathResolutionService(platformService, fileSystem);
                return pathResolution.GetBundleConfigDirectory(platformContext.ExecutableDirectory);
            }

            // For all other cases, config goes next to executable
            return platformContext.ExecutableDirectory;
        }

        /// <summary>
        /// Factory method to create a portable mode path resolver
        /// </summary>
        public static PathResolver CreatePortable(ConfigurationCoreService coreService = null) => new PathResolver(true, coreService);

        /// <summary>
        /// Factory method to create a system mode path resolver
        /// </summary>
        public static PathResolver CreateSystem(ConfigurationCoreService coreService = null) => new PathResolver(false, coreService);
    }
}
