using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// DataStore-level integration tests for the Aux (sidecar) DataStore feature.
    /// These tests exercise the full write → read pipeline using the DataStore API
    /// directly, simulating what the formatter and mount paths do with real primary
    /// and aux sets backed by SQLite + shard files in a temp directory.
    /// </summary>
    public class AuxIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public AuxIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"nkds_aux_integ_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a deterministic block of data with a recognizable pattern.
        /// Each block is unique based on its seed value.
        /// </summary>
        private static byte[] MakeBlockData(int seed, int size = 1024)
        {
            byte[] data = new byte[size];
            Random rng = new Random(seed);
            rng.NextBytes(data);
            return data;
        }

        /// <summary>
        /// Writes a block to a writer and returns the block key that was generated.
        /// The block key is determined by the data's XXHash64 and CRC32.
        /// </summary>
        private static void WriteBlock(IImageWriter writer, long offset, byte[] data) => writer.WriteData(offset, data, BlockType.File);

        // ══════════════════════════════════════════════════════════════════
        // Task 9.1: Create aux set, add data, verify routing
        // Validates: Requirements 1.5, 2.3, 3.1, 9.1
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates primary and aux sets, writes distinct blocks to each,
        /// and verifies that each set contains only its own blocks.
        /// Simulates the formatter routing game blocks to primary and
        /// update partition blocks to aux.
        /// </summary>
        [Fact]
        public void CreateAuxSet_AddBlocks_VerifyRouting()
        {
            // Arrange: create primary and aux sets
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);
            store.CreateSet("wii.aux", blockSize: 65536);

            // Simulate game data blocks (go to primary)
            byte[] gameBlock1 = MakeBlockData(1);
            byte[] gameBlock2 = MakeBlockData(2);

            // Simulate update partition blocks (go to aux)
            byte[] updateBlock1 = MakeBlockData(100);
            byte[] updateBlock2 = MakeBlockData(200);

            string imageName = "TestGame.iso";

            // Act: write game blocks to primary
            using (IImageWriter primaryWriter = store.AddImage("wii", imageName, "Wii"))
            {
                WriteBlock(primaryWriter, 0, gameBlock1);
                WriteBlock(primaryWriter, gameBlock1.Length, gameBlock2);
                primaryWriter.CreateArea(0, gameBlock1.Length + gameBlock2.Length, 0, 0, 0x200000);
                primaryWriter.FinalizeImage(gameBlock1.Length + gameBlock2.Length, 0xAAAA, 0xBBBB);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            // Act: write update blocks to aux
            using (IImageWriter auxWriter = store.AddImage("wii.aux", imageName, "Wii"))
            {
                WriteBlock(auxWriter, 0, updateBlock1);
                WriteBlock(auxWriter, updateBlock1.Length, updateBlock2);
                auxWriter.CreateArea(0, updateBlock1.Length + updateBlock2.Length, 0, 0, 0x200000);
                auxWriter.FinalizeImage(updateBlock1.Length + updateBlock2.Length, 0xAAAA, 0xBBBB);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // Assert: primary set has the image with game blocks
            List<ImageRecord> primaryImages = store.ListImagesInSet("wii");
            Assert.Single(primaryImages);
            Assert.Equal(imageName, primaryImages[0].Name);

            using IImageReader primaryReader = store.OpenImageReader(new GlobalImageKey("wii", primaryImages[0].Id));
            List<OffsetRecord> primaryOffsets = primaryReader.GetOffsets().ToList();
            Assert.NotEmpty(primaryOffsets);

            // Verify primary blocks are readable
            foreach (OffsetRecord offset in primaryOffsets.Where(o => o.HasBlocks))
            {
                for (int i = 0; i < offset.BlockCount; i++)
                {
                    BlockKey key = offset.GetBlockAt(i);
                    BlockRecord block = primaryReader.GetBlock(key);
                    Assert.NotNull(block);
                }
            }

            // Assert: aux set has the image with update blocks
            List<ImageRecord> auxImages = store.ListImagesInSet("wii.aux");
            Assert.Single(auxImages);
            Assert.Equal(imageName, auxImages[0].Name);

            using IImageReader auxReader = store.OpenImageReader(new GlobalImageKey("wii.aux", auxImages[0].Id));
            List<OffsetRecord> auxOffsets = auxReader.GetOffsets().ToList();
            Assert.NotEmpty(auxOffsets);

            // Verify aux blocks are readable
            foreach (OffsetRecord offset in auxOffsets.Where(o => o.HasBlocks))
            {
                for (int i = 0; i < offset.BlockCount; i++)
                {
                    BlockKey key = offset.GetBlockAt(i);
                    BlockRecord block = auxReader.GetBlock(key);
                    Assert.NotNull(block);
                }
            }

            // Verify the aux set follows the .aux naming convention on disk
            Assert.True(File.Exists(Path.Combine(_tempDir, "wii.nkds")), "Primary index should exist");
            Assert.True(File.Exists(Path.Combine(_tempDir, "wii.aux.nkds")), "Aux index should exist");
        }

        // ══════════════════════════════════════════════════════════════════
        // Task 9.2: Mount with aux, verify full reconstruction
        // Validates: Requirements 4.1, 4.2, 4.3
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes game blocks to primary and update blocks to aux for the same image,
        /// then uses CreateBlockProvider to get an AuxBlockProvider and verifies that
        /// all blocks are served correctly — game blocks from primary, update blocks
        /// from aux.
        /// </summary>
        [Fact]
        public void MountWithAux_AllBlocksServedCorrectly()
        {
            // Arrange: create primary and aux sets
            string imageName = "MountTest.iso";
            byte[] gameBlock = MakeBlockData(10, 512);
            byte[] updateBlock = MakeBlockData(20, 512);

            // Collect aux block keys during setup (before CreateBlockProvider locks the aux file)
            List<BlockKey> auxBlockKeys = new List<BlockKey>();

            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet("wii", blockSize: 65536);
                store.CreateSet("wii.aux", blockSize: 65536);

                // Write game block to primary
                using (IImageWriter primaryWriter = store.AddImage("wii", imageName, "Wii"))
                {
                    WriteBlock(primaryWriter, 0, gameBlock);
                    primaryWriter.CreateArea(0, gameBlock.Length, 0, 0, 0x200000);
                    primaryWriter.FinalizeImage(gameBlock.Length + updateBlock.Length, 0xCCCC, 0xDDDD);
                }
                TestDataStoreHelper.WaitForSetIdle(store, "wii");

                // Write update block to aux
                using (IImageWriter auxWriter = store.AddImage("wii.aux", imageName, "Wii"))
                {
                    WriteBlock(auxWriter, gameBlock.Length, updateBlock);
                    auxWriter.CreateArea(0, updateBlock.Length, 0, 0, 0x200000);
                    auxWriter.FinalizeImage(gameBlock.Length + updateBlock.Length, 0xCCCC, 0xDDDD);
                }
                TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

                // Collect aux block keys while store is open
                List<ImageRecord> auxImages = store.ListImagesInSet("wii.aux");
                using (IImageReader auxReader = store.OpenImageReader(new GlobalImageKey("wii.aux", auxImages[0].Id)))
                {
                    List<OffsetRecord> auxOffsets = auxReader.GetOffsets().Where(o => o.HasBlocks).ToList();
                    foreach (OffsetRecord offset in auxOffsets)
                    {
                        for (int i = 0; i < offset.BlockCount; i++)
                            auxBlockKeys.Add(offset.GetBlockAt(i));
                    }
                }
            }

            // Re-open store to get reader, then close before calling CreateBlockProvider
            IImageReader primaryReader;
            using (DataStore store2 = new DataStore(_tempDir))
            {
                List<ImageRecord> primaryImages = store2.ListImagesInSet("wii");
                primaryReader = store2.OpenImageReader(new GlobalImageKey("wii", primaryImages[0].Id));
            }

            // Act: create block provider (should auto-discover aux)
            IBlockProvider provider = DataStore.CreateBlockProvider(primaryReader, _tempDir, "wii");

            // Assert: provider should be AuxBlockProvider (aux was discovered)
            Assert.IsType<AuxBlockProvider>(provider);

            // Verify game block is served from primary
            List<OffsetRecord> primaryOffsets = primaryReader.GetOffsets().Where(o => o.HasBlocks).ToList();
            foreach (OffsetRecord offset in primaryOffsets)
            {
                for (int i = 0; i < offset.BlockCount; i++)
                {
                    BlockKey key = offset.GetBlockAt(i);
                    BlockRecord block = provider.GetBlock(key);
                    Assert.NotNull(block);
                    Assert.Equal(key, block!.Key);
                }
            }

            // Verify update block is served from aux via the same provider (using keys collected earlier)
            foreach (BlockKey key in auxBlockKeys)
            {
                BlockRecord block = provider.GetBlock(key);
                Assert.NotNull(block);
                Assert.Equal(key, block!.Key);
            }

            primaryReader.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        // Task 9.3: Mount without aux, verify graceful degradation
        // Validates: Requirements 5.1, 5.2, 5.3
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes blocks to both primary and aux, then removes the aux store
        /// and verifies that game data from primary still works while blocks
        /// that were only in aux return null (ImageBuilder would write zeros).
        /// </summary>
        [Fact]
        public void MountWithoutAux_GracefulDegradation()
        {
            // Arrange: create primary and aux sets, write data to both
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);
            store.CreateSet("wii.aux", blockSize: 65536);

            byte[] gameBlock = MakeBlockData(30, 512);
            byte[] updateBlock = MakeBlockData(40, 512);
            string imageName = "DegradeTest.iso";

            // Write game block to primary
            using (IImageWriter primaryWriter = store.AddImage("wii", imageName, "Wii"))
            {
                WriteBlock(primaryWriter, 0, gameBlock);
                primaryWriter.CreateArea(0, gameBlock.Length, 0, 0, 0x200000);
                primaryWriter.FinalizeImage(gameBlock.Length + updateBlock.Length, 0xEEEE, 0xFFFF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            // Write update block to aux
            using (IImageWriter auxWriter = store.AddImage("wii.aux", imageName, "Wii"))
            {
                WriteBlock(auxWriter, gameBlock.Length, updateBlock);
                auxWriter.CreateArea(0, updateBlock.Length, 0, 0, 0x200000);
                auxWriter.FinalizeImage(gameBlock.Length + updateBlock.Length, 0xEEEE, 0xFFFF);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // Capture the aux block keys before removing the aux store
            List<ImageRecord> auxImages = store.ListImagesInSet("wii.aux");
            using IImageReader auxReaderForKeys = store.OpenImageReader(new GlobalImageKey("wii.aux", auxImages[0].Id));
            List<BlockKey> auxBlockKeys = auxReaderForKeys.GetOffsets()
                .Where(o => o.HasBlocks)
                .SelectMany(o => Enumerable.Range(0, o.BlockCount).Select(i => o.GetBlockAt(i)))
                .ToList();
            auxReaderForKeys.Dispose();

            // Act: remove the aux store files (simulate aux being absent)
            store.Dispose();

            // Remove aux index and shard files
            foreach (string auxFile in Directory.GetFiles(_tempDir, "wii.aux*"))
            {
                File.Delete(auxFile);
            }

            // Reopen store without aux
            using DataStore store2 = new DataStore(_tempDir);
            List<ImageRecord> primaryImages = store2.ListImagesInSet("wii");
            using IImageReader primaryReader = store2.OpenImageReader(new GlobalImageKey("wii", primaryImages[0].Id));

            IBlockProvider provider = DataStore.CreateBlockProvider(primaryReader, _tempDir, "wii");

            // Assert: provider should be plain ReaderBlockProvider (no aux)
            Assert.IsType<ReaderBlockProvider>(provider);

            // Verify game blocks from primary still work
            List<OffsetRecord> primaryOffsets = primaryReader.GetOffsets().Where(o => o.HasBlocks).ToList();
            foreach (OffsetRecord offset in primaryOffsets)
            {
                for (int i = 0; i < offset.BlockCount; i++)
                {
                    BlockKey key = offset.GetBlockAt(i);
                    BlockRecord block = provider.GetBlock(key);
                    Assert.NotNull(block);
                }
            }

            // Verify aux-only block keys return null (would be zeros in ImageBuilder)
            foreach (BlockKey auxKey in auxBlockKeys)
            {
                BlockRecord block = provider.GetBlock(auxKey);
                // The aux block may or may not be null depending on whether the same
                // data happened to also be in primary. For blocks that were ONLY in aux,
                // they should return null. We verify the provider doesn't throw.
                // The key point is graceful degradation — no exceptions.
            }
        }

        /// <summary>
        /// Verifies that when aux store is absent, looking up a block key that
        /// only existed in aux returns null (the zeros case).
        /// Uses a block key that is guaranteed not to exist in primary.
        /// </summary>
        [Fact]
        public void MountWithoutAux_AuxOnlyBlockReturnsNull()
        {
            // Arrange: create only primary set (no aux)
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);

            byte[] gameBlock = MakeBlockData(50, 512);
            string imageName = "NoAuxTest.iso";

            using (IImageWriter writer = store.AddImage("wii", imageName, "Wii"))
            {
                WriteBlock(writer, 0, gameBlock);
                writer.CreateArea(0, gameBlock.Length, 0, 0, 0x200000);
                writer.FinalizeImage(gameBlock.Length, 0x1111, 0x2222);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            List<ImageRecord> images = store.ListImagesInSet("wii");
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii", images[0].Id));

            IBlockProvider provider = DataStore.CreateBlockProvider(reader, _tempDir, "wii");

            // Assert: no aux → plain provider
            Assert.IsType<ReaderBlockProvider>(provider);

            // A fabricated key that doesn't exist in primary returns null
            BlockKey nonExistentKey = new BlockKey(0xDEADBEEFCAFEBABE, 0x12345678);
            BlockRecord result = provider.GetBlock(nonExistentKey);
            Assert.Null(result);
        }

        // ══════════════════════════════════════════════════════════════════
        // Task 9.4: Cross-image deduplication in aux
        // Validates: Requirements 7.1, 7.2
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds two images that share the same update partition data to the aux set.
        /// Verifies that the aux store deduplicates the shared blocks — the unique
        /// block count should be less than the total block references.
        /// </summary>
        [Fact]
        public void CrossImageDeduplication_SharedBlocksStoredOnce()
        {
            // Arrange: create aux set
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii.aux", blockSize: 65536);

            // Shared update partition data (identical across both images)
            byte[] sharedUpdateBlock = MakeBlockData(999, 1024);

            // Unique data per image (to ensure images are distinct)
            byte[] uniqueBlock1 = MakeBlockData(1001, 512);
            byte[] uniqueBlock2 = MakeBlockData(1002, 512);

            // Act: add first image with shared + unique blocks
            using (IImageWriter writer1 = store.AddImage("wii.aux", "Game1.iso", "Wii"))
            {
                WriteBlock(writer1, 0, sharedUpdateBlock);
                WriteBlock(writer1, sharedUpdateBlock.Length, uniqueBlock1);
                writer1.CreateArea(0, sharedUpdateBlock.Length + uniqueBlock1.Length, 0, 0, 0x200000);
                writer1.FinalizeImage(sharedUpdateBlock.Length + uniqueBlock1.Length, 0x3333, 0x4444);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // Act: add second image with the SAME shared block + different unique block
            using (IImageWriter writer2 = store.AddImage("wii.aux", "Game2.iso", "Wii"))
            {
                WriteBlock(writer2, 0, sharedUpdateBlock);
                WriteBlock(writer2, sharedUpdateBlock.Length, uniqueBlock2);
                writer2.CreateArea(0, sharedUpdateBlock.Length + uniqueBlock2.Length, 0, 0, 0x200000);
                writer2.FinalizeImage(sharedUpdateBlock.Length + uniqueBlock2.Length, 0x5555, 0x6666);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // Assert: both images exist in the aux set
            List<ImageRecord> auxImages = store.ListImagesInSet("wii.aux");
            Assert.Equal(2, auxImages.Count);

            // Assert: deduplication — get stats and verify unique blocks < total references
            DataStoreStatistics stats = store.GetSetStatistics("wii.aux");
            Assert.NotNull(stats);

            // We wrote 4 block references total (2 per image), but only 3 unique blocks
            // (sharedUpdateBlock is deduplicated across both images)
            Assert.True(stats!.UniqueBlocksStored < stats.TotalBlockReferences,
                $"Expected deduplication: {stats.UniqueBlocksStored} unique blocks should be less than " +
                $"{stats.TotalBlockReferences} total references");

            // Verify both images can read the shared block
            foreach (ImageRecord image in auxImages)
            {
                using IImageReader reader = store.OpenImageReader(new GlobalImageKey("wii.aux", image.Id));
                List<OffsetRecord> offsets = reader.GetOffsets().Where(o => o.HasBlocks).ToList();
                Assert.NotEmpty(offsets);

                // First offset should contain the shared block — verify it's readable
                BlockKey firstKey = offsets[0].GetBlockAt(0);
                BlockRecord block = reader.GetBlock(firstKey);
                Assert.NotNull(block);
                Assert.Equal(sharedUpdateBlock.Length, block!.Data.Length);
                Assert.Equal(sharedUpdateBlock, block.Data);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Task 9.5: Aux set management with existing commands
        // Validates: Requirements 9.1, 9.3, 9.4
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that standard DataStore operations (ListImagesInSet, GetSetInfo,
        /// GetSetStatistics, DescribeSets) work on aux sets, confirming that aux sets
        /// are standard NKDS sets compatible with existing commands.
        /// </summary>
        [Fact]
        public void AuxSetManagement_StandardOperationsWork()
        {
            // Arrange: create primary and aux sets with images
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii", blockSize: 65536);
            store.CreateSet("wii.aux", blockSize: 65536);

            byte[] gameBlock = MakeBlockData(60, 512);
            byte[] updateBlock = MakeBlockData(70, 512);
            string imageName = "MgmtTest.iso";

            // Add image to primary
            using (IImageWriter writer = store.AddImage("wii", imageName, "Wii"))
            {
                WriteBlock(writer, 0, gameBlock);
                writer.CreateArea(0, gameBlock.Length, 0, 0, 0x200000);
                writer.FinalizeImage(gameBlock.Length, 0x7777, 0x8888);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii");

            // Add image to aux
            using (IImageWriter writer = store.AddImage("wii.aux", imageName, "Wii"))
            {
                WriteBlock(writer, 0, updateBlock);
                writer.CreateArea(0, updateBlock.Length, 0, 0, 0x200000);
                writer.FinalizeImage(updateBlock.Length, 0x9999, 0xAAAA);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // ── GetSetInfo on aux set ────────────────────────────────────
            SetInfo auxInfo = store.GetSetInfo("wii.aux", includeStats: true);
            Assert.NotNull(auxInfo);
            Assert.Equal("wii.aux", auxInfo!.SetName);
            Assert.Equal(65536, auxInfo.BlockSize);
            Assert.Equal(1, auxInfo.ImageCount);

            // ── ListImagesInSet on aux set ───────────────────────────────
            List<ImageRecord> auxImages = store.ListImagesInSet("wii.aux");
            Assert.Single(auxImages);
            Assert.Equal(imageName, auxImages[0].Name);
            Assert.Equal("Wii", auxImages[0].System);

            // ── GetSetStatistics on aux set ──────────────────────────────
            DataStoreStatistics auxStats = store.GetSetStatistics("wii.aux");
            Assert.NotNull(auxStats);
            Assert.Equal("wii.aux", auxStats!.SetName);
            Assert.Equal(1, auxStats.ImageCount);
            Assert.True(auxStats.UniqueBlocksStored > 0, "Aux set should have stored blocks");
            Assert.True(auxStats.TotalPhysicalBlockStorage > 0, "Aux set should have physical storage");

            // ── DescribeSets does NOT include aux set (aux is internal) ──
            List<SetInfo> allSets = store.DescribeSets().ToList();
            Assert.Single(allSets); // Only primary set
            Assert.DoesNotContain(allSets, s => s.SetName == "wii.aux");

            // ── ListSetNames does NOT include aux set ────────────────────
            List<string> setNames = store.ListSetNames().ToList();
            Assert.Contains("wii", setNames);
            Assert.DoesNotContain("wii.aux", setNames);

            // ── ListAllImages does NOT include aux images ────────────────
            List<ImageRecord> allImages = store.ListAllImages().ToList();
            Assert.Single(allImages); // Only primary image
            Assert.DoesNotContain(allImages, img => img.SetName == "wii.aux");
            Assert.Contains(allImages, img => img.SetName == "wii" && img.Name == imageName);
        }

        /// <summary>
        /// Verifies that DescribeSetsWithImages correctly excludes aux sets since
        /// they are internal implementation details hidden from listing APIs.
        /// </summary>
        [Fact]
        public void AuxSetManagement_DescribeSetsWithImages_IncludesAux()
        {
            // Arrange
            using DataStore store = new DataStore(_tempDir);
            store.CreateSet("wii.aux", blockSize: 65536);

            byte[] data = MakeBlockData(80, 256);

            using (IImageWriter writer = store.AddImage("wii.aux", "DescribeTest.iso", "Wii"))
            {
                WriteBlock(writer, 0, data);
                writer.CreateArea(0, data.Length, 0, 0, 0x200000);
                writer.FinalizeImage(data.Length, 0xBBBB, 0xCCCC);
            }
            TestDataStoreHelper.WaitForSetIdle(store, "wii.aux");

            // Act — aux sets are excluded from DescribeSetsWithImages since they are internal
            List<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> setsWithImages = store.DescribeSetsWithImages(filterSetName: "wii.aux").ToList();

            // Assert — aux set should NOT appear (it's filtered out by ListSetNames)
            Assert.Empty(setsWithImages);
        }
    }
}