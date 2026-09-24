using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NKit.Tests.Binary.Generators;
using NKitDataStore;
using NKitDataStore.Binary.Serialization;
using System.Collections.Generic;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for Image_BlockMap_Section serialization round-trip.
    ///
    /// Feature: binary-index-format
    /// Property 3: Image_BlockMap_Section serialization round-trip
    /// **Validates: Requirements 3.3, 16.3, 16.7**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class ImageBlockMapSectionSerializerTests
    {
        /// <summary>
        /// Generates a list of OffsetRecords along with a matching blockLocations dictionary.
        /// For each OffsetRecord's Blocks list, entries are added to the dictionary with random
        /// (FileId, Offset, Size) values.
        /// </summary>
        private static Arbitrary<(List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> OffsetRecordsWithBlockLocations()
        {
            Gen<(List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations)> gen = Gen.Choose(0, 8).SelectMany(offsetCount =>
                Gen.ArrayOf(BinaryIndexGenerators.ValidOffsetRecord(), offsetCount).SelectMany(offsets =>
                {
                    // Collect all unique block keys from all offset records
                    List<BlockKey> allBlockKeys = offsets
                        .Where(o => o.Blocks != null)
                        .SelectMany(o => o.Blocks!)
                        .Distinct()
                        .ToList();

                    // Generate random locations for each unique block key
                    if (allBlockKeys.Count == 0)
                    {
                        return Gen.Constant((
                            Offsets: offsets.ToList(),
                            BlockLocations: new Dictionary<BlockKey, (int FileId, long Offset, int Size)>()
                        ));
                    }

                    return Gen.ArrayOf(
                        Gen.Choose(0, 100).SelectMany(fileId =>
                            Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).SelectMany(offset =>
                                Gen.Choose(1, 0x100000).Select(size =>
                                    (FileId: fileId, Offset: offset, Size: size)
                                ))),
                        allBlockKeys.Count
                    ).Select(locations =>
                    {
                        Dictionary<BlockKey, (int FileId, long Offset, int Size)> dict = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
                        for (int i = 0; i < allBlockKeys.Count; i++)
                        {
                            dict[allBlockKeys[i]] = locations[i];
                        }
                        return (Offsets: offsets.ToList(), BlockLocations: dict);
                    });
                }));

            return Arb.From(gen);
        }

        /// <summary>
        /// **Validates: Requirements 3.3, 16.3, 16.7**
        ///
        /// Property 3: Image_BlockMap_Section serialization round-trip.
        /// For any valid collection of OffsetRecord objects (with arbitrary Offset, Size, Type,
        /// OffsetStart, and Blocks list — where Blocks may be null, empty, or contain up to
        /// max_offset_blocks BlockKey entries) and their associated block shard locations
        /// (BlockKey → file_id, offset, size), serializing to an Image_BlockMap_Section and
        /// deserializing back SHALL produce collections with identical values for all fields,
        /// where a null or empty Blocks list round-trips as an empty list, and a populated
        /// Blocks list preserves count, order, and each BlockKey's XxHash64 and Crc32.
        /// </summary>
        [Property(MaxTest = 100, Arbitrary = new[] { typeof(ImageBlockMapSectionSerializerTests) })]
        public bool BlockMapSectionSerializationRoundTrip(
            (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) input)
        {
            (List<OffsetRecord> offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations) = input;

            // Serialize
            (byte[] serialized, int _) = ImageBlockMapSectionSerializer.Serialize(offsets, blockLocations);

            // Deserialize
            (List<OffsetRecord> deserializedOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> deserializedBlockLocations) = ImageBlockMapSectionSerializer.Deserialize(serialized);

            // Verify offset record count
            if (deserializedOffsets.Count != offsets.Count)
                return false;

            // Verify each offset record
            for (int i = 0; i < offsets.Count; i++)
            {
                OffsetRecord original = offsets[i];
                OffsetRecord deserialized = deserializedOffsets[i];

                if (deserialized.Offset != original.Offset)
                    return false;
                if (deserialized.Size != original.Size)
                    return false;
                if (deserialized.Type != original.Type)
                    return false;
                if (deserialized.OffsetStart != original.OffsetStart)
                    return false;

                // Null or empty Blocks round-trips as empty list
                int originalBlockCount = original.Blocks?.Count ?? 0;
                int deserializedBlockCount = deserialized.Blocks?.Count ?? 0;

                if (deserializedBlockCount != originalBlockCount)
                    return false;

                // Verify block keys preserve order and values
                if (originalBlockCount > 0)
                {
                    for (int b = 0; b < originalBlockCount; b++)
                    {
                        BlockKey origKey = original.Blocks![b];
                        BlockKey desKey = deserialized.Blocks![b];

                        if (desKey.XxHash64 != origKey.XxHash64)
                            return false;
                        if (desKey.Crc32 != origKey.Crc32)
                            return false;
                    }
                }
            }

            // Verify block locations dictionary
            if (deserializedBlockLocations.Count != blockLocations.Count)
                return false;

            foreach (KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> kvp in blockLocations)
            {
                if (!deserializedBlockLocations.TryGetValue(kvp.Key, out (int FileId, long Offset, int Size) deserializedLocation))
                    return false;
                if (deserializedLocation.FileId != kvp.Value.FileId)
                    return false;
                if (deserializedLocation.Offset != kvp.Value.Offset)
                    return false;
                if (deserializedLocation.Size != kvp.Value.Size)
                    return false;
            }

            return true;
        }
    }
}