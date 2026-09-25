using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for DataStore.ExtractBaseName and DataStore.IsTmdDisambiguatedName.
    /// </summary>
    public class DataStoreHelperPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 3.1, 3.2, 4.1, 4.2** (Property 5)
        ///
        /// For any (baseName, N) pair where baseName doesn't contain " [tmd.",
        /// ExtractBaseName($"{baseName} [tmd.{N}]") equals baseName
        /// and IsTmdDisambiguatedName returns true.
        /// </summary>
        [Property]
        public bool ExtractBaseName_RoundTrip_And_IsTmdDisambiguatedName(NonEmptyString baseNameWrapper, NonNegativeInt nWrapper)
        {
            string baseName = baseNameWrapper.Get;
            int n = nWrapper.Get;

            // Skip inputs where baseName contains the tmd pattern or null chars (would confuse parsing)
            if (baseName.Contains(" [tmd.") || baseName.Contains('\0'))
                return true; // vacuously true for filtered-out inputs

            string composed = $"{baseName} [tmd.{n}]";

            string extracted = DataStore.ExtractBaseName(composed);
            bool isDisambiguated = DataStore.IsTmdDisambiguatedName(composed);

            return extracted == baseName && isDisambiguated;
        }
    }
}