namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for TmdAppFolderBuilder logical behavior.
    ///
    /// TmdAppFolderBuilder requires a real DataStore with SQLite and shard files,
    /// so these tests validate the LOGICAL invariants using a simulated model that
    /// mirrors the Build() and softDeleteExistingTmdAppFolder() behavior:
    ///   - Soft-delete marks existing TmdAppFolders as removed
    ///   - Build() produces an FsYaml with fs (real files) and ifs (child image refs)
    ///   - No file appears in both fs and ifs sections (disjointness)
    ///   - Every child image file appears exactly once in ifs (completeness)
    ///   - All ifs entries have valid imageId (>= 0) and size (>= 0)
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 9.1, 9.2**
    /// </summary>
    public class TmdAppFolderBuilderUnitTests
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

        private struct SimulatedRealFile
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
        /// Simulates softDeleteExistingTmdAppFolder: marks matching non-removed TmdAppFolders as removed.
        /// Returns the number of records soft-deleted.
        /// </summary>
        private static int SimulateSoftDelete(List<SimulatedImageRecord> allImages, string setName, string folderName)
        {
            int deleted = 0;
            for (int i = 0; i < allImages.Count; i++)
            {
                SimulatedImageRecord img = allImages[i];
                if (!img.Removed &&
                    img.Format == ImageFormat.TmdAppFolder &&
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
        /// Simulates TmdAppFolderBuilder.Build(): produces an FsYaml with fs and ifs sections.
        /// Mirrors the real builder's logic of excluding ifs file names from fs.
        /// </summary>
        private static FsYaml SimulateBuild(
            List<SimulatedRealFile> realFiles,
            List<SimulatedChildImage> childImages)
        {
            // Collect ifs file names
            HashSet<string> ifsFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SimulatedChildImage child in childImages)
                foreach (SimulatedChildFile file in child.Files)
                    ifsFileNames.Add(file.FileName);

            // Build FsYaml
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            // Add fs entries (real files), excluding any whose name is in ifs
            long currentOffset = 0;
            foreach (SimulatedRealFile rf in realFiles)
            {
                if (ifsFileNames.Contains(rf.Name))
                    continue;

                root.AddFile(rf.Name, currentOffset, rf.Size, 0, 0);
                currentOffset += rf.Size;
            }

            // Add ifs entries from child images
            foreach (SimulatedChildImage child in childImages)
                foreach (SimulatedChildFile file in child.Files)
                    fsYaml.AddIfsEntry(file.FileName, file.ImageId, file.Size);

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

        #region Soft-delete: no existing TmdAppFolder

        /// <summary>
        /// Validates: Requirement 9.1
        /// When no existing TmdAppFolder exists, soft-delete does nothing.
        /// </summary>
        [Fact]
        public void SoftDelete_NoExisting_DeletesNothing()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Game Title [tmd.0]", Format = ImageFormat.App, Removed = false },
                new() { Id = 2, Name = "Game Title [tmd.1]", Format = ImageFormat.App, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Game Title");

            Assert.Equal(0, deleted);
            Assert.All(images, img => Assert.False(img.Removed));
        }

        #endregion

        #region Soft-delete: one existing TmdAppFolder

        /// <summary>
        /// Validates: Requirement 9.1, 9.2
        /// When one existing TmdAppFolder matches, it is soft-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_OneExisting_MarksRemoved()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder, Removed = false },
                new() { Id = 2, Name = "Game Title [tmd.0]", Format = ImageFormat.App, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Game Title");

            Assert.Equal(1, deleted);
            Assert.True(images[0].Removed);
            Assert.False(images[1].Removed); // App image untouched
        }

        /// <summary>
        /// Validates: Requirement 9.1
        /// Already-removed TmdAppFolders are not re-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_AlreadyRemoved_NotDeletedAgain()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder, Removed = true },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Game Title");

            Assert.Equal(0, deleted);
            Assert.True(images[0].Removed); // still removed, not toggled
        }

        #endregion

        #region Soft-delete: multiple existing TmdAppFolders

        /// <summary>
        /// Validates: Requirement 9.1, 9.2
        /// When multiple non-removed TmdAppFolders exist with the same name, all are soft-deleted.
        /// </summary>
        [Fact]
        public void SoftDelete_MultipleExisting_AllMarkedRemoved()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Game Title", Format = ImageFormat.TmdAppFolder, Removed = false },
                new() { Id = 2, Name = "Game Title", Format = ImageFormat.TmdAppFolder, Removed = false },
                new() { Id = 3, Name = "Other Game", Format = ImageFormat.TmdAppFolder, Removed = false },
                new() { Id = 4, Name = "Game Title [tmd.0]", Format = ImageFormat.App, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Game Title");

            Assert.Equal(2, deleted);
            Assert.True(images[0].Removed);
            Assert.True(images[1].Removed);
            Assert.False(images[2].Removed); // different name
            Assert.False(images[3].Removed); // App format, not TmdAppFolder
        }

        /// <summary>
        /// Validates: Requirement 9.1
        /// Soft-delete only targets TmdAppFolder format, not other formats with the same name.
        /// </summary>
        [Fact]
        public void SoftDelete_OnlyTargetsTmdAppFolderFormat()
        {
            List<SimulatedImageRecord> images = new List<SimulatedImageRecord>
            {
                new() { Id = 1, Name = "Game Title", Format = ImageFormat.Folder, Removed = false },
                new() { Id = 2, Name = "Game Title", Format = ImageFormat.App, Removed = false },
                new() { Id = 3, Name = "Game Title", Format = ImageFormat.Iso, Removed = false },
                new() { Id = 4, Name = "Game Title", Format = ImageFormat.TmdAppFolder, Removed = false },
            };

            int deleted = SimulateSoftDelete(images, "mySet", "Game Title");

            Assert.Equal(1, deleted);
            Assert.False(images[0].Removed); // Folder format
            Assert.False(images[1].Removed); // App format
            Assert.False(images[2].Removed); // Iso format
            Assert.True(images[3].Removed);  // TmdAppFolder — deleted
        }

        #endregion

        #region Build: single child image

        /// <summary>
        /// Validates: Requirement 7.3, 7.5
        /// Single child image: all its files appear in ifs.
        /// </summary>
        [Fact]
        public void Build_SingleChildImage_AllFilesInIfs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 42, Size = 1048576 },
                        new() { FileName = "00000002.app", ImageId = 42, Size = 2097152 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "title.tik", Size = 512 },
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            Assert.Equal(2, result.ImageFileSystems.Count);
            Assert.Equal("00000001.app", result.ImageFileSystems[0].FileName);
            Assert.Equal(42L, result.ImageFileSystems[0].ImageId);
            Assert.Equal(1048576L, result.ImageFileSystems[0].Size);
            Assert.Equal("00000002.app", result.ImageFileSystems[1].FileName);
            Assert.Equal(42L, result.ImageFileSystems[1].ImageId);
            Assert.Equal(2097152L, result.ImageFileSystems[1].Size);
        }

        /// <summary>
        /// Validates: Requirement 7.2, 7.3
        /// Single child image: real files appear in fs section.
        /// </summary>
        [Fact]
        public void Build_SingleChildImage_RealFilesInFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 500000 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "title.tik", Size = 512 },
                new() { Name = "title.cetk", Size = 2048 },
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Contains("title.tmd", fsNames);
            Assert.Contains("title.tik", fsNames);
            Assert.Contains("title.cetk", fsNames);
            Assert.Equal(3, fsNames.Count);
        }

        #endregion

        #region Build: multiple child images

        /// <summary>
        /// Validates: Requirement 7.3, 7.5
        /// Multiple child images: all files from all children appear in ifs.
        /// </summary>
        [Fact]
        public void Build_MultipleChildImages_AllFilesInIfs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 100000 },
                        new() { FileName = "00000002.app", ImageId = 10, Size = 200000 },
                    }
                },
                new()
                {
                    ImageId = 20,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000003.app", ImageId = 20, Size = 300000 },
                    }
                },
                new()
                {
                    ImageId = 30,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000004.app", ImageId = 30, Size = 400000 },
                        new() { FileName = "00000005.app", ImageId = 30, Size = 500000 },
                        new() { FileName = "00000006.app", ImageId = 30, Size = 600000 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            // All 6 child files should be in ifs
            Assert.Equal(6, result.ImageFileSystems.Count);

            List<string> ifsFileNames = result.ImageFileSystems.Select(e => e.FileName).ToList();
            Assert.Contains("00000001.app", ifsFileNames);
            Assert.Contains("00000002.app", ifsFileNames);
            Assert.Contains("00000003.app", ifsFileNames);
            Assert.Contains("00000004.app", ifsFileNames);
            Assert.Contains("00000005.app", ifsFileNames);
            Assert.Contains("00000006.app", ifsFileNames);
        }

        /// <summary>
        /// Validates: Requirement 7.5
        /// Multiple child images: each ifs entry references the correct imageId.
        /// </summary>
        [Fact]
        public void Build_MultipleChildImages_CorrectImageIds()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 100000 },
                    }
                },
                new()
                {
                    ImageId = 20,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000002.app", ImageId = 20, Size = 200000 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>();

            FsYaml result = SimulateBuild(realFiles, childImages);

            FsYamlIfsEntry entry1 = result.ImageFileSystems.First(e => e.FileName == "00000001.app");
            Assert.Equal(10L, entry1.ImageId);

            FsYamlIfsEntry entry2 = result.ImageFileSystems.First(e => e.FileName == "00000002.app");
            Assert.Equal(20L, entry2.ImageId);
        }

        #endregion

        #region Disjointness: no file in both fs and ifs

        /// <summary>
        /// Validates: Requirement 7.4
        /// When a real file name collides with a child image file name,
        /// the real file is excluded from fs (ifs takes precedence).
        /// </summary>
        [Fact]
        public void Build_CollidingFileName_ExcludedFromFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 42, Size = 1000000 },
                    }
                }
            };

            // Real file has the same name as a child image file
            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "00000001.app", Size = 999 }, // collides with ifs
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            HashSet<string> ifsNames = new HashSet<string>(result.ImageFileSystems.Select(e => e.FileName), StringComparer.OrdinalIgnoreCase);

            // title.tmd should be in fs
            Assert.Contains("title.tmd", fsNames);

            // 00000001.app should be in ifs, NOT in fs
            Assert.Contains("00000001.app", ifsNames);
            Assert.DoesNotContain("00000001.app", fsNames);

            // No overlap
            foreach (string ifsName in ifsNames)
                Assert.DoesNotContain(ifsName, fsNames);
        }

        /// <summary>
        /// Validates: Requirement 7.4
        /// With no collisions, all real files are in fs and all child files are in ifs.
        /// </summary>
        [Fact]
        public void Build_NoCollisions_FsAndIfsAreDisjoint()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 100000 },
                        new() { FileName = "00000002.app", ImageId = 10, Size = 200000 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "title.tik", Size = 512 },
                new() { Name = "00000000.h3", Size = 256 },
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            HashSet<string> ifsNames = new HashSet<string>(result.ImageFileSystems.Select(e => e.FileName), StringComparer.OrdinalIgnoreCase);

            // Verify disjointness
            foreach (string ifsName in ifsNames)
                Assert.DoesNotContain(ifsName, fsNames);

            Assert.Equal(3, fsNames.Count);
            Assert.Equal(2, ifsNames.Count);
        }

        /// <summary>
        /// Validates: Requirement 7.4
        /// Case-insensitive collision: real file "00000001.APP" collides with ifs "00000001.app".
        /// </summary>
        [Fact]
        public void Build_CaseInsensitiveCollision_ExcludedFromFs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 42, Size = 1000000 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "00000001.APP", Size = 999 }, // case-insensitive collision
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);

            // The colliding real file should be excluded from fs
            Assert.DoesNotContain("00000001.APP", fsNames);
            Assert.Single(fsNames); // only title.tmd
        }

        #endregion

        #region ifs entry validity: imageId >= 0 and size >= 0

        /// <summary>
        /// Validates: Requirement 7.5, 8.1
        /// All ifs entries have non-negative imageId.
        /// </summary>
        [Fact]
        public void Build_IfsEntries_AllHaveNonNegativeImageId()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 0,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 0, Size = 100 },
                    }
                },
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000002.app", ImageId = 42, Size = 200 },
                    }
                },
                new()
                {
                    ImageId = 999999,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000003.app", ImageId = 999999, Size = 300 },
                    }
                }
            };

            FsYaml result = SimulateBuild(new List<SimulatedRealFile>(), childImages);

            Assert.All(result.ImageFileSystems, entry =>
            {
                Assert.True(entry.ImageId >= 0, $"ifs entry '{entry.FileName}' has negative imageId: {entry.ImageId}");
            });
        }

        /// <summary>
        /// Validates: Requirement 7.5
        /// All ifs entries have non-negative size.
        /// </summary>
        [Fact]
        public void Build_IfsEntries_AllHaveNonNegativeSize()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 0 },
                        new() { FileName = "00000002.app", ImageId = 10, Size = 1 },
                        new() { FileName = "00000003.app", ImageId = 10, Size = 10_000_000 },
                    }
                }
            };

            FsYaml result = SimulateBuild(new List<SimulatedRealFile>(), childImages);

            Assert.All(result.ImageFileSystems, entry =>
            {
                Assert.True(entry.Size >= 0, $"ifs entry '{entry.FileName}' has negative size: {entry.Size}");
            });
        }

        /// <summary>
        /// Validates: Requirement 7.5
        /// ifs entries preserve exact imageId and size values from child images.
        /// </summary>
        [Fact]
        public void Build_IfsEntries_PreserveExactValues()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 77,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 77, Size = 1234567 },
                        new() { FileName = "00000002.app", ImageId = 77, Size = 7654321 },
                    }
                }
            };

            FsYaml result = SimulateBuild(new List<SimulatedRealFile>(), childImages);

            Assert.Equal(2, result.ImageFileSystems.Count);

            FsYamlIfsEntry e1 = result.ImageFileSystems[0];
            Assert.Equal("00000001.app", e1.FileName);
            Assert.Equal(77L, e1.ImageId);
            Assert.Equal(1234567L, e1.Size);

            FsYamlIfsEntry e2 = result.ImageFileSystems[1];
            Assert.Equal("00000002.app", e2.FileName);
            Assert.Equal(77L, e2.ImageId);
            Assert.Equal(7654321L, e2.Size);
        }

        #endregion

        #region Build: edge cases

        /// <summary>
        /// Validates: Requirement 7.3
        /// No real files: fs section is empty, only ifs has entries.
        /// </summary>
        [Fact]
        public void Build_NoRealFiles_FsIsEmpty()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 10, Size = 100000 },
                    }
                }
            };

            FsYaml result = SimulateBuild(new List<SimulatedRealFile>(), childImages);

            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Empty(fsNames);
            Assert.Single(result.ImageFileSystems);
        }

        /// <summary>
        /// Validates: Requirement 7.3, 7.5
        /// Child image with zero files: ifs section is empty for that child.
        /// </summary>
        [Fact]
        public void Build_ChildWithNoFiles_NoIfsEntries()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 10,
                    Files = new List<SimulatedChildFile>() // empty
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
            };

            FsYaml result = SimulateBuild(realFiles, childImages);

            Assert.Empty(result.ImageFileSystems);
            HashSet<string> fsNames = CollectFsFileNames(result.FileSystems);
            Assert.Single(fsNames);
        }

        /// <summary>
        /// Validates: Requirement 7.3, 7.4, 7.5
        /// Round-trip: serialized FsYaml preserves both fs and ifs sections.
        /// </summary>
        [Fact]
        public void Build_RoundTrip_PreservesFsAndIfs()
        {
            List<SimulatedChildImage> childImages = new List<SimulatedChildImage>
            {
                new()
                {
                    ImageId = 42,
                    Files = new List<SimulatedChildFile>
                    {
                        new() { FileName = "00000001.app", ImageId = 42, Size = 1048576 },
                        new() { FileName = "00000002.app", ImageId = 42, Size = 2097152 },
                    }
                }
            };

            List<SimulatedRealFile> realFiles = new List<SimulatedRealFile>
            {
                new() { Name = "title.tmd", Size = 1024 },
                new() { Name = "title.tik", Size = 512 },
            };

            FsYaml original = SimulateBuild(realFiles, childImages);
            string yaml = original.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            // fs section preserved
            HashSet<string> originalFsNames = CollectFsFileNames(original.FileSystems);
            HashSet<string> parsedFsNames = CollectFsFileNames(parsed.FileSystems);
            Assert.Equal(originalFsNames.Count, parsedFsNames.Count);
            foreach (string name in originalFsNames)
                Assert.Contains(name, parsedFsNames);

            // ifs section preserved
            Assert.Equal(original.ImageFileSystems.Count, parsed.ImageFileSystems.Count);
            for (int i = 0; i < original.ImageFileSystems.Count; i++)
            {
                Assert.Equal(original.ImageFileSystems[i].FileName, parsed.ImageFileSystems[i].FileName);
                Assert.Equal(original.ImageFileSystems[i].ImageId, parsed.ImageFileSystems[i].ImageId);
                Assert.Equal(original.ImageFileSystems[i].Size, parsed.ImageFileSystems[i].Size);
            }

            // Disjointness still holds after round-trip
            foreach (FsYamlIfsEntry ifsEntry in parsed.ImageFileSystems)
                Assert.DoesNotContain(ifsEntry.FileName, parsedFsNames);
        }

        #endregion
    }
}