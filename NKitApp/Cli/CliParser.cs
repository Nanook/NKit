using System;
using System.Collections.Generic;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Pure argv → <see cref="ParsedCommand"/> parser. No console, no engine, deterministic — so it
    /// is fully unit-testable. Understands: leading verb (or alias), --long / -short options with
    /// value by space or '=', presence switches, the '--' end-of-options marker, positional inputs,
    /// and a system value-prefix on prefixable options (e.g. --format wii:rvz:19).
    /// <para>
    /// Path/mask values are carried through VERBATIM (including '//' archive masks and * ? wildcards)
    /// — the parser never reinterprets them; downstream <c>FileMask</c> owns that.
    /// </para>
    /// </summary>
    public static class CliParser
    {
        public static ParsedCommand Parse(string[] args)
        {
            List<ScopedValue> values = new List<ScopedValue>();
            List<string> inputs = new List<string>();
            bool showHelp = false;
            bool showVersion = false;
            string helpVerb = null;
            bool hasExplicitInput = false; // set to true only when --input/-in is used

            if (args == null || args.Length == 0)
                return new ParsedCommand(null, values, inputs, false, null, false);

            int i = 0;
            CliVerb verb = null;

            // args[0]: a verb/alias, a bare 'help'/'version', or (drag-drop) an input.
            string first = args[0];
            if (!isOption(first))
            {
                if (first.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    showHelp = true;
                    i = 1;
                    if (i < args.Length && !isOption(args[i]))
                    {
                        CliVerb hv = OptionModel.FindVerb(args[i]);
                        if (hv != null) { helpVerb = hv.Name; i++; }
                    }
                    return new ParsedCommand(null, values, inputs, showHelp, helpVerb, false, hasExplicitInput);
                }
                if (first.Equals("version", StringComparison.OrdinalIgnoreCase))
                    return new ParsedCommand(null, values, inputs, false, null, true, hasExplicitInput);

                CliVerb v = OptionModel.FindVerb(first);
                if (v != null) { verb = v; i = 1; }
                // else: not a verb → treat as input (drag-drop); handled in the loop below (i stays 0)
            }

            bool inMode = false; // POSIX '--'
            while (i < args.Length)
            {
                string tok = args[i];

                if (!inMode && tok == "--")
                {
                    inMode = true;
                    i++;
                    continue;
                }

                if (!inMode && isOption(tok))
                {
                    parseOption(args, ref i, verb, values, inputs, ref showHelp, ref showVersion, ref helpVerb, ref hasExplicitInput);
                    continue;
                }

                // A bare 'help' word after a verb (git-style 'nkit convert help') shows verb help.
                if (!inMode && tok.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    showHelp = true;
                    if (verb != null) helpVerb = verb.Name;
                    i++;
                    continue;
                }

                // positional input
                inputs.Add(tok);
                i++;
            }

            return new ParsedCommand(verb, values, inputs, showHelp, helpVerb, showVersion, hasExplicitInput);
        }

        private static void parseOption(string[] args, ref int i, CliVerb verb, List<ScopedValue> values,
            List<string> inputs, ref bool showHelp, ref bool showVersion, ref string helpVerb, ref bool hasExplicitInput)
        {
            string tok = args[i];
            string raw = tok.TrimStart('-');
            string name;
            string inlineValue = null;

            int eq = raw.IndexOf('=');
            if (eq >= 0)
            {
                name = raw.Substring(0, eq);
                inlineValue = raw.Substring(eq + 1);
            }
            else
            {
                name = raw;
            }

            // Optional system FLAG-prefix on prefixable options: "--wii:format rvz:19" → system=wii,
            // option=format. This mirrors the config YAML's `sys: wii: format:` structure. Only split
            // when the leading token is a KNOWN system AND it resolves to a prefixable option, so a
            // literal option name containing ':' (none today) or a non-system prefix is left intact.
            string flagSystem = null;
            int nameColon = name.IndexOf(':');
            if (nameColon > 0)
            {
                string lead = name.Substring(0, nameColon);
                string rest = name.Substring(nameColon + 1);
                if (OptionModel.IsSystem(lead))
                {
                    CliOption prefixed = OptionModel.FindOption(rest);
                    if (prefixed != null && prefixed.Prefixable)
                    {
                        flagSystem = lead;
                        name = rest;
                    }
                }
            }

            // help / version are always recognised.
            if (name.Equals("help", StringComparison.OrdinalIgnoreCase) || name == "h" || name == "?")
            {
                showHelp = true;
                if (verb != null) helpVerb = verb.Name;
                i++;
                return;
            }
            if (name.Equals("version", StringComparison.OrdinalIgnoreCase) || name.Equals("ver", StringComparison.OrdinalIgnoreCase))
            {
                showVersion = true;
                i++;
                return;
            }

            CliOption opt = OptionModel.FindOption(name);
            if (opt == null)
                throw new CliException($"Unknown option '{tok}'.", verb?.Name);

            if (opt.IsSwitch)
            {
                if (inlineValue != null)
                    throw new CliException($"Option '--{opt.Canonical}' is a switch and does not take a value.", verb?.Name);
                values.Add(new ScopedValue(opt, null, null));
                i++;
                return;
            }

            // value option: inline (=value) or next token
            string value;
            if (inlineValue != null)
            {
                value = inlineValue;
                i++;
            }
            else
            {
                if (i + 1 >= args.Length || isOption(args[i + 1]))
                    throw new CliException($"Option '--{opt.Canonical}' requires a value.", verb?.Name);
                value = args[i + 1];
                i += 2;
            }

            // The 'input' option feeds the same input list as positionals, but marks the input
            // as explicitly supplied (not a drag-drop positional) so CliFrontEnd can skip interactive.
            if (string.Equals(opt.Canonical, "input", StringComparison.OrdinalIgnoreCase))
            {
                inputs.Add(value);
                hasExplicitInput = true;
                return;
            }

            // System scope comes from the FLAG prefix (--wii:format), captured above. The value is
            // carried through verbatim (so "rvz:19" is never mistaken for a scope).
            if (flagSystem != null && !opt.Prefixable)
                throw new CliException($"Option '--{opt.Canonical}' cannot be system-scoped.", verb?.Name);

            values.Add(new ScopedValue(opt, flagSystem, value));
        }

        // A token is an option if it starts with '-' and is not just "-", and is not a negative-ish
        // bare value. NKit treats a leading-dash token containing a '.' as a value (filename), which
        // we preserve so leading-dash filenames still parse as inputs.
        private static bool isOption(string tok)
        {
            if (string.IsNullOrEmpty(tok) || tok[0] != '-' || tok == "-")
                return false;
            // A dashed token that looks like a filename (contains '.') and is not a known option is
            // an input; but if it resolves to an option name we treat it as an option. Keep it simple:
            // dashed + contains '.' + not a known option ⇒ value.
            string raw = tok.TrimStart('-');
            int eq = raw.IndexOf('=');
            string name = eq >= 0 ? raw.Substring(0, eq) : raw;
            if (name.IndexOf('.') >= 0 && OptionModel.FindOption(name) == null
                && !name.Equals("help", StringComparison.OrdinalIgnoreCase) && name != "h" && name != "?")
                return false;
            return true;
        }
    }
}