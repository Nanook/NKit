using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Configuration;
using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for dedupe parameter correctness.
    ///
    /// Feature: nkds-1gmr-command, Property 3: Dedupe param targets correct sanitized set name
    /// **Validates: Requirements 8.3**
    /// </summary>
    public class DedupeParamPropertyTests
    {
        // Valid shard size strings representative of user input
        private static readonly string[] ValidShardSizes = new[]
        {
            "1gb", "10g", "50g", "100g", "50GiB", "1024mb", "500m"
        };

        // Valid block size strings representative of user input
        private static readonly string[] ValidBlockSizes = new[]
        {
            "4k", "8k", "16k", "32k", "64k", "128k", "64KiB"
        };

        /// <summary>
        /// **Validates: Requirements 8.3**
        ///
        /// Property 3: Dedupe param targets correct sanitized set name.
        /// For any game name (including names with unsafe characters), when building
        /// the dedupe parameter with shard and block size strings, the resulting dedupe
        /// string SHALL begin with the game's sanitized name followed by a colon separator.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool DedupeParam_StartsWithSanitizedName_FollowedByColon_WhenSizesSpecified(
            NonEmptyString gameNameWrapper,
            NonNegativeInt shardIndex,
            NonNegativeInt blockIndex)
        {
            string gameName = gameNameWrapper.Get;
            string shardSize = ValidShardSizes[shardIndex.Get % ValidShardSizes.Length];
            string blockSize = ValidBlockSizes[blockIndex.Get % ValidBlockSizes.Length];

            // Derive the sanitized name the same way GameEntry does
            string sanitizedName = FilenameSanitizer.Sanitize(gameName);

            // Build the dedupe param using the same public API that the pipeline uses
            string dedupeParam = ConfigSettingsFormatGenerator.GenerateDedupeFormatString(
                sanitizedName, shardSize, blockSize);

            // The dedupe param must start with the sanitized name followed by a colon
            if (!dedupeParam.StartsWith(sanitizedName + ":"))
                return false;

            // The first colon-separated part must be exactly the sanitized name
            string[] parts = dedupeParam.Split(':');
            if (parts[0] != sanitizedName)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 8.3**
        ///
        /// Property 3 (supplementary): When no shard or block size overrides are specified,
        /// the dedupe parameter SHALL be exactly the sanitized set name (no colon separator).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool DedupeParam_IsExactlySanitizedName_WhenNoSizesSpecified(
            NonEmptyString gameNameWrapper)
        {
            string gameName = gameNameWrapper.Get;

            // Derive the sanitized name the same way GameEntry does
            string sanitizedName = FilenameSanitizer.Sanitize(gameName);

            // Build the dedupe param with no shard/block sizes
            string dedupeParam = ConfigSettingsFormatGenerator.GenerateDedupeFormatString(
                sanitizedName, null, null);

            // The dedupe param must be exactly the sanitized name
            if (dedupeParam != sanitizedName)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 8.3**
        ///
        /// Property 3 (supplementary): The per-file request's SetName equals the
        /// GameEntry's SanitizedName, ensuring the dedupe parameter targets the correct set.
        /// For any game name, constructing a GameEntry and reading its SanitizedName
        /// SHALL produce the same value as calling FilenameSanitizer.Sanitize directly.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool GameEntry_SanitizedName_MatchesFilenameSanitizer(NonEmptyString gameNameWrapper)
        {
            string gameName = gameNameWrapper.Get;

            // GameEntry requires at least one valid mask - use a simple pattern
            GameEntry entry = new GameEntry(gameName, new[] { ".*" });

            // The GameEntry's SanitizedName must equal what FilenameSanitizer produces
            string expectedSanitized = FilenameSanitizer.Sanitize(gameName);

            return entry.SanitizedName == expectedSanitized;
        }
    }
}