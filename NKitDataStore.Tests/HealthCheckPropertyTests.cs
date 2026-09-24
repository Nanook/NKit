using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using Property = FsCheck.Property;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Health check property tests for the compact robustness refactor.
    ///
    /// **Validates: Requirements 3.2, 3.3, 3.5, 3.6, 3.7**
    ///
    /// Property 3: Health Check Correctness for Clean Sets
    /// Property 4: Health Check Detects Intermediate States
    /// Property 5: Health Check Is Read-Only
    /// </summary>
    public class HealthCheckPropertyTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public HealthCheckPropertyTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitHealthCheck_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        #region Generators

        /// <summary>
        /// Represents a generated block index entry for testing.
        /// </summary>
        public class GenBlockIndexModel
        {
            public ulong XxHash64 { get; set; }
            public uint Crc32 { get; set; }
            public int FileId { get; set; }
            public long Offset { get; set; }
            public int Size { get; set; }

            public BlockKey ToBlockKey() => new BlockKey(XxHash64, Crc32);

            public override string ToString() =>
                $"BlockIndex(Key=0x{XxHash64:X16}:0x{Crc32:X8}, FileId={FileId}, Offset={Offset}, Size={Size})";
        }

        /// <summary>
        /// Represents a generated shard content model for testing.
        /// Contains a mix of live and dead chunks with optional orphaned tail data.
        /// </summary>
        public class GenShardContentModel
        {
            public List<ShardChunk> Chunks { get; set; } = new();
            public byte[] OrphanedTailData { get; set; }
            public int FileId { get; set; }

            public long TotalLiveSize => Chunks.Where(c => c.IsLive).Sum(c => (long)c.Data.Length);
            public long TotalSize => Chunks.Sum(c => (long)c.Data.Length) + (OrphanedTailData?.Length ?? 0);
            public bool HasOrphanedTail => OrphanedTailData != null && OrphanedTailData.Length > 0;
            public bool HasDeadChunks => Chunks.Any(c => !c.IsLive);

            public override string ToString() =>
                $"ShardContent(FileId={FileId}, Chunks={Chunks.Count}, Live={Chunks.Count(c => c.IsLive)}, " +
                $"Dead={Chunks.Count(c => !c.IsLive)}, OrphanedTail={OrphanedTailData?.Length ?? 0} bytes)";
        }

        /// <summary>
        /// Represents a single chunk within a shard (either live or dead).
        /// </summary>
        public class ShardChunk
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public bool IsLive { get; set; }
            public BlockKey Key { get; set; }

            public override string ToString() =>
                $"Chunk(IsLive={IsLive}, Size={Data.Length}, Key={Key})";
        }

        /// <summary>
        /// ArbBlockIndex generator: produces random block index entries with varying file IDs, offsets, and sizes.
        /// Generates entries that simulate realistic block index patterns with:
        /// - File IDs ranging from 1 to 10 (multiple shards)
        /// - Offsets that are non-negative and non-overlapping within a file ID
        /// - Sizes between 1 byte and 65536 bytes (block size)
        /// - Unique BlockKeys (xxHash64 + crc32 combinations)
        /// </summary>
        public static Gen<GenBlockIndexModel[]> ArbBlockIndex =>
            from count in Gen.Choose(1, 20)
            from entries in Gen.ArrayOf(GenSingleBlockIndex(), count)
            select DeduplicateAndSortEntries(entries);

        /// <summary>
        /// ArbShardContent generator: produces shard files with live/dead chunks and optional orphaned tail data.
        /// Generates shard content models that simulate:
        /// - A mix of live and dead chunks (75% live, 25% dead)
        /// - Chunk sizes between 64 and 4096 bytes
        /// - Optional orphaned tail data (30% chance, 1-512 bytes)
        /// - Unique BlockKeys per chunk
        /// </summary>
        public static Gen<GenShardContentModel> ArbShardContent =>
            from fileId in Gen.Choose(1, 10)
            from chunkCount in Gen.Choose(1, 15)
            from chunks in Gen.ArrayOf(GenShardChunk(), chunkCount)
            from hasOrphanedTail in Gen.Frequency((3, Gen.Constant(true)), (7, Gen.Constant(false)))
            from orphanedSize in Gen.Choose(1, 512)
            from orphanedData in Gen.ArrayOf(Gen.Choose(0, 255).Select(i => (byte)i), orphanedSize)
            select new GenShardContentModel
            {
                FileId = fileId,
                Chunks = AssignUniqueKeys(chunks.ToList()),
                OrphanedTailData = hasOrphanedTail ? orphanedData : null
            };

        /// <summary>
        /// Generates a single block index entry with random values.
        /// </summary>
        private static Gen<GenBlockIndexModel> GenSingleBlockIndex()
        {
            return from xxHash in Gen.Choose(1, int.MaxValue).Select(v => (ulong)v)
                   from crc in Gen.Choose(1, int.MaxValue).Select(v => (uint)v)
                   from fileId in Gen.Choose(1, 10)
                   from offset in Gen.Choose(0, 10_000_000).Select(v => (long)v)
                   from size in Gen.Choose(1, 65536)
                   select new GenBlockIndexModel
                   {
                       XxHash64 = xxHash,
                       Crc32 = crc,
                       FileId = fileId,
                       Offset = offset,
                       Size = size
                   };
        }

        /// <summary>
        /// Generates a single shard chunk with random data and live/dead status.
        /// </summary>
        private static Gen<ShardChunk> GenShardChunk()
        {
            return from size in Gen.Choose(64, 4096)
                   from data in Gen.ArrayOf(Gen.Choose(0, 255).Select(i => (byte)i), size)
                   from isLive in Gen.Frequency((3, Gen.Constant(true)), (1, Gen.Constant(false)))
                   select new ShardChunk
                   {
                       Data = data,
                       IsLive = isLive,
                       Key = default // Will be assigned unique keys later
                   };
        }

        /// <summary>
        /// Deduplicates entries by BlockKey and sorts them by (XxHash64, Crc32) ascending.
        /// </summary>
        private static GenBlockIndexModel[] DeduplicateAndSortEntries(GenBlockIndexModel[] entries)
        {
            return entries
                .GroupBy(e => (e.XxHash64, e.Crc32))
                .Select(g => g.First())
                .OrderBy(e => e.XxHash64)
                .ThenBy(e => e.Crc32)
                .ToArray();
        }

        /// <summary>
        /// Assigns unique BlockKeys to each chunk in the list.
        /// </summary>
        private static List<ShardChunk> AssignUniqueKeys(List<ShardChunk> chunks)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Key = new BlockKey((ulong)((i * 7919) + 1), (uint)((i * 104729) + 1));
            }
            return chunks;
        }

        #endregion

        #region Snapshot Helpers

        /// <summary>
        /// Represents a snapshot of all files in a directory: relative path → (size, lastWriteTimeUtc).
        /// </summary>
        private class DirectorySnapshot
        {
            public Dictionary<string, (long Size, DateTime LastWriteUtc)> Files { get; }

            public DirectorySnapshot(string directory)
            {
                Files = new Dictionary<string, (long, DateTime)>();
                if (!Directory.Exists(directory))
                    return;

                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    FileInfo info = new FileInfo(file);
                    string relativePath = Path.GetRelativePath(directory, file);
                    Files[relativePath] = (info.Length, info.LastWriteTimeUtc);
                }
            }

            /// <summary>
            /// Compares this snapshot to another and returns a description of differences, or null if identical.
            /// </summary>
            public string CompareWith(DirectorySnapshot other)
            {
                List<string> differences = new List<string>();

                foreach (KeyValuePair<string, (long Size, DateTime LastWriteUtc)> kvp in Files)
                {
                    if (!other.Files.TryGetValue(kvp.Key, out (long Size, DateTime LastWriteUtc) otherEntry))
                    {
                        differences.Add($"File deleted: {kvp.Key}");
                        continue;
                    }

                    if (kvp.Value.Size != otherEntry.Size)
                    {
                        differences.Add($"File size changed: {kvp.Key} ({kvp.Value.Size} → {otherEntry.Size})");
                    }

                    if (kvp.Value.LastWriteUtc != otherEntry.LastWriteUtc)
                    {
                        differences.Add($"File timestamp changed: {kvp.Key} ({kvp.Value.LastWriteUtc:O} → {otherEntry.LastWriteUtc:O})");
                    }
                }

                foreach (KeyValuePair<string, (long Size, DateTime LastWriteUtc)> kvp in other.Files)
                {
                    if (!Files.ContainsKey(kvp.Key))
                    {
                        differences.Add($"File created: {kvp.Key}");
                    }
                }

                return differences.Count > 0 ? string.Join("; ", differences) : null;
            }
        }

        /// <summary>
        /// Enum representing the type of intermediate state to inject.
        /// </summary>
        private enum IntermediateStateType
        {
            /// <summary>Clean set with no intermediate files.</summary>
            Clean,
            /// <summary>.compact.tmp file exists alongside the original index.</summary>
            CompactTmpAlongsideOriginal,
            /// <summary>Shard .tmp file exists alongside original shard.</summary>
            ShardTmpAlongsideOriginal,
            /// <summary>Orphaned tail data appended to a shard file.</summary>
            OrphanedTailData
        }

        #endregion

        #region Property 3: Health Check Correctness for Clean Sets

        /// <summary>
        /// **Validates: Requirements 3.2**
        ///
        /// Property 3: Health Check Correctness for Clean Sets
        ///
        /// For any valid set (separate or embedded mode) with no intermediate files and no orphaned tail data,
        /// CheckSetHealth SHALL return SetHealthStatus.Clean.
        ///
        /// Strategy:
        /// 1. Use ArbBlockIndex to generate block index configurations (varying image counts and block patterns)
        /// 2. Create a DataStore set in separate mode (shardSize > 0)
        /// 3. Add images, optionally remove some and compact (to ensure clean state)
        /// 4. Close the store
        /// 5. Reopen and call CheckSetHealth
        /// 6. Verify it returns SetHealthStatus.Clean
        /// </summary>
        [Property(MaxTest = 10)]
        public Property HealthCheck_ReturnsClean_ForCleanSets()
        {
            Gen<(GenBlockIndexModel[] blockEntries, int imageCount, bool removeAndCompact)> gen = from blockEntries in ArbBlockIndex
                                                                                                  from imageCount in Gen.Choose(1, 5)
                                                                                                  from removeAndCompact in Gen.Elements(true, false)
                                                                                                  select (blockEntries, imageCount, removeAndCompact);

            return Prop.ForAll(gen.ToArbitrary(), (tuple) =>
            {
                (GenBlockIndexModel[] blockEntries, int imageCount, bool removeAndCompact) = tuple;
                int removeCount = removeAndCompact && imageCount > 1 ? 1 + (blockEntries.Length % (imageCount - 1)) : 0;

                string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDir);

                try
                {
                    const string setName = "HealthCheckCleanTest";
                    const int blockSize = 65536;
                    const long shardSize = 50L * 1024 * 1024 * 1024;

                    // Phase 1: Create a set with images in separate mode
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                        Random rnd = new Random(blockEntries.Length + imageCount);
                        for (int i = 0; i < imageCount; i++)
                        {
                            // Use block entry sizes from the generator to vary block counts
                            int blocks = 1 + (i < blockEntries.Length ? blockEntries[i].Size % 3 : 0);
                            byte[] imageData = new byte[blockSize * blocks];
                            rnd.NextBytes(imageData);

                            using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                            {
                                writer.WriteData(0, imageData, BlockType.File);
                                writer.FinalizeImage(imageData.Length, 0, 0);
                            }

                            TestDataStoreHelper.WaitForSetIdle(store, setName);
                        }

                        // Phase 2: Optionally remove some images and compact to ensure clean state
                        if (removeAndCompact && removeCount > 0)
                        {
                            for (int i = 0; i < removeCount; i++)
                            {
                                store.DeleteImage(new GlobalImageKey(setName, i + 1));
                            }

                            store.CompactSet(setName);
                            TestDataStoreHelper.WaitForSetIdle(store, setName);
                        }
                    }

                    // Phase 3: Reopen and call CheckSetHealth
                    using (DataStore store = new DataStore(testDir))
                    {
                        RecoveryState result = store.CheckSetHealth(setName);

                        _output.WriteLine(
                            $"images={imageCount}, removed={removeCount}, compact={removeAndCompact}, " +
                            $"status={result.Status}, issues={result.DetectedIssues.Count}, " +
                            $"blockEntries={blockEntries.Length}");

                        if (result.Status != SetHealthStatus.Clean)
                        {
                            string issueDetails = result.DetectedIssues.Count > 0
                                ? string.Join("; ", result.DetectedIssues)
                                : result.Description;

                            return false.ToProperty().Label(
                                $"Expected Clean but got {result.Status} (images={imageCount}, " +
                                $"removed={removeCount}, compact={removeAndCompact}): {issueDetails}");
                        }

                        return true.ToProperty().Label(
                            $"Clean confirmed (images={imageCount}, removed={removeCount}, compact={removeAndCompact})");
                    }
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label(
                        $"Exception (images={imageCount}, removed={removeCount}): {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        #endregion

        #region Property 4: Health Check Detects Intermediate States

        /// <summary>
        /// **Validates: Requirements 3.3, 3.5, 3.7**
        ///
        /// Property 4: Health Check Detects Intermediate States
        ///
        /// For any set with injected intermediate file states (.compact.tmp, shard .tmp,
        /// embedded extraction intermediates, orphaned tail data), CheckSetHealth SHALL return
        /// SetHealthStatus.NeedsRecovery with a description identifying the detected state.
        ///
        /// Strategy:
        /// 1. Use ArbShardContent to generate shard content models with live/dead chunks and orphaned tail data
        /// 2. Create a DataStore set with images
        /// 3. Close the store
        /// 4. Inject intermediate states based on the generated shard content
        /// 5. Reopen and call CheckSetHealth
        /// 6. Verify it returns SetHealthStatus.NeedsRecovery
        /// 7. Verify DetectedIssues is non-empty
        /// </summary>
        [Property(MaxTest = 10)]
        public Property HealthCheck_DetectsIntermediateStates()
        {
            Gen<(GenShardContentModel shardContent, int stateTypeIdx, int imageCount)> gen = from shardContent in ArbShardContent
                                                                                             from stateTypeIdx in Gen.Choose(0, 2)
                                                                                             from imageCount in Gen.Choose(1, 4)
                                                                                             select (shardContent, stateTypeIdx, imageCount);

            return Prop.ForAll(gen.ToArbitrary(), (tuple) =>
            {
                (GenShardContentModel shardContent, int stateTypeIdx, int imageCount) = tuple;

                // Map index to non-Clean intermediate state types
                IntermediateStateType[] intermediateStates = new[]
                {
                    IntermediateStateType.CompactTmpAlongsideOriginal,
                    IntermediateStateType.ShardTmpAlongsideOriginal,
                    IntermediateStateType.OrphanedTailData
                };
                IntermediateStateType stateType = intermediateStates[stateTypeIdx];

                string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDir);

                try
                {
                    const string setName = "HealthCheckIntermediateTest";
                    const int blockSize = 65536;
                    const long shardSize = 50L * 1024 * 1024 * 1024;

                    // Phase 1: Create a set with images
                    Random rnd = new Random(shardContent.Chunks.Count + imageCount);
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                        for (int i = 0; i < imageCount; i++)
                        {
                            int blocks = 1 + (i % 3);
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

                    // Phase 2: Inject intermediate state using generated shard content
                    InjectIntermediateStateFromModel(testDir, setName, stateType, shardContent, rnd);

                    // Phase 3: Reopen and call CheckSetHealth
                    using (DataStore store = new DataStore(testDir))
                    {
                        RecoveryState result = store.CheckSetHealth(setName);

                        _output.WriteLine(
                            $"CheckSetHealth returned: Status={result.Status}, " +
                            $"Issues=[{string.Join("; ", result.DetectedIssues)}] " +
                            $"(state={stateType}, shardContent={shardContent})");

                        // Phase 4: Verify it returns NeedsRecovery
                        if (result.Status != SetHealthStatus.NeedsRecovery)
                        {
                            return false.ToProperty().Label(
                                $"Expected NeedsRecovery but got {result.Status} " +
                                $"(state={stateType}, issues=[{string.Join("; ", result.DetectedIssues)}])");
                        }

                        // Phase 5: Verify DetectedIssues is non-empty
                        if (result.DetectedIssues.Count == 0)
                        {
                            return false.ToProperty().Label(
                                $"DetectedIssues is empty despite NeedsRecovery status (state={stateType})");
                        }

                        return true.ToProperty().Label(
                            $"Intermediate state detected: {stateType}, {result.DetectedIssues.Count} issue(s)");
                    }
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Exception (state={stateType}): {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        #endregion

        #region Property 5: Health Check Is Read-Only

        /// <summary>
        /// **Validates: Requirements 3.6**
        ///
        /// Property 5: Health Check Is Read-Only
        ///
        /// For any set in any state (clean, intermediate, or corrupt), calling CheckSetHealth
        /// SHALL NOT modify any file on disk (file sizes and modification timestamps remain unchanged).
        ///
        /// Strategy:
        /// 1. Use ArbShardContent to generate varying shard content configurations
        /// 2. Create a DataStore set and optionally inject intermediate states
        /// 3. Snapshot all file sizes and modification timestamps in the set directory
        /// 4. Call CheckSetHealth
        /// 5. Re-snapshot all file sizes and modification timestamps
        /// 6. Verify they are identical (no files modified, created, or deleted)
        /// </summary>
        [Property(MaxTest = 10)]
        public Property HealthCheck_DoesNotModifyAnyFiles()
        {
            Gen<(GenShardContentModel shardContent, int stateTypeIdx, int imageCount)> gen = from shardContent in ArbShardContent
                                                                                             from stateTypeIdx in Gen.Choose(0, 3) // 0=Clean, 1-3=intermediate
                                                                                             from imageCount in Gen.Choose(1, 4)
                                                                                             select (shardContent, stateTypeIdx, imageCount);

            return Prop.ForAll(gen.ToArbitrary(), (tuple) =>
            {
                (GenShardContentModel shardContent, int stateTypeIdx, int imageCount) = tuple;

                IntermediateStateType[] stateTypes = (IntermediateStateType[])Enum.GetValues(typeof(IntermediateStateType));
                IntermediateStateType stateType = stateTypes[stateTypeIdx];

                string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testDir);

                try
                {
                    const string setName = "HealthCheckReadOnlyTest";
                    const int blockSize = 65536;
                    const long shardSize = 50L * 1024 * 1024 * 1024;

                    // Phase 1: Create a set with images
                    Random rnd = new Random(shardContent.Chunks.Count + imageCount + stateTypeIdx);
                    using (DataStore store = new DataStore(testDir))
                    {
                        store.CreateSet(setName, shardSize: shardSize, blockSize: blockSize);

                        for (int i = 0; i < imageCount; i++)
                        {
                            int blocks = 1 + (i % 3);
                            byte[] imageData = new byte[blockSize * blocks];
                            rnd.NextBytes(imageData);

                            using (IImageWriter writer = store.AddImage(setName, $"Image{i + 1}.iso"))
                            {
                                writer.WriteData(0, imageData, BlockType.File);
                                writer.FinalizeImage(imageData.Length, 0, 0);
                            }

                            TestDataStoreHelper.WaitForSetIdle(store, setName);
                        }

                        // Optionally remove some images for more realistic state
                        if (stateType != IntermediateStateType.Clean && imageCount > 1)
                        {
                            int removeCount = 1 + (shardContent.Chunks.Count % (imageCount - 1));
                            for (int i = 0; i < removeCount; i++)
                            {
                                store.DeleteImage(new GlobalImageKey(setName, i + 1));
                            }
                        }
                    }

                    // Phase 2: Inject intermediate state
                    InjectIntermediateStateFromModel(testDir, setName, stateType, shardContent, rnd);

                    // Allow filesystem timestamps to settle
                    Thread.Sleep(50);

                    // Phase 3: Snapshot all file sizes and modification timestamps
                    DirectorySnapshot snapshotBefore = new DirectorySnapshot(testDir);

                    if (snapshotBefore.Files.Count == 0)
                    {
                        return false.ToProperty().Label(
                            $"No files found in test directory (state={stateType})");
                    }

                    // Phase 4: Call CheckSetHealth
                    using (DataStore store = new DataStore(testDir))
                    {
                        RecoveryState result = store.CheckSetHealth(setName);
                        _output.WriteLine(
                            $"CheckSetHealth returned: Status={result.Status}, " +
                            $"Issues={result.DetectedIssues.Count} (state={stateType})");
                    }

                    // Phase 5: Re-snapshot all file sizes and modification timestamps
                    DirectorySnapshot snapshotAfter = new DirectorySnapshot(testDir);

                    // Phase 6: Verify snapshots are identical
                    string differences = snapshotBefore.CompareWith(snapshotAfter);

                    if (differences != null)
                    {
                        return false.ToProperty().Label(
                            $"CheckSetHealth modified files (state={stateType}): {differences}");
                    }

                    return true.ToProperty().Label(
                        $"Read-only confirmed: {snapshotBefore.Files.Count} files unchanged (state={stateType})");
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Exception (state={stateType}): {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            });
        }

        #endregion

        #region Intermediate State Injection

        /// <summary>
        /// Injects an intermediate state into the set directory based on the specified type,
        /// using the generated shard content model for realistic data.
        /// </summary>
        private void InjectIntermediateStateFromModel(
            string testDir, string setName, IntermediateStateType stateType,
            GenShardContentModel shardContent, Random rnd)
        {
            string indexPath = Path.Combine(testDir, $"{setName}.nkds");

            switch (stateType)
            {
                case IntermediateStateType.Clean:
                    // No injection needed — set is already clean
                    break;

                case IntermediateStateType.CompactTmpAlongsideOriginal:
                    // Create a .compact.tmp file alongside the original index
                    // Use shard content chunk data to generate realistic tmp content
                    string compactTmpPath = indexPath + ".compact.tmp";
                    byte[] tmpContent = shardContent.Chunks.Count > 0
                        ? shardContent.Chunks[0].Data
                        : new byte[256];
                    File.WriteAllBytes(compactTmpPath, tmpContent);
                    break;

                case IntermediateStateType.ShardTmpAlongsideOriginal:
                    // Create a shard .nkds.tmp file alongside an existing shard
                    string shardTmpPath = Path.Combine(testDir, $"{setName}_0001.nkds.tmp");
                    byte[] shardTmpContent = shardContent.Chunks.Count > 0
                        ? shardContent.Chunks[rnd.Next(shardContent.Chunks.Count)].Data
                        : new byte[128];
                    File.WriteAllBytes(shardTmpPath, shardTmpContent);
                    break;

                case IntermediateStateType.OrphanedTailData:
                    // Append orphaned data to an existing shard file
                    string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
                    if (shardFiles.Length > 0)
                    {
                        string targetShard = shardFiles[rnd.Next(shardFiles.Length)];
                        // Use orphaned tail data from the model, or generate some
                        byte[] orphanedData = shardContent.OrphanedTailData
                            ?? new byte[64 + rnd.Next(256)];
                        if (orphanedData.Length == 0)
                            orphanedData = new byte[64];
                        rnd.NextBytes(orphanedData);
                        using FileStream fs = new FileStream(targetShard, FileMode.Append, FileAccess.Write);
                        fs.Write(orphanedData, 0, orphanedData.Length);
                    }
                    break;
            }
        }

        #endregion
    }
}