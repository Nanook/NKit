using System.Text;
using Spectre.Console;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Renders general and per-verb help from the <see cref="OptionModel"/> (git-style). Returns
    /// Spectre.Console MARKUP strings (print with <c>AnsiConsole.MarkupLine</c>): the app name is
    /// green, the version yellow, section titles cyan, option long names white and their shorthand
    /// switches yellow. All dynamic/user text is escaped so it never breaks the markup.
    /// </summary>
    public static class HelpRenderer
    {
        // Scheme (matches the log/progress/interactive colours: cyan + yellow, green app name).
        private const string AppColour = "green";
        private const string VersionColour = "yellow";
        private const string TitleColour = "cyan";
        private const string OptionColour = "bold white";
        private const string ShortColour = "yellow";

        private static string E(string s) => Markup.Escape(s ?? "");
        private static string Title(string t) => $"[{TitleColour}]{E(t)}[/]";
        private static string AppName(string name) => $"[{AppColour}]{E(name)}[/]";

        public static string General(string version)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{AppName("NKit")} [{VersionColour}]v{E(version)}[/] - disc image conversion, verification, extraction and dedupe");
            sb.AppendLine();
            sb.AppendLine(Title("Usage:"));
            sb.AppendLine($"  {AppName("nkit")} <task> [[options]] <input ...>");
            sb.AppendLine($"  {AppName("nkit")} <task> help");
            sb.AppendLine($"  {AppName("nkit")} help [[task]]");
            sb.AppendLine($"  {AppName("nkit")}                       (interactive builder)");
            sb.AppendLine($"  {AppName("nkit")} <file>                (interactive, seeded with the input)");
            sb.AppendLine($"  {AppName("nkit")} --help | --version");
            sb.AppendLine();
            sb.AppendLine(Title("Tasks:"));
            foreach (CliVerb v in OptionModel.Verbs)
            {
                string alias = v.Aliases.Length != 0 ? $" ({string.Join(", ", v.Aliases)})" : "";
                // The task name is an "option-like" token → white; keep help text plain.
                string nameCol = $"[{OptionColour}]{E(v.Name + alias)}[/]";
                sb.AppendLine($"  {pad(nameCol, (v.Name + alias).Length, 20)} {E(v.Help)}");
            }
            sb.AppendLine();
            sb.AppendLine(Title("Global options:"));
            appendOptions(sb, rootOnly: true, verb: null);
            sb.AppendLine();
            sb.AppendLine(E("Per-system options may be scoped by prefixing the system, e.g.  --wii:format rvz:19  --gamecube:format wbfs:y"));
            sb.AppendLine($"Run '{AppName("nkit")} <task> help' for task-specific options.");
            return sb.ToString();
        }

        public static string Verb(string verbName, string version)
        {
            CliVerb v = OptionModel.FindVerb(verbName);
            if (v == null)
                return General(version);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{AppName("nkit")} [{OptionColour}]{E(v.Name)}[/] - {E(v.Help)}");
            sb.AppendLine();
            sb.AppendLine(Title("Usage:"));
            sb.AppendLine($"  {AppName("nkit")} [{OptionColour}]{E(v.Name)}[/] [[options]] <input ...>");
            sb.AppendLine();
            sb.AppendLine(Title("Options:"));
            appendOptions(sb, rootOnly: false, verb: v.Name);

            // Examples for this verb's rich options (all systems), sharing the interactive content.
            foreach (CliOption o in OptionModel.Options)
            {
                if (!o.AvailableFor(v.Name) || o.Examples.Length == 0) continue;
                sb.AppendLine();
                sb.AppendLine(Title($"--{o.Canonical} examples:"));
                if (!string.IsNullOrEmpty(o.LongHelp))
                {
                    // LongHelp may be multi-line (e.g. a per-format table); indent every line so the
                    // block stays aligned under the option heading.
                    foreach (string line in o.LongHelp.Replace("\r\n", "\n").Split('\n'))
                        sb.AppendLine(line.Length == 0 ? "" : "  " + E(line));
                }
                int pad = 0;
                foreach (OptionExample e in o.Examples) if (e.Value.Length > pad) pad = e.Value.Length;
                foreach (OptionExample e in o.Examples)
                {
                    string val = e.Value.Length == 0 ? "(blank)" : e.Value;
                    string sysTag = e.Systems.Length != 0 ? " [" + string.Join(",", e.Systems) + "]" : "";
                    sb.AppendLine($"  {padPlain(val, pad)}  {E(e.Note + sysTag)}");
                }
            }
            return sb.ToString();
        }

        private static void appendOptions(StringBuilder sb, bool rootOnly, string verb)
        {
            foreach (CliOption o in OptionModel.Options)
            {
                if (o.Deprecated) continue; // retired: parsed for back-compat but not advertised
                if (rootOnly && !o.Root) continue;
                if (!rootOnly)
                {
                    // per-verb view: show this verb's options plus the globals
                    if (!o.AvailableFor(verb)) continue;
                }

                // Long option name in white; the shorthand switch (", -x") in yellow. The value hint
                // (e.g. "<path>", "<none|info>") stays uncoloured.
                string names = $"[{OptionColour}]--{E(o.Canonical)}[/]";
                int plainLen = ("--" + o.Canonical).Length;
                if (!string.IsNullOrEmpty(o.Short))
                {
                    names += $", [{ShortColour}]-{E(o.Short)}[/]";
                    plainLen += (", -" + o.Short).Length;
                }

                string valueHint = valueHintFor(o);
                string left = names + E(valueHint);
                plainLen += valueHint.Length;

                string scope = o.Prefixable ? " [sys-scopable]" : "";
                sb.AppendLine($"  {pad(left, plainLen, 30)} {E(o.Help + scope)}");
            }
        }

        // Right-pad a MARKUP string to a visible column width, given the visible (unmarked) length.
        private static string pad(string markup, int visibleLen, int width)
            => visibleLen < width ? markup + new string(' ', width - visibleLen) : markup;

        // Right-pad plain text to a column width (no markup involved).
        private static string padPlain(string text, int width)
            => Markup.Escape(text.PadRight(width));

        private static string valueHintFor(CliOption o)
        {
            switch (o.Kind)
            {
                case OptionKind.Switch: return "";
                case OptionKind.Enum:
                case OptionKind.Level: return " <" + string.Join("|", o.EnumValues) + ">";
                case OptionKind.Path: return " <path>";
                case OptionKind.Mask: return " <mask>";
                case OptionKind.Format: return " <format>";
                default: return " <value>";
            }
        }
    }
}
