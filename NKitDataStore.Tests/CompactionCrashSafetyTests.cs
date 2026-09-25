using Nanook.NKit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration tests for the compaction crash-safety fix.
    ///
    /// Background: compaction rewrites shard files (shifting live block offsets) and updates
    /// the block index. The original implementation renamed each compacted shard into place
    /// one-by-one BEFORE committing the index, so a failure part-way through the shard loop
    /// left already-rewritten shards on disk while the committed index still pointed at the old
    /// offsets — corrupting SURVIVING images, not just the removed ones. It could also orphan a
    /// "&lt;set&gt;_NNNN.nkds.tmp" file.
    ///
    /// The fix defers all shard renames until AFTER the index is durably committed. A failure
    /// before the commit therefore leaves every original shard intact and the (unchanged)
    /// committed index still matching them, so surviving images read back byte-for-byte.
    ///
    /// These tests write raw images directly to a DataStore (no disc pipeline needed, since the
    /// bug lives entirely in the DataStore compaction layer) across multiple shards, then
    /// exercise remove + compact and a simulated mid-compaction failure.
    /// </summary>
    public class CompactionCrashSafetyTests : IDisposable
    {
        private readonly string _tempDir;

        public CompactionCrashSafetyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactCrash_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // Deterministic pseudo-random payload for an image, keyed by seed so we can regenerate
        // the exact expected bytes for comparison after compaction.
        private static byte[] MakePayload(int seed, int length)
        {
            Random rnd = new Random(seed);
            byte[] data = new byte[length];
            rnd.NextBytes(data);
            return data;
        }

        private static void WriteImage(DataStore store, string setName, string imageName, byte[] payload)
        {
            using IImageWriter writer = store.AddImage(setName, imageName, "TestSystem", ImageFormat.Iso);
            writer.WriteData(0, new MemoryStream(payload), payload.Length, BlockType.File);
            uint crc = Crc.Compute(payload, 0, payload.Length);
            ulong xx = XXHash64.Compute(payload, 0, payload.Length);
            writer.FinalizeImage(payload.Length, crc, xx);
        }

        // Reads the full stored byte stream for an image back out of the store.
        private static byte[] ReadImage(DataStore store, string setName, long imageId)
        {
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            List<long> offsetStarts = reader.GetOffsets()
                .Where(o => o.Offset >= 0 && o.Type != BlockType.BlockPadding)
                .Select(o => o.OffsetStart)
                .Distinct()
                .OrderBy(o => o)
                .ToList();

            using MemoryStream ms = new MemoryStream();
            foreach (long start in offsetStarts)
            {
                using Stream s = reader.OpenStream(start);
                s.CopyTo(ms);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Baseline: after removing some images and compacting successfully, every surviving
        /// image reads back byte-for-byte identical to what was stored.
        /// </summary>
        [Fact]
        public void Compact_AfterRemove_SurvivingImagesReadBackIdentical()
        {
            const string setName = "compact";
            string dsPath = _tempDir;

            // Small shards + small blocks so several images span multiple shard files.
            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();
            using (DataStore store = new DataStore(dsPath))
            {
                store.CreateSet(setName, shardSize: 512 * 1024, blockSize: 4096);
                for (int i = 1; i <= 6; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = MakePayload(seed: 1000 + i, length: 300_000);
                    payloads[name] = payload;
                    WriteImage(store, setName, name, payload);
                }
            }

            // Confirm multiple shards were created.
            int shardCount = Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds")
                .Count(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            Assert.True(shardCount > 1, $"Expected multiple shards, got {shardCount}");

            // Remove the odd-numbered images, then compact.
            using (DataStore store = new DataStore(dsPath))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName).ToList();
                foreach (ImageRecord img in images.Where(i => int.Parse(new string(i.Name.Where(char.IsDigit).ToArray())) % 2 == 1))
                    store.DeleteImage(new GlobalImageKey(setName, img.Id));

                store.CompactSet(setName);
            }

            // No stray .tmp shards left behind.
            Assert.Empty(Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds.tmp"));

            // Every surviving (even-numbered) image must read back byte-for-byte.
            using (DataStore store = new DataStore(dsPath))
            {
                List<ImageRecord> survivors = store.ListImagesInSet(setName).ToList();
                Assert.All(survivors, img =>
                {
                    byte[] expected = payloads[img.Name];
                    byte[] actual = ReadImage(store, setName, img.Id);
                    Assert.Equal(expected, actual);
                });
            }
        }

        /// <summary>
        /// Crash-safety regression: simulate a failure part-way through the shard-compaction
        /// loop by corrupting (truncating) one shard file on disk after images are removed but
        /// before compaction. Compaction must fail, but the set must remain fully consistent:
        /// every surviving image still reads back byte-for-byte, and no .tmp shard is orphaned.
        ///
        /// With the pre-fix implementation (rename-before-commit), an exception here would leave
        /// some shards already renamed to their compacted layout while the committed index still
        /// referenced the old offsets, corrupting surviving images. The deferred-rename fix
        /// guarantees nothing is renamed and the index is not committed until the whole rewrite
        /// succeeds.
        /// </summary>
        [Fact]
        public void Compact_FailsMidway_SurvivingImagesRemainIntact()
        {
            const string setName = "crash";
            string dsPath = _tempDir;

            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();
            using (DataStore store = new DataStore(dsPath))
            {
                store.CreateSet(setName, shardSize: 512 * 1024, blockSize: 4096);
                for (int i = 1; i <= 6; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = MakePayload(seed: 2000 + i, length: 300_000);
                    payloads[name] = payload;
                    WriteImage(store, setName, name, payload);
                }
            }

            HashSet<string> survivorNames = new HashSet<string>();
            using (DataStore store = new DataStore(dsPath))
            {
                List<ImageRecord> images = store.ListImagesInSet(setName).ToList();
                foreach (ImageRecord img in images)
                {
                    int n = int.Parse(new string(img.Name.Where(char.IsDigit).ToArray()));
                    if (n % 2 == 1)
                        store.DeleteImage(new GlobalImageKey(setName, img.Id));
                    else
                        survivorNames.Add(img.Name);
                }
            }

            // Force compaction to fail part-way through the shard loop WITHOUT damaging any real
            // shard data: hold the highest shard's ".tmp" path open exclusively. When the loop
            // reaches that shard, CompactShard's FileMode.Create on the temp throws IOException.
            // Under the old (rename-before-commit) code, earlier shards in the loop would already
            // be renamed to their compacted layout while the index still referenced old offsets,
            // corrupting surviving images. The deferred-rename fix renames nothing and does not
            // commit unless the whole rewrite succeeds, so the set stays consistent.
            List<string> shardFiles = Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds")
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();
            Assert.True(shardFiles.Count > 1, "Test needs multiple shards to be meaningful");

            // Snapshot every shard's bytes so we can prove compaction changed nothing on failure.
            Dictionary<string, byte[]> shardSnapshots = shardFiles.ToDictionary(f => f, File.ReadAllBytes);

            // Occupy the ".tmp" path of every shard EXCEPT the lowest-numbered one. The compaction
            // loop processes shards in ascending FileId order, so the first compactable shard can
            // proceed while a LATER shard's temp creation fails. This reproduces the dangerous
            // window: under the old rename-before-commit code an earlier shard would already be
            // renamed to its compacted layout (with the index still holding old offsets) when the
            // later shard throws — corrupting surviving images. The deferred-rename fix renames
            // nothing until the entire rewrite + index commit succeeds, so the set is untouched.
            List<FileStream> locks = new List<FileStream>();
            try
            {
                foreach (string shard in shardFiles.Skip(1))
                    locks.Add(new FileStream(shard + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.None));

                using (DataStore store = new DataStore(dsPath))
                {
                    Assert.ThrowsAny<Exception>(() => store.CompactSet(setName));
                }
            }
            finally
            {
                foreach (FileStream l in locks) { try { l.Dispose(); } catch { } }
                foreach (string shard in shardFiles) { try { File.Delete(shard + ".tmp"); } catch { } }
            }

            // No OTHER stray .tmp shards may be left behind (our own placeholder is deleted above;
            // the fix's finally-block deletes any temp it wrote before the failure).
            Assert.Empty(Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds.tmp"));

            // Every real shard must be byte-for-byte unchanged — the failed compaction renamed
            // nothing and committed nothing.
            foreach (KeyValuePair<string, byte[]> kvp in shardSnapshots)
            {
                Assert.True(File.Exists(kvp.Key), $"Shard '{kvp.Key}' vanished after failed compaction");
                Assert.Equal(kvp.Value, File.ReadAllBytes(kvp.Key));
            }

            // Every surviving image must still read back byte-for-byte. Because compaction renamed
            // nothing and did not commit, the committed index still matches the original shards.
            using (DataStore store = new DataStore(dsPath))
            {
                List<ImageRecord> survivors = store.ListImagesInSet(setName).Where(i => survivorNames.Contains(i.Name)).ToList();
                Assert.NotEmpty(survivors);
                foreach (ImageRecord img in survivors)
                {
                    byte[] expected = payloads[img.Name];
                    byte[] actual = ReadImage(store, setName, img.Id);
                    Assert.Equal(expected, actual);
                }
            }
        }

        /// <summary>
        /// Recovery-promote regression: simulate a crash AFTER the index was durably committed
        /// for the compacted layout but BEFORE the deferred shard renames completed. On next open,
        /// recovery must PROMOTE the leftover compacted ".tmp" shards (rename them over the
        /// originals) because the committed index expects the compacted size — NOT delete them.
        /// Deleting them would leave the (compacted) index pointing at un-compacted originals and
        /// corrupt every surviving image.
        ///
        /// The post-commit / pre-rename state is produced by holding the original shard files open
        /// exclusively during compaction, so the Stage-10 renames fail after AtomicCommit succeeds.
        /// </summary>
        [Fact]
        public void Compact_CrashAfterCommitBeforeRename_RecoveryPromotesTempShards()
        {
            const string setName = "promote";
            string dsPath = _tempDir;

            Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();
            using (DataStore store = new DataStore(dsPath))
            {
                store.CreateSet(setName, shardSize: 512 * 1024, blockSize: 4096);
                for (int i = 1; i <= 6; i++)
                {
                    string name = $"img{i}.iso";
                    byte[] payload = MakePayload(seed: 3000 + i, length: 300_000);
                    payloads[name] = payload;
                    WriteImage(store, setName, name, payload);
                }
            }

            HashSet<string> survivorNames = new HashSet<string>();
            using (DataStore store = new DataStore(dsPath))
            {
                foreach (ImageRecord img in store.ListImagesInSet(setName).ToList())
                {
                    int n = int.Parse(new string(img.Name.Where(char.IsDigit).ToArray()));
                    if (n % 2 == 1)
                        store.DeleteImage(new GlobalImageKey(setName, img.Id));
                    else
                        survivorNames.Add(img.Name);
                }
            }

            List<string> shardFiles = Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds")
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();
            Assert.True(shardFiles.Count > 1, "Test needs multiple shards to be meaningful");

            // Snapshot the pre-compact (original) shard bytes so we can reconstruct the exact
            // "crash between index commit and shard rename" on-disk state faithfully — without
            // racing file locks (which would create handle/inode artifacts that don't occur in a
            // real process crash).
            Dictionary<string, byte[]> originalShardBytes = shardFiles.ToDictionary(f => f, File.ReadAllBytes);

            // Perform a real, successful compaction. Afterwards the shards + index on disk hold
            // the committed COMPACTED layout.
            using (DataStore store = new DataStore(dsPath))
            {
                store.CompactSet(setName);
            }

            // Now stage the crash state: for each shard that shrank (was compacted), move the
            // compacted file aside to its ".tmp" name and restore the ORIGINAL (pre-compact)
            // bytes under the real shard name. The index on disk is already committed for the
            // compacted layout. This is byte-for-byte the state a crash after AtomicCommit but
            // before the deferred renames would leave.
            bool stagedAny = false;
            foreach (KeyValuePair<string, byte[]> kvp in originalShardBytes)
            {
                string shard = kvp.Key;
                if (!File.Exists(shard))
                    continue;
                long compactedLen = new FileInfo(shard).Length;
                if (compactedLen == kvp.Value.Length)
                    continue; // shard was not compacted (unchanged) — nothing to stage
                // Move compacted content to the ".tmp" the deferred rename would have promoted.
                File.Move(shard, shard + ".tmp");
                // Restore the original pre-compact bytes as the "un-renamed" shard.
                File.WriteAllBytes(shard, kvp.Value);
                stagedAny = true;
            }
            Assert.True(stagedAny, "Expected at least one shard to have been compacted");

            // Open fresh (simulating a new process after the crash). getIndexFile runs crash
            // recovery, which must PROMOTE the compacted ".tmp" shards because the committed index
            // expects the compacted size. After recovery no temps remain and survivors read back
            // byte-for-byte.
            using (DataStore store = new DataStore(dsPath))
            {
                List<ImageRecord> survivors = store.ListImagesInSet(setName).Where(i => survivorNames.Contains(i.Name)).ToList();
                Assert.NotEmpty(survivors);
                foreach (ImageRecord img in survivors)
                {
                    byte[] expected = payloads[img.Name];
                    byte[] actual = ReadImage(store, setName, img.Id);
                    Assert.Equal(expected, actual);
                }
            }

            // Recovery must have consumed all shard temp files.
            Assert.Empty(Directory.EnumerateFiles(_tempDir, $"{setName}_*.nkds.tmp"));
        }
    }
}