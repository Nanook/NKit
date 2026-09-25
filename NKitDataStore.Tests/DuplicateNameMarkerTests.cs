using Nanook.NKit.Container;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Guards the centralized virtual duplicate-name disambiguation marker
    /// (<see cref="DataStoreAsIso.FormatDuplicateName(string,long)"/> /
    /// <see cref="DataStoreAsIso.FormatDuplicateName(string,string,long)"/> /
    /// <see cref="DataStoreAsIso.TryParseDuplicateName"/>).
    ///
    /// The marker is a curly-brace <c>{id}</c> / <c>{set_id}</c> suffix. It replaced a bare
    /// <c>(id)</c> convention that COLLIDED with legitimate dump-name suffixes such as
    /// "(2003)" / "(Rev 1)" — which caused verify to strip the real suffix and fail to locate the
    /// image (the FreeLoader (2003)/(2004) bug). These tests lock in the two properties that make
    /// the marker safe: (a) format-then-parse round-trips, and (b) a real name ending in a
    /// parenthesised token is NEVER mistaken for an id marker.
    /// </summary>
    public class DuplicateNameMarkerTests
    {
        [Fact]
        public void Format_ProducesCurlyBraceMarker() => Assert.Equal("Game {123}", DataStoreAsIso.FormatDuplicateName("Game", 123));

        [Theory]
        [InlineData("Game", 1)]
        [InlineData("FreeLoader for GameCube (Europe) (Unl) (v1.06B) (2003)", 42)]
        [InlineData("Name {with} braces", 7)]
        [InlineData("Trailing paren name (Rev 1)", 9999)]
        public void FormatThenParse_RoundTrips(string name, long id)
        {
            string formatted = DataStoreAsIso.FormatDuplicateName(name, id);

            Assert.True(DataStoreAsIso.TryParseDuplicateName(formatted, out string baseName, out long parsedId));
            Assert.Equal(name, baseName);
            Assert.Equal(id, parsedId);
        }

        [Theory]
        // Real dump-name suffixes that the OLD "(id)" scheme wrongly treated as an id — these must
        // NOT be parsed as a duplicate marker now (this is the exact bug the {id} marker fixes).
        [InlineData("FreeLoader for GameCube (Europe) (Unl) (v1.06B) (2003)")]
        [InlineData("Some Game (2004)")]
        [InlineData("Some Game (Rev 1)")]
        [InlineData("Some Game (Disc 2)")]
        [InlineData("Plain Name")]
        // The persisted WiiU child form is square-bracket "[tmd.X]" and must never be seen as a
        // curly-brace id marker either.
        [InlineData("Game [tmd.0]")]
        public void Parse_NonMarkerNames_AreNotTreatedAsDuplicates(string name)
        {
            Assert.False(DataStoreAsIso.TryParseDuplicateName(name, out string baseName, out long id));
            // On failure the base name is returned unchanged and id is 0.
            Assert.Equal(name, baseName);
            Assert.Equal(0, id);
        }

        [Fact]
        public void Parse_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(DataStoreAsIso.TryParseDuplicateName(null, out _, out _));
            Assert.False(DataStoreAsIso.TryParseDuplicateName("", out _, out _));
        }

        [Fact]
        public void CrossSetFormat_IncludesSanitizedSetAndId() =>
            // Set name is sanitized to [A-Za-z0-9_-] and combined with the id.
            Assert.Equal("Game {redump_123}", DataStoreAsIso.FormatDuplicateName("Game", "redump", 123));

        [Theory]
        [InlineData("re dump", "re_dump")]   // space -> _
        [InlineData("re/dump", "re_dump")]   // slash -> _
        [InlineData("_edge_", "edge")]       // leading/trailing _ trimmed
        [InlineData("keep-this_1", "keep-this_1")] // allowed chars preserved
        public void CrossSetFormat_SanitizesSetName(string rawSet, string expectedSafe)
        {
            string formatted = DataStoreAsIso.FormatDuplicateName("Game", rawSet, 5);
            Assert.Equal($"Game {{{expectedSafe}_5}}", formatted);
        }

        [Fact]
        public void CrossSetFormat_CapsLongSetNameToTwentyChars()
        {
            string longSet = new string('a', 40);
            string formatted = DataStoreAsIso.FormatDuplicateName("Game", longSet, 5);
            string expectedSafe = new string('a', 20);
            Assert.Equal($"Game {{{expectedSafe}_5}}", formatted);
        }
    }
}
