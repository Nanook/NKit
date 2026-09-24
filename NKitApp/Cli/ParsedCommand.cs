using System;
using System.Collections.Generic;

namespace Nanook.NKit.App.Cli
{
    /// <summary>An option value with an optional system scope (e.g. format "wii" "rvz:19").</summary>
    public sealed class ScopedValue
    {
        public ScopedValue(CliOption option, string system, string value)
        {
            Option = option;
            System = system;   // null = applies to all systems (root/unscoped)
            Value = value;     // null for a switch that is simply present
        }

        public CliOption Option { get; }
        public string System { get; }
        public string Value { get; }
    }

    /// <summary>
    /// Immutable result of parsing argv. Pure data — no console, no engine. A null <see cref="Verb"/>
    /// means no task was resolved (a candidate for interactive mode).
    /// </summary>
    public sealed class ParsedCommand
    {
        public ParsedCommand(CliVerb verb, IReadOnlyList<ScopedValue> values, IReadOnlyList<string> inputs,
            bool showHelp, string helpVerb, bool showVersion, bool hasExplicitInput = false)
        {
            Verb = verb;
            Values = values ?? Array.Empty<ScopedValue>();
            Inputs = inputs ?? Array.Empty<string>();
            ShowHelp = showHelp;
            HelpVerb = helpVerb;
            ShowVersion = showVersion;
            HasExplicitInput = hasExplicitInput;
        }

        public CliVerb Verb { get; }
        public IReadOnlyList<ScopedValue> Values { get; }
        public IReadOnlyList<string> Inputs { get; }
        public bool ShowHelp { get; }
        public string HelpVerb { get; }
        public bool ShowVersion { get; }

        /// <summary>
        /// True when at least one input was supplied via --input / -in on the CLI.
        /// False when all inputs are positional (drag-and-drop or bare path arguments).
        /// CliFrontEnd uses this to decide whether to enter interactive mode: positional-only
        /// inputs always go interactive (the dropped path is pre-seeded); explicit --input
        /// bypasses the interactive builder and runs directly.
        /// </summary>
        public bool HasExplicitInput { get; }

        /// <summary>The first value for a canonical option name (unscoped preferred), or null.</summary>
        public string Get(string canonical)
        {
            ScopedValue best = null;
            foreach (ScopedValue v in Values)
            {
                if (!string.Equals(v.Option.Canonical, canonical, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (v.System == null) return v.Value; // unscoped wins
                best ??= v;
            }
            return best?.Value;
        }

        public bool Has(string canonical) => Get(canonical) != null || HasSwitch(canonical);

        public bool HasSwitch(string canonical)
        {
            foreach (ScopedValue v in Values)
                if (v.Option.IsSwitch && string.Equals(v.Option.Canonical, canonical, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}