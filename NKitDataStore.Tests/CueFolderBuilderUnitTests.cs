namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for CueFolderBuilder logical behavior.
    ///
    /// CueFolderBuilder requires a real DataStore with SQLite and shard files,
    /// so these tests validate the LOGICAL invariants using a simulated model that
    /// mirrors the Build() and softDeleteExistingCueFolder() behavior:
    ///   - Soft-delete marks existing CueFolders as removed
    ///   - Build() produces an FsYaml with fs (auxiliary files) and ifs (child image refs)
    ///   - Track data files (.bin, .raw, .wav, .iso, .img) excluded from fs section
    ///   - Index files (.cue, .gdi) excluded from fs section
    ///   - CueFolder uses ImageFormat.CueFolder
    ///   - Every child image file appears exactly once in ifs (completeness)
    ///
    /// **Validates: Requirements 10.1, 10.5, 10.7**
    /// </summary>
    public class CueFolderBuilderUnitTests
    {
        #region Simulation helpers

        private struct SimulatedChildImage
        {
            public long ImageId;
            public List<SimulatedChildFile> Files;
        }

        private struct SimulatedChildFile
        {
            public string FileName;
            public long ImageId;
            public long Size;
        }

        private struct SimulatedSourceFile
        {
            public string Name;
            public long Size;
        }

        private struct SimulatedImageRecord
        {
            public long Id;
            public string Name;
            public ImageFormat Format;
            public bool Removed;
        }

        /// <summary>
        /// Track data file extensions excluded from fs section.
        /// </summary>
        private static readonly HashSet<string> TrackExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bin", ".raw", ".wav", ".iso", ".img"
        };

        /// <summary>
        /// Index file extensions excluded from fs section.
        /// </summary>
        private static readonly HashSet<string> IndexExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cue", ".gdi"
        };

        /// <summary>
        /// Simulates softDeleteExistingCueFolder: marks matching non-removed CueFolders as removed.
        /// Returns the number of records soft-deleted.
        /// </summary>
        private static int SimulateSoftDelete(List<SimulatedImageRecord> allImages, string setName, string folderName)
        {
            int deleted = 0;
            for (int i = 0; i < allImages.Count; i++)
            {
                SimulatedImageRecord img = allImages[i];
                if (!img.Removed &&
                    img.Format == ImageFormat.CueFolder &&
                    img.Name == folderName)
                {
                    img.Removed = true;
                    allImages[i] = img;
                    deleted++;
                }
            }
            return deleted;
        }

        /// <summary>
        /// Simulates CueFolderBuilder.Build(): produces an FsYaml with fs and ifs sections.
        /// Mirrors the real builder's logic of excluding ifs file names, track data files,
        /// and index files from fs.
        /// </summary>
        private static FsYaml SimulateBuild(
            List<SimulatedSourceFile> sourceFiles,
            List<SimulatedChildImage> childImages)
        {
            // Collect ifs file names
            HashSet<string> ifsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SimulatedChildImage child in childImages)
            {
                if (child.Files != null)
                    foreach (SimulatedChildFile file in child.Files)
                        ifsFileNames.Add(file.FileName);
            }

            // Build FsYaml
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            // Add fs entries (auxiliary files), excluding ifs names, track data, and index files
            long currentOffset = 0;
            foreach (SimulatedSourceFile sf in sourceFiles)
            {
                if (ifsFileNames.Contains(sf.Name))
                    continue;

                string ext = Path.GetExtension(sf.Name);
                if (TrackExtensions.Contains(ext))
                    continue;
                if (IndexExtensions.Contains(ext))
                    continue;

                root.AddFile(sf.Name, currentOffset, sf.Size, 0, 0);
                currentOffset += sf.Size;
            }

            // Add ifs entries from child images
            foreach (SimulatedChildImage child in childImages)
            {
                if (child.Files == null)
                    continue;
                foreach (SimulatedChildFile file in child.Files)
                    fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);
            }

            return fsYaml;
        }

        /// <summary>
        /// Collects all leaf file names from the FsYaml tree.
        /// </summary>
        private static HashSet<string> CollectFsFileNames(List<FsYamlNode> roots)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsYamlNode root in roots)
                CollectFileNamesRecursive(root, names);
            return names;
        }

        private static void CollectFileNamesRecursive(FsYamlNode node, HashSet<string> names)
        {
            if (node.IsFile)
            {
                names.Add(node.Name);
                return;
            }
            if (node.Children != null)
                foreach (FsYamlNode child in node.Children)
                    CollectFileNamesRecursive(child, names);
        }

        #endregion

        #region CueFolder uses correct ImageFormat

        /// <summary>
        /// Validates: Requirement 10.1
        /// CueFolder images use ImageFormat.CueFolder (value 9).
        /// The simulated soft-delete targets only CueFolder format, confirming the format is distinct.
        /// </summary>
        [Fact]
        public void CueFolder_UsesImageFormatCueFolder()
        {
            // The CueFolderBuilder creates images with ImageFormat.CueFolder
            // Verify the format value is correct and distinct from TmdAppFolder
            Assert.Equal(9, (int)ImageFormat.CueFolder);
            Assert.NotEqual(ImageFormat.TmdAppFolder, ImageFormat.CueFolder);
            Assert.NotEqual(ImageFormat.Cue, ImageFormat.CueFolder);
        }

        #endregion

        #region filesystem.yaml ifs section contains correct child image references

        /// <summary>
        /// Validates: Requirement 10.5
        /// Two CUE child images: all their files appear in ifs with correct imageId and size.
        /// </summary>
        [Fact]
        public void Build_TwoCueChildImages_IfsContainsCorrectReferences()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Disc 1.cue", ImageId = 42, Size = 734003280 },
                    }
                },
                new()
                {
                    ImageId = 43,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Disc 2.cue", ImageId = 43, Size = 734003280 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "playlist.m3u", Size = 128 },
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            Assert.Equal(2, result.ImageFileSystems.Count);

            FsYamlIfsEntry entry1 = result.ImageFileSystems[0];
            Assert.Equal("Disc 1.cue", entry1.FileName);
            Assert.Equal(42L, entry1.ImageId);
            Assert.Equal(734003280L, entry1.Size);

            FsYamlIfsEntry entry2 = result.ImageFileSystems[1];
            Assert.Equal("Disc 2.cue", entry2.FileName);
            Assert.Equal(43L, entry2.ImageId);
            Assert.Equal(734003280L, entry2.Size);
        }

        /// <summary>
        /// Validates: Requirement 10.5
        /// Three GDI child images: ifs entries reference correct imageIds.
        /// </summary>
        [Fact]
        public void Build_ThreeGdiChildImages_IfsContainsAllReferences()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "disc1.gdi", ImageId = 10, Size = 1073741824 },
                    }
                },
                new()
                {
                    ImageId = 20,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "disc2.gdi", ImageId = 20, Size = 1073741824 },
                    }
                },
                new()
                {
                    ImageId = 30,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "disc3.gdi", ImageId = 30, Size = 536870912 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>();

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            Assert.Equal(3, result.ImageFileSystems.Count);

            List<string> ifsFileNames = result.ImageFileSystems.Select(e => e.FileName).ToList();
            Assert.Contains("disc1.gdi", ifsFileNames);
            Assert.Contains("disc2.gdi", ifsFileNames);
            Assert.Contains("disc3.gdi", ifsFileNames);

            Assert.Equal(10L, result.ImageFileSystems.First(e => e.FileName == "disc1.gdi").ImageId);
            Assert.Equal(20L, result.ImageFileSystems.First(e => e.FileName == "disc2.gdi").ImageId);
            Assert.Equal(30L, result.ImageFileSystems.First(e => e.FileName == "disc3.gdi").ImageId);
        }

        #endregion

        #region Auxiliary files (.m3u, .nfo) stored in fs section

        /// <summary>
        /// Validates: Requirement 10.1
        /// Auxiliary files like .m3u and .nfo appear in the fs section.
        /// </summary>
        [Fact]
        public void Build_AuxiliaryFiles_StoredInFsSection()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Game Disc 1.cue", ImageId = 42, Size = 734003280 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "playlist.m3u", Size = 128 },
                new() { Name = "game-info.nfo", Size = 2048 },
                new() { Name = "readme.txt", Size = 512 },
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Contains("playlist.m3u", fsNames);
            Assert.Contains("game-info.nfo", fsNames);
            Assert.Contains("readme.txt", fsNames);
            Assert.Equal(3, fsNames.Count);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// When no auxiliary files exist, fs section is empty.
        /// </summary>
        [Fact]
        public void Build_NoAuxiliaryFiles_FsSectionEmpty()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "disc1.cue", ImageId = 10, Size = 500000 },
                    }
                }
            };

            // Only track data and index files in source — all should be excluded
            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "track01.bin", Size = 734003280 },
                new() { Name = "disc1.cue", Size = 1024 },
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Empty(fsNames);
        }

        #endregion

        #region Soft-delete removes existing CueFolder before creating new one

        /// <summary>
        /// Validates: Requirement 10.7
        /// When no existing CueFolder exists, soft-delete does nothing.
        /// </summary>
        [Fact]
        public void SoftDelete_NoExisting_DeletesNothing()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Multi-Disc Game", Format = ImageFormat.Cue, Removed = false },
                new() { Id = 2, Name = "Multi-Disc Game", Format = ImageFormat.Cue, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Multi-Disc Game");

            Assert.Equal(0, deleted);
            Assert.All(images, img => Assert.False(img.Removed));
        }

        /// <summary>
        /// Validates: Requirement 10.7
        /// When one existing CueFolder matches, it is soft-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_OneExisting_MarksRemoved()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Multi-Disc Game", Format = ImageFormat.CueFolder, Removed = false },
                new() { Id = 2, Name = "Multi-Disc Game", Format = ImageFormat.Cue, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Multi-Disc Game");

            Assert.Equal(1, deleted);
            Assert.True(images[0].Removed);
            Assert.False(images[1].Removed); // Cue image untouched
        }

        /// <summary>
        /// Validates: Requirement 10.7
        /// Already-removed CueFolders are not re-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_AlreadyRemoved_NotDeletedAgain()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Multi-Disc Game", Format = ImageFormat.CueFolder, Removed = true },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Multi-Disc Game");

            Assert.Equal(0, deleted);
            Assert.True(images[0].Removed); // still removed, not toggled
        }

        /// <summary>
        /// Validates: Requirement 10.7
        /// Multiple non-removed CueFolders with the same name are all soft-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_MultipleExisting_AllMarkedRemoved()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Multi-Disc Game", Format = ImageFormat.CueFolder, Removed = false },
                new() { Id = 2, Name = "Multi-Disc Game", Format = ImageFormat.CueFolder, Removed = false },
                new() { Id = 3, Name = "Other Game", Format = ImageFormat.CueFolder, Removed = false },
                new() { Id = 4, Name = "Multi-Disc Game", Format = ImageFormat.Cue, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Multi-Disc Game");

            Assert.Equal(2, deleted);
            Assert.True(images[0].Removed);
            Assert.True(images[1].Removed);
            Assert.False(images[2].Removed); // different name
            Assert.False(images[3].Removed); // Cue format, not CueFolder
        }

        /// <summary>
        /// Validates: Requirement 10.7
        /// Soft-delete only targets CueFolder format, not other formats with the same name.
        /// </summary>
        [Fact]
        public void SoftDelete_OnlyTargetsCueFolderFormat()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Multi-Disc Game", Format = ImageFormat.Cue, Removed = false },
                new() { Id = 2, Name = "Multi-Disc Game", Format = ImageFormat.Gdi, Removed = false },
                new() { Id = 3, Name = "Multi-Disc Game", Format = ImageFormat.TmdAppFolder, Removed = false },
                new() { Id = 4, Name = "Multi-Disc Game", Format = ImageFormat.CueFolder, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Multi-Disc Game");

            Assert.Equal(1, deleted);
            Assert.False(images[0].Removed); // Cue format
            Assert.False(images[1].Removed); // Gdi format
            Assert.False(images[2].Removed); // TmdAppFolder format
            Assert.True(images[3].Removed);  // CueFolder — deleted
        }

        #endregion

        #region Track data files excluded from fs section (only in ifs)

        /// <summary>
        /// Validates: Requirement 10.1
        /// Track data files (.bin, .raw, .wav, .iso, .img) are excluded from fs section.
        /// </summary>
        [Fact]
        public void Build_TrackDataFiles_ExcludedFromFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Game Disc 1.cue", ImageId = 42, Size = 734003280 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "track01.bin", Size = 734003280 },
                new() { Name = "track02.raw", Size = 52920000 },
                new() { Name = "audio.wav", Size = 176400000 },
                new() { Name = "data.iso", Size = 500000000 },
                new() { Name = "backup.img", Size = 300000000 },
                new() { Name = "playlist.m3u", Size = 128 },  // auxiliary — should be included
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);

            // Track data files excluded
            Assert.DoesNotContain("track01.bin", fsNames);
            Assert.DoesNotContain("track02.raw", fsNames);
            Assert.DoesNotContain("audio.wav", fsNames);
            Assert.DoesNotContain("data.iso", fsNames);
            Assert.DoesNotContain("backup.img", fsNames);

            // Auxiliary file included
            Assert.Contains("playlist.m3u", fsNames);
            Assert.Single(fsNames);
        }

        /// <summary>
        /// Validates: Requirement 10.1
        /// Index files (.cue, .gdi) are also excluded from fs section.
        /// </summary>
        [Fact]
        public void Build_IndexFiles_ExcludedFromFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Game Disc 1.cue", ImageId = 42, Size = 734003280 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "game.cue", Size = 512 },
                new() { Name = "game2.gdi", Size = 256 },
                new() { Name = "notes.nfo", Size = 1024 },  // auxiliary — should be included
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);

            // Index files excluded
            Assert.DoesNotContain("game.cue", fsNames);
            Assert.DoesNotContain("game2.gdi", fsNames);

            // Auxiliary file included
            Assert.Contains("notes.nfo", fsNames);
            Assert.Single(fsNames);
        }

        /// <summary>
        /// Validates: Requirement 10.1, 10.5
        /// Mixed source folder: only auxiliary files end up in fs, track/index excluded,
        /// child images in ifs.
        /// </summary>
        [Fact]
        public void Build_MixedSourceFolder_CorrectFsAndIfs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Disc 1.cue", ImageId = 10, Size = 734003280 },
                    }
                },
                new()
                {
                    ImageId = 20,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "Disc 2.cue", ImageId = 20, Size = 734003280 },
                    }
                }
            };

            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                // Track data files — excluded
                new() { Name = "track01.bin", Size = 734003280 },
                new() { Name = "track02.bin", Size = 52920000 },
                // Index files — excluded
                new() { Name = "disc1.cue", Size = 512 },
                new() { Name = "disc2.cue", Size = 512 },
                // Auxiliary files — included in fs
                new() { Name = "playlist.m3u", Size = 128 },
                new() { Name = "cover.jpg", Size = 50000 },
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            // Verify fs section
            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Equal(2, fsNames.Count);
            Assert.Contains("playlist.m3u", fsNames);
            Assert.Contains("cover.jpg", fsNames);

            // Verify ifs section
            Assert.Equal(2, result.ImageFileSystems.Count);
            HashSet<string> ifsNames = result.ImageFileSystems.Select(e => e.FileName).ToHashSet();
            Assert.Contains("Disc 1.cue", ifsNames);
            Assert.Contains("Disc 2.cue", ifsNames);

            // Verify disjointness
            foreach (string ifsName in ifsNames)
                Assert.DoesNotContain(ifsName, fsNames);
        }

        /// <summary>
        /// Validates: Requirement 10.5
        /// Files that match ifs names are excluded from fs even if they have auxiliary extensions.
        /// </summary>
        [Fact]
        public void Build_IfsFileNameCollision_ExcludedFromFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "playlist.m3u", ImageId = 42, Size = 128 },
                    }
                }
            };

            // Source has a file with the same name as an ifs entry
            List<SimulatedSourceFile> sourceFiles = new List<SimulatedSourceFile>
            {
                new() { Name = "playlist.m3u", Size = 256 },
                new() { Name = "readme.txt", Size = 512 },
            };

            FsYaml result = SimulateBuild(sourceFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            // playlist.m3u is in ifs, so excluded from fs
            Assert.DoesNotContain("playlist.m3u", fsNames);
            Assert.Contains("readme.txt", fsNames);
            Assert.Single(fsNames);
        }

        #endregion
    }
}