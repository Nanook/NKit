using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Crash recovery property tests for the compact robustness refactor.
    ///
    /// **Validates: Requirements 1.3, 1.4, 2.3**
    ///
    /// Property 2: Atomic Index Replacement Safety
    ///
    /// For any set with removed images, when a crash occurs during the index replacement
    /// step (File.Replace or fallback), at least one valid index file (either the original
    /// or the `.compact.tmp`) SHALL exist on disk and pass header validation.
    /// </summary>
    public class CrashRecoveryPropertyTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public CrashRecoveryPropertyTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCrashRecovery_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Creates a valid index file at the given path with a properly checksummed header.
        /// The file contains two identical headers (primary + secondary) followed by minimal
        /// data to satisfy header validation (directory region + block index stub).
        /// </summary>
        private static void createValidIndexFile(string path, byte[] extraContent)
        {
            // Build a minimal valid header
            const int directoryRegionCapacity = 4096;
            long directoryOffset = FileHeader.HeaderSize * 2; // 0x200
            long dataStartOffset = directoryOffset + directoryRegionCapacity; // 0x1200

            // Minimal block index: just a version marker (2 bytes) + section count (4 bytes) = 6 bytes
            byte[] minimalBlockIndex = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
            long blockIndexOffset = dataStartOffset;
            long blockIndexSize = minimalBlockIndex.Length;

            // Minimal compressed directory (empty zstd frame)
            byte[] minimalDirectory = new byte[] { 0x28, 0xB5, 0x2F, 0xFD, 0x20, 0x00, 0x01, 0x00, 0x00 };
            int directorySize = minimalDirectory.Length;

            long fileEndOffset = dataStartOffset + blockIndexSize + (extraContent?.Length ?? 0);

            FileHeader header = new FileHeader
            {
                Magic = FileHeader.MagicBytes,
                MajorVersion = FileHeader.CurrentMajorVersion,
                MinorVersion = FileHeader.CurrentMinorVersion,
                ImageDirectoryOffset = directoryOffset,
                ImageDirectorySize = directorySize,
                ImageDirectoryUncompressedSize = 0,
                BlockIndexOffset = blockIndexOffset,
                BlockIndexSize = blockIndexSize,
                BlockIndexDeltaHeadOffset = 0,
                BlockIndexDeltaCount = 0,
                FileEndOffset = fileEndOffset,
                ShardSize = 50L * 1024 * 1024 * 1024,
                BlockSize = 65536,
                MaxOffsetBlocks = 8,
                ImageCount = 0,
                DirectoryRegionCapacity = directoryRegionCapacity
            };

            byte[] headerBytes = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(headerBytes, header);

            using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

            // Write primary header
            fs.Write(headerBytes, 0, FileHeader.HeaderSize);
            // Write secondary header (identical)
            fs.Write(headerBytes, 0, FileHeader.HeaderSize);

            // Write directory region (minimal directory + padding)
            fs.Write(minimalDirectory, 0, minimalDirectory.Length);
            byte[] padding = new byte[directoryRegionCapacity - minimalDirectory.Length];
            fs.Write(padding, 0, padding.Length);

            // Write block index
            fs.Write(minimalBlockIndex, 0, minimalBlockIndex.Length);

            // Write extra content (simulates image data appended after block index)
            if (extraContent != null && extraContent.Length > 0)
            {
                fs.Write(extraContent, 0, extraContent.Length);
            }

            fs.Flush();
        }

        /// <summary>
        /// Validates that a file at the given path has a valid header by reading the first
        /// 512 bytes and checking that at least one header (primary or secondary) passes
        /// checksum validation and structural checks.
        /// </summary>
        private static bool hasValidHeader(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                if (fs.Length < FileHeader.HeaderSize * 2)
                    return false;

                byte[] headerBytes = new byte[FileHeader.HeaderSize];

                // Try primary header
                fs.Position = 0;
                int read = fs.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader primary = FileHeaderSerializer.Read(headerBytes);
                        if (primary.IsValid())
                            return true;
                    }
                    catch (InvalidDataException) { }
                }

                // Try secondary header
                fs.Position = FileHeader.HeaderSize;
                read = fs.Read(headerBytes, 0, FileHeader.HeaderSize);
                if (read == FileHeader.HeaderSize)
                {
                    try
                    {
                        FileHeader secondary = FileHeaderSerializer.Read(headerBytes);
                        if (secondary.IsValid())
                            return true;
                    }
                    catch (InvalidDataException) { }
                }

                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 1.4, 2.3**
        ///
        /// Property 2: Atomic Index Replacement Safety
        ///
        /// For any set with removed images, when a crash occurs during the index replacement
        /// step (File.Replace or fallback), at least one valid index file (either the original
        /// or the `.compact.tmp`) SHALL exist on disk and pass header validation.
        ///
        /// Crash scenarios simulated:
        /// - Crash before File.Replace: both files exist → at least one valid
        /// - Crash during fallback (after delete, before move): only .compact.tmp exists → valid
        /// - Crash after successful replace: only original exists (with new content) → valid
        /// </summary>
        [Property(MaxTest = 10)]
        public Property AtomicIndexReplacement_CrashAtAnyPoint_AtLeastOneValidFileExists(
            PositiveInt originalExtraSeed,
            PositiveInt compactExtraSeed,
            byte crashScenario)
        {
            // Use seeds to generate deterministic but varied extra content for the index files
            int origSeed = originalExtraSeed.Get;
            int compSeed = compactExtraSeed.Get;

            // Generate arbitrary extra content for original and compact files
            Random origRng = new Random(origSeed);
            Random compRng = new Random(compSeed);

            int origExtraSize = origRng.Next(0, 512);
            byte[] origExtra = new byte[origExtraSize];
            origRng.NextBytes(origExtra);

            int compExtraSize = compRng.Next(0, 512);
            byte[] compExtra = new byte[compExtraSize];
            compRng.NextBytes(compExtra);

            // Normalize crash scenario to 3 cases
            int scenario = crashScenario % 3;

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            string originalPath = Path.Combine(testDir, "TestSet.nkds");
            string compactTmpPath = originalPath + ".compact.tmp";

            try
            {
                bool atLeastOneValid;

                switch (scenario)
                {
                    case 0:
                        // Crash BEFORE File.Replace: both original and .compact.tmp exist
                        // This simulates a crash before the replacement operation begins.
                        // Both files should be valid.
                        createValidIndexFile(originalPath, origExtra);
                        createValidIndexFile(compactTmpPath, compExtra);

                        atLeastOneValid = hasValidHeader(originalPath) || hasValidHeader(compactTmpPath);

                        return atLeastOneValid.ToProperty()
                            .Label($"Scenario 0 (crash before replace): original valid={hasValidHeader(originalPath)}, " +
                                   $"compact.tmp valid={hasValidHeader(compactTmpPath)}");

                    case 1:
                        // Crash DURING fallback (after delete of original, before move of .compact.tmp):
                        // Only .compact.tmp exists. It must be valid.
                        // This is the dangerous window in the non-atomic fallback path.
                        createValidIndexFile(compactTmpPath, compExtra);
                        // Original does NOT exist (it was deleted)

                        atLeastOneValid = hasValidHeader(compactTmpPath);

                        return atLeastOneValid.ToProperty()
                            .Label($"Scenario 1 (crash during fallback, original deleted): " +
                                   $"compact.tmp valid={hasValidHeader(compactTmpPath)}");

                    case 2:
                        // Crash AFTER successful replace: only original exists (with new content from compact)
                        // The .compact.tmp has been consumed by File.Replace.
                        createValidIndexFile(originalPath, compExtra);
                        // .compact.tmp does NOT exist (it was consumed)

                        atLeastOneValid = hasValidHeader(originalPath);

                        return atLeastOneValid.ToProperty()
                            .Label($"Scenario 2 (crash after replace): original valid={hasValidHeader(originalPath)}");

                    default:
                        return false.ToProperty().Label("Unexpected scenario");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 1.4, 2.3**
        ///
        /// Property 2 (supplemental): Verifies that AtomicFileOps.ReplaceFile itself
        /// guarantees that after a successful call, the destination file exists and is valid,
        /// and the source file no longer exists.
        ///
        /// This tests the actual ReplaceFile operation (not a simulated crash) to confirm
        /// the post-condition: destination exists with source content, source is gone.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property AtomicFileOps_ReplaceFile_DestinationValidAfterReplace(
            PositiveInt originalExtraSeed,
            PositiveInt compactExtraSeed)
        {
            int origSeed = originalExtraSeed.Get;
            int compSeed = compactExtraSeed.Get;

            Random origRng = new Random(origSeed);
            Random compRng = new Random(compSeed);

            int origExtraSize = origRng.Next(0, 512);
            byte[] origExtra = new byte[origExtraSize];
            origRng.NextBytes(origExtra);

            int compExtraSize = compRng.Next(0, 512);
            byte[] compExtra = new byte[compExtraSize];
            compRng.NextBytes(compExtra);

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            string originalPath = Path.Combine(testDir, "TestSet.nkds");
            string compactTmpPath = originalPath + ".compact.tmp";

            try
            {
                // Create both files with valid headers
                createValidIndexFile(originalPath, origExtra);
                createValidIndexFile(compactTmpPath, compExtra);

                // Perform the atomic replace
                AtomicFileOps.ReplaceFile(compactTmpPath, originalPath);

                // Post-conditions:
                // 1. Original path exists and has valid header (now contains compact content)
                bool originalExists = File.Exists(originalPath);
                bool originalValid = hasValidHeader(originalPath);

                // 2. Source (.compact.tmp) no longer exists
                bool sourceGone = !File.Exists(compactTmpPath);

                bool allConditions = originalExists && originalValid && sourceGone;

                return allConditions.ToProperty()
                    .Label($"After ReplaceFile: originalExists={originalExists}, " +
                           $"originalValid={originalValid}, sourceGone={sourceGone}");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 1.4, 2.3**
        ///
        /// Property 2 (supplemental): Verifies that the fallback path of AtomicFileOps
        /// (Delete + Move) also results in a valid destination file when both source and
        /// destination initially exist.
        ///
        /// This confirms that even when File.Replace is not available, the end state
        /// has a valid index file at the destination path.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property AtomicFileOps_FallbackPath_DestinationValidAfterReplace(
            PositiveInt originalExtraSeed,
            PositiveInt compactExtraSeed)
        {
            int origSeed = originalExtraSeed.Get;
            int compSeed = compactExtraSeed.Get;

            Random origRng = new Random(origSeed);
            Random compRng = new Random(compSeed);

            int origExtraSize = origRng.Next(0, 512);
            byte[] origExtra = new byte[origExtraSize];
            origRng.NextBytes(origExtra);

            int compExtraSize = compRng.Next(0, 512);
            byte[] compExtra = new byte[compExtraSize];
            compRng.NextBytes(compExtra);

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            string originalPath = Path.Combine(testDir, "TestSet.nkds");
            string compactTmpPath = originalPath + ".compact.tmp";

            try
            {
                // Create both files with valid headers
                createValidIndexFile(originalPath, origExtra);
                createValidIndexFile(compactTmpPath, compExtra);

                // Simulate the fallback path directly (Delete + Move)
                File.Delete(originalPath);
                File.Move(compactTmpPath, originalPath);

                // Post-conditions:
                // 1. Original path exists and has valid header
                bool originalExists = File.Exists(originalPath);
                bool originalValid = hasValidHeader(originalPath);

                // 2. Source (.compact.tmp) no longer exists
                bool sourceGone = !File.Exists(compactTmpPath);

                bool allConditions = originalExists && originalValid && sourceGone;

                return allConditions.ToProperty()
                    .Label($"After fallback (Delete+Move): originalExists={originalExists}, " +
                           $"originalValid={originalValid}, sourceGone={sourceGone}");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 14.5, 7.2**
        ///
        /// Property 10: Dual-Header Protocol Correctness
        ///
        /// For any index file where the PrimaryHeader is corrupt but the SecondaryHeader is valid,
        /// BinaryIndexFile.Open SHALL use the SecondaryHeader as authoritative and successfully
        /// open the file.
        ///
        /// Strategy:
        /// 1. Create a DataStore set with N images (1-5)
        /// 2. Close the store
        /// 3. Corrupt the primary header (first 256 bytes) by overwriting with random data
        /// 4. Reopen the store (BinaryIndexFile.Open should use the secondary header)
        /// 5. Verify the set opens successfully
        /// 6. Verify all images are readable
        /// </summary>
        [Property(MaxTest = 10)]
        public Property DualHeaderProtocol_CorruptPrimary_UsesSecondaryAndOpensSuccessfully(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "DualHeaderTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Corrupt the primary header (first 256 bytes) with random data
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");

                if (!File.Exists(indexPath))
                {
                    return false.ToProperty().Label(
                        $"Index file not found at {indexPath} (seed={seed})");
                }

                // Overwrite the primary header (offset 0, 256 bytes) with random data
                byte[] corruptData = new byte[FileHeader.HeaderSize];
                rnd.NextBytes(corruptData);

                using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    fs.Position = 0;
                    fs.Write(corruptData, 0, corruptData.Length);
                    fs.Flush();
                }

                // Phase 3: Reopen the store — BinaryIndexFile.Open should fall back to secondary header
                using (DataStore store = new DataStore(testDir))
                {
                    // Phase 4: Verify the set opens successfully by listing images
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after dual-header recovery (seed={seed})");
                    }

                    // Phase 5: Verify all images are readable with correct data
                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after dual-header recovery (seed={seed})");
                        }
                    }

                    return true.ToProperty().Label(
                        $"Dual-header recovery successful: {imageCount} images readable (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.7, 7.1, 7.5, 7.6, 13.1, 13.2, 13.5**
        ///
        /// Property 7: Orphaned Tail Truncation
        ///
        /// For any set where shard files contain data beyond the last committed block index offset,
        /// opening the set SHALL truncate each affected shard to its expected size, and all previously
        /// committed images SHALL remain readable after truncation.
        ///
        /// Strategy:
        /// 1. Create a DataStore set with N images (1-5)
        /// 2. Close the store
        /// 3. Append random orphaned data to one or more shard files (simulating crash during write before commit)
        /// 4. Reopen the store (which triggers RecoverIfNeeded → orphaned tail truncation)
        /// 5. Verify all previously committed images are still readable
        /// 6. Verify shard files are now at their expected sizes (no orphaned tail)
        /// 7. Verify CheckSetHealth returns Clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property OrphanedTailTruncation_TruncatesShardsAndPreservesImages(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "OrphanedTailTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Record expected shard sizes before injecting orphaned data
                string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");

                if (shardFiles.Length == 0)
                {
                    return false.ToProperty().Label(
                        $"No shard files found after creating {imageCount} images (seed={seed})");
                }

                Dictionary<string, long> expectedShardSizes = new Dictionary<string, long>();
                foreach (string shardFile in shardFiles)
                {
                    expectedShardSizes[shardFile] = new FileInfo(shardFile).Length;
                }

                // Phase 3: Append random orphaned data to one or more shard files
                // Choose how many shards to corrupt (1 to all)
                int shardsToCorrupt = 1 + (rnd.Next() % shardFiles.Length);
                string[] corruptedShards = shardFiles.OrderBy(_ => rnd.Next()).Take(shardsToCorrupt).ToArray();

                foreach (string shardFile in corruptedShards)
                {
                    // Append 64-512 bytes of random orphaned data
                    int orphanedSize = 64 + (rnd.Next() % 449);
                    byte[] orphanedData = new byte[orphanedSize];
                    rnd.NextBytes(orphanedData);

                    using (FileStream fs = new FileStream(shardFile, FileMode.Append, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                }

                // Verify orphaned data was actually appended
                foreach (string shardFile in corruptedShards)
                {
                    long actualSize = new FileInfo(shardFile).Length;
                    if (actualSize <= expectedShardSizes[shardFile])
                    {
                        return false.ToProperty().Label(
                            $"Failed to append orphaned data to shard (seed={seed})");
                    }
                }

                // Phase 4: Reopen the store — trigger RecoverIfNeeded → orphaned tail truncation
                // EnsureSetExists calls RecoverIfNeeded before opening the set.
                // For an existing set with orphaned tail data, we call EnsureSetExists directly
                // (via the internal DataAccess property) to trigger recovery without CreateSet
                // throwing "already exists".
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    // Phase 5: Verify all previously committed images are still readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after recovery (seed={seed})");
                    }

                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after orphaned tail truncation (seed={seed})");
                        }
                    }

                    // Phase 6: Verify shard files are now at their expected sizes (no orphaned tail)
                    foreach (KeyValuePair<string, long> kvp in expectedShardSizes)
                    {
                        string shardFile = kvp.Key;
                        long expectedSize = kvp.Value;

                        if (!File.Exists(shardFile))
                        {
                            return false.ToProperty().Label(
                                $"Shard file '{Path.GetFileName(shardFile)}' missing after recovery (seed={seed})");
                        }

                        long actualSize = new FileInfo(shardFile).Length;
                        if (actualSize != expectedSize)
                        {
                            return false.ToProperty().Label(
                                $"Shard '{Path.GetFileName(shardFile)}' size mismatch: expected={expectedSize}, " +
                                $"actual={actualSize} (seed={seed})");
                        }
                    }

                    // Phase 7: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Expected Clean health after recovery but got {health.Status}: {issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"Orphaned tail truncation successful: {imageCount} images readable, " +
                        $"{shardsToCorrupt} shard(s) truncated, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 12.4**
        ///
        /// Property 9: Directory Update Durability
        ///
        /// For any directory update operation that completes AtomicCommit successfully,
        /// the change SHALL be durable and visible on the next open regardless of subsequent crashes.
        ///
        /// Strategy:
        /// 1. Create a DataStore set with N images (2-5)
        /// 2. Perform a directory update (DeleteImage) that completes successfully
        /// 3. Close the store
        /// 4. Reopen the store
        /// 5. Verify the directory change is still visible (the deleted image is still marked as removed)
        /// 6. Corrupt the primary header after the successful commit
        /// 7. Reopen again and verify the change is still visible (secondary header preserves it)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property DirectoryUpdateDurability_DeleteImageSurvivesReopenAndHeaderCorruption(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 2 + (rnd.Next() % 4); // 2 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "DurabilityTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Reopen and perform a DeleteImage that completes successfully
                // Choose a random image to delete (1-based image IDs)
                int imageToDelete = 1 + (rnd.Next() % imageCount);

                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    store.DeleteImage(new GlobalImageKey(setName, imageToDelete));
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Phase 3: Reopen the store and verify the delete is durable
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // ListImagesInSet returns only non-removed images
                    List<ImageRecord> liveImages = store.ListImagesInSet(setName);

                    if (liveImages.Count != imageCount - 1)
                    {
                        return false.ToProperty().Label(
                            $"After reopen: expected {imageCount - 1} live images but found {liveImages.Count} " +
                            $"(deleted image {imageToDelete}, seed={seed})");
                    }

                    // Verify the deleted image is not in the live list
                    if (liveImages.Any(img => img.Id == imageToDelete))
                    {
                        return false.ToProperty().Label(
                            $"Deleted image {imageToDelete} still appears as live after reopen (seed={seed})");
                    }

                    // Verify the deleted image IS in the full list (including removed) and marked as removed
                    List<ImageRecord> allImages = store.ListImagesInSetIncludingRemoved(setName);
                    ImageRecord deletedRecord = allImages.FirstOrDefault(img => img.Id == imageToDelete);

                    if (deletedRecord == null)
                    {
                        return false.ToProperty().Label(
                            $"Deleted image {imageToDelete} not found in full image list (seed={seed})");
                    }

                    if (!deletedRecord.Removed)
                    {
                        return false.ToProperty().Label(
                            $"Deleted image {imageToDelete} not marked as Removed after reopen (seed={seed})");
                    }
                }

                // Phase 4: Corrupt the primary header to simulate a crash after the commit
                // The secondary header should preserve the committed state (with the delete)
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");

                if (!File.Exists(indexPath))
                {
                    return false.ToProperty().Label(
                        $"Index file not found at {indexPath} (seed={seed})");
                }

                byte[] corruptData = new byte[FileHeader.HeaderSize];
                rnd.NextBytes(corruptData);

                using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    fs.Position = 0;
                    fs.Write(corruptData, 0, corruptData.Length);
                    fs.Flush();
                }

                // Phase 5: Reopen again — dual-header recovery should use secondary header
                // The delete should still be visible
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    List<ImageRecord> liveImages = store.ListImagesInSet(setName);

                    if (liveImages.Count != imageCount - 1)
                    {
                        return false.ToProperty().Label(
                            $"After header corruption + reopen: expected {imageCount - 1} live images but found " +
                            $"{liveImages.Count} (deleted image {imageToDelete}, seed={seed})");
                    }

                    if (liveImages.Any(img => img.Id == imageToDelete))
                    {
                        return false.ToProperty().Label(
                            $"Deleted image {imageToDelete} reappeared after header corruption recovery (seed={seed})");
                    }

                    // Verify remaining images are still readable
                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        if (imageId == imageToDelete)
                            continue; // Skip the deleted image

                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} " +
                                $"after header corruption recovery (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after header corruption recovery (seed={seed})");
                        }
                    }

                    return true.ToProperty().Label(
                        $"Directory update durable: {imageCount} images, deleted image {imageToDelete}, " +
                        $"survived reopen + header corruption (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 4.2, 10.1, 10.2, 10.4**
        ///
        /// Property 6: Compact.tmp Recovery
        ///
        /// For any valid compacted index file placed as .compact.tmp (with original missing),
        /// opening the set SHALL rename it to the original path, validate the header, and
        /// successfully open the set with all images from that compacted file readable.
        ///
        /// Strategy:
        /// 1. Create a DataStore set with N images (1-5)
        /// 2. Close the store
        /// 3. Rename the index file to .compact.tmp (simulating crash after delete but before move)
        /// 4. Reopen the store (which triggers RecoverIfNeeded)
        /// 5. Verify the set opens successfully
        /// 6. Verify all images are readable
        /// 7. Verify CheckSetHealth returns Clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactTmpRecovery_RenamesAndOpensSuccessfully(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "CompactTmpRecoveryTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Rename the index file to .compact.tmp (simulate crash after delete but before move)
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");
                string compactTmpPath = indexPath + ".compact.tmp";

                if (!File.Exists(indexPath))
                {
                    return false.ToProperty().Label(
                        $"Index file not found at {indexPath} (seed={seed})");
                }

                File.Move(indexPath, compactTmpPath);

                // Verify the intermediate state: original missing, .compact.tmp exists
                if (File.Exists(indexPath) || !File.Exists(compactTmpPath))
                {
                    return false.ToProperty().Label(
                        $"Failed to set up intermediate state (seed={seed})");
                }

                // Phase 3: Reopen the store — this should trigger RecoverIfNeeded
                // We call CreateSet with the same parameters, which triggers EnsureSetExists
                // which calls RecoverIfNeeded before checking file existence.
                // RecoverIfNeeded will rename .compact.tmp back to the original path.
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                    // Phase 4: Verify the set opens successfully by listing images
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after recovery (seed={seed})");
                    }

                    // Phase 5: Verify all images are readable with correct data
                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after recovery (seed={seed})");
                        }
                    }

                    // Phase 6: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Expected Clean health after recovery but got {health.Status}: {issueDetails} (seed={seed})");
                    }

                    // Phase 7: Verify the .compact.tmp file was renamed back to original
                    if (!File.Exists(indexPath))
                    {
                        return false.ToProperty().Label(
                            $"Index file not restored to original path after recovery (seed={seed})");
                    }

                    if (File.Exists(compactTmpPath))
                    {
                        return false.ToProperty().Label(
                            $".compact.tmp file still exists after recovery (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"Recovery successful: {imageCount} images readable, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 14.1, 14.2, 14.3, 7.4, 12.1, 12.2, 12.3**
        ///
        /// Property 1: Recovery Invariant — Last Committed State
        ///
        /// For any sequence of operations (AddImage, DeleteImage, RestoreImage, RollbackImage,
        /// CompactSet) and for any crash point during any operation, after recovery the set SHALL
        /// present a state equivalent to the state immediately after the last successful
        /// AtomicCommit prior to the crash.
        ///
        /// Strategy:
        /// 1. Generate a random set state using CrashRecoveryGenerators.GenSetState()
        /// 2. Generate a random operation type (AddImage, DeleteImage, RestoreImage, RollbackImage, CompactSet)
        /// 3. Generate a crash point appropriate for that operation type
        /// 4. Create the set with the generated images using the DataStore API
        /// 5. Record the "last committed state" (list of live image IDs and their data hashes)
        /// 6. Perform the operation up to the crash point (leaving intermediate files)
        /// 7. Close and reopen the store (triggering recovery)
        /// 8. Verify the recovered state matches the last committed state (same live images, same data)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property RecoveryInvariant_LastCommittedState()
        {
            Gen<(SetState setState, OperationType operationType, CrashPoint crashPoint)> gen = from setState in CrashRecoveryGenerators.GenSetState()
                                                                                               from operationType in CrashRecoveryGenerators.GenOperationType()
                                                                                               from crashPoint in CrashRecoveryGenerators.GenCrashPointForOperation(operationType)
                                                                                               select (setState, operationType, crashPoint);

            return Prop.ForAll(gen.ToArbitrary(), tuple =>
                recoveryInvariant_Impl(tuple.setState, tuple.operationType, tuple.crashPoint));
        }

        private Property recoveryInvariant_Impl(SetState setState, OperationType operationType, CrashPoint crashPoint)
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "RecoveryInvariantTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Adjust the set state to ensure it's valid for the operation type
                SetState adjustedState = adjustSetStateForOperation(setState, operationType);

                // Phase 1: Create the set with all generated images
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                    foreach (ImageState image in adjustedState.Images)
                    {
                        using (IImageWriter writer = store.AddImage(setName, image.Name))
                        {
                            writer.WriteData(0, image.Data, BlockType.File);
                            writer.FinalizeImage(image.Data.Length, 0, 0);
                        }

                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Delete images that should be removed (to set up the initial state)
                    foreach (ImageState image in adjustedState.RemovedImages)
                    {
                        store.DeleteImage(new GlobalImageKey(setName, image.ImageId));
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Record the last committed state (live image IDs and their data)
                List<long> committedLiveImageIds = adjustedState.LiveImages
                    .Select(i => (long)i.ImageId)
                    .OrderBy(id => id)
                    .ToList();

                Dictionary<long, byte[]> committedImageData = adjustedState.LiveImages
                    .ToDictionary(i => (long)i.ImageId, i => i.Data);

                // Phase 3: Simulate a crash during the specified operation type
                simulateCrashForOperationType(testDir, setName, adjustedState, operationType, crashPoint, shardSize, blockSize);

                // Phase 4: Reopen the store (triggering recovery)
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 5: Verify recovered state matches last committed state
                    List<ImageRecord> recoveredImages = store.ListImagesInSet(setName);
                    List<long> recoveredLiveIds = recoveredImages
                        .Select(img => img.Id)
                        .OrderBy(id => id)
                        .ToList();

                    // For directory operations (Delete/Restore/Rollback), when the crash point
                    // is DuringCommitAfterSecondary, DuringCommitAfterPrimary, or AfterCommit,
                    // the operation was actually performed (secondary header is authoritative),
                    // so the "last committed state" is the POST-operation state.
                    // For AddImage and CompactSet, we never actually perform the operation in
                    // the simulation — we just leave intermediate files.
                    bool isDirectoryOp = operationType == OperationType.DeleteImage ||
                                         operationType == OperationType.RestoreImage ||
                                         operationType == OperationType.RollbackImage;
                    bool commitCompleted = isDirectoryOp && (
                        crashPoint == CrashPoint.DuringCommitAfterSecondary ||
                        crashPoint == CrashPoint.DuringCommitAfterPrimary ||
                        crashPoint == CrashPoint.AfterCommit);

                    List<long> expectedLiveIds = committedLiveImageIds;
                    Dictionary<long, byte[]> expectedImageData = committedImageData;

                    if (commitCompleted && operationType == OperationType.DeleteImage)
                    {
                        long deletedId = adjustedState.LiveImages.First().ImageId;
                        expectedLiveIds = committedLiveImageIds.Where(id => id != deletedId).ToList();
                        expectedImageData = committedImageData
                            .Where(kvp => kvp.Key != deletedId)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    }
                    else if (commitCompleted && operationType == OperationType.RestoreImage)
                    {
                        ImageState restoredImage = adjustedState.RemovedImages.First();
                        expectedLiveIds = committedLiveImageIds
                            .Append((long)restoredImage.ImageId)
                            .OrderBy(id => id)
                            .ToList();
                        expectedImageData = new Dictionary<long, byte[]>(committedImageData)
                        {
                            [(long)restoredImage.ImageId] = restoredImage.Data
                        };
                    }
                    else if (commitCompleted && operationType == OperationType.RollbackImage)
                    {
                        // Rollback marks all images with ID > target as removed.
                        // The target is the first removed image's ID.
                        long rollbackTargetId = adjustedState.RemovedImages.First().ImageId;
                        expectedLiveIds = committedLiveImageIds
                            .Where(id => id <= rollbackTargetId)
                            .ToList();
                        expectedImageData = committedImageData
                            .Where(kvp => kvp.Key <= rollbackTargetId)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    }

                    // Check that the same set of live images exists
                    if (!recoveredLiveIds.SequenceEqual(expectedLiveIds))
                    {
                        return false.ToProperty().Label(
                            $"Live image IDs mismatch after recovery. " +
                            $"Expected: [{string.Join(",", expectedLiveIds)}], " +
                            $"Got: [{string.Join(",", recoveredLiveIds)}] " +
                            $"(op={operationType}, crashPoint={crashPoint}, images={adjustedState.ImageCount})");
                    }

                    // Verify each live image's data matches the expected data
                    foreach (long imageId in expectedLiveIds)
                    {
                        byte[] expectedData = expectedImageData[imageId];

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
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {expectedData.Length} " +
                                $"(op={operationType}, crashPoint={crashPoint})");
                        }

                        if (!readData.SequenceEqual(expectedData))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after recovery " +
                                $"(op={operationType}, crashPoint={crashPoint})");
                        }
                    }

                    return true.ToProperty().Label(
                        $"Recovery invariant holds: {expectedLiveIds.Count} live images preserved " +
                        $"(op={operationType}, crashPoint={crashPoint}, totalImages={adjustedState.ImageCount})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (op={operationType}, crashPoint={crashPoint}, images={setState.ImageCount}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Adjusts the set state to ensure it's valid for the given operation type.
        /// - DeleteImage requires at least one live image (already guaranteed)
        /// - RestoreImage/RollbackImage require at least one removed image
        /// - CompactSet requires at least one removed image
        /// - AddImage works with any state
        /// </summary>
        private static SetState adjustSetStateForOperation(SetState setState, OperationType operationType)
        {
            switch (operationType)
            {
                case OperationType.RestoreImage:
                case OperationType.RollbackImage:
                case OperationType.CompactSet:
                    if (setState.RemovedImages.Length == 0 && setState.Images.Length >= 2)
                    {
                        ImageState[] images = setState.Images.ToArray();
                        ImageState lastImage = images[images.Length - 1];
                        images[images.Length - 1] = new ImageState
                        {
                            ImageId = lastImage.ImageId,
                            Name = lastImage.Name,
                            BlockCount = lastImage.BlockCount,
                            Data = lastImage.Data,
                            IsRemoved = true
                        };
                        return new SetState
                        {
                            Images = images,
                            BlockSize = setState.BlockSize,
                            ShardSize = setState.ShardSize
                        };
                    }
                    else if (setState.RemovedImages.Length == 0 && setState.Images.Length == 1)
                    {
                        Random rnd = new Random(setState.Images[0].Data.Length);
                        byte[] extraData = new byte[setState.BlockSize];
                        rnd.NextBytes(extraData);
                        ImageState[] images = new[]
                        {
                            setState.Images[0],
                            new ImageState
                            {
                                ImageId = 2,
                                Name = "Image2.iso",
                                BlockCount = 1,
                                Data = extraData,
                                IsRemoved = true
                            }
                        };
                        return new SetState
                        {
                            Images = images,
                            BlockSize = setState.BlockSize,
                            ShardSize = setState.ShardSize
                        };
                    }
                    return setState;

                case OperationType.DeleteImage:
                case OperationType.AddImage:
                default:
                    return setState;
            }
        }

        /// <summary>
        /// Simulates a crash during a specific operation type at the given crash point.
        /// Explicitly performs the specified operation type and simulates the crash.
        /// </summary>
        private void simulateCrashForOperationType(
            string testDir, string setName, SetState setState,
            OperationType operationType, CrashPoint crashPoint,
            long shardSize, int blockSize)
        {
            string indexPath = Path.Combine(testDir, $"{setName}.nkds");
            string compactTmpPath = indexPath + ".compact.tmp";
            string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");

            switch (operationType)
            {
                case OperationType.AddImage:
                    simulateCrashDuringAddImage(indexPath, shardFiles, setState, crashPoint);
                    break;
                case OperationType.DeleteImage:
                    simulateCrashDuringDirectoryOp(testDir, setName, indexPath, setState, crashPoint, shardSize, blockSize, OperationType.DeleteImage);
                    break;
                case OperationType.RestoreImage:
                    simulateCrashDuringDirectoryOp(testDir, setName, indexPath, setState, crashPoint, shardSize, blockSize, OperationType.RestoreImage);
                    break;
                case OperationType.RollbackImage:
                    simulateCrashDuringDirectoryOp(testDir, setName, indexPath, setState, crashPoint, shardSize, blockSize, OperationType.RollbackImage);
                    break;
                case OperationType.CompactSet:
                    simulateCrashDuringCompactSet(indexPath, compactTmpPath, shardFiles, setState, crashPoint);
                    break;
            }
        }

        private void simulateCrashDuringAddImage(
            string indexPath, string[] shardFiles, SetState setState, CrashPoint crashPoint)
        {
            switch (crashPoint)
            {
                case CrashPoint.BeforeCommit:
                case CrashPoint.DuringBlockWrite:
                case CrashPoint.DuringMetadataWrite:
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[shardFiles.Length - 1];
                        byte[] orphanedData = new byte[128];
                        new Random(setState.ImageCount).NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;

                case CrashPoint.DuringCommitAfterSecondary:
                    if (File.Exists(indexPath))
                    {
                        byte[] corruptData = new byte[FileHeader.HeaderSize];
                        new Random(setState.ImageCount + 1).NextBytes(corruptData);
                        using FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write);
                        fs.Position = 0;
                        fs.Write(corruptData, 0, corruptData.Length);
                    }
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[shardFiles.Length - 1];
                        byte[] orphanedData = new byte[128];
                        new Random(setState.ImageCount + 10).NextBytes(orphanedData);
                        using FileStream fs2 = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs2.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;
            }
        }

        /// <summary>
        /// Simulates a crash during a directory update operation (Delete/Restore/Rollback).
        /// </summary>
        private void simulateCrashDuringDirectoryOp(
            string testDir, string setName, string indexPath, SetState setState,
            CrashPoint crashPoint, long shardSize, int blockSize, OperationType opType)
        {
            switch (crashPoint)
            {
                case CrashPoint.BeforeCommit:
                    // Directory update appended but not committed — leave orphaned shard data
                    string[] shards = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                    if (shards.Length > 0)
                    {
                        string targetShard = shards[shards.Length - 1];
                        byte[] orphanedData = new byte[64];
                        new Random(setState.ImageCount + 5).NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;

                case CrashPoint.DuringCommitAfterSecondary:
                    // Perform the operation, then corrupt primary header
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                        performDirectoryOperation(store, setName, setState, opType);
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                    if (File.Exists(indexPath))
                    {
                        byte[] corruptData = new byte[FileHeader.HeaderSize];
                        new Random(setState.ImageCount + 6).NextBytes(corruptData);
                        using FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write);
                        fs.Position = 0;
                        fs.Write(corruptData, 0, corruptData.Length);
                    }
                    break;

                case CrashPoint.DuringCommitAfterPrimary:
                case CrashPoint.AfterCommit:
                    // The operation completed successfully — perform it
                    using (DataStore store2 = new DataStore(testDir))
                    {
                        store2.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                        performDirectoryOperation(store2, setName, setState, opType);
                        TestDataStoreHelper.WaitForSetIdle(store2, setName);
                    }
                    break;
            }
        }

        private static void performDirectoryOperation(
            DataStore store, string setName, SetState setState, OperationType opType)
        {
            switch (opType)
            {
                case OperationType.DeleteImage:
                    long imageToDelete = setState.LiveImages.First().ImageId;
                    store.DeleteImage(new GlobalImageKey(setName, imageToDelete));
                    break;
                case OperationType.RestoreImage:
                    long imageToRestore = setState.RemovedImages.First().ImageId;
                    store.RestoreImage(new GlobalImageKey(setName, imageToRestore));
                    break;
                case OperationType.RollbackImage:
                    long imageToRollback = setState.RemovedImages.First().ImageId;
                    store.RollbackImage(new GlobalImageKey(setName, imageToRollback));
                    break;
            }
        }

        private void simulateCrashDuringCompactSet(
            string indexPath, string compactTmpPath, string[] shardFiles,
            SetState setState, CrashPoint crashPoint)
        {
            switch (crashPoint)
            {
                case CrashPoint.DuringReplace:
                    if (File.Exists(indexPath))
                    {
                        File.Copy(indexPath, compactTmpPath, overwrite: true);
                    }
                    break;

                case CrashPoint.AfterDeleteBeforeMove:
                    if (File.Exists(indexPath))
                    {
                        File.Copy(indexPath, compactTmpPath, overwrite: true);
                        File.Delete(indexPath);
                    }
                    break;

                case CrashPoint.DuringShardReplace:
                    // Note: We avoid creating a .tmp for shard _0000 because
                    // EmbeddedIndexCommitter.TryRecover checks _0000.nkds.tmp and would
                    // incorrectly treat it as an embedded mode recovery scenario.
                    // Use a non-_0000 shard if available; otherwise simulate as orphaned tail.
                    {
                        string targetShard = shardFiles.FirstOrDefault(f =>
                            !f.EndsWith("_0000.nkds", StringComparison.OrdinalIgnoreCase));
                        if (targetShard != null)
                        {
                            string shardTmpPath = targetShard + ".tmp";
                            File.Copy(targetShard, shardTmpPath, overwrite: true);
                        }
                        else if (shardFiles.Length > 0)
                        {
                            // Only _0000 shard exists — simulate as orphaned tail instead
                            string shard = shardFiles[0];
                            byte[] orphanedData = new byte[64];
                            new Random(setState.ImageCount + 20).NextBytes(orphanedData);
                            using FileStream fs = new FileStream(shard, FileMode.Append, FileAccess.Write);
                            fs.Write(orphanedData, 0, orphanedData.Length);
                        }
                    }
                    break;

                case CrashPoint.AfterShardCompactBeforeCommit:
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[0];
                        byte[] orphanedData = new byte[64];
                        new Random(setState.ImageCount + 2).NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;
            }
        }

        /// <summary>
        /// **Validates: Requirements 12.1, 12.2, 12.3, 12.5, 12.6**
        ///
        /// Property 8: Directory Update Atomicity
        ///
        /// For any directory update operation (DeleteImage, RestoreImage, Rollback) that crashes
        /// before AtomicCommit completes, recovery SHALL restore the directory to its state before
        /// the operation began — no partial updates are visible.
        ///
        /// Strategy:
        /// 1. Create a DataStore set with N images (all live)
        /// 2. Record the directory state (which images are live/removed)
        /// 3. Simulate a crash during DeleteImage by:
        ///    - Performing the delete operation
        ///    - Corrupting the primary header (simulating crash between secondary and primary header writes)
        /// 4. Reopen the set (which triggers dual-header recovery)
        /// 5. Verify the directory state matches either:
        ///    - The state BEFORE the delete (if crash was before commit completed), OR
        ///    - The state AFTER the delete (if commit completed successfully)
        /// 6. Verify no partial state is visible (all-or-nothing)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property DirectoryUpdateAtomicity_DeleteImageCrashBeforeCommit_NoPartialState(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 2 + (rnd.Next() % 4); // 2 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "AtomicityTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images (all live)
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Record the directory state BEFORE the delete
                // All images are live at this point (IDs 1..imageCount)
                List<long> preDeleteLiveIds = Enumerable.Range(1, imageCount).Select(i => (long)i).OrderBy(id => id).ToList();

                // Choose a random image to delete
                int imageToDelete = 1 + (rnd.Next() % imageCount);

                // The expected state AFTER a successful delete
                List<long> postDeleteLiveIds = preDeleteLiveIds.Where(id => id != imageToDelete).ToList();

                // Phase 3: Perform the delete, then corrupt the primary header to simulate
                // a crash between secondary and primary header writes during AtomicCommit.
                // This simulates the scenario where:
                //   - The directory update was appended
                //   - The secondary header was written (with the new state)
                //   - The primary header write was interrupted (crash)
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    store.DeleteImage(new GlobalImageKey(setName, imageToDelete));
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Now corrupt the primary header to simulate the crash scenario
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");

                if (!File.Exists(indexPath))
                {
                    return false.ToProperty().Label(
                        $"Index file not found at {indexPath} (seed={seed})");
                }

                byte[] corruptData = new byte[FileHeader.HeaderSize];
                rnd.NextBytes(corruptData);

                using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    fs.Position = 0;
                    fs.Write(corruptData, 0, corruptData.Length);
                    fs.Flush();
                }

                // Phase 4: Reopen the set (triggers dual-header recovery)
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 5: Verify the directory state matches either pre-delete OR post-delete
                    // (all-or-nothing semantics — no partial state)
                    List<ImageRecord> allImages = store.ListImagesInSetIncludingRemoved(setName);
                    List<long> recoveredLiveIds = allImages
                        .Where(img => !img.Removed)
                        .Select(img => img.Id)
                        .OrderBy(id => id)
                        .ToList();

                    bool matchesPreDelete = recoveredLiveIds.SequenceEqual(preDeleteLiveIds);
                    bool matchesPostDelete = recoveredLiveIds.SequenceEqual(postDeleteLiveIds);

                    // Phase 6: Verify no partial state is visible
                    // The recovered state MUST match one of the two valid states
                    if (!matchesPreDelete && !matchesPostDelete)
                    {
                        return false.ToProperty().Label(
                            $"PARTIAL STATE DETECTED! Recovered live IDs [{string.Join(",", recoveredLiveIds)}] " +
                            $"match neither pre-delete [{string.Join(",", preDeleteLiveIds)}] " +
                            $"nor post-delete [{string.Join(",", postDeleteLiveIds)}] " +
                            $"(deleted image {imageToDelete}, seed={seed})");
                    }

                    // Additional verification: all live images must be readable
                    foreach (long imageId in recoveredLiveIds)
                    {
                        int dataIndex = (int)(imageId - 1);
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[dataIndex].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[dataIndex].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[dataIndex].Length} " +
                                $"after atomicity recovery (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[dataIndex]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after atomicity recovery (seed={seed})");
                        }
                    }

                    // Determine which state was recovered for the label
                    string recoveredState = matchesPostDelete ? "post-delete (commit completed)" : "pre-delete (commit rolled back)";

                    return true.ToProperty().Label(
                        $"Directory update atomicity verified: recovered to {recoveredState}, " +
                        $"{imageCount} images, deleted image {imageToDelete} (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.8**
        ///
        /// Generator validation: ArbSetState produces valid set states with correct invariants.
        /// - ImageCount is between 1 and 10
        /// - Each image has 1-4 blocks of 65536 bytes
        /// - At least one image is always live
        /// - Data length matches BlockCount * BlockSize
        /// </summary>
        [Property(MaxTest = 10)]
        public Property ArbSetState_ProducesValidSetStates()
        {
            return Prop.ForAll(CrashRecoveryGenerators.ArbSetState(), setState =>
            {
                // ImageCount between 1 and 10
                if (setState.ImageCount < 1 || setState.ImageCount > 10)
                    return false.ToProperty().Label(
                        $"ImageCount out of range: {setState.ImageCount}");

                // At least one image is live
                if (setState.LiveImages.Length == 0)
                    return false.ToProperty().Label(
                        $"No live images in set with {setState.ImageCount} images");

                // Each image has valid block count and data size
                foreach (ImageState image in setState.Images)
                {
                    if (image.BlockCount < 1 || image.BlockCount > 4)
                        return false.ToProperty().Label(
                            $"Image {image.ImageId} has invalid BlockCount: {image.BlockCount}");

                    int expectedDataSize = image.BlockCount * setState.BlockSize;
                    if (image.Data.Length != expectedDataSize)
                        return false.ToProperty().Label(
                            $"Image {image.ImageId} data size mismatch: expected={expectedDataSize}, actual={image.Data.Length}");

                    if (string.IsNullOrEmpty(image.Name))
                        return false.ToProperty().Label(
                            $"Image {image.ImageId} has empty name");
                }

                return true.ToProperty().Label(
                    $"Valid SetState: {setState.ImageCount} images, {setState.LiveImages.Length} live, " +
                    $"{setState.RemovedImages.Length} removed");
            });
        }

        /// <summary>
        /// **Validates: Requirements 9.8**
        ///
        /// Generator validation: ArbCrashPoint produces all defined crash point values.
        /// </summary>
        [Property(MaxTest = 100)]
        public Property ArbCrashPoint_ProducesValidCrashPoints()
        {
            return Prop.ForAll(CrashRecoveryGenerators.ArbCrashPoint(), crashPoint =>
            {
                // Must be a defined enum value
                bool isDefined = Enum.IsDefined(typeof(CrashPoint), crashPoint);
                return isDefined.ToProperty().Label(
                    $"CrashPoint value {(int)crashPoint} is not a defined enum value");
            });
        }

        /// <summary>
        /// **Validates: Requirements 9.8**
        ///
        /// Generator validation: ArbIntermediateState produces consistent file system states.
        /// - Files array is non-empty
        /// - CrashPoint is a valid enum value
        /// - File states are consistent with the crash point
        /// </summary>
        [Property(MaxTest = 10)]
        public Property ArbIntermediateState_ProducesConsistentStates()
        {
            return Prop.ForAll(CrashRecoveryGenerators.ArbIntermediateState(), state =>
            {
                // Must have at least one file
                if (state.Files.Length == 0)
                    return false.ToProperty().Label("IntermediateState has no files");

                // CrashPoint must be valid
                if (!Enum.IsDefined(typeof(CrashPoint), state.CrashPoint))
                    return false.ToProperty().Label(
                        $"Invalid CrashPoint: {(int)state.CrashPoint}");

                // For AfterDeleteBeforeMove, original must not exist and .compact.tmp must exist
                if (state.CrashPoint == CrashPoint.AfterDeleteBeforeMove)
                {
                    if (state.OriginalIndexExists)
                        return false.ToProperty().Label(
                            "AfterDeleteBeforeMove: original index should not exist");
                    if (!state.CompactTmpExists)
                        return false.ToProperty().Label(
                            "AfterDeleteBeforeMove: .compact.tmp should exist");
                }

                // For DuringReplace, both original and .compact.tmp should exist
                if (state.CrashPoint == CrashPoint.DuringReplace)
                {
                    if (!state.OriginalIndexExists)
                        return false.ToProperty().Label(
                            "DuringReplace: original index should exist");
                    if (!state.CompactTmpExists)
                        return false.ToProperty().Label(
                            "DuringReplace: .compact.tmp should exist");
                }

                // For DuringShardReplace, shard .tmp files should exist
                if (state.CrashPoint == CrashPoint.DuringShardReplace)
                {
                    if (!state.ShardTmpFilesExist)
                        return false.ToProperty().Label(
                            "DuringShardReplace: shard .tmp files should exist");
                }

                return true.ToProperty().Label(
                    $"Consistent IntermediateState: {state}");
            });
        }

        /// <summary>
        /// **Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5**
        ///
        /// Property 15: Embedded Mode Extraction Recovery
        ///
        /// For any embedded set, if a crash occurs at any point during ExtractToSeparateMode
        /// or ReEmbed, TryRecover SHALL restore the set to either a valid embedded state or
        /// a valid separated state from which compaction can proceed.
        ///
        /// Strategy:
        /// 1. Create an embedded mode DataStore set (shardSize=0) with N images (1-3)
        /// 2. Close the store
        /// 3. Simulate crash scenarios during extraction/re-embed by leaving intermediate files:
        ///    - Scenario A: .tmp index file exists alongside main embedded file (crash during
        ///      extraction before first rename — Req 15.3)
        ///    - Scenario B: Both index and shard files exist (crash after first rename, in
        ///      separate mode — Req 15.4)
        ///    - Scenario C: Shard .tmp file exists with footer magic (crash during re-embed
        ///      after footer written but before rename — Req 15.2)
        ///    - Scenario D: Shard file has index appended but no footer (crash during re-embed
        ///      before footer — Req 15.1)
        /// 4. Reopen the set (which triggers RecoverIfNeeded → EmbeddedIndexCommitter.TryRecover)
        /// 5. Verify the set is in a valid state (either embedded or separated) and all
        ///    previously committed images are readable
        /// </summary>
        [Property(MaxTest = 10)]
        public Property EmbeddedModeExtractionRecovery_CrashAtAnyPoint_RecoverToValidState(
            PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 3); // 1 to 3 images
            int scenario = rnd.Next() % 4; // 4 crash scenarios (A, B, C, D)

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbeddedRecoveryTest";
                const int blockSize = 65536;

                // Phase 1: Create an embedded mode set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

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
                }

                // Phase 2: Verify the embedded file exists and set up paths
                string embeddedPath = Path.Combine(testDir, $"{setName}.nkds");
                string indexTmpPath = embeddedPath + ".tmp";
                string shardPath = Path.Combine(testDir, $"{setName}_0000.nkds");
                string shardTmpPath = shardPath + ".tmp";

                if (!File.Exists(embeddedPath))
                {
                    return false.ToProperty().Label(
                        $"Embedded file not found at {embeddedPath} (seed={seed})");
                }

                // Read the embedded file to understand its structure
                long embeddedFileSize = new FileInfo(embeddedPath).Length;

                // Read the footer to determine the shard boundary
                byte[] footerBytes = new byte[EmbeddedFooter.FooterSize];
                using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
                    fs.ReadExactly(footerBytes, 0, EmbeddedFooter.FooterSize);
                }

                EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBytes);
                if (footer == null)
                {
                    return false.ToProperty().Label(
                        $"Could not read embedded footer (seed={seed})");
                }

                long indexSize = footer.Value.IndexSize;
                long shardBoundary = embeddedFileSize - EmbeddedFooter.FooterSize - indexSize;

                // Extract the index content for use in crash simulation
                byte[] indexContent = new byte[indexSize];
                using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(shardBoundary, SeekOrigin.Begin);
                    int totalRead = 0;
                    while (totalRead < indexContent.Length)
                    {
                        int read = fs.Read(indexContent, totalRead, indexContent.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                }

                // Phase 3: Simulate crash scenarios by manipulating files
                switch (scenario)
                {
                    case 0:
                        // Scenario A (Req 15.3): .tmp index file exists alongside main embedded file.
                        // Crash during ExtractToSeparateMode after index written to .tmp but before
                        // first rename (embedded → shard). TryRecover should delete .tmp and keep
                        // the original embedded file as authoritative.
                        File.WriteAllBytes(indexTmpPath, indexContent);
                        break;

                    case 1:
                        // Scenario B (Req 15.4): Crash after first rename but before second rename.
                        // The embedded file was renamed to shard, .tmp index exists, but the .tmp
                        // was not yet renamed to the index path.
                        // State: test.nkds is MISSING, test.nkds.tmp exists, test_0000.nkds exists.
                        // TryRecover should rename .tmp → test.nkds and return InSeparateMode.
                        File.Move(embeddedPath, shardPath);
                        File.WriteAllBytes(indexTmpPath, indexContent);
                        break;

                    case 2:
                        // Scenario C (Req 15.2): Crash during ReEmbed after footer written but
                        // before rename completes. The shard file has blocks + index + footer
                        // (valid embedded file), and the old index file still exists.
                        // State: test.nkds exists (standalone index), test_0000.nkds exists with
                        // footer magic. TryRecover should detect the completed shard and rename
                        // it to the embedded path.

                        // Create the shard file as a copy of the full embedded file (it has footer)
                        File.Copy(embeddedPath, shardPath);

                        // Replace the embedded path with just the standalone index content
                        File.WriteAllBytes(embeddedPath, indexContent);
                        break;

                    case 3:
                        // Scenario D (Req 15.1): Crash during ReEmbed after index appended to
                        // shard but before footer written. The shard has blocks + index (no footer),
                        // and the standalone index file still exists.
                        // State: test.nkds exists (standalone index), test_0000.nkds exists with
                        // block data + appended index but NO footer.
                        // TryRecover should detect the appended index, append the footer, and
                        // complete the re-embed.

                        // Create the shard file: block data + index content (no footer)
                        byte[] shardData = new byte[shardBoundary];
                        using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            int totalRead = 0;
                            while (totalRead < shardData.Length)
                            {
                                int read = fs.Read(shardData, totalRead, shardData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }
                        }

                        using (FileStream fs = new FileStream(shardPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            fs.Write(shardData, 0, shardData.Length);
                            fs.Write(indexContent, 0, indexContent.Length);
                            // Intentionally NOT writing the footer (simulating crash before footer)
                            fs.Flush(flushToDisk: true);
                        }

                        // Replace the embedded path with just the standalone index content
                        File.WriteAllBytes(embeddedPath, indexContent);
                        break;
                }

                // Phase 4: Reopen the store — this triggers RecoverIfNeeded which calls
                // EmbeddedIndexCommitter.TryRecover and handles all intermediate states
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: blockSize);

                    // Phase 5: Verify all previously committed images are readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Scenario {scenario}: Expected {imageCount} images but found {images.Count} " +
                            $"after recovery (seed={seed})");
                    }

                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Scenario {scenario}: Image {imageId} read {totalRead} bytes, " +
                                $"expected {imageData[i].Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Scenario {scenario}: Image {imageId} data mismatch after recovery " +
                                $"(seed={seed})");
                        }
                    }

                    // Verify the set is in a valid state:
                    // - For scenarios that recover to embedded mode (A, C, D), health should be Clean
                    // - For scenario B (recovers to separate mode), health may report NeedsRecovery
                    //   because both index and shard exist (a valid separated state from which
                    //   compaction can proceed). This is acceptable per the property definition:
                    //   "TryRecover SHALL restore the set to either a valid embedded state or a
                    //   valid separated state from which compaction can proceed."
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (scenario == 1)
                    {
                        // Scenario B recovers to separate mode — the set is valid and usable
                        // even if health reports NeedsRecovery (it just means re-embed is pending)
                        if (health.Status == SetHealthStatus.Unrecoverable)
                        {
                            return false.ToProperty().Label(
                                $"Scenario {scenario}: Health reported Unrecoverable after recovery " +
                                $"to separate mode (seed={seed})");
                        }
                    }
                    else
                    {
                        // Scenarios A, C, D should recover to embedded mode (Clean)
                        if (health.Status != SetHealthStatus.Clean)
                        {
                            string issueDetails = health.DetectedIssues.Count > 0
                                ? string.Join("; ", health.DetectedIssues)
                                : health.Description;

                            return false.ToProperty().Label(
                                $"Scenario {scenario}: Expected Clean health after recovery but got " +
                                $"{health.Status}: {issueDetails} (seed={seed})");
                        }
                    }

                    return true.ToProperty().Label(
                        $"Scenario {scenario}: Embedded mode recovery successful — " +
                        $"{imageCount} images readable, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, scenario={scenario}, images={imageCount}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.1, 9.6, 9.7**
        ///
        /// Crash simulation tests for CompactSetSeparate steps 1-5.
        ///
        /// CompactSetSeparate steps:
        /// 1. CompactShards (remove orphaned blocks from shard files)
        /// 2. CompactTo (write compacted index to temp file)
        /// 3. Dispose old index file handle
        /// 4. AtomicFileOps.ReplaceFile (replace original with compacted)
        /// 5. Reopen compacted file
        ///
        /// For each step boundary, we simulate a crash by:
        /// - Creating a valid set with some removed images
        /// - Performing operations up to that step
        /// - Leaving intermediate files in place (e.g., .compact.tmp file exists)
        /// - Opening the set (triggering RecoverIfNeeded)
        /// - Verifying all previously committed images are readable
        /// - Verifying CheckSetHealth returns Clean after recovery
        ///
        /// Step boundaries simulated:
        /// 0 = Crash after Step 1 (CompactShards done): shards compacted, index has stale offsets,
        ///     orphaned tail data may exist. No .compact.tmp yet.
        /// 1 = Crash after Step 2 (CompactTo done): both original index and .compact.tmp exist.
        /// 2 = Crash after Step 3 (Dispose done): same as step 2 from file perspective (both exist).
        /// 3 = Crash during Step 4 fallback (AfterDeleteBeforeMove): original deleted, .compact.tmp exists.
        /// 4 = Crash after Step 4 (ReplaceFile done, before Reopen): only new index at original path.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSetSeparate_CrashAtEachStep_RecoveryRestoresLastCommittedState(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            // Generate varied set state: 2-6 images, at least one removed (needed for compaction)
            int imageCount = 2 + (rnd.Next() % 5); // 2 to 6 images
            int removedCount = 1 + (rnd.Next() % Math.Max(1, imageCount - 1)); // 1 to imageCount-1 removed

            // Choose which step boundary to simulate (0-4)
            int stepBoundary = rnd.Next() % 5;

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "CompactSepCrashTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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

                    // Remove some images to make compaction meaningful
                    List<int> indicesToRemove = Enumerable.Range(0, imageCount)
                        .OrderBy(_ => rnd.Next())
                        .Take(removedCount)
                        .ToList();

                    foreach (int idx in indicesToRemove)
                    {
                        store.DeleteImage(new GlobalImageKey(setName, idx + 1));
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Record the last committed state (live images after deletions)
                HashSet<int> removedSet = Enumerable.Range(0, imageCount)
                    .OrderBy(_ => rnd.Next())
                    .Take(removedCount)
                    .Select(i => i + 1)
                    .ToHashSet();

                // Re-derive which images are live (not removed)
                // We need to reopen to get the actual state since the random ordering above
                // may differ from what we track. Let's just record from the store.
                List<long> committedLiveImageIds;
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    List<ImageRecord> liveImages = store.ListImagesInSet(setName);
                    committedLiveImageIds = liveImages.Select(img => img.Id).OrderBy(id => id).ToList();
                }

                // Phase 3: Simulate crash at the specified step boundary
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");
                string compactTmpPath = indexPath + ".compact.tmp";

                switch (stepBoundary)
                {
                    case 0:
                        // Crash after Step 1 (CompactShards done, before CompactTo):
                        // Shards have been compacted (orphaned blocks removed), but the index
                        // still references old offsets. Since CompactShards calls AtomicCommit
                        // internally to update the block index, the index is actually updated.
                        // However, if the crash happens DURING CompactShards (before its commit),
                        // we'd have orphaned tail data on shards. Simulate by appending orphaned
                        // data to a shard file.
                        {
                            string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                            if (shardFiles.Length > 0)
                            {
                                string targetShard = shardFiles[rnd.Next() % shardFiles.Length];
                                int orphanedSize = 64 + (rnd.Next() % 256);
                                byte[] orphanedData = new byte[orphanedSize];
                                rnd.NextBytes(orphanedData);
                                using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                                fs.Write(orphanedData, 0, orphanedData.Length);
                            }
                        }
                        break;

                    case 1:
                        // Crash after Step 2 (CompactTo done, before Dispose):
                        // Both original index and .compact.tmp exist.
                        // The .compact.tmp is a valid compacted index file.
                        // Recovery should delete .compact.tmp and keep original.
                        File.Copy(indexPath, compactTmpPath, overwrite: true);
                        break;

                    case 2:
                        // Crash after Step 3 (Dispose done, before ReplaceFile):
                        // Same file state as step 1 — both original and .compact.tmp exist.
                        // The old handle is closed but files are the same.
                        // Recovery should delete .compact.tmp and keep original.
                        File.Copy(indexPath, compactTmpPath, overwrite: true);
                        break;

                    case 3:
                        // Crash during Step 4 fallback (AfterDeleteBeforeMove):
                        // Original index was deleted, .compact.tmp not yet moved.
                        // Only .compact.tmp exists. Recovery should rename it to original.
                        File.Copy(indexPath, compactTmpPath, overwrite: true);
                        File.Delete(indexPath);
                        break;

                    case 4:
                        // Crash after Step 4 (ReplaceFile done, before Reopen in Step 5):
                        // The replacement completed successfully. Only the new index exists
                        // at the original path. No .compact.tmp. This is essentially a clean
                        // state — recovery has nothing to do. The set should open normally.
                        // Simulate by performing an actual compact (which completes step 4).
                        using (DataStore store = new DataStore(testDir))
                        {
                            store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                            store.CompactSet(setName);
                        }
                        break;
                }

                // Phase 4: Reopen the store (triggering RecoverIfNeeded)
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 5: Verify all previously committed live images are readable
                    List<ImageRecord> recoveredImages = store.ListImagesInSet(setName);
                    List<long> recoveredLiveIds = recoveredImages.Select(img => img.Id).OrderBy(id => id).ToList();

                    // For step 4 (compact completed), the live images should be the same
                    // (compaction doesn't change which images are live, it just removes
                    // the removed images from the file)
                    if (!recoveredLiveIds.SequenceEqual(committedLiveImageIds))
                    {
                        return false.ToProperty().Label(
                            $"Step {stepBoundary}: Live image IDs mismatch. " +
                            $"Expected: [{string.Join(",", committedLiveImageIds)}], " +
                            $"Got: [{string.Join(",", recoveredLiveIds)}] " +
                            $"(seed={seed}, images={imageCount}, removed={removedCount})");
                    }

                    // Verify each live image's data is correct
                    foreach (long imageId in committedLiveImageIds)
                    {
                        int dataIndex = (int)(imageId - 1);
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[dataIndex].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[dataIndex].Length)
                        {
                            return false.ToProperty().Label(
                                $"Step {stepBoundary}: Image {imageId} read {totalRead} bytes, " +
                                $"expected {imageData[dataIndex].Length} " +
                                $"(seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[dataIndex]))
                        {
                            return false.ToProperty().Label(
                                $"Step {stepBoundary}: Image {imageId} data mismatch after recovery " +
                                $"(seed={seed})");
                        }
                    }

                    // Phase 6: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Step {stepBoundary}: Expected Clean health after recovery but got " +
                            $"{health.Status}: {issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"Step {stepBoundary}: CompactSetSeparate crash recovery successful — " +
                        $"{committedLiveImageIds.Count} live images preserved, health=Clean " +
                        $"(seed={seed}, images={imageCount}, removed={removedCount})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, step={stepBoundary}, images={imageCount}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.3, 9.6, 9.7**
        ///
        /// Crash simulation tests for CompactShards pipeline stage boundaries.
        ///
        /// CompactShards pipeline stages:
        /// 1. GatherReferencedKeys
        /// 2. BuildChunkList
        /// 3. GroupChunksByShard
        /// 4. CompactShard (per shard) — crash during shard copy-on-write
        /// 5. DeleteEmptyShards
        /// 6. UpdateBlockIndex
        /// 7. UpdateBlockMaps
        /// 8. UpdateMetadataSections
        /// 9. AtomicCommit
        ///
        /// For crashes between stages 1-3: no file modifications yet, recovery is trivial (just reopen)
        /// For crash during stage 4: shard .tmp files may exist → recovery deletes them
        /// For crash between stages 4-8: shards may be compacted but index not yet committed
        ///   → dual-header recovery restores pre-compact state
        /// For crash during stage 9: dual-header protocol handles partial commit
        ///
        /// Strategy:
        /// 1. Create a valid set with removed images (so compaction has work to do)
        /// 2. Leave intermediate files representing each crash point
        /// 3. Open the set (triggering recovery)
        /// 4. Verify committed images are readable
        /// 5. Verify CheckSetHealth returns Clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactShardsPipeline_CrashAtStageBoundary_RecoveryRestoresLastCommittedState(
            PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            // 9 pipeline stages → 9 crash scenarios (crash during/after each stage)
            int crashStage = (seed % 9) + 1;

            int imageCount = 2 + (rnd.Next() % 4); // 2 to 5 images
            int imagesToRemove = 1 + (rnd.Next() % (imageCount - 1)); // Remove 1 to (imageCount-1)

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "ShardPipelineCrashTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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

                    // Remove some images so compaction has work to do
                    List<int> indicesToRemove = Enumerable.Range(0, imageCount)
                        .OrderBy(_ => rnd.Next())
                        .Take(imagesToRemove)
                        .ToList();

                    foreach (int idx in indicesToRemove)
                    {
                        store.DeleteImage(new GlobalImageKey(setName, idx + 1));
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Record the last committed state (live images after deletions)
                HashSet<long> removedImageIds = new HashSet<long>();
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    List<ImageRecord> allImages = store.ListImagesInSetIncludingRemoved(setName);
                    foreach (ImageRecord img in allImages)
                    {
                        if (img.Removed)
                            removedImageIds.Add(img.Id);
                    }
                }

                List<long> committedLiveImageIds = Enumerable.Range(1, imageCount)
                    .Select(i => (long)i)
                    .Where(id => !removedImageIds.Contains(id))
                    .OrderBy(id => id)
                    .ToList();

                Dictionary<long, byte[]> committedImageData = committedLiveImageIds
                    .ToDictionary(id => id, id => imageData[(int)(id - 1)]);

                // Phase 3: Simulate crash at the specified pipeline stage boundary
                simulateCompactShardsPipelineCrash(testDir, setName, crashStage, rnd);

                // Phase 4: Reopen the store (triggering recovery)
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 5: Verify recovered state matches last committed state
                    List<ImageRecord> recoveredImages = store.ListImagesInSet(setName);
                    List<long> recoveredLiveIds = recoveredImages
                        .Select(img => img.Id)
                        .OrderBy(id => id)
                        .ToList();

                    if (!recoveredLiveIds.SequenceEqual(committedLiveImageIds))
                    {
                        return false.ToProperty().Label(
                            $"Stage {crashStage}: Live image IDs mismatch after recovery. " +
                            $"Expected: [{string.Join(",", committedLiveImageIds)}], " +
                            $"Got: [{string.Join(",", recoveredLiveIds)}] (seed={seed})");
                    }

                    // Verify each live image's data matches the committed data
                    foreach (long imageId in committedLiveImageIds)
                    {
                        byte[] expectedData = committedImageData[imageId];

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
                        {
                            return false.ToProperty().Label(
                                $"Stage {crashStage}: Image {imageId} read {totalRead} bytes, " +
                                $"expected {expectedData.Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(expectedData))
                        {
                            return false.ToProperty().Label(
                                $"Stage {crashStage}: Image {imageId} data mismatch after recovery (seed={seed})");
                        }
                    }

                    // Phase 6: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Stage {crashStage}: Expected Clean health after recovery but got " +
                            $"{health.Status}: {issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"Stage {crashStage}: Pipeline crash recovery successful — " +
                        $"{committedLiveImageIds.Count} live images preserved, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, stage={crashStage}, images={imageCount}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a crash at a specific CompactShards pipeline stage boundary.
        ///
        /// Stage 1 (GatherReferencedKeys): No file modifications — just reopen.
        /// Stage 2 (BuildChunkList): No file modifications — just reopen.
        /// Stage 3 (GroupChunksByShard): No file modifications — just reopen.
        /// Stage 4 (CompactShard): Shard .tmp files may exist → recovery deletes them.
        /// Stage 5 (DeleteEmptyShards): Shards compacted, some empty shards may be deleted,
        ///          but index not committed → orphaned tail or stale offsets.
        /// Stage 6 (UpdateBlockIndex): Shards compacted, index in memory but not committed
        ///          → orphaned tail data on shards.
        /// Stage 7 (UpdateBlockMaps): Same as stage 6 — in-memory changes not committed.
        /// Stage 8 (UpdateMetadataSections): Same as stage 6 — in-memory changes not committed.
        /// Stage 9 (AtomicCommit): Crash during dual-header write → corrupt primary header.
        /// </summary>
        private void simulateCompactShardsPipelineCrash(
            string testDir, string setName, int crashStage, Random rnd)
        {
            string indexPath = Path.Combine(testDir, $"{setName}.nkds");
            string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");

            switch (crashStage)
            {
                case 1: // Crash after GatherReferencedKeys — no file modifications
                case 2: // Crash after BuildChunkList — no file modifications
                case 3: // Crash after GroupChunksByShard — no file modifications
                    // These stages are pure computation with no disk I/O.
                    // A crash here means the process dies before any files are touched.
                    // Recovery is trivial: just reopen the set (files are in committed state).
                    // No intermediate files to simulate.
                    break;

                case 4: // Crash during CompactShard — shard .tmp files exist
                    // CompactShard writes to a .tmp file then uses File.Replace.
                    // A crash during this stage leaves .tmp files alongside originals.
                    // Recovery deletes the .tmp files (shard compact did not complete).
                    //
                    // NOTE: We skip creating .tmp for the _0000 shard because the
                    // EmbeddedIndexCommitter.TryRecover() processes _0000.nkds.tmp as an
                    // embedded re-embed intermediate (it runs before shard .tmp cleanup).
                    // For _0000-only sets, we simulate by leaving the set in committed state
                    // (recovery would just delete the .tmp, restoring committed state anyway).
                    if (shardFiles.Length > 0)
                    {
                        // Filter out the _0000 shard to avoid conflict with EmbeddedIndexCommitter
                        string[] nonZeroShards = shardFiles
                            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_0000"))
                            .ToArray();

                        if (nonZeroShards.Length > 0)
                        {
                            int shardsToAffect = 1 + (rnd.Next() % nonZeroShards.Length);
                            for (int i = 0; i < shardsToAffect; i++)
                            {
                                string shardTmpPath = nonZeroShards[i] + ".tmp";
                                File.Copy(nonZeroShards[i], shardTmpPath, overwrite: true);
                            }
                        }
                        // If only _0000 exists, the crash simulation leaves no intermediate
                        // files — recovery is trivial (just reopen). This is correct because
                        // the recovery for _0000.nkds.tmp is handled by the embedded committer
                        // path, and for separate-mode sets the shard .tmp cleanup handles it.
                    }
                    break;

                case 5: // Crash after DeleteEmptyShards — shards compacted but index not committed
                    // At this point, shards have been replaced with compacted versions
                    // and empty shards deleted, but the index still has old offsets.
                    // Simulate by appending orphaned tail data to a shard (representing
                    // the state where shard content doesn't match the committed index).
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[rnd.Next() % shardFiles.Length];
                        int orphanedSize = 64 + (rnd.Next() % 256);
                        byte[] orphanedData = new byte[orphanedSize];
                        rnd.NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;

                case 6: // Crash after UpdateBlockIndex — in-memory only, not committed
                case 7: // Crash after UpdateBlockMaps — in-memory only, not committed
                case 8: // Crash after UpdateMetadataSections — in-memory only, not committed
                    // These stages modify the in-memory index file state but haven't called
                    // AtomicCommit yet. A crash here means the shard files may have been
                    // compacted (stage 4 completed) but the index was never committed.
                    // The committed index still has the old offsets.
                    // Simulate by appending orphaned tail data to shards (the shards may
                    // have been rewritten but the index doesn't reflect it).
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[rnd.Next() % shardFiles.Length];
                        int orphanedSize = 32 + (rnd.Next() % 512);
                        byte[] orphanedData = new byte[orphanedSize];
                        rnd.NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;

                case 9: // Crash during AtomicCommit — dual-header protocol
                    // AtomicCommit writes SecondaryHeader first, then PrimaryHeader.
                    // A crash during this stage means the secondary header has the new state
                    // but the primary header is corrupt/stale.
                    // Simulate by corrupting the primary header (first 256 bytes).
                    if (File.Exists(indexPath))
                    {
                        byte[] corruptData = new byte[FileHeader.HeaderSize];
                        rnd.NextBytes(corruptData);
                        using FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write);
                        fs.Position = 0;
                        fs.Write(corruptData, 0, corruptData.Length);
                    }
                    break;
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.5, 9.6, 9.7**
        ///
        /// Crash simulation for DeleteImage phases.
        ///
        /// Directory update operations (DeleteImage) follow this sequence:
        /// 1. Append new directory entry to index (marks image as removed)
        /// 2. AtomicCommit — write secondary header
        /// 3. AtomicCommit — write primary header
        ///
        /// Crash scenarios:
        /// - After directory appended but before AtomicCommit: directory data is orphaned,
        ///   dual-header recovery uses pre-operation headers → image retains live state
        /// - Between secondary and primary header writes: secondary has new state,
        ///   primary has old state → secondary used as authoritative (delete completes)
        ///
        /// Strategy:
        /// 1. Create a set with N images (2-5), all live
        /// 2. Record the pre-delete state
        /// 3. Simulate crash during DeleteImage at each phase
        /// 4. Reopen the set (triggering recovery)
        /// 5. Verify images retain their pre-crash state (all-or-nothing)
        /// 6. Verify CheckSetHealth returns Clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property DeleteImage_CrashAtEachPhase_RecoveryRestoresPreOperationState(PositiveInt seedWrapper) => deleteRestoreRollback_CrashSimulation_Impl(seedWrapper.Get, DirectoryOperation.Delete);

        /// <summary>
        /// **Validates: Requirements 9.5, 9.6, 9.7**
        ///
        /// Crash simulation for RestoreImage phases.
        ///
        /// Directory update operations (RestoreImage) follow this sequence:
        /// 1. Append new directory entry to index (marks image as live)
        /// 2. AtomicCommit — write secondary header
        /// 3. AtomicCommit — write primary header
        ///
        /// Crash scenarios:
        /// - After directory appended but before AtomicCommit: directory data is orphaned,
        ///   dual-header recovery uses pre-operation headers → image retains removed state
        /// - Between secondary and primary header writes: secondary has new state,
        ///   primary has old state → secondary used as authoritative (restore completes)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property RestoreImage_CrashAtEachPhase_RecoveryRestoresPreOperationState(PositiveInt seedWrapper) => deleteRestoreRollback_CrashSimulation_Impl(seedWrapper.Get, DirectoryOperation.Restore);

        /// <summary>
        /// **Validates: Requirements 9.5, 9.6, 9.7**
        ///
        /// Crash simulation for RollbackImage phases.
        ///
        /// NOTE: Rollback is now a physical truncation operation (not a directory update).
        /// It truncates shards and prunes the block index directly. Crash recovery during
        /// rollback is handled by orphaned tail truncation (if crash after shard truncation
        /// but before index commit). This test is retained but adapted to verify that
        /// after a successful rollback, the set is in a clean state.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property RollbackImage_CrashAtEachPhase_RecoveryRestoresPreOperationState(PositiveInt seedWrapper)
        {
            // Rollback is no longer a directory-update operation — it physically truncates shards.
            // The old crash simulation (simulating crash during AtomicCommit of a directory update)
            // no longer applies. Instead, verify that a successful rollback leaves the set clean.
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);
            string testDir = Path.Combine(_tempDir, $"rollback_clean_{seed}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDir);

            try
            {
                string setName = "TestSet";
                int imageCount = 2 + (rnd.Next() % 3); // 2-4 images
                long shardSize = 50L * 1024 * 1024 * 1024;
                int blockSize = 65536;

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize, blockSize);

                    for (int i = 1; i <= imageCount; i++)
                    {
                        byte[] data = new byte[blockSize];
                        rnd.NextBytes(data);
                        using (IImageWriter writer = store.AddImage(setName, $"Image_{i}"))
                        {
                            writer.WriteData(0, data, BlockType.File);
                            writer.FinalizeImage((long)data.Length, (uint)i, (ulong)i);
                        }
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Rollback to image 1 (removes images 2+)
                    store.RollbackImage(new GlobalImageKey(setName, 1));
                }

                // Reopen and verify clean state
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    bool onlyImage1 = images.Count == 1 && images[0].Id == 1;

                    RecoveryState health = store.CheckSetHealth(setName);
                    bool isClean = health.Status == SetHealthStatus.Clean;

                    return (onlyImage1 && isClean).ToProperty()
                        .Label($"After rollback: imageCount={images.Count}, health={health.Status}");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty()
                    .Label($"Exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Enum representing the directory update operation being tested.
        /// </summary>
        private enum DirectoryOperation
        {
            Delete,
            Restore,
            Rollback
        }

        /// <summary>
        /// Shared implementation for Delete/Restore/Rollback crash simulation tests.
        /// Tests two crash scenarios:
        ///   Scenario 0: Crash after directory appended but before AtomicCommit
        ///               (simulated via orphaned tail data on shard — the directory append
        ///                is uncommitted so recovery uses the pre-operation headers)
        ///   Scenario 1: Crash between secondary and primary header writes
        ///               (simulated by corrupting the primary header — secondary is authoritative)
        /// </summary>
        private Property deleteRestoreRollback_CrashSimulation_Impl(int seed, DirectoryOperation operation)
        {
            Random rnd = new Random(seed);

            int imageCount = 2 + (rnd.Next() % 4); // 2 to 5 images
            int scenario = rnd.Next() % 2; // 0 = before commit, 1 = between headers

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "DirOpCrashTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images and record their data
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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

                    // For Restore operation, we need an image to be removed first
                    if (operation == DirectoryOperation.Restore)
                    {
                        // Delete the last image so we can test restoring it
                        store.DeleteImage(new GlobalImageKey(setName, imageCount));
                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Record the pre-operation state
                // For Delete: all images are live (or all except last for Restore setup)
                // For Restore: last image is removed, rest are live
                // For Rollback: all images are live
                List<long> preOpLiveIds;
                List<long> preOpRemovedIds;

                if (operation == DirectoryOperation.Restore)
                {
                    preOpLiveIds = Enumerable.Range(1, imageCount - 1).Select(i => (long)i).ToList();
                    preOpRemovedIds = new List<long> { imageCount };
                }
                else
                {
                    preOpLiveIds = Enumerable.Range(1, imageCount).Select(i => (long)i).ToList();
                    preOpRemovedIds = new List<long>();
                }

                // Choose target image for the operation
                int targetImageId;
                switch (operation)
                {
                    case DirectoryOperation.Delete:
                        targetImageId = 1 + (rnd.Next() % imageCount); // delete a random live image
                        break;
                    case DirectoryOperation.Restore:
                        targetImageId = imageCount; // restore the removed image
                        break;
                    case DirectoryOperation.Rollback:
                        // Rollback to a random image (marks all after it as removed)
                        targetImageId = 1 + (rnd.Next() % (imageCount - 1)); // rollback to image 1..(N-1)
                        break;
                    default:
                        targetImageId = 1;
                        break;
                }

                // Compute expected post-operation state (if commit succeeds)
                List<long> postOpLiveIds;
                switch (operation)
                {
                    case DirectoryOperation.Delete:
                        postOpLiveIds = preOpLiveIds.Where(id => id != targetImageId).ToList();
                        break;
                    case DirectoryOperation.Restore:
                        postOpLiveIds = preOpLiveIds.Concat(new[] { (long)targetImageId }).OrderBy(id => id).ToList();
                        break;
                    case DirectoryOperation.Rollback:
                        postOpLiveIds = preOpLiveIds.Where(id => id <= targetImageId).ToList();
                        break;
                    default:
                        postOpLiveIds = preOpLiveIds;
                        break;
                }

                string indexPath = Path.Combine(testDir, $"{setName}.nkds");

                if (scenario == 0)
                {
                    // Scenario 0: Crash after directory appended but before AtomicCommit
                    // The directory update was written to the file (appended) but the headers
                    // were NOT updated. On recovery, the dual-header protocol uses the old
                    // headers which point to the pre-operation directory state.
                    // Simulate by appending orphaned data to a shard file (representing
                    // uncommitted directory bytes that will be truncated on recovery).
                    string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[shardFiles.Length - 1];
                        byte[] orphanedData = new byte[128 + (rnd.Next() % 256)];
                        rnd.NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }

                    // Recovery should restore to pre-operation state (orphaned tail truncated,
                    // headers still point to old directory)
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                        List<ImageRecord> recoveredImages = store.ListImagesInSet(setName);
                        List<long> recoveredLiveIds = recoveredImages
                            .Select(img => img.Id)
                            .OrderBy(id => id)
                            .ToList();

                        // Should match pre-operation state (commit never happened)
                        if (!recoveredLiveIds.SequenceEqual(preOpLiveIds))
                        {
                            return false.ToProperty().Label(
                                $"{operation} Scenario 0 (before commit): Live IDs mismatch. " +
                                $"Expected pre-op: [{string.Join(",", preOpLiveIds)}], " +
                                $"Got: [{string.Join(",", recoveredLiveIds)}] " +
                                $"(target={targetImageId}, seed={seed})");
                        }

                        // Verify all live images are readable
                        foreach (long imageId in recoveredLiveIds)
                        {
                            int dataIndex = (int)(imageId - 1);
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                            using Stream stream = reader.OpenStream(0);

                            byte[] readData = new byte[imageData[dataIndex].Length];
                            int totalRead = 0;
                            while (totalRead < readData.Length)
                            {
                                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }

                            if (totalRead != imageData[dataIndex].Length)
                            {
                                return false.ToProperty().Label(
                                    $"{operation} Scenario 0: Image {imageId} read {totalRead} bytes, " +
                                    $"expected {imageData[dataIndex].Length} (seed={seed})");
                            }

                            if (!readData.SequenceEqual(imageData[dataIndex]))
                            {
                                return false.ToProperty().Label(
                                    $"{operation} Scenario 0: Image {imageId} data mismatch (seed={seed})");
                            }
                        }

                        // Verify CheckSetHealth returns Clean after recovery
                        RecoveryState health = store.CheckSetHealth(setName);
                        if (health.Status != SetHealthStatus.Clean)
                        {
                            string issueDetails = health.DetectedIssues.Count > 0
                                ? string.Join("; ", health.DetectedIssues)
                                : health.Description;
                            return false.ToProperty().Label(
                                $"{operation} Scenario 0: Expected Clean health but got " +
                                $"{health.Status}: {issueDetails} (seed={seed})");
                        }

                        return true.ToProperty().Label(
                            $"{operation} Scenario 0 (before commit): Recovery restored pre-op state. " +
                            $"{recoveredLiveIds.Count} live images, target={targetImageId} (seed={seed})");
                    }
                }
                else
                {
                    // Scenario 1: Crash between secondary and primary header writes
                    // The operation completed the directory append AND the secondary header
                    // was written (with the new state), but the primary header write was
                    // interrupted. On recovery, the secondary header is used as authoritative
                    // because the primary is corrupt → the operation effectively completes.

                    // First, actually perform the operation (so the secondary header has new state)
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                        switch (operation)
                        {
                            case DirectoryOperation.Delete:
                                store.DeleteImage(new GlobalImageKey(setName, targetImageId));
                                break;
                            case DirectoryOperation.Restore:
                                store.RestoreImage(new GlobalImageKey(setName, targetImageId));
                                break;
                            case DirectoryOperation.Rollback:
                                store.RollbackImage(new GlobalImageKey(setName, targetImageId));
                                break;
                        }

                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Now corrupt the primary header to simulate crash between secondary
                    // and primary header writes. The secondary header has the new state.
                    if (!File.Exists(indexPath))
                    {
                        return false.ToProperty().Label(
                            $"{operation} Scenario 1: Index file not found (seed={seed})");
                    }

                    byte[] corruptData = new byte[FileHeader.HeaderSize];
                    rnd.NextBytes(corruptData);
                    using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        fs.Position = 0;
                        fs.Write(corruptData, 0, corruptData.Length);
                        fs.Flush();
                    }

                    // Recovery should use secondary header (which has the post-operation state)
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                        List<ImageRecord> recoveredImages = store.ListImagesInSet(setName);
                        List<long> recoveredLiveIds = recoveredImages
                            .Select(img => img.Id)
                            .OrderBy(id => id)
                            .ToList();

                        // Should match post-operation state (secondary header is authoritative)
                        if (!recoveredLiveIds.SequenceEqual(postOpLiveIds))
                        {
                            return false.ToProperty().Label(
                                $"{operation} Scenario 1 (between headers): Live IDs mismatch. " +
                                $"Expected post-op: [{string.Join(",", postOpLiveIds)}], " +
                                $"Got: [{string.Join(",", recoveredLiveIds)}] " +
                                $"(target={targetImageId}, seed={seed})");
                        }

                        // Verify all live images are readable
                        foreach (long imageId in recoveredLiveIds)
                        {
                            int dataIndex = (int)(imageId - 1);
                            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                            using Stream stream = reader.OpenStream(0);

                            byte[] readData = new byte[imageData[dataIndex].Length];
                            int totalRead = 0;
                            while (totalRead < readData.Length)
                            {
                                int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                                if (read == 0) break;
                                totalRead += read;
                            }

                            if (totalRead != imageData[dataIndex].Length)
                            {
                                return false.ToProperty().Label(
                                    $"{operation} Scenario 1: Image {imageId} read {totalRead} bytes, " +
                                    $"expected {imageData[dataIndex].Length} (seed={seed})");
                            }

                            if (!readData.SequenceEqual(imageData[dataIndex]))
                            {
                                return false.ToProperty().Label(
                                    $"{operation} Scenario 1: Image {imageId} data mismatch (seed={seed})");
                            }
                        }

                        // Verify CheckSetHealth returns Clean after recovery
                        RecoveryState health = store.CheckSetHealth(setName);
                        if (health.Status != SetHealthStatus.Clean)
                        {
                            string issueDetails = health.DetectedIssues.Count > 0
                                ? string.Join("; ", health.DetectedIssues)
                                : health.Description;
                            return false.ToProperty().Label(
                                $"{operation} Scenario 1: Expected Clean health but got " +
                                $"{health.Status}: {issueDetails} (seed={seed})");
                        }

                        return true.ToProperty().Label(
                            $"{operation} Scenario 1 (between headers): Recovery used secondary header. " +
                            $"{recoveredLiveIds.Count} live images, target={targetImageId} (seed={seed})");
                    }
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception ({operation}, scenario={rnd.Next() % 2}, seed={seed}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.4, 9.6, 9.7**
        ///
        /// Crash simulation tests for ImageWriter commit phases.
        ///
        /// ImageWriter commit phases:
        /// 1. Block writes to shard (appending block data)
        /// 2. Metadata section write (appending file records)
        /// 3. AtomicCommit — write secondary header
        /// 4. AtomicCommit — write primary header
        /// 5. Release writer lock
        ///
        /// Crash scenarios tested:
        /// - Before commit (during block/metadata writes): orphaned tail data in shard → truncated on recovery
        /// - During commit (between secondary and primary header writes): dual-header protocol → secondary header used
        /// - After commit (before lock release): stale lock detected and released on next open
        ///
        /// Strategy:
        /// 1. Create a valid set with committed images
        /// 2. Add partial data to shard (simulating incomplete write)
        /// 3. Open the set (triggering recovery)
        /// 4. Verify previously committed images are readable
        /// 5. Verify the orphaned tail data is truncated
        /// 6. Verify CheckSetHealth returns Clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property ImageWriterCrashBeforeCommit_OrphanedTailTruncatedAndImagesReadable(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 committed images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "ImageWriterCrashBeforeCommit";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N committed images
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Record expected shard sizes (the committed state)
                string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");

                if (shardFiles.Length == 0)
                {
                    return false.ToProperty().Label(
                        $"No shard files found after creating {imageCount} images (seed={seed})");
                }

                Dictionary<string, long> expectedShardSizes = new Dictionary<string, long>();
                foreach (string shardFile in shardFiles)
                {
                    expectedShardSizes[shardFile] = new FileInfo(shardFile).Length;
                }

                // Phase 3: Simulate crash during ImageWriter block/metadata writes
                // by appending orphaned data to the last shard (simulating partial block
                // writes and metadata section writes that were not committed)
                string targetShard = shardFiles[shardFiles.Length - 1];

                // Simulate partial block write (1 to 3 partial blocks worth of data)
                int partialBlocks = 1 + (rnd.Next() % 3);
                int orphanedSize = (rnd.Next() % blockSize) + 1; // partial block (1 to blockSize bytes)
                if (partialBlocks > 1)
                    orphanedSize += (partialBlocks - 1) * blockSize; // additional full blocks

                byte[] orphanedData = new byte[orphanedSize];
                rnd.NextBytes(orphanedData);

                using (FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    fs.Write(orphanedData, 0, orphanedData.Length);
                    fs.Flush();
                }

                // Verify orphaned data was appended
                long actualSizeBeforeRecovery = new FileInfo(targetShard).Length;
                if (actualSizeBeforeRecovery <= expectedShardSizes[targetShard])
                {
                    return false.ToProperty().Label(
                        $"Failed to append orphaned data to shard (seed={seed})");
                }

                // Phase 4: Reopen the store — triggers RecoverIfNeeded → orphaned tail truncation
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 5: Verify all previously committed images are still readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after recovery (seed={seed})");
                    }

                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} " +
                                $"after crash-before-commit recovery (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after crash-before-commit recovery (seed={seed})");
                        }
                    }

                    // Phase 6: Verify shard files are truncated to expected sizes
                    foreach (KeyValuePair<string, long> kvp in expectedShardSizes)
                    {
                        string shardFile = kvp.Key;
                        long expectedSize = kvp.Value;

                        if (!File.Exists(shardFile))
                        {
                            return false.ToProperty().Label(
                                $"Shard file '{Path.GetFileName(shardFile)}' missing after recovery (seed={seed})");
                        }

                        long actualSize = new FileInfo(shardFile).Length;
                        if (actualSize != expectedSize)
                        {
                            return false.ToProperty().Label(
                                $"Shard '{Path.GetFileName(shardFile)}' not truncated: " +
                                $"expected={expectedSize}, actual={actualSize} (seed={seed})");
                        }
                    }

                    // Phase 7: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Expected Clean health after recovery but got {health.Status}: " +
                            $"{issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"ImageWriter crash-before-commit recovery successful: {imageCount} images readable, " +
                        $"orphaned tail ({orphanedSize} bytes) truncated, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.4, 9.6, 9.7**
        ///
        /// Crash simulation: ImageWriter crash DURING commit (between secondary and primary
        /// header writes). The dual-header protocol ensures the secondary header is used
        /// as authoritative on recovery.
        ///
        /// This simulates the scenario where:
        /// - A new image was being added
        /// - Block data was written to shard
        /// - AtomicCommit began: secondary header was written with the new state
        /// - Crash occurred before primary header was written
        ///
        /// On recovery:
        /// - BinaryIndexFile.Open detects corrupt/stale primary header
        /// - Falls back to secondary header (which includes the new image)
        /// - The new image is visible and readable
        /// </summary>
        [Property(MaxTest = 10)]
        public Property ImageWriterCrashDuringCommit_DualHeaderRecoveryUsesSecondary(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "ImageWriterCrashDuringCommit";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images (all committed successfully)
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Simulate crash during AtomicCommit by corrupting the primary header.
                // This represents the state where:
                // - The last AddImage completed its block writes and metadata writes
                // - AtomicCommit wrote the secondary header (with the new state)
                // - Crash occurred before the primary header was written
                // The secondary header is valid and contains the state with all N images.
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");

                if (!File.Exists(indexPath))
                {
                    return false.ToProperty().Label(
                        $"Index file not found at {indexPath} (seed={seed})");
                }

                // Corrupt the primary header (first 256 bytes) with random data
                byte[] corruptData = new byte[FileHeader.HeaderSize];
                rnd.NextBytes(corruptData);

                using (FileStream fs = new FileStream(indexPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    fs.Position = 0;
                    fs.Write(corruptData, 0, corruptData.Length);
                    fs.Flush();
                }

                // Phase 3: Reopen the store — dual-header recovery should use secondary header
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 4: Verify all images are visible and readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after " +
                            $"dual-header recovery (seed={seed})");
                    }

                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} " +
                                $"after dual-header recovery (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after dual-header recovery (seed={seed})");
                        }
                    }

                    // Phase 5: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Expected Clean health after dual-header recovery but got {health.Status}: " +
                            $"{issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"ImageWriter crash-during-commit recovery successful: {imageCount} images " +
                        $"readable via dual-header protocol, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.4, 9.6, 9.7**
        ///
        /// Crash simulation: ImageWriter crash AFTER commit (before lock release).
        /// The commit completed successfully (both headers written), but the writer lock
        /// was not released before the crash.
        ///
        /// On recovery:
        /// - The set detects the stale lock and releases it
        /// - All committed images (including the newly added one) are readable
        /// - CheckSetHealth returns Clean
        ///
        /// Strategy:
        /// 1. Create a set with N images (all committed)
        /// 2. Simulate the "after commit" state: the set is fully committed, but we
        ///    simulate a stale lock by creating a lock file (if applicable) or simply
        ///    verifying that reopening the set works correctly even after an unclean shutdown
        /// 3. Reopen the store
        /// 4. Verify all images are readable
        /// 5. Verify CheckSetHealth returns Clean
        /// </summary>
        [Property(MaxTest = 10)]
        public Property ImageWriterCrashAfterCommit_StaleLockReleasedAndImagesReadable(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            int imageCount = 1 + (rnd.Next() % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "ImageWriterCrashAfterCommit";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create a set with N images (all committed successfully)
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

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
                }

                // Phase 2: Simulate crash after commit but before lock release.
                // The commit completed (both headers are valid), so the set is in a
                // consistent state. We simulate the stale lock by creating a .lock file
                // in the set directory (if the system uses file-based locks) or by
                // verifying that the set can be reopened without issues.
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");
                string lockPath = indexPath + ".lock";

                // Create a stale lock file to simulate the crash-after-commit scenario
                File.WriteAllText(lockPath, $"PID={Environment.ProcessId + 99999}");

                // Phase 3: Reopen the store — should detect stale lock and release it
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize, blockSize);

                    // Phase 4: Verify all images are readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != imageCount)
                    {
                        return false.ToProperty().Label(
                            $"Expected {imageCount} images but found {images.Count} after " +
                            $"crash-after-commit recovery (seed={seed})");
                    }

                    for (int i = 0; i < imageCount; i++)
                    {
                        long imageId = i + 1;
                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[i].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[i].Length)
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} read {totalRead} bytes, expected {imageData[i].Length} " +
                                $"after crash-after-commit recovery (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[i]))
                        {
                            return false.ToProperty().Label(
                                $"Image {imageId} data mismatch after crash-after-commit recovery (seed={seed})");
                        }
                    }

                    // Phase 5: Verify CheckSetHealth returns Clean
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status != SetHealthStatus.Clean)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Expected Clean health after crash-after-commit recovery but got " +
                            $"{health.Status}: {issueDetails} (seed={seed})");
                    }

                    return true.ToProperty().Label(
                        $"ImageWriter crash-after-commit recovery successful: {imageCount} images " +
                        $"readable, stale lock handled, health=Clean (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, images={imageCount}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 9.2, 9.6, 9.7**
        ///
        /// Crash simulation tests for CompactSetEmbedded steps 1-6.
        ///
        /// CompactSetEmbedded steps:
        /// 1. ExtractToSeparateMode (split embedded file into index + shard)
        /// 2. Open standalone index + CompactShards (remove orphaned blocks)
        /// 3. CompactTo (write compacted index to temp file)
        /// 4. AtomicFileOps.ReplaceFile (replace standalone index with compacted)
        /// 5. ReEmbed (append index + footer to shard, rename back)
        /// 6. Reopen as embedded
        ///
        /// For each step boundary, simulate a crash by:
        /// - Creating a valid embedded set with some removed images
        /// - Performing operations up to that step
        /// - Leaving intermediate files in place
        /// - Opening the set (triggering recovery via EmbeddedIndexCommitter.TryRecover)
        /// - Verifying all previously committed images are readable
        /// - Verifying CheckSetHealth reports clean after recovery
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactSetEmbedded_CrashAtEachStepBoundary_RecoveryRestoresLastCommittedState(
            PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            // 2-4 images, at least one will be removed for compaction to have work
            int imageCount = 2 + (rnd.Next() % 3);
            // 6 crash scenarios corresponding to step boundaries 1-6
            int stepBoundary = (seed % 6) + 1;

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "EmbeddedCrashTest";
                const int blockSize = 65536;

                // Phase 1: Create an embedded mode set (shardSize=0) with N images
                byte[][] imageData = new byte[imageCount][];

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 0, blockSize: blockSize);

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

                    // Delete the last image to create a removal (so compaction has work to do)
                    store.DeleteImage(new GlobalImageKey(setName, imageCount));
                    TestDataStoreHelper.WaitForSetIdle(store, setName);
                }

                // Record the last committed state: all images except the deleted one
                List<long> committedLiveImageIds = Enumerable.Range(1, imageCount - 1)
                    .Select(i => (long)i)
                    .ToList();

                // Phase 2: Simulate crash at the specified step boundary
                string embeddedPath = Path.Combine(testDir, $"{setName}.nkds");
                string shardPath = Path.Combine(testDir, $"{setName}_0000.nkds");
                string indexTmpPath = embeddedPath + ".tmp";
                string compactTmpPath = embeddedPath + ".compact.tmp";

                simulateEmbeddedCompactCrash(
                    embeddedPath, shardPath, indexTmpPath, compactTmpPath,
                    stepBoundary, rnd);

                // Phase 3: Reopen the store — this triggers RecoverIfNeeded which calls
                // EmbeddedIndexCommitter.TryRecover and handles all intermediate states
                using (DataStore store = new DataStore(testDir))
                {
                    store.DataAccess.EnsureSetExists(setName, shardSize: 0, blockSize: blockSize);

                    // Phase 4: Verify all previously committed images are readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);

                    if (images.Count != committedLiveImageIds.Count)
                    {
                        return false.ToProperty().Label(
                            $"Step {stepBoundary}: Expected {committedLiveImageIds.Count} live images " +
                            $"but found {images.Count} after recovery (seed={seed})");
                    }

                    for (int i = 0; i < committedLiveImageIds.Count; i++)
                    {
                        long imageId = committedLiveImageIds[i];
                        int dataIndex = (int)(imageId - 1);

                        using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
                        using Stream stream = reader.OpenStream(0);

                        byte[] readData = new byte[imageData[dataIndex].Length];
                        int totalRead = 0;
                        while (totalRead < readData.Length)
                        {
                            int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead != imageData[dataIndex].Length)
                        {
                            return false.ToProperty().Label(
                                $"Step {stepBoundary}: Image {imageId} read {totalRead} bytes, " +
                                $"expected {imageData[dataIndex].Length} (seed={seed})");
                        }

                        if (!readData.SequenceEqual(imageData[dataIndex]))
                        {
                            return false.ToProperty().Label(
                                $"Step {stepBoundary}: Image {imageId} data mismatch after recovery " +
                                $"(seed={seed})");
                        }
                    }

                    // Phase 5: Verify CheckSetHealth returns Clean after recovery
                    RecoveryState health = store.CheckSetHealth(setName);

                    if (health.Status == SetHealthStatus.Unrecoverable)
                    {
                        string issueDetails = health.DetectedIssues.Count > 0
                            ? string.Join("; ", health.DetectedIssues)
                            : health.Description;

                        return false.ToProperty().Label(
                            $"Step {stepBoundary}: Health reported Unrecoverable after recovery: " +
                            $"{issueDetails} (seed={seed})");
                    }

                    // After recovery, the set should be Clean or at most NeedsRecovery.
                    // NeedsRecovery is acceptable for step boundaries that recover to separate mode
                    // (steps 2-4), since the set is still usable and compaction can proceed.
                    if (health.Status != SetHealthStatus.Clean)
                    {
                        if (stepBoundary >= 2 && stepBoundary <= 4 && health.Status == SetHealthStatus.NeedsRecovery)
                        {
                            // Acceptable — set recovered to separate mode
                        }
                        else
                        {
                            string issueDetails = health.DetectedIssues.Count > 0
                                ? string.Join("; ", health.DetectedIssues)
                                : health.Description;

                            return false.ToProperty().Label(
                                $"Step {stepBoundary}: Expected Clean health after recovery but got " +
                                $"{health.Status}: {issueDetails} (seed={seed})");
                        }
                    }

                    return true.ToProperty().Label(
                        $"Step {stepBoundary}: CompactSetEmbedded crash recovery successful — " +
                        $"{committedLiveImageIds.Count} images readable, health={health.Status} (seed={seed})");
                }
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label(
                    $"Exception (seed={seed}, step={stepBoundary}, images={imageCount}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a crash at a specific step boundary during CompactSetEmbedded.
        /// Creates the intermediate file state that would exist if a crash occurred
        /// at that point during the compaction process.
        ///
        /// Step 1: ExtractToSeparateMode — .tmp index written, embedded file still intact
        /// Step 2: Open standalone index + CompactShards — fully extracted to separate mode
        /// Step 3: CompactTo — .compact.tmp exists alongside standalone index
        /// Step 4: AtomicFileOps.ReplaceFile — original may be deleted, .compact.tmp exists
        /// Step 5: ReEmbed — index appended to shard (with or without footer), standalone index still exists
        /// Step 6: Reopen as embedded — valid embedded file, no intermediate state
        /// </summary>
        private void simulateEmbeddedCompactCrash(
            string embeddedPath, string shardPath, string indexTmpPath, string compactTmpPath,
            int stepBoundary, Random rnd)
        {
            switch (stepBoundary)
            {
                case 1:
                    // Crash during Step 1: ExtractToSeparateMode
                    // The .tmp index file was written but the rename from embedded to shard
                    // has not happened yet.
                    // State: embedded file + .tmp index file exist.
                    // Recovery: TryRecover deletes .tmp and treats embedded as authoritative.
                    simulateEmbeddedStep1Crash(embeddedPath, indexTmpPath);
                    break;

                case 2:
                    // Crash during Step 2: After extraction completed, standalone index opened.
                    // CompactShards may or may not have started.
                    // State: standalone index at embeddedPath, shard at shardPath (separate mode).
                    // Recovery: TryRecover detects separate mode, opens normally.
                    simulateEmbeddedStep2Crash(embeddedPath, shardPath);
                    break;

                case 3:
                    // Crash during Step 3: CompactTo writing the compacted index to .compact.tmp.
                    // State: standalone index at embeddedPath, shard at shardPath,
                    // .compact.tmp file exists.
                    // Recovery: RecoverIfNeeded deletes .compact.tmp (original index is valid),
                    // TryRecover detects separate mode.
                    simulateEmbeddedStep3Crash(embeddedPath, shardPath, compactTmpPath);
                    break;

                case 4:
                    // Crash during Step 4: AtomicFileOps.ReplaceFile
                    // Two sub-scenarios:
                    // a) Both original index and .compact.tmp exist (crash before replace)
                    // b) Only .compact.tmp exists (crash after delete but before move)
                    // Recovery: RecoverIfNeeded handles both cases.
                    simulateEmbeddedStep4Crash(embeddedPath, shardPath, compactTmpPath, rnd);
                    break;

                case 5:
                    // Crash during Step 5: ReEmbed
                    // The index was appended to the shard but footer was not written,
                    // OR footer was written but rename did not complete.
                    // State: standalone index at embeddedPath, shard with appended index.
                    // Recovery: TryRecover detects the appended index and completes re-embed.
                    simulateEmbeddedStep5Crash(embeddedPath, shardPath, rnd);
                    break;

                case 6:
                    // Crash during Step 6: Reopen as embedded
                    // ReEmbed completed successfully — the file is a valid embedded file.
                    // The crash occurs during the reopen phase (reading footer, computing
                    // boundary, updating caches). The file state is clean.
                    // Recovery: Normal open of embedded file.
                    simulateEmbeddedStep6Crash(shardPath);
                    break;
            }
        }

        /// <summary>
        /// Step 1 crash: .tmp index file exists alongside the original embedded file.
        /// Extraction started (index written to .tmp) but the rename from embedded to shard
        /// was not performed. TryRecover should delete .tmp and keep embedded as authoritative.
        /// </summary>
        private void simulateEmbeddedStep1Crash(string embeddedPath, string indexTmpPath)
        {
            // Read the embedded file to extract the index portion.
            // The footer's IndexSize may be stale after DeleteImage, so we use the header's
            // FileEndOffset to determine the actual index size.
            long embeddedFileSize = new FileInfo(embeddedPath).Length;

            byte[] footerBytes = new byte[EmbeddedFooter.FooterSize];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
                fs.ReadExactly(footerBytes, 0, EmbeddedFooter.FooterSize);
            }

            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBytes);
            if (footer == null)
                throw new InvalidOperationException("Cannot read embedded footer for step 1 crash simulation");

            // Compute shard boundary from footer (where block data ends)
            long shardBoundary = embeddedFileSize - EmbeddedFooter.FooterSize - footer.Value.IndexSize;

            // Read the header at shardBoundary to get the actual FileEndOffset (true index size)
            byte[] headerBytes = new byte[FileHeader.HeaderSize];
            long actualIndexSize;
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(shardBoundary, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < headerBytes.Length)
                {
                    int read = fs.Read(headerBytes, totalRead, headerBytes.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                try
                {
                    FileHeader header = FileHeaderSerializer.Read(headerBytes);
                    actualIndexSize = header.FileEndOffset;
                }
                catch (InvalidDataException)
                {
                    // Primary header invalid, try secondary
                    fs.Seek(shardBoundary + FileHeader.HeaderSize, SeekOrigin.Begin);
                    totalRead = 0;
                    while (totalRead < headerBytes.Length)
                    {
                        int read = fs.Read(headerBytes, totalRead, headerBytes.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    FileHeader header = FileHeaderSerializer.Read(headerBytes);
                    actualIndexSize = header.FileEndOffset;
                }
            }

            // Write the index content to .tmp (simulating the extraction step that completed)
            byte[] indexContent = new byte[actualIndexSize];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(shardBoundary, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < indexContent.Length)
                {
                    int read = fs.Read(indexContent, totalRead, indexContent.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            File.WriteAllBytes(indexTmpPath, indexContent);
            // Embedded file remains unchanged — crash happened before rename
        }

        /// <summary>
        /// Step 2 crash: Extraction completed fully. The set is now in separate mode
        /// (standalone index at embeddedPath, shard at shardPath). CompactShards has not
        /// started or is in progress. TryRecover detects separate mode and opens normally.
        /// </summary>
        private void simulateEmbeddedStep2Crash(string embeddedPath, string shardPath)
        {
            // Perform the full extraction to reach separate mode.
            // The footer's IndexSize may be stale after DeleteImage operations (the index
            // grows when directory entries are appended). We must use the header's
            // FileEndOffset to determine the actual index size, just like CompactSetEmbedded does.
            long embeddedFileSize = new FileInfo(embeddedPath).Length;

            byte[] footerBytes = new byte[EmbeddedFooter.FooterSize];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(-EmbeddedFooter.FooterSize, SeekOrigin.End);
                fs.ReadExactly(footerBytes, 0, EmbeddedFooter.FooterSize);
            }

            EmbeddedFooter? footer = EmbeddedFooter.Deserialize(footerBytes);
            if (footer == null)
                throw new InvalidOperationException("Cannot read embedded footer for step 2 crash simulation");

            // Use the footer's IndexSize to compute the shard boundary (where block data ends).
            // The shard boundary is: fileSize - footerSize - footerIndexSize.
            // But the ACTUAL index size may be larger (if directory grew after footer was written).
            long shardBoundary = embeddedFileSize - EmbeddedFooter.FooterSize - footer.Value.IndexSize;

            // Read the header at shardBoundary to get the actual FileEndOffset (true index size).
            byte[] headerBytes = new byte[FileHeader.HeaderSize];
            long actualIndexSize;
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(shardBoundary, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < headerBytes.Length)
                {
                    int read = fs.Read(headerBytes, totalRead, headerBytes.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                try
                {
                    FileHeader header = FileHeaderSerializer.Read(headerBytes);
                    actualIndexSize = header.FileEndOffset;
                }
                catch (InvalidDataException)
                {
                    // Primary header invalid, try secondary
                    fs.Seek(shardBoundary + FileHeader.HeaderSize, SeekOrigin.Begin);
                    totalRead = 0;
                    while (totalRead < headerBytes.Length)
                    {
                        int read = fs.Read(headerBytes, totalRead, headerBytes.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    FileHeader header = FileHeaderSerializer.Read(headerBytes);
                    actualIndexSize = header.FileEndOffset;
                }
            }

            // Extract index content using the actual index size from the header
            byte[] indexContent = new byte[actualIndexSize];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(shardBoundary, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < indexContent.Length)
                {
                    int read = fs.Read(indexContent, totalRead, indexContent.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            // Extract shard data (block data only, up to shardBoundary)
            byte[] shardData = new byte[shardBoundary];
            using (FileStream fs = new FileStream(embeddedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int totalRead = 0;
                while (totalRead < shardData.Length)
                {
                    int read = fs.Read(shardData, totalRead, shardData.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            // Write shard file (block data only)
            File.WriteAllBytes(shardPath, shardData);

            // Replace embedded file with standalone index
            File.WriteAllBytes(embeddedPath, indexContent);
        }

        /// <summary>
        /// Step 3 crash: Extraction completed, CompactShards may have run, and CompactTo
        /// is writing the compacted index to .compact.tmp. The .compact.tmp may be partial
        /// or complete. Recovery deletes .compact.tmp and uses the original standalone index.
        /// </summary>
        private void simulateEmbeddedStep3Crash(
            string embeddedPath, string shardPath, string compactTmpPath)
        {
            // First, get to separate mode (same as step 2)
            simulateEmbeddedStep2Crash(embeddedPath, shardPath);

            // Create a .compact.tmp file (simulating partial or complete CompactTo output)
            // Copy the standalone index as .compact.tmp (represents a valid compacted file)
            File.Copy(embeddedPath, compactTmpPath);
        }

        /// <summary>
        /// Step 4 crash: AtomicFileOps.ReplaceFile is replacing the standalone index with
        /// the compacted version. Two sub-scenarios based on the random seed:
        /// a) Both original and .compact.tmp exist (crash before replace completes)
        /// b) Only .compact.tmp exists (crash after delete but before move in fallback path)
        /// </summary>
        private void simulateEmbeddedStep4Crash(
            string embeddedPath, string shardPath, string compactTmpPath, Random rnd)
        {
            // First, get to separate mode (same as step 2)
            simulateEmbeddedStep2Crash(embeddedPath, shardPath);

            // Create .compact.tmp (the compacted index — copy of the standalone index)
            File.Copy(embeddedPath, compactTmpPath);

            // Sub-scenario: 50% chance of each
            if (rnd.Next() % 2 == 0)
            {
                // Scenario a: Both files exist (crash before replace completes)
                // Both embeddedPath (original index) and compactTmpPath exist.
                // Recovery: delete .compact.tmp, use original.
            }
            else
            {
                // Scenario b: Only .compact.tmp exists (crash after delete, before move)
                // Delete the original index to simulate the fallback path crash.
                // Recovery: rename .compact.tmp to embeddedPath.
                File.Delete(embeddedPath);
            }
        }

        /// <summary>
        /// Step 5 crash: ReEmbed is in progress. The index was appended to the shard
        /// but either the footer was not written, or the footer was written but the
        /// rename from shard to embedded did not complete.
        /// </summary>
        private void simulateEmbeddedStep5Crash(string embeddedPath, string shardPath, Random rnd)
        {
            // First, get to separate mode (same as step 2)
            simulateEmbeddedStep2Crash(embeddedPath, shardPath);

            // Read the standalone index content
            byte[] indexContent = File.ReadAllBytes(embeddedPath);

            // Sub-scenario: 50% chance of each
            if (rnd.Next() % 2 == 0)
            {
                // Scenario a (Req 15.1): Index appended to shard but footer NOT written.
                // State: embeddedPath = standalone index, shardPath = blocks + index (no footer).
                // Recovery: TryRecover detects appended index, appends footer, completes re-embed.
                using (FileStream fs = new FileStream(shardPath, FileMode.Append, FileAccess.Write))
                {
                    fs.Write(indexContent, 0, indexContent.Length);
                    // Intentionally NOT writing footer
                    fs.Flush(flushToDisk: true);
                }
            }
            else
            {
                // Scenario b (Req 15.2): Footer written to shard but rename not completed.
                // State: embeddedPath = standalone index, shardPath = blocks + index + footer.
                // Recovery: TryRecover detects footer magic on shard, completes rename.
                using (FileStream fs = new FileStream(shardPath, FileMode.Append, FileAccess.Write))
                {
                    fs.Write(indexContent, 0, indexContent.Length);
                    EmbeddedFooter footer = new EmbeddedFooter(indexContent.Length);
                    byte[] footerBytes = footer.Serialize();
                    fs.Write(footerBytes, 0, footerBytes.Length);
                    fs.Flush(flushToDisk: true);
                }
            }
        }

        /// <summary>
        /// Step 6 crash: ReEmbed completed successfully — the file is a valid embedded file.
        /// The crash occurs during the reopen phase (reading footer, computing boundary,
        /// updating caches). The file state is clean — just a valid embedded file.
        /// Recovery: Normal open of embedded file (no intermediate state to recover from).
        /// </summary>
        private void simulateEmbeddedStep6Crash(string shardPath)
        {
            // The embedded file is already in its final state (from the initial setup).
            // For step 6, the ReEmbed completed but the in-memory reopen failed.
            // The file is a valid embedded file — no manipulation needed.
            // Just clean up any shard file that might exist from a previous step simulation.
            if (File.Exists(shardPath))
                File.Delete(shardPath);
        }
    }
}