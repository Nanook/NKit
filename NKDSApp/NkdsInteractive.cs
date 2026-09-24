using Nanook.NKit.Interactive;
using System;
using System.Collections.Generic;

namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Additive interactive front-end for nkds. Presents a scrolling command dropdown, then prompts
    /// the inputs relevant to the chosen command, and assembles an <c>nkds</c> argv — the SAME argv
    /// the existing <see cref="NkdsCommandLine.Parse"/> consumes, so the current CLI is untouched and
    /// there is one execution path. All prompting goes through <see cref="IPrompter"/> so it is
    /// unit-testable with a scripted prompter.
    /// </summary>
    internal sealed class NkdsInteractive
    {
        private readonly IPrompter _p;
        private string _seededDatastore; // set for the duration of Build(); drives datastore() default

        // Sentinels shown as the final entries in the command list (same wording as the nkit builder).
        private const string HelpChoice = "help  (show command help)";
        private const string QuitChoice = "quit (ctrl+c)";

        public NkdsInteractive(IPrompter prompter)
        {
            _p = prompter;
        }

        // Commands offered in the dropdown (canonical name → short description). Order = display order.
        private static readonly (string name, string desc)[] _commands =
        {
            ("add",     "Import images through the NKit pipeline"),
            ("export",  "Export images from a set (convert/expand)"),
            ("verify",  "Verify images through the NKit pipeline"),
            ("list",    "List images across sets"),
            ("stats",   "Show statistics for a set or all sets"),
            ("sets",    "List sets in a datastore"),
            ("create",  "Create a new set in a datastore"),
            ("mount",   "Mount a datastore or set as a virtual filesystem"),
            ("remove",  "Mark images as removed"),
            ("restore", "Restore images previously removed"),
            ("compact", "Permanently delete marked images and unused blocks"),
            ("rollback","Rollback a set to a specific image"),
            ("ogmr",    "Batch import routed by an OGMR YAML"),
        };

        /// <summary>Assemble an nkds argv interactively. Returns null if the user cancels.</summary>
        /// <param name="seededDatastore">
        /// An optional datastore path pre-supplied by the caller (e.g. from a drag-drop positional
        /// argument). When set, the datastore prompt is skipped and the value is used directly.
        /// </param>
        public string[] Build(string seededDatastore = null)
        {
            _seededDatastore = seededDatastore;

            // Header banner (green app name, yellow version) matching the nkit interactive builder.
            _p.InfoMarkup($"[green]nkds[/] [yellow]v{Spectre.Console.Markup.Escape(Nanook.NKit.AppSettings.GetVersion())}[/] - NKit DataStore filesystem and inspection tool\n");
            _p.Info("Press Ctrl+C at any time to exit.");
            _p.Info("");

            // 'help' and 'quit' are offered as the final entries, matching the nkit builder:
            // help shows the general nkds help and returns to the command list; quit exits.
            string cmd;
            while (true)
            {
                List<string> choices = new List<string>();
                foreach ((string name, string desc) in _commands)
                    choices.Add($"{name.PadRight(9)} {desc}");
                choices.Add(HelpChoice);
                choices.Add(QuitChoice);

                string picked = _p.Select("Select nkds command", choices);
                if (picked == null || picked == QuitChoice)
                    return null; // user quit
                if (picked == HelpChoice)
                {
                    NkdsCommandLine.WriteHelp(); // general nkds help
                    continue;                    // back to the command list
                }
                cmd = picked.Split(' ')[0];
                break;
            }
            _p.InfoPair("Command", cmd); // echo the chosen command like nkit echoes Task

            List<string> argv = new List<string> { cmd };

            switch (cmd)
            {
                case "add": buildAdd(argv); break;
                case "export": buildExport(argv); break;
                case "verify": buildVerify(argv); break;
                case "list": buildList(argv); break;
                case "stats": buildStats(argv); break;
                case "sets": buildSets(argv); break;
                case "create": buildCreate(argv); break;
                case "mount": buildMount(argv); break;
                case "remove":
                case "restore": buildIdsCommand(argv); break;
                case "compact": buildDatastoreOnly(argv); break;
                case "rollback": buildRollback(argv); break;
                case "ogmr": buildOgmr(argv); break;
            }

            _p.Info("Command:");
            _p.Info("  nkds " + string.Join(" ", quote(argv)));
            if (!_p.Confirm("Run this now?", true))
                return null;
            return argv.ToArray();
        }

        // ── per-command prompt sets ──────────────────────────────────────────────

        private void buildAdd(List<string> a)
        {
            datastore(a, "DataStore directory or .nkds set path");
            input(a, "Input file, folder or mask");
            if (_p.Confirm("Scan input folders recursively?", false)) a.Add("--recursive");
            if (_p.Confirm("Skip scanning inside archives?", false)) a.Add("--no-archives");
            optional(a, "--config", "NKit config file (optional)");
        }

        private void buildExport(List<string> a)
        {
            datastore(a, "DataStore .nkds set path");
            mask(a);
            required(a, "--output", "Output folder");
            // Context help for the convert format (same values NKit convert accepts). Blank keeps
            // the full stored format via the expand path.
            _p.ShowHelp("format  (blank = full stored format)",
                "The NKit convert format for the exported images. Leave blank to export the full stored format.",
                _formatExamples);
            optional(a, "--format", "Convert format (blank = full stored format)");
            optional(a, "--config", "NKit config file (optional)");
        }

        // A curated cross-system set of convert-format examples for the export help panel. (NKit's
        // interactive mode has the full per-system set; unifying both on one shared option model is
        // a natural follow-up once that model is relocated to the shared library.)
        private static readonly IReadOnlyList<(string value, string note)> _formatExamples = new[]
        {
            ("rvz:zstd:19:128k:16", "Wii/GC — best compression"),
            ("wbfs:y",              "Wii/GC — lossless WBFS"),
            ("wux",                 "WiiU"),
            ("deciso",              "PS3 decrypted ISO"),
            ("cso:9:16k:4/cue:split","PS2 — iso + cue"),
            ("cue",                 "redump cue/bin (PS1/Saturn/...)"),
        };

        private void buildVerify(List<string> a)
        {
            datastore(a, "DataStore .nkds set path");
            mask(a);
            optional(a, "--config", "NKit config file (optional)");
        }

        private void buildList(List<string> a)
        {
            datastore(a, "DataStore directory or .nkds set path");
            optional(a, "--system", "Filter by system (optional)");
            optional(a, "--search", "Filter by image name text (optional)");
            format(a, new[] { "text", "json" });
            if (_p.Confirm("Show removed images?", false)) a.Add("--removed");
        }

        private void buildStats(List<string> a)
        {
            datastore(a, "DataStore directory or .nkds set path");
            if (_p.Confirm("Include per-image details?", false)) a.Add("--details");
            format(a, new[] { "text", "json", "yaml" });
        }

        private void buildSets(List<string> a)
        {
            datastore(a, "DataStore directory");
            format(a, new[] { "text", "json" });
        }

        private void buildCreate(List<string> a)
        {
            datastore(a, "DataStore directory or .nkds set path");
            optional(a, null, "Set name (or leave blank to use the .nkds path)", positional: true);
            optional(a, "--shard-size", "Shard size (default 50GiB; 0 = single DB)");
            optional(a, "--block-size", "Block size (2KiB..2MiB, default 64KiB)");
        }

        private void buildMount(List<string> a)
        {
            datastore(a, "DataStore directory or .nkds set path");
            required(a, "--mount", "Mount point");
            IReadOnlyList<string> views = _p.MultiSelect(
                "Views to show (none = images + filesystem folders)",
                new[] { "images (-i)", "filesystem (-fs)", "system (-s)" });
            foreach (string v in views)
            {
                if (v.StartsWith("images")) a.Add("--image");
                else if (v.StartsWith("filesystem")) a.Add("--filesystem");
                else if (v.StartsWith("system")) a.Add("--system");
            }
            if (_p.Confirm("Enable update (rename) mode?", false)) a.Add("--update");
        }

        private void buildIdsCommand(List<string> a)
        {
            datastore(a, "DataStore .nkds set path");
            string ids = _p.Text("Image IDs (space separated)", null, allowEmpty: true);
            if (!string.IsNullOrWhiteSpace(ids))
                foreach (string id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    a.Add(id);
        }

        private void buildDatastoreOnly(List<string> a)
            => datastore(a, "DataStore .nkds set path");

        private void buildRollback(List<string> a)
        {
            datastore(a, "DataStore .nkds set path");
            optional(a, null, "Image ID (blank to list)", positional: true);
        }

        private void buildOgmr(List<string> a)
        {
            required(a, null, "OGMR YAML file", positional: true);
            datastore(a, "DataStore directory");
            input(a, "Input file, folder or mask");
            if (_p.Confirm("Scan input folders recursively?", false)) a.Add("--recursive");
            optional(a, "--shard-size", "Shard size for new sets (default 50GiB)");
            optional(a, "--block-size", "Block size for new sets (default 64KiB)");
            optional(a, "--config", "NKit config file (optional)");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private void datastore(List<string> a, string title)
        {
            // If a path was pre-seeded (drag-drop), show it as the default and skip prompting.
            string defaultVal = _seededDatastore;
            if (!string.IsNullOrWhiteSpace(defaultVal))
            {
                a.Add("--datastore"); a.Add(defaultVal);
                _p.InfoPair("DataStore", defaultVal); // seeded — no prompt line to collapse
                return;
            }
            string v = _p.Text(title, null, allowEmpty: false);
            if (!string.IsNullOrWhiteSpace(v))
            {
                a.Add("--datastore"); a.Add(v);
                _p.ReplaceLastLinePair("DataStore", v);
            }
        }

        private void input(List<string> a, string title)
        {
            string v = _p.Text(title, null, allowEmpty: false);
            if (!string.IsNullOrWhiteSpace(v))
            {
                a.Add(v); // positional input
                _p.ReplaceLastLinePair("Input", v);
            }
        }

        private void mask(List<string> a)
        {
            string v = _p.Text("Image mask within the set (e.g. *.iso)", "*.iso", allowEmpty: false);
            if (!string.IsNullOrWhiteSpace(v))
            {
                a.Add("--mask"); a.Add(v);
                _p.ReplaceLastLinePair("Mask", v);
            }
        }

        private void required(List<string> a, string opt, string title, bool positional = false)
        {
            string v = _p.Text(title, null, allowEmpty: false);
            if (string.IsNullOrWhiteSpace(v)) return;
            if (positional) a.Add(v);
            else { a.Add(opt); a.Add(v); }
            _p.ReplaceLastLinePair(echoLabel(opt, title), v);
        }

        private void optional(List<string> a, string opt, string title, bool positional = false)
        {
            string v = _p.Text(title, null, allowEmpty: true);
            if (string.IsNullOrWhiteSpace(v)) return;
            if (positional) a.Add(v);
            else { a.Add(opt); a.Add(v); }
            _p.ReplaceLastLinePair(echoLabel(opt, title), v);
        }

        private void format(List<string> a, string[] values)
        {
            List<string> choices = new List<string> { "text" };
            foreach (string v in values) if (v != "text") choices.Add(v);
            string chosen = _p.Select("Output format", choices, "text");
            _p.InfoPair("Format", chosen ?? "text");
            if (!string.IsNullOrWhiteSpace(chosen) && chosen != "text") { a.Add("--format"); a.Add(chosen); }
        }

        // Derive a short echo label from the option name ("--shard-size" → "Shard-size") or, for a
        // positional value, from the leading word of the prompt title.
        private static string echoLabel(string opt, string title)
        {
            if (!string.IsNullOrEmpty(opt))
            {
                string s = opt.TrimStart('-');
                return s.Length == 0 ? title : char.ToUpperInvariant(s[0]) + s.Substring(1);
            }
            // Positional: first word of the title (e.g. "OGMR YAML file" → "OGMR").
            int sp = title.IndexOf(' ');
            return sp > 0 ? title.Substring(0, sp) : title;
        }

        private static IEnumerable<string> quote(IEnumerable<string> tokens)
        {
            foreach (string t in tokens)
                yield return t.IndexOf(' ') >= 0 ? "\"" + t + "\"" : t;
        }
    }
}