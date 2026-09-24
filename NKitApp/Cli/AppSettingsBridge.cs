using System;
using System.Collections.Generic;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Maps a <see cref="ParsedCommand"/> to the existing NKit override dictionary consumed by
    /// <c>new AppSettings(configFileName, overrideValues)</c>. Keys are the legacy NKit param names
    /// (from <see cref="CliOption.ConfigKey"/>); system-scoped values use the existing
    /// <c>&lt;system&gt;:&lt;param&gt;</c> override form the config layer already understands.
    /// <para>
    /// The engine and config precedence are unchanged — this is purely a front-end translation.
    /// </para>
    /// </summary>
    public static class AppSettingsBridge
    {
        /// <summary>Build the (configFile, overrides) pair for AppSettings from a parsed command.</summary>
        public static (string configFile, Dictionary<string, string> overrides) Build(ParsedCommand cmd)
        {
            Dictionary<string, string> o = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            // task from the verb
            if (cmd.Verb != null)
                set(o, null, "task", cmd.Verb.Task);

            // inputs → the 'in' key, multiple joined by NUL (matches AppSettings.parseCommandLine)
            if (cmd.Inputs.Count != 0)
                o["in"] = string.Join("\0", cmd.Inputs);

            string configFile = null;

            foreach (ScopedValue v in cmd.Values)
            {
                CliOption opt = v.Option;

                // 'config' is not a config key — it selects the config file itself.
                if (string.Equals(opt.Canonical, "config", StringComparison.OrdinalIgnoreCase))
                {
                    configFile = v.Value;
                    continue;
                }

                // Options with no config key are CLI-only modifiers handled elsewhere (e.g. forensic
                // is folded into the extract mask below).
                if (opt.ConfigKey == null)
                    continue;

                if (opt.IsSwitch)
                {
                    // Switches map to the legacy y/n params.
                    set(o, v.System, opt.ConfigKey, switchValue(opt));
                }
                else
                {
                    set(o, v.System, opt.ConfigKey, v.Value);
                }
            }

            // --no-config forces "no config file", reusing the same mechanism as --config n.
            // It takes precedence over any --config value (a config file cannot be both selected
            // and disabled). AppSettings treats "n" / empty as ConfigSource.None.
            if (cmd.HasSwitch("no-config"))
                configFile = "n";

            // Forensic: the legacy 'extract' string carries an 'f' flag. If --forensic was given,
            // ensure the extract mask value(s) include it. Applied after the loop so it can augment
            // whatever mask value was set (scoped or unscoped).
            if (cmd.HasSwitch("forensic"))
                applyForensic(o);

            return (configFile, o);
        }

        private static string switchValue(CliOption opt)
        {
            // 'no-archives' inverts: presence means archives OFF (arc=n). All other switches → y.
            if (string.Equals(opt.Canonical, "no-archives", StringComparison.OrdinalIgnoreCase))
                return "n";
            return "y";
        }

        private static void applyForensic(Dictionary<string, string> o)
        {
            // Add the forensic 'f' flag to every 'extract' key (scoped or not). If none present,
            // create a default forensic extract that selects everything.
            List<string> extractKeys = new List<string>();
            foreach (string k in o.Keys)
                if (k.Equals("extract", StringComparison.OrdinalIgnoreCase) || k.EndsWith(":extract", StringComparison.OrdinalIgnoreCase))
                    extractKeys.Add(k);

            if (extractKeys.Count == 0)
            {
                o["extract"] = "fri:.*";
                return;
            }

            foreach (string k in extractKeys)
                o[k] = addForensicFlag(o[k]);
        }

        // extract value is "flags:mask"; ensure 'f' is in the flags segment.
        private static string addForensicFlag(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "fri:.*";
            int c = value.IndexOf(':');
            if (c < 0)
                return "f" + value; // malformed — prepend
            string flags = value.Substring(0, c);
            string rest = value.Substring(c);
            if (flags.IndexOf('f') < 0)
                flags = "f" + flags;
            return flags + rest;
        }

        private static void set(Dictionary<string, string> o, string system, string key, string value)
        {
            string k = system == null ? key : system + ":" + key;
            o[k] = value ?? string.Empty;
        }
    }
}