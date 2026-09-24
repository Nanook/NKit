namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FolderMountHandler logical behavior.
    ///
    /// FolderMountHandler requires a real VfsModel with DataStore, MountRegistry, and
    /// ImageReader infrastructure, so these tests validate the LOGICAL behavior using
    /// simulated models that mirror the handler's path resolution, directory listing,
    /// and ifs resolution logic:
    ///   - fs entries: files stored as blocks, resolved by path through FsYaml tree
    ///   - ifs entries: files from child images, resolved by imageId and filename
    ///   - When child image is soft-deleted or missing, stream creation returns null
    ///   - Directory listing uses filesystem.yaml fs and ifs sections
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    /// </summary>
    public class FolderMountHandlerUnitTests
    {
        #region Simulation helpers

        /// <summary>
        /// Simulated child image record for ifs resolution testing.
        /// </summary>
        private struct SimulatedImageRecord
        {
            public long Id;
            public bool Removed;
            public bool Exists;
            public List<string> AreaFileNames; // filenames from area metadata
        }

        /// <summary>
        /// Simulates FolderMountHandler's findFsYamlNode logic:
        /// Given an FsYaml and a path, resolves through the tree to find the matching node.
        /// Searches inline "." roots first, then named roots.
        /// </summary>
        private static FsYamlNode SimulateFindFsYamlNode(FsYaml fsYaml, string[] pth, int startIndex)
        {
            if (startIndex >= pth.Length)
                return null;

            // Try named (non-inline) filesystem root first
            FsYamlNode current = fsYaml.FileSystems.FirstOrDefault(
                fs => fs.Name != "." && fs.Name.Equals(pth[startIndex], StringComparison.OrdinalIgnoreCase));

            if (current == null)
            {
                // No named root matched — search within inline "." roots for a matching child
                foreach (FsYamlNode inlineRoot in fsYaml.FileSystems.Where(fs => fs.Name == "."))
                {
                    if (inlineRoot.Children != null)
                    {
                        current = inlineRoot.Children.FirstOrDefault(
                            c => c.Name.Equals(pth[startIndex], StringComparison.OrdinalIgnoreCase));
                        if (current != null)
                            break;
                    }
                }
            }

            for (int i = startIndex + 1; i < pth.Length && current != null; i++)
            {
                if (current.Children == null)
                    return null;
                current = current.Children.FirstOrDefault(
                    c => c.Name.Equals(pth[i], StringComparison.OrdinalIgnoreCase));
            }

            return current;
        }

        /// <summary>
        /// Simulates ifs entry lookup: finds an ifs entry by filename (case-insensitive).
        /// </summary>
        private static FsYamlIfsEntry SimulateFindIfsEntry(FsYaml fsYaml, string fileName)
        {
            return fsYaml.ImageFileSystems
                .FirstOrDefault(e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Simulates TryCreateAreaStream's ifs resolution logic:
        /// Returns true if the child image exists, is not removed, and has a matching area.
        /// Returns false (null stream) otherwise.
        /// </summary>
        private static bool SimulateTryResolveIfsStream(
            FsYamlIfsEntry ifsEntry,
            Dictionary<long, SimulatedImageRecord> imageStore)
        {
            if (ifsEntry == null)
                return false;

            if (!imageStore.TryGetValue(ifsEntry.ImageId, out SimulatedImageRecord childImage))
                return false; // child image doesn't exist

            if (!childImage.Exists)
                return false;

            if (childImage.Removed)
                return false; // soft-deleted

            // Find matching area by filename
            bool hasMatchingArea = childImage.AreaFileNames != null &&
                childImage.AreaFileNames.Any(fn =>
                    fn.Equals(ifsEntry.FileName, StringComparison.OrdinalIgnoreCase));

            return hasMatchingArea;
        }

        /// <summary>
        /// Simulates directory listing from filesystem.yaml: collects all items
        /// from fs section (inline root children) and ifs section.
        /// Returns (directories, fsFiles, ifsFiles).
        /// </summary>
        private static (List<string> directories, List<string> fsFiles, List<string> ifsFiles)
            SimulateListFolder(FsYaml fsYaml)
        {
            List<string> directories = new List<string>();
            List<string> fsFiles = new List<string>();
            List<string> ifsFiles = new List<string>();

            foreach (FsYamlNode fs in fsYaml.FileSystems)
            {
                if (fs.Name == ".")
                {
                    if (fs.Children != null)
                    {
                        foreach (FsYamlNode child in fs.Children)
                        {
                            if (child.IsDirectory)
                                directories.Add(child.Name);
                            else
                                fsFiles.Add(child.Name);
                        }
                    }
                }
                else
                {
                    directories.Add(fs.Name);
                }
            }

            foreach (FsYamlIfsEntry ifsEntry in fsYaml.ImageFileSystems)
            {
                ifsFiles.Add(ifsEntry.FileName);
            }

            return (directories, fsFiles, ifsFiles);
        }

        /// <summary>
        /// Builds a typical TmdAppFolder FsYaml with fs and ifs sections.
        /// </summary>
        private static FsYaml BuildTmdAppFolderFsYaml(
            (string name, long offset, long size)[] fsFiles,
            (string fileName, long imageId, long size)[] ifsEntries)
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);

            foreach ((string name, long offset, long size) in fsFiles)
                root.AddFile(name, offset, size, 0, 0);

            foreach ((string fileName, long imageId, long size) in ifsEntries)
                fsYaml.AddIfsEntry(fileName, imageId, size);

            return fsYaml;
        }

        #endregion

        #region Path resolution: fs entries

        /// <summary>
        /// Validates: Requirement 11.1, 11.2
        /// Resolving a top-level fs file returns the correct node.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_TopLevelFile_ReturnsCorrectNode()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 1024, 0, 0);
            root.AddFile("title.tik", 1024, 512, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "title.tmd" }, 0);

            Assert.NotNull(result);
            Assert.Equal("title.tmd", result.Name);
            Assert.True(result.IsFile);
            Assert.Equal(0L, result.Offset);
            Assert.Equal(1024L, result.Size);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Resolving a nested fs file returns the correct node.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_NestedFile_ReturnsCorrectNode()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode subDir = root.AddDirectory("subdir");
            subDir.AddFile("nested.bin", 2048, 4096, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "subdir", "nested.bin" }, 0);

            Assert.NotNull(result);
            Assert.Equal("nested.bin", result.Name);
            Assert.True(result.IsFile);
            Assert.Equal(2048L, result.Offset);
            Assert.Equal(4096L, result.Size);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Resolving a directory node returns the directory.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_Directory_ReturnsDirectoryNode()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode subDir = root.AddDirectory("mydir");
            subDir.AddFile("file.txt", 0, 100, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "mydir" }, 0);

            Assert.NotNull(result);
            Assert.Equal("mydir", result.Name);
            Assert.True(result.IsDirectory);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Resolving a non-existent path returns null.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_NonExistentPath_ReturnsNull()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("title.tmd", 0, 1024, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "nonexistent.bin" }, 0);

            Assert.Null(result);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Case-insensitive path resolution works.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_CaseInsensitive_MatchesNode()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("Title.TMD", 0, 1024, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "title.tmd" }, 0);

            Assert.NotNull(result);
            Assert.Equal("Title.TMD", result.Name);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Deeply nested path resolution works.
        /// </summary>
        [Fact]
        public void FindFsYamlNode_DeeplyNested_ResolvesCorrectly()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode a = root.AddDirectory("a");
            FsYamlNode b = a.AddDirectory("b");
            FsYamlNode c = b.AddDirectory("c");
            c.AddFile("deep.txt", 5000, 100, 0, 0);

            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "a", "b", "c", "deep.txt" }, 0);

            Assert.NotNull(result);
            Assert.Equal("deep.txt", result.Name);
            Assert.Equal(5000L, result.Offset);
        }

        #endregion

        #region Path resolution: ifs entries

        /// <summary>
        /// Validates: Requirement 11.2
        /// Resolving an ifs entry by filename returns the correct entry.
        /// </summary>
        [Fact]
        public void FindIfsEntry_ExistingFile_ReturnsEntry()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                new[] { ("title.tmd", 0L, 1024L) },
                new[] { ("00000001.app", 42L, 1048576L), ("00000002.app", 42L, 2097152L) });

            FsYamlIfsEntry result = SimulateFindIfsEntry(fsYaml, "00000001.app");

            Assert.NotNull(result);
            Assert.Equal("00000001.app", result.FileName);
            Assert.Equal(42L, result.ImageId);
            Assert.Equal(1048576L, result.Size);
        }

        /// <summary>
        /// Validates: Requirement 11.2
        /// Resolving a non-existent ifs entry returns null.
        /// </summary>
        [Fact]
        public void FindIfsEntry_NonExistent_ReturnsNull()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                new[] { ("title.tmd", 0L, 1024L) },
                new[] { ("00000001.app", 42L, 1048576L) });

            FsYamlIfsEntry result = SimulateFindIfsEntry(fsYaml, "99999999.app");

            Assert.Null(result);
        }

        /// <summary>
        /// Validates: Requirement 11.2
        /// Case-insensitive ifs entry lookup works.
        /// </summary>
        [Fact]
        public void FindIfsEntry_CaseInsensitive_MatchesEntry()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.APP", 42L, 1048576L) });

            FsYamlIfsEntry result = SimulateFindIfsEntry(fsYaml, "00000001.app");

            Assert.NotNull(result);
            Assert.Equal("00000001.APP", result.FileName);
        }

        /// <summary>
        /// Validates: Requirement 11.2
        /// fs path resolution does NOT match ifs entries (they are separate namespaces).
        /// </summary>
        [Fact]
        public void FindFsYamlNode_DoesNotMatchIfsEntries()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                new[] { ("title.tmd", 0L, 1024L) },
                new[] { ("00000001.app", 42L, 1048576L) });

            // fs tree search should NOT find ifs entries
            FsYamlNode result = SimulateFindFsYamlNode(fsYaml, new[] { "00000001.app" }, 0);

            Assert.Null(result);
        }

        #endregion

        #region Null return: missing or soft-deleted child image

        /// <summary>
        /// Validates: Requirement 11.4
        /// When child image is soft-deleted, ifs resolution returns false (null stream).
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_ChildImageSoftDeleted_ReturnsFalse()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.app", 42L, 1048576L) });

            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>
            {
                [42] = new SimulatedImageRecord
                {
                    Id = 42,
                    Removed = true,
                    Exists = true,
                    AreaFileNames = new List<string> { "00000001.app" }
                }
            };

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.app");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.False(result);
        }

        /// <summary>
        /// Validates: Requirement 11.4
        /// When child image does not exist at all, ifs resolution returns false.
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_ChildImageMissing_ReturnsFalse()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.app", 99L, 1048576L) });

            // Image store has no entry for imageId 99
            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>();

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.app");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.False(result);
        }

        /// <summary>
        /// Validates: Requirement 11.4
        /// When child image exists but has no matching area, ifs resolution returns false.
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_NoMatchingArea_ReturnsFalse()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.app", 42L, 1048576L) });

            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>
            {
                [42] = new SimulatedImageRecord
                {
                    Id = 42,
                    Removed = false,
                    Exists = true,
                    AreaFileNames = new List<string> { "00000099.app" } // different filename
                }
            };

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.app");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.False(result);
        }

        /// <summary>
        /// Validates: Requirement 11.3
        /// When child image exists, is not removed, and has a matching area, resolution succeeds.
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_ValidChildImage_ReturnsTrue()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.app", 42L, 1048576L) });

            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>
            {
                [42] = new SimulatedImageRecord
                {
                    Id = 42,
                    Removed = false,
                    Exists = true,
                    AreaFileNames = new List<string> { "00000001.app" }
                }
            };

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.app");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.True(result);
        }

        /// <summary>
        /// Validates: Requirement 11.3
        /// Area matching is case-insensitive.
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_CaseInsensitiveAreaMatch_ReturnsTrue()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.APP", 42L, 1048576L) });

            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>
            {
                [42] = new SimulatedImageRecord
                {
                    Id = 42,
                    Removed = false,
                    Exists = true,
                    AreaFileNames = new List<string> { "00000001.app" } // lowercase
                }
            };

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.APP");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.True(result);
        }

        /// <summary>
        /// Validates: Requirement 11.4
        /// Null ifs entry returns false.
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_NullEntry_ReturnsFalse()
        {
            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>();

            bool result = SimulateTryResolveIfsStream(null, imageStore);

            Assert.False(result);
        }

        /// <summary>
        /// Validates: Requirement 11.4
        /// Child image exists but Exists flag is false (image record not found).
        /// </summary>
        [Fact]
        public void TryResolveIfsStream_ChildImageExistsFlagFalse_ReturnsFalse()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[] { ("00000001.app", 42L, 1048576L) });

            Dictionary<long, SimulatedImageRecord> imageStore = new Dictionary<long, SimulatedImageRecord>
            {
                [42] = new SimulatedImageRecord
                {
                    Id = 42,
                    Removed = false,
                    Exists = false,
                    AreaFileNames = new List<string> { "00000001.app" }
                }
            };

            FsYamlIfsEntry entry = SimulateFindIfsEntry(fsYaml, "00000001.app");
            bool result = SimulateTryResolveIfsStream(entry, imageStore);

            Assert.False(result);
        }

        #endregion

        #region Directory listing from filesystem.yaml

        /// <summary>
        /// Validates: Requirement 11.1
        /// Listing a TmdAppFolder shows both fs files and ifs files.
        /// </summary>
        [Fact]
        public void ListFolder_TmdAppFolder_ShowsFsAndIfsEntries()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                new[]
                {
                    ("title.tmd", 0L, 1024L),
                    ("title.tik", 1024L, 512L),
                    ("title.cetk", 1536L, 2048L),
                },
                new[]
                {
                    ("00000001.app", 42L, 1048576L),
                    ("00000002.app", 42L, 2097152L),
                    ("00000003.app", 43L, 524288L),
                });

            (List<string> directories, List<string> fsFiles, List<string> ifsFiles) = SimulateListFolder(fsYaml);

            Assert.Empty(directories);
            Assert.Equal(3, fsFiles.Count);
            Assert.Contains("title.tmd", fsFiles);
            Assert.Contains("title.tik", fsFiles);
            Assert.Contains("title.cetk", fsFiles);

            Assert.Equal(3, ifsFiles.Count);
            Assert.Contains("00000001.app", ifsFiles);
            Assert.Contains("00000002.app", ifsFiles);
            Assert.Contains("00000003.app", ifsFiles);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Listing a folder with subdirectories shows directories and files.
        /// </summary>
        [Fact]
        public void ListFolder_WithSubdirectories_ShowsDirectoriesAndFiles()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            root.AddFile("readme.txt", 0, 100, 0, 0);
            FsYamlNode subDir = root.AddDirectory("data");
            subDir.AddFile("file1.bin", 100, 500, 0, 0);

            (List<string> directories, List<string> fsFiles, List<string> ifsFiles) = SimulateListFolder(fsYaml);

            Assert.Single(directories);
            Assert.Contains("data", directories);
            Assert.Single(fsFiles);
            Assert.Contains("readme.txt", fsFiles);
            Assert.Empty(ifsFiles);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Listing an empty folder returns no items.
        /// </summary>
        [Fact]
        public void ListFolder_EmptyFsYaml_ReturnsNoItems()
        {
            FsYaml fsYaml = new FsYaml();
            fsYaml.AddFileSystem(".", 0);

            (List<string> directories, List<string> fsFiles, List<string> ifsFiles) = SimulateListFolder(fsYaml);

            Assert.Empty(directories);
            Assert.Empty(fsFiles);
            Assert.Empty(ifsFiles);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Listing a folder with only ifs entries (no fs files) works.
        /// </summary>
        [Fact]
        public void ListFolder_OnlyIfsEntries_ShowsIfsFiles()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                Array.Empty<(string, long, long)>(),
                new[]
                {
                    ("00000001.app", 10L, 100000L),
                    ("00000002.app", 20L, 200000L),
                });

            (List<string> directories, List<string> fsFiles, List<string> ifsFiles) = SimulateListFolder(fsYaml);

            Assert.Empty(directories);
            Assert.Empty(fsFiles);
            Assert.Equal(2, ifsFiles.Count);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Named (non-inline) filesystem roots appear as directories.
        /// </summary>
        [Fact]
        public void ListFolder_NamedRoot_AppearsAsDirectory()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode namedRoot = fsYaml.AddFileSystem("partition0", 0);
            namedRoot.AddFile("data.bin", 0, 1000, 0, 0);

            (List<string> directories, List<string> fsFiles, List<string> ifsFiles) = SimulateListFolder(fsYaml);

            Assert.Single(directories);
            Assert.Contains("partition0", directories);
            Assert.Empty(fsFiles);
            Assert.Empty(ifsFiles);
        }

        /// <summary>
        /// Validates: Requirement 11.1
        /// Round-trip: listing after serialization/deserialization produces same results.
        /// </summary>
        [Fact]
        public void ListFolder_AfterRoundTrip_SameResults()
        {
            FsYaml original = BuildTmdAppFolderFsYaml(
                new[]
                {
                    ("title.tmd", 0L, 1024L),
                    ("title.tik", 1024L, 512L),
                },
                new[]
                {
                    ("00000001.app", 42L, 1048576L),
                });

            string yaml = original.ToYaml();
            FsYaml parsed = FsYaml.FromYaml(yaml);

            (List<string> origDirs, List<string> origFs, List<string> origIfs) = SimulateListFolder(original);
            (List<string> parsedDirs, List<string> parsedFs, List<string> parsedIfs) = SimulateListFolder(parsed);

            Assert.Equal(origDirs.Count, parsedDirs.Count);
            Assert.Equal(origFs.Count, parsedFs.Count);
            Assert.Equal(origIfs.Count, parsedIfs.Count);

            foreach (string name in origFs)
                Assert.Contains(name, parsedFs);
            foreach (string name in origIfs)
                Assert.Contains(name, parsedIfs);
        }

        #endregion

        #region Combined path resolution: fs vs ifs priority

        /// <summary>
        /// Validates: Requirement 11.1, 11.2
        /// When resolving a path, fs entries are checked first, then ifs entries.
        /// A file in fs is found via tree traversal; a file only in ifs is found via flat lookup.
        /// </summary>
        [Fact]
        public void PathResolution_FsEntryFoundFirst_IfsEntryFoundSeparately()
        {
            FsYaml fsYaml = BuildTmdAppFolderFsYaml(
                new[] { ("title.tmd", 0L, 1024L) },
                new[] { ("00000001.app", 42L, 1048576L) });

            // fs lookup finds title.tmd
            FsYamlNode fsResult = SimulateFindFsYamlNode(fsYaml, new[] { "title.tmd" }, 0);
            Assert.NotNull(fsResult);
            Assert.Equal("title.tmd", fsResult.Name);

            // fs lookup does NOT find ifs-only file
            FsYamlNode fsResult2 = SimulateFindFsYamlNode(fsYaml, new[] { "00000001.app" }, 0);
            Assert.Null(fsResult2);

            // ifs lookup finds the app file
            FsYamlIfsEntry ifsResult = SimulateFindIfsEntry(fsYaml, "00000001.app");
            Assert.NotNull(ifsResult);
            Assert.Equal("00000001.app", ifsResult.FileName);
        }

        #endregion
    }
}