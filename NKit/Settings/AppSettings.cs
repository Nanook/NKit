using Nanook.NKit.Configuration;
using Nanook.NKit.Configuration.Models;
using Nanook.NKit.Dats;
using Nanook.NKit.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Nanook.NKit
{
    public class AppSettings
    {
        internal const string _DefaultYaml = ".yaml";
        internal const string _ConfigParamName = "cfg";
        internal const string _InputFilePathParamName = "in";
        private const char _ParamNamePrefix = '-';

        private SystemPresetSettings _presets; //store the presets for use with each image type
        private ConfigurationManager _configManager;

        private Dictionary<SystemType, SystemSettings> _systemSettings;
        public DatManager DatManager { get; private set; }
        internal Log _log;

        public static FileInfo ThisExe { get; private set; }

        public string Version => GetVersion();

        /// <summary>
        /// Gets the product version. The assembly's InformationalVersion (from the git tag) can
        /// carry a "-prerelease" suffix (e.g. "-alpha.1") and a "+commitHash" build-metadata element
        /// appended by the SDK. The "+commitHash" is stripped so the version never shows the build
        /// hash, but any "-prerelease" suffix is PRESERVED so an alpha/beta/rc build announces itself
        /// (e.g. "3.0.0-alpha.1"). A clean release shows just "major.minor.revision".
        /// <para>All apps in the solution route through this single helper, so they all report the
        /// exact same string.</para>
        /// </summary>
        public static string GetVersion()
        {
            string raw = typeof(AppSettings).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(AppSettings).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            return NormalizeVersion(raw);
        }

        /// <summary>Normalize a version string: drop the "+build-metadata" element (e.g. the SDK's
        /// "+commitHash") and reduce the numeric core to "major.minor.revision", while PRESERVING any
        /// "-prerelease" suffix (e.g. "-alpha.1"). Examples: "3.0.0+abc123" → "3.0.0";
        /// "3.0.0-alpha.1+abc123" → "3.0.0-alpha.1"; "2.1" → "2.1.0".</summary>
        internal static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "0.0.0";

            // Drop SemVer build metadata (+...) entirely — this is where the commit hash lives.
            int plus = raw.IndexOf('+');
            if (plus >= 0) raw = raw.Substring(0, plus);

            // Split off the pre-release suffix (-...) so we can normalize the numeric core alone,
            // then re-attach it verbatim.
            string prerelease = "";
            int dash = raw.IndexOf('-');
            if (dash >= 0)
            {
                prerelease = raw.Substring(dash); // includes the leading '-'
                raw = raw.Substring(0, dash);
            }

            // Keep only the first three dot-separated numeric parts, padding missing ones with 0.
            string[] parts = raw.Split('.');
            string major = parts.Length > 0 && parts[0].Length != 0 ? parts[0] : "0";
            string minor = parts.Length > 1 && parts[1].Length != 0 ? parts[1] : "0";
            string revision = parts.Length > 2 && parts[2].Length != 0 ? parts[2] : "0";
            return $"{major}.{minor}.{revision}{prerelease}";
        }

        public FileInfo ConfigFile { get; private set; }
        public ConfigSource ConfigSource { get; private set; }
        internal Dictionary<string, string> CmdLineValues { get; set; }

        /////////////////////////////////////////////////
        // These params are root only params so first is always the same as all systems
        /////////////////////////////////////////////////
        public SystemType SystemType { get; set; } //filter system

        //these settings apply to all images, so just use the first one
        public TaskType TaskType => _systemSettings.First().Value.TaskType;
        public string[] In => _systemSettings.First().Value.In;
        public string LogOut => _systemSettings.First().Value.LogOut;
        public LogLevel LogOutLevel => _systemSettings.First().Value.LogOutLevel;
        public LogLevel ConsoleLevel => _systemSettings.First().Value.ConsoleLevel;
        public bool R => _systemSettings.First().Value.R;
        public bool Arc => _systemSettings.First().Value.Arc;
        /////////////////////////////////////////////////

        public AppSettings(string[] useCommandLineArgs) : this(null, null, useCommandLineArgs)
        {

        }
        public AppSettings(SystemPresetSettings presets) : this(presets, null, null)
        {

        }

        public AppSettings(string configFileName, string[] useCommandLineArgs) : this(null, configFileName, useCommandLineArgs)
        {
        }

        public AppSettings(string configFileName, IDictionary<string, string> overrideValues) : this(null, configFileName, null, overrideValues)
        {
        }

        private AppSettings(SystemPresetSettings presets, string configFileName, string[] useCommandLineArgs, IDictionary<string, string> overrideValues = null)
        {
            _presets = presets;

            using (Process p = Process.GetCurrentProcess())
                ThisExe = new FileInfo(p.MainModule.FileName);

            Dictionary<object, object> settings = null;
            Dictionary<object, object> dats = null;
            this.DatManager = new DatManager();
            this._log = new Log();
            _systemSettings = new Dictionary<SystemType, SystemSettings>();

            // Initialize configuration manager (new implementation)
            _configManager = new ConfigurationManager();

            if (useCommandLineArgs != null && useCommandLineArgs.Length != 0)
                parseCommandLine(useCommandLineArgs);
            else if (overrideValues != null)
                this.CmdLineValues = new Dictionary<string, string>(overrideValues, StringComparer.InvariantCultureIgnoreCase);

            if (_presets != null)
            {
                this.SystemType = _presets.System; //filter system
                _systemSettings.Add(_presets.System, _presets.ToSystemSettings()); //create a default set

                // Register dat collections from presets using the same method as CLI
                registerDatCollectionsFromPresets(_presets);
            }
            else //config files
            {
                ConfigFile = getConfig(configFileName); //exceptions are handled

                try
                {
                    string yamlContent = ConfigFile == null ? emptyConfig : File.ReadAllText(ConfigFile.FullName);
                    settings = AotYamlDeserializer.Deserialize(yamlContent);

                    // If the config file has no sys section (e.g. empty/comments-only file),
                    // fall back to the generated default config which has all system definitions
                    bool hasSysSection = false;
                    foreach (KeyValuePair<object, object> kv in settings)
                    {
                        if ((string)kv.Key == "sys") { hasSysSection = true; break; }
                    }
                    if (!hasSysSection && ConfigFile != null)
                    {
                        settings = AotYamlDeserializer.Deserialize(emptyConfig);
                    }
                }
                catch (Exception ex)
                {
                    throw new HandledException(ex, $"Failed to read config [{ConfigSource}]: {ConfigFile?.Name ?? "empty config"}");
                }

                // Process root parameters
                Dictionary<string, string> rootParams = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                foreach (KeyValuePair<object, object> rootVal in settings)
                {
                    try
                    {
                        if (rootVal.Value == null || rootVal.Value is string)
                        {
                            string stringValue = (string)rootVal.Value;
                            rootParams.Add((string)rootVal.Key, stringValue);
                        }
                        else if (rootVal.Value is List<object>)
                        {
                            string listValue = String.Join("\0", (List<object>)rootVal.Value);
                            rootParams.Add((string)rootVal.Key, listValue);
                        }
                        else if ((string)rootVal.Key == "sys")
                        {
                            if (rootVal.Value is Dictionary<object, object> sysDict)
                            {
                                settings = sysDict; //process after all root items are added
                            }
                            else
                            {
                                throw new HandledException($"Invalid sys section type: {rootVal.Value?.GetType()?.Name}");
                            }
                        }
                        else if ((string)rootVal.Key == "dats")
                        {
                            if (rootVal.Value is Dictionary<object, object> datsDict)
                            {
                                dats = datsDict; //process after all root items are added
                            }
                            else
                            {
                                throw new HandledException($"Invalid dats section type: {rootVal.Value?.GetType()?.Name}");
                            }
                        }
                    }
                    catch (InvalidCastException ex)
                    {
                        throw new HandledException($"Cast error processing config key '{rootVal.Key}': {ex.Message}");
                    }
                }

                if (rootParams.ContainsKey("system") && !string.IsNullOrWhiteSpace(rootParams["system"]))
                    this.SystemType = (SystemType)Enum.Parse(typeof(SystemType), rootParams["system"], true); //filter system

                if (rootParams.ContainsKey("tmp") && (rootParams["tmp"] ?? "").Contains("$system$"))
                    throw new HandledException($"Configuration error: 'tmp' cannot contain '$system$' [{ConfigSource}]: {ConfigFile?.Name}");

                if (rootParams.ContainsKey("logOut") && (rootParams["logOut"] ?? "").Contains("$system$"))
                    throw new HandledException($"Configuration error: 'logOut' cannot contain '$system$' [{ConfigSource}]: {ConfigFile?.Name}");

                if (dats != null)
                {
                    foreach (KeyValuePair<object, object> datItm in dats)
                    {
                        try
                        {
                            if (datItm.Value is string stringValue)
                            {
                                this.DatManager.Register((string)datItm.Key, stringValue);
                            }
                        }
                        catch (InvalidCastException)
                        {
                            // Skip this item and continue
                        }
                    }
                }

                // Register the dat collections supplied as root params / command-line overrides
                // (redumpDatsPath/noIntroDatsPath/tosecDatsPath — the CLI --dats-redump/-nointro/-tosec
                // options). These are equivalent to the nested 'dats:' section above but flow through
                // the flat param/override path. Override value wins over the root config value.
                registerDatCollectionsFromParams(rootParams);

                if (settings != null)
                {
                    foreach (KeyValuePair<object, object> sysItm in settings)
                    {
                        // Skip entries where the value is not a dictionary (e.g. plain string values)
                        if (sysItm.Value is not Dictionary<object, object> sysDict)
                            continue;

                        Dictionary<string, string> sysParams = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
                        foreach (KeyValuePair<object, object> sysVal in sysDict)
                        {
                            if (sysVal.Value == null || sysVal.Value is string)
                                sysParams.Add((string)sysVal.Key, (string)sysVal.Value);
                        }
                        SystemSettings sys = new SystemSettings(rootParams, (string)sysItm.Key, sysParams, this.CmdLineValues, this._log, _configManager);

                        if (sys.ErrorForcedParamsUnknown.Count != 0)
                            throw new HandledException("Param error: '{0}' not recognised", String.Join(", ", sys.ErrorForcedParamsUnknown));
                        if (sys.ErrorRootOnlyForcedParamsInSystem.Count != 0)
                            throw new HandledException("Param error: '{0}' root level only and not permitted in system {1}", String.Join(", ", sys.ErrorRootOnlyForcedParamsInSystem), (string)sysItm.Key);
                        // Unknown config file parameters are silently ignored for forward/backward compatibility
                        // (e.g. old configs with removed params like 'dedupeInPath')
                        if (sys.ErrorRootOnlyParamsInSystem.Count != 0)
                            throw new HandledException("Configuration error: config '{0}' root level only and not permitted in system {1}", String.Join(", ", sys.ErrorRootOnlyParamsInSystem), (string)sysItm.Key);

                        _systemSettings.Add(sys.SystemType, sys);
                    }
                }
            }

            // Ensure configuration is set up properly
        }

        /// <summary>
        /// Gets the default config filename based on the executable name
        /// </summary>
        private static string getAppBasedConfigFileName()
        {
            // Use ConfigurationManager to get the config file name based on the executable
            using ConfigurationManager configManager = new ConfigurationManager();
            return configManager.Context.ConfigFileName;
        }

        private FileInfo getConfig(string externalConfig)
        {
            string configName = null;
            try
            {
                string cfgPath = null;
                this.ConfigSource = ConfigSource.UserConfig;

                if (!string.IsNullOrWhiteSpace(externalConfig) && externalConfig.Trim().ToLower() != "n")
                {
                    this.ConfigSource = ConfigSource.External;
                    cfgPath = Path.IsPathRooted(externalConfig) ? externalConfig : Path.Combine(ThisExe.DirectoryName, externalConfig);
                }
                else if (!string.IsNullOrWhiteSpace(externalConfig)) // "n" → explicit no-config
                {
                    this.ConfigSource = ConfigSource.None;
                    cfgPath = null;
                }
                else if (CmdLineValues != null && CmdLineValues.ContainsKey(_ConfigParamName))
                {
                    this.ConfigSource = ConfigSource.CommandLine;
                    if (string.IsNullOrWhiteSpace(CmdLineValues[_ConfigParamName]) || CmdLineValues[_ConfigParamName].Trim().ToLower() == "n")
                    {
                        this.ConfigSource = ConfigSource.None;
                        cfgPath = null;
                    }
                    else
                        cfgPath = Path.IsPathRooted(CmdLineValues[_ConfigParamName]) ? CmdLineValues[_ConfigParamName] : Path.Combine(ThisExe.DirectoryName, CmdLineValues[_ConfigParamName]);
                }
                else
                {
                    // Use ConfigurationManager to determine config file location
                    ConfigurationInfo configInfo = _configManager.GetConfigurationInfo();
                    if (configInfo.HasValidConfiguration)
                    {
                        cfgPath = configInfo.ConfigFile;
                        this.ConfigSource = configInfo.ConfigSource;
                    }
                    else
                    {
                        this.ConfigSource = ConfigSource.None;
                        cfgPath = null;
                    }
                }

                if (this.ConfigSource != ConfigSource.None)
                {
                    FileInfo cfg = new FileInfo(cfgPath);
                    configName = cfg.FullName;
                    if (cfg == null)
                        throw new HandledException($"Configuration not found [{ConfigSource}]: '{configName}'");
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Configuration error [{ConfigSource}]: '{configName}'");
            }
            return null;
        }

        /// <summary>
        /// Ensures a default config file exists in the user config directory by copying from the local directory if available.
        /// This method is intended for CLI use to help users get started with a proper config file.
        /// Also creates the complete NKit folder structure if setting up for the first time.
        /// </summary>
        /// <returns>True if a new config file was created, false if one already existed</returns>
        public static bool EnsureDefaultConfigExists()
        {
            try
            {
                using ConfigurationManager configManager = new ConfigurationManager();
                SetupResult setupResult = configManager.EnsureConfiguration();
                return setupResult.ConfigFileCreated;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the path where config files are stored for this user
        /// </summary>
        /// <returns>The config directory path</returns>
        public static string GetUserConfigFullName()
        {
            using ConfigurationManager configManager = new ConfigurationManager();
            return Path.Combine(configManager.Context.ConfigDirectory, getAppBasedConfigFileName());
        }

        /// <summary>
        /// Gets the config directory, checking for portable mode first
        /// </summary>
        /// <returns>Config directory path - either portable (local) or user directory</returns>
        public static string GetConfigDirectory()
        {
            using ConfigurationManager configManager = new ConfigurationManager();
            return configManager.Context.ConfigDirectory;
        }

        /// <summary>
        /// Gets the cross-platform user config directory for NKit
        /// </summary>
        /// <returns>The user config directory path</returns>
        public static string GetUserConfigDirectory()
        {
            using ConfigurationManager configManager = new ConfigurationManager();
            return configManager.Context.ConfigDirectory;
        }

        /// <summary>
        /// Gets the user directory
        /// </summary>
        /// <returns>The user directory path</returns>
        public static string GetUserDirectory()
        {
            using ConfigurationManager configManager = new ConfigurationManager();
            return configManager.Context.UserDataDirectory;
        }

        private void parseCommandLine(string[] commandLineArgs)
        {
            Dictionary<string, string> cmdLine = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            CmdLineValues = cmdLine;
            try
            {
                if (commandLineArgs.Length > 0)
                    commandLineArgs[0] = ThisExe.Name;

                string name = null;
                List<string> ins = new List<string>();
                bool inMode = false; //"--" mode https://pubs.opengroup.org/onlinepubs/9699919799/basedefs/V1_chap12.html#tag_12_02

                for (int i = 1; i < commandLineArgs.Length; i++)
                {
                    if (!inMode && commandLineArgs[i] == "--")
                    {
                        inMode = true;
                        name = null;
                    }
                    else if (!inMode && name == null && commandLineArgs[i].Length > 0 && commandLineArgs[i][0] == _ParamNamePrefix && !commandLineArgs[i].Contains('.'))
                        name = commandLineArgs[i].Substring(1);
                    else
                    {
                        if (name == null || name == _InputFilePathParamName)
                            ins.Add(commandLineArgs[i]);
                        else
                        {
                            if (!cmdLine.ContainsKey(name))
                                cmdLine.Add(name, commandLineArgs[i]);
                        }
                        name = null;
                    }
                }
                if (name != null) //a param starting with - and no extra value
                    cmdLine.Add(name, "");

                if (ins.Count != 0)
                    cmdLine.Add(_InputFilePathParamName, String.Join("\0", ins));

                // Validate -ogmr requires a path value
                if (cmdLine.TryGetValue("ogmr", out string ogmrValue) && string.IsNullOrWhiteSpace(ogmrValue))
                    throw new HandledException("The -ogmr parameter requires a YAML file path argument.");
            }
            catch (Exception ex) when (ex is not HandledException)
            {
                throw new HandledException(ex, $"Command line error: {ex.Message}");
            }
        }

        public string ResolveAndCreateTempPath(SystemType system, string defaultPath)
        {
            string tmp = this[system].Tmp;

            if (string.IsNullOrWhiteSpace(tmp))
            {
                string outPath = this[system].Out;
                // Don't use an .nkds file as a temp directory — fall through to defaultPath
                if (!string.IsNullOrWhiteSpace(outPath) && !outPath.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    tmp = outPath;
            }

            if (string.IsNullOrWhiteSpace(tmp))
                tmp = defaultPath;

            if (File.Exists(tmp))
                throw new HandledException($"Temp path is an existing file '{tmp}'");
            else if (!Directory.Exists(tmp))
                Directory.CreateDirectory(tmp);

            return trimTrailingDirectorySeparators(tmp);
        }
        public string GetScanOutFilesPath(string defaultPath, SystemType systemType)
        {
            string path = this[systemType].ScanOut;

            if (this.TaskType == TaskType.Scan)
            {
                if (string.IsNullOrWhiteSpace(path))
                    path = this[systemType].Out;
                if (string.IsNullOrWhiteSpace(path))
                    path = defaultPath;
            }

            if (File.Exists(path))
                throw new HandledException($"ScanOut path is an existing file '{path}'");
            return trimTrailingDirectorySeparators(path);
        }

        public string GetOutFilesPath(string appendToPath, string defaultPath, SystemType systemType, TaskType taskType)
        {
            string path = null;
            string source;
            if (!string.IsNullOrWhiteSpace(this[systemType].Out))
            {
                path = ExpandPath(ExpandStatic(this[systemType].Out, taskType, systemType));
                source = "system setting";
            }
            else
            {
                source = defaultPath != null ? "default" : "cwd";
            }

            if (path == null)
                path = defaultPath ?? Environment.CurrentDirectory;

            if (!string.IsNullOrWhiteSpace(appendToPath))
                path = Path.Combine(path, appendToPath);

            // Detail: which source resolved the output path (answers "where did this path come from").
            ILogScope cfgScope = _log?.ScopeFor(LogScopes.Config);
            if (cfgScope != null && cfgScope.IsEnabled(LogLevel.Detail))
                cfgScope.Log(LogLevel.Detail,
                    $"Out path [{path}] (source: {source}, system: {systemType}, task: {taskType})");

            if (File.Exists(path) && !(taskType == TaskType.Dedupe && path.EndsWith(NKitDataStore.DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)))
                throw new HandledException($"Out path is an existing file '{path}'");

            return trimTrailingDirectorySeparators(path);
        }

        private static string trimTrailingDirectorySeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string root = Path.GetPathRoot(path) ?? string.Empty;
            string res = path;
            while (res.Length > (root?.Length ?? 0) && (res.EndsWith(Path.DirectorySeparatorChar) || res.EndsWith(Path.AltDirectorySeparatorChar)))
                res = res.Substring(0, res.Length - 1);
            return res;
        }

        internal static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                path = Environment.CurrentDirectory;
            //if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path);
            return path;
        }

        internal static string ExpandStatic(string value, TaskType taskType, SystemType systemType)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            using ConfigurationManager configManager = new ConfigurationManager();
            return configManager.ExpandConfigPath(value, taskType.ToString(), systemType.ToString());
        }

        public Log GetLog(Action<string, LogLevel> consoleLog, bool dynamic = false)
        {
            LogLevel conLevel = this.ConsoleLevel;
            LogLevel fileLevel = this.LogOutLevel;
            this._log.Initialise(conLevel, fileLevel, consoleLog, this.LogOut, dynamic);
            return this._log;
        }

        /// <summary>
        /// Dispose this AppSettings' Log (flushing + tearing down its NKitLog bus, which owns the
        /// async FileLogSink's background drain thread and its BlockingCollection wait handles).
        /// A host that creates a FRESH AppSettings PER IMAGE (the UI does) must call this once the
        /// image is done — otherwise each image's log sink thread + OS handles leak for the life of
        /// the (long-lived) process, showing up as steadily climbing thread/handle counts and working
        /// set even though the managed heap stays flat. A host that REUSES one AppSettings across all
        /// images (the CLI) should NOT call this until the whole run ends. Idempotent-safe: after
        /// disposal a later GetLog re-initialises the bus.
        /// </summary>
        public void DisposeLog()
        {
            try { _log?.Dispose(); } catch { }
        }

        private string normalizePath(string path)
        {
            return new DirectoryInfo(Path.GetFullPath(path)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).FullName
                       .ToUpperInvariant();
        }

        public SystemSettings this[SystemType type]
        {
            get
            {
                SystemSettings settings = null;
                if (_systemSettings.Count == 1 && _systemSettings.First().Key == SystemType.NotSet)
                {
                    settings = _systemSettings.First().Value;
                    _systemSettings.Clear();
                    _systemSettings.Add(type, settings);
                }
                else
                {
                    bool exists = _systemSettings.ContainsKey(type);
                    if (!exists && _presets != null)
                    {
                        settings = _presets.ToSystemSettings();
                        _systemSettings.Add(type, settings); //create a default set
                        exists = true;
                    }
                    if (!exists && _presets == null && _systemSettings.Count > 0)
                    {
                        // NKDS path: no presets available, but system settings exist for another type.
                        // Clone from the first available settings (typically the Default/NotSet entry)
                        // so the pipeline has something to work with.
                        settings = _systemSettings.First().Value;
                        _systemSettings[type] = settings;
                        exists = true;
                    }
                    if (exists)
                        settings = _systemSettings[type];
                    else
                        settings = _systemSettings[type]; // Will throw KeyNotFoundException if truly missing
                }
                if (settings.SystemType == SystemType.NotSet)
                    settings.UpdateNotSetSystemType(type);
                return settings;
            }
        }

        public SystemType GetSystem(string inPath)
        {
            string ip = normalizePath(inPath);
            var paths = _systemSettings.Where(a => !string.IsNullOrWhiteSpace(a.Value.BaseInPath))
                                    .Select(a => new { ImageType = a.Key, Path = a.Value.BaseInPath })
                                    .OrderByDescending(a => a.Path.Length);

            ILogScope cfgScope = _log?.ScopeFor(LogScopes.Config);
            foreach (var pth in paths)
            {
                if (ip.StartsWith(normalizePath(pth.Path)))
                {
                    if (cfgScope != null && cfgScope.IsEnabled(LogLevel.Detail))
                        cfgScope.Log(LogLevel.Detail,
                            $"System [{pth.ImageType}] routed from BaseInPath prefix [{pth.Path}]");
                    return pth.ImageType;
                }
            }

            SystemType resolved = this.SystemType != SystemType.NotSet ? this.SystemType : SystemType.Default;
            if (cfgScope != null && cfgScope.IsEnabled(LogLevel.Detail))
                cfgScope.Log(LogLevel.Detail,
                    $"System [{resolved}] (no BaseInPath match; fallback)");
            return resolved;
        }

        public void OverrideDedupeSettings(string dedupe)
        {
            foreach (SystemSettings settings in _systemSettings.Values)
                settings.OverrideDedupeSettings(dedupe);
        }

        /// <summary>
        /// Overrides only the set name in each system's dedupe config, preserving
        /// the system's own shard size, block size, and aux mode settings.
        /// Used by 1GMR routing where only the set name changes per file.
        /// </summary>
        public void OverrideDedupeSetName(string setName)
        {
            foreach (SystemSettings settings in _systemSettings.Values)
                settings.OverrideDedupeSetName(setName);
        }

        /// <summary>
        /// Checks the config and returns false if no In values were set. Caller should prompt user / usage
        /// </summary>
        /// <param name="l"></param>
        public bool Check(ILogScope l)
        {
            l.Info(() => $"NKit v{this.Version} :: Help & Usage - https://github.com/Nanook/NKit/wiki"); // header — no tag
            l.Info(() => Log.Divider);
            l.Info(() => $"Config [{this.ConfigSource}]" + (this.ConfigFile == null ? "" : $" {this.ConfigFile.Name}"));
            l.Info(() => $"ConfigPath [{GetConfigDirectory()}]");

            if (this.SystemType != SystemType.NotSet)
                l.Info(() => $"System filter [{this.SystemType}]");
            l.Info(() => $"Task [{this.TaskType}]");

            return this.In != null && this.In.Length != 0 && this.TaskType != TaskType.NotSet;
        }

        /// <summary>
        /// Generates default configuration using the configuration services instead of hardcoded values
        /// </summary>
        private static string generateDefaultConfig()
        {
            StringBuilder sb = new StringBuilder();

            // Root level defaults (canonical CLI names; legacy names still accepted when reading)
            sb.AppendLine("input: ");
            sb.AppendLine("output: ");
            sb.AppendLine("scan-out: ");
            sb.AppendLine("scan-in: ");
            sb.AppendLine("temp: ");
            sb.AppendLine("recursive: n");
            sb.AppendLine("archives: y");
            sb.AppendLine("verify: y");
            sb.AppendLine("task: ");
            sb.AppendLine("system:");
            sb.AppendLine($"console-level: {ConfigSettingsDefaults.GetDefaultLogLevel().ToString().ToLower()}");
            sb.AppendLine($"log-level: {ConfigSettingsDefaults.GetDefaultLogLevel().ToString().ToLower()}");
            sb.AppendLine("log: $configPath$/logs/$date$_log.txt");
            sb.AppendLine("results: n");
            sb.AppendLine("results-out: ");
            sb.AppendLine("dat-match: n");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("sys:");

            // Generate system-specific defaults using configuration services
            (SystemType, string)[] systems = new[]
            {
                (SystemType.GameCube, "gamecube"),
                (SystemType.Wii, "wii"),
                (SystemType.WiiU, "wiiu"),
                (SystemType.PS1, "ps1"),
                (SystemType.PS2, "ps2"),
                (SystemType.PS3, "ps3"),
                (SystemType.PSP, "psp"),
                (SystemType.Dreamcast, "dreamcast"),
                (SystemType.Saturn, "saturn"),
                (SystemType.SegaCD, "segaCd"),
                (SystemType.CDi, "cdi"),
                (SystemType.PcEngine, "pcEngine"),
                (SystemType.Default, "default")
            };

            foreach ((SystemType systemType, string systemName) in systems)
            {
                sb.AppendLine($"  {systemName}:");
                sb.AppendLine("    base-in: ");
                sb.AppendLine("    dat: ");

                // Add keys for systems that need them
                if (systemType == SystemType.Wii || systemType == SystemType.WiiU || systemType == SystemType.PS3)
                {
                    sb.AppendLine("    keys: ");
                }

                // Add fix-files and fix-info for Nintendo systems
                if (systemType == SystemType.GameCube || systemType == SystemType.Wii)
                {
                    sb.AppendLine("    fix-files: ");
                    sb.AppendLine("    fix-info: ");
                }

                // Generate convert format using configuration services
                string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(systemType);
                string convertFormat;

                switch (defaultFormat)
                {
                    case "rvz":
                        // Generate full RVZ format string with defaults
                        convertFormat = ConfigSettingsFormatGenerator.GenerateRvzFormatString(
                            RvzEncodingType.ZStd,
                            ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd),
                            ConfigSettingsDefaults.GetDefaultBlockSize(systemType, defaultFormat),
                            ConfigSettingsDefaults.GetDefaultParallelism(systemType));
                        break;

                    case "cue":
                        // Generate CUE format string with defaults
                        convertFormat = ConfigSettingsFormatGenerator.GenerateCueFormatString();
                        break;

                    default:
                        // Simple formats without parameters
                        convertFormat = defaultFormat;
                        break;
                }

                sb.AppendLine($"    format: {convertFormat}");
            }

            return sb.ToString();
        }

        private static string emptyConfig => generateDefaultConfig();

        /// <summary>
        /// Registers the dat collections supplied via flat root params or command-line overrides
        /// (keys redumpDatsPath/noIntroDatsPath/tosecDatsPath → collection types redump/nointro/tosec).
        /// A command-line override value takes precedence over the root config value for the same key.
        /// Mirrors <see cref="registerDatCollectionsFromPresets"/> for the non-preset (config/CLI) path.
        /// </summary>
        private void registerDatCollectionsFromParams(IDictionary<string, string> rootParams)
        {
            register("redumpDatsPath", "redump");
            register("noIntroDatsPath", "nointro");
            register("tosecDatsPath", "tosec");

            void register(string paramKey, string collectionType)
            {
                // Override (command line) wins over the root config value.
                string path = null;
                if (this.CmdLineValues != null && this.CmdLineValues.TryGetValue(paramKey, out string ov) && !string.IsNullOrEmpty(ov))
                    path = ov;
                else if (rootParams != null && rootParams.TryGetValue(paramKey, out string rv) && !string.IsNullOrEmpty(rv))
                    path = rv;

                if (string.IsNullOrEmpty(path))
                    return;

                try
                {
                    this.DatManager.Register(collectionType, path);
                }
                catch (Exception ex)
                {
                    // Log but don't fail — same behaviour as the preset/YAML paths.
                    System.Diagnostics.Debug.WriteLine($"Failed to register dat collection '{collectionType}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Registers dat collections from SystemPresetSettings using the same logic as CLI YAML processing
        /// </summary>
        private void registerDatCollectionsFromPresets(SystemPresetSettings presets)
        {
            // Create a dictionary similar to the CLI dats section and reuse the same registration logic
            Dictionary<string, string> datCollections = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(presets.RedumpDatsPath))
                datCollections.Add("redump", presets.RedumpDatsPath);

            if (!string.IsNullOrEmpty(presets.NoIntroDatsPath))
                datCollections.Add("nointro", presets.NoIntroDatsPath);

            if (!string.IsNullOrEmpty(presets.TosecDatsPath))
                datCollections.Add("tosec", presets.TosecDatsPath);

            // Use the same registration logic as CLI
            foreach (KeyValuePair<string, string> datCollection in datCollections)
            {
                try
                {
                    this.DatManager.Register(datCollection.Key, datCollection.Value);
                }
                catch (Exception ex)
                {
                    // Log but don't fail - same behavior as CLI
                    System.Diagnostics.Debug.WriteLine($"Failed to register dat collection '{datCollection.Key}': {ex.Message}");
                }
            }
        }
    }
}