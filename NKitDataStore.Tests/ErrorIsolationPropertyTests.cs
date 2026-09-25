using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore.Interfaces;
using System.Text;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for error isolation
    /// (Property 8: Error Isolation).
    ///
    /// Verifies that an exception in one folder group's processing returns
    /// FolderProcessResult.Error and does not prevent processing of other groups.
    /// Each group is processed independently via FolderImageProcessor.Process(),
    /// so a failure in one group must not affect the others.
    ///
    /// **Validates: Requirements 11.1, 11.2, 11.4**
    /// </summary>
    public class ErrorIsolationPropertyTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly List<string> _logMessages;

        public ErrorIsolationPropertyTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "ErrorIsolationPropTests", Guid.NewGuid().ToString("N"));
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
        /// Creates a corrupted datastore directory by writing garbage to the .nkds file.
        /// This reliably causes FolderImageProcessor.Process() to catch an exception
        /// and return FolderProcessResult.Error.
        /// </summary>
        private string CreateCorruptedDatastore(string name, string setName)
        {
            string dir = CreateTestDir(name);
            string nkdsPath = Path.Combine(dir, setName + ".nkds");
            File.WriteAllText(nkdsPath, "this is not a valid sqlite database");
            return dir;
        }

        /// <summary>
        /// **Validates: Requirements 11.1, 11.2, 11.4**
        ///
        /// Property 8: Error Isolation.
        /// Given N folder groups (N >= 2), when one group's processing fails
        /// (by pointing it at a corrupted datastore), the failing group returns
        /// FolderProcessResult.Error while all other groups still process
        /// successfully (returning Created). A failure in one group does not
        /// prevent processing of other groups.
        ///
        /// The test generates a random number of groups (2..4) and a random
        /// index for the failing group. Valid groups have real child images in
        /// a real datastore. The failing group is processed against a corrupted
        /// datastore so that an exception is thrown internally, caught by
        /// FolderImageProcessor.Process(), and returned as Error.
        /// </summary>
        [Property(MaxTest = 10)]
        public bool ErrorIsolation_FailingGroupDoesNotBlockOthers(
            NonNegativeInt groupCountWrapper,
            NonNegativeInt failIndexWrapper,
            NonNegativeInt seed)
        {
            int groupCount = (groupCountWrapper.Get % 3) + 2; // 2..4 groups
            int failIndex = failIndexWrapper.Get % groupCount; // which group fails
            int s = seed.Get;

            string testDir = CreateTestDir($"eriso_{s}_{groupCount}_{failIndex}");
            string sourceFolder = Path.Combine(testDir, "source");
            Directory.CreateDirectory(sourceFolder);

            // Create a corrupted datastore for the failing group
            string corruptedDir = CreateCorruptedDatastore(
                $"corrupt_{s}_{groupCount}_{failIndex}", "TestSet");

            // Set up valid datastore with child images for all groups
            string[] groupNames = new string[groupCount];
            for (int g = 0; g < groupCount; g++)
                groupNames[g] = $"Game{g}s{s}";

            using (DataStore store = new DataStore(testDir))
            {
                for (int g = 0; g < groupCount; g++)
                {
                    int childCount = (((s + (g * 7)) & 0x7FFFFFFF) % 2) + 2; // 2..3 children
                    for (int c = 0; c < childCount; c++)
                    {
                        int fileCount = (((s + (g * 11) + (c * 3)) & 0x7FFFFFFF) % 2) + 1; // 1..2 files
                        string[] fileNames = new string[fileCount];
                        for (int f = 0; f < fileCount; f++)
                            fileNames[f] = $"{(g * 1000) + (c * 100) + f:D8}.app";

                        AddChildImage(store, "TestSet", groupNames[g], c, fileNames);
                    }
                }
                TestDataStoreHelper.WaitForSetIdle(store, "TestSet");
            }

            // Process each group independently.
            // Valid groups use the real datastore; the failing group uses the corrupted one.
            FolderProcessResult[] results = new FolderProcessResult[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                SourceFile syntheticSource = CreateSyntheticSource(groupNames[g], sourceFolder);
                FolderImageProcessor processor = new FolderImageProcessor();

                if (g == failIndex)
                {
                    // Corrupted datastore → should catch exception and return Error
                    results[g] = processor.Process(syntheticSource, corruptedDir, "TestSet", Log);
                }
                else
                {
                    // Valid datastore → should return Created
                    results[g] = processor.Process(syntheticSource, testDir, "TestSet", Log);
                }
            }

            // Verify: the failing group returned Error
            if (results[failIndex] != FolderProcessResult.Error)
                return false;

            // Verify: all other groups returned Created (they have children, no existing folder)
            for (int g = 0; g < groupCount; g++)
            {
                if (g == failIndex)
                    continue;
                if (results[g] != FolderProcessResult.Created)
                    return false;
            }

            return true;
        }
    }
}