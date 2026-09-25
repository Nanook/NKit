using Nanook.NKit.Configuration.Models;
using Nanook.NKit.Configuration.Services;
using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;

namespace Nanook.NKit.Configuration
{
    /// <summary>
    /// Interface for configuration management operations
    /// </summary>
    public interface IConfigurationManager : IDisposable
    {
        /// <summary>
        /// Gets the immutable configuration context containing all resolved paths and settings
        /// </summary>
        ConfigurationContext Context { get; }

        /// <summary>
        /// Ensures the complete configuration environment is set up
        /// </summary>
        /// <returns>Setup result containing details about what was created</returns>
        SetupResult EnsureConfiguration();

        /// <summary>
        /// Expands configuration path variables in the given string
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="taskType">Optional task type for $task$ variable</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <returns>Expanded path string</returns>
        string ExpandConfigPath(string path, string taskType = null, string systemType = null);

        /// <summary>
        /// Expands configuration path variables excluding date, task, and timestamp variables.
        /// Useful for configuration storage where dynamic values should be preserved as variables.
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <returns>Expanded path string with static variables only</returns>
        string ExpandConfigPathSystemOnly(string path, string systemType = null);

        /// <summary>
        /// Gets comprehensive information about the current configuration setup
        /// </summary>
        /// <returns>Configuration information object</returns>
        ConfigurationInfo GetConfigurationInfo();

        /// <summary>
        /// Gets UI defaults when no system configuration is available
        /// </summary>
        /// <param name="systemType">System type to get defaults for</param>
        /// <returns>Complete UI defaults structure</returns>
        CompleteUiDefaults GetUiDefaults(SystemType systemType);

        /// <summary>
        /// Creates a SystemSettings instance with UI defaults when no configuration is available
        /// </summary>
        /// <param name="systemType">System type to create settings for</param>
        /// <param name="taskType">Task type</param>
        /// <param name="log">Log instance</param>
        /// <returns>SystemSettings with default values</returns>
        SystemSettings CreateDefaultSystemSettings(SystemType systemType, TaskType taskType, ILogScope log);
    }

    /// <summary>
    /// Centralized configuration manager for NKit that handles all configuration operations
    /// including path detection, mode selection, and directory structure creation.
    /// </summary>
    public class ConfigurationManager : IConfigurationManager
    {
        private readonly ConfigurationContext _context;
        private readonly IConfigurationSetup _configurationSetup;
        private readonly DefaultPathProvider _pathProvider;
        private readonly IFileSystemService _fileSystem;
        private bool _disposed;

        /// <summary>
        /// Gets the immutable configuration context containing all resolved paths and settings
        /// </summary>
        public ConfigurationContext Context => _context;

        /// <summary>
        /// Creates a new configuration manager with dependency injection support
        /// </summary>
        /// <param name="platformService">Platform service for system operations (optional)</param>
        /// <param name="fileSystem">File system service for I/O operations (optional)</param>
        public ConfigurationManager(IPlatformService platformService = null, IFileSystemService fileSystem = null)
        {
            platformService ??= new PlatformService();
            fileSystem ??= new FileSystemService();

            // Store the file system service for later use
            _fileSystem = fileSystem;

            try
            {
                // Use the consolidated core service for all detection operations
                ConfigurationCoreService coreService = new ConfigurationCoreService();

                // Detect platform context (OS, app type, bundle status)
                PlatformContext platformContext = coreService.DetectPlatformContext(platformService);

                // Determine configuration mode (portable vs system)
                bool isPortableMode = coreService.ShouldUsePortableMode(platformContext, platformService, fileSystem);

                // Resolve paths using unified path resolver
                PathResolver pathResolver = isPortableMode
                    ? PathResolver.CreatePortable(coreService)
                    : PathResolver.CreateSystem(coreService);

                _context = pathResolver.ResolveConfiguration(platformContext, platformService, fileSystem);

                // Detect configuration source and file
                _context = coreService.DetectConfigSource(_context, fileSystem);

                // Initialize setup service and path provider
                _configurationSetup = new ConfigurationSetup();
                _pathProvider = new DefaultPathProvider(fileSystem);
            }
            catch (Exception)
            {
                // Create fallback configuration if initialization fails
                _context = createFallbackContext(platformService);
                _configurationSetup = new ConfigurationSetup();
                _pathProvider = new DefaultPathProvider(fileSystem);
            }
        }

        /// <summary>
        /// Ensures the complete configuration environment is set up
        /// </summary>
        /// <returns>Setup result containing details about what was created</returns>
        public SetupResult EnsureConfiguration()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            return _configurationSetup.EnsureConfiguration(_context, _fileSystem);
        }

        /// <summary>
        /// Expands configuration path variables in the given string
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="taskType">Optional task type for $task$ variable</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <returns>Expanded path string</returns>
        public string ExpandConfigPath(string path, string taskType = null, string systemType = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            return PathExpansion.ExpandPath(path, _context, taskType, systemType);
        }

        /// <summary>
        /// Expands configuration path variables excluding date, task, and timestamp variables.
        /// Useful for configuration storage where dynamic values should be preserved as variables.
        /// </summary>
        /// <param name="path">Path string containing variables to expand</param>
        /// <param name="systemType">Optional system type for $system$ variable</param>
        /// <returns>Expanded path string with static variables only</returns>
        public string ExpandConfigPathSystemOnly(string path, string systemType = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            return PathExpansion.ExpandPathSystemOnly(path, _context, systemType);
        }

        /// <summary>
        /// Gets comprehensive information about the current configuration setup
        /// </summary>
        /// <returns>Configuration information object</returns>
        public ConfigurationInfo GetConfigurationInfo()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            return new ConfigurationInfo
            {
                ExecutableDirectory = _context.ExecutableDirectory,
                ConfigDirectory = _context.ConfigDirectory,
                UserDataDirectory = _context.UserDataDirectory,
                IsPortableMode = _context.IsPortableMode,
                ConfigSource = _context.ConfigSource,
                ConfigFile = _context.ConfigFile,
                ConfigFileName = _context.ConfigFileName
            };
        }

        /// <summary>
        /// Gets UI defaults when no system configuration is available
        /// </summary>
        /// <param name="systemType">System type to get defaults for</param>
        /// <returns>Complete UI defaults structure</returns>
        public CompleteUiDefaults GetUiDefaults(SystemType systemType)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            // Use the new DefaultPathProvider for path defaults
            UiPathDefaults pathDefaults = _pathProvider.GetModeOptimizedDefaults(_context);

            // Get other defaults from configuration provider
            UiGeneralDefaults generalDefaults = ConfigSettingsDefaults.GetUiGeneralDefaults();
            UiConversionDefaults conversionDefaults = ConfigSettingsDefaults.GetUiConversionDefaults(systemType);
            UiExtractionDefaults extractionDefaults = ConfigSettingsDefaults.GetUiExtractionDefaults();

            return new CompleteUiDefaults
            {
                SystemType = systemType,
                TaskType = TaskType.NotSet,
                Paths = pathDefaults,
                General = generalDefaults,
                Conversion = conversionDefaults,
                Extraction = extractionDefaults
            };
        }

        /// <summary>
        /// Creates a SystemSettings instance with UI defaults when no configuration is available
        /// </summary>
        /// <param name="systemType">System type to create settings for</param>
        /// <param name="taskType">Task type</param>
        /// <param name="log">Log instance</param>
        /// <returns>SystemSettings with default values</returns>
        public SystemSettings CreateDefaultSystemSettings(SystemType systemType, TaskType taskType, ILogScope log)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConfigurationManager));

            CompleteUiDefaults defaults = GetUiDefaults(systemType);

            // Create parameter dictionaries with defaults using constants
            Dictionary<string, string> rootParams = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                [ConfigSettingsConstants.ParamTask] = taskType.ToString(),
                [ConfigSettingsConstants.ParamSystem] = systemType.ToString(),
                [ConfigSettingsConstants.ParamR] = defaults.General.R ? "y" : "n",
                [ConfigSettingsConstants.ParamArc] = defaults.General.Arc ? "y" : "n",
                [ConfigSettingsConstants.ParamV] = defaults.General.V.ToString(),
                [ConfigSettingsConstants.ParamConsoleLevel] = defaults.General.ConsoleLevel.ToString(),
                [ConfigSettingsConstants.ParamLogOutLevel] = defaults.General.LogOutLevel.ToString(),
                [ConfigSettingsConstants.ParamResults] = defaults.General.Results ? "y" : "n",
                [ConfigSettingsConstants.ParamOutAsDatMatch] = defaults.General.OutAsDatMatch ? "y" : "n",
                [ConfigSettingsConstants.ParamDeleteProcessed] = defaults.General.DeleteProcessed ? "y" : "n",
                [ConfigSettingsConstants.ParamSkipIfCompleted] = defaults.General.SkipIfCompleted ? "y" : "n"
            };

            Dictionary<string, string> systemParams = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                [ConfigSettingsConstants.ParamOut] = defaults.Paths.Out,
                [ConfigSettingsConstants.ParamScanIn] = defaults.Paths.ScanIn,
                [ConfigSettingsConstants.ParamScanOut] = defaults.Paths.ScanOut,
                [ConfigSettingsConstants.ParamTmp] = defaults.Paths.Tmp,
                [ConfigSettingsConstants.ParamLogOut] = defaults.Paths.LogOut,
                [ConfigSettingsConstants.ParamResultsOut] = defaults.Paths.ResultsOut,
                [ConfigSettingsConstants.ParamFixInfo] = defaults.Paths.FixInfo,
                [ConfigSettingsConstants.ParamFixFiles] = defaults.Paths.FixFiles,
                [ConfigSettingsConstants.ParamDat] = defaults.Paths.Dat,
                [ConfigSettingsConstants.ParamKeys] = defaults.Paths.Keys
            };

            // Generate conversion format string using configuration provider
            string convertFormat = ConfigSettingsDefaults.GetDefaultFormat(systemType);
            string convertFormatString;

            switch (convertFormat)
            {
                case ConfigSettingsConstants.FormatRvz:
                    convertFormatString = ConfigSettingsFormatGenerator.GenerateRvzFormatString(
                        RvzEncodingType.ZStd,
                        ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd),
                        ConfigSettingsDefaults.GetDefaultBlockSize(systemType, convertFormat),
                        ConfigSettingsDefaults.GetDefaultParallelism(systemType));
                    break;
                case ConfigSettingsConstants.FormatCue:
                    convertFormatString = ConfigSettingsFormatGenerator.GenerateCueFormatString();
                    break;
                default:
                    convertFormatString = convertFormat;
                    break;
            }

            systemParams[ConfigSettingsConstants.ParamConvert] = convertFormatString;
            systemParams[ConfigSettingsConstants.ParamExtract] = ConfigSettingsFormatGenerator.GenerateExtractFormatString();

            // Create SystemSettings with defaults
            SystemSettings settings = new SystemSettings(
                rootParams: rootParams,
                system: systemType.ToString(),
                systemParams: systemParams,
                overrideParams: new Dictionary<string, string>(),
                log: log,
                configManager: this);

            // Initialize the settings to populate the path properties
            // Note: We need to create a dummy DatManager for initialization
            DatManager datManager = new DatManager(); // This might be empty, but it's needed for initialization
            settings.Initialise(systemType, taskType, log, datManager);

            return settings;
        }

        /// <summary>
        /// Creates a fallback configuration context when normal initialization fails
        /// </summary>
        private static ConfigurationContext createFallbackContext(IPlatformService platformService)
        {
            try
            {
                string executableDirectory = platformService?.GetExecutableDirectory() ??
                    System.AppDomain.CurrentDomain.BaseDirectory;

                return new ConfigurationContext(
                    executableDirectory: executableDirectory,
                    configDirectory: executableDirectory,
                    userDataDirectory: executableDirectory,
                    isPortableMode: true,
                    isMacOSBundle: false,
                    configSource: ConfigSource.None,
                    configFile: null,
                    configFileName: ConfigSettingsConstants.ConfigFileNameCLI
                );
            }
            catch
            {
                // Absolute fallback
                return new ConfigurationContext(
                    executableDirectory: System.Environment.CurrentDirectory,
                    configDirectory: System.Environment.CurrentDirectory,
                    userDataDirectory: System.Environment.CurrentDirectory,
                    isPortableMode: true,
                    isMacOSBundle: false,
                    configSource: ConfigSource.None,
                    configFile: null,
                    configFileName: ConfigSettingsConstants.ConfigFileNameCLI
                );
            }
        }

        /// <summary>
        /// Disposes the configuration manager
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
                _disposed = true;
        }
    }
}