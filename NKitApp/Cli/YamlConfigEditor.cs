using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nanook.NKit.App.Cli
{
    /// <summary>
    /// Minimal, comment-preserving reader/writer for the nkit YAML config. It does NOT re-serialize
    /// (which would strip the hand-written comments) — it reads a key's RAW value and, on write,
    /// surgically replaces just that key's value on its existing line, or inserts the key under the
    /// right block. AOT-safe (no YAML library); understands the two-level shape this config uses:
    /// root keys at column 0, and per-system keys indented under <c>sys: &lt;system&gt;:</c>.
    /// </summary>
    public sealed class YamlConfigEditor
    {
        private readonly List<string> _lines;

        public YamlConfigEditor(string text)
        {
            _lines = new List<string>((text ?? string.Empty).Replace("\r\n", "\n").Split('\n'));
        }

        public static YamlConfigEditor Load(string path)
            => new YamlConfigEditor(File.Exists(path) ? File.ReadAllText(path) : string.Empty);

        public string Text => string.Join(Environment.NewLine, _lines);

        public void Save(string path) => File.WriteAllText(path, Text);

        /// <summary>Read a root-level key's raw value (matching any of <paramref name="keys"/>).</summary>
        public string GetRoot(params string[] keys)
        {
            int i = findRootKey(keys);
            return i < 0 ? null : valueOf(_lines[i]);
        }

        /// <summary>
        /// True if a root-level key (matching any of <paramref name="keys"/>) has a non-empty value
        /// EITHER inline (<c>in: file</c>) OR as a following YAML list block (<c>in:</c> then
        /// <c>  - item</c> lines). Used to detect a configured input, which may be a list.
        /// </summary>
        public bool HasRootValue(params string[] keys)
        {
            int i = findRootKey(keys);
            if (i < 0) return false;

            // Inline scalar value present?
            if (!string.IsNullOrEmpty(valueOf(_lines[i])))
                return true;

            // Otherwise look for a following list block: subsequent, more-indented "- item" lines
            // (skipping blanks/comments) that carry a non-empty item.
            for (int j = i + 1; j < _lines.Count; j++)
            {
                string l = _lines[j];
                if (isBlank(l) || l.TrimStart().StartsWith("#")) continue;
                if (indentOf(l) == 0) break; // back at root — no list belonged to the key

                string t = l.TrimStart();
                if (t.StartsWith("-"))
                {
                    string item = t.Substring(1).Trim();
                    int hash = indexOfComment(item);
                    if (hash >= 0) item = item.Substring(0, hash).Trim();
                    if (item.Length != 0) return true; // a real list item
                }
                else
                {
                    // A non-list, more-indented line means this wasn't a list for our key.
                    break;
                }
            }
            return false;
        }

        /// <summary>Read a per-system key's raw value (matching any of <paramref name="keys"/>).</summary>
        public string GetSystem(string system, params string[] keys)
        {
            int block = findSystemBlock(system, out int blockIndent);
            if (block < 0) return null;
            int i = findKeyInBlock(block, blockIndent, keys);
            return i < 0 ? null : valueOf(_lines[i]);
        }

        /// <summary>
        /// Set a root-level key's value, preserving the line's comment. Matches any of
        /// <paramref name="keys"/> (canonical + legacy); the FIRST key is used when inserting.
        /// </summary>
        public void SetRoot(string primaryKey, string value, params string[] altKeys)
        {
            string[] keys = withPrimary(primaryKey, altKeys);
            int i = findRootKey(keys);
            if (i >= 0) _lines[i] = replaceValue(_lines[i], value);
            else _lines.Add($"{primaryKey}: {value}");
        }

        /// <summary>Set a per-system key's value under its <c>sys:</c> block, preserving comments.</summary>
        public void SetSystem(string system, string primaryKey, string value, params string[] altKeys)
        {
            string[] keys = withPrimary(primaryKey, altKeys);
            int block = findSystemBlock(system, out int blockIndent);
            if (block < 0) return; // system block not present — skip (do not invent structure)
            int i = findKeyInBlock(block, blockIndent, keys);
            if (i >= 0) { _lines[i] = replaceValue(_lines[i], value); return; }
            // insert a new key line right after the block header, indented one level deeper.
            string indent = new string(' ', blockIndent + 2);
            _lines.Insert(block + 1, $"{indent}{primaryKey}: {value}");
        }

        private static string[] withPrimary(string primary, string[] alts)
        {
            if (alts == null || alts.Length == 0) return new[] { primary };
            string[] all = new string[alts.Length + 1];
            all[0] = primary;
            Array.Copy(alts, 0, all, 1, alts.Length);
            return all;
        }

        // ── parsing helpers ──────────────────────────────────────────────────────

        private int findRootKey(string[] keys)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                string l = _lines[i];
                if (indentOf(l) != 0) continue;
                if (isKeyLine(l, keys)) return i;
            }
            return -1;
        }

        // Find "  <system>:" under the top-level "sys:" mapping. Returns the line index of the
        // system header and its indent, or -1.
        private int findSystemBlock(string system, out int indent)
        {
            indent = -1;
            int sysLine = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (indentOf(_lines[i]) == 0 && isKeyLine(_lines[i], new[] { "sys" })) { sysLine = i; break; }
            }
            if (sysLine < 0) return -1;
            for (int i = sysLine + 1; i < _lines.Count; i++)
            {
                string l = _lines[i];
                int ind = indentOf(l);
                if (isBlank(l)) continue;
                if (ind == 0) break; // left the sys: mapping
                if (isKeyLine(l, new[] { system })) { indent = ind; return i; }
            }
            return -1;
        }

        private int findKeyInBlock(int blockHeader, int blockIndent, string[] keys)
        {
            for (int i = blockHeader + 1; i < _lines.Count; i++)
            {
                string l = _lines[i];
                if (isBlank(l)) continue;
                int ind = indentOf(l);
                if (ind <= blockIndent) break; // left this system's block
                if (isKeyLine(l, keys)) return i;
            }
            return -1;
        }

        private static bool isBlank(string l) => l.TrimStart().Length == 0;

        private static int indentOf(string l)
        {
            int n = 0;
            while (n < l.Length && l[n] == ' ') n++;
            return n;
        }

        // A "key:" line (ignoring leading spaces) whose key matches any of the given names.
        private static bool isKeyLine(string line, string[] keys)
        {
            string t = line.TrimStart();
            if (t.StartsWith("#")) return false;
            int colon = t.IndexOf(':');
            if (colon < 0) return false;
            string k = t.Substring(0, colon).Trim();
            foreach (string key in keys)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The raw value after "key:" up to any trailing " #comment" (trimmed). "" if none.
        private static string valueOf(string line)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) return "";
            string rest = line.Substring(colon + 1);
            int hash = indexOfComment(rest);
            if (hash >= 0) rest = rest.Substring(0, hash);
            return rest.Trim();
        }

        // Replace the value between "key:" and any trailing comment, preserving indent + comment.
        private static string replaceValue(string line, string value)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) return line;
            string head = line.Substring(0, colon + 1);   // "  key:"
            string rest = line.Substring(colon + 1);
            int hash = indexOfComment(rest);
            string comment = hash >= 0 ? rest.Substring(hash).Trim() : "";

            StringBuilder sb = new StringBuilder();
            sb.Append(head);
            if (!string.IsNullOrEmpty(value))
                sb.Append(' ').Append(value);
            if (comment.Length != 0)
                sb.Append(string.IsNullOrEmpty(value) ? " " : "  ").Append(comment);
            return sb.ToString();
        }

        // Index of a '#' that starts a comment (not inside the value). Simple heuristic: a '#'
        // preceded by whitespace, or at start. Values in this config don't contain '#'.
        private static int indexOfComment(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '#' && (i == 0 || char.IsWhiteSpace(s[i - 1])))
                    return i;
            return -1;
        }
    }
}