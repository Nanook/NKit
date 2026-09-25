using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Maps the new canonical (CLI long-form) config keys to the legacy NKit param names, so a
    /// config file (or override) written with new names resolves to the existing params. The legacy
    /// names remain the primary keys; this only adds forward-compatibility for the new spelling.
    /// <para>
    /// Static data — AOT-safe. Legacy names are what the <c>Param</c> enum already uses, so only the
    /// NEW → legacy direction is needed here.
    /// </para>
    /// </summary>
    internal static class ConfigKeyAliases
    {
        // canonical (new) → legacy param name
        private static readonly Dictionary<string, string> _toLegacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "input", "in" },
            { "output", "out" },
            { "recursive", "r" },
            // archives: new configs may say "archives: y"; legacy key is "arc"
            { "archives", "arc" },
            { "verify", "v" },
            { "temp", "tmp" },
            { "results-out", "resultsOut" },
            { "delete-processed", "deleteProcessed" },
            { "skip-if-completed", "skipIfCompleted" },
            { "dat-match", "outAsDatMatch" },
            { "console-level", "consoleLevel" },
            { "log-level", "logOutLevel" },
            { "log", "logOut" },
            { "format", "convert" },
            { "mask", "extract" },
            { "fix-info", "fixInfo" },
            { "fix-files", "fixFiles" },
            { "base-in", "baseInPath" },
            { "scan-out", "scanOut" },
            { "scan-format", "scanFormat" },
            { "scan-in", "scanIn" },
            { "config", "cfg" },
        };

        // legacy param name → canonical (new) name, built as the reverse of _toLegacy.
        private static readonly Dictionary<string, string> _toCanonical = buildReverse();

        private static Dictionary<string, string> buildReverse()
        {
            Dictionary<string, string> r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in _toLegacy)
                if (!r.ContainsKey(kv.Value)) r.Add(kv.Value, kv.Key);
            return r;
        }

        /// <summary>The legacy key for a new canonical name, or null if it is not an aliased new name.</summary>
        public static string ToLegacy(string canonical)
            => canonical != null && _toLegacy.TryGetValue(canonical, out string legacy) ? legacy : null;

        /// <summary>The new canonical key for a legacy param name, or null.</summary>
        public static string ToCanonical(string legacy)
            => legacy != null && _toCanonical.TryGetValue(legacy, out string canonical) ? canonical : null;

        /// <summary>All canonical (new) key names, for extending the config's known-param validation.</summary>
        public static IEnumerable<string> CanonicalNames => _toLegacy.Keys;

        /// <summary>The canonical name for a legacy name if it is one of the root-only params, else null.</summary>
        public static string CanonicalForRootOnly(string legacy) => ToCanonical(legacy);
    }
}