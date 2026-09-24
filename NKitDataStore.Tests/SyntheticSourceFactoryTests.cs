using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for SyntheticSourceFactory.CreateSyntheticSources().
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
    /// </summary>
    public class SyntheticSourceFactoryTests
    {
        // === Requirement 2.1: One SourceFile per FolderGroupInfo ===

        [Fact]
        public void EmptyList_ReturnsEmptyList()
        {
            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(new List<FolderGroupInfo>());
            Assert.Empty(result);
        }

        [Fact]
        public void SingleGroup_ReturnsOneSourceFile()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario Kart 8", "/games/Mario Kart 8", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Single(result);
        }

        [Fact]
        public void MultipleGroups_ReturnsCorrectCount()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario Kart 8", "/games/Mario Kart 8", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("Zelda HD", "/games/Zelda HD", FolderGroupType.TmdAppFolder, SystemType.WiiU, 4),
                CreateGroup("Splatoon", "/games/Splatoon", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal(3, result.Count);
        }

        // === Requirement 2.2: IsSyntheticFolder == true on all outputs ===

        [Fact]
        public void SingleGroup_IsSyntheticFolderIsTrue()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario", "/games/Mario", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.True(result[0].IsSyntheticFolder);
        }

        [Fact]
        public void MultipleGroups_AllHaveIsSyntheticFolderTrue()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Game A", "/games/A", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("Game B", "/games/B", FolderGroupType.CueFolder, SystemType.GameCube, 3),
                CreateGroup("Game C", "/games/C", FolderGroupType.GdiFolder, SystemType.Default, 4),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.All(result, sf => Assert.True(sf.IsSyntheticFolder));
        }

        // === Requirement 2.2: SyntheticFolderGroup references the correct FolderGroupInfo ===

        [Fact]
        public void SingleGroup_SyntheticFolderGroupIsReferenceEqual()
        {
            FolderGroupInfo group = CreateGroup("Mario", "/games/Mario", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2);
            List<FolderGroupInfo> groups = new List<FolderGroupInfo> { group };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Same(group, result[0].SyntheticFolderGroup);
        }

        [Fact]
        public void MultipleGroups_EachSyntheticFolderGroupReferencesCorrectInput()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Alpha", "/games/Alpha", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("Beta", "/games/Beta", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
                CreateGroup("Gamma", "/games/Gamma", FolderGroupType.TmdAppFolder, SystemType.WiiU, 4),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            for (int i = 0; i < groups.Count; i++)
                Assert.Same(groups[i], result[i].SyntheticFolderGroup);
        }

        // === Requirement 2.2: Name == BaseName and CleanName == BaseName ===

        [Fact]
        public void SingleGroup_NameAndCleanNameMatchBaseName()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Zelda Wind Waker HD", "/games/Zelda", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal("Zelda Wind Waker HD", result[0].Name);
            Assert.Equal("Zelda Wind Waker HD", result[0].CleanName);
        }

        [Fact]
        public void MultipleGroups_EachNameMatchesBaseName()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Game One", "/games/One", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("Game Two", "/games/Two", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal("Game One", result[0].Name);
            Assert.Equal("Game One", result[0].CleanName);
            Assert.Equal("Game Two", result[1].Name);
            Assert.Equal("Game Two", result[1].CleanName);
        }

        // === Requirement 2.2: Status == SourceFileResult.Valid ===

        [Fact]
        public void SingleGroup_StatusIsValid()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario", "/games/Mario", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal(SourceFileResult.Valid, result[0].Status);
        }

        [Fact]
        public void MultipleGroups_AllStatusValid()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("A", "/a", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("B", "/b", FolderGroupType.CueFolder, SystemType.GameCube, 3),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.All(result, sf => Assert.Equal(SourceFileResult.Valid, sf.Status));
        }

        // === Requirement 2.3: ImageFiles and IndexFile are null ===

        [Fact]
        public void SingleGroup_ImageFilesAndIndexFileAreNull()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario", "/games/Mario", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Null(result[0].ImageFiles);
            Assert.Null(result[0].IndexFile);
        }

        [Fact]
        public void MultipleGroups_AllImageFilesAndIndexFileAreNull()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("A", "/a", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("B", "/b", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
                CreateGroup("C", "/c", FolderGroupType.TmdAppFolder, SystemType.WiiU, 4),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.All(result, sf =>
            {
                Assert.Null(sf.ImageFiles);
                Assert.Null(sf.IndexFile);
            });
        }

        // === Requirement 2.4: Order preservation ===

        [Fact]
        public void MultipleGroups_OutputOrderMatchesInputOrder()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Zebra", "/z", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("Apple", "/a", FolderGroupType.TmdAppFolder, SystemType.WiiU, 3),
                CreateGroup("Mango", "/m", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal("Zebra", result[0].Name);
            Assert.Equal("Apple", result[1].Name);
            Assert.Equal("Mango", result[2].Name);
        }

        // === Requirement 2.2: SystemType propagated from group ===

        [Fact]
        public void SingleGroup_SystemTypeMatchesGroup()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("Mario", "/games/Mario", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal(SystemType.WiiU, result[0].SystemType);
        }

        [Fact]
        public void MultipleGroups_DifferentSystemTypes_EachMatchesGroup()
        {
            List<FolderGroupInfo> groups = new List<FolderGroupInfo>
            {
                CreateGroup("A", "/a", FolderGroupType.TmdAppFolder, SystemType.WiiU, 2),
                CreateGroup("B", "/b", FolderGroupType.CueFolder, SystemType.GameCube, 3),
                CreateGroup("C", "/c", FolderGroupType.GdiFolder, SystemType.Wii, 2),
            };

            List<SourceFile> result = SyntheticSourceFactory.CreateSyntheticSources(groups);

            Assert.Equal(SystemType.WiiU, result[0].SystemType);
            Assert.Equal(SystemType.GameCube, result[1].SystemType);
            Assert.Equal(SystemType.Wii, result[2].SystemType);
        }

        // === Helper Methods ===

        private static FolderGroupInfo CreateGroup(
            string baseName, string sourceFolder,
            FolderGroupType groupType, SystemType systemType,
            int childCount)
        {
            List<SourceFile> children = new List<SourceFile>();
            for (int i = 0; i < childCount; i++)
            {
                children.Add(new SourceFile
                {
                    Name = baseName,
                    CleanName = baseName,
                    Status = SourceFileResult.Valid,
                    SystemType = systemType,
                });
            }

            return new FolderGroupInfo
            {
                GroupType = groupType,
                BaseName = baseName,
                SourceFolder = sourceFolder,
                ChildSources = children,
                SystemType = systemType,
            };
        }
    }
}