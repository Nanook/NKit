using System.Globalization;
using System.Text;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Tracks sequential shard compaction progress via a human-readable text file.
    /// 
    /// If this file exists, the set is mid-compaction and must not be used for normal
    /// operations until compaction completes or recovery runs.
    /// 
    /// The file format is designed to be:
    /// - Human-readable for debugging and support
    /// - Simple to parse without binary format concerns
    /// - Explicit about state so recovery is deterministic
    /// </summary>
    internal sealed class CompactProgressFile
    {
        /// <summary>Current file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>File format version.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>When the compaction was started (UTC).</summary>
        public DateTime Created { get; set; } = DateTime.UtcNow;

        /// <summary>Name of the set being compacted.</summary>
        public string SetName { get; set; } = "";

        /// <summary>FileIds of shards that need compaction.</summary>
        public List<int> ShardsToCompact { get; set; } = new();

        /// <summary>FileIds of shards where compaction is fully complete (original replaced).</summary>
        public List<int> CompletedShards { get; set; } = new();

        /// <summary>
        /// FileId of the shard currently being processed, or null if between shards.
        /// If set, a .tmp file may exist and needs recovery decision.
        /// </summary>
        public int? PendingShard { get; set; }

        /// <summary>
        /// Expected compacted sizes for each shard (FileId → expected bytes).
        /// Used during recovery to determine if the index was committed for a shard.
        /// </summary>
        public Dictionary<int, long> ExpectedSizes { get; set; } = new();

        /// <summary>
        /// Gets the progress file path for a set.
        /// </summary>
        public static string GetPath(string indexFilePath)
        {
            return indexFilePath + DataStore.ExtensionCompactProgress;
        }

        /// <summary>
        /// Checks if a progress file exists for the given set.
        /// </summary>
        public static bool Exists(string indexFilePath)
        {
            return File.Exists(GetPath(indexFilePath));
        }

        /// <summary>
        /// Deletes the progress file if it exists.
        /// </summary>
        public static void Delete(string indexFilePath)
        {
            string path = GetPath(indexFilePath);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Writes the progress file to disk, flushing to ensure durability.
        /// </summary>
        public void Write(string indexFilePath)
        {
            string path = GetPath(indexFilePath);
            string content = serialize();

            // Write to temp then rename for atomicity
            string tempPath = path + DataStore.ExtensionTmp;
            try
            {
                File.WriteAllText(tempPath, content, Encoding.UTF8);
                // Flush to disk
                using (FileStream fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    fs.Flush(flushToDisk: true);
                }
                AtomicFileOps.ReplaceFile(tempPath, path);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Reads a progress file from disk. Returns null if the file doesn't exist
        /// or is corrupt/unreadable.
        /// </summary>
        public static CompactProgressFile? Read(string indexFilePath)
        {
            string path = GetPath(indexFilePath);
            if (!File.Exists(path))
                return null;

            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                return parse(content);
            }
            catch
            {
                // Corrupt or unreadable — treat as no progress file
                return null;
            }
        }

        /// <summary>
        /// Marks a shard as having its .tmp file ready for potential promotion.
        /// Call this AFTER the .tmp is fully written and flushed.
        /// </summary>
        public void SetPendingShard(int fileId, string indexFilePath)
        {
            this.PendingShard = fileId;
            Write(indexFilePath);
        }

        /// <summary>
        /// Marks a shard as fully complete (original replaced with compacted version).
        /// </summary>
        public void MarkShardComplete(int fileId, string indexFilePath)
        {
            if (this.PendingShard == fileId)
                this.PendingShard = null;

            if (!this.CompletedShards.Contains(fileId))
                this.CompletedShards.Add(fileId);

            Write(indexFilePath);
        }

        /// <summary>
        /// Gets the list of shards that still need to be compacted.
        /// </summary>
        public List<int> GetRemainingShardsToCompact()
        {
            return this.ShardsToCompact
                .Where(id => !this.CompletedShards.Contains(id))
                .ToList();
        }

        /// <summary>
        /// Checks if all shards have been compacted.
        /// </summary>
        public bool IsComplete()
        {
            return this.ShardsToCompact.All(id => this.CompletedShards.Contains(id));
        }

        private string serialize()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("# NKDS Compact Progress - DO NOT EDIT");
            sb.AppendLine("# If this file exists, the set is mid-compaction and must not be used");
            sb.AppendLine("# until compaction completes or recovery runs.");
            sb.AppendLine();

            sb.AppendLine($"Version: {this.Version}");
            sb.AppendLine($"Created: {this.Created:O}");
            sb.AppendLine($"SetName: {this.SetName}");
            sb.AppendLine();

            sb.AppendLine("# Shards identified as needing compaction (FileIds)");
            sb.AppendLine($"ShardsToCompact: {string.Join(",", this.ShardsToCompact)}");
            sb.AppendLine();

            sb.AppendLine("# Shards where compaction is fully complete (replaced original)");
            sb.AppendLine($"CompletedShards: {string.Join(",", this.CompletedShards)}");
            sb.AppendLine();

            sb.AppendLine("# Shard currently being processed (empty if between shards)");
            sb.AppendLine($"PendingShard: {(this.PendingShard.HasValue ? this.PendingShard.Value.ToString() : "")}");
            sb.AppendLine();

            sb.AppendLine("# Expected compacted sizes for each shard (bytes)");
            sb.AppendLine("# Format: FileId:ExpectedSize");
            string expectedSizesStr = string.Join(",", this.ExpectedSizes.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
            sb.AppendLine($"ExpectedSizes: {expectedSizesStr}");

            return sb.ToString();
        }

        private static CompactProgressFile? parse(string content)
        {
            CompactProgressFile result = new CompactProgressFile();
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                string key = trimmed.Substring(0, colonIndex).Trim();
                string value = trimmed.Substring(colonIndex + 1).Trim();
                fields[key] = value;
            }

            // Version (required)
            if (!fields.TryGetValue("Version", out string? versionStr) ||
                !int.TryParse(versionStr, out int version))
                return null;

            if (version > CurrentVersion)
                return null; // Future version we don't understand

            result.Version = version;

            // Created
            if (fields.TryGetValue("Created", out string? createdStr) &&
                DateTime.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime created))
            {
                result.Created = created;
            }

            // SetName
            if (fields.TryGetValue("SetName", out string? setName))
                result.SetName = setName;

            // ShardsToCompact
            if (fields.TryGetValue("ShardsToCompact", out string? shardsToCompactStr) &&
                !string.IsNullOrEmpty(shardsToCompactStr))
            {
                result.ShardsToCompact = parseIntList(shardsToCompactStr);
            }

            // CompletedShards
            if (fields.TryGetValue("CompletedShards", out string? completedStr) &&
                !string.IsNullOrEmpty(completedStr))
            {
                result.CompletedShards = parseIntList(completedStr);
            }

            // PendingShard
            if (fields.TryGetValue("PendingShard", out string? pendingStr) &&
                !string.IsNullOrEmpty(pendingStr) &&
                int.TryParse(pendingStr, out int pendingId))
            {
                result.PendingShard = pendingId;
            }

            // ExpectedSizes
            if (fields.TryGetValue("ExpectedSizes", out string? sizesStr) &&
                !string.IsNullOrEmpty(sizesStr))
            {
                result.ExpectedSizes = parseSizesDict(sizesStr);
            }

            return result;
        }

        private static List<int> parseIntList(string value)
        {
            List<int> result = new List<int>();
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int id))
                    result.Add(id);
            }
            return result;
        }

        private static Dictionary<int, long> parseSizesDict(string value)
        {
            Dictionary<int, long> result = new Dictionary<int, long>();
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split(':');
                if (kv.Length == 2 &&
                    int.TryParse(kv[0].Trim(), out int fileId) &&
                    long.TryParse(kv[1].Trim(), out long size))
                {
                    result[fileId] = size;
                }
            }
            return result;
        }
    }
}
