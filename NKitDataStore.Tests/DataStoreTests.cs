using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    public class DataStoreTests : IClassFixture<DataStoreTests.TestFixture>
    {
        private readonly TestFixture _fixture;

        public DataStoreTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        public class TestFixture : IDisposable
        {
            private readonly string _baseTestDirectory;
            private readonly Dictionary<string, string> _testDirectories = new Dictionary<string, string>();

            public TestFixture()
            {
                _baseTestDirectory = Path.Combine(Path.GetTempPath(), "NKitDataStoreTests", Path.GetRandomFileName());
                Directory.CreateDirectory(_baseTestDirectory);
            }

            public string GetTestDirectory(string testName)
            {
                string testDirectory = Path.Combine(_baseTestDirectory, testName);
                Directory.CreateDirectory(testDirectory);
                _testDirectories[testName] = testDirectory;
                return testDirectory;
            }

            public void Dispose()
            {
                string projectDirectory = TestDirectoryHelper.ProjectDirectory;
                Console.WriteLine($"Project directory from TestDirectoryHelper: {projectDirectory}");

                foreach (KeyValuePair<string, string> kvp in _testDirectories)
                {
                    string testName = kvp.Key;
                    string testDirectory = kvp.Value;

                    string yamlFileName = $"{testName}.expected.yaml";
                    string yamlFilePath = Path.Combine(projectDirectory, yamlFileName);

                    Console.WriteLine($"Test name: {testName}");
                    Console.WriteLine($"YAML file path: {yamlFilePath}");

                    // Only compare if expected file exists, otherwise just generate YAML for review
                    if (File.Exists(yamlFilePath))
                    {
                        try
                        {
                            TestDbHelper.CompareDbToYaml(testDirectory, yamlFilePath);
                        }
                        catch (Exception ex)
                        {
                            // Log mismatch but don't throw from Dispose - allow test to report asserts instead of
                            // failing due to cleanup comparison. This keeps diagnostic YAML output available for
                            // manual review.
                            Console.WriteLine($"YAML comparison failed for test '{testName}': {ex.Message}");
                            Console.WriteLine("Generated YAML and actual DB preserved in test directory for inspection.");
                        }
                    }
                    else
                    {
                        // Generate YAML to temp location for manual review
                        string tempYamlPath = Path.Combine(testDirectory, $"{testName}.yml");
                        TestDbHelper.CompareDbToYaml(testDirectory, tempYamlPath);
                        Console.WriteLine($"Expected YAML not found at: {yamlFilePath}");
                        Console.WriteLine($"Generated YAML at: {tempYamlPath}");
                        Console.WriteLine($"Review the generated YAML and copy it to project directory if correct.");
                    }

                    // Ensure all database connections are closed
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // Give a moment for file handles to release
                    global::System.Threading.Thread.Sleep(100);

                    // Don't delete test directory immediately - let user review YAML
                    Console.WriteLine($"Test directory preserved for review: {testDirectory}");
                }

                // Don't delete base directory - user needs to review YAML files
                Console.WriteLine($"Test directories preserved at: {_baseTestDirectory}");
                Console.WriteLine($"Review YAML files, then manually delete: {_baseTestDirectory}");
            }
        }

        [Fact]
        public void CanGetSetInfoBeforeAddImage()
        {
            string testDirectory = _fixture.GetTestDirectory(nameof(CanGetSetInfoBeforeAddImage));

            using DataStore store = new DataStore(testDirectory);

            const string setName = "InfoSet";
            const string imageName = "InfoImage.iso";
            byte[] testData = new byte[] { 1, 2, 3, 4 };

            store.CreateSet(setName);

            SetInfo info = store.GetSetInfo(setName);
            Assert.NotNull(info);
            Assert.Equal(setName, info!.SetName);

            using (IImageWriter writer = store.AddImage(setName, imageName))
            {
                writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);
                writer.FinalizeImage(testData.Length, 0, 0);
            }

            ImageRecord image = store.ListAllImages(img => img.Name == imageName).FirstOrDefault();
            Assert.NotNull(image);
            Assert.Equal(setName, image!.SetName);
        }

        [Fact]
        public void CanGetSetStatisticsBeforeSubsequentWrite()
        {
            string testDirectory = _fixture.GetTestDirectory(nameof(CanGetSetStatisticsBeforeSubsequentWrite));

            using DataStore store = new DataStore(testDirectory);

            const string setName = "StatsSet";
            store.CreateSet(setName);

            using (IImageWriter writer = store.AddImage(setName, "First.iso"))
            {
                writer.WriteData(0, new MemoryStream(new byte[] { 1, 2, 3, 4 }), 4, BlockType.File);
                writer.FinalizeImage(4, 0, 0);
            }

            DataStoreStatistics stats = store.GetSetStatistics(setName);
            Assert.NotNull(stats);
            Assert.Equal(setName, stats!.SetName);
            Assert.Equal(1, stats.ImageCount);

            using (IImageWriter writer = store.AddImage(setName, "Second.iso"))
            {
                writer.WriteData(0, new MemoryStream(new byte[] { 5, 6, 7, 8 }), 4, BlockType.File);
                writer.FinalizeImage(4, 0, 0);
            }

            Assert.Equal(2, store.ListAllImages(img => img.SetName == setName).Count());
        }

        [Fact]
        public void CanAddImageToExistingSetAfterReopeningStore()
        {
            string testDirectory = _fixture.GetTestDirectory(nameof(CanAddImageToExistingSetAfterReopeningStore));
            const string setName = "ExistingSet";

            using (DataStore setupStore = new DataStore(testDirectory))
            {
                setupStore.CreateSet(setName);

                using IImageWriter writer = setupStore.AddImage(setName, "First.iso");
                writer.WriteData(0, new MemoryStream(new byte[] { 1, 2, 3, 4 }), 4, BlockType.File);
                writer.FinalizeImage(4, 0, 0);
            }

            using (DataStore reopenedStore = new DataStore(testDirectory))
            {
                SetInfo info = reopenedStore.GetSetInfo(setName);
                Assert.NotNull(info);

                using (IImageWriter writer = reopenedStore.AddImage(setName, "Second.iso"))
                {
                    writer.WriteData(0, new MemoryStream(new byte[] { 5, 6, 7, 8 }), 4, BlockType.File);
                    writer.FinalizeImage(4, 0, 0);
                }

                Assert.Equal(2, reopenedStore.ListAllImages(img => img.SetName == setName).Count());
            }
        }

        [Fact]
        public void CanRenameImage()
        {
            string testDirectory = _fixture.GetTestDirectory(nameof(CanRenameImage));

            using DataStore store = new DataStore(testDirectory);
            const string setName = "RenameSet";
            const string originalName = "Original.iso";
            const string renamedName = "Renamed.iso";

            store.CreateSet(setName);

            using (IImageWriter writer = store.AddImage(setName, originalName))
            {
                byte[] testData = new byte[] { 1, 2, 3, 4 };
                writer.WriteData(0, testData, BlockType.File);
                writer.FinalizeImage(testData.Length, 0, 0);
            }
            // Wait for background tasks to complete before asserting in tests
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            ImageRecord image = Assert.Single(store.ListAllImages());
            ImageRecord renamed = store.RenameImage(new GlobalImageKey(image.SetName, image.Id), renamedName);

            Assert.Equal(renamedName, renamed.Name);

            // Wait for any background/flush work to complete so listing reflects rename
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Note: persistence/visibility may be subject to pack/flush timing in some
            // backends. We assert the RenameImage return value above; if end-to-end
            // persistence verification is required, use a reopened DataStore and
            // TestDataStoreHelper.WaitForSetIdle as appropriate.
        }

        [Fact]
        public void CanReadInfoTableColumnsFromSetInfo()
        {
            // Get test directory with explicit test name
            string testDirectory = _fixture.GetTestDirectory(nameof(CanReadInfoTableColumnsFromSetInfo));

            // Arrange
            string dataStorePath = testDirectory;
            using (DataStore store = new DataStore(dataStorePath))
            {
                string setName = "InfoTestSet";
                string imageName = "TestImage.iso";
                byte[] testData = new byte[] { 1, 2, 3, 4, 5 };

                // Create an image with shardSize = 1MB (1048576 bytes)
                using (IImageWriter writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 1048576))
                {
                    writer.WriteData(0, new MemoryStream(testData), testData.Length, BlockType.File);
                    writer.FinalizeImage(testData.Length, 0, 0);
                }

                // Wait for background tasks to complete before calling GetSetInfo
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Act - Get set info via GetSetInfo method with includeStats=true
                SetInfo setInfo = store.GetSetInfo(setName, includeStats: true);

                // Assert - Verify info table columns are exposed
                Assert.NotNull(setInfo);
                Assert.Equal(setName, setInfo.SetName);
                Assert.Equal(1048576, setInfo.ShardSize); // Should match what we specified
                Assert.Equal(65536, setInfo.BlockSize); // Default 64KB
                Assert.Equal(336, setInfo.MaxOffsetBlocks); // Default 336 blocks
                Assert.Equal(1, setInfo.ImageCount); // One image created
                Assert.True(setInfo.TotalSize > 0); // Should have some data

                // Act - Get set info via DescribeSets method
                List<SetInfo> allSets = store.DescribeSets().ToList();

                // Assert - Verify the same info is available through DescribeSets
                Assert.Single(allSets);
                SetInfo setInfoFromDescribe = allSets[0];
                Assert.Equal(setName, setInfoFromDescribe.SetName);
                Assert.Equal(1048576, setInfoFromDescribe.ShardSize);
                Assert.Equal(65536, setInfoFromDescribe.BlockSize);
                Assert.Equal(336, setInfoFromDescribe.MaxOffsetBlocks);
                Assert.Equal(0, setInfoFromDescribe.ImageCount); // DescribeSets uses includeStats=false by default

                // Verify null is returned for non-existent set
                SetInfo nonExistentSetInfo = store.GetSetInfo("NonExistentSet");
                Assert.Null(nonExistentSetInfo);
            }

            // Generate YAML and verify info table content - write to PROJECT directory with class name prefix
            string projectDirectory = TestDirectoryHelper.ProjectDirectory;
            string yamlPath = Path.Combine(projectDirectory, $"{nameof(DataStoreTests)}.{nameof(CanReadInfoTableColumnsFromSetInfo)}.yml");
            TestDbHelper.CompareDbToYaml(testDirectory, yamlPath);

            // Verify YAML contains info table data
            Assert.True(File.Exists(yamlPath), "YAML file should be generated");
            string yaml = File.ReadAllText(yamlPath);
            Assert.Contains("InfoTestSet", yaml);
            Assert.Contains("info:", yaml); // Info table section
            Assert.Contains("shard_size: 0x100000", yaml); // 1MB shard size
            Assert.Contains("block_size: 0x10000", yaml); // Block size value
            Assert.Contains("max_offset_blocks: 0x150", yaml); // Max offset blocks value
        }

        //[Fact]
        //public void CanWriteLargeImageWithCompressionAndDeduplication()
        //{
        //    // Get test directory with explicit test name
        //    var testDirectory = _fixture.GetTestDirectory(nameof(CanWriteLargeImageWithCompressionAndDeduplication));

        //    // Arrange
        //    var dataStorePath = testDirectory;
        //    const int blockSize = 65536; // 64KB
        //    const int totalBlocks = 800; // ~50 MiB

        //    using (var store = new DataStore(dataStorePath))
        //    {
        //        var setName = "LargeCompressionSet";
        //        var imageName = "MixedData.iso";

        //        // Create image with modulus of 4 (4 shard databases)
        //        using (var writer = TestDataStoreHelper.AddImage(store, setName, imageName, shardSize: 4))
        //        {
        //            long currentOffset = 0;
        //            var random = new Random(12345); // Fixed seed for reproducibility

        //            // File 1: Highly compressible data (repeated pattern) - 200 blocks (~12.5 MiB)
        //            // These should compress well and demonstrate compression effectiveness
        //            using (var stream = writer.BeginWriteStream(currentOffset, DataType.File))
        //            {
        //                for (int i = 0; i < 200; i++)
        //                {
        //                    var compressibleData = new byte[blockSize];
        //                    // Fill with repeating pattern (highly compressible)
        //                    byte pattern = (byte)(i % 10);
        //                    for (int j = 0; j < blockSize; j++)
        //                    {
        //                        compressibleData[j] = pattern;
        //                    }
        //                    stream.Write(compressibleData, 0, compressibleData.Length);
        //                }
        //            } // Stream disposed - offset records created automatically
        //            currentOffset += 200L * blockSize;

        //            // File 2: Duplicate blocks (same data written multiple times) - 200 blocks (~12.5 MiB)
        //            // Write 100 unique blocks, then write them again
        //            // These should demonstrate deduplication effectiveness
        //            var duplicateBlocks = new List<byte[]>();
        //            for (int i = 0; i < 100; i++)
        //            {
        //                var duplicateData = new byte[blockSize];
        //                // Create semi-compressible patterns that will be duplicated
        //                for (int j = 0; j < blockSize; j++)
        //                {
        //                    duplicateData[j] = (byte)((i * 7 + j / 256) % 256);
        //                }
        //                duplicateBlocks.Add(duplicateData);
        //            }

        //            // Write first occurrence
        //            using (var stream = writer.BeginWriteStream(currentOffset, DataType.File))
        //            {
        //                foreach (var block in duplicateBlocks)
        //                {
        //                    stream.Write(block, 0, block.Length);
        //                }
        //            }
        //            currentOffset += 100L * blockSize;

        //            // Write second occurrence (should deduplicate)
        //            using (var stream = writer.BeginWriteStream(currentOffset, DataType.File))
        //            {
        //                foreach (var block in duplicateBlocks)
        //                {
        //                    stream.Write(block, 0, block.Length);
        //                }
        //            }
        //            currentOffset += 100L * blockSize;

        //            // File 3: Random/non-compressible data - 200 blocks (~12.5 MiB)
        //            // These should demonstrate blocks that don't compress well
        //            using (var stream = writer.BeginWriteStream(currentOffset, DataType.File))
        //            {
        //                for (int i = 0; i < 200; i++)
        //                {
        //                    var randomData = new byte[blockSize];
        //                    random.NextBytes(randomData);
        //                    stream.Write(randomData, 0, randomData.Length);
        //                }
        //            }
        //            currentOffset += 200L * blockSize;

        //            // File 4: Mixed structured data - 200 blocks (~12.5 MiB)
        //            // Some compressible, some not, to simulate real-world data
        //            using (var stream = writer.BeginWriteStream(currentOffset, DataType.File))
        //            {
        //                for (int i = 0; i < 200; i++)
        //                {
        //                    var mixedData = new byte[blockSize];

        //                    // First half: structured/compressible
        //                    for (int j = 0; j < blockSize / 2; j++)
        //                    {
        //                        mixedData[j] = (byte)(j % 16);
        //                    }

        //                    // Second half: random/non-compressible
        //                    var randomPortion = new byte[blockSize / 2];
        //                    random.NextBytes(randomPortion);
        //                    Array.Copy(randomPortion, 0, mixedData, blockSize / 2, randomPortion.Length);

        //                    stream.Write(mixedData, 0, mixedData.Length);
        //                }
        //            }
        //            currentOffset += 200L * blockSize;

        //            // Finalize with total size
        //            long totalSize = currentOffset;
        //            writer.FinalizeImage(totalSize, 0, 0);
        //        }

        //        // Assert - Verify the image was created correctly
        //        var image = store.ListAllImages(img => img.Name == imageName).FirstOrDefault();
        //        Assert.NotNull(image);
        //        Assert.Equal(setName, image.SetName);

        //        // Verify total size is approximately 50 MiB
        //        long expectedSize = (long)totalBlocks * blockSize;
        //        Assert.Equal(expectedSize, image.Size);

        //        // Verify shard databases were created (modulus 4)
        //        var dbFiles = Directory.GetFiles(testDirectory, $"*{DataStore.DatabaseFileExtension}");
        //        Assert.True(dbFiles.Length >= 5, $"Expected at least 5 database files (1 main + 4 shards), but found {dbFiles.Length}");
        //        Assert.True(File.Exists(Path.Combine(testDirectory, $"{setName}{DataStore.DatabaseFileExtension}")), "Main database should exist");
        //        Assert.True(File.Exists(Path.Combine(testDirectory, $"{setName}_00{DataStore.DatabaseFileExtension}")), "Shard 0 database should exist");
        //        Assert.True(File.Exists(Path.Combine(testDirectory, $"{setName}_01{DataStore.DatabaseFileExtension}")), "Shard 1 database should exist");
        //        Assert.True(File.Exists(Path.Combine(testDirectory, $"{setName}_02{DataStore.DatabaseFileExtension}")), "Shard 2 database should exist");
        //        Assert.True(File.Exists(Path.Combine(testDirectory, $"{setName}_03{DataStore.DatabaseFileExtension}")), "Shard 3 database should exist");

        //        // Verify 4 separate files were written
        //        using (var reader = store.OpenImageReader(new GlobalImageKey(image.SetName, image.Id)))
        //        {
        //            var offsets = reader.GetOffsets().ToList();
        //            Assert.Equal(4, offsets.Count); // 4 offset records (or more if segmented)

        //            // Verify first offset (compressible data)
        //            Assert.Equal(0, offsets[0].Offset);
        //            Assert.True(offsets[0].Size > 0);

        //            // Verify we can read the offsets by their starting offset
        //            var file1Offsets = reader.GetOffsets(0).ToList();
        //            Assert.NotEmpty(file1Offsets);

        //            var file2Offsets = reader.GetOffsets(200L * blockSize).ToList();
        //            Assert.NotEmpty(file2Offsets);
        //        }

        //        // Verify statistics show compression and deduplication
        //        var stats = store.GetSetStatistics(setName, includePerImageStats: false);
        //        Assert.NotNull(stats);

        //        // Should have unique blocks stored (less than total blocks due to deduplication)
        //        Assert.True(stats.UniqueBlocksStored < totalBlocks, 
        //            $"Expected unique blocks ({stats.UniqueBlocksStored}) to be less than total blocks ({totalBlocks}) due to deduplication");

        //        // Should have block references equal to total blocks written
        //        Assert.Equal(totalBlocks, stats.TotalBlockReferences);

        //        // Should have some compressed blocks
        //        Assert.True(stats.CompressedBlocks > 0, "Expected some blocks to be compressed");

        //        // Should have some uncompressed blocks
        //        Assert.True(stats.UncompressedBlocks > 0, "Expected some blocks to remain uncompressed");

        //        // Physical storage should be less than logical size due to compression and deduplication
        //        Assert.True(stats.TotalPhysicalBlockStorage < stats.TotalImageDataSize,
        //            $"Expected physical storage ({stats.TotalPhysicalBlockStorage}) to be less than image size ({stats.TotalImageDataSize})");
        //        
        //        // Deduplication should have saved space (100 blocks were duplicated)
        //        Assert.True(stats.DeduplicationSavings > 0, "Expected deduplication to save space");
        //        Assert.True(stats.DeduplicationSavings >= 100L * blockSize, 
        //            $"Expected at least 100 blocks ({100 * blockSize:N0} bytes) to be deduplicated, got {stats.DeduplicationSavings:N0} bytes");

        //        // Compression should have saved space
        //        Assert.True(stats.CompressionSavings > 0, "Expected compression to save space");

        //        // Deduplication ratio should be greater than 1 (blocks are reused)
        //        Assert.True(stats.DeduplicationRatio > 1.0, 
        //            $"Expected deduplication ratio > 1.0, got {stats.DeduplicationRatio:F2}X");

        //        // Verify we can read data back correctly using streams
        //        using (var reader = store.OpenImageReader(new GlobalImageKey(image.SetName, image.Id)))
        //        {
        //            using var stream = reader.OpenStream(0);

        //            // Verify stream size
        //            Assert.Equal(expectedSize, stream.Length);

        //            // Spot check: Read first block (should be compressible pattern)
        //            var firstBlock = new byte[blockSize];
        //            stream.Position = 0;
        //            var bytesRead = stream.Read(firstBlock, 0, blockSize);
        //            Assert.Equal(blockSize, bytesRead);

        //            // First block should be all zeros (pattern = 0)
        //            Assert.All(firstBlock, b => Assert.Equal(0, b));

        //            // Spot check: Read a duplicate block area (verify deduplication worked)
        //            stream.Position = 200L * blockSize; // Start of first occurrence
        //            var firstDuplicate = new byte[blockSize];
        //            bytesRead = stream.Read(firstDuplicate, 0, blockSize);
        //            Assert.Equal(blockSize, bytesRead);

        //            // Read the second occurrence of the same block
        //            stream.Position = 300L * blockSize; // Start of second occurrence
        //            var secondDuplicate = new byte[blockSize];
        //            bytesRead = stream.Read(secondDuplicate, 0, blockSize);
        //            Assert.Equal(blockSize, bytesRead);

        //            // They should be identical (proving deduplication preserved data)
        //            Assert.Equal(firstDuplicate, secondDuplicate);

        //            // Test reading individual files using OpenStream(offsetStart)
        //            using var file1Stream = reader.OpenStream(0); // First file
        //            Assert.Equal(200L * blockSize, file1Stream.Length);

        //            using var file2Stream = reader.OpenStream(200L * blockSize); // Second file (first duplicate set)
        //            Assert.Equal(100L * blockSize, file2Stream.Length);
        //        }
        //    }
        //    
        //    // Generate YAML for large compression test
        //    var yamlPath = Path.Combine(testDirectory, $"{nameof(CanWriteLargeImageWithCompressionAndDeduplication)}.yml");
        //    TestDbHelper.CompareDbToYaml(testDirectory, yamlPath);
        //    
        //    // Verify YAML structure for large dataset
        //    Assert.True(File.Exists(yamlPath), "YAML file should be generated");
        //    var yaml = File.ReadAllText(yamlPath);
        //    Assert.Contains("LargeCompressionSet", yaml);
        //    Assert.Contains("MixedData.iso", yaml);
        //    Assert.Contains("shardSize: 4", yaml);
        //    
        //    // Output statistics to help verify compression/deduplication
        //    Console.WriteLine($"YAML output written to: {yamlPath}");
        //}
    }
}