using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DataStore.ResolveAuxSetName"/> and related aux discovery.
    /// Validates convention-based aux discovery (Requirements 1.1, 1.2, 1.3, 1.4)
    /// and graceful degradation (Requirements 5.1–5.4, 10.1, 10.2).
    /// </summary>
    public class AuxDiscoveryTests : IDisposable
    {
        private readonly string _tempDir;

        public AuxDiscoveryTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds_aux_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void ResolveAuxSetName_AuxExists_ReturnsAuxSetName()
        {
            // Arrange: create primary and aux index files
            string primaryPath = Path.Combine(_tempDir, "wii.nkds");
            string auxPath = Path.Combine(_tempDir, "wii.aux.nkds");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());
            File.WriteAllBytes(auxPath, Array.Empty<byte>());

            // Act
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Assert
            Assert.Equal("wii.aux", result);
        }

        [Fact]
        public void ResolveAuxSetName_AuxMissing_ReturnsNull()
        {
            // Arrange: create only the primary index file
            string primaryPath = Path.Combine(_tempDir, "wii.nkds");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());

            // Act
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ResolveAuxSetName_NullInput_ReturnsNull() => Assert.Null(DataStore.ResolveAuxSetName(null!));

        [Fact]
        public void ResolveAuxSetName_EmptyInput_ReturnsNull() => Assert.Null(DataStore.ResolveAuxSetName(""));

        [Fact]
        public void ResolveAuxSetName_WhitespaceInput_ReturnsNull() => Assert.Null(DataStore.ResolveAuxSetName("   "));

        [Fact]
        public void ResolveAuxSetName_DifferentSetNames_ReturnsCorrectAuxName()
        {
            // Arrange
            string primaryPath = Path.Combine(_tempDir, "gamecube.nkds");
            string auxPath = Path.Combine(_tempDir, "gamecube.aux.nkds");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());
            File.WriteAllBytes(auxPath, Array.Empty<byte>());

            // Act
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Assert
            Assert.Equal("gamecube.aux", result);
        }

        [Fact]
        public void ResolveAuxSetName_AuxShardNamingConvention_FollowsPattern()
        {
            // Verify the naming convention: aux shards are {name}.aux_NNNN.nkds
            // The method itself only checks for the index file, but we verify the
            // returned set name can be used to derive shard file names.
            string primaryPath = Path.Combine(_tempDir, "wii.nkds");
            string auxPath = Path.Combine(_tempDir, "wii.aux.nkds");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());
            File.WriteAllBytes(auxPath, Array.Empty<byte>());

            string auxSetName = DataStore.ResolveAuxSetName(primaryPath);
            Assert.NotNull(auxSetName);

            // Verify the aux set name can derive shard paths: {auxSetName}_0000.nkds
            string expectedShardName = $"{auxSetName}_0000{DataStore.DatabaseFileExtension}";
            Assert.Equal("wii.aux_0000.nkds", expectedShardName);
        }

        [Fact]
        public void ResolveAuxSetName_OnlyShardExists_ReturnsNull()
        {
            // If only aux shard files exist but not the index, should return null
            string primaryPath = Path.Combine(_tempDir, "wii.nkds");
            string auxShardPath = Path.Combine(_tempDir, "wii.aux_0000.nkds");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());
            File.WriteAllBytes(auxShardPath, Array.Empty<byte>());

            string result = DataStore.ResolveAuxSetName(primaryPath);

            Assert.Null(result);
        }

        [Fact]
        public void ResolveAuxSetName_AuxConstant_MatchesExpectedValue() =>
            // Verify the constant is ".aux" as expected by the naming convention
            Assert.Equal(".aux", DataStore._AuxSetSuffix);

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_MatchingBlockSize_ReturnsAuxSetName()
        {
            // Arrange: create primary and aux sets with the same block size
            using (DataStore primaryStore = new DataStore(_tempDir))
            {
                primaryStore.CreateSet("wii", blockSize: 65536);
                primaryStore.CreateSet("wii.aux", blockSize: 65536);
            }

            string primaryPath = Path.Combine(_tempDir, "wii.nkds");

            // Act
            string result = DataStore.ResolveAuxSetNameWithBlockSizeValidation(primaryPath, 65536);

            // Assert
            Assert.Equal("wii.aux", result);
        }

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_MismatchedBlockSize_ReturnsNull()
        {
            // Arrange: create primary and aux sets with different block sizes
            using DataStore primaryStore = new DataStore(_tempDir);
            primaryStore.CreateSet("wii", blockSize: 65536);
            primaryStore.CreateSet("wii.aux", blockSize: 32768);

            string primaryPath = Path.Combine(_tempDir, "wii.nkds");

            // Act
            string result = DataStore.ResolveAuxSetNameWithBlockSizeValidation(primaryPath, 65536);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_AuxMissing_ReturnsNull()
        {
            // Arrange: create only the primary set
            using DataStore primaryStore = new DataStore(_tempDir);
            primaryStore.CreateSet("wii", blockSize: 65536);

            string primaryPath = Path.Combine(_tempDir, "wii.nkds");

            // Act
            string result = DataStore.ResolveAuxSetNameWithBlockSizeValidation(primaryPath, 65536);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_NullInput_ReturnsNull() => Assert.Null(DataStore.ResolveAuxSetNameWithBlockSizeValidation(null!, 65536));

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_EmptyInput_ReturnsNull() => Assert.Null(DataStore.ResolveAuxSetNameWithBlockSizeValidation("", 65536));

        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_DefaultBlockSize_MatchesWhenBothDefault()
        {
            // Arrange: create primary and aux sets with default block size
            // When blockSize=0 is passed to CreateSet, the actual stored value is 0x10000 (65536)
            int storedBlockSize;
            using (DataStore primaryStore = new DataStore(_tempDir))
            {
                primaryStore.CreateSet("wii");
                primaryStore.CreateSet("wii.aux");

                // The stored block size is 65536 (0x10000), not 0
                SetInfo primaryInfo = primaryStore.GetSetInfo("wii");
                Assert.NotNull(primaryInfo);
                storedBlockSize = primaryInfo!.BlockSize;
            }

            string primaryPath = Path.Combine(_tempDir, "wii.nkds");

            // Act
            string result = DataStore.ResolveAuxSetNameWithBlockSizeValidation(primaryPath, storedBlockSize);

            // Assert
            Assert.Equal("wii.aux", result);
        }

        // ── Task 6.1: Absent aux store at read time ──────────────────────────

        /// <summary>
        /// Validates Requirement 5.1, 5.2: When aux .aux.nkds is not found during
        /// mount/export/verify, CreateBlockProvider returns a plain ReaderBlockProvider
        /// (no AuxBlockProvider wrapping). All block lookups go to primary only.
        /// </summary>
        [Fact]
        public void CreateBlockProvider_AuxAbsent_ReturnsPrimaryOnlyProvider()
        {
            // Arrange: create a primary set with an image, but NO aux set
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);

            using (IImageWriter writer = store.AddImage("wii", "TestImage", "Wii"))
            {
                writer.CreateArea(0, 1024, 0, 0, 0x200000);
                writer.FinalizeImage(1024, 0xDEAD, 0xBEEF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            List<ImageRecord> images = store.ListImagesInSet("wii");
            Assert.Single(images);

            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii", images[0].Id));

            // Act
            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Assert: should be a plain ReaderBlockProvider, NOT an AuxBlockProvider
            Assert.IsType<ReaderBlockProvider>(provider);
        }

        /// <summary>
        /// Validates Requirement 5.3: When blocks are missing from the primary and
        /// no aux store is available, lookups return null (ImageBuilder writes zeros).
        /// </summary>
        [Fact]
        public void CreateBlockProvider_AuxAbsent_MissingBlocksReturnNull()
        {
            // Arrange: create a primary set with an image
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);

            using (IImageWriter writer = store.AddImage("wii", "TestImage", "Wii"))
            {
                writer.CreateArea(0, 1024, 0, 0, 0x200000);
                writer.FinalizeImage(1024, 0xDEAD, 0xBEEF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            List<ImageRecord> images = store.ListImagesInSet("wii");
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii", images[0].Id));

            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Act: look up a block key that doesn't exist in the primary
            BlockKey nonExistentKey = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);
            BlockRecord result = provider.GetBlock(nonExistentKey);

            // Assert: should return null (ImageBuilder would write zeros)
            Assert.Null(result);
        }

        // ── Task 6.2: Corrupted aux store ────────────────────────────────────

        /// <summary>
        /// Validates Requirement 5.4: When aux store file is corrupted (invalid SQLite),
        /// ResolveAuxSetNameWithBlockSizeValidation catches the exception and returns null,
        /// falling back to primary-only mode.
        /// </summary>
        [Fact]
        public void ResolveAuxSetNameWithBlockSizeValidation_CorruptedAuxStore_ReturnsNull()
        {
            // Arrange: create a valid primary set
            using DataStore primaryStore = new DataStore(_tempDir);
            primaryStore.CreateSet("wii", blockSize: 65536);

            // Write garbage to the aux .nkds file to simulate corruption
            string auxPath = Path.Combine(_tempDir, "wii.aux.nkds");
            File.WriteAllText(auxPath, "this is not a valid SQLite database");

            string primaryPath = Path.Combine(_tempDir, "wii.nkds");

            // Act
            string result = DataStore.ResolveAuxSetNameWithBlockSizeValidation(primaryPath, 65536);

            // Assert: should return null (corrupted aux → fall back to primary-only)
            Assert.Null(result);
        }

        /// <summary>
        /// Validates Requirement 5.4: When aux store is corrupted, CreateBlockProvider
        /// catches the exception and returns a plain ReaderBlockProvider.
        /// </summary>
        [Fact]
        public void CreateBlockProvider_CorruptedAuxStore_ReturnsPrimaryOnlyProvider()
        {
            // Arrange: create a valid primary set with an image
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);

            using (IImageWriter writer = store.AddImage("wii", "TestImage", "Wii"))
            {
                writer.CreateArea(0, 1024, 0, 0, 0x200000);
                writer.FinalizeImage(1024, 0xDEAD, 0xBEEF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            List<ImageRecord> images = store.ListImagesInSet("wii");
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii", images[0].Id));

            // Write garbage to the aux .nkds file to simulate corruption
            string auxPath = Path.Combine(_tempDir, "wii.aux.nkds");
            File.WriteAllText(auxPath, "this is not a valid SQLite database");

            // Act
            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Assert: should fall back to plain ReaderBlockProvider
            Assert.IsType<ReaderBlockProvider>(provider);
        }

        // ── Task 6.3: Block size mismatch at read time ───────────────────────

        /// <summary>
        /// Validates Requirements 10.1, 10.2: When aux store has a different block size
        /// than the primary, ResolveAuxSetNameWithBlockSizeValidation returns null and
        /// the aux attachment is skipped.
        /// </summary>
        [Fact]
        public void CreateBlockProvider_BlockSizeMismatch_ReturnsPrimaryOnlyProvider()
        {
            // Arrange: create primary set with blockSize=65536 and aux set with blockSize=32768
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);
            store.CreateSet("wii.aux", blockSize: 32768);

            using (IImageWriter writer = store.AddImage("wii", "TestImage", "Wii"))
            {
                writer.CreateArea(0, 1024, 0, 0, 0x200000);
                writer.FinalizeImage(1024, 0xDEAD, 0xBEEF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            List<ImageRecord> images = store.ListImagesInSet("wii");
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii", images[0].Id));

            // Act
            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Assert: should fall back to plain ReaderBlockProvider due to block size mismatch
            Assert.IsType<ReaderBlockProvider>(provider);
        }

        /// <summary>
        /// Validates Requirements 10.1, 10.2: When block sizes match, CreateBlockProvider
        /// returns an AuxBlockProvider (confirming the mismatch check is the gate).
        /// </summary>
        [Fact]
        public void CreateBlockProvider_BlockSizeMatch_ReturnsAuxBlockProvider()
        {
            // Arrange: create primary and aux sets with matching block sizes and matching images
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet("wii", blockSize: 65536);
                store.CreateSet("wii.aux", blockSize: 65536);

                using (IImageWriter primaryWriter = store.AddImage("wii", "TestImage", "Wii"))
                {
                    primaryWriter.CreateArea(0, 1024, 0, 0, 0x200000);
                    primaryWriter.FinalizeImage(1024, 0xDEAD, 0xBEEF);
                }
                TestDataStoreHelper.WaitForSetIdle(store, "wii");

                using (IImageWriter auxWriter = store.AddImage("wii.aux", "TestImage", "Wii"))
                {
                    auxWriter.CreateArea(0, 1024, 0, 0, 0x200000);
                    auxWriter.FinalizeImage(1024, 0xDEAD, 0xBEEF);
                }
                TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");
            }

            // Re-open store to get reader, then close store before calling CreateBlockProvider
            IImageReader reader;
            using (DataStore store2 = new DataStore(_tempDir))
            {
                List<ImageRecord> images = store2.ListImagesInSet("wii");
                reader = store2.OpenImageReader(new GlobalImageKey("wii", images[0].Id));
            }

            // Act - store is closed, CreateBlockProvider can open its own DataStore
            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Assert: should return AuxBlockProvider since aux exists with matching block size and image
            Assert.IsType<AuxBlockProvider>(provider);

            reader.Dispose();
        }
    }
}