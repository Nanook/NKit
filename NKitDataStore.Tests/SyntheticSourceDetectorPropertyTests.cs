using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for SyntheticSourceDetector.DetectFolderGroups()
    /// (Property 7: Folder Group Detection Correctness).
    ///
    /// Creating real SourceFile instances with valid IndexFile objects requires
    /// parsing actual TMD binary data, which is impractical for property-based
    /// test generators. Instead, these tests construct minimal SourceFile instances
    /// using the internal constructor and helpers, then call the real
    /// DetectFolderGroups() method to verify the detection invariants.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 18.1**
    /// </summary>
    public class SyntheticSourceDetectorPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 18.1**
        ///
        /// Property 7: Folder Group Detection Correctness.
        /// For any set of directories with varying TmdApp source counts:
        ///   1. A FolderGroupInfo is created for every directory with 1+ TmdApp sources
        ///   2. No FolderGroupInfo is created for directories with 0 TmdApp sources
        ///   3. Groups are sorted by BaseName case-insensitively
        /// </summary>
        [Property]
        public bool FolderGroupDetection_CorrectGrouping(
            NonNegativeInt dirCountWrapper,
            NonNegativeInt seed)
        {
            int dirCount = (dirCountWrapper.Get % 8) + 1; // 1..8 directories
            int s = seed.Get;

            // Generate random directory structures
            List<DirectorySpec> directories = new List<DirectorySpec>();
            for (int d = 0; d < dirCount; d++)
            {
                // Vary TmdApp count: 0..5 per directory
                int tmdAppCount = ((s + (d * 31)) & 0x7FFFFFFF) % 6;
                // Vary non-TmdApp count: 0..3 per directory
                int nonTmdAppCount = ((s + (d * 17)) & 0x7FFFFFFF) % 4;
                string dirName = GenerateDirectoryName(s, d);
                string dirPath = $"/games/{dirName}";

                directories.Add(new DirectorySpec
                {
                    DirName = dirName,
                    DirPath = dirPath,
                    TmdAppCount = tmdAppCount,
                    NonTmdAppCount = nonTmdAppCount,
                });
            }

            // Build SourceFile list
            List<SourceFile> sourceFiles = new List<SourceFile>();
            foreach (DirectorySpec dir in directories)
            {
                for (int i = 0; i < dir.TmdAppCount; i++)
                {
                    SourceFile sf = CreateTmdAppSourceFile(dir.DirPath, dir.DirName, $"tmd.{i}");
                    sourceFiles.Add(sf);
                }
                for (int i = 0; i < dir.NonTmdAppCount; i++)
                {
                    SourceFile sf = CreateNonTmdAppSourceFile(dir.DirPath, $"disc{i}.iso");
                    sourceFiles.Add(sf);
                }
            }

            // Call the real DetectFolderGroups
            List<FolderGroupInfo> groups = SyntheticSourceDetector.DetectFolderGroups(sourceFiles);

            // === Property 7 Assertions ===

            // 1. A group exists for every directory with 1+ TmdApp sources
            List<string> expectedGroupDirs = directories
                .Where(d => d.TmdAppCount >= 1)
                .Select(d => d.DirPath)
                .ToList();

            // Count of groups must match count of directories with 1+ TmdApp sources
            if (groups.Count != expectedGroupDirs.Count)
                return false;

            // 2. Each group's SourceFolder must correspond to a directory with 2+ TmdApp sources
            foreach (FolderGroupInfo group in groups)
            {
                DirectorySpec matchingDir = directories.FirstOrDefault(d =>
                    string.Equals(d.DirPath, group.SourceFolder, StringComparison.OrdinalIgnoreCase));
                if (matchingDir.DirPath == null)
                    return false;
                if (matchingDir.TmdAppCount < 1)
                    return false;

                // Verify child count matches TmdApp count for that directory
                if (group.ChildSources.Count != matchingDir.TmdAppCount)
                    return false;

                // Verify group type is TmdAppFolder
                if (group.GroupType != FolderGroupType.TmdAppFolder)
                    return false;
            }

            // 3. No group for directories with 0 TmdApp sources
            HashSet<string> nonGroupDirs = directories
                .Where(d => d.TmdAppCount < 1)
                .Select(d => d.DirPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (FolderGroupInfo group in groups)
            {
                if (nonGroupDirs.Contains(group.SourceFolder))
                    return false;
            }

            // 4. Groups are sorted by BaseName case-insensitively
            for (int i = 1; i < groups.Count; i++)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(
                    groups[i - 1].BaseName, groups[i].BaseName) > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.2, 1.3**
        ///
        /// Property 7 (single-tmd inclusion): Directories with exactly 1 TmdApp source
        /// produce a FolderGroupInfo with 1 child, ensuring uniform TmdAppFolder
        /// creation for all WiiU content.
        /// </summary>
        [Property]
        public bool FolderGroupDetection_SingleTmdApp_ProducesGroup(
            NonNegativeInt dirCountWrapper,
            NonNegativeInt seed)
        {
            int dirCount = (dirCountWrapper.Get % 6) + 1; // 1..6 directories
            int s = seed.Get;

            // Create directories each with exactly 1 TmdApp source
            List<SourceFile> sourceFiles = new List<SourceFile>();
            for (int d = 0; d < dirCount; d++)
            {
                string dirName = GenerateDirectoryName(s, d);
                string dirPath = $"/single/{dirName}";
                sourceFiles.Add(CreateTmdAppSourceFile(dirPath, dirName, "tmd.0"));
            }

            List<FolderGroupInfo> groups = SyntheticSourceDetector.DetectFolderGroups(sourceFiles);

            // Each directory should produce a group with 1 child
            if (groups.Count != dirCount)
                return false;

            foreach (FolderGroupInfo group in groups)
            {
                if (group.ChildSources.Count != 1)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirement 1.1**
        ///
        /// Property 7 (case-insensitive grouping): Sources in the same directory
        /// but with different-cased BasePath values are grouped together.
        /// </summary>
        [Property]
        public bool FolderGroupDetection_CaseInsensitiveGrouping(
            NonNegativeInt seed)
        {
            int s = seed.Get;
            string dirName = GenerateDirectoryName(s, 0);

            // Create 2 TmdApp sources with different-cased paths
            string pathLower = $"/games/{dirName.ToLowerInvariant()}";
            string pathUpper = $"/games/{dirName.ToUpperInvariant()}";

            List<SourceFile> sourceFiles = new List<SourceFile>
            {
                CreateTmdAppSourceFile(pathLower, dirName, "tmd.0"),
                CreateTmdAppSourceFile(pathUpper, dirName, "tmd.1"),
            };

            List<FolderGroupInfo> groups = SyntheticSourceDetector.DetectFolderGroups(sourceFiles);

            // Should produce exactly 1 group (case-insensitive grouping)
            if (groups.Count != 1)
                return false;

            // Group should have 2 child sources
            return groups[0].ChildSources.Count == 2;
        }

        /// <summary>
        /// **Validates: Requirement 1.5**
        ///
        /// Property 7 (sort order): When multiple groups exist, they are sorted
        /// by BaseName using case-insensitive comparison.
        /// </summary>
        [Property]
        public bool FolderGroupDetection_SortedByBaseName(
            NonNegativeInt seed)
        {
            int s = seed.Get;

            // Create 3 directories with 2+ TmdApp sources each, with names
            // that would sort differently with case-sensitive vs case-insensitive
            string[] dirNames = { "Zelda", "alpha", "Beta" };
            List<SourceFile> sourceFiles = new List<SourceFile>();

            foreach (string dirName in dirNames)
            {
                string dirPath = $"/games/{dirName}";
                sourceFiles.Add(CreateTmdAppSourceFile(dirPath, dirName, "tmd.0"));
                sourceFiles.Add(CreateTmdAppSourceFile(dirPath, dirName, "tmd.1"));
            }

            List<FolderGroupInfo> groups = SyntheticSourceDetector.DetectFolderGroups(sourceFiles);

            if (groups.Count != 3)
                return false;

            // Verify case-insensitive sort: alpha, Beta, Zelda
            for (int i = 1; i < groups.Count; i++)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(
                    groups[i - 1].BaseName, groups[i].BaseName) > 0)
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
                // No IndexFile — this is a plain ISO, not a TmdApp
            };
            return sf;
        }

        /// <summary>
        /// Builds a minimal TMD v0 binary with the specified number of content entries.
        /// This is the simplest TMD format (vWii) that TmdInfo can parse.
        /// </summary>
        private static byte[] BuildMinimalTmdBinary(int contentCount)
        {
            // TMD v0 layout:
            //   0x180: Version = 0
            //   0x18c: TitleId (8 bytes)
            //   0x1de: TotalContents (2 bytes big-endian)
            //   0x1e4: Content entries start (each 0x24 bytes)
            //     +0x00: ContentId (4 bytes big-endian)
            //     +0x04: Index (2 bytes big-endian)
            //     +0x06: Type (2 bytes big-endian)
            //     +0x08: Size (8 bytes big-endian)
            //     +0x10: Hash (20 bytes)
            int contentOffset = 0x1e4;
            int contentItemLen = 0x24;
            int totalSize = contentOffset + (contentCount * contentItemLen);
            byte[] data = new byte[totalSize];

            // Version = 0 at offset 0x180
            data[0x180] = 0;

            // TotalContents at 0x1de (big-endian 16-bit)
            data[0x1de] = (byte)((contentCount >> 8) & 0xFF);
            data[0x1df] = (byte)(contentCount & 0xFF);

            // Write content entries
            for (int i = 0; i < contentCount; i++)
            {
                int off = contentOffset + (i * contentItemLen);
                // ContentId (4 bytes big-endian) — use sequential IDs
                data[off + 3] = (byte)(i & 0xFF);
                data[off + 2] = (byte)((i >> 8) & 0xFF);
                // Index (2 bytes)
                data[off + 5] = (byte)(i & 0xFF);
                // Size (8 bytes) — small non-zero size
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