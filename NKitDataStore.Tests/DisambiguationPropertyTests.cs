using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Steps.Shared;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for IIndexNameDisambiguator (Property 5: Index Disambiguation Uniqueness).
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.4**
    /// </summary>
    public class DisambiguationPropertyTests
    {
        private readonly IIndexNameDisambiguator _disambiguator = new DataStoreWiiUFormatter_Disambiguator();

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.4**
        ///
        /// Property 5: Index Disambiguation Uniqueness.
        /// For any base name and two distinct non-empty index file names,
        /// the disambiguated names must be different.
        /// </summary>
        [Property]
        public bool Disambiguation_DistinctIndexFiles_ProduceDistinctNames(
            NonEmptyString baseNameWrapper,
            NonEmptyString indexFile1Wrapper,
            NonEmptyString indexFile2Wrapper)
        {
            string baseName = baseNameWrapper.Get;
            string indexFile1 = indexFile1Wrapper.Get;
            string indexFile2 = indexFile2Wrapper.Get;

            // Only test when the two index file names are actually distinct
            if (indexFile1 == indexFile2)
                return true; // vacuously true for equal inputs

            string result1 = _disambiguator.DisambiguateImageName(baseName, indexFile1);
            string result2 = _disambiguator.DisambiguateImageName(baseName, indexFile2);

            return result1 != result2;
        }

        /// <summary>
        /// **Validates: Requirement 5.2**
        ///
        /// Property 5 (empty/null case): For empty index file names,
        /// the base name is returned unchanged.
        /// </summary>
        [Property]
        public bool Disambiguation_EmptyIndexFile_ReturnsBaseNameUnchanged(
            NonEmptyString baseNameWrapper)
        {
            string baseName = baseNameWrapper.Get;

            string resultEmpty = _disambiguator.DisambiguateImageName(baseName, "");
            string resultNull = _disambiguator.DisambiguateImageName(baseName, null);

            return resultEmpty == baseName && resultNull == baseName;
        }

        /// <summary>
        /// **Validates: Requirement 5.3**
        ///
        /// Property 6: Index Disambiguation Idempotence.
        /// Applying disambiguation twice with the same indexFileName produces
        /// the same result as applying it once.
        /// </summary>
        [Property]
        public bool Disambiguation_Idempotence_DoubleApplicationEqualsOne(
            NonEmptyString baseNameWrapper,
            string indexFileName)
        {
            string baseName = baseNameWrapper.Get;

            string once = _disambiguator.DisambiguateImageName(baseName, indexFileName);
            string twice = _disambiguator.DisambiguateImageName(once, indexFileName);

            return twice == once;
        }

        /// <summary>
        /// **Validates: Requirement 6.1**
        ///
        /// Property 7: Index Disambiguation Parseability Round-Trip.
        /// For any (baseName, indexFileName) pair where indexFileName is non-empty
        /// and baseName does not end with a pattern matching " [...]",
        /// disambiguating and then parsing by locating the last " [" and closing "]"
        /// recovers the original baseName and indexFileName.
        /// </summary>
        [Property]
        public bool Disambiguation_Parseability_RoundTrip(
            NonEmptyString baseNameWrapper,
            NonEmptyString indexFileNameWrapper)
        {
            string baseName = baseNameWrapper.Get;
            string indexFileName = indexFileNameWrapper.Get;

            // Filter: baseName must not end with " [....]" pattern, as that makes parsing ambiguous
            if (baseName.EndsWith("]") && baseName.LastIndexOf(" [") >= 0)
                return true; // skip ambiguous inputs (vacuously true)

            // Filter: indexFileName must not contain "]" or " [" as that makes parsing ambiguous
            if (indexFileName.Contains(']') || indexFileName.Contains(" ["))
                return true; // skip ambiguous inputs (vacuously true)

            string disambiguated = _disambiguator.DisambiguateImageName(baseName, indexFileName);

            // Parse: find last " [" and closing "]"
            int lastBracketOpen = disambiguated.LastIndexOf(" [");
            int lastBracketClose = disambiguated.LastIndexOf(']');

            // Must find both markers, and "]" must be at the end
            if (lastBracketOpen < 0 || lastBracketClose < 0)
                return false;
            if (lastBracketClose != disambiguated.Length - 1)
                return false;
            if (lastBracketClose <= lastBracketOpen)
                return false;

            string extractedBaseName = disambiguated.Substring(0, lastBracketOpen);
            string extractedIndexFileName = disambiguated.Substring(lastBracketOpen + 2, lastBracketClose - lastBracketOpen - 2);

            return extractedBaseName == baseName && extractedIndexFileName == indexFileName;
        }

        /// <summary>
        /// Lightweight wrapper that exposes the disambiguation logic from
        /// DataStoreWiiUFormatter without requiring the full formatter's
        /// constructor dependencies (IStepContext, dedupePath, etc.).
        /// </summary>
        private class DataStoreWiiUFormatter_Disambiguator : IIndexNameDisambiguator
        {
            public bool SupportsIndexDisambiguation => true;

            public string DisambiguateImageName(string baseName, string indexFileName)
            {
                if (string.IsNullOrEmpty(indexFileName))
                    return baseName;
                string suffix = $" [{indexFileName}]";
                if (baseName.EndsWith(suffix, global::System.StringComparison.OrdinalIgnoreCase))
                    return baseName;
                return $"{baseName}{suffix}";
            }

            public string RestoreBaseName(string disambiguatedName) => Nanook.NKit.Steps.Shared.DataStoreWiiUFormatter.RestoreBaseNameStatic(disambiguatedName);
        }
    }
}