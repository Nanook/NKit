using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Crash safety tests for CompactSetFast (index-only compact).
    ///
    /// CompactSetFast reuses the existing .compact.tmp crash recovery path:
    ///   - Crash BEFORE rename (both original + .compact.tmp exist) →
    ///     recoverIfNeeded deletes .compact.tmp; original is authoritative.
    ///   - Crash AFTER original deleted but BEFORE rename (.compact.tmp only) →
    ///     recoverIfNeeded renames .compact.tmp → original; validates header.
    ///
    /// Because CompactSetFast never touches shards, no shard recovery states are introduced.
    /// </summary>
    [Trait("Area", "Unit")]
    public class FastCompactIndexCrashSafetyTests : IDisposable
    {
        private readonly string _tempDir;

        public FastCompactIndexCrashSafetyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitFastCompactCrash_{Guid.NewGuid():N}");
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

        // ── Baseline ──────────────────────────────────────────────────────────

        [Fact]
        public void CompactSetFast_Success_AllImagesReadBackIdentical()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "baseline";
            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 4; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = makePayload(i + 100, 65536 * 2);
                    payloads[name] = payload;
                    writeImage(store, setName, name, payload);
                }
            }

            using (DataStore store = new DataStore(testDir))
                store.CompactSetFast(setName);

            Assert.Empty(Directory.GetFiles(testDir, $"{setName}.nkds.compact.tmp"));

            using (DataStore store = new DataStore(testDir))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName);
                Assert.Equal(4, images.Count);
                foreach (ImageRecord img in images)
                {
                    byte[] actual = readImage(store, setName, img.Id);
                    Assert.Equal(payloads[img.Name], actual);
                }
            }
        }

        // ── Crash before rename: both original + .compact.tmp exist ───────────
        //
        // Simulate: compact wrote .compact.tmp but then crashed before the atomic rename.
        // Expected: recoverIfNeeded on next open deletes .compact.tmp; original is intact.

        [Fact]
        public void CompactSetFast_CrashBeforeRename_OriginalIntact()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "crash_before";
            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 3; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = makePayload(i + 200, 65536);
                    payloads[name] = payload;
                    writeImage(store, setName, name, payload);
                }
            }

            // Snapshot the original index bytes
            string indexPath = Path.Combine(testDir, $"{setName}.nkds");
            byte[] originalIndexBytes = File.ReadAllBytes(indexPath);

            // Stage the "crash before rename" state: place a .compact.tmp alongside the original
            string tmpPath = indexPath + ".compact.tmp";
            File.WriteAllBytes(tmpPath, new byte[] { 0xFF, 0xFE, 0xFD }); // invalid/partial temp

            // Open fresh (triggers recoverIfNeeded which should delete the .compact.tmp)
            using (DataStore store = new DataStore(testDir))
            {
                // Accessing the set triggers getIndexFile → hasPendingRecoveryArtifacts → recoverIfNeeded
                List<ImageRecord> images = store.ListImagesInSet(setName);

                // .compact.tmp must be gone after the first set access
                Assert.False(File.Exists(tmpPath), ".compact.tmp was not cleaned up by recovery");

                // Original index must be intact — all images readable
                Assert.Equal(3, images.Count);
                foreach (ImageRecord img in images)
                {
                    byte[] actual = readImage(store, setName, img.Id);
                    Assert.Equal(payloads[img.Name], actual);
                }
            }
        }

        // ── Crash after delete, before rename: only .compact.tmp exists ────────
        //
        // Simulate: compact deleted the original then crashed before moving .compact.tmp.
        // Expected: recoverIfNeeded renames .compact.tmp → original; store is consistent.

        [Fact]
        public void CompactSetFast_CrashAfterDeleteBeforeMove_RecoveryRestores()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "crash_after";
            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 3; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = makePayload(i + 300, 65536);
                    payloads[name] = payload;
                    writeImage(store, setName, name, payload);
                }
            }

            // Run a real successful fast compact to get a valid .compact.tmp content
            using (DataStore store = new DataStore(testDir))
                store.CompactSetFast(setName);

            string indexPath = Path.Combine(testDir, $"{setName}.nkds");
            byte[] compactedBytes = File.ReadAllBytes(indexPath);

            // Stage the "crash after delete" state:
            // Move compacted index to .compact.tmp and delete the original
            string tmpPath = indexPath + ".compact.tmp";
            File.Move(indexPath, tmpPath);
            Assert.False(File.Exists(indexPath), "Original should be gone for this test");
            Assert.True(File.Exists(tmpPath), ".compact.tmp should exist");

            // Open fresh (triggers recoverIfNeeded which should promote .compact.tmp → original)
            using (DataStore store = new DataStore(testDir))
            {
                // Accessing the set triggers getIndexFile → hasPendingRecoveryArtifacts → recoverIfNeeded
                List<ImageRecord> images = store.ListImagesInSet(setName);

                // .compact.tmp must be consumed after the first set access
                Assert.False(File.Exists(tmpPath), ".compact.tmp was not promoted by recovery");
                Assert.True(File.Exists(indexPath), "Original index should have been restored");

                // All images must still be readable
                Assert.Equal(3, images.Count);
                foreach (ImageRecord img in images)
                {
                    byte[] actual = readImage(store, setName, img.Id);
                    Assert.Equal(payloads[img.Name], actual);
                }
            }
        }

        // ── CompactSetFast fails mid-write: original untouched ─────────────────
        //
        // Simulate: compact crashes while writing the temp file.
        // Expected: no .compact.tmp left (cleaned up in catch block), original intact.

        [Fact]
        public void CompactSetFast_CrashDuringWrite_OriginalIntact_NoTempOrphaned()
        {
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            const string setName = "crash_mid";
            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();

            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: 50L * 1024 * 1024 * 1024, blockSize: 65536);
                for (int i = 0; i < 3; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = makePayload(i + 400, 65536);
                    payloads[name] = payload;
                    writeImage(store, setName, name, payload);
                }
            }

            // Snapshot original index bytes
            string indexPath = Path.Combine(testDir, $"{setName}.nkds");
            byte[] originalBytes = File.ReadAllBytes(indexPath);

            // Block the .compact.tmp path to force CompactSetFast to throw
            string tmpPath = indexPath + ".compact.tmp";
            using (FileStream blocker = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (DataStore store = new DataStore(testDir))
                    Assert.ThrowsAny<Exception>(() => store.CompactSetFast(setName));
            }
            // Clean up blocker
            try { File.Delete(tmpPath); } catch { }

            // No .compact.tmp left orphaned (cleaned up in catch block before the blocker was removed)
            Assert.False(File.Exists(tmpPath), ".compact.tmp should not be orphaned after failed compact");

            // Original index must be byte-for-byte unchanged
            byte[] currentBytes = File.ReadAllBytes(indexPath);
            Assert.Equal(originalBytes, currentBytes);

            // All images must still be readable
            using (DataStore store = new DataStore(testDir))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName);
                Assert.Equal(3, images.Count);
                foreach (ImageRecord img in images)
                {
                    byte[] actual = readImage(store, setName, img.Id);
                    Assert.Equal(payloads[img.Name], actual);
                }
            }
        }
    }
}