using NKitDataStore.Binary;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Bug condition exploration tests for shard compaction corruption.
    /// 
    /// These tests encode the EXPECTED (correct) behavior after compaction:
    /// - Block index contains ONLY entries referenced by live images
    /// - No orphaned shard files exist after all images are removed
    /// - All remaining images pass full verification (block data integrity)
    /// 
    /// On UNFIXED code, these tests are EXPECTED TO FAIL because the
    /// `anyShardModified` early return at line ~1268 in CompactShards prevents
    /// block index pruning when remaining blocks don't move.
    /// 
    /// **Validates: Requirements 1.2, 1.5, 2.2, 2.6, 2.7, 2.8**
    /// </summary>
    public class CompactShardCorruptionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public CompactShardCorruptionTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactCorruptionTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Test Case 1: Add 2 images in separate mode, remove image 1 whose blocks are at the
        /// start of the shard (remaining blocks don't move), call CompactSet, verify image 2
        /// passes full verification.
        /// 
        /// Bug Condition: Image 1's blocks are at offset 0 in the shard. Image 2's blocks follow.
        /// When image 1 is removed, image 2's blocks don't need to move (they're already contiguous
        /// after the removed blocks are gone — but the shard DOES need rewriting to remove the gap).
        /// However, if the shard doesn't require compaction (e.g., blocks are already contiguous
        /// from the perspective of live data), `anyShardModified` stays false and block index
        /// pruning is skipped.
        /// 
        /// Expected: After CompactSet, block index contains ONLY image 2's block entries.
        /// Image 2 can be fully read back with correct data.
        /// 
        /// **Validates: Requirements 1.2, 2.2, 2.7**
        /// </summary>
        [Fact]
        public void RemoveFirstImage_CompactSet_RemainingImageVerifies()
        {
            const string setName = "TwoImageSet";
            const int blockSize = 65536; // 64KB
            // Use a large shard size so both images end up in the same shard file
            const long shardSize = 50L * 1024 * 1024;

            // Prepare distinct data for each image
            byte[] image1Data = new byte[blockSize * 2]; // 2 blocks
            byte[] image2Data = new byte[blockSize * 2]; // 2 blocks
            Random rnd = new Random(42);
            rnd.NextBytes(image1Data);
            rnd.NextBytes(image2Data);

            // Track block keys for each image
            HashSet<BlockKey> image1BlockKeys;
            HashSet<BlockKey> image2BlockKeys;

            // Step 1: Create set and add 2 images
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                using (IImageWriter writer = store.AddImage(setName, "Image1.bin"))
                {
                    writer.WriteData(0, image1Data, BlockType.File);
                    writer.FinalizeImage(image1Data.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                using (IImageWriter writer = store.AddImage(setName, "Image2.bin"))
                {
                    writer.WriteData(0, image2Data, BlockType.File);
                    writer.FinalizeImage(image2Data.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Collect block keys for each image
                image1BlockKeys = GetImageBlockKeys(store, setName, 1);
                image2BlockKeys = GetImageBlockKeys(store, setName, 2);

                _output.WriteLine($"Image 1 has {image1BlockKeys.Count} blocks");
                _output.WriteLine($"Image 2 has {image2BlockKeys.Count} blocks");

                // Verify both images are readable before deletion
                VerifyImageData(store, setName, 2, image2Data);

                // Step 2: Delete image 1
                store.DeleteImage(new GlobalImageKey(setName, 1));

                // Step 3: Compact the set
                store.CompactSet(setName);
            }

            // Step 4: Reopen and verify image 2 data integrity
            using (DataStore store = new DataStore(_tempDir))
            {
                // Verify image 2 can be fully read back with correct data
                VerifyImageData(store, setName, 2, image2Data);

                // Verify block index only contains image 2's blocks
                HashSet<BlockKey> remainingBlockKeys = GetAllBlockIndexKeys(setName);
                _output.WriteLine($"Block index has {remainingBlockKeys.Count} entries after compact");
                _output.WriteLine($"Image 2 has {image2BlockKeys.Count} expected block entries");

                // The block index should NOT contain image 1's blocks
                foreach (BlockKey key in image1BlockKeys)
                {
                    Assert.DoesNotContain(key, remainingBlockKeys);
                }

                // The block index SHOULD contain image 2's blocks
                foreach (BlockKey key in image2BlockKeys)
                {
                    Assert.Contains(key, remainingBlockKeys);
                }

                // Block index entry count should equal image 2's block count
                Assert.Equal(image2BlockKeys.Count, remainingBlockKeys.Count);
            }
        }

        /// <summary>
        /// Test Case 2: Add 3 images, remove all 3, call CompactSet, verify block index is empty
        /// and no shard files remain on disk.
        /// 
        /// Bug Condition: When all images are removed, liveImages.Count == 0 but there are
        /// removed entries. The current code may early-return without cleaning up.
        /// 
        /// Expected: After CompactSet with all images removed, block index is empty and
        /// no shard files remain on disk.
        /// 
        /// **Validates: Requirements 2.6**
        /// </summary>
        [Fact]
        public void RemoveAllImages_CompactSet_BlockIndexEmptyAndNoShardFiles()
        {
            const string setName = "ThreeImageSet";
            const int blockSize = 65536;
            const long shardSize = 50L * 1024 * 1024;

            Random rnd = new Random(123);

            // Step 1: Create set and add 3 images with distinct data
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                for (int i = 1; i <= 3; i++)
                {
                    byte[] data = new byte[blockSize]; // 1 block each
                    rnd.NextBytes(data);

                    using (IImageWriter writer = store.AddImage(setName, $"Image{i}.bin"))
                    {
                        writer.WriteData(0, data, BlockType.File);
                        writer.FinalizeImage(data.Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Verify we have 3 images
                List<ImageRecord> images = store.ListImagesInSet(setName);
                Assert.Equal(3, images.Count);

                // Step 2: Delete all 3 images
                store.DeleteImage(new GlobalImageKey(setName, 1));
                store.DeleteImage(new GlobalImageKey(setName, 2));
                store.DeleteImage(new GlobalImageKey(setName, 3));

                // Step 3: Compact the set
                store.CompactSet(setName);
            }

            // Step 4: Verify block index is empty and no shard files remain
            string[] shardFiles = Directory.GetFiles(_tempDir, $"{setName}_*.nkds");
            _output.WriteLine($"Shard files remaining after compact: {shardFiles.Length}");
            foreach (string f in shardFiles)
                _output.WriteLine($"  {Path.GetFileName(f)}");

            Assert.Empty(shardFiles);

            // Verify block index is empty
            HashSet<BlockKey> remainingBlockKeys = GetAllBlockIndexKeys(setName);
            _output.WriteLine($"Block index entries remaining: {remainingBlockKeys.Count}");
            Assert.Empty(remainingBlockKeys);
        }

        /// <summary>
        /// Test Case 3: Add 3 images, remove one at a time with CompactSet after each removal,
        /// verify remaining images at each intermediate step.
        /// 
        /// Bug Condition: After each removal+compact, the block index should only contain
        /// entries for the remaining live images. Due to the `anyShardModified` early return,
        /// removed images' block entries may persist in the index, potentially causing
        /// stale offset lookups for remaining images.
        /// 
        /// Expected: At each intermediate step, all remaining images pass full verification.
        /// 
        /// **Validates: Requirements 2.7, 2.8**
        /// </summary>
        [Fact]
        public void IncrementalRemoval_CompactAfterEach_RemainingImagesVerify()
        {
            const string setName = "IncrementalSet";
            const int blockSize = 65536;
            const long shardSize = 50L * 1024 * 1024;

            Random rnd = new Random(999);

            // Prepare distinct data for 3 images
            byte[][] imageData = new byte[3][];
            for (int i = 0; i < 3; i++)
            {
                imageData[i] = new byte[blockSize * 2]; // 2 blocks each
                rnd.NextBytes(imageData[i]);
            }

            HashSet<BlockKey>[] imageBlockKeys = new HashSet<BlockKey>[3];

            // Step 1: Create set and add 3 images
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                for (int i = 0; i < 3; i++)
                {
                    using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.bin"))
                    {
                        writer.WriteData(0, imageData[i], BlockType.File);
                        writer.FinalizeImage(imageData[i].Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Collect block keys for each image
                for (int i = 0; i < 3; i++)
                {
                    imageBlockKeys[i] = GetImageBlockKeys(store, setName, i + 1);
                    _output.WriteLine($"Image {i + 1} has {imageBlockKeys[i].Count} blocks");
                }

                // Verify all 3 images are readable
                for (int i = 0; i < 3; i++)
                {
                    VerifyImageData(store, setName, i + 1, imageData[i]);
                }

                // Step 2: Remove image 1, compact, verify images 2 and 3
                _output.WriteLine("--- Removing image 1 ---");
                store.DeleteImage(new GlobalImageKey(setName, 1));
                store.CompactSet(setName);
            }

            // Reopen to verify after first removal
            using (DataStore store = new DataStore(_tempDir))
            {
                VerifyImageData(store, setName, 2, imageData[1]);
                VerifyImageData(store, setName, 3, imageData[2]);

                // Verify block index doesn't contain image 1's blocks
                HashSet<BlockKey> remainingKeys = GetAllBlockIndexKeys(setName);
                _output.WriteLine($"After removing image 1: block index has {remainingKeys.Count} entries");
                foreach (BlockKey key in imageBlockKeys[0])
                {
                    Assert.DoesNotContain(key, remainingKeys);
                }

                // Step 3: Remove image 2, compact, verify image 3
                _output.WriteLine("--- Removing image 2 ---");
                store.DeleteImage(new GlobalImageKey(setName, 2));
                store.CompactSet(setName);
            }

            // Reopen to verify after second removal
            using (DataStore store = new DataStore(_tempDir))
            {
                VerifyImageData(store, setName, 3, imageData[2]);

                // Verify block index doesn't contain image 1's or 2's blocks
                HashSet<BlockKey> remainingKeys = GetAllBlockIndexKeys(setName);
                _output.WriteLine($"After removing image 2: block index has {remainingKeys.Count} entries");
                foreach (BlockKey key in imageBlockKeys[0])
                {
                    Assert.DoesNotContain(key, remainingKeys);
                }
                foreach (BlockKey key in imageBlockKeys[1])
                {
                    Assert.DoesNotContain(key, remainingKeys);
                }

                // Only image 3's blocks should remain
                Assert.Equal(imageBlockKeys[2].Count, remainingKeys.Count);

                // Step 4: Remove image 3, compact, verify empty
                _output.WriteLine("--- Removing image 3 ---");
                store.DeleteImage(new GlobalImageKey(setName, 3));
                store.CompactSet(setName);
            }

            // Reopen to verify after final removal
            using (DataStore store = new DataStore(_tempDir))
            {
                HashSet<BlockKey> remainingKeys = GetAllBlockIndexKeys(setName);
                _output.WriteLine($"After removing all images: block index has {remainingKeys.Count} entries");
                Assert.Empty(remainingKeys);

                // No shard files should remain
                string[] shardFiles = Directory.GetFiles(_tempDir, $"{setName}_*.nkds");
                _output.WriteLine($"Shard files remaining: {shardFiles.Length}");
                Assert.Empty(shardFiles);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Gets all block keys referenced by a specific image.
        /// Iterates through ALL blocks in each offset record (not just FirstBlock).
        /// </summary>
        private HashSet<BlockKey> GetImageBlockKeys(DataStore store, string setName, long imageId)
        {
            HashSet<BlockKey> keys = new HashSet<BlockKey>();
            using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId)))
            {
                List<OffsetRecord> offsets = reader.GetOffsets().ToList();
                foreach (OffsetRecord offset in offsets)
                {
                    if (offset.HasBlocks && offset.Blocks != null)
                    {
                        foreach (BlockKey blockKey in offset.Blocks)
                        {
                            keys.Add(blockKey);
                        }
                    }
                }
            }
            return keys;
        }

        /// <summary>
        /// Gets all block keys currently in the block index by opening the index file directly.
        /// </summary>
        private HashSet<BlockKey> GetAllBlockIndexKeys(string setName)
        {
            HashSet<BlockKey> keys = new HashSet<BlockKey>();
            string indexPath = Path.Combine(_tempDir, $"{setName}.nkds");
            if (!File.Exists(indexPath))
                return keys;

            using BinaryIndexFile indexFile = BinaryIndexFile.Open(indexPath);
            InMemoryBlockIndex blockIndex = indexFile.LoadBlockIndex();
            BlockIndexEntry[] entries = blockIndex.GetEntries();
            foreach (BlockIndexEntry entry in entries)
            {
                keys.Add(entry.Key);
            }
            return keys;
        }

        /// <summary>
        /// Verifies that an image's data can be fully read back and matches the expected data.
        /// This is the "full verification" — block data integrity check.
        /// </summary>
        private void VerifyImageData(DataStore store, string setName, long imageId, byte[] expectedData)
        {
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            using Stream stream = reader.OpenStream(0);

            byte[] readData = new byte[expectedData.Length];
            int totalRead = 0;
            while (totalRead < readData.Length)
            {
                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            Assert.Equal(expectedData.Length, totalRead);
            Assert.Equal(expectedData, readData);
        }

        #endregion
    }
}