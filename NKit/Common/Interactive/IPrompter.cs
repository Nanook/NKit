using System.Collections.Generic;

namespace Nanook.NKit.Interactive
{
    /// <summary>
    /// Dependency-free abstraction over interactive console prompts, shared by the NKit and NKDS
    /// interactive builders. A Spectre-backed implementation lives in each app (so the core library
    /// takes no UI dependency); a scripted implementation drives unit tests.
    /// </summary>
    public interface IPrompter
    {
        /// <summary>Select one item from a scrolling list (dropdown). Returns the chosen value.</summary>
        string Select(string title, IReadOnlyList<string> choices, string defaultChoice = null);

        /// <summary>Select zero or more items (checkboxes).</summary>
        IReadOnlyList<string> MultiSelect(string title, IReadOnlyList<string> choices);

        /// <summary>Yes/no confirmation.</summary>
        bool Confirm(string title, bool defaultValue = false);

        /// <summary>Free-text entry with an optional default; empty allowed unless required.</summary>
        string Text(string title, string defaultValue = null, bool allowEmpty = true);

        /// <summary>Write an informational line (e.g. the assembled command).</summary>
        void Info(string message);

        /// <summary>
        /// Write pre-formatted markup (Spectre.Console markup for the console apps). Used for content
        /// that already carries colour, e.g. the rendered help text. Plain implementations may write
        /// it verbatim; the console apps render the markup.
        /// </summary>
        void InfoMarkup(string markup);

        /// <summary>
        /// Write a labelled echo line as "label: value" (e.g. "Task: convert"). Implementations may
        /// style the label and value distinctly (the Spectre apps colour label white, value cyan);
        /// plain implementations render "label: value".
        /// </summary>
        void InfoPair(string label, string value);

        /// <summary>
        /// Like <see cref="InfoPair"/> but collapses the most recently written prompt line into the
        /// echo (same behaviour as <see cref="ReplaceLastLine"/>).
        /// </summary>
        void ReplaceLastLinePair(string label, string value);

        /// <summary>
        /// Replace the most recently written console line with <paramref name="message"/>. Used to
        /// collapse a just-answered prompt (e.g. "Input file, folder or mask: X") into a clean echo
        /// ("Input: X") on the same line. Implementations that cannot address the cursor may simply
        /// write a new line.
        /// </summary>
        void ReplaceLastLine(string message);

        /// <summary>
        /// Render a contextual help panel: a title, a description, and a list of worked examples
        /// (each "value" + optional "note"). Implementations draw a bordered box; tests may record it.
        /// </summary>
        void ShowHelp(string title, string description, IReadOnlyList<(string value, string note)> examples);
    }
}