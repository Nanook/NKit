using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Ogmr
{
    /// <summary>
    /// Hand-rolled parser for the simple 1GMR YAML format.
    /// Reads a <c>games:</c> list where each entry has a <c>name</c> and one or more
    /// regex <c>masks</c> used for filename matching.
    /// </summary>
    public sealed class OgmrYamlParser
    {
        /// <summary>
        /// Parses a 1GMR YAML file and returns a list of <see cref="GameEntry"/> objects
        /// with pre-compiled regex masks.
        /// </summary>
        /// <param name="filePath">Path to the 1GMR YAML file.</param>
        /// <returns>A list of parsed and validated game entries.</returns>
        /// <exception cref="OgmrException">
        /// Thrown when the file is not found, the YAML structure is invalid,
        /// or a regex pattern cannot be compiled.
        /// </exception>
        public static List<GameEntry> Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new OgmrException($"1GMR YAML file not found: '{filePath}'");

            string[] lines = File.ReadAllLines(filePath);

            bool foundGamesKey = false;
            List<GameEntry> entries = new List<GameEntry>();
            string currentName = null;
            List<string> currentMasks = new List<string>();
            bool inMasks = false;
            int entryIndex = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                // Look for the top-level "games:" key
                if (!foundGamesKey)
                {
                    if (trimmed == "games:" || trimmed == "games: ")
                    {
                        foundGamesKey = true;
                    }
                    continue;
                }

                // Inside the games list - detect list items and properties
                if (trimmed.StartsWith("- name:"))
                {
                    // Flush previous entry if any
                    if (currentName != null)
                    {
                        flushEntry(entries, currentName, currentMasks, entryIndex);
                        entryIndex++;
                    }

                    currentName = extractValue(trimmed, "- name:");
                    if (string.IsNullOrWhiteSpace(currentName))
                        throw new OgmrException($"1GMR YAML: entry at index {entryIndex} has an empty 'name' field.");

                    currentMasks.Clear();
                    inMasks = false;
                }
                else if (trimmed == "masks:" || trimmed == "masks: ")
                {
                    inMasks = true;
                }
                else if (inMasks && trimmed.StartsWith("- "))
                {
                    string pattern = extractMaskPattern(trimmed);
                    currentMasks.Add(pattern);
                }
                else if (trimmed.StartsWith("name:"))
                {
                    // "name:" without the list marker "- " means it's a property of the current entry
                    // but only if we haven't already captured a name for this entry
                    if (currentName == null)
                    {
                        currentName = extractValue(trimmed, "name:");
                        if (string.IsNullOrWhiteSpace(currentName))
                            throw new OgmrException($"1GMR YAML: entry at index {entryIndex} has an empty 'name' field.");
                    }
                    inMasks = false;
                }
                else
                {
                    // Unknown line inside games block - could be another top-level key, stop parsing
                    if (!line.StartsWith(' ') && !line.StartsWith('\t'))
                        break;
                }
            }

            // Flush the last entry
            if (currentName != null)
            {
                flushEntry(entries, currentName, currentMasks, entryIndex);
            }

            if (!foundGamesKey)
                throw new OgmrException("1GMR YAML: the file does not contain a 'games:' key.");

            if (entries.Count == 0)
                throw new OgmrException("1GMR YAML: the 'games:' list is empty.");

            return entries;
        }

        private static void flushEntry(List<GameEntry> entries, string name, List<string> masks, int entryIndex)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new OgmrException($"1GMR YAML: entry at index {entryIndex} is missing a 'name' field.");

            if (masks.Count == 0)
                throw new OgmrException($"1GMR YAML: game '{name}' has no masks defined.");

            // Validate each regex pattern before constructing the GameEntry
            foreach (string pattern in masks)
            {
                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException ex)
                {
                    throw new OgmrException($"1GMR YAML: game '{name}' has an invalid regex pattern '{pattern}': {ex.Message}");
                }
            }

            entries.Add(new GameEntry(name, masks.ToArray()));
        }

        private static string extractValue(string line, string prefix)
        {
            string value = line.Substring(prefix.Length).Trim();

            // Remove surrounding quotes if present
            if (value.Length >= 2)
            {
                if ((value[0] == '\'' && value[^1] == '\'') ||
                    (value[0] == '"' && value[^1] == '"'))
                {
                    value = value[1..^1];
                }
            }

            return value;
        }

        private static string extractMaskPattern(string trimmed)
        {
            // Remove the leading "- " from the list item
            string value = trimmed.Substring(2).Trim();

            // Remove surrounding single or double quotes if present
            if (value.Length >= 2)
            {
                if ((value[0] == '\'' && value[^1] == '\'') ||
                    (value[0] == '"' && value[^1] == '"'))
                {
                    value = value[1..^1];
                }
            }

            return value;
        }
    }
}