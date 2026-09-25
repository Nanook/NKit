using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Settings
{
    /// <summary>
    /// AOT-compatible YAML deserializer that produces the same Dictionary<object, object> structure
    /// as YamlDotNet.Serialization.Deserializer for compatibility with existing AppSettings and fix file code
    /// </summary>
    internal static class AotYamlDeserializer
    {
        // YAML parsing constants
        private const string YAML_COMMENT_PREFIX = "#";
        private const string YAML_LIST_PREFIX = "-";
        private const string YAML_PROPERTY_SEPARATOR = ":";
        private const string YAML_DOUBLE_QUOTE = "\"";
        private const string YAML_SINGLE_QUOTE = "'";
        private const string YAML_SPACE = " ";
        private const string EMPTY_STRING = "";

        // Section name constants for main config
        private const string SECTION_SYS = "sys";
        private const string SECTION_DATS = "dats";

        // Magic numbers
        private const int SYSTEM_INDENT_LEVEL = 2;

        /// <summary>
        /// Deserializes YAML content to Dictionary<object, object> structure compatible with original AppSettings
        /// </summary>
        public static Dictionary<object, object> Deserialize(string yamlContent)
        {
            Dictionary<object, object> result = new Dictionary<object, object>();
            string[] lines = yamlContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string currentSection = EMPTY_STRING;
            string currentSystemName = EMPTY_STRING;
            Dictionary<object, object> currentSystemDict = null;
            Dictionary<object, object> datsDict = null;
            Dictionary<object, object> sysDict = new Dictionary<object, object>();

            int sectionIndent = 0;
            int systemIndent = 0;

            // Track list state for root-level lists
            string currentListProperty = null;
            List<object> currentList = null;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                string trimmed = line.Trim();

                // Skip comments and empty lines
                if (trimmed.StartsWith(YAML_COMMENT_PREFIX) || string.IsNullOrEmpty(trimmed))
                    continue;

                int indent = line.Length - line.TrimStart().Length;

                // Handle list items (lines starting with -)
                if (trimmed.StartsWith(YAML_LIST_PREFIX))
                {
                    string listValue = trimmed.Substring(1).Trim();
                    if (currentList != null && !string.IsNullOrEmpty(listValue))
                    {
                        currentList.Add(parseValue(listValue));
                    }
                    continue;
                }

                // Complete any active list when we encounter a non-list item
                if (currentList != null && currentListProperty != null)
                {
                    result[currentListProperty] = currentList;
                    currentList = null;
                    currentListProperty = null;
                }

                // Handle top-level sections and properties
                if (indent == 0 && trimmed.EndsWith(YAML_PROPERTY_SEPARATOR) && !trimmed.Contains(YAML_SPACE))
                {
                    string sectionName = trimmed.TrimEnd(YAML_PROPERTY_SEPARATOR.ToCharArray());

                    if (sectionName == SECTION_SYS || sectionName == SECTION_DATS)
                    {
                        handleTopLevelSection(sectionName, ref currentSection, ref sectionIndent,
                                            ref currentSystemName, ref currentSystemDict,
                                            ref datsDict, sysDict, result, indent);
                    }
                    else
                    {
                        handleRootProperty(sectionName, lines, lineIndex, result,
                                         ref currentListProperty, ref currentList);
                    }
                    continue;
                }

                // Handle system names under sys section
                if (currentSection == SECTION_SYS && indent > sectionIndent &&
                    trimmed.EndsWith(YAML_PROPERTY_SEPARATOR) && !trimmed.Contains(YAML_SPACE) && indent == SYSTEM_INDENT_LEVEL)
                {
                    currentSystemName = trimmed.TrimEnd(YAML_PROPERTY_SEPARATOR.ToCharArray());
                    currentSystemDict = new Dictionary<object, object>();
                    sysDict[currentSystemName] = currentSystemDict;
                    systemIndent = indent;
                    continue;
                }

                // Handle property assignments
                if (trimmed.Contains(YAML_PROPERTY_SEPARATOR))
                {
                    handlePropertyAssignment(trimmed, indent, sectionIndent, systemIndent,
                                           currentSection, currentSystemDict, datsDict, result);
                }
            }

            // Handle any remaining list at the end of file
            if (currentList != null && currentListProperty != null)
            {
                result[currentListProperty] = currentList;
            }

            return result;
        }

        /// <summary>
        /// Deserializes fix YAML files (GameCube/Wii, PS3, Dreamcast) to Dictionary<object, object> structure
        /// These files have simpler flat or two-level hierarchical structures
        /// </summary>
        public static Dictionary<object, object> DeserializeFixFile(string yamlContent)
        {
            Dictionary<object, object> result = new Dictionary<object, object>();
            string[] lines = yamlContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Stack of (dict, indent) pairs
            Stack<(Dictionary<object, object> dict, int indent)> contextStack = new Stack<(Dictionary<object, object> dict, int indent)>();
            contextStack.Push((result, 0));

            string currentListProperty = null;
            List<object> currentList = null;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed))
                    continue;

                int indent = line.Length - line.TrimStart().Length;

                // Pop stack to correct parent
                while (contextStack.Count > 1 && indent <= contextStack.Peek().indent)
                    contextStack.Pop();

                Dictionary<object, object> currentDict = contextStack.Peek().dict;

                // Top-level section (no spaces, ends with colon)
                if (indent == 0 && trimmed.EndsWith(":") && !trimmed.Contains(" "))
                {
                    if (currentList != null && currentListProperty != null)
                    {
                        currentDict[currentListProperty] = currentList;
                        currentList = null;
                        currentListProperty = null;
                    }

                    string section = trimmed.TrimEnd(':');
                    Dictionary<object, object> sectionDict = new Dictionary<object, object>();
                    currentDict[section] = sectionDict;
                    contextStack.Push((sectionDict, indent));
                    continue;
                }

                // Sub-section (indented, ends with colon)
                if (trimmed.EndsWith(":"))
                {
                    if (currentList != null && currentListProperty != null)
                    {
                        currentDict[currentListProperty] = currentList;
                        currentList = null;
                        currentListProperty = null;
                    }

                    string subSection = trimmed.TrimEnd(':');
                    Dictionary<object, object> subDict = new Dictionary<object, object>();
                    currentDict[subSection] = subDict;
                    contextStack.Push((subDict, indent));
                    continue;
                }

                // Property assignment
                if (trimmed.Contains(":"))
                {
                    int colonIndex = trimmed.IndexOf(':');
                    string key = trimmed.Substring(0, colonIndex).Trim();
                    string value = trimmed.Substring(colonIndex + 1).Trim();

                    // Remove inline comments
                    int commentIndex = value.IndexOf("#");
                    if (commentIndex >= 0)
                        value = value.Substring(0, commentIndex).Trim();

                    if (value.StartsWith("[") && value.EndsWith("]"))
                    {
                        currentDict[key] = parseFixValue(value, lines, lineIndex);
                    }
                    else if (string.IsNullOrEmpty(value) && hasArrayItemsFollowing(lines, lineIndex))
                    {
                        currentListProperty = key;
                        currentList = new List<object>();
                    }
                    else if (string.IsNullOrEmpty(value) && hasIndentedKeyValueFollowing(lines, lineIndex))
                    {
                        Dictionary<object, object> dict = new Dictionary<object, object>();
                        int baseIndent = indent;
                        int i = lineIndex + 1;
                        for (; i < lines.Length; i++)
                        {
                            string nextLine = lines[i];
                            string nextTrimmed = nextLine.Trim();
                            if (nextTrimmed.StartsWith("#") || string.IsNullOrEmpty(nextTrimmed))
                                continue;

                            int nextIndent = nextLine.Length - nextLine.TrimStart().Length;
                            if (nextIndent <= baseIndent)
                                break;

                            int nextColon = nextTrimmed.IndexOf(':');
                            if (nextColon > 0)
                            {
                                string subKey = nextTrimmed.Substring(0, nextColon).Trim();
                                string subValue = nextTrimmed.Substring(nextColon + 1).Trim();
                                int subComment = subValue.IndexOf("#");
                                if (subComment >= 0)
                                    subValue = subValue.Substring(0, subComment).Trim();
                                dict[subKey] = parseFixValue(subValue, null, 0);
                            }
                        }
                        currentDict[key] = dict;
                        lineIndex = i - 1;
                    }
                    else
                    {
                        currentDict[key] = parseFixValue(value, lines, lineIndex);
                    }
                    continue;
                }

                // List item
                if (trimmed.StartsWith("-"))
                {
                    string listValue = trimmed.Substring(1).Trim();
                    if (currentList != null && !string.IsNullOrEmpty(listValue))
                        currentList.Add(parseFixValue(listValue, null, 0));
                    continue;
                }

                // End of list
                if (currentList != null && currentListProperty != null)
                {
                    currentDict[currentListProperty] = currentList;
                    currentList = null;
                    currentListProperty = null;
                }
            }

            // Handle any remaining list at the end of file
            if (currentList != null && currentListProperty != null)
            {
                contextStack.Peek().dict[currentListProperty] = currentList;
            }

            return result;
        }

        private static bool hasIndentedKeyValueFollowing(string[] lines, int currentIndex)
        {
            for (int i = currentIndex + 1; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith(YAML_COMMENT_PREFIX) || string.IsNullOrEmpty(trimmed))
                    continue;

                int indent = lines[i].Length - lines[i].TrimStart().Length;
                if (indent == 0)
                    return false;

                // Looks like a key: value pair
                if (trimmed.Contains(YAML_PROPERTY_SEPARATOR))
                    return true;

                // If it's not a key-value, not a valid dict
                return false;
            }
            return false;
        }

        private static void handleTopLevelSection(string sectionName, ref string currentSection,
            ref int sectionIndent, ref string currentSystemName,
            ref Dictionary<object, object> currentSystemDict, ref Dictionary<object, object> datsDict,
            Dictionary<object, object> sysDict, Dictionary<object, object> result, int indent)
        {
            currentSection = sectionName;
            currentSystemName = EMPTY_STRING;
            currentSystemDict = null;
            sectionIndent = indent;

            if (sectionName == SECTION_SYS)
            {
                result[SECTION_SYS] = sysDict;
            }
            else if (sectionName == SECTION_DATS)
            {
                datsDict = new Dictionary<object, object>();
                result[SECTION_DATS] = datsDict;
            }
        }

        private static void handleRootProperty(string propertyName, string[] lines, int lineIndex,
            Dictionary<object, object> result, ref string currentListProperty, ref List<object> currentList)
        {
            if (hasListItemsFollowing(lines, lineIndex))
            {
                currentListProperty = propertyName;
                currentList = new List<object>();
            }
            else
            {
                result[propertyName] = EMPTY_STRING;
            }
        }

        private static void handlePropertyAssignment(string trimmed, int indent, int sectionIndent,
            int systemIndent, string currentSection, Dictionary<object, object> currentSystemDict,
            Dictionary<object, object> datsDict, Dictionary<object, object> result)
        {
            int colonIndex = trimmed.IndexOf(YAML_PROPERTY_SEPARATOR);
            string key = trimmed.Substring(0, colonIndex).Trim();
            string value = trimmed.Substring(colonIndex + 1).Trim();

            // Remove inline comments
            int commentIndex = value.IndexOf(YAML_COMMENT_PREFIX);
            if (commentIndex >= 0)
            {
                value = value.Substring(0, commentIndex).Trim();
            }

            object parsedValue = parseValue(value);

            // Determine where to place the property based on context and indentation
            if (indent == 0)
            {
                result[key] = parsedValue;
            }
            else if (currentSection == SECTION_DATS && datsDict != null && indent > sectionIndent)
            {
                datsDict[key] = parsedValue;
            }
            else if (currentSection == SECTION_SYS && currentSystemDict != null && indent > systemIndent)
            {
                currentSystemDict[key] = parsedValue;
            }
        }

        /// <summary>
        /// Check if there are list items following this line
        /// </summary>
        private static bool hasListItemsFollowing(string[] lines, int currentIndex)
        {
            for (int i = currentIndex + 1; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith(YAML_COMMENT_PREFIX) || string.IsNullOrEmpty(trimmed))
                    continue;

                return trimmed.StartsWith(YAML_LIST_PREFIX);
            }
            return false;
        }

        /// <summary>
        /// Check if there are array items following this line
        /// </summary>
        private static bool hasArrayItemsFollowing(string[] lines, int currentIndex)
        {
            for (int i = currentIndex + 1; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith(YAML_COMMENT_PREFIX) || string.IsNullOrEmpty(trimmed))
                    continue;

                return trimmed.StartsWith(YAML_LIST_PREFIX);
            }
            return false;
        }

        /// <summary>
        /// Parses a value string to appropriate type (mimics YamlDotNet behavior for SystemSettings compatibility)
        /// </summary>
        private static object parseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return EMPTY_STRING; // Return empty string, not null - SystemSettings expects strings
            }

            // Handle quoted strings
            if ((value.StartsWith(YAML_DOUBLE_QUOTE) && value.EndsWith(YAML_DOUBLE_QUOTE)) ||
                (value.StartsWith(YAML_SINGLE_QUOTE) && value.EndsWith(YAML_SINGLE_QUOTE)))
            {
                return value.Substring(1, value.Length - 2);
            }

            // Handle numbers - only for parallelism property compatibility
            if (int.TryParse(value, out int intVal))
            {
                return intVal;
            }

            // Keep all other values as strings (including y/n, true/false)
            // SystemSettings does manual string conversion: getVal(Param.r)?.ToLower() == "y"
            return value;
        }

        /// <summary>
        /// Parse values for fix files - handles complex structures like lists and dictionaries
        /// </summary>
        private static object parseFixValue(string value, string[] lines, int currentLineIndex)
        {
            if (string.IsNullOrEmpty(value))
            {
                return EMPTY_STRING;
            }

            // Handle quoted strings
            if ((value.StartsWith(YAML_DOUBLE_QUOTE) && value.EndsWith(YAML_DOUBLE_QUOTE)) ||
                (value.StartsWith(YAML_SINGLE_QUOTE) && value.EndsWith(YAML_SINGLE_QUOTE)))
            {
                return value.Substring(1, value.Length - 2);
            }

            // Handle inline lists like [ { 0: ae4 } ]
            if (value.StartsWith("[") && value.EndsWith("]"))
            {
                return parseInlineList(value);
            }

            // Handle inline objects like { 0: ae4 }
            if (value.StartsWith("{") && value.EndsWith("}"))
            {
                return parseInlineObject(value);
            }

            // Handle hex numbers
            if (value.All(c => "0123456789ABCDEFabcdef".Contains(c)))
            {
                return value; // Keep as string for hex values
            }

            // Handle regular numbers
            if (int.TryParse(value, out int intVal))
            {
                return intVal;
            }

            // Default to string
            return value;
        }

        /// <summary>
        /// Parse inline YAML list like [ 00375EA0,045A0447,0579542A,... ]
        /// Enhanced to handle hex values without spaces after commas
        /// </summary>
        private static List<object> parseInlineList(string listStr)
        {
            List<object> result = new List<object>();
            string content = listStr.Substring(1, listStr.Length - 2).Trim();

            if (string.IsNullOrEmpty(content))
                return result;

            // Enhanced parser for comma-separated items (handle no spaces after commas)
            string[] items = splitInlineListItems(content);
            foreach (string item in items)
            {
                string trimmedItem = item.Trim();
                if (trimmedItem.StartsWith("{") && trimmedItem.EndsWith("}"))
                {
                    result.Add(parseInlineObject(trimmedItem));
                }
                else
                {
                    result.Add(parseFixValue(trimmedItem, null, 0));
                }
            }

            return result;
        }

        /// <summary>
        /// Parse inline YAML object like { 0: ae4, 2: b10 }
        /// </summary>
        private static Dictionary<object, object> parseInlineObject(string objStr)
        {
            Dictionary<object, object> result = new Dictionary<object, object>();
            string content = objStr.Substring(1, objStr.Length - 2).Trim();

            if (string.IsNullOrEmpty(content))
                return result;

            // Simple parser for comma-separated key-value pairs
            string[] pairs = splitInlineObjectPairs(content);
            foreach (string pair in pairs)
            {
                int colonIndex = pair.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = pair.Substring(0, colonIndex).Trim();
                    string value = pair.Substring(colonIndex + 1).Trim();

                    result[parseFixValue(key, null, 0)] = parseFixValue(value, null, 0);
                }
            }

            return result;
        }

        /// <summary>
        /// Enhanced splitting to handle hex arrays without spaces like [ 00375EA0,045A0447,... ]
        /// </summary>
        private static string[] splitInlineListItems(string content)
        {
            List<string> items = new List<string>();
            StringBuilder current = new System.Text.StringBuilder();
            int braceDepth = 0;
            int bracketDepth = 0;
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (!inQuotes && (c == '"' || c == '\''))
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (inQuotes && c == quoteChar)
                {
                    inQuotes = false;
                }
                else if (!inQuotes)
                {
                    if (c == '{')
                        braceDepth++;
                    else if (c == '}')
                        braceDepth--;
                    else if (c == '[')
                        bracketDepth++;
                    else if (c == ']')
                        bracketDepth--;
                    else if (c == ',' && braceDepth == 0 && bracketDepth == 0)
                    {
                        items.Add(current.ToString());
                        current.Clear();
                        continue;
                    }
                }

                current.Append(c);
            }

            if (current.Length > 0)
                items.Add(current.ToString());

            return items.ToArray();
        }

        /// <summary>
        /// Split inline object pairs respecting nested structures
        /// </summary>
        private static string[] splitInlineObjectPairs(string content)
        {
            List<string> pairs = new List<string>();
            StringBuilder current = new System.Text.StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (!inQuotes && (c == '"' || c == '\''))
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (inQuotes && c == quoteChar)
                {
                    inQuotes = false;
                }
                else if (!inQuotes && c == ',' && !isInsideNestedStructure(content, i))
                {
                    pairs.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                pairs.Add(current.ToString());

            return pairs.ToArray();
        }

        /// <summary>
        /// Check if we're inside a nested structure at the given position
        /// </summary>
        private static bool isInsideNestedStructure(string content, int position)
        {
            int braceDepth = 0;
            int bracketDepth = 0;

            for (int i = 0; i < position; i++)
            {
                char c = content[i];
                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;
            }

            return braceDepth > 0 || bracketDepth > 0;
        }
    }
}