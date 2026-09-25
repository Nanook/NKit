using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Preservation property tests for shard compaction corruption bugfix.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.6, 3.7**
    ///
    /// Property 2: Preservation — Unmodified Sets and Shared Blocks Remain Unchanged
    ///
    /// These tests are EXPECTED TO PASS on unfixed code. They confirm baseline behavior
    /// that must be preserved after the fix is applied:
    /// - Compacting a set with no removed images produces no changes
    /// - Shared blocks between live images are preserved after compaction
    /// - Shard files where all blocks are still referenced are not rewritten
    /// - Reading blocks from a set that has never been compacted returns correct data
    /// - Adding images without removing any works correctly in both modes
    /// </summary>
    public class CompactShardPreservationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public CompactShardPreservationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactPreserve_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Helper: Creates a set with N images in separate mode (shardSize > 0).
        /// Each image has unique random data to ensure distinct blocks.
        /// Returns the original data for each image for later verification.
        /// </summary>
        private byte[][] CreateSetWithImages(DataStore store, string setName, int imageCount, int blockSize = 65536, long shardSize = 50L * 1024 * 1024 * 1024)
        {
            store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

            byte[][] imageData = new byte[imageCount][];
            Random rnd = new Random(42 + imageCount);

            for (int i = 0; i < imageCount; i++)
            {
                // Each image gets 1-2 blocks of random data
                int dataSize = blockSize * (1 + (i % 2));
                imageData[i] = new byte[dataSize];
                rnd.NextBytes(imageData[i]);

                using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                {
                    writer.WriteData(0, imageData[i], BlockType.File);
                    writer.FinalizeImage(imageData[i].Length, 0, 0);
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);
            }

            return imageData;
        }

        /// <summary>
        /// Helper: Verifies that an image can be read back and its data matches the original.
        /// Returns true if verification passes, false otherwise.
        /// </summary>
        private bool VerifyImage(DataStore store, string setName, long imageId, byte[] expectedData)
        {
            try
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

                if (totalRead != expectedData.Length)
                    return false;

                return readData.SequenceEqual(expectedData);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"VerifyImage failed for image {imageId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Helper: Gets the list of shard files for a set.
        /// </summary>
        private string[] GetShardFiles(string testDir, string setName) => Directory.GetFiles(testDir, $"{setName}_*.nkds");

        /// <summary>
        /// **Validates: Requirements 3.1, 3.4**
        ///
        /// Property 2a - No-Removal Preservation: For all sets with no removed images,
        /// CompactSet produces no shard file rewrites, no block index entry removals,
        /// and all images remain fully verifiable.
        ///
        /// Observation: Compacting a set with no removed images produces no changes
        /// (shard files byte-for-byte identical, block index unchanged, all images verifiable).
        ///
        /// Strategy: Create set, snapshot shard files (by closing the store), compact (by
        /// reopening), then compare shard files after compaction.
        /// </summary>
        [Property(MaxTest = 20)]
        public Property CompactSet_NoRemovedImages_ProducesNoChanges(PositiveInt imageCountWrapper)
        {
            int imageCount = 1 + (imageCountWrapper.Get % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                byte[][] imageData;

                // Phase 1: Create set with images, then close store to release file handles
                using (DataStore store = new DataStore(testDir))
                {
                    const string setName = "PreserveTest";
                    imageData = CreateSetWithImages(store, setName, imageCount);
                }

                // Phase 2: Snapshot shard files (store is closed, no file locks)
                const string setNameConst = "PreserveTest";
                string[] shardFilesBefore = GetShardFiles(testDir, setNameConst);
                Dictionary<string, byte[]> snapshotBefore = new Dictionary<string, byte[]>();
                foreach (string file in shardFilesBefore)
                {
                    snapshotBefore[Path.GetFileName(file)] = File.ReadAllBytes(file);
                }

                // Phase 3: Reopen store, compact, verify images, then close
                bool allVerifiable = true;
                using (DataStore store = new DataStore(testDir))
                {
                    store.CompactSet(setNameConst);

                    // Verify all images remain fully verifiable
                    List<ImageRecord> images = store.ListImagesInSet(setNameConst);
                    for (int i = 0; i < images.Count; i++)
                    {
                        if (!VerifyImage(store, setNameConst, images[i].Id, imageData[i]))
                        {
                            allVerifiable = false;
                            _output.WriteLine($"Image {images[i].Id} ({images[i].Name}) failed verification after compact with no removals");
                            break;
                        }
                    }
                }

                // Phase 4: Snapshot shard files after compaction (store closed)
                string[] shardFilesAfter = GetShardFiles(testDir, setNameConst);
                Dictionary<string, byte[]> snapshotAfter = new Dictionary<string, byte[]>();
                foreach (string file in shardFilesAfter)
                {
                    snapshotAfter[Path.GetFileName(file)] = File.ReadAllBytes(file);
                }

                // Verify: shard files are byte-for-byte identical
                bool shardsUnchanged = snapshotBefore.Count == snapshotAfter.Count;
                if (shardsUnchanged)
                {
                    foreach (KeyValuePair<string, byte[]> kvp in snapshotBefore)
                    {
                        if (!snapshotAfter.TryGetValue(kvp.Key, out byte[] afterContent))
                        {
                            shardsUnchanged = false;
                            break;
                        }
                        if (!kvp.Value.SequenceEqual(afterContent))
                        {
                            shardsUnchanged = false;
                            break;
                        }
                    }
                }

                bool passed = shardsUnchanged && allVerifiable;

                string label = passed
                    ? $"Set with {imageCount} images: compact with no removals produced no changes (preservation confirmed)"
                    : $"FAILURE: shardsUnchanged={shardsUnchanged}, allVerifiable={allVerifiable}, imageCount={imageCount}";

                return passed.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.2**
        ///
        /// Property 2b - Shared Block Preservation: For all sets where multiple live images
        /// share blocks (via deduplication), those shared blocks remain accessible to all
        /// live images. No compaction occurs (no images removed), so shared blocks must
        /// remain intact.
        ///
        /// Observation: When two images share blocks (via deduplication) and no images are
        /// removed, compacting the set preserves shared blocks and both images remain verifiable.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_SharedBlocksBetweenLiveImages_PreservedAfterCompact(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "SharedBlockTest";
                const int blockSize = 65536;
                byte[] image1Data;
                byte[] image2Data;

                // Create a set with two images that share a block (via deduplication)
                // Both images have the same first block (shared) and different second blocks
                Random rnd = new Random(seed);
                byte[] sharedBlock = new byte[blockSize];
                rnd.NextBytes(sharedBlock);

                byte[] uniqueBlock1 = new byte[blockSize];
                rnd.NextBytes(uniqueBlock1);

                byte[] uniqueBlock2 = new byte[blockSize];
                rnd.NextBytes(uniqueBlock2);

                // Image 1: sharedBlock + uniqueBlock1
                image1Data = new byte[blockSize * 2];
                Array.Copy(sharedBlock, 0, image1Data, 0, blockSize);
                Array.Copy(uniqueBlock1, 0, image1Data, blockSize, blockSize);

                // Image 2: sharedBlock + uniqueBlock2 (shares first block with image 1)
                image2Data = new byte[blockSize * 2];
                Array.Copy(sharedBlock, 0, image2Data, 0, blockSize);
                Array.Copy(uniqueBlock2, 0, image2Data, blockSize, blockSize);

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: blockSize);

                    using (IImageWriter writer = store.AddImage(setName, "Image1.iso"))
                    {
                        writer.WriteData(0, image1Data, BlockType.File);
                        writer.FinalizeImage(image1Data.Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    using (IImageWriter writer = store.AddImage(setName, "Image2.iso"))
                    {
                        writer.WriteData(0, image2Data, BlockType.File);
                        writer.FinalizeImage(image2Data.Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    // No images removed - compact the set
                    store.CompactSet(setName);

                    // Verify both images remain fully verifiable (shared blocks preserved)
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    bool image1Verifiable = VerifyImage(store, setName, images[0].Id, image1Data);
                    bool image2Verifiable = VerifyImage(store, setName, images[1].Id, image2Data);

                    bool passed = image1Verifiable && image2Verifiable;

                    string label = passed
                        ? $"Shared blocks preserved: both images verifiable after compact with no removals (seed={seed})"
                        : $"FAILURE: image1={image1Verifiable}, image2={image2Verifiable} after compact with shared blocks (seed={seed})";

                    return passed.ToProperty().Label(label);
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.4**
        ///
        /// Property 2c - No Unnecessary Shard Rewrite: For all shard files where all blocks
        /// are still referenced (no images removed), the shard file is not rewritten
        /// (no unnecessary copy-on-write).
        ///
        /// Observation: When no images are removed, compacting a set does not rewrite
        /// any shard files.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_AllBlocksReferenced_ShardNotRewritten(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "NoRewriteTest";
                const int blockSize = 65536;

                Random rnd = new Random(seed);
                int imageCount = 2 + (seed % 3); // 2-4 images
                byte[][] imageData = new byte[imageCount][];

                // Phase 1: Create set with images, then close store
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        int dataSize = blockSize * (1 + (i % 2));
                        imageData[i] = new byte[dataSize];
                        rnd.NextBytes(imageData[i]);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData[i], BlockType.File);
                            writer.FinalizeImage(imageData[i].Length, 0, 0);
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Snapshot shard files (store closed)
                string[] shardFilesBefore = GetShardFiles(testDir, setName);
                Dictionary<string, byte[]> snapshotBefore = new Dictionary<string, byte[]>();
                foreach (string file in shardFilesBefore)
                {
                    snapshotBefore[Path.GetFileName(file)] = File.ReadAllBytes(file);
                }

                // Phase 3: Reopen, compact (no removals), close
                using (DataStore store = new DataStore(testDir))
                {
                    store.CompactSet(setName);
                }

                // Phase 4: Snapshot shard files after compaction (store closed)
                string[] shardFilesAfter = GetShardFiles(testDir, setName);
                Dictionary<string, byte[]> snapshotAfter = new Dictionary<string, byte[]>();
                foreach (string file in shardFilesAfter)
                {
                    snapshotAfter[Path.GetFileName(file)] = File.ReadAllBytes(file);
                }

                // Verify: shard files are unchanged (no unnecessary rewrite)
                bool shardsUnchanged = snapshotBefore.Count == snapshotAfter.Count;
                if (shardsUnchanged)
                {
                    foreach (KeyValuePair<string, byte[]> kvp in snapshotBefore)
                    {
                        if (!snapshotAfter.TryGetValue(kvp.Key, out byte[] afterContent))
                        {
                            shardsUnchanged = false;
                            break;
                        }
                        if (!kvp.Value.SequenceEqual(afterContent))
                        {
                            shardsUnchanged = false;
                            break;
                        }
                    }
                }

                string label = shardsUnchanged
                    ? $"All blocks referenced: shard files not rewritten (seed={seed}, images={imageCount})"
                    : $"FAILURE: shard files were rewritten despite all blocks being referenced (seed={seed})";

                return shardsUnchanged.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.6**
        ///
        /// Property 2d - Read-After-Write Preservation: Reading blocks from a set that has
        /// never been compacted returns correct data at original offsets.
        ///
        /// Observation: Reading blocks from a set that has never been compacted returns
        /// correct data at original offsets.
        /// </summary>
        [Property(MaxTest = 20)]
        public Property ReadBlocks_NeverCompacted_ReturnsCorrectData(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                using DataStore store = new DataStore(testDir);
                const string setName = "ReadTest";
                const int blockSize = 65536;

                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: blockSize);

                Random rnd = new Random(seed);
                int imageCount = 1 + (seed % 4); // 1-4 images
                byte[][] imageData = new byte[imageCount][];

                for (int i = 0; i < imageCount; i++)
                {
                    int blocks = 1 + (rnd.Next() % 3); // 1-3 blocks per image
                    imageData[i] = new byte[blockSize * blocks];
                    rnd.NextBytes(imageData[i]);

                    using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                    {
                        writer.WriteData(0, imageData[i], BlockType.File);
                        writer.FinalizeImage(imageData[i].Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Verify all images without compacting
                List<ImageRecord> images = store.ListImagesInSet(setName);
                bool allVerifiable = true;

                for (int i = 0; i < images.Count; i++)
                {
                    if (!VerifyImage(store, setName, images[i].Id, imageData[i]))
                    {
                        allVerifiable = false;
                        _output.WriteLine($"Image {images[i].Id} failed read-back verification (never compacted, seed={seed})");
                        break;
                    }
                }

                string label = allVerifiable
                    ? $"All {imageCount} images readable at original offsets without compaction (seed={seed})"
                    : $"FAILURE: read-back failed for non-compacted set (seed={seed})";

                return allVerifiable.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.7**
        ///
        /// Property 2e - Add-Only Preservation: Adding images without removing any continues
        /// to work correctly in separate mode (shardSize > 0).
        ///
        /// Observation: Adding images without removing any continues to work correctly.
        /// </summary>
        [Property(MaxTest = 20)]
        public Property AddImages_NoRemovals_WorksCorrectly_SeparateMode(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                using DataStore store = new DataStore(testDir);
                const string setName = "AddOnlyTest";
                const int blockSize = 65536;

                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: blockSize);

                Random rnd = new Random(seed);
                int imageCount = 2 + (seed % 4); // 2-5 images
                byte[][] imageData = new byte[imageCount][];

                for (int i = 0; i < imageCount; i++)
                {
                    int blocks = 1 + (rnd.Next() % 2); // 1-2 blocks per image
                    imageData[i] = new byte[blockSize * blocks];
                    rnd.NextBytes(imageData[i]);

                    using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                    {
                        writer.WriteData(0, imageData[i], BlockType.File);
                        writer.FinalizeImage(imageData[i].Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    // Verify all images added so far are still readable
                    List<ImageRecord> currentImages = store.ListImagesInSet(setName);
                    for (int j = 0; j <= i; j++)
                    {
                        if (!VerifyImage(store, setName, currentImages[j].Id, imageData[j]))
                        {
                            string failLabel = $"FAILURE: Image {j + 1} not verifiable after adding image {i + 1} (seed={seed})";
                            return false.ToProperty().Label(failLabel);
                        }
                    }
                }

                string label = $"All {imageCount} images added and verified incrementally in separate mode (seed={seed})";
                return true.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.3, 3.7**
        ///
        /// Property 2f - Add-Only Preservation Embedded Mode: Adding images without removing
        /// any continues to work correctly in embedded mode (shardSize = 0).
        ///
        /// Observation: Adding images without removing any continues to work correctly
        /// in embedded mode.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property AddImages_NoRemovals_WorksCorrectly_EmbeddedMode(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                using DataStore store = new DataStore(testDir);
                const string setName = "EmbeddedAddTest";
                const int blockSize = 65536;

                store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

                Random rnd = new Random(seed);
                int imageCount = 2 + (seed % 3); // 2-4 images
                byte[][] imageData = new byte[imageCount][];

                for (int i = 0; i < imageCount; i++)
                {
                    int blocks = 1 + (rnd.Next() % 2); // 1-2 blocks per image
                    imageData[i] = new byte[blockSize * blocks];
                    rnd.NextBytes(imageData[i]);

                    using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                    {
                        writer.WriteData(0, imageData[i], BlockType.File);
                        writer.FinalizeImage(imageData[i].Length, 0, 0);
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Verify all images are readable
                List<ImageRecord> images = store.ListImagesInSet(setName);
                bool allVerifiable = true;

                for (int i = 0; i < images.Count; i++)
                {
                    if (!VerifyImage(store, setName, images[i].Id, imageData[i]))
                    {
                        allVerifiable = false;
                        _output.WriteLine($"Image {images[i].Id} failed verification in embedded mode (seed={seed})");
                        break;
                    }
                }

                // Also verify no separate shard files exist (embedded mode)
                string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                bool noShardFiles = shardFiles.Length == 0;

                bool passed = allVerifiable && noShardFiles;

                string label = passed
                    ? $"All {imageCount} images added and verified in embedded mode (seed={seed})"
                    : $"FAILURE: allVerifiable={allVerifiable}, noShardFiles={noShardFiles} (seed={seed})";

                return passed.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }
    }
}