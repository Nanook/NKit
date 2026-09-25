using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using NKitDataStore.Binary;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for InMemoryBlockIndex operations.
    ///
    /// Feature: binary-index-format
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class InMemoryBlockIndexTests
    {
        /// <summary>
        /// Generates a sorted array of 0-50 unique BlockIndexEntry values with distinct keys.
        /// </summary>
        private static BlockIndexEntry[] GenerateSortedEntries(int seed, out BlockKey[] keysInIndex)
        {
            Random rng = new Random(seed);
            int count = rng.Next(0, 51); // 0 to 50 entries

            // Generate entries with unique keys
            HashSet<(ulong, uint)> keySet = new System.Collections.Generic.HashSet<(ulong, uint)>();
            List<BlockIndexEntry> entries = new System.Collections.Generic.List<BlockIndexEntry>();

            while (entries.Count < count)
            {
                ulong xxHash64 = ((ulong)(uint)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue);
                uint crc32 = (uint)rng.Next(int.MinValue, int.MaxValue);

                if (!keySet.Add((xxHash64, crc32)))
                    continue; // Skip duplicate keys

                entries.Add(new BlockIndexEntry
                {
                    Key = new BlockKey(xxHash64, crc32),
                    FileId = rng.Next(0, 100),
                    Offset = (long)rng.Next(0, int.MaxValue / 2),
                    Size = rng.Next(1, 0x100000),
                });
            }

            BlockIndexEntry[] sorted = entries.ToArray();
            Array.Sort(sorted);
            keysInIndex = sorted.Select(e => e.Key).ToArray();
            return sorted;
        }

        /// <summary>
        /// Generates random BlockKeys that are NOT in the provided set.
        /// </summary>
        private static BlockKey[] GenerateAbsentKeys(int seed, System.Collections.Generic.HashSet<(ulong, uint)> existingKeys, int count)
        {
            Random rng = new Random(seed);
            List<BlockKey> absentKeys = new System.Collections.Generic.List<BlockKey>();

            while (absentKeys.Count < count)
            {
                ulong xxHash64 = ((ulong)(uint)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue);
                uint crc32 = (uint)rng.Next(int.MinValue, int.MaxValue);

                if (!existingKeys.Contains((xxHash64, crc32)))
                    absentKeys.Add(new BlockKey(xxHash64, crc32));
            }

            return absentKeys.ToArray();
        }

        /// <summary>
        /// **Validates: Requirements 5.3**
        ///
        /// Property 7: Block_Index binary search correctness.
        /// For any valid InMemoryBlockIndex containing N entries and any BlockKey K:
        /// if K exists in the index, TryGetBlock(K) SHALL return true with the correct
        /// (fileId, offset, size); if K does not exist in the index, TryGetBlock(K) SHALL
        /// return false.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BinarySearchReturnsCorrectResultForExistingKeys(NonNegativeInt seed)
        {
            BlockIndexEntry[] sortedEntries = GenerateSortedEntries(seed.Get, out BlockKey[] keysInIndex);
            InMemoryBlockIndex index = new InMemoryBlockIndex(sortedEntries);

            // Test 1: For each entry in the index, verify TryGetBlock returns true with correct values
            for (int i = 0; i < sortedEntries.Length; i++)
            {
                ref BlockIndexEntry expected = ref sortedEntries[i];
                bool found = index.TryGetBlock(expected.Key, out int fileId, out long offset, out int size);

                if (!found)
                    return false;
                if (fileId != expected.FileId)
                    return false;
                if (offset != expected.Offset)
                    return false;
                if (size != expected.Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 5.3**
        ///
        /// Property 7: Block_Index binary search correctness.
        /// For any valid InMemoryBlockIndex containing N entries and any BlockKey K:
        /// if K does not exist in the index, TryGetBlock(K) SHALL return false.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BinarySearchReturnsFalseForAbsentKeys(NonNegativeInt seed)
        {
            BlockIndexEntry[] sortedEntries = GenerateSortedEntries(seed.Get, out BlockKey[] keysInIndex);
            InMemoryBlockIndex index = new InMemoryBlockIndex(sortedEntries);

            // Build set of existing keys for exclusion
            HashSet<(ulong, uint)> existingKeys = new System.Collections.Generic.HashSet<(ulong, uint)>(
                keysInIndex.Select(k => (k.XxHash64, k.Crc32)));

            // Test 2: Generate random BlockKeys NOT in the index, verify TryGetBlock returns false
            int absentCount = Math.Max(1, sortedEntries.Length);
            BlockKey[] absentKeys = GenerateAbsentKeys(seed.Get + 7919, existingKeys, absentCount);

            for (int i = 0; i < absentKeys.Length; i++)
            {
                bool found = index.TryGetBlock(absentKeys[i], out int fileId, out long offset, out int size);

                if (found)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 6.3**
        ///
        /// MergeDelta: when the same BlockKey exists in both the main index and the delta,
        /// the delta entry (newer entry) wins unconditionally.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MergeDelta_NewerEntryWins_ForDuplicateKeys(NonNegativeInt seed)
        {
            BlockIndexEntry[] mainEntries = GenerateSortedEntries(seed.Get, out BlockKey[] keysInIndex);
            if (mainEntries.Length == 0)
                return true; // Nothing to test with empty index

            InMemoryBlockIndex index = new InMemoryBlockIndex(mainEntries);

            // Create delta entries that share some keys with the main index but have different values
            Random rng = new Random(seed.Get + 42);
            int overlapCount = Math.Min(mainEntries.Length, rng.Next(1, Math.Max(2, mainEntries.Length)));
            BlockIndexEntry[] deltaEntries = new BlockIndexEntry[overlapCount];

            for (int i = 0; i < overlapCount; i++)
            {
                // Use the same key but different FileId, Offset, Size
                deltaEntries[i] = new BlockIndexEntry
                {
                    Key = mainEntries[i].Key,
                    FileId = mainEntries[i].FileId + 1000, // Distinct from main
                    Offset = mainEntries[i].Offset + 999999,
                    Size = mainEntries[i].Size + 500,
                };
            }

            Array.Sort(deltaEntries);

            BlockIndexDelta delta = new BlockIndexDelta { Entries = deltaEntries };
            index.MergeDelta(delta);

            // Verify that for each overlapping key, the delta entry's values are present
            for (int i = 0; i < overlapCount; i++)
            {
                bool found = index.TryGetBlock(deltaEntries[i].Key, out int fileId, out long offset, out int size);
                if (!found)
                    return false;
                if (fileId != deltaEntries[i].FileId)
                    return false;
                if (offset != deltaEntries[i].Offset)
                    return false;
                if (size != deltaEntries[i].Size)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.2, 6.3**
        ///
        /// AddEntry: when a new entry is added with a BlockKey that already exists,
        /// the new entry unconditionally replaces the existing entry (newer wins).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AddEntry_NewerEntryWins_ForDuplicateKey(NonNegativeInt seed)
        {
            BlockIndexEntry[] mainEntries = GenerateSortedEntries(seed.Get, out BlockKey[] keysInIndex);
            if (mainEntries.Length == 0)
                return true; // Nothing to test with empty index

            InMemoryBlockIndex index = new InMemoryBlockIndex(mainEntries);

            // Pick a random existing entry and add a new entry with the same key but different values
            Random rng = new Random(seed.Get + 101);
            int targetIdx = rng.Next(0, mainEntries.Length);
            BlockIndexEntry existingEntry = mainEntries[targetIdx];

            BlockIndexEntry newEntry = new BlockIndexEntry
            {
                Key = existingEntry.Key,
                FileId = existingEntry.FileId + 2000,
                Offset = existingEntry.Offset + 777777,
                Size = existingEntry.Size + 300,
            };

            index.AddEntry(newEntry);

            // Verify the new entry's values are returned
            bool found = index.TryGetBlock(newEntry.Key, out int fileId, out long offset, out int size);
            if (!found)
                return false;
            if (fileId != newEntry.FileId)
                return false;
            if (offset != newEntry.Offset)
                return false;
            if (size != newEntry.Size)
                return false;

            // Verify total count didn't increase (replacement, not addition)
            if (index.Count != mainEntries.Length)
                return false;

            return true;
        }
    }
}