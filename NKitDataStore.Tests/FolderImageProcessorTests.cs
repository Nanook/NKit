using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FolderImageProcessor.
    ///
    /// FolderImageProcessor is an internal class in the NKit project that processes
    /// synthetic folder SourceFiles by querying the datastore for child images,
    /// checking staleness, and delegating to TmdAppFolderBuilder.Build().
    ///
    /// These tests use real DataStore instances with temporary directories to validate
    /// the full Process() flow including child image queries, staleness detection,
    /// and error handling.
    ///
    /// **Validates: Requirements 6.2, 7.1, 7.2, 7.3, 7.4, 8.2, 8.3, 8.4, 11.1, 11.3, 11.4**
    /// </summary>
    public class FolderImageProcessorTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly List<string> _logMessages;

        public FolderImageProcessorTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "FolderImageProcessorTests", Guid.NewGuid().ToString("N"));
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

        private SourceFile CreateSyntheticSource(string baseName, string sourceFolder)
        {
            FolderGroupInfo group = new FolderGroupInfo
            {
                GroupType = FolderGroupType.TmdAppFolder,
                BaseName = baseName,
                SourceFolder = sourceFolder,
                ChildSources = new List<SourceFile>(),
                SystemType = SystemType.WiiU,
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
                SystemType = SystemType.WiiU,
            };
        }

        /// <summary>
        /// Adds a child App image with disambiguated name "baseName [tmd.X]" to the datastore,
        /// with area metadata containing FileName entries for each file, and a filesystem.yaml
        /// so that needsRebuild can count child files correctly.
        /// </summary>
        private void AddChildImage(DataStore store, string setName, string baseName, int tmdIndex, string[] fileNames)
        {
            string imageName = $"{baseName} [tmd.{tmdIndex}]";
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 0, system: "WiiU", format: ImageFormat.App))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                // Create areas with FileName metadata for each file
                long offset = 0;
                foreach (string fileName in fileNames)
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FileName, fileName);
                    writer.CreateArea(offset, 1024, 0, 0, 0x200000, metadata: metadata);
                    offset += 1024;
                }

                // Write filesystem.yaml so needsRebuild can count child files
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                long fsOffset = 0;
                foreach (string fileName in fileNames)
                {
                    root.AddFile(fileName, fsOffset, 1024, 0, 0);
                    fsOffset += 1024;
                }
                string yaml = fsYaml.ToYaml();
                writer.WriteFile(DataStore.FileSystemYamlRootPath, Encoding.UTF8.GetBytes(yaml));

                writer.FinalizeImage(testData.Length, 0, 0);
            }
        }

        /// <summary>
        /// Adds a TmdAppFolder image to the datastore with a filesystem.yaml containing
        /// the specified number of ifs entries.
        /// </summary>
        private void AddExistingTmdAppFolder(DataStore store, string setName, string baseName, int ifsCount)
        {
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, baseName, shardSize: 0, system: "WiiU", format: ImageFormat.TmdAppFolder))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                // Build filesystem.yaml with ifs entries
                FsYaml fsYaml = new FsYaml();
                FsYamlNode root = fsYaml.AddFileSystem(".", 0);
                root.AddFile("tmd.0", 0, 100, 0, 0); // fs entry

                for (int i = 0; i < ifsCount; i++)
                {
                    fsYaml.AddIfsEntry($"{i:D8}.app", i + 1, 1024);
                }

                string yaml = fsYaml.ToYaml();
                writer.WriteFile(DataStore.FileSystemYamlRootPath, Encoding.UTF8.GetBytes(yaml));

                writer.FinalizeImage(testData.Length, 0, 0);
            }
        }

        // =====================================================================
        // Test 1: Process() returns NoChildren when no child images exist
        // Validates: Requirement 6.2
        // =====================================================================

        [Fact]
        public void Process_NoChildImages_ReturnsNoChildren()
        {
            string testDir = CreateTestDir(nameof(Process_NoChildImages_ReturnsNoChildren));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet("TestSet", shardSize: 0);
            }

            SourceFile syntheticSource = CreateSyntheticSource("Mario", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", Log);

            Assert.Equal(FolderProcessResult.NoChildren, result);
            Assert.Contains(_logMessages, m => m.Contains("NoChildren"));
        }

        // =====================================================================
        // Test 2: Process() returns UpToDate when existing TmdAppFolder is current
        // Validates: Requirements 7.3, 10.1
        // =====================================================================

        [Fact]
        public void Process_ExistingFolderUpToDate_ReturnsUpToDate()
        {
            string testDir = CreateTestDir(nameof(Process_ExistingFolderUpToDate_ReturnsUpToDate));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            using (DataStore store = new DataStore(testDir))
            {
                // Add 2 child images with 1 file each = 2 total files
                AddChildImage(store, "TestSet", "Mario", 0, new[] { "00000000.app" });
                AddChildImage(store, "TestSet", "Mario", 1, new[] { "0000000a.app" });
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");

                // Add existing TmdAppFolder with matching ifs count (2)
                AddExistingTmdAppFolder(store, "TestSet", "Mario", 2);
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");
            }

            SourceFile syntheticSource = CreateSyntheticSource("Mario", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", Log);

            Assert.Equal(FolderProcessResult.UpToDate, result);
            Assert.Contains(_logMessages, m => m.Contains("UpToDate"));
        }

        // =====================================================================
        // Test 5: Process() returns Error when datastore connection fails
        // Validates: Requirement 11.3
        // =====================================================================

        [Fact]
        public void Process_InvalidDatastorePath_ReturnsError()
        {
            // Use a path with invalid characters that cannot be created as a directory
            string bogusPath = Path.Combine(_testBaseDir, "path\0with\0nulls");
            string sourceFolder = Path.Combine(_testBaseDir, "source");
            Directory.CreateDirectory(sourceFolder);

            SourceFile syntheticSource = CreateSyntheticSource("Mario", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, bogusPath, "TestSet", Log);

            Assert.Equal(FolderProcessResult.Error, result);
            Assert.Contains(_logMessages, m => m.Contains("Result") && m.Contains("Error"));
        }

        // =====================================================================
        // Test 6: Process() catches exceptions and returns Error
        // Validates: Requirement 11.1
        // =====================================================================

        [Fact]
        public void Process_ExceptionDuringProcessing_ReturnsError()
        {
            // Use a path that exists but is not a valid datastore (no .nkds file)
            string testDir = CreateTestDir(nameof(Process_ExceptionDuringProcessing_ReturnsError));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            // Create a synthetic source with a null group to trigger an exception path
            // Actually, let's use a corrupted datastore scenario
            // Write a garbage file where the .nkds database should be
            File.WriteAllText(Path.Combine(testDir, "TestSet.nkds"), "not a database");

            SourceFile syntheticSource = CreateSyntheticSource("Mario", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", Log);

            Assert.Equal(FolderProcessResult.Error, result);
        }

        // =====================================================================
        // Test: Process() with null log does not throw
        // Validates: Requirement 11.1 (error handling robustness)
        // =====================================================================

        [Fact]
        public void Process_NullLog_DoesNotThrow()
        {
            string testDir = CreateTestDir(nameof(Process_NullLog_DoesNotThrow));
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet("TestSet", shardSize: 0);
            }

            SourceFile syntheticSource = CreateSyntheticSource("Mario", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            // Should not throw even with null log
            FolderProcessResult result = processor.Process(syntheticSource, testDir, "TestSet", null);

            Assert.Equal(FolderProcessResult.NoChildren, result);
        }

    }
}