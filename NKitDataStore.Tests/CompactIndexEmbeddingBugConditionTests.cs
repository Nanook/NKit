using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Interfaces;
using System.Collections.Concurrent;
using System.Reflection;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Bug condition exploration test for CompactSet index embedding.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    ///
    /// Property 1: Bug Condition — Separate-Mode Sets Incorrectly Route to compactSetEmbedded
    ///
    /// This test is EXPECTED TO FAIL on unfixed code. Failure confirms the bug exists:
    /// when CompactSet is called on a set with shardSize > 0 (separate mode) and the
    /// _embeddedMode cache contains a stale `true` value, the system incorrectly routes
    /// to compactSetEmbedded which re-embeds the index into the shard file.
    ///
    /// Expected behavior: CompactSet should route to compactSetSeparate, leaving the
    /// index as a standalone file without embedding it into the shard.
    /// </summary>
    public class CompactIndexEmbeddingBugConditionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public CompactIndexEmbeddingBugConditionTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitCompactEmbedBug_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 1: Bug Condition — For any separate-mode set (shardSize > 0) where
        /// _embeddedMode cache contains a stale `true` value, calling CompactSet must
        /// leave the index as a standalone separate file (route to compactSetSeparate).
        ///
        /// On unfixed code, this FAILS because CompactSet relies solely on the
        /// _embeddedMode dictionary for routing. When _embeddedMode[setName] == true,
        /// it routes to compactSetEmbedded which re-embeds the index into the shard,
        /// corrupting the separate-mode file layout.
        ///
        /// The shardSize is varied using FsCheck to demonstrate the bug across
        /// different separate-mode configurations.
        /// </summary>
        [Property(MaxTest = 10)]
        public bool CompactSet_SeparateMode_WithStaleCacheTrue_MustNotEmbedIndex(PositiveInt shardSizeMultiplier)
        {
            // Choose a shardSize > 0 (separate mode) — vary between 64KiB and 512KiB
            long shardSize = (long)((shardSizeMultiplier.Get % 8) + 1) * 65536;

            // Create a unique subdirectory per iteration to avoid collisions
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            string setName = "TestSet";

            // Step 1: Create a separate-mode set and add images
            using (DataStore store = new DataStore(testDir))
            {
                store.CreateSet(setName, shardSize: shardSize, blockSize: 65536);

                // Add 3 images with data
                for (int i = 1; i <= 3; i++)
                {
                    byte[] data = new byte[65536];
                    new Random(i * 42).NextBytes(data);
                    using (IImageWriter writer = store.AddImage(setName, $"Image{i}"))
                    {
                        writer.WriteData(0, data, BlockType.File);
                        writer.FinalizeImage(data.Length, (uint)(i * 1111), (ulong)(i * 2222));
                    }
                }

                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Step 2: Delete one image so CompactSet has work to do
                store.DeleteImage(new GlobalImageKey(setName, 2));
            }

            // Step 3: Reopen the store, inject stale _embeddedMode = true, then call CompactSet
            using (DataStore store = new DataStore(testDir))
            {
                // Access the internal BinaryDataStoreDataAccess
                BinaryDataStoreDataAccess dataAccess = (BinaryDataStoreDataAccess)store.DataAccess;

                // Force the index file to be loaded (so the set is initialized)
                store.GetSetInfo(setName);

                // Use reflection to access the private _embeddedMode dictionary
                // and inject a stale `true` value to simulate the bug condition
                FieldInfo embeddedModeField = typeof(BinaryDataStoreDataAccess)
                    .GetField("_embeddedMode", BindingFlags.NonPublic | BindingFlags.Instance);
                ConcurrentDictionary<string, bool> embeddedModeDict = (ConcurrentDictionary<string, bool>)embeddedModeField!.GetValue(dataAccess)!;

                // Inject stale cache: set _embeddedMode[setName] = true
                // This simulates the condition where a previous code path (e.g., crash recovery,
                // reEmbedAfterCommit) incorrectly set the cache to true for a separate-mode set
                embeddedModeDict[setName] = true;

                // Step 4: Call CompactSet — on unfixed code, this will route to compactSetEmbedded
                store.CompactSet(setName);
            }

            // Step 5: Verify the file layout is correct (separate mode preserved)
            string indexFilePath = Path.Combine(testDir, $"{setName}.nkds");
            string shardFilePath = Path.Combine(testDir, $"{setName}_0000.nkds");

            // Assert 1: The index file must still exist as a standalone separate file
            bool indexFileExists = File.Exists(indexFilePath);

            // Assert 2: The index file must NOT have an EmbeddedFooter
            // (if compactSetEmbedded ran, it would have re-embedded the index into the shard)
            bool indexFileHasNoFooter = true;
            if (indexFileExists)
            {
                indexFileHasNoFooter = !HasEmbeddedFooter(indexFilePath);
            }

            // Assert 3: The shard file (if it exists) must NOT contain an EmbeddedFooter
            bool shardFileHasNoFooter = true;
            if (File.Exists(shardFilePath))
            {
                shardFileHasNoFooter = !HasEmbeddedFooter(shardFilePath);
            }

            // Assert 4: The header's ShardSize field must remain > 0 (unchanged)
            // If the file was corrupted by compactSetEmbedded, opening it will throw —
            // that corruption IS proof of the bug
            bool headerShardSizePreserved = false;
            if (indexFileExists)
            {
                try
                {
                    using BinaryIndexFile idxFile = BinaryIndexFile.Open(indexFilePath);
                    headerShardSizePreserved = idxFile.Header.ShardSize > 0;
                }
                catch (InvalidDataException ex)
                {
                    // The index file is corrupted — compactSetEmbedded destroyed it
                    _output.WriteLine(
                        $"COUNTEREXAMPLE: shardSize={shardSize}. " +
                        $"Index file is CORRUPTED after CompactSet: {ex.Message}. " +
                        $"Bug confirmed: compactSetEmbedded was incorrectly called on a separate-mode set, " +
                        $"corrupting the standalone index file.");
                    return false;
                }
            }

            bool allPassed = indexFileExists && indexFileHasNoFooter && shardFileHasNoFooter && headerShardSizePreserved;

            if (!allPassed)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE: shardSize={shardSize}, " +
                    $"indexFileExists={indexFileExists}, " +
                    $"indexFileHasNoFooter={indexFileHasNoFooter}, " +
                    $"shardFileHasNoFooter={shardFileHasNoFooter}, " +
                    $"headerShardSizePreserved={headerShardSizePreserved}. " +
                    $"Bug confirmed: CompactSet with stale _embeddedMode=true on a separate-mode set " +
                    $"incorrectly routed to compactSetEmbedded, embedding the index into the shard file.");
            }

            return allPassed;
        }

        /// <summary>
        /// Checks if a file has an EmbeddedFooter (last 4 bytes match the NKDS magic).
        /// </summary>
        private static bool HasEmbeddedFooter(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < EmbeddedFooter.FooterSize)
                return false;

            Span<byte> magicBuffer = stackalloc byte[4];
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(-4, SeekOrigin.End);
            int bytesRead = stream.Read(magicBuffer);
            if (bytesRead < 4)
                return false;

            return EmbeddedFooter.IsMagicValid(magicBuffer);
        }
    }
}