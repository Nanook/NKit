using System;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Reads RAW config values (pre-expansion, as written in the YAML) to seed the interactive
    /// table as defaults, and locates the config file. Uses <see cref="YamlConfigEditor"/> so values
    /// round-trip exactly to the file on write-back.
    /// </summary>
    public sealed class ConfigDefaults
    {
        private readonly YamlConfigEditor _yaml;

        public ConfigDefaults(YamlConfigEditor yaml, string path)
        {
            _yaml = yaml;
            Path = path;
        }

        /// <summary>The config file path (for write-back), or null if none was found.</summary>
        public string Path { get; }

        public bool HasConfig => _yaml != null;

        /// <summary>
        /// True if the config specifies a root-level input (canonical <c>input</c> or legacy
        /// <c>in</c>), as either an inline value or a YAML list block. Drives the interactive-mode
        /// gate: if the config already has an input, there is something to run.
        /// </summary>
        public bool HasInput()
        {
            if (_yaml == null) return false;
            CliOption input = OptionModel.FindOption("input");
            return input != null && _yaml.HasRootValue(Keys(input));
        }

        /// <summary>
        /// True if the config specifies a root-level <c>task</c> (the command verb). A blank
        /// <c>task:</c> counts as "no task". Drives the interactive-mode gate together with
        /// <see cref="HasInput"/>: a config that supplies BOTH a task and an input can run directly.
        /// </summary>
        public bool HasTask() => !string.IsNullOrEmpty(TaskValue());

        /// <summary>
        /// The raw root-level <c>task</c> value from the config (the command verb name), or null if
        /// none. Used to pre-select the default task in the interactive builder and to know whether
        /// the config alone can drive a direct run.
        /// </summary>
        public string TaskValue()
        {
            string t = _yaml?.GetRoot("task");
            return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        }

        public static ConfigDefaults Load(string configPath)
        {
            if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
                return new ConfigDefaults(null, configPath);
            return new ConfigDefaults(YamlConfigEditor.Load(configPath), configPath);
        }

        /// <summary>
        /// The raw config value for an option: the per-system value when <paramref name="system"/> is
        /// set (falling back to root), otherwise the root value. Null if the config has nothing.
        /// </summary>
        public string ValueFor(CliOption o, string system)
        {
            if (_yaml == null || o == null) return null;
            // Config may use the canonical (new) key or the legacy key; accept both.
            string[] keys = Keys(o);
            if (!string.IsNullOrEmpty(system) && o.Prefixable)
            {
                string sysVal = _yaml.GetSystem(system, keys);
                if (sysVal != null) return sysVal;
            }
            return _yaml.GetRoot(keys);
        }

        /// <summary>Canonical + legacy config key names for an option (canonical first).</summary>
        internal static string[] Keys(CliOption o)
        {
            if (o.ConfigKey != null && !string.Equals(o.ConfigKey, o.Canonical, StringComparison.OrdinalIgnoreCase))
                return new[] { o.Canonical, o.ConfigKey };
            return new[] { o.Canonical };
        }

        internal YamlConfigEditor Yaml => _yaml;
    }
}