using Nanook.NKit.Interactive;
using System;

namespace Nanook.NKit.App.Cli
{
    /// <summary>Outcome of the CLI front-end: either run with the built settings, or exit early.</summary>
    public sealed class CliResult
    {
        private CliResult() { }

        public bool ShouldRun { get; private init; }
        public int ExitCode { get; private init; }
        public string ConfigFile { get; private init; }
        public System.Collections.Generic.Dictionary<string, string> Overrides { get; private init; }

        public static CliResult Run(string configFile, System.Collections.Generic.Dictionary<string, string> overrides)
            => new CliResult { ShouldRun = true, ConfigFile = configFile, Overrides = overrides };

        public static CliResult Exit(int code)
            => new CliResult { ShouldRun = false, ExitCode = code };
    }

    /// <summary>
    /// Top-level CLI orchestration: parse argv, handle help/version, launch interactive when there
    /// is no task and the terminal allows it, otherwise translate to AppSettings overrides. Kept
    /// thin; all real work is in the (testable) parser/bridge/builder.
    /// </summary>
    public static class CliFrontEnd
    {
        /// <param name="args">Raw argv (excluding the exe name is fine; parser skips a leading verb only).</param>
        /// <param name="version">Version string for help/version output.</param>
        /// <param name="defaultTask">Config default task name to pre-select in interactive (or null).</param>
        /// <param name="prompterFactory">Creates the interactive prompter (injectable for tests).</param>
        public static CliResult Process(string[] args, string version, string defaultTask, Func<IPrompter> prompterFactory)
        {
            ParsedCommand cmd;
            try
            {
                cmd = CliParser.Parse(args);
            }
            catch (CliException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.WriteLine();
                // HelpRenderer returns Spectre markup (already newline-terminated) — print with Markup.
                Spectre.Console.AnsiConsole.Markup(ex.Verb != null ? HelpRenderer.Verb(ex.Verb, version) : HelpRenderer.General(version));
                return CliResult.Exit(2);
            }

            if (cmd.ShowVersion)
            {
                // App name green, version yellow — matching the help header.
                Spectre.Console.AnsiConsole.MarkupLine($"[green]NKit[/] [yellow]v{Spectre.Console.Markup.Escape(version)}[/]");
                return CliResult.Exit(0);
            }

            if (cmd.ShowHelp)
            {
                Spectre.Console.AnsiConsole.Markup(cmd.HelpVerb != null ? HelpRenderer.Verb(cmd.HelpVerb, version) : HelpRenderer.General(version));
                return CliResult.Exit(0);
            }

            // Interactive mode rules (in priority order):
            //
            //   1. --interactive (-i) on the CLI: ALWAYS launch the guided builder, whatever else was
            //      supplied. Any verb/inputs already parsed are pre-seeded.
            //
            //   2. Positional-only inputs with no verb (drag-drop: `nkit file.iso`): launch
            //      interactive with the dropped path(s) pre-seeded so the user picks a task/options.
            //      If a verb IS present (e.g. `nkit convert file.iso`), the user has specified the
            //      task explicitly — run directly with the positional treated as the input.
            //
            //   3. No CLI params at all (no verb, no options, no inputs): launch interactive UNLESS the
            //      config alone can drive a direct run — i.e. it supplies BOTH a task and an input.
            //      A config missing either one goes interactive (the config task pre-selects the verb).
            //
            //   4. Anything else (an explicit --input, a verb, or other options present) runs directly;
            //      the task may still come from the config.
            ConfigDefaults config = ConfigDefaults.Load(SafeConfigPath(cmd));
            bool hasExplicitInput = cmd.HasExplicitInput;   // true only for --input / -in
            bool hasPositionalInput = cmd.Inputs.Count != 0 && !hasExplicitInput;
            string configTask = config.TaskValue();         // config default verb (null if unset)

            // "No CLI params" = no verb, no inputs, and no options other than the bare --interactive
            // switch itself (which is handled by rule 1). help/version were already dealt with above.
            bool hasCliParams = cmd.Verb != null
                             || cmd.Inputs.Count != 0
                             || hasOptionsBeyondInteractive(cmd);

            bool configCanRunDirect = config.HasTask() && config.HasInput();

            // Rule 2 only fires when there is NO verb — a positional alongside a verb (e.g.
            // `nkit convert file.iso`) is a fully specified command and must run directly.
            bool isDragDropWithoutVerb = hasPositionalInput && cmd.Verb == null;

            bool goInteractive = cmd.HasSwitch("interactive")               // rule 1: explicit flag
                              || isDragDropWithoutVerb                      // rule 2: bare drag-drop
                              || (!hasCliParams && !configCanRunDirect);     // rule 3: nothing to run

            if (goInteractive)
            {
                if (!TerminalCapabilities.IsInteractive)
                {
                    Spectre.Console.AnsiConsole.Markup(HelpRenderer.General(version));
                    Console.Error.WriteLine("No task/input specified. Provide one, e.g. 'nkit convert <input>'.");
                    return CliResult.Exit(2);
                }

                // Pre-select the config's task in the builder when the CLI didn't name a verb.
                string seedTask = defaultTask ?? configTask;

                // Seed the builder from the active config (raw values) and let it offer a write-back.
                // Positional inputs are already in cmd.Inputs and will be pre-seeded by the builder.
                InteractiveBuilder builder = new InteractiveBuilder(prompterFactory(), config);
                string[] built;
                try
                {
                    built = builder.Build(cmd, seedTask);
                }
                finally
                {
                    // Belt-and-braces: Spectre's interactive prompts hide the cursor (ESC[?25l) and
                    // on Linux may not restore it if the builder exits via an unexpected path. Ensure
                    // the terminal cursor is visible again no matter how Build() returned.
                    try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { }
                }
                if (built == null)
                    return CliResult.Exit(0); // user declined

                // Re-parse the assembled argv through the SAME path (single source of truth).
                try
                {
                    cmd = CliParser.Parse(built);
                }
                catch (CliException ex)
                {
                    Console.Error.WriteLine("Error: " + ex.Message);
                    return CliResult.Exit(2);
                }
            }

            (string configFile, System.Collections.Generic.Dictionary<string, string> overrides) = AppSettingsBridge.Build(cmd);
            return CliResult.Run(configFile, overrides);
        }

        // True when the command carries any parsed option/switch OTHER than the bare --interactive
        // flag. Used to decide "no CLI params" for the interactive gate: --interactive on its own does
        // not count as a param (it only forces interactive, which rule 1 already handles).
        private static bool hasOptionsBeyondInteractive(ParsedCommand cmd)
        {
            foreach (ScopedValue v in cmd.Values)
                if (!string.Equals(v.Option.Canonical, "interactive", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // True when the command disables config loading (--no-config, or --config n / empty), so the
        // config's input must not be considered when deciding whether to go interactive.
        private static bool NoConfig(ParsedCommand cmd)
        {
            if (cmd.HasSwitch("no-config"))
                return true;
            string cfg = cmd.Get("config");
            return cfg != null && (cfg.Trim().Length == 0 || string.Equals(cfg.Trim(), "n", StringComparison.OrdinalIgnoreCase));
        }

        // Resolve the active config path for interactive seeding/write-back. Honours a --config
        // override (rooted, or relative to the exe dir like AppSettings.getConfig); falls back to
        // the user config. Returns null when config is disabled. Never throws.
        private static string SafeConfigPath(ParsedCommand cmd)
        {
            try
            {
                if (NoConfig(cmd))
                    return null;

                string cfg = cmd.Get("config");
                if (!string.IsNullOrWhiteSpace(cfg))
                {
                    return System.IO.Path.IsPathRooted(cfg)
                        ? cfg
                        : System.IO.Path.Combine(AppSettings.ThisExe.DirectoryName, cfg);
                }

                return AppSettings.GetUserConfigFullName();
            }
            catch { return null; }
        }
    }
}