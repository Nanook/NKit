using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for idempotent processing
    /// (Property 3: Idempotent Processing).
    ///
    /// These tests use real DataStore instances with temporary directories to verify
    /// that running FolderImageProcessor.Process() twice with no changes between runs
    /// produces Created on the first run and UpToDate on the second run.
    ///
    /// **Validates: Requirements 10.1, 10.2**
    /// </summary>
    public class IdempotentProcessingPropertyTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly List<string> _logMessages;

        public IdempotentProcessingPropertyTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "IdempotentPropTests", Guid.NewGuid().ToString("N"));
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
        /// Adds a child App image with area metadata FileName entries and filesystem.yaml.
        /// </summary>
        private void AddChildImage(DataStore store, string setName, string baseName, int tmdIndex, string[] fileNames)
        {
            string imageName = $"{baseName} [tmd.{tmdIndex}]";
            using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 0, system: "WiiU", format: ImageFormat.App))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);

                long offset = 0;
                foreach (string fileName in fileNames)
                {
                    AreaMetadata metadata = new AreaMetadata();
                    metadata.Set(AreaValueType.FileName, fileName);
                    writer.CreateArea(offset, 1024, 0, 0, 0x200000, metadata: metadata);
                    offset += 1024;
                }

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
        /// **Validates: Requirements 10.1, 10.2**
        ///
        /// Property 3: Idempotent Processing.
        /// For any valid configuration of child images (varying child count and
        /// files per child), running FolderImageProcessor.Process() twice with
        /// no changes between runs produces Created on the first run and
        /// UpToDate on the second run.
        /// </summary>
        [Property(MaxTest = 10)]
        public bool IdempotentProcessing_SecondRunReturnsUpToDate(
            NonNegativeInt childCountWrapper,
            NonNegativeInt seed)
        {
            int childCount = (childCountWrapper.Get % 4) + 2; // 2..5 children
            int s = seed.Get;

            string testDir = CreateTestDir($"idem_{s}_{childCount}");
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            // Set up datastore with generated child images
            using (DataStore store = new DataStore(testDir))
            {
                for (int c = 0; c < childCount; c++)
                {
                    int fileCount = (((s + (c * 13)) & 0x7FFFFFFF) % 3) + 1; // 1..3 files per child
                    string[] fileNames = new string[fileCount];
                    for (int f = 0; f < fileCount; f++)
                        fileNames[f] = $"{(c * 100) + f:D8}.app";

                    AddChildImage(store, "TestSet", "Game", c, fileNames);
                }
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");
            }

            SourceFile syntheticSource = CreateSyntheticSource("Game", sourceFolder);
            FolderImageProcessor processor = new FolderImageProcessor();

            // First run: should return Created
            _logMessages.Clear();
            FolderProcessResult firstResult = processor.Process(syntheticSource, testDir, "TestSet", Log);
            if (firstResult != FolderProcessResult.Created)
                return false;

            // Wait for background tasks to complete before second run
            using (DataStore waitStore = new DataStore(testDir))
            {
                TestDataStoreHelper.WaitForSetIdle(waitStore, "TestSet");
            }
            // Additional delay for file system flush and SQLite WAL checkpoint
            Thread.Sleep(200);

            // Second run (no changes): should return UpToDate
            _logMessages.Clear();
            FolderProcessResult secondResult = processor.Process(syntheticSource, testDir, "TestSet", Log);
            return secondResult == FolderProcessResult.UpToDate;
        }
    }
}