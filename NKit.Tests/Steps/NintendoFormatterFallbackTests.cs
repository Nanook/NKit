using Nanook.NKit;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>
    /// Unit tests for Nintendo formatter (GameCube, Wii, WiiU) fallback behavior.
    /// When the Filesystem_Fidelity_Collection is not populated (null), the formatters
    /// fall back to iterating IFileSystem.Files.
    ///
    /// The fallback expression used by all Nintendo formatters is:
    ///   area.FsInfo?.FidelityFiles?.Entries ?? (IEnumerable&lt;IFsFile&gt;)fs.Files
    ///
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class NintendoFormatterFallbackTests
    {
        #region Test Helpers

        /// <summary>
        /// Models the fallback expression used by all Nintendo formatters in BuildFileSystemYaml:
        ///   area.FsInfo?.FidelityFiles?.Entries ?? (IEnumerable&lt;IFsFile&gt;)fs.Files
        /// </summary>
        private static IEnumerable<IFsFile> ResolveFileSource(IFileSystemData fsInfo, IFileSystem fs) => fsInfo?.FidelityFiles?.Entries ?? (IEnumerable<IFsFile>)fs.Files;

        /// <summary>
        /// Minimal IFsFile stub for testing file source resolution.
        /// </summary>
        private class StubFsFile : IFsFile
        {
            public string Name { get; set; }
            public IFsFolder Parent { get; set; }
            public string Path { get; set; }
            public string FullName { get; set; }
            public long FsSize { get; set; }
            public long FsOffset { get; set; }
            public long PostGapSize { get; set; }
            public long PostGapFsOffset { get; set; }
            public bool IsMissing { get; set; }
            public bool IsLastFile { get; set; }
            public int SplitIndex { get; set; }
            public IFsFileParts SplitParts { get; set; }
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public bool IsSystemFile { get; set; }
            public IFsFile Clone() => new StubFsFile
            {
                Name = Name,
                Parent = Parent,
                Path = Path,
                FullName = FullName,
                FsSize = FsSize,
                FsOffset = FsOffset,
                PostGapSize = PostGapSize,
                PostGapFsOffset = PostGapFsOffset,
                IsMissing = IsMissing,
                IsLastFile = IsLastFile,
                SplitIndex = SplitIndex,
                SplitParts = SplitParts,
                XxHash = XxHash,
                Crc = Crc,
                GapCrc = GapCrc,
                IsSystemFile = IsSystemFile
            };
            public override string ToString() => FullName ?? Name ?? "(unnamed)";
        }

        /// <summary>
        /// Minimal IFileSystem stub for testing.
        /// </summary>
        private class StubFileSystem : IFileSystem
        {
            public List<IFsFile> Files { get; set; } = new List<IFsFile>();
            public IFsFolder Root { get; set; }
            public List<IFsFile> CloneFiles() => Files.Select(f => f.Clone()).ToList();
        }

        /// <summary>
        /// Minimal IFileSystemData stub for testing.
        /// </summary>
        private class StubFileSystemData : IFileSystemData
        {
            public long ImageOffset { get; set; }
            public long Size { get; set; }
            public IFileSystem FileSystem { get; set; }
            public bool InvalidFileSystem { get; set; }
            public PartitionType Type { get; set; }
            public bool AllFoldersParsed { get; set; }
            public FidelityFileList FidelityFiles { get; set; }
            public IAreaFileSystemView AreaView => null;
        }

        private static List<IFsFile> CreateTestFiles(params string[] names)
        {
            return names.Select((name, i) => (IFsFile)new StubFsFile
            {
                Name = name,
                FullName = $"/{name}",
                Path = "/",
                FsOffset = i * 0x1000,
                FsSize = 0x800
            }).ToList();
        }

        #endregion

        #region Null FidelityFiles Fallback Tests

        /// <summary>
        /// When FidelityFiles is null on the IFileSystemData, the fallback expression
        /// resolves to fs.Files. This is the primary backward-compatibility scenario
        /// for Nintendo formats that may not produce collisions.
        /// Validates: Requirement 4.3
        /// </summary>
        [Fact]
        public void ResolveFileSource_NullFidelityFiles_FallsBackToFsFiles()
        {
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol", "game.dat");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };
            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = null // Not populated
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);

            Assert.Same(fsFiles, result);
        }

        /// <summary>
        /// When FsInfo itself is null, the fallback expression resolves to fs.Files.
        /// This can happen if the area has no filesystem data at all.
        /// Validates: Requirement 4.3
        /// </summary>
        [Fact]
        public void ResolveFileSource_NullFsInfo_FallsBackToFsFiles()
        {
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };

            IEnumerable<IFsFile> result = ResolveFileSource(null, fs);

            Assert.Same(fsFiles, result);
        }

        /// <summary>
        /// When FidelityFiles is null, the resolved file source should contain
        /// exactly the same entries as fs.Files (same count, same references).
        /// Validates: Requirement 4.4
        /// </summary>
        [Fact]
        public void ResolveFileSource_NullFidelityFiles_ContainsSameEntriesAsFsFiles()
        {
            List<IFsFile> fsFiles = CreateTestFiles("sys.bin", "apploader.img", "fst.bin", "game.iso");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };
            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = null
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);
            List<IFsFile> resultList = result.ToList();

            Assert.Equal(fsFiles.Count, resultList.Count);
            for (int i = 0; i < fsFiles.Count; i++)
            {
                Assert.Same(fsFiles[i], resultList[i]);
            }
        }

        #endregion

        #region Empty FidelityFiles Behavior Tests

        /// <summary>
        /// When FidelityFiles exists but is empty (Count == 0), the ?? operator does NOT
        /// trigger because Entries is a non-null empty List. The formatter iterates the
        /// empty fidelity list. This verifies the actual code behavior.
        /// Validates: Requirement 4.3 (documents that empty != null for fallback purposes)
        /// </summary>
        [Fact]
        public void ResolveFileSource_EmptyFidelityFiles_UsesEmptyFidelityEntries()
        {
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol", "game.dat");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };
            FidelityFileList fidelity = new FidelityFileList(); // Empty but non-null
            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = fidelity
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);

            // The ?? operator does not trigger for empty lists — Entries is non-null
            Assert.Same(fidelity.Entries, result);
            Assert.Empty(result);
        }

        /// <summary>
        /// Confirms that an empty FidelityFileList.Entries is not reference-equal to fs.Files,
        /// demonstrating that the fallback did NOT occur.
        /// Validates: Requirement 4.3
        /// </summary>
        [Fact]
        public void ResolveFileSource_EmptyFidelityFiles_DoesNotReturnFsFiles()
        {
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };
            FidelityFileList fidelity = new FidelityFileList(); // Empty
            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = fidelity
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);

            Assert.NotSame(fsFiles, result);
        }

        #endregion

        #region Populated FidelityFiles Tests

        /// <summary>
        /// When FidelityFiles is populated with entries, the formatter uses those entries
        /// rather than fs.Files. This is the normal case when fidelity collection is active.
        /// Validates: Requirement 4.4
        /// </summary>
        [Fact]
        public void ResolveFileSource_PopulatedFidelityFiles_UsesFidelityEntries()
        {
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };

            FidelityFileList fidelity = new FidelityFileList();
            StubFsFile fidelityFile1 = new StubFsFile { Name = "boot.bin", FullName = "/boot.bin", FsOffset = 0x0, FsSize = 0x400 };
            StubFsFile fidelityFile2 = new StubFsFile { Name = "main.dol", FullName = "/main.dol", FsOffset = 0x1000, FsSize = 0x800 };
            StubFsFile fidelityFile3 = new StubFsFile { Name = "extra.dat", FullName = "/extra.dat", FsOffset = 0x0, FsSize = 0x200 };
            fidelity.Add(fidelityFile1);
            fidelity.Add(fidelityFile2);
            fidelity.Add(fidelityFile3);

            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = fidelity
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);

            Assert.Same(fidelity.Entries, result);
            Assert.Equal(3, result.Count());
        }

        /// <summary>
        /// When FidelityFiles has more entries than fs.Files (due to collisions),
        /// the resolved source reflects the fidelity count, not the OrderedList count.
        /// Validates: Requirement 4.4
        /// </summary>
        [Fact]
        public void ResolveFileSource_FidelityHasMoreEntries_ReturnsAllFidelityEntries()
        {
            // fs.Files has 2 entries (deduplicated by offset)
            List<IFsFile> fsFiles = CreateTestFiles("boot.bin", "main.dol");
            StubFileSystem fs = new StubFileSystem { Files = fsFiles };

            // Fidelity has 4 entries (includes collision entries)
            FidelityFileList fidelity = new FidelityFileList();
            fidelity.Add(new StubFsFile { Name = "boot.bin", FullName = "/boot.bin", FsOffset = 0x0, FsSize = 0x400 });
            fidelity.Add(new StubFsFile { Name = "zero1.dat", FullName = "/zero1.dat", FsOffset = 0x0, FsSize = 0 });
            fidelity.Add(new StubFsFile { Name = "main.dol", FullName = "/main.dol", FsOffset = 0x1000, FsSize = 0x800 });
            fidelity.Add(new StubFsFile { Name = "zero2.dat", FullName = "/zero2.dat", FsOffset = 0x1000, FsSize = 0 });

            StubFileSystemData fsInfo = new StubFileSystemData
            {
                FileSystem = fs,
                FidelityFiles = fidelity
            };

            IEnumerable<IFsFile> result = ResolveFileSource(fsInfo, fs);

            Assert.Equal(4, result.Count());
            Assert.NotSame(fsFiles, result);
        }

        #endregion
    }
}