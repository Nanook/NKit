using Nanook.NKit.Configuration;
using Nanook.NKit.Interactive;
using System;
using System.Collections.Generic;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Guided command builder. Flow: task → input images → output dir (unless the task has none) →
    /// an editable options TABLE the user navigates and edits until Done. Drives an
    /// <see cref="IPrompter"/> from the <see cref="OptionModel"/> and assembles an argv — the SAME
    /// shape <see cref="CliParser"/> consumes — so interactive and direct modes share one path.
    /// No console access here (all via the prompter), so it is unit-testable.
    /// </summary>
    public sealed class InteractiveBuilder
    {
        private readonly IPrompter _p;
        private readonly ConfigDefaults _config;

        public InteractiveBuilder(IPrompter prompter, ConfigDefaults config = null)
        {
            _p = prompter;
            _config = config;
        }

        // Editable value store for one option (canonical). Holds either a single value (or "on"/null
        // for a switch) plus optional per-system overrides. Original holds the config-seeded value so
        // write-back can detect what the user actually changed.
        private sealed class OptState
        {
            public CliOption Option;
            public bool SwitchOn;                 // switches
            public string Value;                  // unscoped value (null = unset)
            public string Original;               // config-seeded value (for change detection)
            public readonly List<(string sys, string val)> Scoped = new List<(string, string)>();

            public bool Changed => !string.Equals(Value ?? "", Original ?? "", StringComparison.Ordinal);

            public string Display()
            {
                if (Option.IsSwitch) return SwitchOn ? "yes" : "no";
                if (Scoped.Count != 0)
                {
                    List<string> parts = new List<string>();
                    if (!string.IsNullOrEmpty(Value)) parts.Add(Value);
                    foreach ((string s, string v) in Scoped) parts.Add($"{s}:{v}");
                    return string.Join("  ", parts);
                }
                // Blank means "fall back to the config" — for a prefixable option that is resolved
                // per detected system, so show that rather than a bare "(not set)".
                if (string.IsNullOrEmpty(Value))
                    return Option.Prefixable ? "(from config)" : "(not set)";
                return Value;
            }
        }

        /// <summary>
        /// Build an argv. <paramref name="seed"/> may carry inputs (drag-drop) and a default verb.
        /// Returns null if the user cancels or declines to run.
        /// </summary>
        // Sentinels shown as the final entries in the task list.
        private const string HelpChoice = "help  (show command help)";
        private const string TaskQuitChoice = "quit (ctrl+c)";
        // Action rows at the bottom of the options table.
        private const string RunChoice = ">> Run";
        private const string QuitChoice = ">> Quit";

        public string[] Build(ParsedCommand seed, string defaultVerb)
        {
            _p.InfoMarkup($"[green]NKit[/] [yellow]v{Spectre.Console.Markup.Escape(AppSettings.GetVersion())}[/] - disc image conversion, verification, extraction and dedupe\n");
            _p.Info("Press Ctrl+C at any time to exit.");
            _p.Info("");

            // 1. task (capability-filtered). 'help' is offered as the last entry; choosing it shows
            //    the general help and returns to the task prompt.
            string chosenVerb;
            if (seed?.Verb?.Name != null)
            {
                chosenVerb = seed.Verb.Name;
            }
            else
            {
                while (true)
                {
                    List<string> verbNames = new List<string>();
                    foreach (CliVerb v in OptionModel.Verbs) verbNames.Add(v.Name);
                    verbNames.Add(HelpChoice);
                    verbNames.Add(TaskQuitChoice);

                    chosenVerb = _p.Select("Select task", verbNames, defaultVerb);
                    if (chosenVerb == HelpChoice)
                    {
                        _p.InfoMarkup(HelpRenderer.General(AppSettings.GetVersion()));
                        continue; // back to the task list
                    }
                    if (chosenVerb == TaskQuitChoice || chosenVerb == null)
                        return null; // user quit — same as declining to run
                    break;
                }
            }
            _p.InfoPair("Task", chosenVerb);
            CliVerb verb = OptionModel.FindVerb(chosenVerb);

            // 2. input images (seed from drag-drop, else prompt)
            List<string> inputs = new List<string>();
            if (seed != null && seed.Inputs.Count != 0)
                inputs.AddRange(seed.Inputs);
            else
            {
                string inp = _p.Text("Input file, folder or mask", null, allowEmpty: false);
                if (!string.IsNullOrWhiteSpace(inp)) inputs.Add(inp);
                // Collapse the prompt line into a clean echo.
                if (inputs.Count != 0)
                    _p.ReplaceLastLinePair("Input", string.Join(", ", inputs));
            }
            if (inputs.Count != 0 && seed != null && seed.Inputs.Count != 0)
                _p.InfoPair("Input", string.Join(", ", inputs)); // seeded (no prompt to collapse)

            // 3. output directory — only for tasks that write one
            string outputDir = null;
            if (verb != null && verb.HasOutputDir)
            {
                outputDir = _p.Text("Output directory", null, allowEmpty: true);
                _p.ReplaceLastLinePair("Output", string.IsNullOrWhiteSpace(outputDir) ? "(default)" : outputDir);
            }

            // 4. build the editable option table (everything relevant except input/output/config,
            //    which are handled above). System is included as a table row. Rows are seeded with
            //    the RAW config value (root-level to start; per-system re-seeds when a system is set).
            List<OptState> table = buildTable(chosenVerb);

            if (!editTable(chosenVerb, table))
                return null; // cancelled

            // 4b. offer to save changed values back to the config (comment-preserving).
            offerWriteBack(table);

            // 5. assemble argv. The RUN argv carries every resolved value; the DISPLAYED command
            // shows only mandatory items + what the user specifically set (config still applies).
            string[] argv = assemble(chosenVerb, inputs, outputDir, table);
            string[] shown = assemble(chosenVerb, inputs, outputDir, table, onlyUserSet: true);

            _p.Info("");
            _p.Info("Equivalent command line:");
            _p.Info("  nkit " + string.Join(" ", quote(shown)));
            _p.Info("");
            if (!_p.Confirm("Run this now?", true))
                return null;
            return argv;
        }

        private List<OptState> buildTable(string verb)
        {
            List<OptState> table = new List<OptState>();
            foreach (CliOption o in OptionModel.Options)
            {
                if (!o.AvailableFor(verb)) continue;
                if (o.Deprecated) continue; // retired knobs are still parsed but never offered here
                if (o.Canonical is "input" or "output" or "config" or "interactive") continue;

                // Seed with the root-level config value (system-specific re-seed happens when the
                // user chooses a system). System row itself is never seeded from a value.
                string seed = null;
                if (_config != null && !string.Equals(o.Canonical, "system", StringComparison.OrdinalIgnoreCase))
                    seed = _config.ValueFor(o, null);

                OptState st = new OptState { Option = o };
                if (!o.IsSwitch && !string.IsNullOrEmpty(seed)) { st.Value = seed; st.Original = seed; }
                table.Add(st);
            }
            return table;
        }

        // Re-seed the prefixable rows from the chosen system's config values (raw), unless the user
        // already changed them. Called when the system row is set.
        private void reseedForSystem(List<OptState> table, string system)
        {
            if (_config == null || string.IsNullOrEmpty(system)) return;
            foreach (OptState st in table)
            {
                if (st.Option.IsSwitch || !st.Option.Prefixable) continue;
                if (st.Changed) continue; // respect the user's edit
                string v = _config.ValueFor(st.Option, system);
                st.Value = string.IsNullOrEmpty(v) ? null : v;
                st.Original = st.Value;
            }
        }

        // The navigable table: list "label  value" rows + Done/Cancel; edit the selected row; repeat.
        // When a single system is chosen, rows for options that don't apply to that system are hidden
        // (e.g. keys for non-key systems, fix-info/fix-files for systems the fix task can't process).
        // With no system chosen (auto-detect) every applicable-to-the-verb row is shown, because the
        // inputs may span multiple systems and the config resolves each one at run time.
        private bool editTable(string verb, List<OptState> table)
        {
            while (true)
            {
                string system = currentSystem(table);

                // Build the visible subset for this pass and keep a parallel index back into `table`
                // so an edited row maps to the correct OptState regardless of what is hidden.
                List<OptState> visible = new List<OptState>();
                foreach (OptState st in table)
                    if (AppliesToSystem(st.Option, system, verb))
                        visible.Add(st);

                List<string> rows = new List<string>();
                foreach (OptState st in visible)
                    rows.Add(rowLabel(st));
                rows.Add(RunChoice);
                rows.Add(QuitChoice);

                string picked = _p.Select($"Edit '{verb}' options  (enter to edit, Run to start)", rows, RunChoice);
                if (picked == null || picked == QuitChoice) return false;
                if (picked == RunChoice) return true;

                int idx = rows.IndexOf(picked);
                if (idx >= 0 && idx < visible.Count)
                {
                    OptState edited = visible[idx];
                    editOption(edited, system, verb);
                    // If the system row was just set, re-seed per-system rows from that system's config.
                    if (string.Equals(edited.Option.Canonical, "system", StringComparison.OrdinalIgnoreCase))
                        reseedForSystem(table, edited.Value);
                }
            }
        }

        // Whether an option is worth showing once a single system is chosen. Returns true for every
        // option while no system is set (auto-detect spans systems). When a system IS set, hide
        // options that can never apply to it:
        //   • keys   — only WiiU/PS3/XBox/XBox360 consume key files.
        //   • fix-*  — only where the fix task has a real processing path for that system.
        // All other options remain visible (their per-system relevance is resolved at run time).
        private static bool AppliesToSystem(CliOption o, string system, string verb)
        {
            if (string.IsNullOrEmpty(system)) return true; // auto-detect → show everything for the verb

            if (!Enum.TryParse(system, ignoreCase: true, out SystemType sys))
                return true; // unknown spelling → don't hide anything

            switch (o.Canonical)
            {
                case "keys":
                    return sys is SystemType.WiiU or SystemType.PS3 or SystemType.XBox or SystemType.XBox360;
                case "fix-info":
                case "fix-files":
                    // Gate to systems the *fix* task can actually process (GameCube/Wii/PS3 at runtime).
                    return TaskCapabilities.IsSupported(TaskType.Fix, sys);
                default:
                    return true;
            }
        }

        // The system value currently chosen in the table (drives context-sensitive help), or null.
        private static string currentSystem(List<OptState> table)
        {
            foreach (OptState st in table)
                if (string.Equals(st.Option.Canonical, "system", StringComparison.OrdinalIgnoreCase))
                    return st.Value;
            return null;
        }

        private static string rowLabel(OptState st)
        {
            string name = st.Option.Canonical.PadRight(20);
            return $"{name} {st.Display()}";
        }

        private void editOption(OptState st, string chosenSystem, string verb)
        {
            CliOption o = st.Option;

            if (o.IsSwitch)
            {
                st.SwitchOn = _p.Confirm(o.Help, st.SwitchOn);
                return;
            }

            // system row (the 'system' option) is a capability-filtered single select
            if (string.Equals(o.Canonical, "system", StringComparison.OrdinalIgnoreCase))
            {
                st.Value = pickSystem(verb);
                return;
            }

            if (o.Kind == OptionKind.Enum || o.Kind == OptionKind.Level)
            {
                List<string> choices = new List<string> { "(not set)" };
                choices.AddRange(o.EnumValues);
                string v = _p.Select(o.Help, choices, st.Value ?? "(not set)");
                st.Value = (v == null || v == "(not set)") ? null : v;
                return;
            }

            // Guided convert-format flow: only when a single system is chosen (so the exact set of
            // formats and their sub-options is knowable). With no system, fall through to the
            // free-text path so a value can span multiple detected systems via the config.
            if (o.Kind == OptionKind.Format && verb == "convert"
                && !string.IsNullOrEmpty(chosenSystem)
                && Enum.TryParse(chosenSystem, ignoreCase: true, out SystemType fmtSys))
            {
                // Show the (system-scoped) help panel first — same reference the free-text path gives.
                ShowOptionHelp(_p, o, chosenSystem);
                string composed = pickConvertFormat(fmtSys);
                if (composed != null) // null = user backed out; leave the current value untouched
                    st.Value = composed;
                return;
            }

            // Context-sensitive help for rich options — examples filtered by the chosen system.
            ShowOptionHelp(_p, o, chosenSystem);

            // Prefixable value → offer all-systems vs per-system editing.
            if (o.Prefixable && _p.Confirm($"Set '{o.Canonical}' per system?", st.Scoped.Count != 0))
            {
                st.Scoped.Clear();
                IReadOnlyList<string> systems = _p.MultiSelect($"Systems for '{o.Canonical}'", OptionModel.Systems);
                foreach (string sys in systems)
                {
                    // Per-system: show that system's examples specifically.
                    ShowOptionHelp(_p, o, sys);
                    string v = _p.Text($"{o.Canonical} for {sys}", null, allowEmpty: true);
                    if (!string.IsNullOrWhiteSpace(v))
                        st.Scoped.Add((sys, v));
                }
                return;
            }

            string val = _p.Text(o.Help, st.Value, allowEmpty: true);
            st.Value = string.IsNullOrWhiteSpace(val) ? null : val;
        }

        /// <summary>Show the option's help panel with examples filtered to the chosen system.</summary>
        internal static void ShowOptionHelp(IPrompter p, CliOption o, string system)
        {
            if (!o.HasRichHelp) return;
            IReadOnlyList<OptionExample> ex = o.ExamplesFor(system);
            List<(string, string)> examples = new List<(string, string)>();
            foreach (OptionExample e in ex) examples.Add((e.Value, e.Note));
            string title = system != null ? $"{o.Canonical}  ({system})" : o.Canonical;
            string desc = o.LongHelp ?? o.Help;
            // System-optional guidance: with no system chosen, leaving this blank lets the config
            // decide per detected system — so all systems' examples are shown as a reference.
            if (system == null && o.Prefixable)
                desc += "  (Leave blank to use the config for each detected system; a value here applies to all.)";
            p.ShowHelp(title, desc, examples);
        }

        // System is OPTIONAL. NKit's normal mode auto-detects each input's system and uses that
        // system's config section (dat/keys/format/...). Choosing a system only NARROWS the run to
        // that one system. The default is therefore always "(all / auto-detect)".
        private string pickSystem(string verb)
        {
            List<string> choices = new List<string> { "(all / auto-detect — use config per system)" };
            IReadOnlyList<SystemType> systems = capabilitySystems(verb);
            foreach (SystemType s in systems) choices.Add(s.ToString());
            string chosen = _p.Select("Narrow to one system? (optional — auto-detect uses the config for every system)",
                choices, choices[0]);
            return (chosen == null || chosen.StartsWith("(all")) ? null : chosen;
        }

        // Systems to offer when narrowing: only those the task supports (so a chosen system can
        // actually run the task). Falls back to all selectable systems if the verb is unknown.
        private static IReadOnlyList<SystemType> capabilitySystems(string verb)
        {
            CliVerb v = OptionModel.FindVerb(verb);
            if (v != null && Enum.TryParse(v.Task, ignoreCase: true, out TaskType task))
            {
                IReadOnlyList<SystemType> supported = TaskCapabilities.SupportedSystems(task);
                if (supported.Count != 0) return supported;
            }
            return TaskCapabilities.SelectableSystems;
        }

        // ── Guided convert-format picker ────────────────────────────────────────────────
        // Mirrors the UI (ConvertSettingsViewModel + ConfigurationMappingService.MapToConvertFormat):
        // a system may have a SINGLE format (iso/rvz/wbfs/ciso/cso/…), an INDEXED format (cue/gdi),
        // or both. When both exist the result is "single/indexed". Sub-options are gated by the same
        // ConfigSettings* predicates the UI uses, so the CLI only ever offers valid combinations.
        // Returns the composed ':'-string (with optional '/'), or null if the user backed out.
        private const string BackChoice = "(back — leave unchanged)";

        private string pickConvertFormat(SystemType system)
        {
            bool hasSingle = ConfigSettingsDefaults.IsSingleFormatSupported(system)
                             && ConfigSettingsRanges.GetSupportedDualSingleFormats(system).Count != 0;
            bool hasIndexed = ConfigSettingsDefaults.IsIndexedFormatSupported(system)
                              && ConfigSettingsRanges.GetSupportedDualIndexFormats(system).Count != 0;

            string single = null;
            string indexed = null;

            if (hasSingle)
            {
                single = pickSingleFormat(system);
                if (single == null) return null; // backed out
            }

            if (hasIndexed)
            {
                // Index-only systems (e.g. Dreamcast) MUST pick an indexed format; when a single
                // format also exists, the indexed part is optional (choose "(none)" to skip it).
                indexed = pickIndexedFormat(system, optional: hasSingle);
                if (indexed == null && !hasSingle) return null; // backed out on an index-only system
            }

            // Compose exactly like MapToConvertFormat: single/indexed, or whichever exists alone.
            if (!string.IsNullOrEmpty(single) && !string.IsNullOrEmpty(indexed))
                return single + "/" + indexed;
            if (!string.IsNullOrEmpty(indexed))
                return indexed;
            return single;
        }

        // Pick a single (non-indexed) format and its format-specific sub-options.
        private string pickSingleFormat(SystemType system)
        {
            List<string> choices = new List<string>(ConfigSettingsRanges.GetSupportedDualSingleFormats(system));
            choices.Add(BackChoice);
            string def = ConfigSettingsDefaults.GetDefaultFormat(system);
            if (!choices.Contains(def)) def = choices[0];

            string fmt = _p.Select($"Convert format for {system}", choices, def);
            if (fmt == null || fmt == BackChoice) return null;
            return composeSingleFormat(system, fmt);
        }

        // Pick an indexed format (cue/gdi). When optional, a "(none)" entry lets the user keep only
        // the single format. Returns "" for "(none)", the composed string otherwise, or null if the
        // user backed out with no single format to fall back to.
        private string pickIndexedFormat(SystemType system, bool optional)
        {
            List<string> choices = new List<string>();
            if (optional) choices.Add("(none)");
            choices.AddRange(ConfigSettingsRanges.GetSupportedDualIndexFormats(system));
            choices.Add(BackChoice);

            string fmt = _p.Select($"Indexed (disc) format for {system}", choices, choices[0]);
            if (fmt == null || fmt == BackChoice) return optional ? "" : null;
            if (fmt == "(none)") return "";
            return composeIndexedFormat(fmt);
        }

        // Build the ':'-string for a single format, gating each sub-option by the same predicates the
        // UI uses and drawing choices from the NKit-layer ranges (single source of truth).
        private string composeSingleFormat(SystemType system, string format)
        {
            switch (format)
            {
                case ConfigSettingsConstants.FormatRvz:
                    {
                        // RVZ: encoding → (level if the encoding has levels) → blockSize → parallelism.
                        RvzEncodingType encoding = RvzEncodingType.ZStd;
                        if (ConfigSettingsDefaults.IsEncodingTypeSupported(system, format))
                        {
                            List<string> encChoices = new List<string>();
                            foreach (RvzEncodingType e in ConfigSettingsRanges.GetRvzEncodingTypes())
                                encChoices.Add(e.ToString());
                            string enc = _p.Select("RVZ encoding", encChoices, RvzEncodingType.ZStd.ToString());
                            Enum.TryParse(enc, out encoding);
                        }

                        string encStr = encoding == RvzEncodingType.ZStd ? ConfigSettingsConstants.EncodingZStd
                                      : encoding == RvzEncodingType.Lzma ? ConfigSettingsConstants.EncodingLzma
                                      : ConfigSettingsConstants.EncodingNone;

                        int? level = null;
                        if (ConfigSettingsDefaults.IsLevelsSupported(format, encStr))
                        {
                            List<string> levels = new List<string>();
                            foreach (int l in ConfigSettingsRanges.GetCompressionLevels(format, encoding)) levels.Add(l.ToString());
                            if (levels.Count != 0)
                            {
                                string dl = ConfigSettingsDefaults.GetDefaultCompressionLevel(encoding).ToString();
                                if (!levels.Contains(dl)) dl = levels[levels.Count - 1];
                                string picked = _p.Select("Compression level", levels, dl);
                                if (int.TryParse(picked, out int lv)) level = lv;
                            }
                        }

                        string block = pickBlockSize(format);
                        int? par = pickParallelism(system);
                        return ConfigSettingsFormatGenerator.GenerateRvzFormatString(encoding, level, block, par);
                    }

                case ConfigSettingsConstants.FormatCso:
                    return ConfigSettingsFormatGenerator.GenerateCsoFormatString(
                        pickLevel(format), pickBlockSize(format), pickParallelismStr(system));
                case ConfigSettingsConstants.FormatCso2:
                    return ConfigSettingsFormatGenerator.GenerateCso2FormatString(
                        pickLevel(format), pickBlockSize(format), pickParallelismStr(system));
                case ConfigSettingsConstants.FormatZso:
                    return ConfigSettingsFormatGenerator.GenerateZsoFormatString(
                        pickLevel(format), pickBlockSize(format), pickParallelismStr(system));

                case ConfigSettingsConstants.FormatWbfs:
                    return ConfigSettingsFormatGenerator.GenerateWbfsFormatString(pickLossless(format));
                case ConfigSettingsConstants.FormatCiso:
                    return ConfigSettingsFormatGenerator.GenerateCisoFormatString(pickLossless(format));

                // Parameterless single formats (iso, deciso, app, tmd, wux, xiso, …) map to their name.
                default:
                    return format;
            }
        }

        // Build the ':'-string for an indexed format (cue has sub-options; gdi is parameterless).
        private string composeIndexedFormat(string format)
        {
            if (format == ConfigSettingsConstants.FormatCue)
            {
                string cueType = null, binary = null, audio = null;
                if (ConfigSettingsDefaults.IsCueTypesSupported(format))
                {
                    List<string> types = new List<string>(ConfigSettingsRanges.GetCueTypes());
                    cueType = _p.Select("CUE type", types, ConfigSettingsDefaults.GetDefaultCueType());
                }
                if (ConfigSettingsDefaults.IsBinaryExtensionsSupported(format))
                {
                    List<string> bins = new List<string>(ConfigSettingsRanges.GetBinaryExtensions());
                    binary = _p.Select("Binary extension", bins, ConfigSettingsDefaults.GetDefaultBinaryExtension());
                }
                if (ConfigSettingsDefaults.IsAudioExtensionsSupported(format))
                {
                    List<string> auds = new List<string>(ConfigSettingsRanges.GetAudioExtensions());
                    audio = _p.Select("Audio extension", auds, ConfigSettingsDefaults.GetDefaultAudioExtension());
                }
                return ConfigSettingsFormatGenerator.GenerateCueFormatString(cueType, binary, audio);
            }
            // gdi (and any future parameterless indexed format) → just the name.
            return format;
        }

        // Shared sub-option pickers (gated by the caller; each returns null to accept the generator default).
        private string pickBlockSize(string format)
        {
            if (!ConfigSettingsDefaults.IsBlockSizesSupported(format)) return null;
            List<string> sizes = new List<string>(ConfigSettingsRanges.GetBlockSizes(format));
            if (sizes.Count == 0) return null;
            string def = ConfigSettingsDefaults.GetDefaultBlockSize(SystemType.GameCube, format);
            if (!sizes.Contains(def)) def = sizes[0];
            return _p.Select("Block size", sizes, def);
        }

        private string pickLevel(string format)
        {
            // cso/cso2/zso report level support regardless of encoding; rvz is handled inline elsewhere.
            if (!ConfigSettingsDefaults.IsLevelsSupported(format, null)) return null;
            List<string> levels = new List<string>();
            foreach (int l in ConfigSettingsRanges.GetCompressionLevels(format)) levels.Add(l.ToString());
            if (levels.Count == 0) return null;
            string def = ConfigSettingsDefaults.GetDefaultCompressionLevel(format).ToString();
            if (!levels.Contains(def)) def = levels[levels.Count - 1];
            return _p.Select("Compression level", levels, def);
        }

        // Only called for compressed formats (rvz/cso/cso2/zso), which all support parallelism.
        private int? pickParallelism(SystemType system)
        {
            List<string> vals = new List<string>();
            foreach (int v in ConfigSettingsRanges.GetParallelismValues(system)) vals.Add(v.ToString());
            if (vals.Count == 0) return null;
            string def = ConfigSettingsDefaults.GetDefaultParallelism(system).ToString();
            if (!vals.Contains(def)) def = vals[0];
            string picked = _p.Select("Parallelism (threads)", vals, def);
            return int.TryParse(picked, out int p) ? p : (int?)null;
        }

        private string pickParallelismStr(SystemType system)
        {
            int? p = pickParallelism(system);
            return p?.ToString();
        }

        private bool? pickLossless(string format)
        {
            if (!ConfigSettingsDefaults.IsLosslessSupported(format)) return null;
            string chosen = _p.Select("Lossless", new List<string> { "yes", "no" }, "yes");
            return chosen == "yes";
        }

        private string[] assemble(string verb, List<string> inputs, string outputDir, List<OptState> table)
            => assemble(verb, inputs, outputDir, table, onlyUserSet: false);

        // When onlyUserSet is true, emit ONLY the mandatory items (verb + inputs + a chosen output)
        // plus options the user SPECIFICALLY set — i.e. values they changed from the config seed,
        // per-system scoped edits, and switches they turned on. Untouched config-seeded defaults are
        // omitted (they still apply at run time via the config). Used for the reproducible command
        // display so it shows the user's intent, not the whole resolved config.
        private string[] assemble(string verb, List<string> inputs, string outputDir, List<OptState> table, bool onlyUserSet)
        {
            List<string> argv = new List<string> { verb };
            foreach (string inp in inputs) argv.Add(inp); // positional, verbatim
            if (!string.IsNullOrWhiteSpace(outputDir))
                argv.Add2("--output", outputDir);

            foreach (OptState st in table)
            {
                CliOption o = st.Option;
                if (o.IsSwitch)
                {
                    if (st.SwitchOn) argv.Add("--" + o.Canonical);
                    continue;
                }
                // Unscoped value: for the run argv emit any value; for the reproducible view emit
                // only values the user changed from the config seed.
                if (!string.IsNullOrEmpty(st.Value) && (!onlyUserSet || st.Changed))
                    argv.Add2("--" + o.Canonical, st.Value);
                // Flag-prefix scoping (--wii:format rvz:19) mirrors the config's sys: structure and
                // round-trips through CliParser. Scoped entries are always explicit user edits.
                foreach ((string sys, string val) in st.Scoped)
                    argv.Add2("--" + sys + ":" + o.Canonical, val);
            }
            return argv.ToArray();
        }

        // Offer to persist changed values back to the config (comment-preserving surgical edit).
        // Root options write to root keys; prefixable options write to the chosen system's block
        // (or, for values entered with a wii:-style scope, to that system's block).
        private void offerWriteBack(List<OptState> table)
        {
            if (_config == null || !_config.HasConfig || string.IsNullOrEmpty(_config.Path)) return;

            string system = currentSystem(table);

            // Collect (system|null, configKey, newValue, oldValue) for changed value options.
            List<(string sys, CliOption opt, string val, string old)> changes = new List<(string, CliOption, string, string)>();
            foreach (OptState st in table)
            {
                CliOption o = st.Option;
                if (o.IsSwitch || o.ConfigKey == null) continue;
                if (string.Equals(o.Canonical, "system", StringComparison.OrdinalIgnoreCase)) continue;

                // scoped edits (wii:val ...) → each to its system
                foreach ((string sys, string val) in st.Scoped)
                    changes.Add((sys, o, val, _config.ValueFor(o, sys)));

                if (!st.Changed) continue;
                string targetSys = o.Prefixable ? system : null; // root option → root
                changes.Add((targetSys, o, st.Value ?? "", st.Original));
            }

            if (changes.Count == 0) return;

            _p.Info("These config values changed:");
            foreach ((string sys, CliOption opt, string val, string old) in changes)
            {
                string scope = sys == null ? "" : sys + ":";
                _p.Info($"  {scope}{opt.Canonical}:  {(string.IsNullOrEmpty(old) ? "(unset)" : old)}  ->  {(string.IsNullOrEmpty(val) ? "(unset)" : val)}");
            }

            if (!_p.Confirm($"Save these to the config ({System.IO.Path.GetFileName(_config.Path)})?", false))
                return;

            try
            {
                YamlConfigEditor yaml = _config.Yaml;
                foreach ((string sys, CliOption opt, string val, string old) in changes)
                {
                    // canonical config key primary, legacy as fallback so existing-key lines are edited.
                    string[] keys = ConfigDefaults.Keys(opt);
                    string primary = keys[0];
                    string[] alts = keys.Length > 1 ? new[] { keys[1] } : System.Array.Empty<string>();
                    if (sys == null) yaml.SetRoot(primary, val, alts);
                    else yaml.SetSystem(sys, primary, val, alts);
                }
                yaml.Save(_config.Path);
                _p.Info("Config saved.");
            }
            catch (Exception ex)
            {
                _p.Info("Could not save config: " + ex.Message);
            }
        }

        private static IEnumerable<string> quote(IEnumerable<string> tokens)
        {
            foreach (string t in tokens)
                yield return t.IndexOf(' ') >= 0 ? "\"" + t + "\"" : t;
        }
    }

    internal static class ListExtensions
    {
        public static void Add2(this List<string> list, string a, string b)
        {
            list.Add(a);
            list.Add(b);
        }
    }
}