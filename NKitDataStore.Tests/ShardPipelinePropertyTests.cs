using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using Property = FsCheck.Property;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Shard pipeline property tests for the compact robustness refactor.
    ///
    /// **Validates: Requirements 5.3, 5.6**
    ///
    /// Property 11: GatherReferencedKeys Correctness
    /// Property 14: Pipeline No-Op for Clean Sets
    /// </summary>
    public class ShardPipelinePropertyTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public ShardPipelinePropertyTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitShardPipeline_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// Captures a byte-for-byte snapshot of shard files for a set.
        /// </summary>
        private Dictionary<string, byte[]> SnapshotShardFiles(string testDir, string setName)
        {
            Dictionary<string, byte[]> snapshot = new Dictionary<string, byte[]>();
            foreach (string shardFile in Directory.GetFiles(testDir, $"{setName}_*.nkds"))
            {
                snapshot[Path.GetFileName(shardFile)] = File.ReadAllBytes(shardFile);
            }
            return snapshot;
        }

        /// <summary>
        /// **Validates: Requirements 5.3**
        ///
        /// Property 11: GatherReferencedKeys Correctness
        ///
        /// For any index file with a mix of live and removed images, GatherReferencedKeys
        /// SHALL return exactly the set of BlockKeys referenced by live (non-removed) images
        /// and no others.
        ///
        /// Strategy:
        /// 1. Create a set with N images (2-5), each with random block data
        /// 2. Remove a random non-empty subset of images
        /// 3. Close the store to flush all data
        /// 4. Open the BinaryIndexFile directly
        /// 5. Get directory entries, separate into live and removed
        /// 6. Call GatherReferencedKeys with the live images
        /// 7. Independently compute expected keys by reading block maps for live images
        /// 8. Verify the returned set equals exactly the expected keys from live images
        /// </summary>
        [Property(MaxTest = 10)]
        public Property GatherReferencedKeys_ReturnsExactlyLiveImageBlockKeys(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);
            int imageCount = 2 + (seed % 4); // 2 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "GatherKeysTest";
                const int blockSize = 65536;
                const long shardSize = 50L * 1024 * 1024 * 1024;

                // Phase 1: Create set with images
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        int blocks = 1 + (rnd.Next() % 3); // 1-3 blocks per image
                        byte[] imageData = new byte[blockSize * blocks];
                        rnd.NextBytes(imageData);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData, BlockType.File);
                            writer.FinalizeImage(imageData.Length, 0, 0);
                        }

                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }

                    // Phase 2: Remove a random non-empty subset of images
                    // Ensure at least one image remains live and at least one is removed
                    int removeCount = 1 + (rnd.Next() % (imageCount - 1)); // 1 to imageCount-1
                    List<int> allImageIds = Enumerable.Range(1, imageCount).ToList();
                    List<int> toRemove = allImageIds.OrderBy(_ => rnd.Next()).Take(removeCount).ToList();

                    foreach (int imageId in toRemove)
                    {
                        store.DeleteImage(new GlobalImageKey(setName, imageId));
                    }
                }

                // Phase 3: Open the BinaryIndexFile directly and test GatherReferencedKeys
                string indexPath = Path.Combine(testDir, $"{setName}.nkds");
                using BinaryIndexFile indexFile = BinaryIndexFile.Open(indexPath);

                ImageDirectory directory = indexFile.GetDirectory();
                List<ImageDirectoryEntry> liveImages = directory.GetNonRemovedEntries().ToList();
                List<ImageDirectoryEntry> allImages = directory.GetAllEntries().ToList();
                List<ImageDirectoryEntry> removedImages = allImages.Where(e => e.Removed).ToList();

                // Sanity check: we have both live and removed images
                if (liveImages.Count == 0 || removedImages.Count == 0)
                {
                    return false.ToProperty().Label(
                        $"Setup error: liveImages={liveImages.Count}, removedImages={removedImages.Count} (seed={seed})");
                }

                // Phase 4: Call GatherReferencedKeys
                HashSet<BlockKey> result = BinaryDataStoreDataAccess.GatherReferencedKeys(indexFile, liveImages);

                // Phase 5: Independently compute expected keys from live images
                HashSet<BlockKey> expectedKeys = new HashSet<BlockKey>();
                foreach (ImageDirectoryEntry image in liveImages)
                {
                    (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = indexFile.ReadImageBlockMap(image.ImageId);
                    foreach (BlockKey key in blockLocations.Keys)
                        expectedKeys.Add(key);
                }

                // Phase 6: Compute keys from removed images (for exclusion check)
                HashSet<BlockKey> removedOnlyKeys = new HashSet<BlockKey>();
                foreach (ImageDirectoryEntry image in removedImages)
                {
                    (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = indexFile.ReadImageBlockMap(image.ImageId);
                    foreach (BlockKey key in blockLocations.Keys)
                    {
                        // Only track keys that are exclusively in removed images
                        if (!expectedKeys.Contains(key))
                            removedOnlyKeys.Add(key);
                    }
                }

                // Verify: result contains exactly the expected keys
                bool setsEqual = result.SetEquals(expectedKeys);

                // Verify: result does not contain any keys exclusive to removed images
                bool noRemovedOnlyKeys = !result.Any(k => removedOnlyKeys.Contains(k));

                if (!setsEqual)
                {
                    List<BlockKey> missing = expectedKeys.Except(result).ToList();
                    List<BlockKey> extra = result.Except(expectedKeys).ToList();
                    return false.ToProperty().Label(
                        $"Sets not equal: missing={missing.Count}, extra={extra.Count}, " +
                        $"expected={expectedKeys.Count}, actual={result.Count} (seed={seed})");
                }

                if (!noRemovedOnlyKeys)
                {
                    List<BlockKey> leaked = result.Intersect(removedOnlyKeys).ToList();
                    return false.ToProperty().Label(
                        $"Result contains {leaked.Count} keys exclusive to removed images (seed={seed})");
                }

                return true.ToProperty().Label(
                    $"GatherReferencedKeys correct: {liveImages.Count} live, {removedImages.Count} removed, " +
                    $"{result.Count} keys returned, {removedOnlyKeys.Count} removed-only keys excluded (seed={seed})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 14: Pipeline No-Op for Clean Sets
        ///
        /// For any set with no removed images and no orphaned data, executing the shard
        /// pipeline SHALL produce no file modifications and no AtomicCommit.
        ///
        /// The shard pipeline (CompactShards) is the sequence: GatherReferencedKeys →
        /// BuildChunkList → GroupChunksByShard → CompactShard → DeleteEmptyShards →
        /// UpdateBlockIndex → UpdateBlockMaps → UpdateMetadataSections → AtomicCommit.
        /// When no blocks are unreferenced and no offsets changed, the pipeline exits
        /// early without modifying any shard files and without calling AtomicCommit.
        ///
        /// Strategy:
        /// 1. Create a set with N images (all live, none removed)
        /// 2. Close the store to flush all data and release file handles
        /// 3. Record byte-for-byte content of all shard files
        /// 4. Reopen the store and call CompactSet (which invokes the pipeline)
        /// 5. Close the store again
        /// 6. Verify all shard files are byte-for-byte identical (pipeline produced no changes)
        /// 7. Verify no shard files were created or deleted
        ///
        /// Note: CompactSetSeparate also calls CompactTo (index compaction) which may rewrite
        /// the index file. This is outside the shard pipeline scope and is not tested here.
        /// The property specifically validates that the shard pipeline's early-exit path
        /// leaves shard data untouched when there is nothing to compact.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property Pipeline_NoRemovedImages_ProducesNoFileModifications(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            int imageCount = 1 + (seed % 5); // 1 to 5 images

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "PipelineNoOpTest";
                const int blockSize = 65536;
                Random rnd = new Random(seed);

                // Phase 1: Create set with images (all live, none removed), then close store
                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: blockSize);

                    for (int i = 0; i < imageCount; i++)
                    {
                        int blocks = 1 + (rnd.Next() % 3); // 1-3 blocks per image
                        byte[] imageData = new byte[blockSize * blocks];
                        rnd.NextBytes(imageData);

                        using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                        {
                            writer.WriteData(0, imageData, BlockType.File);
                            writer.FinalizeImage(imageData.Length, 0, 0);
                        }

                        TestDataStoreHelper.WaitForSetIdle(store, setName);
                    }
                }

                // Phase 2: Snapshot shard files before compaction (store is closed, no file locks)
                Dictionary<string, byte[]> shardsBefore = SnapshotShardFiles(testDir, setName);

                // Phase 3: Reopen store, compact (no removals), then close
                using (DataStore store = new DataStore(testDir))
                {
                    store.CompactSet(setName);
                }

                // Phase 4: Snapshot shard files after compaction (store closed)
                Dictionary<string, byte[]> shardsAfter = SnapshotShardFiles(testDir, setName);

                // Verify: same number of shard files (no shards created or deleted)
                if (shardsBefore.Count != shardsAfter.Count)
                {
                    return false.ToProperty().Label(
                        $"Shard file count changed: before={shardsBefore.Count}, after={shardsAfter.Count} (seed={seed}, images={imageCount})");
                }

                // Verify: all shard files are byte-for-byte identical
                foreach (KeyValuePair<string, byte[]> kvp in shardsBefore)
                {
                    if (!shardsAfter.TryGetValue(kvp.Key, out byte[] afterContent))
                    {
                        return false.ToProperty().Label(
                            $"Shard file '{kvp.Key}' disappeared after compact (seed={seed})");
                    }

                    if (!kvp.Value.SequenceEqual(afterContent))
                    {
                        return false.ToProperty().Label(
                            $"Shard file '{kvp.Key}' content changed: before={kvp.Value.Length} bytes, after={afterContent.Length} bytes (seed={seed})");
                    }
                }

                // Verify: no new shard files appeared
                foreach (KeyValuePair<string, byte[]> kvp in shardsAfter)
                {
                    if (!shardsBefore.ContainsKey(kvp.Key))
                    {
                        return false.ToProperty().Label(
                            $"New shard file '{kvp.Key}' appeared after compact (seed={seed})");
                    }
                }

                return true.ToProperty().Label(
                    $"Pipeline no-op confirmed: {imageCount} images, {shardsBefore.Count} shard files unchanged (seed={seed})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        /// <summary>
        /// **Validates: Requirements 5.5**
        ///
        /// Property 13: CompactShard Data Preservation
        ///
        /// For any shard file with a mix of live and dead chunks, CompactShard SHALL produce
        /// an output containing all live chunk data at contiguous offsets with byte-for-byte
        /// identical content, and the output size SHALL equal the sum of live chunk sizes.
        ///
        /// Strategy:
        /// 1. Generate arbitrary chunk data (random byte arrays of varying sizes)
        /// 2. Create a shard file with interleaved live and dead chunks (dead chunks = gaps)
        /// 3. Build a list of DataChunks representing only the live chunks
        /// 4. Call CompactShard(shardPath, liveChunks)
        /// 5. Verify: the output shard file size equals the sum of live chunk sizes
        /// 6. Verify: each live chunk's data in the output matches the original byte-for-byte
        /// 7. Verify: chunks are at contiguous offsets (no gaps)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactShard_PreservesLiveChunkData()
        {
            // Generator: produce a list of chunks where each chunk is either live or dead,
            // with random byte content and varying sizes (1 to 4096 bytes).
            Gen<(byte[] Data, bool IsLive)[]> gen = from chunkCount in Gen.Choose(2, 20)
                                                    from chunkSpecs in Gen.ArrayOf(GenChunkSpec(), chunkCount)
                                                    select chunkSpecs;

            return Prop.ForAll(gen.ToArbitrary(), (chunkSpecs) =>
            {
                string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDir);

                try
                {
                    string shardPath = Path.Combine(testDir, "TestSet_0001.nkds");
                    const int fileId = 1;

                    // Write the shard file with interleaved live and dead chunks
                    List<DataChunk> liveChunks = new List<DataChunk>();
                    List<byte[]> liveChunkData = new List<byte[]>(); // original data for verification
                    long currentOffset = 0;

                    using (FileStream fs = new FileStream(shardPath, FileMode.Create, FileAccess.Write))
                    {
                        for (int i = 0; i < chunkSpecs.Length; i++)
                        {
                            (byte[] data, bool isLive) = chunkSpecs[i];
                            long chunkOffset = currentOffset;

                            fs.Write(data, 0, data.Length);

                            if (isLive)
                            {
                                // Create a DataChunk for this live chunk
                                BlockKey blockKey = new BlockKey((ulong)((i * 1000) + 1), (uint)((i * 100) + 1));
                                liveChunks.Add(new DataChunk(
                                    IsBlock: true,
                                    BlockKey: blockKey,
                                    BlockEntryIndex: i,
                                    ImageId: 1,
                                    FileName: "",
                                    FileId: fileId,
                                    Offset: chunkOffset,
                                    Size: data.Length));
                                liveChunkData.Add(data);
                            }

                            currentOffset += data.Length;
                        }
                    }

                    // If there are no live chunks, CompactShard returns WasCompacted=false
                    // and doesn't modify the file. Skip this degenerate case.
                    if (liveChunks.Count == 0)
                        return true.ToProperty().Label("No live chunks — trivial case");

                    // Check if compaction is actually needed
                    long expectedOutputSize = liveChunks.Sum(c => c.Size);
                    long actualFileSize = new FileInfo(shardPath).Length;
                    bool hasDeadChunks = chunkSpecs.Any(c => !c.IsLive);

                    if (!hasDeadChunks && actualFileSize == expectedOutputSize)
                    {
                        // All chunks are live and contiguous — verify CompactShard returns no-op
                        ShardCompactResult noOpResult = BinaryDataStoreDataAccess.CompactShard(shardPath, liveChunks);
                        if (noOpResult.WasCompacted)
                            return false.ToProperty().Label("CompactShard compacted when no compaction was needed");
                        return true.ToProperty().Label("All live, contiguous — no-op confirmed");
                    }

                    // Call CompactShard
                    ShardCompactResult result = BinaryDataStoreDataAccess.CompactShard(shardPath, liveChunks);

                    // Verify: compaction occurred
                    if (!result.WasCompacted)
                        return false.ToProperty().Label(
                            $"CompactShard did not compact despite dead chunks present (liveChunks={liveChunks.Count}, totalChunks={chunkSpecs.Length})");

                    // Verify 1: output file size equals sum of live chunk sizes
                    long outputSize = new FileInfo(shardPath).Length;
                    if (outputSize != expectedOutputSize)
                        return false.ToProperty().Label(
                            $"Output size mismatch: expected={expectedOutputSize}, actual={outputSize}");

                    // Verify 2 & 3: each live chunk's data is byte-for-byte identical at contiguous offsets
                    byte[] outputData = File.ReadAllBytes(shardPath);
                    long verifyOffset = 0;

                    // Sort live chunks by their original offset to match the order CompactShard uses
                    List<int> sortedIndices = liveChunks
                        .Select((chunk, idx) => (chunk, idx))
                        .OrderBy(x => x.chunk.Offset)
                        .Select(x => x.idx)
                        .ToList();

                    for (int si = 0; si < sortedIndices.Count; si++)
                    {
                        int originalIdx = sortedIndices[si];
                        byte[] expectedData = liveChunkData[originalIdx];

                        // Verify contiguous: chunk starts exactly at verifyOffset
                        if (verifyOffset + expectedData.Length > outputData.Length)
                            return false.ToProperty().Label(
                                $"Output too short: need {verifyOffset + expectedData.Length} bytes but only have {outputData.Length}");

                        // Verify byte-for-byte match
                        for (int b = 0; b < expectedData.Length; b++)
                        {
                            if (outputData[verifyOffset + b] != expectedData[b])
                                return false.ToProperty().Label(
                                    $"Data mismatch at output offset {verifyOffset + b} (chunk {si}, byte {b}): expected=0x{expectedData[b]:X2}, actual=0x{outputData[verifyOffset + b]:X2}");
                        }

                        verifyOffset += expectedData.Length;
                    }

                    // Final contiguity check: we consumed exactly the entire output
                    if (verifyOffset != outputSize)
                        return false.ToProperty().Label(
                            $"Contiguity violation: verified {verifyOffset} bytes but output is {outputSize} bytes");

                    return true.ToProperty().Label(
                        $"Data preserved: {liveChunks.Count} live chunks, {chunkSpecs.Length - liveChunks.Count} dead chunks, output={outputSize} bytes");
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Exception: {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        /// <summary>
        /// Generates a chunk specification: random byte data (1-4096 bytes) and whether it's live or dead.
        /// </summary>
        private static Gen<(byte[] Data, bool IsLive)> GenChunkSpec()
        {
            return from size in Gen.Choose(1, 4096)
                   from data in Gen.ArrayOf(Gen.Choose(0, 255).Select(i => (byte)i), size)
                   from isLive in Gen.Frequency((3, Gen.Constant(true)), (1, Gen.Constant(false)))
                   select (data, isLive);
        }

        /// <summary>
        /// **Validates: Requirements 5.4**
        ///
        /// Property 12: BuildChunkList Classification
        ///
        /// For any block index and set of referenced keys, BuildChunkList SHALL classify
        /// every entry as referenced if and only if its key is in the referenced set, and
        /// unreferenced otherwise.
        ///
        /// Strategy:
        /// 1. Generate arbitrary BlockIndexEntry arrays with random keys, file IDs, offsets, sizes
        /// 2. Generate a subset of those keys as the "referenced" set
        /// 3. Call BuildChunkList with empty liveImages (isolating block classification logic)
        /// 4. Verify: every entry whose key is in referencedKeys appears in ReferencedChunks
        /// 5. Verify: every entry whose key is NOT in referencedKeys appears in UnreferencedBlockIndices
        /// 6. Verify: no entry appears in both lists (partition property)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property BuildChunkList_ClassifiesEntriesCorrectly()
        {
            // Generator: create random block entries and a subset of their keys as referenced
            Gen<(BlockIndexEntry[] entries, HashSet<BlockKey> referencedSubset)> gen = from entryCount in Gen.Choose(0, 30)
                                                                                       from entries in Gen.ArrayOf(GenBlockIndexEntry(), entryCount)
                                                                                       from referencedSubset in GenReferencedSubset(entries)
                                                                                       select (entries, referencedSubset);

            return Prop.ForAll(gen.ToArbitrary(), (tuple) =>
            {
                BlockIndexEntry[] allEntries = tuple.entries;
                HashSet<BlockKey> referencedKeys = tuple.referencedSubset;

                // Call BuildChunkList with empty liveImages to isolate block classification
                ChunkClassification result = BinaryDataStoreDataAccess.BuildChunkList(
                    allEntries,
                    referencedKeys,
                    new List<ImageDirectoryEntry>(),
                    indexFile: null!);

                // Extract the block entry indices that ended up in ReferencedChunks
                HashSet<int> referencedIndices = new HashSet<int>(
                    result.ReferencedChunks
                        .Where(c => c.IsBlock)
                        .Select(c => c.BlockEntryIndex));

                HashSet<int> unreferencedIndices = new HashSet<int>(result.UnreferencedBlockIndices);

                // Property 1: Every entry whose key is in referencedKeys is in ReferencedChunks
                for (int i = 0; i < allEntries.Length; i++)
                {
                    if (referencedKeys.Contains(allEntries[i].Key))
                    {
                        if (!referencedIndices.Contains(i))
                            return false.ToProperty().Label(
                                $"Entry at index {i} with key {allEntries[i].Key} is in referencedKeys but NOT in ReferencedChunks");
                    }
                }

                // Property 2: Every entry whose key is NOT in referencedKeys is in UnreferencedBlockIndices
                for (int i = 0; i < allEntries.Length; i++)
                {
                    if (!referencedKeys.Contains(allEntries[i].Key))
                    {
                        if (!unreferencedIndices.Contains(i))
                            return false.ToProperty().Label(
                                $"Entry at index {i} with key {allEntries[i].Key} is NOT in referencedKeys but NOT in UnreferencedBlockIndices");
                    }
                }

                // Property 3: No entry appears in both lists (partition)
                List<int> overlap = referencedIndices.Intersect(unreferencedIndices).ToList();
                if (overlap.Any())
                    return false.ToProperty().Label(
                        $"Entry indices {string.Join(", ", overlap)} appear in BOTH referenced and unreferenced lists");

                // Property 4: Total classified entries equals input count
                int totalClassified = referencedIndices.Count + unreferencedIndices.Count;
                if (totalClassified != allEntries.Length)
                    return false.ToProperty().Label(
                        $"Total classified ({totalClassified}) != input count ({allEntries.Length})");

                return true.ToProperty().Label(
                    $"Classification correct: {allEntries.Length} entries, {referencedIndices.Count} referenced, {unreferencedIndices.Count} unreferenced");
            });
        }

        /// <summary>
        /// Generates a random BlockIndexEntry with arbitrary key, file ID, offset, and size.
        /// </summary>
        private static Gen<BlockIndexEntry> GenBlockIndexEntry()
        {
            return from xxHash in Gen.Choose(0, int.MaxValue).Select(v => (ulong)v)
                   from crc in Gen.Choose(0, int.MaxValue).Select(v => (uint)v)
                   from fileId in Gen.Choose(1, 10)
                   from offset in Gen.Choose(0, 1_000_000).Select(v => (long)v)
                   from size in Gen.Choose(1, 65536)
                   select new BlockIndexEntry
                   {
                       Key = new BlockKey(xxHash, crc),
                       FileId = fileId,
                       Offset = offset,
                       Size = size
                   };
        }

        /// <summary>
        /// Given an array of block entries, generates a HashSet containing a random subset of their keys.
        /// Also may include keys not present in the entries (to test that extra referenced keys don't cause issues).
        /// </summary>
        private static Gen<HashSet<BlockKey>> GenReferencedSubset(BlockIndexEntry[] entries)
        {
            if (entries.Length == 0)
                return Gen.Constant(new HashSet<BlockKey>());

            // For each entry, randomly decide if its key is "referenced"
            return Gen.ArrayOf(Gen.Elements(true, false), entries.Length)
                .Select(flags =>
                {
                    HashSet<BlockKey> set = new HashSet<BlockKey>();
                    for (int i = 0; i < entries.Length; i++)
                    {
                        if (flags[i])
                            set.Add(entries[i].Key);
                    }
                    return set;
                });
        }

        /// <summary>
        /// **Validates: Requirements 5.5**
        ///
        /// Property 13: CompactShard Data Preservation
        ///
        /// For any shard file with a mix of live and dead chunks, CompactShard SHALL produce
        /// an output containing all live chunk data at contiguous offsets with byte-for-byte
        /// identical content, and the output size SHALL equal the sum of live chunk sizes.
        ///
        /// Strategy:
        /// 1. Generate arbitrary shard content (random bytes for live chunks and dead chunks)
        /// 2. Create a temp shard file with interleaved live and dead data
        /// 3. Build a list of DataChunks representing the live portions
        /// 4. Call CompactShard(shardPath, liveChunks)
        /// 5. Verify: the output file size equals the sum of live chunk sizes
        /// 6. Verify: each live chunk's data is byte-for-byte identical in the compacted file
        ///    at its new contiguous offset
        /// 7. Verify: the result's offset mappings are correct
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactShard_PreservesAllLiveChunkData(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                Random rnd = new Random(seed);

                // Generate chunk layout: interleaved live and dead chunks
                int totalChunks = 2 + (Math.Abs(rnd.Next()) % 8); // 2 to 9 total chunks
                int liveCount = 1 + (Math.Abs(rnd.Next()) % (totalChunks - 1)); // At least 1 live, leave room for dead
                int deadCount = totalChunks - liveCount;

                // Ensure we have at least 1 dead chunk so compaction actually occurs
                if (deadCount == 0)
                {
                    deadCount = 1;
                    totalChunks = liveCount + deadCount;
                }

                // Generate chunk sizes (between 64 and 4096 bytes each)
                int[] chunkSizes = new int[totalChunks];
                for (int i = 0; i < totalChunks; i++)
                    chunkSizes[i] = 64 + (Math.Abs(rnd.Next()) % 4033); // 64 to 4096

                // Decide which chunks are live vs dead (interleaved randomly)
                bool[] isLive = new bool[totalChunks];
                int liveAssigned = 0;
                int deadAssigned = 0;
                for (int i = 0; i < totalChunks; i++)
                {
                    int remaining = totalChunks - i;
                    int liveRemaining = liveCount - liveAssigned;
                    int deadRemaining = deadCount - deadAssigned;

                    if (deadRemaining == 0 || (liveRemaining > 0 && rnd.Next(remaining) < liveRemaining))
                    {
                        isLive[i] = true;
                        liveAssigned++;
                    }
                    else
                    {
                        isLive[i] = false;
                        deadAssigned++;
                    }
                }

                // Generate random data for all chunks and write the shard file
                byte[][] chunkData = new byte[totalChunks][];
                const int shardFileId = 1;
                string shardPath = Path.Combine(testDir, $"TestSet_{shardFileId:D4}.nkds");

                using (FileStream fs = new FileStream(shardPath, FileMode.Create, FileAccess.Write))
                {
                    for (int i = 0; i < totalChunks; i++)
                    {
                        chunkData[i] = new byte[chunkSizes[i]];
                        rnd.NextBytes(chunkData[i]);
                        fs.Write(chunkData[i], 0, chunkData[i].Length);
                    }
                }

                // Build DataChunk list for live portions only
                List<DataChunk> liveChunks = new List<DataChunk>();
                List<byte[]> liveChunkOriginalData = new List<byte[]>();
                long offset = 0;
                int blockEntryIdx = 0;

                for (int i = 0; i < totalChunks; i++)
                {
                    if (isLive[i])
                    {
                        BlockKey blockKey = new BlockKey((ulong)((seed * 1000L) + i), (uint)((i * 7) + 3));
                        liveChunks.Add(new DataChunk(
                            IsBlock: true,
                            BlockKey: blockKey,
                            BlockEntryIndex: blockEntryIdx,
                            ImageId: 1,
                            FileName: "test.iso",
                            FileId: shardFileId,
                            Offset: offset,
                            Size: chunkSizes[i]));
                        liveChunkOriginalData.Add(chunkData[i]);
                        blockEntryIdx++;
                    }
                    offset += chunkSizes[i];
                }

                long expectedOutputSize = liveChunks.Sum(c => c.Size);

                // Call CompactShard
                ShardCompactResult result = BinaryDataStoreDataAccess.CompactShard(shardPath, liveChunks);

                // Verify: compaction occurred (since we have dead chunks creating gaps)
                if (!result.WasCompacted)
                {
                    return false.ToProperty().Label(
                        $"CompactShard did not compact despite dead chunks (seed={seed}, live={liveCount}, dead={deadCount})");
                }

                // Verify: output file size equals sum of live chunk sizes
                long actualOutputSize = new FileInfo(shardPath).Length;
                if (actualOutputSize != expectedOutputSize)
                {
                    return false.ToProperty().Label(
                        $"Output size mismatch: expected={expectedOutputSize}, actual={actualOutputSize} (seed={seed})");
                }

                // Verify: each live chunk's data is byte-for-byte identical at contiguous offsets
                // CompactShard sorts chunks by original offset, so the output order matches
                // the order of liveChunks sorted by offset
                List<int> sortedLiveIndices = liveChunks
                    .Select((chunk, idx) => (chunk, idx))
                    .OrderBy(x => x.chunk.Offset)
                    .Select(x => x.idx)
                    .ToList();

                byte[] compactedContent = File.ReadAllBytes(shardPath);
                long readOffset = 0;
                for (int si = 0; si < sortedLiveIndices.Count; si++)
                {
                    int originalIdx = sortedLiveIndices[si];
                    byte[] expectedData = liveChunkOriginalData[originalIdx];

                    for (int b = 0; b < expectedData.Length; b++)
                    {
                        if (compactedContent[readOffset + b] != expectedData[b])
                        {
                            return false.ToProperty().Label(
                                $"Data mismatch at sorted chunk {si} (original idx {originalIdx}), byte {b}: " +
                                $"expected=0x{expectedData[b]:X2}, actual=0x{compactedContent[readOffset + b]:X2} (seed={seed})");
                        }
                    }
                    readOffset += expectedData.Length;
                }

                // Verify: result's offset mappings are correct
                List<DataChunk> sortedLiveChunks = liveChunks.OrderBy(c => c.Offset).ToList();
                long expectedNewOffset = 0;
                for (int i = 0; i < sortedLiveChunks.Count; i++)
                {
                    DataChunk chunk = sortedLiveChunks[i];
                    long originalOffset = chunk.Offset;

                    if (originalOffset != expectedNewOffset)
                    {
                        // This chunk moved — verify it's in the BlockOffsetUpdates
                        if (!result.BlockOffsetUpdates.TryGetValue(chunk.BlockKey, out long mappedOffset))
                        {
                            return false.ToProperty().Label(
                                $"Missing offset mapping for moved chunk {i} (original={originalOffset}, expectedNew={expectedNewOffset}, seed={seed})");
                        }
                        if (mappedOffset != expectedNewOffset)
                        {
                            return false.ToProperty().Label(
                                $"Incorrect offset mapping for chunk {i}: expected={expectedNewOffset}, got={mappedOffset} (seed={seed})");
                        }
                    }
                    else
                    {
                        // Chunk didn't move — should NOT be in offset updates
                        if (result.BlockOffsetUpdates.ContainsKey(chunk.BlockKey))
                        {
                            return false.ToProperty().Label(
                                $"Unexpected offset mapping for unmoved chunk {i} at offset {originalOffset} (seed={seed})");
                        }
                    }

                    expectedNewOffset += chunk.Size;
                }

                return true.ToProperty().Label(
                    $"CompactShard preserved all live data: {liveCount} live, {deadCount} dead chunks, output={actualOutputSize} bytes (seed={seed})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 14 (Unit): BuildChunkList returns empty UnreferencedBlockIndices
        /// when all block keys are in the referenced set.
        ///
        /// For any arbitrary block index where every entry's key is in the referenced keys set,
        /// BuildChunkList SHALL return an empty UnreferencedBlockIndices list.
        /// This proves the pipeline's first decision point (are there unreferenced blocks?)
        /// would answer "no" for a clean set.
        /// </summary>
        [Property(MaxTest = 10)]
        public Property BuildChunkList_AllKeysReferenced_ReturnsEmptyUnreferencedIndices()
        {
            Gen<BlockIndexEntry[]> gen = from entryCount in Gen.Choose(1, 20)
                                         from entries in Gen.ArrayOf(GenBlockIndexEntry(), entryCount)
                                         select entries;

            return Prop.ForAll(gen.ToArbitrary(), (BlockIndexEntry[] allEntries) =>
            {
                // All keys are referenced (simulating a clean set with no removed images)
                HashSet<BlockKey> referencedKeys = new HashSet<BlockKey>(allEntries.Select(e => e.Key));

                // Call BuildChunkList with empty liveImages (isolating block classification)
                ChunkClassification classification = BinaryDataStoreDataAccess.BuildChunkList(
                    allEntries, referencedKeys, new List<ImageDirectoryEntry>(), indexFile: null!);

                // Verify: no unreferenced block indices
                if (classification.UnreferencedBlockIndices.Count != 0)
                    return false.ToProperty().Label(
                        $"Expected empty UnreferencedBlockIndices but got {classification.UnreferencedBlockIndices.Count} entries");

                // Verify: all entries appear as referenced chunks
                int referencedBlockCount = classification.ReferencedChunks.Count(c => c.IsBlock);
                if (referencedBlockCount != allEntries.Length)
                    return false.ToProperty().Label(
                        $"Expected {allEntries.Length} referenced block chunks but got {referencedBlockCount}");

                return true.ToProperty().Label(
                    $"All {allEntries.Length} blocks correctly classified as referenced");
            });
        }

        /// <summary>
        /// **Validates: Requirements 5.6**
        ///
        /// Property 14 (Unit): CompactShard returns WasCompacted=false when all live chunks
        /// are already contiguous from offset 0 and the shard file size matches exactly.
        ///
        /// For any shard file where live chunks are packed contiguously starting at offset 0
        /// with no gaps and no trailing orphaned data, CompactShard SHALL return
        /// WasCompacted=false (no file modifications needed).
        /// </summary>
        [Property(MaxTest = 10)]
        public Property CompactShard_ContiguousLiveData_ReturnsWasCompactedFalse(PositiveInt chunkCountWrapper, PositiveInt seedWrapper)
        {
            int chunkCount = 1 + (chunkCountWrapper.Get % 10); // 1 to 10 chunks
            int seed = seedWrapper.Get;
            Random rnd = new Random(seed);

            string testDir = Path.Combine(_tempDir, $"CompactShardNoOp_{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDir);
            string shardPath = Path.Combine(testDir, "TestSet_0001.nkds");

            try
            {
                // Build contiguous chunks starting at offset 0
                List<DataChunk> liveChunks = new List<DataChunk>();
                long currentOffset = 0;

                for (int i = 0; i < chunkCount; i++)
                {
                    int chunkSize = 64 + (Math.Abs(rnd.Next()) % 4033); // 64 to 4096 bytes
                    BlockKey key = new BlockKey((ulong)rnd.NextInt64(), (uint)rnd.Next());

                    liveChunks.Add(new DataChunk(
                        IsBlock: true,
                        BlockKey: key,
                        BlockEntryIndex: i,
                        ImageId: 0,
                        FileName: "",
                        FileId: 1,
                        Offset: currentOffset,
                        Size: chunkSize));

                    currentOffset += chunkSize;
                }

                // Create a shard file with exactly the right size (contiguous, no orphaned tail)
                long totalSize = currentOffset;
                byte[] shardData = new byte[totalSize];
                rnd.NextBytes(shardData);
                File.WriteAllBytes(shardPath, shardData);

                // Call CompactShard — should detect no compaction needed
                ShardCompactResult result = BinaryDataStoreDataAccess.CompactShard(shardPath, liveChunks);

                if (result.WasCompacted)
                {
                    return false.ToProperty().Label(
                        $"Expected WasCompacted=false but got true (chunks={chunkCount}, totalSize={totalSize}, seed={seed})");
                }

                // Verify shard file was not modified (byte-for-byte identical)
                byte[] afterData = File.ReadAllBytes(shardPath);
                if (!shardData.SequenceEqual(afterData))
                {
                    return false.ToProperty().Label(
                        $"Shard file was modified despite WasCompacted=false (seed={seed})");
                }

                return true.ToProperty().Label(
                    $"CompactShard correctly returned WasCompacted=false for {chunkCount} contiguous chunks, {totalSize} bytes (seed={seed})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
            finally
            {
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }
}