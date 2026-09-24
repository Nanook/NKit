using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for SyntheticSourceDetector.DetectFolderGroups().
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6**
    /// </summary>
    public class SyntheticSourceDetectorTests
    {
        // === Requirement 1.6: Empty source list returns empty groups ===

        [Fact]
        public void EmptySourceList_ReturnsEmptyGroups()
        {
            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(Array.Empty<SourceFile>());
            Assert.Empty(result);
        }

        // === Requirement 1.3, 18.1: Single TmdApp source per directory returns no groups ===

        [Fact]
        public void SingleTmdAppPerDirectory_ReturnsOneGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.0"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);
            Assert.Single(result);
            Assert.Equal("Mario", result[0].BaseName);
            Assert.Single(result[0].ChildSources);
        }

        // === Requirement 1.2: Two TmdApp sources in same directory returns one group ===

        [Fact]
        public void TwoTmdAppInSameDirectory_ReturnsOneGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.0"),
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.1"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(FolderGroupType.TmdAppFolder, result[0].GroupType);
            Assert.Equal("Mario", result[0].BaseName);
            Assert.Equal("/games/Mario", result[0].SourceFolder);
            Assert.Equal(2, result[0].ChildSources.Count);
            Assert.Equal(SystemType.WiiU, result[0].SystemType);
        }

        // === Requirement 1.2: Three+ TmdApp sources returns one group with correct child count ===

        [Fact]
        public void ThreeTmdAppInSameDirectory_ReturnsOneGroupWithThreeChildren()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.0"),
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.1"),
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.2"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(3, result[0].ChildSources.Count);
            Assert.Equal("Zelda", result[0].BaseName);
        }

        [Fact]
        public void FiveTmdAppInSameDirectory_ReturnsOneGroupWithFiveChildren()
        {
            List<SourceFile> sources = new List<SourceFile>();
            for (int i = 0; i < 5; i++)
                sources.Add(CreateTmdAppSourceFile("/games/Splatoon", "Splatoon", $"tmd.{i}"));

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(5, result[0].ChildSources.Count);
        }

        // === Requirements 1.2, 1.3: Mixed directories ===

        [Fact]
        public void MixedDirectories_GroupsForAllDirectoriesWithTmdApp()
        {
            SourceFile[] sources = new[]
            {
                // Dir with 3 TmdApp — should produce a group
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.0"),
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.1"),
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.2"),
                // Dir with 1 TmdApp — should also produce a group
                CreateTmdAppSourceFile("/games/Kirby", "Kirby", "tmd.0"),
                // Dir with 2 TmdApp — should produce a group
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.0"),
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.1"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Equal(3, result.Count);
            // Sorted by BaseName case-insensitively: Kirby, Mario, Zelda
            Assert.Equal("Kirby", result[0].BaseName);
            Assert.Equal(1, result[0].ChildSources.Count);
            Assert.Equal("Mario", result[1].BaseName);
            Assert.Equal(3, result[1].ChildSources.Count);
            Assert.Equal("Zelda", result[2].BaseName);
            Assert.Equal(2, result[2].ChildSources.Count);
        }

        // === Requirement 1.1, 1.3: Non-TmdApp sources are excluded from groups ===

        [Fact]
        public void NonTmdAppSources_ExcludedFromGroups()
        {
            SourceFile[] sources = new[]
            {
                CreateNonTmdAppSourceFile("/games/Disc", "game.iso"),
                CreateNonTmdAppSourceFile("/games/Disc", "game2.iso"),
                CreateNonTmdAppSourceFile("/games/Disc", "game3.iso"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);
            Assert.Empty(result);
        }

        [Fact]
        public void MixedTmdAppAndNonTmdApp_OnlyTmdAppCounted()
        {
            SourceFile[] sources = new[]
            {
                // 1 TmdApp + 2 non-TmdApp in same dir — should produce a group with 1 child
                CreateTmdAppSourceFile("/games/Mixed", "Mixed", "tmd.0"),
                CreateNonTmdAppSourceFile("/games/Mixed", "game.iso"),
                CreateNonTmdAppSourceFile("/games/Mixed", "game2.iso"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);
            Assert.Single(result);
            Assert.Single(result[0].ChildSources);
        }

        [Fact]
        public void TwoTmdAppPlusNonTmdApp_GroupContainsOnlyTmdApp()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.0"),
                CreateTmdAppSourceFile("/games/Mario", "Mario", "tmd.1"),
                CreateNonTmdAppSourceFile("/games/Mario", "extra.iso"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(2, result[0].ChildSources.Count);
            // Verify all children are TmdApp
            foreach (SourceFile child in result[0].ChildSources)
                Assert.Equal(IndexFileType.TmdApp, child.IndexFile.FileType);
        }

        // === Requirement 1.1: Case-insensitive BasePath grouping ===

        [Fact]
        public void CaseInsensitiveBasePath_GroupedTogether()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/mario", "Mario", "tmd.0"),
                CreateTmdAppSourceFile("/GAMES/MARIO", "Mario", "tmd.1"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(2, result[0].ChildSources.Count);
        }

        [Fact]
        public void MixedCaseBasePath_ProducesSingleGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/Games/Zelda HD", "Zelda HD", "tmd.0"),
                CreateTmdAppSourceFile("/games/zelda hd", "Zelda HD", "tmd.1"),
                CreateTmdAppSourceFile("/GAMES/ZELDA HD", "Zelda HD", "tmd.2"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Single(result);
            Assert.Equal(3, result[0].ChildSources.Count);
        }

        // === Requirement 1.5: Groups sorted by BaseName case-insensitively ===

        [Fact]
        public void GroupsSortedByBaseName_CaseInsensitive()
        {
            SourceFile[] sources = new[]
            {
                // "Zelda" sorts after "alpha" and "Beta" case-insensitively
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.0"),
                CreateTmdAppSourceFile("/games/Zelda", "Zelda", "tmd.1"),
                CreateTmdAppSourceFile("/games/alpha", "alpha", "tmd.0"),
                CreateTmdAppSourceFile("/games/alpha", "alpha", "tmd.1"),
                CreateTmdAppSourceFile("/games/Beta", "Beta", "tmd.0"),
                CreateTmdAppSourceFile("/games/Beta", "Beta", "tmd.1"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Equal(3, result.Count);
            // Case-insensitive sort: alpha, Beta, Zelda
            Assert.Equal("alpha", result[0].BaseName);
            Assert.Equal("Beta", result[1].BaseName);
            Assert.Equal("Zelda", result[2].BaseName);
        }

        [Fact]
        public void GroupsSortedByBaseName_NumericNames()
        {
            SourceFile[] sources = new[]
            {
                CreateTmdAppSourceFile("/games/Game 10", "Game 10", "tmd.0"),
                CreateTmdAppSourceFile("/games/Game 10", "Game 10", "tmd.1"),
                CreateTmdAppSourceFile("/games/Game 2", "Game 2", "tmd.0"),
                CreateTmdAppSourceFile("/games/Game 2", "Game 2", "tmd.1"),
                CreateTmdAppSourceFile("/games/Game 1", "Game 1", "tmd.0"),
                CreateTmdAppSourceFile("/games/Game 1", "Game 1", "tmd.1"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            Assert.Equal(3, result.Count);
            Assert.Equal("Game 1", result[0].BaseName);
            Assert.Equal("Game 10", result[1].BaseName);
            Assert.Equal("Game 2", result[2].BaseName);
        }

        // === Helper Methods (reusing patterns from SyntheticSourceDetectorPropertyTests) ===

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
    }
}