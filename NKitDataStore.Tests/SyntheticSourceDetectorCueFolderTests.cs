using Nanook.NKit;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for SyntheticSourceDetector.DetectFolderGroups() — CueFolder group detection.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    public class SyntheticSourceDetectorCueFolderTests
    {
        // === Requirement 10.1: 2+ CUE images in same container produces CueFolder group ===

        [Fact]
        public void TwoCueImagesInSameFolder_ProducesCueFolderGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateCueSourceFile("/games/MultiDisc", "Disc 1", "track01.bin"),
                CreateCueSourceFile("/games/MultiDisc", "Disc 2", "track01.bin"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            List<FolderGroupInfo> cueFolderGroups = result.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
            Assert.Single(cueFolderGroups);
            Assert.Equal(FolderGroupType.CueFolder, cueFolderGroups[0].GroupType);
            Assert.Equal(2, cueFolderGroups[0].ChildSources.Count);
            Assert.Equal("/games/MultiDisc", cueFolderGroups[0].SourceFolder);
        }

        // === Requirement 10.1: 3 GDI images in same container produces CueFolder group ===

        [Fact]
        public void ThreeGdiImagesInSameFolder_ProducesCueFolderGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateGdiSourceFile("/games/Dreamcast", "Game Disc 1"),
                CreateGdiSourceFile("/games/Dreamcast", "Game Disc 2"),
                CreateGdiSourceFile("/games/Dreamcast", "Game Disc 3"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            List<FolderGroupInfo> cueFolderGroups = result.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
            Assert.Single(cueFolderGroups);
            Assert.Equal(FolderGroupType.CueFolder, cueFolderGroups[0].GroupType);
            Assert.Equal(3, cueFolderGroups[0].ChildSources.Count);
            Assert.Equal("/games/Dreamcast", cueFolderGroups[0].SourceFolder);
        }

        // === Requirement 10.2: Single CUE image does NOT produce CueFolder group ===

        [Fact]
        public void SingleCueImageInFolder_DoesNotProduceCueFolderGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateCueSourceFile("/games/SingleDisc", "Game", "track01.bin"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            List<FolderGroupInfo> cueFolderGroups = result.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
            Assert.Empty(cueFolderGroups);
        }

        // === Requirement 10.1: Mixed CUE and GDI images produces CueFolder group ===

        [Fact]
        public void MixedCueAndGdiImagesInSameFolder_ProducesCueFolderGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateCueSourceFile("/games/Mixed", "Disc 1", "track01.bin"),
                CreateGdiSourceFile("/games/Mixed", "Disc 2"),
                CreateCueSourceFile("/games/Mixed", "Disc 3", "track01.bin"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            List<FolderGroupInfo> cueFolderGroups = result.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
            Assert.Single(cueFolderGroups);
            Assert.Equal(FolderGroupType.CueFolder, cueFolderGroups[0].GroupType);
            Assert.Equal(3, cueFolderGroups[0].ChildSources.Count);
        }

        // === Requirement 10.1: CUE images with non-disc files still produces CueFolder group ===

        [Fact]
        public void CueImagesWithNonDiscFiles_StillProducesCueFolderGroup()
        {
            SourceFile[] sources = new[]
            {
                CreateCueSourceFile("/games/WithExtras", "Disc 1", "track01.bin"),
                CreateCueSourceFile("/games/WithExtras", "Disc 2", "track01.bin"),
                CreateNonDiscSourceFile("/games/WithExtras", "readme.txt"),
                CreateNonDiscSourceFile("/games/WithExtras", "playlist.m3u"),
            };

            List<FolderGroupInfo> result = SyntheticSourceDetector.DetectFolderGroups(sources);

            List<FolderGroupInfo> cueFolderGroups = result.Where(g => g.GroupType == FolderGroupType.CueFolder).ToList();
            Assert.Single(cueFolderGroups);
            Assert.Equal(FolderGroupType.CueFolder, cueFolderGroups[0].GroupType);
            Assert.Equal(2, cueFolderGroups[0].ChildSources.Count);
            // Non-disc files should NOT be included in the CueFolder group's children
            Assert.All(cueFolderGroups[0].ChildSources, child =>
                Assert.True(
                    child.IndexFile?.FileType == IndexFileType.Cue ||
                    child.IndexFile?.FileType == IndexFileType.Gdi));
        }

        // === Helper Methods ===

        /// <summary>
        /// Creates a minimal SourceFile with an IndexFile of type Cue.
        /// Uses IndexFile.Parse with a minimal CUE sheet content.
        /// </summary>
        private static SourceFile CreateCueSourceFile(string dirPath, string name, string trackFileName)
        {
            // Minimal CUE sheet content that will parse into a valid IndexFile
            string cueContent = $"FILE \"{trackFileName}\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n";
            byte[] cueBytes = Encoding.UTF8.GetBytes(cueContent);

            SourceFile sf = new SourceFile
            {
                Status = SourceFileResult.Valid,
                Name = name,
                CleanName = name,
                SystemType = SystemType.Default,
                ImageFiles = new[]
                {
                    new SourceFileItem(dirPath, trackFileName, ".bin", "", 0, 700_000_000, 0, false, false)
                },
                IndexFile = IndexFile.Parse(dirPath, $"{name}.cue", ".cue", null,
                    cueBytes, false, false, new FileItem[0]),
            };
            return sf;
        }

        /// <summary>
        /// Creates a minimal SourceFile with an IndexFile of type Gdi.
        /// Uses IndexFile.Parse with a minimal GDI descriptor content.
        /// </summary>
        private static SourceFile CreateGdiSourceFile(string dirPath, string name)
        {
            // Minimal GDI content: track count line + track entries
            string gdiContent = "3\r\n1 0 4 2352 \"track01.bin\" 0\r\n2 450 0 2352 \"track02.raw\" 0\r\n3 45000 4 2048 \"track03.bin\" 0\r\n";
            byte[] gdiBytes = Encoding.UTF8.GetBytes(gdiContent);

            SourceFile sf = new SourceFile
            {
                Status = SourceFileResult.Valid,
                Name = name,
                CleanName = name,
                SystemType = SystemType.Default,
                ImageFiles = new[]
                {
                    new SourceFileItem(dirPath, "track01.bin", ".bin", "", 0, 100_000_000, 0, false, false)
                },
                IndexFile = IndexFile.Parse(dirPath, $"{name}.gdi", ".gdi", null,
                    gdiBytes, false, false, new FileItem[0]),
            };
            return sf;
        }

        /// <summary>
        /// Creates a minimal SourceFile that is NOT a disc image (e.g., a plain file).
        /// Has no IndexFile, so it won't be counted as CUE or GDI.
        /// </summary>
        private static SourceFile CreateNonDiscSourceFile(string dirPath, string fileName)
        {
            SourceFile sf = new SourceFile
            {
                Status = SourceFileResult.Valid,
                Name = fileName,
                CleanName = fileName,
                SystemType = SystemType.Default,
                ImageFiles = new[]
                {
                    new SourceFileItem(dirPath, fileName, Path.GetExtension(fileName), "", 0, 1024, 0, false, false)
                },
                // No IndexFile — not a disc image
            };
            return sf;
        }
    }
}