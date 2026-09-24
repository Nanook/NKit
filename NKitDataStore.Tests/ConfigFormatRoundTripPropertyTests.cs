using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Configuration;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for config format round-trip.
    ///
    /// Feature: mandatory-filesystem-storage
    /// Property 2: Config format round-trip preserves values
    /// </summary>
    public class ConfigFormatRoundTripPropertyTests
    {
        // Valid shard size strings that ParseSizeToBytes can handle
        private static readonly string[] ValidShardSizes = new[]
        {
            "0", "1gb", "10g", "50g", "100g", "50GiB", "1024mb", "500m", "1tb"
        };

        // Valid block size strings that ParseSizeToBytes can handle
        private static readonly string[] ValidBlockSizes = new[]
        {
            "4k", "8k", "16k", "32k", "64k", "128k", "128kb", "64KiB", "4096"
        };

        /// <summary>
        /// **Validates: Requirements 4.1, 4.3, 4.4**
        ///
        /// Property 2: Config format round-trip preserves values (without storeFs).
        /// For any valid set name, shard size string, and block size string,
        /// generating a dedupe format string and then parsing it back SHALL produce
        /// an equivalent DedupeConfiguration. The generated string SHALL never
        /// contain more than 3 colon-separated parts.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool RoundTrip_PreservesValues_And_NoFourthPart(
            NonNegativeInt setNameSeed,
            NonNegativeInt shardIndex,
            NonNegativeInt blockIndex)
        {
            // Generate a valid set name (alphanumeric, no colons)
            string setName = $"testSet{setNameSeed.Get}";
            string shardSize = ValidShardSizes[shardIndex.Get % ValidShardSizes.Length];
            string blockSize = ValidBlockSizes[blockIndex.Get % ValidBlockSizes.Length];

            // Act: generate the format string
            string formatString = ConfigSettingsFormatGenerator.GenerateDedupeFormatString(
                setName, shardSize, blockSize);

            // Verify: no more than 3 colon-separated parts
            string[] parts = formatString.Split(':');
            if (parts.Length > 3)
                return false;

            // Act: parse it back
            DedupeConfiguration parsed = ConfigSettingsFormatParser.ParseDedupeConfiguration(formatString);

            // Verify: set name matches
            if (parsed.SetName != setName)
                return false;

            // Verify: shard size matches by parsing the original string to bytes
            long expectedShardBytes = ConfigSettingsFormatParser.ParseSizeToBytes(shardSize);
            if (expectedShardBytes > 0 && parsed.ShardSize != expectedShardBytes)
                return false;
            if (shardSize == "0" && parsed.ShardSize != 0)
                return false;

            // Verify: block size matches by parsing the original string to bytes
            long expectedBlockBytes = ConfigSettingsFormatParser.ParseSizeToBytes(blockSize);
            if (expectedBlockBytes > 0 && expectedBlockBytes <= int.MaxValue && parsed.BlockSize != (int)expectedBlockBytes)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.1, 4.4**
        ///
        /// Legacy format strings with a 4th part (persistFs) SHALL parse without error,
        /// discarding the 4th part. The parsed values for set name, shard size, and
        /// block size SHALL still be correct.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool LegacyStrings_WithFourthPart_ParseWithoutError(
            NonNegativeInt setNameSeed,
            NonNegativeInt shardIndex,
            NonNegativeInt blockIndex,
            bool legacyFsFlag)
        {
            string setName = $"legacySet{setNameSeed.Get}";
            string shardSize = ValidShardSizes[shardIndex.Get % ValidShardSizes.Length];
            string blockSize = ValidBlockSizes[blockIndex.Get % ValidBlockSizes.Length];
            string fourthPart = legacyFsFlag ? "y" : "n";

            // Build a legacy 4-part format string
            string legacyFormat = $"{setName}:{shardSize}:{blockSize}:{fourthPart}";

            // Act: parse should not throw
            DedupeConfiguration parsed = ConfigSettingsFormatParser.ParseDedupeConfiguration(legacyFormat);

            // Verify: set name matches
            if (parsed.SetName != setName)
                return false;

            // Verify: shard size matches
            long expectedShardBytes = ConfigSettingsFormatParser.ParseSizeToBytes(shardSize);
            if (expectedShardBytes > 0 && parsed.ShardSize != expectedShardBytes)
                return false;
            if (shardSize == "0" && parsed.ShardSize != 0)
                return false;

            // Verify: block size matches
            long expectedBlockBytes = ConfigSettingsFormatParser.ParseSizeToBytes(blockSize);
            if (expectedBlockBytes > 0 && expectedBlockBytes <= int.MaxValue && parsed.BlockSize != (int)expectedBlockBytes)
                return false;

            return true;
        }
    }
}