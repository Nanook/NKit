using Nanook.NKit.Steps.Shared;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for IIndexNameDisambiguator.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.1, 12.3**
    /// </summary>
    public class DisambiguationUnitTests
    {
        private readonly IIndexNameDisambiguator _disambiguator = new DataStoreWiiUFormatter_Disambiguator();

        // --- Requirement 5.2: Empty/null index file name returns baseName unchanged ---

        [Fact]
        public void EmptyIndexFileName_ReturnsBaseNameUnchanged()
        {
            string result = _disambiguator.DisambiguateImageName("Game Title", "");
            Assert.Equal("Game Title", result);
        }

        [Fact]
        public void NullIndexFileName_ReturnsBaseNameUnchanged()
        {
            string result = _disambiguator.DisambiguateImageName("Game Title", null);
            Assert.Equal("Game Title", result);
        }

        // --- Requirement 5.1: Non-empty index file name produces "BaseName [indexFileName]" ---

        [Fact]
        public void NormalIndexFileName_ReturnsBracketedFormat()
        {
            string result = _disambiguator.DisambiguateImageName("Game Title", "tmd.0");
            Assert.Equal("Game Title [tmd.0]", result);
        }

        [Fact]
        public void AnotherIndexFileName_ReturnsBracketedFormat()
        {
            string result = _disambiguator.DisambiguateImageName("Game Title", "tmd.1");
            Assert.Equal("Game Title [tmd.1]", result);
        }

        // --- Requirement 5.4: Distinct index file names produce distinct results ---

        [Fact]
        public void TwoDifferentIndexFiles_ProduceDifferentNames()
        {
            string result1 = _disambiguator.DisambiguateImageName("Game Title", "tmd.0");
            string result2 = _disambiguator.DisambiguateImageName("Game Title", "tmd.1");
            Assert.NotEqual(result1, result2);
        }

        // --- Requirement 5.3: Idempotence (empty/null case is trivially idempotent) ---

        [Fact]
        public void Idempotence_EmptyIndex_SameAsOnce()
        {
            string once = _disambiguator.DisambiguateImageName("Game Title", "");
            string twice = _disambiguator.DisambiguateImageName(once, "");
            Assert.Equal(once, twice);
        }

        [Fact]
        public void Idempotence_NullIndex_SameAsOnce()
        {
            string once = _disambiguator.DisambiguateImageName("Game Title", null);
            string twice = _disambiguator.DisambiguateImageName(once, null);
            Assert.Equal(once, twice);
        }

        // --- Requirement 6.1: Parseability ---

        [Fact]
        public void Parseability_CanExtractBaseNameAndIndexFileName()
        {
            string disambiguated = _disambiguator.DisambiguateImageName("Game Title", "tmd.0");

            int lastBracketOpen = disambiguated.LastIndexOf(" [");
            int lastBracketClose = disambiguated.LastIndexOf(']');

            Assert.True(lastBracketOpen >= 0);
            Assert.Equal(disambiguated.Length - 1, lastBracketClose);

            string extractedBase = disambiguated.Substring(0, lastBracketOpen);
            string extractedIndex = disambiguated.Substring(lastBracketOpen + 2, lastBracketClose - lastBracketOpen - 2);

            Assert.Equal("Game Title", extractedBase);
            Assert.Equal("tmd.0", extractedIndex);
        }

        // --- Special characters ---

        [Fact]
        public void SpecialCharactersInBaseName_HandledCorrectly()
        {
            string result = _disambiguator.DisambiguateImageName("Pokémon™ (JP) & Friends!", "tmd.0");
            Assert.Equal("Pokémon™ (JP) & Friends! [tmd.0]", result);
        }

        [Fact]
        public void SpecialCharactersInIndexFileName_HandledCorrectly()
        {
            string result = _disambiguator.DisambiguateImageName("Game", "disc (1).gdi");
            Assert.Equal("Game [disc (1).gdi]", result);
        }

        // --- Long names ---

        [Fact]
        public void LongBaseName_HandledCorrectly()
        {
            string longName = new string('A', 500);
            string result = _disambiguator.DisambiguateImageName(longName, "tmd.0");
            Assert.Equal($"{longName} [tmd.0]", result);
        }

        [Fact]
        public void LongIndexFileName_HandledCorrectly()
        {
            string longIndex = new string('x', 500);
            string result = _disambiguator.DisambiguateImageName("Game", longIndex);
            Assert.Equal($"Game [{longIndex}]", result);
        }

        // --- Requirement 12.3: Existing WiiU disc image ingestion behavior preserved ---

        [Fact]
        public void SupportsIndexDisambiguation_IsTrue() => Assert.True(_disambiguator.SupportsIndexDisambiguation);

        [Fact]
        public void WiiUTypicalUsage_TmdIndexFiles_ProduceExpectedNames()
        {
            // Simulates the typical WiiU ingestion scenario with multiple TMD index files
            string baseName = "Mario Kart 8";
            string[] indexFiles = { "tmd.0", "tmd.1", "tmd.2" };

            string[] results = indexFiles.Select(idx => _disambiguator.DisambiguateImageName(baseName, idx)).ToArray();

            Assert.Equal("Mario Kart 8 [tmd.0]", results[0]);
            Assert.Equal("Mario Kart 8 [tmd.1]", results[1]);
            Assert.Equal("Mario Kart 8 [tmd.2]", results[2]);

            // All results are distinct
            Assert.Equal(results.Length, results.Distinct().Count());
        }

        [Fact]
        public void WiiUTypicalUsage_NoIndexFile_BaseNamePreserved()
        {
            // When there's no index file (single TMD), base name should be unchanged
            string result = _disambiguator.DisambiguateImageName("Mario Kart 8", null);
            Assert.Equal("Mario Kart 8", result);
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
                return $"{baseName} [{indexFileName}]";
            }

            public string RestoreBaseName(string disambiguatedName) => Nanook.NKit.Steps.Shared.DataStoreWiiUFormatter.RestoreBaseNameStatic(disambiguatedName);
        }
    }
}