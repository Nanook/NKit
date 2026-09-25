using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Preservation property tests for CompactSet index embedding bugfix.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    ///
    /// Property 2: Preservation — Embedded-Mode Sets Continue to Re-Embed Correctly
    ///
    /// These tests are EXPECTED TO PASS on unfixed code. They confirm baseline behavior
    /// that must be preserved after the fix is applied:
    /// - Embedded-mode sets (shardSize == 0) compact correctly with valid EmbeddedFooter
    /// - CompactSet on sets with no removed images completes without error (no-op behavior)
    /// - After embedded-mode compaction, the shard file contains a valid embedded index and footer
    /// - Varying image counts and removal patterns all produce valid embedded files
    /// </summary>
    public class CompactIndexEmbeddingPreservationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public CompactIndexEmbeddingPreservationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactEmbedPreserve_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Checks if a file has a valid EmbeddedFooter (last 12 bytes: 8-byte index size + 4-byte magic).
        /// Returns the footer if valid, null otherwise.
        /// </summary>
        private static EmbeddedFooter? ReadEmbeddedFooter(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < EmbeddedFooter.FooterSize)
                return null;

            byte[] footerBuffer = new byte[EmbeddedFooter.FooterSize];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
            int bytesRead = stream.Read(footerBuffer, 0, EmbeddedFooter.FooterSize);
            if (bytesRead < EmbeddedFooter.FooterSize)
                return null;

            return EmbeddedFooter.Deserialize(footerBuffer);
        }

        /// <summary>
        /// Checks if a file has a valid EmbeddedFooter magic at the end.
        /// </summary>
        private static bool HasEmbeddedFooter(string filePath) => ReadEmbeddedFooter(filePath) != null;

        /// <summary>
        /// **Validates: Requirements 3.1, 3.2**
        ///
        /// Property 2a - Embedded-Mode Compaction Preserves Embedded Layout:
        /// For all embedded-mode sets (shardSize == 0) with images added and some removed,
        /// CompactSet extracts the index, compacts it, and re-embeds it into the shard file
        /// preserving the embedded layout with a valid EmbeddedFooter.
        ///
        /// This test creates embedded-mode sets with varying image counts, removes some
        /// images, calls CompactSet, and verifies:
        /// - The embedded file still exists
        /// - The file has a valid EmbeddedFooter
        /// - The footer's IndexSize is positive and reasonable
        /// - No separate shard files exist (embedded mode preserved)
        /// - Remaining images are still readable
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_EmbeddedMode_WithRemovals_PreservesEmbeddedLayout(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbedPreserve";
                const int blockSize = 65536;
                Random rnd = new Random(seed);

                // Vary image count: 3 to 6 images
                int imageCount = 3 + (seed % 4);
                byte[][] imageData = new byte[imageCount][];

                // Phase 1: Create embedded-mode set and add images
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        // Each image gets 1-2 blocks of random data
                        int blocks = 1 + (rnd.Next() % 2);
                        imageData[i] = new byte[blockSize * blocks];
                        rnd.NextBytes(imageData[i]);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData[i], BlockType.File);
                            writer.FinalizeImage(imageData[i].Length, (uint)((i + 1) * 1111), (ulong)((i + 1) * 2222));
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Phase 2: Remove some images (1 to imageCount/2)
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    int removeCount = 1 + (seed % Math.Max(1, imageCount / 2));
                    removeCount = Math.Min(removeCount, imageCount - 1); // Keep at least 1 image

                    for (int i = 0; i < removeCount; i++)
                    {
                        // Remove from the end to keep indexing simple
                        int removeIdx = imageCount - 1 - i;
                        store.DeleteImage(new GlobalImageKey(setName, images[removeIdx].Id));
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    // Phase 3: Call CompactSet
                    store.CompactSet(setName);
                }

                // Phase 4: Verify the embedded file layout
                string embeddedFilePath = Path.Combine(testDir, $"{setName}.nkds");

                // Assert 1: The embedded file still exists
                bool fileExists = File.Exists(embeddedFilePath);
                if (!fileExists)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Embedded file does not exist after CompactSet (seed={seed}, images={imageCount})");
                }

                // Assert 2: The file has a valid EmbeddedFooter
                EmbeddedFooter? footer = ReadEmbeddedFooter(embeddedFilePath);
                bool hasValidFooter = footer != null;
                if (!hasValidFooter)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Embedded file has no valid EmbeddedFooter after CompactSet (seed={seed}, images={imageCount})");
                }

                // Assert 3: The footer's IndexSize is positive and reasonable
                bool indexSizeValid = footer!.Value.IndexSize > 0 && footer.Value.IndexSize < new FileInfo(embeddedFilePath).Length;
                if (!indexSizeValid)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Footer IndexSize={footer.Value.IndexSize} is invalid (fileSize={new FileInfo(embeddedFilePath).Length}, seed={seed})");
                }

                // Assert 4: No separate shard files exist (embedded mode preserved)
                string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                bool noSeparateShards = shardFiles.Length == 0;
                if (!noSeparateShards)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Separate shard files found after embedded-mode CompactSet (seed={seed}, count={shardFiles.Length})");
                }

                // Assert 5: Remaining images are still readable
                bool allReadable = true;
                using (DataStore store = new DataStore(testDir))
                {
                    List<ImageRecord> remainingImages = store.ListImagesInSet(setName);
                    int removeCount2 = 1 + (seed % Math.Max(1, imageCount / 2));
                    removeCount2 = Math.Min(removeCount2, imageCount - 1);
                    int expectedRemaining = imageCount - removeCount2;

                    for (int i = 0; i < remainingImages.Count && i < expectedRemaining; i++)
                    {
                        try
                        {
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, remainingImages[i].Id));
                            using Stream stream = reader.OpenStream(0);
                            byte[] readData = new byte[imageData[i].Length];
                            int totalRead = 0;
                            while (totalRead < readData.Length)
                            {
                                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }
                            if (totalRead != imageData[i].Length || !readData.SequenceEqual(imageData[i]))
                            {
                                allReadable = false;
                                _output.WriteLine($"Image {remainingImages[i].Id} data mismatch after embedded compact (seed={seed})");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            allReadable = false;
                            _output.WriteLine($"Image {remainingImages[i].Id} read failed: {ex.Message} (seed={seed})");
                            break;
                        }
                    }
                }

                if (!allReadable)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Remaining images not readable after embedded-mode CompactSet (seed={seed})");
                }

                return true.ToProperty().Label(
                    $"Embedded-mode CompactSet preserved layout: valid footer, no separate shards, images readable (seed={seed}, images={imageCount})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.4**
        ///
        /// Property 2b - No-Op Preservation (Embedded Mode):
        /// For all embedded-mode sets with no removed images, CompactSet completes
        /// without error and the file remains a valid embedded file.
        ///
        /// This confirms that CompactSet on embedded-mode sets with no removals
        /// is a no-op or minimal-work operation that preserves the embedded layout.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_EmbeddedMode_NoRemovals_CompletesWithoutError(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbedNoRemove";
                const int blockSize = 65536;
                Random rnd = new Random(seed);

                // Vary image count: 1 to 4 images
                int imageCount = 1 + (seed % 4);
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        int blocks = 1 + (rnd.Next() % 2);
                        imageData[i] = new byte[blockSize * blocks];
                        rnd.NextBytes(imageData[i]);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData[i], BlockType.File);
                            writer.FinalizeImage(imageData[i].Length, (uint)((i + 1) * 1111), (ulong)((i + 1) * 2222));
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // No removals — call CompactSet directly
                    store.CompactSet(setName);

                    // Verify: file still has valid EmbeddedFooter
                    string embeddedFilePath = Path.Combine(testDir, $"{setName}.nkds");
                    bool hasValidFooter = HasEmbeddedFooter(embeddedFilePath);

                    // Verify: no separate shard files
                    string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                    bool noSeparateShards = shardFiles.Length == 0;

                    // Verify: all images still readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    bool allReadable = true;
                    for (int i = 0; i < images.Count; i++)
                    {
                        try
                        {
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, images[i].Id));
                            using Stream stream = reader.OpenStream(0);
                            byte[] readData = new byte[imageData[i].Length];
                            int totalRead = 0;
                            while (totalRead < readData.Length)
                            {
                                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }
                            if (totalRead != imageData[i].Length || !readData.SequenceEqual(imageData[i]))
                            {
                                allReadable = false;
                                _output.WriteLine($"Image {images[i].Id} data mismatch (no-removal embedded compact, seed={seed})");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            allReadable = false;
                            _output.WriteLine($"Image {images[i].Id} read failed: {ex.Message} (seed={seed})");
                            break;
                        }
                    }

                    bool passed = hasValidFooter && noSeparateShards && allReadable;

                    string label = passed
                        ? $"Embedded-mode no-removal CompactSet: valid footer, no shards, all readable (seed={seed}, images={imageCount})"
                        : $"FAILURE: hasValidFooter={hasValidFooter}, noSeparateShards={noSeparateShards}, allReadable={allReadable} (seed={seed})";

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
        /// Property 2c - No-Op Preservation (Separate Mode):
        /// For all separate-mode sets (shardSize > 0) with no removed images,
        /// CompactSet completes without error and the index file remains standalone.
        ///
        /// This confirms that CompactSet on separate-mode sets with no removals
        /// is a no-op or minimal-work operation that preserves the separate layout.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_SeparateMode_NoRemovals_CompletesWithoutError(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "SepNoRemove";
                const int blockSize = 65536;
                long shardSize = (long)((seed % 4) + 1) * 65536; // Vary shard size
                Random rnd = new Random(seed);

                // Vary image count: 1 to 4 images
                int imageCount = 1 + (seed % 4);
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        imageData[i] = new byte[blockSize];
                        rnd.NextBytes(imageData[i]);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData[i], BlockType.File);
                            writer.FinalizeImage(imageData[i].Length, (uint)((i + 1) * 1111), (ulong)((i + 1) * 2222));
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // No removals — call CompactSet directly
                    store.CompactSet(setName);

                    // Verify: index file exists as standalone
                    string indexFilePath = Path.Combine(testDir, $"{setName}.nkds");
                    bool indexExists = File.Exists(indexFilePath);

                    // Verify: index file does NOT have an EmbeddedFooter
                    bool indexHasNoFooter = !HasEmbeddedFooter(indexFilePath);

                    // Verify: header ShardSize is preserved
                    bool headerPreserved = false;
                    using (BinaryIndexFile idxFile = BinaryIndexFile.Open(indexFilePath))
                    {
                        headerPreserved = idxFile.Header.ShardSize > 0;
                    }

                    // Verify: all images still readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    bool allReadable = true;
                    for (int i = 0; i < images.Count; i++)
                    {
                        try
                        {
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, images[i].Id));
                            using Stream stream = reader.OpenStream(0);
                            byte[] readData = new byte[imageData[i].Length];
                            int totalRead = 0;
                            while (totalRead < readData.Length)
                            {
                                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }
                            if (totalRead != imageData[i].Length || !readData.SequenceEqual(imageData[i]))
                            {
                                allReadable = false;
                                _output.WriteLine($"Image {images[i].Id} data mismatch (no-removal separate compact, seed={seed})");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            allReadable = false;
                            _output.WriteLine($"Image {images[i].Id} read failed: {ex.Message} (seed={seed})");
                            break;
                        }
                    }

                    bool passed = indexExists && indexHasNoFooter && headerPreserved && allReadable;

                    string label = passed
                        ? $"Separate-mode no-removal CompactSet: index standalone, no footer, header preserved (seed={seed}, images={imageCount})"
                        : $"FAILURE: indexExists={indexExists}, indexHasNoFooter={indexHasNoFooter}, headerPreserved={headerPreserved}, allReadable={allReadable} (seed={seed})";

                    return passed.ToProperty().Label(label);
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.3**
        ///
        /// Property 2d - Embedded-Mode Compaction With Varying Removal Patterns:
        /// For all embedded-mode sets with varying image counts and removal patterns,
        /// CompactSet always produces a valid embedded file with a valid EmbeddedFooter.
        ///
        /// This generates random embedded-mode sets with different numbers of images
        /// and different removal patterns (remove first, last, middle, multiple) and
        /// verifies CompactSet always produces a valid embedded file.
        /// </summary>
        [Property(MaxTest = 15)]
        public Property CompactSet_EmbeddedMode_VaryingRemovalPatterns_AlwaysProducesValidEmbeddedFile(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbedVaryRemove";
                const int blockSize = 65536;
                Random rnd = new Random(seed);

                // Vary image count: 3 to 7 images
                int imageCount = 3 + (seed % 5);

                // Phase 1: Create embedded-mode set and add images
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        // Vary data size: 1-3 blocks
                        int blocks = 1 + (rnd.Next() % 3);
                        byte[] data = new byte[blockSize * blocks];
                        rnd.NextBytes(data);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, data, BlockType.File);
                            writer.FinalizeImage(data.Length, (uint)((i + 1) * 1111), (ulong)((i + 1) * 2222));
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Phase 2: Remove images using varying patterns
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    int removeCount = 1 + (seed % Math.Max(1, imageCount - 1));
                    removeCount = Math.Min(removeCount, imageCount - 1); // Keep at least 1

                    // Generate removal indices based on seed for variety
                    List<int> removeIndices = Enumerable.Range(0, imageCount)
                        .OrderBy(x => rnd.Next())
                        .Take(removeCount)
                        .ToList();

                    foreach (int idx in removeIndices)
                    {
                        store.DeleteImage(new GlobalImageKey(setName, images[idx].Id));
                    }
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    // Phase 3: Call CompactSet
                    store.CompactSet(setName);
                }

                // Phase 4: Verify the embedded file is valid
                string embeddedFilePath = Path.Combine(testDir, $"{setName}.nkds");

                // Assert 1: File exists
                if (!File.Exists(embeddedFilePath))
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Embedded file missing after CompactSet (seed={seed}, images={imageCount})");
                }

                // Assert 2: Valid EmbeddedFooter
                EmbeddedFooter? footer = ReadEmbeddedFooter(embeddedFilePath);
                if (footer == null)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: No valid EmbeddedFooter after CompactSet (seed={seed}, images={imageCount})");
                }

                // Assert 3: IndexSize is positive and less than file size
                long fileSize = new FileInfo(embeddedFilePath).Length;
                if (footer.Value.IndexSize <= 0 || footer.Value.IndexSize >= fileSize)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Invalid IndexSize={footer.Value.IndexSize} (fileSize={fileSize}, seed={seed})");
                }

                // Assert 4: No separate shard files
                string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                if (shardFiles.Length > 0)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Separate shard files found after embedded CompactSet (seed={seed}, count={shardFiles.Length})");
                }

                // Assert 5: Can reopen and read remaining images
                bool canReopen = false;
                using (DataStore store = new DataStore(testDir))
                {
                    List<ImageRecord> remainingImages = store.ListImagesInSet(setName);
                    canReopen = remainingImages.Count > 0;

                    // Verify at least one image is readable
                    if (canReopen)
                    {
                        try
                        {
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, remainingImages[0].Id));
                            using Stream stream = reader.OpenStream(0);
                            byte[] buf = new byte[blockSize];
                            int read = stream.Read(buf, 0, buf.Length);
                            canReopen = read > 0;
                        }
                        catch (Exception ex)
                        {
                            canReopen = false;
                            _output.WriteLine($"Read failed after embedded compact: {ex.Message} (seed={seed})");
                        }
                    }
                }

                if (!canReopen)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: Cannot read images after embedded CompactSet (seed={seed})");
                }

                return true.ToProperty().Label(
                    $"Embedded CompactSet with varying removals: valid footer, readable (seed={seed}, images={imageCount})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.2**
        ///
        /// Property 2e - Embedded-Mode Header ShardSize Remains Zero After Compaction:
        /// For all embedded-mode sets, after CompactSet the header's ShardSize field
        /// remains 0, confirming the set stays in embedded mode.
        ///
        /// This verifies that compactSetEmbedded correctly re-embeds the index
        /// and the resulting file's header still indicates embedded mode (ShardSize == 0).
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSet_EmbeddedMode_HeaderShardSizeRemainsZero(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbedHeaderCheck";
                const int blockSize = 65536;
                Random rnd = new Random(seed);

                int imageCount = 2 + (seed % 4); // 2-5 images

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        byte[] data = new byte[blockSize];
                        rnd.NextBytes(data);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, data, BlockType.File);
                            writer.FinalizeImage(data.Length, (uint)((i + 1) * 1111), (ulong)((i + 1) * 2222));
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Remove one image so CompactSet has work to do
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    store.DeleteImage(new GlobalImageKey(setName, images[imageCount - 1].Id));
                    TestDataStoreHelper.WaitForSetIdle(store, setName);

                    // Compact
                    store.CompactSet(setName);
                }

                // Verify header ShardSize == 0 after compaction
                string embeddedFilePath = Path.Combine(testDir, $"{setName}.nkds");

                // Read the footer to find the index boundary
                EmbeddedFooter? footer = ReadEmbeddedFooter(embeddedFilePath);
                if (footer == null)
                {
                    return false.ToProperty().Label(
                        $"FAILURE: No valid footer after embedded CompactSet (seed={seed})");
                }

                // Open the index at the correct offset and check ShardSize
                long fileSize = new FileInfo(embeddedFilePath).Length;
                long indexSize = footer.Value.IndexSize;
                long shardBoundary = fileSize - EmbeddedFooter.FooterSize - indexSize;

                using BinaryIndexFile idxFile = BinaryIndexFile.Open(embeddedFilePath, baseOffset: shardBoundary);
                bool shardSizeIsZero = idxFile.Header.ShardSize == 0;

                string label = shardSizeIsZero
                    ? $"Embedded-mode header ShardSize remains 0 after CompactSet (seed={seed}, images={imageCount})"
                    : $"FAILURE: Header ShardSize={idxFile.Header.ShardSize} (expected 0) after embedded CompactSet (seed={seed})";

                return shardSizeIsZero.ToProperty().Label(label);
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }
    }
}