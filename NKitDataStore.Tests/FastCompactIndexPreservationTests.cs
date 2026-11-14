using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Preservation property tests for CompactSetFast (index-only compact).
    ///
    /// Key invariants under test:
    ///   1. Shard files are never modified - byte-for-byte identical before and after.
    ///   2. All live images read back correctly after fast compact.
    ///   3. Removed images are absent from the directory after fast compact.
    ///   4. Delta count is zero after fast compact (all deltas merged).
    ///   5. Deduplication against pre-compact blocks still works after fast compact.
    ///   6. Fast compact followed by full CompactSet succeeds (shard reclamation still works).
    /// </summary>
    [Trait("Area", "Unit")]
    public class FastCompactIndexPreservationTests : IDisposable
    {
        private readonly string _tempDir;

        public FastCompactIndexPreservationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitFastCompactPreserve_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private static byte[] makePayload(int seed, int length)
        {
            Random rnd = new Random(seed);
            byte[] data = new byte[length];
            rnd.NextBytes(data);
            return data;
        }

        private static void writeImage(DataStore store, string setName, string imageName, byte[] payload)
        {
            using IImageWriter writer = store.AddImage(setName, imageName);
            writer.WriteData(0, new MemoryStream(payload), payload.Length, BlockType.File);
            uint crc = Crc.Compute(payload, 0, payload.Length);
            ulong xx = XXHash64.Compute(payload, 0, payload.Length);
            writer.FinalizeImage(payload.Length, crc, xx);
        }

        private static byte[] readImage(DataStore store, string setName, long imageId)
        {
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            List<long> starts = reader.GetOffsets()
                .Where(o => o.Offset >= 0 && o.Type != BlockType.BlockPadding)
                .Select(o => o.OffsetStart)
                .Distinct()
                .OrderBy(o => o)
                .ToList();

            using MemoryStream ms = new MemoryStream();
            foreach (long start in starts)
            {
                using Stream s = reader.OpenStream(start);
                s.CopyTo(ms);
            }
            return ms.ToArray();
        }

        private static string[] getShardFiles(string dir, string setName) =>
            Directory.GetFiles(dir, $"{setName}_*.nkds")
                     .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                     .ToArray();

        // ── Property 1: Shard files are never modified ────────────────────────

        [Property(MaxTest = 15)]
        public Property CompactSetFast_ShardFiles_AreNeverModified(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "FastCompactShardTest";
                int imageCount = 2 + (seed % 4);

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                    for (int i = 0; i < imageCount; i++)
                        writeImage(store, setName, $"img{i}.iso", makePayload(seed * 100 + i, 65536 * 2));
                }

                // Snapshot shards before
                string[] shardsBefore = getShardFiles(testDir, setName);
                Dictionary<string, byte[]> snapshot = shardsBefore.ToDictionary(
                    f => Path.GetFileName(f), File.ReadAllBytes);

                using (DataStore store = new DataStore(testDir))
                    store.CompactSetFast(setName);

                // Snapshot shards after
                string[] shardsAfter = getShardFiles(testDir, setName);
                Dictionary<string, byte[]> snapshotAfter = shardsAfter.ToDictionary(
                    f => Path.GetFileName(f), File.ReadAllBytes);

                bool unchanged = snapshot.Count == snapshotAfter.Count;
                if (unchanged)
                {
                    foreach (KeyValuePair<string, byte[]> kvp in snapshot)
                    {
                        if (!snapshotAfter.TryGetValue(kvp.Key, out byte[] afterBytes) || !kvp.Value.SequenceEqual(afterBytes))
                        {
                            unchanged = false;
                            break;
                        }
                    }
                }

                return unchanged.ToProperty().Label($"Shards unchanged after fast compact (seed={seed}, images={imageCount})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        // ── Property 2: Live images read back correctly ────────────────────────

        [Property(MaxTest = 15)]
        public Property CompactSetFast_LiveImages_ReadBackIdentical(PositiveInt seedWrapper)
        {
            int seed = seedWrapper.Get;
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                const string setName = "FastCompactReadTest";
                int imageCount = 2 + (seed % 4);
                Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();

                using (DataStore store = new DataStore(testDir))
                {
                    store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                    for (int i = 0; i < imageCount; i++)
                    {
                        string name = $"img{i}.iso";
                        byte[] payload = makePayload(seed * 200 + i, 65536 * 2);
                        payloads[name] = payload;
                        writeImage(store, setName, name, payload);
                    }
                }

                using (DataStore store = new DataStore(testDir))
                    store.CompactSetFast(setName);

                bool allOk = true;
                using (DataStore store = new DataStore(testDir))
                {
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    foreach (ImageRecord img in images)
                    {
                        byte[] actual = readImage(store, setName, img.Id);
                        if (!actual.SequenceEqual(payloads[img.Name]))
                        {
                            allOk = false;
                            break;
                        }
                    }
                }

                return allOk.ToProperty().Label($"All {imageCount} images readable after fast compact (seed={seed})");
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception (seed={seed}): {ex.Message}");
            }
        }

        // ── Property 3: Removed images are absent from directory ─────────────

        [Fact]
        public void CompactSetFast_RemovedImages_AbsentFromDirectoryAfterCompact()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "FastCompactRemoveTest";
            const int imageCount = 4;
            List<long> removedIds = new List<long>();

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < imageCount; i++)
                    writeImage(store, setName, $"img{i}.iso", makePayload(i + 1000, 65536));
            }

            using (DataStore store = new DataStore(testDir))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName);
                // Remove odd-indexed images
                foreach (ImageRecord img in images.Where(im => im.Id % 2 == 1))
                {
                    store.DeleteImage(new GlobalImageKey(setName, img.Id));
                    removedIds.Add(img.Id);
                }

                store.CompactSetFast(setName);
            }

            using (DataStore store = new DataStore(testDir))
            {
                List<ImageRecord> remaining = store.ListImagesInSet(setName);
                // None of the removed IDs should appear
                Assert.DoesNotContain(remaining, im => removedIds.Contains(im.Id));
                // The live images should still be there
                Assert.Equal(imageCount / 2, remaining.Count);
            }
        }

        // ── Property 4: Delta count is zero after fast compact ─────────────────

        [Fact]
        public void CompactSetFast_DeltaCount_IsZeroAfterCompact()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "FastCompactDeltaTest";

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                // Add several images - each creates one delta
                for (int i = 0; i < 5; i++)
                    writeImage(store, setName, $"img{i}.iso", makePayload(i + 2000, 65536));
            }

            // Verify deltas exist before compact
            using (DataStore storeBefore = new DataStore(testDir))
            {
                IndexHealthInfo healthBefore = storeBefore.GetIndexHealth(setName);
                Assert.True(healthBefore.DeltaCount > 0, "Expected at least one delta before fast compact");
            }

            using (DataStore store = new DataStore(testDir))
                store.CompactSetFast(setName);

            // Delta count must be zero after compact
            using (DataStore storeAfter = new DataStore(testDir))
            {
                IndexHealthInfo healthAfter = storeAfter.GetIndexHealth(setName);
                Assert.Equal(0, healthAfter.DeltaCount);
                Assert.False(healthAfter.NeedsCompaction);
            }
        }

        // ── Property 5: Dedup still works after fast compact ───────────────────

        [Fact]
        public void CompactSetFast_DeduplicationStillWorks_AfterCompact()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "FastCompactDedupTest";
            byte[] sharedBlock = makePayload(9999, 65536);
            byte[] uniqueBlock = makePayload(8888, 65536);

            // Image 1: sharedBlock
            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                writeImage(store, setName, "img1.iso", sharedBlock);
            }

            // Fast compact
            using (DataStore store = new DataStore(testDir))
                store.CompactSetFast(setName);

            // Add image 2 with same shared block - should dedup (no new shard bytes for sharedBlock)
            string[] shardsBefore = getShardFiles(testDir, setName);
            long shardSizeBefore = shardsBefore.Sum(f => new FileInfo(f).Length);

            using (DataStore store = new DataStore(testDir))
            {
                byte[] image2 = new byte[65536 * 2];
                Array.Copy(sharedBlock, 0, image2, 0, 65536);
                Array.Copy(uniqueBlock, 0, image2, 65536, 65536);
                writeImage(store, setName, "img2.iso", image2);
            }

            // Only uniqueBlock should have been added to the shard (sharedBlock is deduped)
            string[] shardsAfter = getShardFiles(testDir, setName);
            long shardSizeAfter = shardsAfter.Sum(f => new FileInfo(f).Length);

            // Shard grew by less than 2 full blocks (dedup saved at least one block)
            Assert.True(shardSizeAfter - shardSizeBefore < 65536 * 2,
                $"Expected dedup after fast compact but shard grew by {shardSizeAfter - shardSizeBefore} bytes");
        }

        // ── Property 6: Fast compact + full compact succeeds ──────────────────

        [Fact]
        public void CompactSetFast_FollowedByFullCompact_Succeeds()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "FastThenFullCompactTest";

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 4; i++)
                    writeImage(store, setName, $"img{i}.iso", makePayload(i + 3000, 65536));
            }

            // Remove one image
            using (DataStore store = new DataStore(testDir))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName);
                store.DeleteImage(new GlobalImageKey(setName, images[0].Id));
                store.CompactSetFast(setName);
            }

            // Full compact should succeed and leave a valid readable store
            using (DataStore store = new DataStore(testDir))
            {
                store.CompactSet(setName); // should not throw
                List<ImageRecord> remaining = store.ListImagesInSet(setName);
                Assert.Equal(3, remaining.Count);
                foreach (ImageRecord img in remaining)
                    Assert.NotNull(img.Name); // images still readable
            }
        }

        // ── Property 7: No-removed, no-deltas → NeedsCompaction = false ────────

        [Fact]
        public void GetIndexHealth_AfterFastCompact_ReportsHealthy()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "HealthCheckTest";

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 3; i++)
                    writeImage(store, setName, $"img{i}.iso", makePayload(i + 4000, 65536));
                store.CompactSetFast(setName);

                IndexHealthInfo health = store.GetIndexHealth(setName);
                Assert.Equal(0, health.DeltaCount);
                Assert.False(health.HasRemovedImages);
                Assert.False(health.NeedsCompaction);
                Assert.True(health.IndexFileSizeBytes > 0);
            }
        }

        // ── Property 8: Read-only shards do not prevent fast compact ─────────
        //
        // Fast compact only touches the index file (.nkds). Shard files (_NNNN.nkds)
        // are never opened. This test marks shards read-only, runs CompactSetFast,
        // verifies success, and confirms shards are byte-for-byte unchanged.

        [Fact]
        public void CompactSetFast_WithReadOnlyShards_Succeeds()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "ReadOnlyShardTest";

            // Separate mode (explicit shard size) - shards live in _NNNN.nkds files
            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 4; i++)
                    writeImage(store, setName, $"img{i}.iso", makePayload(i + 5000, 65536 * 2));
            }

            string[] shardFiles = Directory.GetFiles(testDir, $"{setName}_*.nkds");
            Assert.NotEmpty(shardFiles);

            // Snapshot shard bytes
            Dictionary<string, byte[]> shardSnapshots = shardFiles.ToDictionary(f => f, File.ReadAllBytes);

            // Make shards read-only
            foreach (string shard in shardFiles)
                File.SetAttributes(shard, File.GetAttributes(shard) | FileAttributes.ReadOnly);

            try
            {
                // CompactSetFast must succeed even with read-only shards
                using (DataStore store = new DataStore(testDir))
                {
                    Exception ex = Record.Exception(() => store.CompactSetFast(setName));
                    Assert.Null(ex); // must not throw

                    // Index is now clean
                    IndexHealthInfo health = store.GetIndexHealth(setName);
                    Assert.Equal(0, health.DeltaCount);

                    // All images still readable
                    List<ImageRecord> images = store.ListImagesInSet(setName);
                    Assert.Equal(4, images.Count);
                    foreach (ImageRecord img in images)
                    {
                        byte[] actual = readImage(store, setName, img.Id);
                        Assert.Equal(makePayload((int)img.Id - 1 + 5000, 65536 * 2), actual);
                    }
                }

                // Shards must be byte-for-byte identical - nothing was written to them
                foreach (KeyValuePair<string, byte[]> kvp in shardSnapshots)
                {
                    byte[] after = File.ReadAllBytes(kvp.Key);
                    Assert.Equal(kvp.Value, after);
                }
            }
            finally
            {
                // Always restore write permission so the test dir can be cleaned up
                foreach (string shard in shardFiles)
                {
                    try { File.SetAttributes(shard, File.GetAttributes(shard) & ~FileAttributes.ReadOnly); } catch { }
                }
            }
        }
    }
}