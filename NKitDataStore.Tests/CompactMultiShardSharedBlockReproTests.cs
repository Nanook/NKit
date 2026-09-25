using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using System.Security.Cryptography;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Reproduction tests for the user-reported corruption:
    ///
    ///   "Fill a container across multiple instances/sessions, with some different-named but
    ///    same-CRC roms. Delete the doubles, rename the survivors (Dat-checker), then Compact.
    ///    Afterwards nearly all LATER-added roms verify-fail. Before compact a full verify passed."
    ///
    /// The suspected mechanism (v2.1.0, code path unchanged at HEAD):
    ///   - Same-CRC roms deduplicate: a LATER-session image references blocks physically stored in
    ///     an EARLIER session's shard, so its per-image block map holds a FileId pointing at the
    ///     earlier shard.
    ///   - CompactShards remaps block offsets keyed by BlockKey ALONE (ignoring FileId) and
    ///     UpdateBlockMaps stamps that single per-shard offset onto every image that references the
    ///     key while preserving each image's own FileId. When the block-index copy and an image's
    ///     block-map copy of the same key live in different shards, the image ends up with
    ///     (itsFileId, someOtherShardOffset) -> reads garbage -> verify fails.
    ///
    /// These tests are written to FAIL on the buggy code and PASS once the offset remap is made
    /// FileId-aware. They make NO production changes.
    ///
    /// Multi-shard is forced with a small shardSize so each image's blocks roll into a new shard
    /// file (name_0000.nkds, name_0001.nkds, ...). Images are added across separate DataStore
    /// sessions to mirror the "multiple instances" part of the report.
    /// </summary>
    public class CompactMultiShardSharedBlockReproTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        // Small shard so each image rolls into its own shard file, giving us multiple FileIds.
        private const int BlockSize = 65536;               // 64 KiB
        private const long ShardSize = 128L * 1024;        // 128 KiB -> ~2 blocks per shard

        public CompactMultiShardSharedBlockReproTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactReproTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// PUREST form of the user's report: add many images across many sessions (producing many
        /// shard files), with cross-session/cross-shard block dedup, then COMPACT and verify ALL.
        /// No delete, no rename. If the multi-shard offset remap misfires, later-added images fail.
        /// </summary>
        [Fact]
        public void MultiSessionMultiShard_AddThenCompact_AllImagesVerify()
        {
            const string setName = "AddCompactRepro";
            const int imageCount = 12;

            // A pool of shared blocks reused across many images so dedup spreads references across
            // multiple shards (a later image's shared block physically lives in an earlier shard).
            byte[][] sharedPool = Enumerable.Range(0, 4).Select(i => MakeBlock(0x100 + i)).ToArray();

            (long id, byte[] data)[] images = new (long id, byte[] data)[imageCount];
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: ShardSize, blockSize: BlockSize);
                for (int i = 0; i < imageCount; i++)
                {
                    // Each image = one shared-pool block (dedups against earlier images) + one unique block.
                    byte[] shared = sharedPool[i % sharedPool.Length];
                    byte[] uniq = MakeBlock(0x1000 + i);
                    byte[] data = Concat(shared, uniq);
                    // New DataStore instance per image = separate "session" like the user's multi-instance fill.
                    long id;
                    if (i == 0) { id = AddImage(store, setName, $"Game{i:D2}.iso", data); }
                    else { using DataStore s2 = new DataStore(_tempDir); id = AddImage(s2, setName, $"Game{i:D2}.iso", data); }
                    images[i] = (id, data);
                }
            }

            LogShards(setName);

            // Before compact: everything verifies (matches the user's "before compact a full verify passed").
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);

            // Just compact.
            using (DataStore store = new DataStore(_tempDir))
                store.CompactSet(setName);

            LogShards(setName);

            // After compact: EVERY image must still verify — especially the later-added ones.
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);
        }

        /// <summary>
        /// Same multi-shard cross-dedup build, but now remove a couple of EARLY images (creating
        /// holes in early shards) before compacting — forcing real relocation of blocks that
        /// later images reference cross-shard.
        /// </summary>
        [Fact]
        public void MultiSessionMultiShard_RemoveEarly_ThenCompact_LaterImagesVerify()
        {
            const string setName = "AddRemoveCompactRepro";
            const int imageCount = 12;

            byte[][] sharedPool = Enumerable.Range(0, 4).Select(i => MakeBlock(0x200 + i)).ToArray();

            (long id, byte[] data)[] images = new (long id, byte[] data)[imageCount];
            using (DataStore store = new DataStore(_tempDir))
                store.CreateSet(setName, shardSize: ShardSize, blockSize: BlockSize);

            for (int i = 0; i < imageCount; i++)
            {
                byte[] shared = sharedPool[i % sharedPool.Length];
                byte[] uniq = MakeBlock(0x2000 + i);
                byte[] data = Concat(shared, uniq);
                using DataStore s = new DataStore(_tempDir);
                long id = AddImage(s, setName, $"Game{i:D2}.iso", data);
                images[i] = (id, data);
            }

            LogShards(setName);

            // Remove two early images (ids 2 and 4) — their unique blocks become dead, but their
            // shared-pool blocks are still referenced by later images. Compact must relocate.
            using (DataStore store = new DataStore(_tempDir))
            {
                store.DeleteImage(new GlobalImageKey(setName, images[1].id));
                store.DeleteImage(new GlobalImageKey(setName, images[3].id));
                store.CompactSet(setName);
            }

            LogShards(setName);

            using (DataStore store = new DataStore(_tempDir))
            {
                for (int i = 0; i < imageCount; i++)
                {
                    if (i == 1 || i == 3) continue; // removed
                    VerifyImageData(store, setName, images[i].id, images[i].data);
                }
            }
        }

        /// <summary>
        /// THE user-reported "add more images AFTER a compact" scenario — the gap the Test Suite Audit
        /// flagged as untested and the likely home of the compaction-corruption bug.
        ///
        /// Build a multi-shard set with cross-session/cross-shard dedup, COMPACT it, then ADD MORE
        /// images whose content DEDUPS against blocks that survived (and were possibly relocated by)
        /// the compaction. A post-compact block index left inconsistent with the images' block maps
        /// (the FileId-blind offset remap in compactShards/UpdateBlockMaps) makes the newly-added
        /// images inherit a bad (FileId, Offset) for the deduped block and read garbage.
        ///
        /// Every image — the pre-compact set AND the post-compact additions — must verify byte-for-byte.
        /// Written to FAIL on the buggy code and PASS once the offset remap is FileId-aware. No
        /// production changes.
        /// </summary>
        [Fact]
        public void MultiSessionMultiShard_CompactThenAddMore_AllImagesVerify()
        {
            const string setName = "CompactThenAddRepro";
            const int initialCount = 12;
            const int addedCount = 6;

            // Shared pool reused before AND after the compact so post-compact additions dedup against
            // surviving (possibly relocated) blocks that were physically written in earlier shards.
            byte[][] sharedPool = Enumerable.Range(0, 4).Select(i => MakeBlock(0x300 + i)).ToArray();

            List<(long id, byte[] data)> images = new List<(long id, byte[] data)>();

            // --- Phase 1: build the initial multi-shard set across separate sessions ---
            using (DataStore store = new DataStore(_tempDir))
                store.CreateSet(setName, shardSize: ShardSize, blockSize: BlockSize);

            for (int i = 0; i < initialCount; i++)
            {
                byte[] shared = sharedPool[i % sharedPool.Length];
                byte[] uniq = MakeBlock(0x3000 + i);
                byte[] data = Concat(shared, uniq);
                using DataStore s = new DataStore(_tempDir);
                long id = AddImage(s, setName, $"Game{i:D2}.iso", data);
                images.Add((id, data));
            }

            LogShards(setName);

            // Guard: the whole point of this test is a MULTI-shard layout (blocks spread across several
            // shard FileIds). If the layout ever degenerates to a single shard the cross-shard aliasing
            // check below would be vacuous — assert we really have >1 shard FileId in the block index.
            AssertMultipleShardsUsed(setName);

            // Sanity: everything verifies before we touch anything.
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);

            // --- Phase 2: COMPACT (the reported trigger) ---
            using (DataStore store = new DataStore(_tempDir))
                store.CompactSet(setName);

            LogShards(setName);

            // All pre-compact images must still verify immediately after the compact.
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);

            // --- Phase 3: ADD MORE images AFTER the compact ---
            // Each new image reuses a shared-pool block (dedups against a SURVIVING, post-compact
            // relocated block) plus a fresh unique block. New session per add, like the user's fill.
            for (int i = 0; i < addedCount; i++)
            {
                byte[] shared = sharedPool[i % sharedPool.Length];
                byte[] uniq = MakeBlock(0x3500 + i);
                byte[] data = Concat(shared, uniq);
                using DataStore s = new DataStore(_tempDir);
                long id = AddImage(s, setName, $"Added{i:D2}.iso", data);
                images.Add((id, data));
            }

            LogShards(setName);

            // --- Phase 4: EVERY image must verify — the pre-compact set and the post-compact adds ---
            // Structural check first (fails on FileId/offset aliasing even if bytes read back OK),
            // then full byte-for-byte content verify of the whole store.
            AssertBlockMapMatchesIndex(setName, images.Select(x => x.id));
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);

            // And once more after a SECOND compact, to catch corruption that only manifests when the
            // post-compact additions are themselves compacted.
            using (DataStore store = new DataStore(_tempDir))
                store.CompactSet(setName);

            AssertBlockMapMatchesIndex(setName, images.Select(x => x.id));
            using (DataStore store = new DataStore(_tempDir))
                foreach ((long id, byte[] data) in images)
                    VerifyImageData(store, setName, id, data);
        }

        /// <summary>
        /// Diagnostic (no correctness assertions beyond basic sanity): prints how many shards were
        /// created, whether the same-CRC duplicate actually deduped (shared block keys), and which
        /// FileId the block index vs each image's block map hold for the shared keys — before and
        /// after compact. This tells us whether the preconditions for the cross-shard aliasing bug
        /// are even being produced by this setup.
        /// </summary>
        [Fact]
        public void Diagnostic_DumpShardAndBlockState()
        {
            const string setName = "DiagSet";

            long bigShard = 50L * 1024 * 1024;
            byte[] blockShared = MakeBlock(0x51);
            byte[] blockAUniq = MakeBlock(0xA1);
            byte[] blockBUniq = MakeBlock(0xB1);
            byte[] blockDUniq = MakeBlock(0xD1);

            byte[] dataD = blockDUniq;
            byte[] dataA = Concat(blockShared, blockAUniq);
            byte[] dataB = Concat(blockShared, blockBUniq);

            long idD, idA, idB;
            try { File.Delete(DiagLog); } catch { }
            using (DataStore store = new DataStore(_tempDir))
            {
                store.CreateSet(setName, shardSize: bigShard, blockSize: BlockSize);
                idD = AddImage(store, setName, "GameD (USA).iso", dataD);
            }
            using (DataStore store = new DataStore(_tempDir)) idA = AddImage(store, setName, "GameA (USA).iso", dataA);
            using (DataStore store = new DataStore(_tempDir)) idB = AddImage(store, setName, "GameB (USA).iso", dataB);

            Log($"ids: D={idD} A={idA} B={idB}");
            using (DataStore store = new DataStore(_tempDir))
            {
                List<ImageRecord> imgs = store.ListImagesInSet(setName);
                Log($"live images: {imgs.Count} -> {string.Join(", ", imgs.Select(i => $"{i.Id}:{i.Name}"))}");
            }
            LogShards(setName);
            DumpBlockState(setName, "BEFORE compact", new[] { idD, idA, idB });

            using (DataStore store = new DataStore(_tempDir))
            {
                store.DeleteImage(new GlobalImageKey(setName, idD));
                store.RenameImage(new GlobalImageKey(setName, idB), "GameB (Renamed).iso");
                store.CompactSet(setName);
            }

            LogShards(setName);
            DumpBlockState(setName, "AFTER compact", new[] { idA, idB });
        }

        private static readonly string DiagLog = Path.Combine(Path.GetTempPath(), "nkds-repro-diag.log");

        private void Log(string s)
        {
            _output.WriteLine(s);
            try { File.AppendAllText(DiagLog, s + Environment.NewLine); } catch { }
        }

        private void DumpBlockState(string setName, string label, long[] imageIds)
        {
            Log($"--- {label} ---");
            // Block index: canonical (FileId, Offset) per key
            string indexPath = Path.Combine(_tempDir, $"{setName}.nkds");
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> indexLoc = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
            if (File.Exists(indexPath))
            {
                using BinaryIndexFile indexFile = BinaryIndexFile.Open(indexPath);
                foreach (BlockIndexEntry e in indexFile.LoadBlockIndex().GetEntries())
                    indexLoc[e.Key] = (e.FileId, e.Offset, e.Size);
                Log($"block index entries: {indexLoc.Count}");
                foreach (KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> kv in indexLoc)
                    Log($"  IDX key={kv.Key} -> file={kv.Value.FileId} off={kv.Value.Offset} size={kv.Value.Size}");
            }

            using DataStore store = new DataStore(_tempDir);
            foreach (long id in imageIds)
            {
                bool exists = store.ListImagesInSet(setName).Any(i => i.Id == id);
                if (!exists) { Log($"  image {id}: (removed)"); continue; }
                try
                {
                    // Read the image's OWN block-map locations directly from the index file so we can
                    // compare (imageMapFileId, imageMapOffset) against the canonical block index.
                    Dictionary<BlockKey, (int FileId, long Offset, int Size)> mapLoc = null;
                    if (File.Exists(indexPath))
                    {
                        using BinaryIndexFile idxf = BinaryIndexFile.Open(indexPath);
                        (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = idxf.ReadImageBlockMap(id);
                        mapLoc = new Dictionary<BlockKey, (int, long, int)>(blockLocations);
                    }
                    Log($"  image {id}: {mapLoc?.Count ?? 0} block-map entries");
                    foreach (KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> kv in mapLoc ?? new Dictionary<BlockKey, (int, long, int)>())
                    {
                        BlockKey k = kv.Key;
                        string mapInfo = $"mapFile={kv.Value.FileId} mapOff={kv.Value.Offset}";
                        string idxInfo = indexLoc.TryGetValue(k, out (int FileId, long Offset, int Size) loc) ? $"idxFile={loc.FileId} idxOff={loc.Offset}" : "NOT-IN-INDEX";
                        bool mismatch = indexLoc.TryGetValue(k, out (int FileId, long Offset, int Size) l2) && (l2.FileId != kv.Value.FileId || l2.Offset != kv.Value.Offset);
                        Log($"     key={k} {mapInfo} | {idxInfo}{(mismatch ? "  <<< MISMATCH" : "")}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"  image {id}: EXCEPTION reading map: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// REAL-DATA reproduction. Points at a pre-built binary NKDS set on disk (created by
        /// `nkit dedupe` against real Wii/GameCube images across multiple sessions with a small
        /// shard size, including a same-CRC duplicate pair). Reconstructs every image's full byte
        /// stream (SHA1) BEFORE compact, runs CompactSet, then reconstructs again and asserts the
        /// hashes are unchanged. Any change = the compact corrupted that image (the user's bug).
        ///
        /// Set the env var NKDS_REPRO_STORE to the datastore directory to enable; skips otherwise so
        /// it never fails in CI without the asset.
        /// </summary>
        [Fact]
        public void RealStore_CompactPreservesEveryImage()
        {
            string storeDir = Environment.GetEnvironmentVariable("NKDS_REPRO_STORE");
            if (string.IsNullOrEmpty(storeDir) || !Directory.Exists(storeDir))
            {
                _output.WriteLine($"SKIP: set NKDS_REPRO_STORE to a datastore dir (was '{storeDir}').");
                return;
            }

            try { File.Delete(DiagLog); } catch { }

            // Hash every image before compact.
            Dictionary<(string set, long id), string> before = new Dictionary<(string set, long id), string>();
            List<string> setNames = new List<string>();
            using (DataStore store = new DataStore(storeDir))
            {
                foreach (string set in store.ListSetNames())
                {
                    setNames.Add(set);
                    foreach (ImageRecord img in store.ListImagesInSet(set))
                        before[(set, img.Id)] = HashImage(store, set, img.Id);
                }
            }
            Log($"images hashed before compact: {before.Count} across sets [{string.Join(", ", setNames)}]");
            foreach (KeyValuePair<(string set, long id), string> kv in before) Log($"  BEFORE {kv.Key.set}/{kv.Key.id} = {kv.Value}");

            // Compact each set (the reported trigger).
            using (DataStore store = new DataStore(storeDir))
                foreach (string set in setNames)
                {
                    Log($"compacting set '{set}' ...");
                    store.CompactSet(set);
                }

            // Re-hash and compare.
            List<string> mismatches = new List<string>();
            using (DataStore store = new DataStore(storeDir))
            {
                foreach (KeyValuePair<(string set, long id), string> kv in before)
                {
                    string after;
                    try { after = HashImage(store, kv.Key.set, kv.Key.id); }
                    catch (Exception ex) { after = $"EXCEPTION: {ex.Message}"; }
                    bool ok = after == kv.Value;
                    Log($"  AFTER  {kv.Key.set}/{kv.Key.id} = {after} {(ok ? "OK" : "<<< CHANGED (was " + kv.Value + ")")}");
                    if (!ok) mismatches.Add($"{kv.Key.set}/{kv.Key.id}: before={kv.Value} after={after}");
                }
            }

            Assert.True(mismatches.Count == 0,
                $"Compact changed {mismatches.Count} image(s):{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
        }

        private static string HashImage(DataStore store, string setName, long imageId)
        {
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey(setName, imageId));
            using Stream stream = reader.OpenStream(0);
            using SHA1 sha = SHA1.Create();
            byte[] buf = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                sha.TransformBlock(buf, 0, read, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash);
        }

        #region Helpers

        /// <summary>Deterministic pseudo-random data so identical seeds produce identical (dedup-able) content.</summary>
        private static byte[] MakeData(int seed, int blocks)
        {
            byte[] data = new byte[BlockSize * blocks];
            Random rnd = new Random(seed);
            rnd.NextBytes(data);
            return data;
        }

        /// <summary>A single deterministic block whose content is fixed by <paramref name="seed"/> (same seed = dedupable).</summary>
        private static byte[] MakeBlock(int seed) => MakeData(seed, 1);

        private static byte[] Concat(params byte[][] parts)
        {
            int len = parts.Sum(p => p.Length);
            byte[] result = new byte[len];
            int pos = 0;
            foreach (byte[] p in parts) { Array.Copy(p, 0, result, pos, p.Length); pos += p.Length; }
            return result;
        }

        private static long AddImage(DataStore store, string setName, string name, byte[] data)
        {
            using (IImageWriter writer = store.AddImage(setName, name))
            {
                writer.WriteData(0, data, BlockType.File);
                writer.FinalizeImage(data.Length, 0, 0);
            }
            TestDataStoreHelper.WaitForSetIdle(store, setName);

            // Resolve the id we just created (highest id with this name).
            ImageRecord img = store.ListImagesInSet(setName)
                .Where(i => string.Equals(i.Name, name, StringComparison.Ordinal))
                .OrderByDescending(i => i.Id)
                .First();
            return img.Id;
        }

        private void LogShards(string setName)
        {
            List<string> shards = Directory.GetFiles(_tempDir, $"{setName}_*.nkds")
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();
            Log($"Shard files for '{setName}': {shards.Count}");
            foreach (string s in shards)
                Log($"  {Path.GetFileName(s)} ({new FileInfo(s).Length} bytes)");
        }

        /// <summary>
        /// Structural integrity check: for every live image, assert that each block in its per-image
        /// block map resolves to the SAME (FileId, Offset) as the canonical block index. This catches
        /// the suspected cross-shard aliasing bug directly — the moment compaction stamps a block's new
        /// offset onto an image while keeping the image's own (different) FileId, the block map and the
        /// block index disagree for that key. It fails even if the bytes happen to read back correctly.
        /// Reads only small index-file metadata (no image decode), so it's near-instant.
        /// </summary>
        private void AssertBlockMapMatchesIndex(string setName, IEnumerable<long> imageIds)
        {
            string indexPath = Path.Combine(_tempDir, $"{setName}.nkds");
            Assert.True(File.Exists(indexPath), $"Index file not found for set '{setName}' at {indexPath}");

            // Canonical (FileId, Offset, Size) per block key, from the block index.
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> indexLoc = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
            using (BinaryIndexFile indexFile = BinaryIndexFile.Open(indexPath))
            {
                foreach (BlockIndexEntry e in indexFile.LoadBlockIndex().GetEntries())
                    indexLoc[e.Key] = (e.FileId, e.Offset, e.Size);

                List<string> mismatches = new List<string>();
                foreach (long id in imageIds)
                {
                    (List<OffsetRecord> _, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = indexFile.ReadImageBlockMap(id);
                    foreach (KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> kv in blockLocations)
                    {
                        if (!indexLoc.TryGetValue(kv.Key, out (int FileId, long Offset, int Size) idx))
                        {
                            mismatches.Add($"image {id} key={kv.Key}: block map references a key NOT in the block index");
                            continue;
                        }
                        if (idx.FileId != kv.Value.FileId || idx.Offset != kv.Value.Offset)
                        {
                            mismatches.Add(
                                $"image {id} key={kv.Key}: block map (file={kv.Value.FileId}, off={kv.Value.Offset}) " +
                                $"!= block index (file={idx.FileId}, off={idx.Offset})");
                        }
                    }
                }

                Assert.True(mismatches.Count == 0,
                    $"Block map / block index (FileId, Offset) mismatch for {mismatches.Count} entr(y/ies):" +
                    Environment.NewLine + string.Join(Environment.NewLine, mismatches));
            }
        }

        /// <summary>
        /// Asserts the set's block index spans more than one shard FileId, so the cross-shard aliasing
        /// checks are meaningful (not vacuously satisfied by a single-shard layout).
        /// </summary>
        private void AssertMultipleShardsUsed(string setName)
        {
            string indexPath = Path.Combine(_tempDir, $"{setName}.nkds");
            Assert.True(File.Exists(indexPath), $"Index file not found for set '{setName}' at {indexPath}");
            using BinaryIndexFile indexFile = BinaryIndexFile.Open(indexPath);
            List<int> fileIds = indexFile.LoadBlockIndex().GetEntries().Select(e => e.FileId).Distinct().ToList();
            Assert.True(fileIds.Count > 1,
                $"Expected a multi-shard layout (>1 FileId) so cross-shard aliasing is exercised, " +
                $"but the block index used only {fileIds.Count} FileId(s): [{string.Join(", ", fileIds)}]. " +
                $"Reduce ShardSize or increase image count.");
        }

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