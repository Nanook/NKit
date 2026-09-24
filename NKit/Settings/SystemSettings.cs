using Nanook.NKit.Configuration;
using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    public class SystemSettings
    {
        private IDictionary<string, string> _rootParams;
        private string _system;
        private IDictionary<string, string> _systemParams;
        private IDictionary<string, string> _overrideParams;
        private ILogScope _log;
        private Func<string, string, string, string> _expandConfigPath;

        private enum Param { _in, _out, cfg, task, system, scanIn, scanOut, scanFormat, tmp, r, arc, v, consoleLevel, logOutLevel, logOut, results, resultsOut, baseInPath, dedupe, ogmr, fixInfo, fixFiles, dat, keys, convert, extract, outAsDatMatch, deleteProcessed, skipIfCompleted, redumpDatsPath, noIntroDatsPath, tosecDatsPath }

        internal SystemSettings(IDictionary<string, string> rootParams, string system, IDictionary<string, string> systemParams, IDictionary<string, string> overrideParams, ILogScope log, object configManager = null)
        {
            _log = log;
            _rootParams = rootParams;
            _system = system;
            _overrideParams = overrideParams;
            _systemParams = systemParams;

            // Accept ConfigurationManager or create a singleton instance
            if (configManager is ConfigurationManager configV2)
            {
                _expandConfigPath = (path, task, system) => configV2.ExpandConfigPath(path, task, system);
            }
            else
            {
                // Create a single instance that will live for the duration of this object
                ConfigurationManager fallbackConfig = new ConfigurationManager();
                _expandConfigPath = (path, task, system) => fallbackConfig.ExpandConfigPath(path, task, system);
            }

            this.ErrorForcedParamsUnknown = new List<string>();
            this.ErrorRootParamsUnknown = new List<string>();
            this.ErrorSystemParamsUnknown = new List<string>();
            this.ErrorRootOnlyParamsInSystem = new List<string>();
            this.ErrorRootOnlyForcedParamsInSystem = new List<string>();
            // Known params = the legacy Param enum names PLUS the new canonical (CLI long) names,
            // so a config/override written with either spelling passes validation. "parallelism" is
            // a RETIRED knob (worker count is now fully autoscaled): still ACCEPTED for back-compat
            // with existing configs so it doesn't trip the unknown-param check, but it is IGNORED.
            string[] knownParams = ((Param[])Enum.GetValues(typeof(Param))).Select(a => a.ToString().TrimStart('_'))
                .Concat(ConfigKeyAliases.CanonicalNames)
                .Concat(new[] { "parallelism" }) // retired, accepted-but-ignored
                .Distinct().ToArray();
            // Root-only set, with the canonical spelling of each legacy root-only param added too.
            string[] legacyRootOnly = (new[] { Param.task, Param.system, Param._in, Param.r, Param.arc, Param.logOut, Param.logOutLevel, Param.consoleLevel })
                .Select(a => a.ToString().TrimStart('_')).ToArray();
            string[] rootOnlyParams = legacyRootOnly
                .Concat(legacyRootOnly.Select(ConfigKeyAliases.ToCanonical).Where(a => a != null))
                .Distinct().ToArray();
            string tmp = null;

            try
            {
                this.SystemType = (SystemType)Enum.Parse(typeof(SystemType), system, true);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Config Error: 'systems:{system ?? "<null>"}' is not valid system type.'");
            }

            if (this.SystemType == SystemType.NotSet)
            {
                try
                {
                    if (_overrideParams.TryGetValue("system", out tmp) || (rootParams.TryGetValue("system", out tmp) && !string.IsNullOrWhiteSpace(tmp)))
                        this.SystemType = getEnum<SystemType>(Param.system);
                    else
                        this.SystemType = SystemType.NotSet;
                }
                catch (Exception ex)
                {
                    throw new HandledException(ex, $"Config Error: The value for 'system' is not valid - '{tmp ?? "<null>"}.'");
                }
            }
            tmp = null;
            try
            {
                this.TaskType = getEnum(Param.task, TaskType.NotSet);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Config Error: The value for 'task' cmdline/config is invalid");
            }

            if (_overrideParams != null)
            {
                foreach (KeyValuePair<string, string> kp in _overrideParams) //root only params set on cmd line e.g. wii:tmp
                {
                    string[] s = kp.Key.Split(':');
                    if (s.Length > 2)
                        ErrorForcedParamsUnknown.Add(kp.Key);
                    else
                    {
                        string kSys = s.Length == 2 ? s[0] : "";
                        string kPrm = s.Length == 2 ? s[1] : s[0];
                        if (kSys == "" || _system == kSys) //only check params for this system - root params will be defaulted to this system
                        {
                            if (kSys != "" && rootOnlyParams.FirstOrDefault(a => a.Equals(kPrm, StringComparison.OrdinalIgnoreCase)) != null)
                                ErrorRootOnlyForcedParamsInSystem.Add(kp.Key);
                            else if (knownParams.FirstOrDefault(a => a.Equals(kPrm, StringComparison.OrdinalIgnoreCase)) == null)
                                ErrorForcedParamsUnknown.Add(kp.Key);
                        }
                    }
                }
            }
            foreach (KeyValuePair<string, string> kp in _systemParams) //root only params set in config e.g. sys/wii/tmp
            {
                if (rootOnlyParams.FirstOrDefault(a => a.Equals(kp.Key, StringComparison.OrdinalIgnoreCase)) != null)
                    ErrorRootOnlyParamsInSystem.Add(kp.Key);
                else if (knownParams.FirstOrDefault(a => a.Equals(kp.Key, StringComparison.OrdinalIgnoreCase)) == null)
                    ErrorSystemParamsUnknown.Add(kp.Key);
            }
            foreach (KeyValuePair<string, string> kp in _rootParams)
            {
                if (knownParams.FirstOrDefault(a => a.Equals(kp.Key, StringComparison.OrdinalIgnoreCase)) == null)
                    ErrorRootParamsUnknown.Add(kp.Key);
            }


            this.R = getVal(Param.r)?.ToLower() == "y";
            this.Arc = getVal(Param.arc)?.ToLower() == "y";
            this.DeleteProcessed = getVal(Param.deleteProcessed)?.ToLower() == "y";
            this.SkipIfCompleted = getVal(Param.skipIfCompleted)?.ToLower() == "y";
            this.Results = getVal(Param.results)?.ToLower() == "y";
            this.OutAsDatMatch = getVal(Param.outAsDatMatch)?.ToLower() == "y";

            this.ConsoleLevel = getLogLevel(Param.consoleLevel);
            this.LogOutLevel = getLogLevel(Param.logOutLevel);
            this.V = getEnum(Param.v, Verify.N);
            // Scan output format is an UNDOCUMENTED, config-only setting (no CLI/UI option). XML is
            // legacy and only honoured for the Scan task; every other task always emits YAML so the
            // produced/verified scans stay in the query-friendly format. Default is YamlCompact.
            this.ScanFormat = getEnum(Param.scanFormat, ScanFormat.YamlCompact);
            if (this.ScanFormat == ScanFormat.Xml && this.TaskType != TaskType.Scan)
                this.ScanFormat = ScanFormat.YamlCompact;
            if (this.TaskType == TaskType.FixExtract && this.V != Verify.N)
                this.V = Verify.N; //force off
            else if (this.TaskType == TaskType.Verify && this.V == Verify.N) //turn on verify if off and verify task
                this.V = Verify.Y; //force on

            this.Convert = getVal(Param.convert);
            this.Extract = getVal(Param.extract) ?? "ri:.*";

            string inPath = getVal(Param._in);
            if (!string.IsNullOrWhiteSpace(inPath))
                this.In = inPath.Split('\0').Select(p => expand(expandPath(p, Environment.CurrentDirectory), Param._in)).ToArray();
            this.LogOut = getPath(Param.logOut, null);
            this.BaseInPath = getPath(Param.baseInPath, null);
            this.Dedupe = getVal(Param.dedupe);
            this.DedupeConfig = Configuration.ConfigSettingsFormatParser.ParseDedupeConfiguration(this.Dedupe);

            string ogmrRaw = getVal(Param.ogmr);
            if (!string.IsNullOrWhiteSpace(ogmrRaw))
            {
                OgmrYamlPath = Path.IsPathRooted(ogmrRaw)
                    ? ogmrRaw
                    : Path.GetFullPath(ogmrRaw);
            }

        }

        public void Initialise(SystemType imageSystemType, TaskType processingTask, ILogScope log, DatManager datManager)
        {
            this.SystemType = imageSystemType;
            this.TaskType = processingTask;
            this.Out = getPath(Param._out, "");
            this.ScanIn = getPath(Param.scanIn, null);
            this.ScanOut = getPath(Param.scanOut, null);
            this.Tmp = getPath(Param.tmp, null);
            this.FixInfo = getPath(Param.fixInfo, null);
            this.FixFiles = getPath(Param.fixFiles, null);
            this.Dat = getPath(Param.dat, null);
            this.Keys = getPath(Param.keys, null);
            this.ResultsOut = getPath(Param.resultsOut, null);

            // Initialize dat collections (these are global settings, not paths)
            // Note: Registration with DatManager is handled by AppSettings to avoid duplication
            this.RedumpDatsPath = getVal(Param.redumpDatsPath);
            this.NoIntroDatsPath = getVal(Param.noIntroDatsPath);
            this.TosecDatsPath = getVal(Param.tosecDatsPath);

            SettingsDataProvider lookup = new SettingsDataProvider(this.SystemType, _log, this.ScanIn, this.FixInfo, this.FixFiles, this.Keys, datManager);
            lookup.DedupePath = this.Out;
            this.Lookup = lookup;
        }

        private T getEnum<T>(Param prm) => (T)Enum.Parse(typeof(T), getVal(prm), true);

        // LogLevel parse with back-compat: the legacy enum had "Debug" where the unified enum now
        // uses "Trace". Normalise the stored/CLI name so existing configs still read correctly.
        private LogLevel getLogLevel(Param prm)
        {
            string v = getVal(prm);
            if (string.IsNullOrWhiteSpace(v))
                return LogLevel.None;
            if (v.Trim().Equals("Debug", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Trace;
            return (LogLevel)Enum.Parse(typeof(LogLevel), v, true);
        }
        private T getEnum<T>(Param prm, T defaultEnum)
        {
            string v = getVal(prm);
            if (string.IsNullOrWhiteSpace(v))
                return defaultEnum;
            return (T)Enum.Parse(typeof(T), v, true);
        }
        private string getPath(Param prm, string defaultPath) => expandPath(expand(getVal(prm), prm), defaultPath);

        private string getVal(Param prm)
        {
            string name = prm.ToString().TrimStart('_'); //remove the leading '_' on reserved enum names
            string key = $"{_system}:{name}";
            if (_overrideParams?.ContainsKey(key) ?? false)
                return _overrideParams[key];
            else if (_overrideParams?.ContainsKey(name) ?? false)
                return _overrideParams[name];
            else if (_systemParams.ContainsKey(name))
                return _systemParams[name];
            else if (_rootParams.ContainsKey(name))
                return _rootParams[name];

            // Forward-compat: also accept the new canonical (CLI long-form) key spelling in config
            // and overrides, mapping it back to this legacy param. Legacy names above take precedence.
            string canonical = ConfigKeyAliases.ToCanonical(name);
            if (canonical != null)
            {
                string cKey = $"{_system}:{canonical}";
                if (_overrideParams?.ContainsKey(cKey) ?? false)
                    return _overrideParams[cKey];
                else if (_overrideParams?.ContainsKey(canonical) ?? false)
                    return _overrideParams[canonical];
                else if (_systemParams.ContainsKey(canonical))
                    return _systemParams[canonical];
                else if (_rootParams.ContainsKey(canonical))
                    return _rootParams[canonical];
            }
            return null;
        }

        private string expand(string value, Param prm)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if ((prm == Param._in || prm == Param.logOut) && (value.Contains("$task$") || value.Contains("$system$") || value.Contains("baseInPath")))
                throw new HandledException("$task$ and $system$ are not allowed for 'in','logOut' or 'baseInPath' params");

            // Use the injected path expansion function
            return _expandConfigPath(value,
                                   this.TaskType.ToString(),
                                   this.SystemType == SystemType.NotSet ? "" : this.SystemType.ToString());
        }

        internal static string expandPath(string path, string defaultPath)
        {
            string arc = "";
            int midx = path.LastIndexOf("//");
            if (midx != -1 && midx != 0) //can't start with // (might be unc) 
            {
                arc = path.Substring(midx);
                path = path.Substring(0, midx);
            }


            if (string.IsNullOrWhiteSpace(path))
                path = defaultPath;
            //if (!Path.IsPathRooted(path))
            if (!string.IsNullOrWhiteSpace(path))
            {
                string pth = SourceFiles.GetPathRemoveMasks(path);
                path = Path.GetFullPath(pth) + path.Substring(pth.Length, path.Length - pth.Length);
            }
            // Trim any trailing directory separators from the resolved base path
            // (but preserve root paths such as "/" or "C:\\") so combining
            // with other segments does not produce duplicate separators.
            path = trimTrailingDirectorySeparators(path);
            return path + arc;
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

        internal void UpdateNotSetSystemType(SystemType type)
        {
            if (type == SystemType.NotSet)
                throw new HandledException("Can not set to SystemType to NotSet");
            if (this.SystemType == SystemType.NotSet)
                this.SystemType = type;
        }

        public void OverrideDedupeSettings(string dedupe)
        {
            if (this.Lookup is SettingsDataProvider sdp)
                sdp.DedupePath = this.Out;

            Dedupe = string.IsNullOrWhiteSpace(dedupe) ? null : dedupe.Trim();
            DedupeConfig = Configuration.ConfigSettingsFormatParser.ParseDedupeConfiguration(Dedupe);
        }

        /// <summary>
        /// Overrides only the set name in the dedupe config, preserving the existing
        /// shard size, block size, and aux mode settings from this system's config.
        /// </summary>
        public void OverrideDedupeSetName(string setName)
        {
            if (this.Lookup is SettingsDataProvider sdp)
                sdp.DedupePath = this.Out;

            // Preserve existing shard/block/aux from current config, only change set name
            DedupeConfiguration config = DedupeConfig ?? new Configuration.DedupeConfiguration();
            config.SetName = setName;
            DedupeConfig = config;

            // Regenerate the Dedupe string to match
            string shardStr = config.ShardSize == Configuration.DedupeConfiguration.DefaultShardSize
                ? null : config.ShardSize == 0 ? "0" : config.ShardSize.ToString();
            string blockStr = config.BlockSize == 0 ? "0" : config.BlockSize.ToString();
            string auxStr = config.AutoCreateAux ? "y" : null;
            Dedupe = Configuration.ConfigSettingsFormatGenerator.GenerateDedupeFormatString(setName, shardStr, blockStr, auxStr);
        }

        public List<string> ErrorForcedParamsUnknown { get; }
        public List<string> ErrorRootParamsUnknown { get; }
        public List<string> ErrorSystemParamsUnknown { get; }
        public List<string> ErrorRootOnlyParamsInSystem { get; }
        public List<string> ErrorRootOnlyForcedParamsInSystem { get; }

        public string[] In { get; }
        public string LogOut { get; }

        public string Out { get; private set; }
        public string ScanIn { get; private set; }
        public string ScanOut { get; private set; }
        public ScanFormat ScanFormat { get; private set; }
        public string Tmp { get; private set; }
        public string ResultsOut { get; private set; }
        internal IDataProvider Lookup { get; private set; }
        public string BaseInPath { get; private set; }
        public string Dedupe { get; private set; }
        public Configuration.DedupeConfiguration DedupeConfig { get; private set; }
        public string OgmrYamlPath { get; private set; }
        public string FixInfo { get; private set; }
        public string FixFiles { get; private set; }
        public string Dat { get; private set; }
        public string Keys { get; private set; }

        // Dat collection properties (global, read from root params)
        public string RedumpDatsPath { get; private set; }
        public string NoIntroDatsPath { get; private set; }
        public string TosecDatsPath { get; private set; }

        public bool R { get; }
        public bool Arc { get; }
        public Verify V { get; }
        public bool DeleteProcessed { get; }
        public bool SkipIfCompleted { get; }
        public bool Results { get; }
        public TaskType TaskType { get; private set; }
        public SystemType SystemType { get; private set; } //from the config file - can be blank
        public LogLevel ConsoleLevel { get; }
        public LogLevel LogOutLevel { get; }
        public bool OutAsDatMatch { get; }
        public string Convert { get; }
        public string Extract { get; }

        /// <summary>
        /// Gets the appropriate index extension for the system type using configuration services
        /// </summary>
        public string ExpandIndexExtension
        {
            get
            {
                // Use configuration service to get default format, then determine index extension
                string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(this.SystemType);

                return this.SystemType switch
                {
                    SystemType.WiiU => "tmd",        // WiiU uses TMD files for indexing
                    SystemType.Dreamcast => ConfigSettingsConstants.FormatGdi, // Dreamcast uses GDI
                    _ => ConfigSettingsConstants.FormatCue                      // Most systems use CUE
                };
            }
        }

        /// <summary>
        /// Gets the appropriate expansion extension for the system type using configuration services
        /// </summary>
        public string ExpandExtension
        {
            get
            {
                // Use configuration service to determine appropriate extension based on system
                IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(this.SystemType);

                // Prefer ISO for systems that support it, otherwise use CUE
                if (supportedFormats.Contains(ConfigSettingsConstants.FormatIso, StringComparer.OrdinalIgnoreCase))
                {
                    return ConfigSettingsConstants.FormatIso;
                }

                return ConfigSettingsConstants.FormatCue;
            }
        }

        /// <summary>
        /// Gets the convert extension from the format string using configuration services
        /// </summary>
        public string ConvertExtension
        {
            get
            {
                string fmt = (this.Convert ?? "").Split(':')[0].ToLower();
                if (!string.IsNullOrWhiteSpace(fmt))
                {
                    // Validate that the format is supported by this system
                    IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(this.SystemType);
                    if (supportedFormats.Contains(fmt, StringComparer.OrdinalIgnoreCase))
                    {
                        return fmt;
                    }
                }

                // Fall back to expand extension if convert format is invalid or empty
                return this.ExpandExtension;
            }
        }

        /// <summary>
        /// Validates the current convert format string using the configuration services
        /// </summary>
        public (bool IsValid, string ErrorMessage, IEnumerable<string> Warnings) ValidateConvertFormat()
        {
            if (string.IsNullOrWhiteSpace(this.Convert))
            {
                return (true, null, Array.Empty<string>()); // No convert format is valid
            }

            try
            {
                // Use configuration service's unified validation
                ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(this.Convert);

                if (!validationResult.IsValid)
                {
                    return (false, validationResult.ErrorMessage, Array.Empty<string>());
                }

                // Get configuration warnings
                string[] formatParts = this.Convert.Split(':');
                string format = formatParts.Length > 0 ? formatParts[0] : "";
                string level = formatParts.Length > 2 ? formatParts[2] : "";
                string blockSize = formatParts.Length > 3 ? formatParts[3] : "";

                IEnumerable<string> warnings = ConfigSettingsFormatValidator.GetConfigurationWarnings(this.SystemType, format, level, blockSize);
                return (true, null, warnings);
            }
            catch (Exception ex)
            {
                return (false, $"Format validation failed: {ex.Message}", Array.Empty<string>());
            }
        }

        /// <summary>
        /// Gets the default convert format for this system using the configuration services
        /// </summary>
        public string GetDefaultConvertFormat()
        {
            string defaultFormat = ConfigSettingsDefaults.GetDefaultFormat(this.SystemType);

            // Generate full format string with defaults based on format type
            return defaultFormat switch
            {
                var f when f == ConfigSettingsConstants.FormatRvz => ConfigSettingsFormatGenerator.GenerateRvzFormatString(
                    RvzEncodingType.ZStd,
                    ConfigSettingsDefaults.GetDefaultCompressionLevel(RvzEncodingType.ZStd),
                    ConfigSettingsDefaults.GetDefaultBlockSize(this.SystemType, defaultFormat),
                    ConfigSettingsDefaults.GetDefaultParallelism(this.SystemType)),

                var f when f == ConfigSettingsConstants.FormatCue => ConfigSettingsFormatGenerator.GenerateCueFormatString(),

                _ => defaultFormat // Simple formats without parameters
            };
        }
    }
}