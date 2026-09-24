using System;
using System.Collections.Generic;

namespace Nanook.NKit.Interactive
{
    /// <summary>
    /// Test/automation <see cref="IPrompter"/> returning pre-scripted answers. Supports ordered
    /// queues and title/choice-keyed answers so tests can target a specific prompt regardless of
    /// order. Shared by the NKit and NKDS interactive builder tests.
    /// </summary>
    public sealed class ScriptedPrompter : IPrompter
    {
        private readonly Queue<string> _selects = new Queue<string>();
        private readonly Queue<IReadOnlyList<string>> _multi = new Queue<IReadOnlyList<string>>();
        private readonly Queue<bool> _confirms = new Queue<bool>();
        private readonly Queue<string> _texts = new Queue<string>();

        private readonly List<(string key, string value)> _textByTitle = new List<(string, string)>();
        private readonly List<(string key, bool value)> _confirmByTitle = new List<(string, bool)>();
        private readonly List<string> _selectByChoice = new List<string>();

        public List<string> InfoLines { get; } = new List<string>();

        public ScriptedPrompter QueueSelect(string value) { _selects.Enqueue(value); return this; }
        public ScriptedPrompter QueueMultiSelect(params string[] values) { _multi.Enqueue(values); return this; }
        public ScriptedPrompter QueueConfirm(bool value) { _confirms.Enqueue(value); return this; }
        public ScriptedPrompter QueueText(string value) { _texts.Enqueue(value); return this; }

        /// <summary>Answer any Text prompt whose title contains <paramref name="titleKey"/>.</summary>
        public ScriptedPrompter TextFor(string titleKey, string value) { _textByTitle.Add((titleKey, value)); return this; }

        /// <summary>Answer any Confirm prompt whose title contains <paramref name="titleKey"/>.</summary>
        public ScriptedPrompter ConfirmFor(string titleKey, bool value) { _confirmByTitle.Add((titleKey, value)); return this; }

        /// <summary>Answer the next Select whose choices contain one starting with <paramref name="choicePrefix"/>.</summary>
        public ScriptedPrompter SelectFor(string choicePrefix, string ignored = null) { _selectByChoice.Add(choicePrefix); return this; }

        public string Select(string title, IReadOnlyList<string> choices, string defaultChoice = null)
        {
            for (int i = 0; i < _selectByChoice.Count; i++)
            {
                string prefix = _selectByChoice[i];
                foreach (string ch in choices)
                {
                    if (ch != null && ch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectByChoice.RemoveAt(i);
                        return ch;
                    }
                }
            }
            return _selects.Count > 0 ? _selects.Dequeue() : (defaultChoice ?? (choices.Count > 0 ? choices[0] : null));
        }

        public IReadOnlyList<string> MultiSelect(string title, IReadOnlyList<string> choices)
            => _multi.Count > 0 ? _multi.Dequeue() : Array.Empty<string>();

        public bool Confirm(string title, bool defaultValue = false)
        {
            foreach ((string key, bool value) in _confirmByTitle)
                if (title != null && title.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return value;
            return _confirms.Count > 0 ? _confirms.Dequeue() : defaultValue;
        }

        public string Text(string title, string defaultValue = null, bool allowEmpty = true)
        {
            foreach ((string key, string value) in _textByTitle)
                if (title != null && title.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return value;
            return _texts.Count > 0 ? _texts.Dequeue() : defaultValue;
        }

        public void Info(string message) => InfoLines.Add(message);

        /// <summary>Records pre-formatted markup verbatim (tags included) for test assertions.</summary>
        public void InfoMarkup(string markup) => InfoLines.Add(markup);

        /// <summary>Records the labelled echo as "label: value" (unstyled) for test assertions.</summary>
        public void InfoPair(string label, string value) => InfoLines.Add($"{label}: {value}");

        /// <summary>Records the replacement as the final info line (mirrors the console collapse).</summary>
        public void ReplaceLastLine(string message) => InfoLines.Add(message);

        /// <summary>Records the labelled replacement as "label: value" (unstyled).</summary>
        public void ReplaceLastLinePair(string label, string value) => InfoLines.Add($"{label}: {value}");

        /// <summary>Records help panels shown (title → examples) for test assertions.</summary>
        public List<(string title, IReadOnlyList<(string value, string note)> examples)> HelpShown { get; }
            = new List<(string, IReadOnlyList<(string, string)>)>();

        public void ShowHelp(string title, string description, IReadOnlyList<(string value, string note)> examples)
            => HelpShown.Add((title, examples));
    }
}