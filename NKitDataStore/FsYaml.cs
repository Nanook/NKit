using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NKitDataStore
{
    /// <summary>
    /// Represents a valid YAML filesystem tree for one or more filesystems within a disc image.
    ///
    /// Format (valid YAML, AOT-safe, no external dependencies):
    ///   version: 1.0
    ///   fs:
    ///     dirName:
    ///       fileName: [offsetStart, size, xxhash64, crc32]
    ///
    /// - Directory:        name:             � no value, children indented below
    /// - System directory: /name:            � leading '/' marks as system (hidden unless system mode)
    /// - File:             name: [offsetStart, size, xxhash64, crc32] � 4 values, leaf node
    ///
    /// System items use a leading '/' prefix on the name. '/' can never appear at the start
    /// of a real file or directory name so it is an unambiguous marker. When a directory is
    /// marked as system, all of its contents are implicitly system and do not need individual
    /// marking. The VFS hides '/'-prefixed directories and their entire subtree unless system
    /// mode is enabled.
    ///
    /// Numeric values are decimal. Names containing special characters are double-quoted.
    /// Indentation is 2 spaces per nesting level.
    ///
    /// Legacy format (pre-1.0) is still supported for reading:
    ///   fsName: [areaOffset]
    ///     ...
    /// </summary>
    public class FsYaml
    {
        private const int _IndentSize = 2;

        /// <summary>
        /// Top-level filesystem entries. Each represents a filesystem root (e.g., a disc partition).
        /// A node's Offset maps back to the area table for full metadata retrieval.
        /// </summary>
        public List<FsYamlNode> FileSystems { get; } = new List<FsYamlNode>();

        /// <summary>
        /// Image File System entries — references to files in child images.
        /// Used by TmdAppFolder to point to files reconstructed from child image area records.
        /// </summary>
        public List<FsYamlIfsEntry> ImageFileSystems { get; } = new List<FsYamlIfsEntry>();

        /// <summary>
        /// Adds a filesystem root with the specified name and area offset.
        /// Returns the created node so child directories and files can be added fluently.
        /// </summary>
        /// <param name="name">Display name of the filesystem (e.g., "Game", "Update").</param>
        /// <param name="areaOffset">The area offset in the image, used to look up the area table.</param>
        public FsYamlNode AddFileSystem(string name, long areaOffset)
        {
            FsYamlNode fs = FsYamlNode.CreateDirectory(name, areaOffset);
            FileSystems.Add(fs);
            return fs;
        }

        /// <summary>
        /// Adds an ifs entry referencing a file in a child image.
        /// </summary>
        /// <param name="fileName">The file name (e.g., "00000001.app").</param>
        /// <param name="imageId">Lookup handle: first child image containing this area.</param>
        /// <param name="size">File size in bytes.</param>
        public void AddIfsEntry(string fileName, long imageId, long size) => ImageFileSystems.Add(new FsYamlIfsEntry { FileName = fileName, ImageId = imageId, Size = size });

        /// <summary>
        /// Serializes the filesystem tree to valid YAML text.
        /// </summary>
        public string ToYaml()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("version: 1.0");
            sb.AppendLine("fs:");

            foreach (FsYamlNode fs in FileSystems)
            {
                if (fs.Name == ".")
                {
                    // Inline root: write children directly under fs:
                    appendChildren(sb, fs, 1);
                }
                else
                {
                    // Named root: write as a directory under fs:
                    sb.Append(new string(' ', _IndentSize));
                    appendEscapedName(sb, fs.Name);
                    sb.Append(':');
                    sb.AppendLine();
                    appendChildren(sb, fs, 2);
                }
            }

            if (ImageFileSystems.Count > 0)
            {
                sb.AppendLine("ifs:");
                string indent = new string(' ', _IndentSize);
                foreach (FsYamlIfsEntry entry in ImageFileSystems)
                {
                    sb.Append(indent);
                    appendEscapedName(sb, entry.FileName);
                    sb.Append(": [");
                    sb.Append(entry.ImageId);
                    sb.Append(", ");
                    sb.Append(entry.Size);
                    sb.Append(']');
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Serializes to UTF-8 encoded bytes.
        /// </summary>
        public byte[] ToBytes() => Encoding.UTF8.GetBytes(ToYaml());

        /// <summary>
        /// Deserializes from YAML text. Supports both the current format (version 1.0)
        /// and the legacy format (pre-1.0) for backward compatibility.
        /// </summary>
        public static FsYaml FromYaml(string yaml)
        {
            if (string.IsNullOrEmpty(yaml))
                return new FsYaml();

            string[] lines = yaml.Split('\n');

            // Detect format by checking if the first non-empty line starts with "version:"
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.Length == 0 || (trimmed.Length > 0 && trimmed[0] == '#'))
                    continue;
                if (trimmed.StartsWith("version:"))
                    return fromYamlV1(lines);
                break;
            }

            return fromYamlLegacy(lines);
        }

        /// <summary>
        /// Parses the version 1.0 format: version header followed by fs: root,
        /// and optionally an ifs: section for image file system entries.
        /// All children under fs: are placed into a single inline "." filesystem root.
        /// </summary>
        private static FsYaml fromYamlV1(string[] lines)
        {
            FsYaml result = new FsYaml();

            // Find the "fs:" line
            int fsLineIndex = -1;
            int fsIndent = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    line = line.Substring(0, line.Length - 1);

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ')
                    indent++;
                if (indent == line.Length)
                    continue;

                string content = line.Substring(indent);
                parseLine(content, out string? name, out _);
                if (name == "fs")
                {
                    fsLineIndex = i;
                    fsIndent = indent;
                    break;
                }
            }

            if (fsLineIndex < 0)
            {
                // No fs: section — still try to parse ifs: section
                parseIfsSection(lines, 0, result);
                return result;
            }

            int baseIndent = fsIndent + _IndentSize;

            FsYamlNode root = FsYamlNode.CreateDirectory(".", 0);
            result.FileSystems.Add(root);

            Stack<(int indent, FsYamlNode node)> stack = new Stack<(int, FsYamlNode)>();
            stack.Push((-1, root));

            for (int i = fsLineIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    line = line.Substring(0, line.Length - 1);
                if (line.Length == 0)
                    continue;

                int rawIndent = 0;
                while (rawIndent < line.Length && line[rawIndent] == ' ')
                    rawIndent++;
                if (rawIndent == line.Length)
                    continue;

                // Lines at or before the fs: indent level end the fs: block
                if (rawIndent <= fsIndent)
                    break;

                int normalizedIndent = rawIndent - baseIndent;

                string content = line.Substring(rawIndent);
                parseLine(content, out string? name, out string[]? values);

                if (string.IsNullOrEmpty(name))
                    continue;

                // Detect system marker: leading '/' in name
                bool isSystem = name.Length > 0 && name[0] == '/';
                if (isSystem)
                    name = name.Substring(1);

                while (stack.Count > 0 && stack.Peek().indent >= normalizedIndent)
                    stack.Pop();

                if (stack.Count == 0)
                    continue;

                FsYamlNode parent = stack.Peek().node;

                if (values != null && values.Length >= 4)
                {
                    tryParseLong(values[0], out long offset);
                    tryParseLong(values[1], out long size);
                    tryParseULong(values[2], out ulong xxHash64);
                    tryParseUInt(values[3], out uint crc32);
                    // Strip multi-extent display suffix " /N" (e.g., "file.m2ts /1" → "file.m2ts")
                    name = stripExtentSuffix(name);
                    parent.AddFile(name, offset, size, xxHash64, crc32, isSystem);
                }
                else
                {
                    FsYamlNode dir = parent.AddDirectory(name, isSystem);
                    stack.Push((normalizedIndent, dir));
                }
            }

            // Parse the ifs: section (if present)
            parseIfsSection(lines, 0, result);

            return result;
        }

        /// <summary>
        /// Scans for an "ifs:" line starting from <paramref name="startLine"/> and parses
        /// all indented children as <see cref="FsYamlIfsEntry"/> objects.
        /// Malformed entries (wrong value count, non-numeric imageId/size) are skipped with a warning.
        /// </summary>
        private static void parseIfsSection(string[] lines, int startLine, FsYaml result)
        {
            int ifsLineIndex = -1;
            int ifsIndent = 0;

            for (int i = startLine; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    line = line.Substring(0, line.Length - 1);

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ')
                    indent++;
                if (indent == line.Length)
                    continue;

                string content = line.Substring(indent);
                parseLine(content, out string? name, out _);
                if (name == "ifs")
                {
                    ifsLineIndex = i;
                    ifsIndent = indent;
                    break;
                }
            }

            if (ifsLineIndex < 0)
                return;

            for (int i = ifsLineIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    line = line.Substring(0, line.Length - 1);
                if (line.Length == 0)
                    continue;

                int rawIndent = 0;
                while (rawIndent < line.Length && line[rawIndent] == ' ')
                    rawIndent++;
                if (rawIndent == line.Length)
                    continue;

                // Lines at or before the ifs: indent level end the ifs: block
                if (rawIndent <= ifsIndent)
                    break;

                string content = line.Substring(rawIndent);
                parseLine(content, out string? name, out string[]? values);

                if (string.IsNullOrEmpty(name))
                    continue;

                // ifs entries must have exactly 2 values: [imageId, size]
                if (values == null || values.Length != 2)
                {
                    Debug.WriteLine($"FsYaml: Skipping malformed ifs entry '{name}': expected 2 values [imageId, size], got {values?.Length ?? 0}");
                    continue;
                }

                if (!tryParseLong(values[0], out long imageId))
                {
                    Debug.WriteLine($"FsYaml: Skipping malformed ifs entry '{name}': non-numeric imageId '{values[0]}'");
                    continue;
                }

                if (!tryParseLong(values[1], out long size))
                {
                    Debug.WriteLine($"FsYaml: Skipping malformed ifs entry '{name}': non-numeric size '{values[1]}'");
                    continue;
                }

                result.ImageFileSystems.Add(new FsYamlIfsEntry
                {
                    FileName = name,
                    ImageId = imageId,
                    Size = size
                });
            }
        }

        /// <summary>
        /// Parses the legacy format where filesystem roots appear at indent 0
        /// as "name: [areaOffset]".
        /// </summary>
        private static FsYaml fromYamlLegacy(string[] lines)
        {
            FsYaml result = new FsYaml();

            Stack<(int indent, FsYamlNode node)> stack = new Stack<(int, FsYamlNode)>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (line.Length > 0 && line[line.Length - 1] == '\r')
                    line = line.Substring(0, line.Length - 1);
                if (line.Length == 0)
                    continue;

                int rawIndent = 0;
                while (rawIndent < line.Length && line[rawIndent] == ' ')
                    rawIndent++;
                if (rawIndent == line.Length)
                    continue;

                string content = line.Substring(rawIndent);
                parseLine(content, out string? name, out string[]? values);

                if (string.IsNullOrEmpty(name))
                    continue;

                if (rawIndent == 0)
                {
                    long areaOffset = 0;
                    if (values != null && values.Length >= 1)
                        tryParseLong(values[0], out areaOffset);

                    FsYamlNode fs = FsYamlNode.CreateDirectory(name, areaOffset);
                    result.FileSystems.Add(fs);
                    stack.Clear();
                    stack.Push((-1, fs));
                }
                else
                {
                    while (stack.Count > 0 && stack.Peek().indent >= rawIndent)
                        stack.Pop();

                    if (stack.Count == 0)
                        continue;

                    FsYamlNode parent = stack.Peek().node;

                    if (values != null && values.Length >= 4)
                    {
                        tryParseLong(values[0], out long offset);
                        tryParseLong(values[1], out long size);
                        tryParseULong(values[2], out ulong xxHash64);
                        tryParseUInt(values[3], out uint crc32);
                        bool isSystem = values.Length >= 5 && int.TryParse(values[4], out int flags) && flags != 0;
                        // Strip multi-extent display suffix " /N"
                        name = stripExtentSuffix(name);
                        parent.AddFile(name, offset, size, xxHash64, crc32, isSystem);
                    }
                    else
                    {
                        bool isSystem = values != null && values.Length >= 1 && int.TryParse(values[0], out int flags) && flags != 0;
                        FsYamlNode dir = parent.AddDirectory(name, isSystem);
                        stack.Push((rawIndent, dir));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Deserializes from UTF-8 encoded bytes.
        /// </summary>
        public static FsYaml FromBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return new FsYaml();
            return FromYaml(Encoding.UTF8.GetString(data));
        }

        private static void appendChildren(StringBuilder sb, FsYamlNode node, int depth)
        {
            if (node.Children == null)
                return;

            string indent = new string(' ', depth * _IndentSize);

            // Track consecutive same-name file entries for multi-extent display suffixing
            int i = 0;
            while (i < node.Children.Count)
            {
                FsYamlNode child = node.Children[i];
                sb.Append(indent);
                string outputName = child.IsSystem ? "/" + child.Name : child.Name;

                if (child.IsFile)
                {
                    // Count consecutive same-name file entries (multi-extent chain)
                    int chainStart = i;
                    int chainLength = 1;
                    while (i + chainLength < node.Children.Count)
                    {
                        FsYamlNode next = node.Children[i + chainLength];
                        if (!next.IsFile || next.Name != child.Name)
                            break;
                        chainLength++;
                    }

                    if (chainLength > 1)
                    {
                        // Multi-extent chain: first entry uses plain name, subsequent get /1, /2, ...
                        appendEscapedName(sb, outputName);
                        appendFileValue(sb, child);

                        for (int j = 1; j < chainLength; j++)
                        {
                            FsYamlNode extent = node.Children[i + j];
                            sb.Append(indent);
                            string extentOutputName = (extent.IsSystem ? "/" + extent.Name : extent.Name) + " /" + j;
                            appendEscapedName(sb, extentOutputName);
                            appendFileValue(sb, extent);
                        }
                        i += chainLength;
                    }
                    else
                    {
                        // Single file entry
                        appendEscapedName(sb, outputName);
                        appendFileValue(sb, child);
                        i++;
                    }
                }
                else
                {
                    appendEscapedName(sb, outputName);
                    sb.Append(':');
                    sb.AppendLine();
                    appendChildren(sb, child, depth + 1);
                    i++;
                }
            }
        }

        private static void appendFileValue(StringBuilder sb, FsYamlNode child)
        {
            sb.Append(": [0x");
            sb.Append(child.Offset.ToString("X"));
            sb.Append(", 0x");
            sb.Append(child.Size.ToString("X"));
            sb.Append(", ");
            sb.Append(child.XxHash64);
            sb.Append(", ");
            sb.Append(child.Crc32);
            sb.Append(']');
            sb.AppendLine();
        }

        private static void appendEscapedName(StringBuilder sb, string name)
        {
            if (needsQuoting(name))
            {
                sb.Append('"');
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if (c == '"' || c == '\\')
                        sb.Append('\\');
                    sb.Append(c);
                }
                sb.Append('"');
            }
            else
            {
                sb.Append(name);
            }
        }

        private static bool needsQuoting(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;
            if (name[0] == ' ' || name[name.Length - 1] == ' ')
                return true;
            // Names starting with a digit must be quoted to avoid YAML number interpretation
            if (name[0] >= '0' && name[0] <= '9')
                return true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ':' || c == '[' || c == ']' || c == '#' || c == '"' || c == '\'' || c == '\\' || c == '{' || c == '}')
                    return true;
            }
            return false;
        }

        private static void parseLine(string content, out string? name, out string[]? values)
        {
            values = null;
            name = null;

            if (string.IsNullOrEmpty(content))
                return;

            int pos = 0;

            // Parse name (possibly double-quoted)
            if (content[0] == '"')
            {
                pos = 1;
                StringBuilder nameSb = new StringBuilder();
                while (pos < content.Length)
                {
                    char c = content[pos];
                    if (c == '\\' && pos + 1 < content.Length)
                    {
                        nameSb.Append(content[pos + 1]);
                        pos += 2;
                    }
                    else if (c == '"')
                    {
                        pos++;
                        break;
                    }
                    else
                    {
                        nameSb.Append(c);
                        pos++;
                    }
                }
                name = nameSb.ToString();
            }
            else
            {
                // Unquoted: name extends to first ':'
                int colonPos = content.IndexOf(':');
                if (colonPos < 0)
                {
                    name = content.Trim();
                    return;
                }
                name = content.Substring(0, colonPos);
                pos = colonPos;
            }

            // Skip ':' and optional whitespace
            if (pos < content.Length && content[pos] == ':')
                pos++;
            while (pos < content.Length && content[pos] == ' ')
                pos++;

            // Parse optional flow sequence [v1, v2, ...]
            if (pos < content.Length && content[pos] == '[')
            {
                pos++; // skip '['
                int closePos = content.IndexOf(']', pos);
                string inner = closePos >= 0
                    ? content.Substring(pos, closePos - pos)
                    : content.Substring(pos);

                if (inner.Length == 0)
                    return;

                string[] parts = inner.Split(',');
                values = new string[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    values[i] = parts[i].Trim();
            }
        }

        /// <summary>
        /// Strips the multi-extent display suffix " /N" from a filename during YAML import.
        /// For example, "00001.m2ts /2" becomes "00001.m2ts".
        /// This suffix is added by ToYaml() for human readability but is not part of the actual filename.
        /// </summary>
        private static string stripExtentSuffix(string name)
        {
            int slashPos = name.LastIndexOf(" /");
            if (slashPos < 0)
                return name;

            // Verify everything after " /" is digits
            for (int i = slashPos + 2; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i]))
                    return name; // Not an extent suffix
            }

            return slashPos + 2 < name.Length ? name.Substring(0, slashPos) : name;
        }

        /// <summary>
        /// Parses a numeric string that may be decimal or hex (with 0x prefix).
        /// </summary>
        private static bool tryParseLong(string value, out long result)
        {
            if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out result);
            return long.TryParse(value, out result);
        }

        /// <summary>
        /// Parses a numeric string that may be decimal or hex (with 0x prefix).
        /// </summary>
        private static bool tryParseULong(string value, out ulong result)
        {
            if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ulong.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out result);
            return ulong.TryParse(value, out result);
        }

        /// <summary>
        /// Parses a numeric string that may be decimal or hex (with 0x prefix).
        /// </summary>
        private static bool tryParseUInt(string value, out uint result)
        {
            if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out result);
            return uint.TryParse(value, out result);
        }
    }

    /// <summary>
    /// A node in the filesystem YAML tree. Represents a filesystem root, directory, or file.
    /// <list type="bullet">
    ///   <item><description>Filesystem root / Directory: Children is non-null. Offset is the area offset.</description></item>
    ///   <item><description>File (leaf): Children is null. Offset is the offsetStart from the offset table.</description></item>
    /// </list>
    /// Use <see cref="CreateDirectory"/> or <see cref="CreateFile"/> factory methods,
    /// or the fluent <see cref="AddDirectory"/> / <see cref="AddFile"/> methods on a parent node.
    /// </summary>
    public class FsYamlNode
    {
        /// <summary>Name of the file or directory.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// For filesystem roots: the area offset (maps to AreaRecord.Offset in the area table).
        /// For files: the offsetStart value from the offset table.
        /// </summary>
        public long Offset { get; set; }

        /// <summary>File size in bytes. Meaningful for file nodes only.</summary>
        public long Size { get; set; }

        /// <summary>XXHash64 of file content. Meaningful for file nodes only.</summary>
        public ulong XxHash64 { get; set; }

        /// <summary>CRC32 of file content. Meaningful for file nodes only.</summary>
        public uint Crc32 { get; set; }

        /// <summary>
        /// Indicates this node belongs to a system partition (e.g., SI or Update on WiiU).
        /// Used by VFS to toggle visibility with the --system flag.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// Child nodes. Non-null for directories and filesystem roots; null for file nodes.
        /// </summary>
        public List<FsYamlNode>? Children { get; private set; }

        /// <summary>True when this node is a file (leaf with no children).</summary>
        public bool IsFile => Children == null;

        /// <summary>True when this node is a directory or filesystem root.</summary>
        public bool IsDirectory => Children != null;

        private FsYamlNode() { }

        /// <summary>
        /// Creates a directory or filesystem root node.
        /// </summary>
        /// <param name="name">Directory name.</param>
        /// <param name="offset">Area offset for filesystem roots, or 0 for subdirectories.</param>
        /// <param name="isSystem">True if this node belongs to a system partition.</param>
        public static FsYamlNode CreateDirectory(string name, long offset = 0, bool isSystem = false)
        {
            return new FsYamlNode
            {
                Name = name ?? string.Empty,
                Offset = offset,
                IsSystem = isSystem,
                Children = new List<FsYamlNode>()
            };
        }

        /// <summary>
        /// Creates a file (leaf) node.
        /// </summary>
        /// <param name="name">File name.</param>
        /// <param name="offset">The offsetStart value from the offset table.</param>
        /// <param name="size">File size in bytes.</param>
        /// <param name="xxHash64">XXHash64 of file content.</param>
        /// <param name="crc32">CRC32 of file content.</param>
        /// <param name="isSystem">True if this node belongs to a system partition.</param>
        public static FsYamlNode CreateFile(string name, long offset, long size, ulong xxHash64, uint crc32, bool isSystem = false)
        {
            return new FsYamlNode
            {
                Name = name ?? string.Empty,
                Offset = offset,
                Size = size,
                XxHash64 = xxHash64,
                Crc32 = crc32,
                IsSystem = isSystem,
                Children = null
            };
        }

        /// <summary>
        /// Adds a child directory. Returns the new directory node for fluent tree building.
        /// </summary>
        public FsYamlNode AddDirectory(string name, bool isSystem = false)
        {
            if (Children == null)
                throw new InvalidOperationException("Cannot add children to a file node.");
            FsYamlNode dir = CreateDirectory(name, 0, isSystem);
            Children.Add(dir);
            return dir;
        }

        /// <summary>
        /// Adds a child file. Returns the created file node.
        /// </summary>
        public FsYamlNode AddFile(string name, long offset, long size, ulong xxHash64, uint crc32, bool isSystem = false)
        {
            if (Children == null)
                throw new InvalidOperationException("Cannot add children to a file node.");
            FsYamlNode file = CreateFile(name, offset, size, xxHash64, crc32, isSystem);
            Children.Add(file);
            return file;
        }

        /// <summary>
        /// Adds a file by its full path (e.g., "/sys/main.dol"), creating intermediate
        /// directory nodes as needed. The last segment is treated as the filename.
        /// </summary>
        /// <param name="fullPath">Full path using '/' as separator. Leading '/' is ignored.</param>
        /// <param name="offset">The offsetStart value from the offset table.</param>
        /// <param name="size">File size in bytes.</param>
        /// <param name="xxHash64">XXHash64 of file content.</param>
        /// <param name="crc32">CRC32 of file content.</param>
        /// <param name="isSystem">True if this node belongs to a system partition.</param>
        public FsYamlNode AddFileByPath(string fullPath, long offset, long size, ulong xxHash64, uint crc32, bool isSystem = false)
        {
            if (Children == null)
                throw new InvalidOperationException("Cannot add children to a file node.");
            if (string.IsNullOrEmpty(fullPath))
                throw new ArgumentException("Path cannot be empty.", nameof(fullPath));

            string[] segments = fullPath.Split('/');
            FsYamlNode current = this;

            // Navigate/create directories for all segments except the last (filename)
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                    continue; // skip empty from leading '/'

                FsYamlNode? existing = null;
                if (current.Children != null)
                {
                    foreach (FsYamlNode child in current.Children)
                    {
                        if (child.IsDirectory && child.Name == segment)
                        {
                            existing = child;
                            break;
                        }
                    }
                }
                // Intermediate directories are never marked as system — they are structural.
                // Only leaf files carry the system flag.
                current = existing ?? current.AddDirectory(segment);
            }

            string fileName = segments[segments.Length - 1];
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("Path must end with a filename.", nameof(fullPath));

            return current.AddFile(fileName, offset, size, xxHash64, crc32, isSystem);
        }

        public override string ToString()
        {
            string sys = IsSystem ? " [sys]" : "";
            if (IsFile)
                return $"{Name}: [{Offset}, {Size}, {XxHash64}, {Crc32}]{sys}";
            return $"{Name}: ({Children!.Count} children){sys}";
        }
    }

    /// <summary>
    /// Represents a file reference in the ifs (Image File System) section.
    /// Points to a file reconstructed from a child image's area records.
    /// The ImageId is a lookup handle — it identifies the first child image that
    /// contains this file as an area (matched via AreaValueType.FileName metadata).
    /// </summary>
    public class FsYamlIfsEntry
    {
        public string FileName { get; set; } = string.Empty;
        public long ImageId { get; set; }
        public long Size { get; set; }
    }
}