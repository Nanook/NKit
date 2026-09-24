using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FolderImageProcessor CueFolder processing path.
    ///
    /// These tests validate the processCueFolder method which queries the DataStore
    /// for child CUE/GDI images, checks staleness via ifs count comparison, and
    /// delegates to CueFolderBuilder.Build() when rebuild is needed.
    ///
    /// **Validates: Requirements 10.1, 10.6, 10.7**
    /// </summary>
    public class FolderImageProcessorCueFolderTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly List<string> _logMessages;

        public FolderImageProcessorCueFolderTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "FolderImageProcessorCueFolderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testBaseDir);
            _logMessages = new List<string>();
        }

        public void Dispose()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
                if (Directory.Exists(_testBaseDir))
                    Directory.Delete(_testBaseDir, true);
            }
            catch { }
        }

        private void Log(string message, LogLevel level) => _logMessages.Add(message);

        private string CreateTestDir(string name)
        {
            string dir = Path.Combine(_testBaseDir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Creates a synthetic CueFolder SourceFile with the given child source names.
        /// </summary>
        private SourceFile CreateCueFolderSyntheticSource(string baseName, string sourceFolder, string[] childNames)
        {
            List<SourceFile> childSources = new List<SourceFile>();
            foreach (string childName in childNames)
            {
                childSources.Add(new SourceFile
                {
                    Name = childName,
                    CleanName = childName,
                    Status = SourceFileResult.Valid,
                });
            }

            FolderGroupInfo group = new FolderGroupInfo
            {
                GroupType = FolderGroupType.CueFolder,
                BaseName = baseName,
                SourceFolder = sourceFolder,
                ChildSources = childSources,
                SystemType = SystemType.PS1,
                IsArchived = false,
                ArchiveFiles = null,
            };

            return new SourceFile
            {
                IsSyntheticFolder = true,
                SyntheticFolderGroup = group,
                Name = baseName,
                CleanName = baseName,
                Status = SourceFileResult.Valid,
                SystemType = SystemType.PS1,
            };
        }

        /// <summary>
        /// Adds a child CUE image to the datastore with area metadata containing FileName entries.
        /// </summary>
        private void AddChildCueImage(DataStore store, string setName, string imageName, string[] trackFileNames)
        {
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 0, system: "Directories", format: ImageFormat.Cue))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                long offset = 0;
                foreach (string fileName in trackFileNames)
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FileName, fileName);
                    writer.CreateArea(offset, 1024, 0, 0, 0x200000, metadata: metadata);
                    offset += 1024;
                }

                writer.FinalizeImage(testData.Length, 0, 0);
            }
        }

        /// <summary>
        /// Adds an existing CueFolder image to the datastore with a filesystem.yaml
        /// containing the specified number of ifs entries.
        /// </summary>
        private void AddExistingCueFolder(DataStore store, string setName, string baseName, int ifsCount)
        {
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, baseName, shardSize: 0, system: "Directories", format: ImageFormat.CueFolder))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);

                for (int i = 0; i < ifsCount; i++)
                {
                    fsYaml.AddIfsEntry($"Disc {i + 1}.cue", i + 1, 734003280);
                }

                string yaml = fsYaml.ToYaml();
                writer.WriteFile(DataStore.FileSystemYamlRootPath, Encoding.UTF8.GetBytes(yaml));

                writer.FinalizeImage(testData.Length, 0, 0);
            }
        }

        // =====================================================================
        // Test: CueFolder not created when no child images found
        // Validates: Requirement 10.6
        // =====================================================================

        [Fact]
        public void ProcessCueFolder_NoChildImages_ReturnsNoChildren()
        {
            string testDir = CreateTestDir(nameof(ProcessCueFolder_NoChildImages_ReturnsNoChildren));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            // Create the set but don't add any child images
            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet("TestSet", shardSize: 0);
            }

            string[] childNames = new[] { "Game Disc 1", "Game Disc 2" };
            SourceFile syntheticSource = CreateCueFolderSyntheticSource("MultiDisc Game", sourceFolder, childNames);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", Log);

            Assert.Equal(FolderProcessResult.NoChildren, result);
            Assert.Contains(_logMessages, m => m.Contains("NoChildren"));
        }

        // =====================================================================
        // Test: Existing CueFolder skipped when up-to-date
        // Validates: Requirement 10.6
        // =====================================================================

        [Fact]
        public void ProcessCueFolder_ExistingUpToDate_ReturnsUpToDate()
        {
            string testDir = CreateTestDir(nameof(ProcessCueFolder_ExistingUpToDate_ReturnsUpToDate));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            string[] childNames = new[] { "Game Disc 1", "Game Disc 2" };

            using (DataStore store = new DataStore(testDir))
            {
                // Add 2 child CUE images
                AddChildCueImage(store, "TestSet", "Game Disc 1", new[] { "track01.bin", "track02.bin" });
                AddChildCueImage(store, "TestSet", "Game Disc 2", new[] { "track01.bin" });
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");

                // Add existing CueFolder with matching ifs count (2 child images)
                AddExistingCueFolder(store, "TestSet", "MultiDisc Game", 2);
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");
            }

            SourceFile syntheticSource = CreateCueFolderSyntheticSource("MultiDisc Game", sourceFolder, childNames);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", Log);

            Assert.Equal(FolderProcessResult.UpToDate, result);
            Assert.Contains(_logMessages, m => m.Contains("UpToDate"));
        }

    }
}