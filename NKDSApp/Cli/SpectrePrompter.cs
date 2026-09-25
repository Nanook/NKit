using Nanook.NKit.Interactive;
using Spectre.Console;
using System;
using System.Collections.Generic;

namespace Nanook.NKit.Cli
{
    /// <summary>
    /// Spectre.Console implementation of the shared <see cref="IPrompter"/> — the single class that
    /// touches Spectre prompt widgets for BOTH console apps (nkit and nkds). AOT-safe: concrete
    /// string prompts, no reflection.
    /// <para>
    /// This file is shared by <c>&lt;Compile Include Link&gt;</c> into each app rather than living in
    /// the core NKit library, because that library is deliberately presentation-agnostic and takes no
    /// Spectre.Console dependency. Compiling it into each AOT app avoids a cross-assembly dependency
    /// and keeps a single source of truth for the interactive palette and behaviour.
    /// </para>
    /// <para>
    /// Palette (matches the LiveConsole log/progress scheme: cyan + yellow, no red/green): prompt
    /// titles = yellow; the moving selection highlight = cyan; a labelled echo = white label : cyan
    /// value.
    /// </para>
    /// </summary>
    public sealed class SpectrePrompter : IPrompter
    {
        private static string Heading(string title) => $"[yellow]{Markup.Escape(title)}[/]";
        private static readonly Style HighlightStyle = new Style(foreground: Color.Cyan1);

        public string Select(string title, IReadOnlyList<string> choices, string defaultChoice = null)
        {
            SelectionPrompt<string> prompt = new SelectionPrompt<string>()
                .Title(Heading(title))
                .PageSize(15)
                .HighlightStyle(HighlightStyle)
                .AddChoices(choices);
            string result = AnsiConsole.Prompt(prompt);
            // Restore the cursor — Spectre hides it (ESC[?25l) during interactive selection and, on
            // Linux, may not restore it cleanly, leaving the terminal cursor invisible afterwards.
            // SelectionPrompt has no visible text caret, so it is the main offender.
            try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { }
            return result;
        }

        public IReadOnlyList<string> MultiSelect(string title, IReadOnlyList<string> choices)
        {
            MultiSelectionPrompt<string> prompt = new MultiSelectionPrompt<string>()
                .Title(Heading(title))
                .PageSize(15)
                .NotRequired()
                .HighlightStyle(HighlightStyle)
                .InstructionsText("[grey](space to toggle, enter to accept)[/]")
                .AddChoices(choices);
            IReadOnlyList<string> result = AnsiConsole.Prompt(prompt);
            try { if (!Console.IsOutputRedirected) Console.CursorVisible = true; } catch { }
            return result;
        }

        public bool Confirm(string title, bool defaultValue = false)
            => AnsiConsole.Confirm(Heading(title), defaultValue);

        public string Text(string title, string defaultValue = null, bool allowEmpty = true)
        {
            TextPrompt<string> prompt = new TextPrompt<string>(Heading(title) + ":");
            if (defaultValue != null)
                prompt.DefaultValue(defaultValue);
            if (allowEmpty)
                prompt.AllowEmpty();
            return AnsiConsole.Prompt(prompt);
        }

        public void Info(string message)
            => AnsiConsole.WriteLine(message);

        // Pre-formatted Spectre markup (e.g. coloured help text). The content is already
        // newline-terminated, so use Markup (not MarkupLine) to avoid an extra blank line.
        public void InfoMarkup(string markup)
            => AnsiConsole.Markup(markup);

        // Labelled echo: "label: value" with the label white and the value cyan (matches the
        // scheme's White : cyan param styling).
        private static string PairMarkup(string label, string value)
            => $"[bold white]{Markup.Escape(label)}[/]: [cyan]{Markup.Escape(value)}[/]";

        public void InfoPair(string label, string value)
            => AnsiConsole.MarkupLine(PairMarkup(label, value));

        public void ReplaceLastLinePair(string label, string value)
        {
            collapseLastLine();
            AnsiConsole.MarkupLine(PairMarkup(label, value));
        }

        public void ReplaceLastLine(string message)
        {
            collapseLastLine();
            AnsiConsole.WriteLine(message);
        }

        // Move the cursor up onto the just-printed prompt line and clear it so the caller's echo
        // replaces it. Guarded: if the console isn't addressable (redirected/no cursor), do nothing
        // and the caller simply writes a new line below.
        private static void collapseLastLine()
        {
            try
            {
                if (!Console.IsOutputRedirected && Console.CursorTop > 0)
                {
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                    Console.Write(new string(' ', Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 0));
                    Console.SetCursorPosition(0, Console.CursorTop);
                }
            }
            catch { /* not addressable — just write the line below */ }
        }

        public void ShowHelp(string title, string description, IReadOnlyList<(string value, string note)> examples)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(description))
                sb.Append(Markup.Escape(description));
            if (examples != null && examples.Count != 0)
            {
                if (sb.Length != 0) sb.Append("\n\n");
                sb.Append("[bold yellow]Examples:[/]");
                int pad = 0;
                foreach ((string value, string note) in examples)
                    if (value.Length > pad) pad = value.Length;
                foreach ((string value, string note) in examples)
                {
                    string v = value.Length == 0 ? "(blank)" : value;
                    // Example values in cyan, notes in grey — matches the cyan/yellow scheme.
                    sb.Append("\n  [cyan]").Append(Markup.Escape(v.PadRight(pad))).Append("[/]");
                    if (!string.IsNullOrEmpty(note))
                        sb.Append("  [grey]").Append(Markup.Escape(note)).Append("[/]");
                }
            }
            AnsiConsole.Write(new Panel(new Markup(sb.ToString()))
                .Header($"[yellow]{Markup.Escape(title)}[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1));
        }
    }
}
