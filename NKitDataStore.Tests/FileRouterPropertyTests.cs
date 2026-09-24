using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for FileRouter.
    ///
    /// Feature: nkds-1gmr-command, Property 2: File routing correctness (first-match-wins with unmatched fallback)
    /// **Validates: Requirements 7.1, 7.3, 7.4, 7.5**
    /// </summary>
    public class FileRouterPropertyTests
    {
        /// <summary>
        /// Pool of simple prefix patterns used to build GameEntry masks.
        /// These are valid regex patterns that match filenames starting with a given prefix.
        /// </summary>
        private static readonly string[] PrefixPatterns = new[]
        {
            "^GameA", "^GameB", "^GameC", "^GameD", "^GameE",
            "^GameF", "^GameG", "^GameH", "^GameI", "^GameJ"
        };

        /// <summary>
        /// Pool of game names for generating GameEntry objects.
        /// </summary>
        private static readonly string[] GameNames = new[]
        {
            "Game Alpha", "Game Beta", "Game Charlie", "Game Delta", "Game Echo",
            "Game Foxtrot", "Game Golf", "Game Hotel", "Game India", "Game Juliet"
        };

        /// <summary>
        /// Filenames that are designed to match specific prefix patterns.
        /// Index i matches PrefixPatterns[i].
        /// </summary>
        private static readonly string[] MatchingFilenames = new[]
        {
            "GameA Something.iso", "GameB Title.iso", "GameC Disc.iso",
            "GameD Version.iso", "GameE Release.iso", "GameF Edition.iso",
            "GameG Variant.iso", "GameH Copy.iso", "GameI Build.iso",
            "GameJ Final.iso"
        };

        /// <summary>
        /// Filenames that do not match any of the prefix patterns.
        /// </summary>
        private static readonly string[] UnmatchedFilenames = new[]
        {
            "Unrelated File.iso", "Random Title.wbfs", "NoMatch.nkit.iso",
            "Something Else.gcz", "Unknown Game.rvz"
        };

        /// <summary>
        /// Builds a list of GameEntry objects from the given seeds.
        /// Each entry gets a unique prefix pattern so we can verify first-match-wins.
        /// </summary>
        private static List<GameEntry> BuildGameEntries(int entryCount, int startIndex)
        {
            List<GameEntry> entries = new List<GameEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                int idx = (startIndex + i) % PrefixPatterns.Length;
                string name = GameNames[idx];
                string[] masks = new[] { PrefixPatterns[idx] };
                entries.Add(new GameEntry(name, masks));
            }
            return entries;
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.3, 7.4, 7.5**
        ///
        /// Property 2a: First-match-wins semantics.
        /// When a filename matches multiple GameEntry masks, the FileRouter SHALL return
        /// the first matching GameEntry in list order.
        ///
        /// Strategy: Create multiple entries that all match the same filename (using a
        /// shared pattern), then verify the router always returns the first one.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool FirstMatchWins_ReturnsFirstMatchingEntry(
            NonNegativeInt entryCountSeed,
            NonNegativeInt patternIndexSeed)
        {
            // Create 2-5 entries that all share the same pattern (so all match the same filename)
            int entryCount = (entryCountSeed.Get % 4) + 2; // 2-5 entries
            int patternIdx = patternIndexSeed.Get % PrefixPatterns.Length;

            string sharedPattern = PrefixPatterns[patternIdx];
            string matchingFilename = MatchingFilenames[patternIdx];

            List<GameEntry> entries = new List<GameEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                // All entries use the same pattern, so all match the same filename
                string name = $"Entry_{i}_{GameNames[(patternIdx + i) % GameNames.Length]}";
                entries.Add(new GameEntry(name, new[] { sharedPattern }));
            }

            FileRouter router = new FileRouter(entries);
            GameEntry result = router.Match(matchingFilename);

            // Must return the first entry (index 0) since all entries match
            if (result == null)
                return false;

            return result.Name == entries[0].Name;
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.3, 7.4, 7.5**
        ///
        /// Property 2b: Determinism - calling Match twice with the same inputs produces
        /// the same result.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool Match_IsDeterministic_SameInputsSameResult(
            NonNegativeInt entryCountSeed,
            NonNegativeInt startIndexSeed,
            NonNegativeInt filenameSeed)
        {
            // Build 1-5 game entries
            int entryCount = (entryCountSeed.Get % 5) + 1;
            int startIndex = startIndexSeed.Get % PrefixPatterns.Length;
            List<GameEntry> entries = BuildGameEntries(entryCount, startIndex);

            // Pick a filename (either matching or unmatched)
            string[] allFilenames = new string[MatchingFilenames.Length + UnmatchedFilenames.Length];
            MatchingFilenames.CopyTo(allFilenames, 0);
            UnmatchedFilenames.CopyTo(allFilenames, MatchingFilenames.Length);
            string filename = allFilenames[filenameSeed.Get % allFilenames.Length];

            FileRouter router = new FileRouter(entries);

            // Call Match twice
            GameEntry result1 = router.Match(filename);
            GameEntry result2 = router.Match(filename);

            // Both results must be identical
            if (result1 == null && result2 == null)
                return true;

            if (result1 == null || result2 == null)
                return false;

            return ReferenceEquals(result1, result2);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.3, 7.4, 7.5**
        ///
        /// Property 2c: Unmatched fallback - filenames that don't match any pattern return null.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool UnmatchedFilename_ReturnsNull(
            NonNegativeInt entryCountSeed,
            NonNegativeInt startIndexSeed,
            NonNegativeInt unmatchedSeed)
        {
            // Build 1-5 game entries with prefix patterns
            int entryCount = (entryCountSeed.Get % 5) + 1;
            int startIndex = startIndexSeed.Get % PrefixPatterns.Length;
            List<GameEntry> entries = BuildGameEntries(entryCount, startIndex);

            // Pick an unmatched filename (none of these start with "Game" prefix patterns)
            string filename = UnmatchedFilenames[unmatchedSeed.Get % UnmatchedFilenames.Length];

            FileRouter router = new FileRouter(entries);
            GameEntry result = router.Match(filename);

            // Must return null for unmatched filenames
            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.3, 7.4, 7.5**
        ///
        /// Property 2d: Correct routing - when entries have distinct patterns, a filename
        /// matching entry N is routed to entry N (not any other entry).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CorrectRouting_FilenameMatchesExpectedEntry(
            NonNegativeInt entryCountSeed,
            NonNegativeInt targetIndexSeed)
        {
            // Build 2-5 entries with distinct patterns
            int entryCount = (entryCountSeed.Get % 4) + 2; // 2-5 entries
            List<GameEntry> entries = BuildGameEntries(entryCount, 0);

            // Pick a target entry and use its matching filename
            int targetIdx = targetIndexSeed.Get % entryCount;
            string filename = MatchingFilenames[targetIdx];

            FileRouter router = new FileRouter(entries);
            GameEntry result = router.Match(filename);

            // Must return the expected entry
            if (result == null)
                return false;

            return result.Name == entries[targetIdx].Name;
        }

        /// <summary>
        /// Feature: nkds-1gmr-command, Property 4: Case-insensitive regex matching
        /// **Validates: Requirements 11.3**
        ///
        /// For any filename and for any case variation of that filename, the FileRouter.Match()
        /// function SHALL produce the same routing result — i.e., if a filename matches a game
        /// entry, then any case variation of that filename also matches the same game entry.
        ///
        /// Strategy: Create game entries with simple prefix patterns, generate a filename that
        /// matches one of the patterns, apply random case transformations (uppercase, lowercase,
        /// mixed) to the filename, and verify that the router returns the same GameEntry for all
        /// case variations.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CaseInsensitiveMatching_AllCaseVariationsRouteToSameEntry(
            NonNegativeInt entryCountSeed,
            NonNegativeInt targetIndexSeed,
            NonNegativeInt caseTransformSeed)
        {
            // Build 2-5 entries with distinct patterns
            int entryCount = (entryCountSeed.Get % 4) + 2;
            List<GameEntry> entries = BuildGameEntries(entryCount, 0);

            // Pick a target entry and use its matching filename as the base
            int targetIdx = targetIndexSeed.Get % entryCount;
            string originalFilename = MatchingFilenames[targetIdx];

            FileRouter router = new FileRouter(entries);

            // Get the result for the original filename
            GameEntry originalResult = router.Match(originalFilename);

            // Apply case transformations and verify same result
            string uppercased = originalFilename.ToUpperInvariant();
            string lowercased = originalFilename.ToLowerInvariant();
            string mixedCase = ApplyMixedCase(originalFilename, caseTransformSeed.Get);

            GameEntry upperResult = router.Match(uppercased);
            GameEntry lowerResult = router.Match(lowercased);
            GameEntry mixedResult = router.Match(mixedCase);

            // All case variations must produce the same routing result
            return SameRoutingResult(originalResult, upperResult)
                && SameRoutingResult(originalResult, lowerResult)
                && SameRoutingResult(originalResult, mixedResult);
        }

        /// <summary>
        /// Feature: nkds-1gmr-command, Property 4: Case-insensitive regex matching
        /// **Validates: Requirements 11.3**
        ///
        /// Verifies that unmatched filenames remain unmatched regardless of case transformation.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CaseInsensitiveMatching_UnmatchedRemainsUnmatchedAcrossCases(
            NonNegativeInt entryCountSeed,
            NonNegativeInt startIndexSeed,
            NonNegativeInt unmatchedSeed,
            NonNegativeInt caseTransformSeed)
        {
            // Build 1-5 game entries
            int entryCount = (entryCountSeed.Get % 5) + 1;
            int startIndex = startIndexSeed.Get % PrefixPatterns.Length;
            List<GameEntry> entries = BuildGameEntries(entryCount, startIndex);

            // Pick an unmatched filename
            string originalFilename = UnmatchedFilenames[unmatchedSeed.Get % UnmatchedFilenames.Length];

            FileRouter router = new FileRouter(entries);

            // Apply case transformations
            string uppercased = originalFilename.ToUpperInvariant();
            string lowercased = originalFilename.ToLowerInvariant();
            string mixedCase = ApplyMixedCase(originalFilename, caseTransformSeed.Get);

            // All case variations of an unmatched filename must also be unmatched (null)
            return router.Match(originalFilename) == null
                && router.Match(uppercased) == null
                && router.Match(lowercased) == null
                && router.Match(mixedCase) == null;
        }

        /// <summary>
        /// Applies a deterministic mixed-case transformation to a string based on a seed.
        /// Each character is toggled to upper or lower case based on the seed bits.
        /// </summary>
        private static string ApplyMixedCase(string input, int seed)
        {
            char[] chars = input.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                // Use different bits of the seed to decide case for each character
                bool toUpper = ((seed >> (i % 31)) & 1) == 1;
                chars[i] = toUpper ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
            }
            return new string(chars);
        }

        /// <summary>
        /// Compares two routing results for equality (both null, or both reference the same entry).
        /// </summary>
        private static bool SameRoutingResult(GameEntry a, GameEntry b)
        {
            if (a == null && b == null)
                return true;
            if (a == null || b == null)
                return false;
            return ReferenceEquals(a, b);
        }
    }
}