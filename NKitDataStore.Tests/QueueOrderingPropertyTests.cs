using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for queue ordering (Property 1: Queue Ordering Guarantee).
    ///
    /// The processing queue is assembled by the orchestrators (NKitApp/Program.cs
    /// and NKDS/CommandLine.ExecuteAdd) as follows:
    ///   1. Real SourceFiles are scanned and sorted by name
    ///   2. SyntheticSourceDetector.DetectFolderGroups() detects folder groups
    ///   3. SyntheticSourceFactory.CreateSyntheticSources() creates synthetic sources
    ///   4. Synthetic sources are appended AFTER all real sources via images.AddRange()
    ///
    /// This means ALL real sources come before ALL synthetic sources in the queue.
    /// These tests verify that invariant holds for any random input.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    public class QueueOrderingPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 3.1, 3.2**
        ///
        /// Property 1: Queue Ordering Guarantee.
        /// For any set of directories with varying TmdApp and non-TmdApp source counts:
        ///   1. All real child sources appear before their group's synthetic source in the queue
        ///   2. No synthetic source appears before any real source
        ///   3. The ordering guarantee holds regardless of how many groups or children per group
        /// </summary>
        [Property]
        public bool QueueOrdering_AllRealSourcesBeforeSynthetics(
            NonNegativeInt dirCountWrapper,
            NonNegativeInt seed)
        {
            int dirCount = (dirCountWrapper.Get % 8) + 1; // 1..8 directories
            int s = seed.Get;

            // Step 1: Generate random real SourceFiles across multiple directories
            List<SourceFile> realSources = new List<SourceFile>();
            List<DirectorySpec> dirSpecs = new List<DirectorySpec>();

            for (int d = 0; d < dirCount; d++)
            {
                int tmdAppCount = ((s + (d * 31)) & 0x7FFFFFFF) % 6; // 0..5 TmdApp per dir
                int nonTmdAppCount = ((s + (d * 17)) & 0x7FFFFFFF) % 4; // 0..3 non-TmdApp per dir
                string dirName = GenerateDirectoryName(s, d);
                string dirPath = $"/games/{dirName}";

                dirSpecs.Add(new DirectorySpec
                {
                    DirName = dirName,
                    DirPath = dirPath,
                    TmdAppCount = tmdAppCount,
                    NonTmdAppCount = nonTmdAppCount,
                });

                for (int i = 0; i < tmdAppCount; i++)
                    realSources.Add(CreateTmdAppSourceFile(dirPath, dirName, $"tmd.{i}"));

                for (int i = 0; i < nonTmdAppCount; i++)
                    realSources.Add(CreateNonTmdAppSourceFile(dirPath, $"disc{i}.iso"));
            }

            // Step 2: Simulate the orchestrator queue assembly
            // Sort real sources by name (as the orchestrators do)
            List<SourceFile> sortedReal = realSources
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Detect folder groups and create synthetic sources
            List<FolderGroupInfo> folderGroups = SyntheticSourceDetector.DetectFolderGroups(sortedReal);
            List<SourceFile> syntheticSources = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            // Assemble queue: real first, then synthetic appended
            List<SourceFile> queue = new List<SourceFile>(sortedReal);
            queue.AddRange(syntheticSources);

            // === Property 1 Assertions ===

            // Assertion 1: No synthetic source appears before any real source
            int firstSyntheticIndex = -1;
            int lastRealIndex = -1;

            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].IsSyntheticFolder)
                {
                    if (firstSyntheticIndex == -1)
                        firstSyntheticIndex = i;
                }
                else
                {
                    lastRealIndex = i;
                }
            }

            // If there are both real and synthetic sources, all real must come first
            if (firstSyntheticIndex != -1 && lastRealIndex != -1)
            {
                if (lastRealIndex >= firstSyntheticIndex)
                    return false;
            }

            // Assertion 2: For every synthetic source, ALL of its child real sources
            // appear earlier in the queue
            foreach (SourceFile sf in queue.Where(q => q.IsSyntheticFolder))
            {
                FolderGroupInfo group = sf.SyntheticFolderGroup;
                int syntheticIdx = queue.IndexOf(sf);

                foreach (SourceFile child in group.ChildSources)
                {
                    // Find the child in the queue by reference
                    int childIdx = queue.FindIndex(q =>
                        !q.IsSyntheticFolder &&
                        ReferenceEquals(q.IndexFile, child.IndexFile) &&
                        q.Name == child.Name &&
                        q.BasePath == child.BasePath);

                    if (childIdx == -1)
                        return false; // Child not found in queue

                    if (childIdx >= syntheticIdx)
                        return false; // Child appears at or after its synthetic source
                }
            }

            // Assertion 3: Every directory with 1+ TmdApp sources has a synthetic source
            int expectedGroupCount = dirSpecs.Count(d => d.TmdAppCount >= 1);
            int actualSyntheticCount = queue.Count(q => q.IsSyntheticFolder);
            if (actualSyntheticCount != expectedGroupCount)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirement 3.1**
        ///
        /// Property 1 (no synthetics without TmdApp): When no directory has any TmdApp
        /// sources, the queue contains only real sources and no synthetic sources.
        /// </summary>
        [Property]
        public bool QueueOrdering_NoGroups_NoSynthetics(
            NonNegativeInt dirCountWrapper,
            NonNegativeInt seed)
        {
            int dirCount = (dirCountWrapper.Get % 6) + 1; // 1..6 directories
            int s = seed.Get;

            // Create directories with only non-TmdApp sources (no tmd files at all)
            List<SourceFile> realSources = new List<SourceFile>();
            for (int d = 0; d < dirCount; d++)
            {
                string dirName = GenerateDirectoryName(s, d);
                string dirPath = $"/nogroup/{dirName}";

                // Add only non-TmdApp sources
                realSources.Add(CreateNonTmdAppSourceFile(dirPath, $"game{d}.iso"));
            }

            List<SourceFile> sortedReal = realSources
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<FolderGroupInfo> folderGroups = SyntheticSourceDetector.DetectFolderGroups(sortedReal);
            List<SourceFile> syntheticSources = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            List<SourceFile> queue = new List<SourceFile>(sortedReal);
            queue.AddRange(syntheticSources);

            // No synthetic sources should exist
            if (syntheticSources.Count != 0)
                return false;

            // Queue should equal the sorted real sources exactly
            return queue.Count == sortedReal.Count;
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.2**
        ///
        /// Property 1 (multiple groups): When multiple directories each have 1+ TmdApp
        /// sources, every group's children appear before every synthetic source.
        /// </summary>
        [Property]
        public bool QueueOrdering_MultipleGroups_AllChildrenBeforeAllSynthetics(
            NonNegativeInt groupCountWrapper,
            NonNegativeInt seed)
        {
            int groupCount = (groupCountWrapper.Get % 5) + 2; // 2..6 groups
            int s = seed.Get;

            List<SourceFile> realSources = new List<SourceFile>();
            for (int g = 0; g < groupCount; g++)
            {
                string dirName = GenerateDirectoryName(s, g);
                string dirPath = $"/multi/{dirName}";
                int childCount = (((s + (g * 23)) & 0x7FFFFFFF) % 4) + 2; // 2..5 children

                for (int i = 0; i < childCount; i++)
                    realSources.Add(CreateTmdAppSourceFile(dirPath, dirName, $"tmd.{i}"));
            }

            List<SourceFile> sortedReal = realSources
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<FolderGroupInfo> folderGroups = SyntheticSourceDetector.DetectFolderGroups(sortedReal);
            List<SourceFile> syntheticSources = SyntheticSourceFactory.CreateSyntheticSources(folderGroups);

            List<SourceFile> queue = new List<SourceFile>(sortedReal);
            queue.AddRange(syntheticSources);

            // Must have the expected number of synthetic sources
            if (syntheticSources.Count != groupCount)
                return false;

            // All real sources must appear before all synthetic sources
            int realCount = sortedReal.Count;
            for (int i = 0; i < realCount; i++)
            {
                if (queue[i].IsSyntheticFolder)
                    return false;
            }
            for (int i = realCount; i < queue.Count; i++)
            {
                if (!queue[i].IsSyntheticFolder)
                    return false;
            }

            return true;
        }

        // === Helper Methods ===

        /// <summary>
        /// Creates a minimal SourceFile that looks like a TmdApp source.
        /// Sets up ImageFiles for BasePath, and IndexFile with FileType = TmdApp.
        /// </summary>
        private static SourceFile CreateTmdAppSourceFile(string dirPath, string dirName, string tmdFileName)
        {
            SourceFile sf = new SourceFile
            {
                Status = SourceFileResult.Valid,
                Name = dirName,
                CleanName = dirName,
                SystemType = SystemType.WiiU,
                ImageFiles = new[]
                {
                    new SourceFileItem(dirPath, "00000000.app", ".app", "", 0, 1024, 0, false, false)
                },
                IndexFile = IndexFile.Parse(dirPath, tmdFileName, ".tmd", null,
                    BuildMinimalTmdBinary(1), false, false, new FileItem[0]),
            };
            return sf;
        }

        /// <summary>
        /// Creates a minimal SourceFile that is NOT a TmdApp source (e.g., an ISO).
        /// </summary>
        private static SourceFile CreateNonTmdAppSourceFile(string dirPath, string fileName)
        {
            SourceFile sf = new SourceFile
            {
                Status = SourceFileResult.Valid,
                Name = fileName,
                CleanName = fileName,
                SystemType = SystemType.Default,
                ImageFiles = new[]
                {
                    new SourceFileItem(dirPath, fileName, ".iso", "", 0, 1024, 0, false, false)
                },
            };
            return sf;
        }

        /// <summary>
        /// Builds a minimal TMD v0 binary with the specified number of content entries.
        /// </summary>
        private static byte[] BuildMinimalTmdBinary(int contentCount)
        {
            int contentOffset = 0x1e4;
            int contentItemLen = 0x24;
            int totalSize = contentOffset + (contentCount * contentItemLen);
            byte[] data = new byte[totalSize];

            data[0x180] = 0; // Version = 0
            data[0x1de] = (byte)((contentCount >> 8) & 0xFF);
            data[0x1df] = (byte)(contentCount & 0xFF);

            for (int i = 0; i < contentCount; i++)
            {
                int off = contentOffset + (i * contentItemLen);
                data[off + 3] = (byte)(i & 0xFF);
                data[off + 2] = (byte)((i >> 8) & 0xFF);
                data[off + 5] = (byte)(i & 0xFF);
                data[off + 0x0F] = 0x10;
            }

            return data;
        }

        /// <summary>
        /// Generates a deterministic directory name from a seed and index.
        /// </summary>
        private static string GenerateDirectoryName(int seed, int index)
        {
            string[] prefixes = { "Game", "Mario", "Zelda", "Metroid", "Kirby", "Splatoon", "Pikmin", "Xenoblade" };
            string[] suffixes = { "HD", "Deluxe", "3D", "World", "Kart", "Bros", "Party", "Maker" };
            int pi = ((seed + (index * 13)) & 0x7FFFFFFF) % prefixes.Length;
            int si = ((seed + (index * 29)) & 0x7FFFFFFF) % suffixes.Length;
            return $"{prefixes[pi]} {suffixes[si]} {index}";
        }

        private struct DirectorySpec
        {
            public string DirName;
            public string DirPath;
            public int TmdAppCount;
            public int NonTmdAppCount;
        }
    }
}