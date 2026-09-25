using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FileRouter.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    /// </summary>
    public class FileRouterTests
    {
        // --- Known filenames match expected games ---

        [Fact]
        public void Match_FilenameMatchingSingleEntry_ReturnsCorrectEntry()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Mario Kart Wii", new[] { @"^Mario Kart Wii" }),
                new GameEntry("Zelda Twilight Princess", new[] { @"^Zelda" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Mario Kart Wii (USA).iso");

            Assert.NotNull(result);
            Assert.Equal("Mario Kart Wii", result.Name);
        }

        [Fact]
        public void Match_FilenameMatchingSecondEntry_ReturnsSecondEntry()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Mario Kart Wii", new[] { @"^Mario Kart Wii" }),
                new GameEntry("Zelda Twilight Princess", new[] { @"^Zelda" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Zelda Twilight Princess (EUR).iso");

            Assert.NotNull(result);
            Assert.Equal("Zelda Twilight Princess", result.Name);
        }

        [Fact]
        public void Match_FilenameMatchingSecondMaskOfEntry_ReturnsEntry()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Metroid Prime", new[] { @"^Metroid Prime 1", @"^Metroid Prime(?= \(|\.iso|$)" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Metroid Prime (USA).iso");

            Assert.NotNull(result);
            Assert.Equal("Metroid Prime", result.Name);
        }

        // --- Unmatched filenames return null ---

        [Fact]
        public void Match_FilenameMatchingNoEntry_ReturnsNull()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Mario Kart Wii", new[] { @"^Mario Kart Wii" }),
                new GameEntry("Zelda Twilight Princess", new[] { @"^Zelda" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Donkey Kong Country Returns (USA).iso");

            Assert.Null(result);
        }

        [Fact]
        public void Match_EmptyFilename_ReturnsNull()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Some Game", new[] { @"^Some" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("");

            Assert.Null(result);
        }

        [Fact]
        public void Match_EmptyGameList_ReturnsNull()
        {
            FileRouter router = new FileRouter(new List<GameEntry>());

            GameEntry result = router.Match("Any File.iso");

            Assert.Null(result);
        }

        // --- First-match-wins when multiple entries could match ---

        [Fact]
        public void Match_MultipleEntriesMatchFilename_ReturnsFirstMatch()
        {
            // Both entries have patterns that match "Mario Kart Wii (USA).iso"
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Mario Games Collection", new[] { @"^Mario" }),
                new GameEntry("Mario Kart Wii", new[] { @"^Mario Kart Wii" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Mario Kart Wii (USA).iso");

            Assert.NotNull(result);
            Assert.Equal("Mario Games Collection", result.Name);
        }

        [Fact]
        public void Match_OverlappingPatterns_FirstEntryWins()
        {
            // A broad pattern listed first should win over a more specific one listed later
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("All Sports", new[] { @"Sports" }),
                new GameEntry("Wii Sports", new[] { @"^Wii Sports" }),
                new GameEntry("Wii Sports Resort", new[] { @"^Wii Sports Resort" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("Wii Sports Resort (USA).iso");

            Assert.NotNull(result);
            Assert.Equal("All Sports", result.Name);
        }

        // --- Case-insensitive matching ---

        [Fact]
        public void Match_CaseInsensitive_MatchesRegardlessOfCase()
        {
            List<GameEntry> entries = new List<GameEntry>
            {
                new GameEntry("Mario Kart Wii", new[] { @"^Mario Kart Wii" })
            };
            FileRouter router = new FileRouter(entries);

            GameEntry result = router.Match("mario kart wii (usa).iso");

            Assert.NotNull(result);
            Assert.Equal("Mario Kart Wii", result.Name);
        }
    }
}