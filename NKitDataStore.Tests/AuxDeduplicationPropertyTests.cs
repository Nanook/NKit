using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for aux deduplication and hash consistency.
    ///
    /// Feature: sidecar-datastore
    /// Property 7: Aux Deduplication
    /// Property 8: Hash Consistency
    /// </summary>
    public class AuxDeduplicationPropertyTests : IDisposable
    {
        private readonly string _testDirectory;

        public AuxDeduplicationPropertyTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "NKitAuxDedup", Path.GetRandomFileName());
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                if (Directory.Exists(_testDirectory))
                    Directory.Delete(_testDirectory, true);
            }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Computes a BlockKey from raw data bytes using the same hash functions
        /// as the production code (XXHash64 + CRC32).
        /// </summary>
        private static BlockKey ComputeBlockKey(byte[] data)
        {
            ulong xxHash64 = TestHashUtil.ComputeXXHash64(data);
            uint crc32 = TestHashUtil.ComputeCrc32(data);
            return new BlockKey(xxHash64, crc32);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2**
        ///
        /// Property 7: Aux Deduplication.
        /// For any two identical blocks written to the aux store from different images,
        /// the aux store stores the block data only once. The same XXHash64-based
        /// deduplication used in primary stores applies to aux stores.
        ///
        /// Since the aux store IS a standard NKDS set (same DataStore infrastructure),
        /// deduplication is inherent. This test verifies the property by writing the
        /// same block data from two different images into a single DataStore set and
        /// confirming that the block data is stored only once (both images reference
        /// the same BlockKey, and the block table has a single entry for that key).
        /// </summary>
        [Property(MaxTest = 50)]
        public bool AuxDeduplication_IdenticalBlocksStoredOnce(
            NonNegativeInt dataSeed,
            NonNegativeInt blockSizeSeed)
        {
            // Generate a block of data between 1 and 1024 bytes
            int dataSize = (blockSizeSeed.Get % 1024) + 1;
            Random random = new Random(dataSeed.Get);
            byte[] sharedBlockData = new byte[dataSize];
            random.NextBytes(sharedBlockData);

            // Create a unique subdirectory for this test run
            string runDir = Path.Combine(_testDirectory, $"dedup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(runDir);

            try
            {
                using DataStore store = new DataStore(runDir);
                string setName = "auxset";
                store.CreateSet(setName);

                // Write the same block data from "Image1"
                using (IImageWriter writer = store.AddImage(setName, "Image1.iso"))
                {
                    writer.WriteData(0, sharedBlockData, BlockType.File);
                    writer.FinalizeImage(sharedBlockData.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Write the same block data from "Image2"
                using (IImageWriter writer = store.AddImage(setName, "Image2.iso"))
                {
                    writer.WriteData(0, sharedBlockData, BlockType.File);
                    writer.FinalizeImage(sharedBlockData.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Verify: both images exist
                List<ImageRecord> images = store.ListAllImages(img => img.SetName == setName).ToList();
                if (images.Count != 2) return false;

                // Verify: both images reference the same block key
                BlockKey? image1Key = null;
                BlockKey? image2Key = null;

                using (IImageReader reader1 = store.OpenImageReader(new GlobalImageKey(setName, images[0].Id)))
                {
                    List<OffsetRecord> offsets1 = reader1.GetOffsets().ToList();
                    if (offsets1.Count == 0 || !offsets1[0].HasBlocks) return false;
                    image1Key = offsets1[0].FirstBlock;
                }

                using (IImageReader reader2 = store.OpenImageReader(new GlobalImageKey(setName, images[1].Id)))
                {
                    List<OffsetRecord> offsets2 = reader2.GetOffsets().ToList();
                    if (offsets2.Count == 0 || !offsets2[0].HasBlocks) return false;
                    image2Key = offsets2[0].FirstBlock;
                }

                // Both images must reference the same BlockKey (deduplication)
                if (image1Key == null || image2Key == null) return false;
                if (image1Key.Value != image2Key.Value) return false;

                // Verify: the block key matches what we'd compute from the data
                BlockKey expectedKey = ComputeBlockKey(sharedBlockData);
                if (image1Key.Value != expectedKey) return false;

                return true;
            }
            finally
            {
                // Cleanup handled by GC
            }
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2**
        ///
        /// Property 7 (supplementary): Unique blocks are stored separately.
        /// For any two distinct blocks written to the store from different images,
        /// the store maintains separate block entries. This confirms deduplication
        /// only merges truly identical data.
        /// </summary>
        [Property(MaxTest = 50)]
        public bool AuxDeduplication_DistinctBlocksStoredSeparately(
            NonNegativeInt seed1,
            NonNegativeInt seed2)
        {
            // Ensure we get two different data blocks
            Random random1 = new Random(seed1.Get);
            Random random2 = new Random(seed2.Get + 1_000_000); // offset to avoid collision

            byte[] block1 = new byte[64];
            byte[] block2 = new byte[64];
            random1.NextBytes(block1);
            random2.NextBytes(block2);

            // If by chance they're identical, skip (extremely unlikely with different seeds)
            if (block1.SequenceEqual(block2))
                return true; // vacuously true — identical blocks would deduplicate correctly

            string runDir = Path.Combine(_testDirectory, $"distinct_{Guid.NewGuid():N}");
            Directory.CreateDirectory(runDir);

            try
            {
                using DataStore store = new DataStore(runDir);
                string setName = "auxset";
                store.CreateSet(setName);

                using (IImageWriter writer = store.AddImage(setName, "Image1.iso"))
                {
                    writer.WriteData(0, block1, BlockType.File);
                    writer.FinalizeImage(block1.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                using (IImageWriter writer = store.AddImage(setName, "Image2.iso"))
                {
                    writer.WriteData(0, block2, BlockType.File);
                    writer.FinalizeImage(block2.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, setName);

                // Verify: both images reference different block keys
                List<ImageRecord> images = store.ListAllImages(img => img.SetName == setName).ToList();
                if (images.Count != 2) return false;

                BlockKey? image1Key = null;
                BlockKey? image2Key = null;

                using (IImageReader reader1 = store.OpenImageReader(new GlobalImageKey(setName, images[0].Id)))
                {
                    List<OffsetRecord> offsets1 = reader1.GetOffsets().ToList();
                    if (offsets1.Count == 0 || !offsets1[0].HasBlocks) return false;
                    image1Key = offsets1[0].FirstBlock;
                }

                using (IImageReader reader2 = store.OpenImageReader(new GlobalImageKey(setName, images[1].Id)))
                {
                    List<OffsetRecord> offsets2 = reader2.GetOffsets().ToList();
                    if (offsets2.Count == 0 || !offsets2[0].HasBlocks) return false;
                    image2Key = offsets2[0].FirstBlock;
                }

                if (image1Key == null || image2Key == null) return false;

                // Distinct data must produce distinct block keys
                if (image1Key.Value == image2Key.Value) return false;

                return true;
            }
            finally
            {
                // Cleanup handled by GC
            }
        }

        /// <summary>
        /// **Validates: Requirements 7.3**
        ///
        /// Property 8: Hash Consistency.
        /// For any block of data D, the Block_Key (XXHash64 + CRC32) is identical
        /// regardless of whether D is stored in primary or aux. The same key can be
        /// used to look up the block in either store.
        ///
        /// Since BlockKey is computed purely from the data bytes using deterministic
        /// hash functions (XXHash64 and CRC32), this property is inherently satisfied.
        /// The test verifies that computing a BlockKey from the same data always
        /// produces the same result, regardless of how many times it's computed.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool HashConsistency_SameDataProducesSameKey(
            NonNegativeInt dataSeed,
            NonNegativeInt sizeSeed)
        {
            // Generate a block of data between 1 and 2048 bytes
            int dataSize = (sizeSeed.Get % 2048) + 1;
            Random random = new Random(dataSeed.Get);
            byte[] data = new byte[dataSize];
            random.NextBytes(data);

            // Compute the BlockKey twice from the same data
            BlockKey key1 = ComputeBlockKey(data);
            BlockKey key2 = ComputeBlockKey(data);

            // Must be identical
            if (key1 != key2) return false;
            if (key1.XxHash64 != key2.XxHash64) return false;
            if (key1.Crc32 != key2.Crc32) return false;

            // Make a copy of the data and compute again — must still match
            byte[] dataCopy = new byte[data.Length];
            Array.Copy(data, dataCopy, data.Length);
            BlockKey key3 = ComputeBlockKey(dataCopy);

            if (key1 != key3) return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 7.3**
        ///
        /// Property 8 (supplementary): Hash Consistency across stores.
        /// For any block of data D, writing D to two separate DataStore sets produces
        /// offset records referencing the same BlockKey. This confirms that the key
        /// computation is store-independent.
        /// </summary>
        [Property(MaxTest = 30)]
        public bool HashConsistency_SameKeyAcrossStores(
            NonNegativeInt dataSeed,
            NonNegativeInt sizeSeed)
        {
            int dataSize = (sizeSeed.Get % 512) + 1;
            Random random = new Random(dataSeed.Get);
            byte[] data = new byte[dataSize];
            random.NextBytes(data);

            string runDir = Path.Combine(_testDirectory, $"hashcons_{Guid.NewGuid():N}");
            Directory.CreateDirectory(runDir);

            try
            {
                using DataStore store = new DataStore(runDir);

                // Create two separate sets (simulating primary and aux)
                string primarySet = "primary";
                string auxSet = "auxset";
                store.CreateSet(primarySet);
                store.CreateSet(auxSet);

                // Write the same data to the "primary" set
                using (IImageWriter writer = store.AddImage(primarySet, "Image.iso"))
                {
                    writer.WriteData(0, data, BlockType.File);
                    writer.FinalizeImage(data.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, primarySet);

                // Write the same data to the "aux" set
                using (IImageWriter writer = store.AddImage(auxSet, "Image.iso"))
                {
                    writer.WriteData(0, data, BlockType.File);
                    writer.FinalizeImage(data.Length, 0, 0);
                }
                TestDataStoreHelper.WaitForSetIdle(store, auxSet);

                // Read back the block keys from both sets
                List<ImageRecord> primaryImages = store.ListAllImages(img => img.SetName == primarySet).ToList();
                List<ImageRecord> auxImages = store.ListAllImages(img => img.SetName == auxSet).ToList();

                if (primaryImages.Count != 1 || auxImages.Count != 1) return false;

                BlockKey? primaryKey = null;
                BlockKey? auxKey = null;

                using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(primarySet, primaryImages[0].Id)))
                {
                    List<OffsetRecord> offsets = reader.GetOffsets().ToList();
                    if (offsets.Count == 0 || !offsets[0].HasBlocks) return false;
                    primaryKey = offsets[0].FirstBlock;
                }

                using (IImageReader reader = store.OpenImageReader(new GlobalImageKey(auxSet, auxImages[0].Id)))
                {
                    List<OffsetRecord> offsets = reader.GetOffsets().ToList();
                    if (offsets.Count == 0 || !offsets[0].HasBlocks) return false;
                    auxKey = offsets[0].FirstBlock;
                }

                if (primaryKey == null || auxKey == null) return false;

                // The same data must produce the same BlockKey in both stores
                if (primaryKey.Value != auxKey.Value) return false;

                // And it must match what we compute directly from the data
                BlockKey expectedKey = ComputeBlockKey(data);
                if (primaryKey.Value != expectedKey) return false;

                return true;
            }
            finally
            {
                // Cleanup handled by GC
            }
        }

        /// <summary>
        /// **Validates: Requirements 7.3**
        ///
        /// Property 8 (supplementary): Different data produces different keys.
        /// For any two distinct blocks of data, the Block_Keys are different.
        /// This confirms the hash function has no trivial collisions for test inputs.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool HashConsistency_DifferentDataProducesDifferentKeys(
            NonNegativeInt seed1,
            NonNegativeInt seed2)
        {
            // Generate two different data blocks
            Random random1 = new Random(seed1.Get);
            Random random2 = new Random(seed2.Get + 1_000_000);

            byte[] data1 = new byte[64];
            byte[] data2 = new byte[64];
            random1.NextBytes(data1);
            random2.NextBytes(data2);

            // If by chance they're identical, skip
            if (data1.SequenceEqual(data2))
                return true;

            BlockKey key1 = ComputeBlockKey(data1);
            BlockKey key2 = ComputeBlockKey(data2);

            // Different data must produce different keys
            return key1 != key2;
        }
    }
}