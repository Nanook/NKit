using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for Block_Index serialization round-trip.
    ///
    /// Feature: binary-index-format
    /// Property 5: Block_Index sorted invariant and round-trip
    /// **Validates: Requirements 5.1, 5.5, 16.4**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class BlockIndexSerializerTests
    {
        private static BlockIndexEntry[] GenerateEntries(int seed)
        {
            Random rng = new Random(seed);
            int count = rng.Next(0, 51); // 0 to 50 entries
            BlockIndexEntry[] entries = new BlockIndexEntry[count];

            for (int i = 0; i < count; i++)
            {
                entries[i] = new BlockIndexEntry
                {
                    Key = new BlockKey(
                        ((ulong)(uint)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue),
                        (uint)rng.Next(int.MinValue, int.MaxValue)),
                    FileId = rng.Next(0, 100),
                    Offset = (long)rng.Next(0, int.MaxValue / 2),
                    Size = rng.Next(1, 0x100000),
                };
            }

            return entries;
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.5, 16.4**
        ///
        /// Property 5: Block_Index sorted invariant and round-trip.
        /// For any valid array of BlockIndexEntry structs (each with arbitrary BlockKey,
        /// FileId, Offset, Size), serializing as a Block_Index (which sorts
        /// by BlockKey) and deserializing SHALL produce an array with identical entries in
        /// ascending BlockKey order, preserving all field values.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool BlockIndexSortedInvariantAndRoundTrip(NonNegativeInt seed)
        {
            BlockIndexEntry[] entries = GenerateEntries(seed.Get);

            // Serialize (sorts internally by BlockKey)
            byte[] serialized = BlockIndexSerializer.Serialize(entries);

            // Deserialize
            BlockIndexEntry[] deserialized = BlockIndexSerializer.Deserialize(serialized);

            // Build expected: sort input by (XxHash64, Crc32) ascending
            BlockIndexEntry[] expected = new BlockIndexEntry[entries.Length];
            Array.Copy(entries, expected, entries.Length);
            Array.Sort(expected);

            // Verify count matches
            if (deserialized.Length != expected.Length)
                return false;

            // Verify result is sorted by (XxHash64, Crc32) ascending
            for (int i = 1; i < deserialized.Length; i++)
            {
                if (deserialized[i - 1].CompareTo(deserialized[i]) > 0)
                    return false;
            }

            // Verify all entries from input are present with identical field values
            for (int i = 0; i < expected.Length; i++)
            {
                if (deserialized[i].Key.XxHash64 != expected[i].Key.XxHash64)
                    return false;
                if (deserialized[i].Key.Crc32 != expected[i].Key.Crc32)
                    return false;
                if (deserialized[i].FileId != expected[i].FileId)
                    return false;
                if (deserialized[i].Offset != expected[i].Offset)
                    return false;
                if (deserialized[i].Size != expected[i].Size)
                    return false;
            }

            return true;
        }
    }
}