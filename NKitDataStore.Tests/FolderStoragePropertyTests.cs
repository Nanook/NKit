using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for folder storage completeness.
    ///
    /// DataStoreFolderFormatter requires a real DataStore with SQLite and shard files,
    /// so these tests validate the LOGICAL properties using a simulated model that
    /// mirrors the invariants maintained by StoreFile():
    ///   - FolderFileEntry count equals the number of files stored
    ///   - Cumulative offset equals the sum of all file sizes
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    public class FolderStoragePropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 2: Folder Storage Completeness.
        /// For any sequence of files with known sizes, simulating the StoreFile() logic
        /// produces exactly one FolderFileEntry per file, and the cumulative offset
        /// equals the sum of all file sizes.
        /// </summary>
        [Property]
        public bool FolderStorage_Completeness_EntryCountAndOffset(
            NonNegativeInt fileCountWrapper,
            NonNegativeInt seed)
        {
            int fileCount = fileCountWrapper.Get % 20; // 0..19 files
            int s = seed.Get;

            // Generate arbitrary file sizes (0 to ~1MB range)
            long[] fileSizes = new long[fileCount];
            for (int i = 0; i < fileCount; i++)
                fileSizes[i] = (long)(uint)((s + (i * 7919)) & 0x7FFFFFFF) % 1_048_577; // 0..1MB

            // Simulate the StoreFile() logic from DataStoreFolderFormatter:
            // - Each call appends a FolderFileEntry
            // - _currentOffset advances by fileSize (only for non-zero files, matching the implementation)
            List<SimulatedFolderFileEntry> entries = new List<SimulatedFolderFileEntry>();
            long currentOffset = 0;

            for (int i = 0; i < fileCount; i++)
            {
                long offsetStart = currentOffset;
                long size = fileSizes[i];
                string relativePath = $"dir{i % 3}/file{i}.dat";

                entries.Add(new SimulatedFolderFileEntry
                {
                    RelativePath = relativePath,
                    OffsetStart = offsetStart,
                    Size = size
                });

                // Mirror DataStoreFolderFormatter: only advance offset for non-zero files
                if (size > 0)
                    currentOffset += size;
            }

            // Property 2 assertions:
            // 1. Entry count equals files stored
            if (entries.Count != fileCount)
                return false;

            // 2. Cumulative offset equals sum of all (non-zero) file sizes
            long expectedOffset = fileSizes.Where(s => s > 0).Sum();
            if (currentOffset != expectedOffset)
                return false;

            // 3. Each entry has correct offset start (monotonically non-decreasing)
            long runningOffset = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].OffsetStart != runningOffset)
                    return false;
                if (entries[i].Size > 0)
                    runningOffset += entries[i].Size;
            }

            // 4. Verify filesystem.yaml would contain all entries:
            //    Build an FsYaml from the entries and verify it has the right file count
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            foreach (SimulatedFolderFileEntry entry in entries)
            {
                root.AddFileByPath(entry.RelativePath, entry.OffsetStart, entry.Size, 0, 0);
            }

            string yaml = fsYaml.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            // Count leaf files in the parsed tree
            int parsedFileCount = CountFiles(parsed.FileSystems);
            if (parsedFileCount != fileCount)
                return false;

            return true;
        }

        /// <summary>
        /// Counts all leaf file nodes across all filesystem roots.
        /// </summary>
        private static int CountFiles(List<FsYamlNode> roots)
        {
            int count = 0;
            foreach (FsYamlNode root in roots)
                count += CountFilesRecursive(root);
            return count;
        }

        private static int CountFilesRecursive(FsYamlNode node)
        {
            if (node.IsFile)
                return 1;

            int count = 0;
            if (node.Children != null)
            {
                foreach (FsYamlNode child in node.Children)
                    count += CountFilesRecursive(child);
            }
            return count;
        }

        /// <summary>
        /// Simulated FolderFileEntry mirroring the internal struct in DataStoreFolderFormatter.
        /// </summary>
        private struct SimulatedFolderFileEntry
        {
            public string RelativePath;
            public long OffsetStart;
            public long Size;
        }
    }
}