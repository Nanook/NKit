using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for FilenameSanitizer.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    public class FilenameSanitizerTests
    {
        // --- Known input/output mappings ---

        [Fact]
        public void Sanitize_ColonInName_ReplacedWithUnderscore()
        {
            string result = FilenameSanitizer.Sanitize("007: Quantum");
            Assert.Equal("007_ Quantum", result);
        }

        [Fact]
        public void Sanitize_EmptyString_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize("");
            Assert.Equal("_unnamed_", result);
        }

        [Fact]
        public void Sanitize_DotsOnly_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize("...");
            Assert.Equal("_unnamed_", result);
        }

        [Fact]
        public void Sanitize_Null_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize(null!);
            Assert.Equal("_unnamed_", result);
        }

        // --- Consecutive unsafe chars collapse to single underscore ---

        [Fact]
        public void Sanitize_ConsecutiveUnsafeChars_CollapsedToSingleUnderscore()
        {
            // Multiple consecutive unsafe chars should each become '_', then collapse
            string result = FilenameSanitizer.Sanitize("Game::Name");
            Assert.Equal("Game_Name", result);
        }

        [Fact]
        public void Sanitize_MixedConsecutiveUnsafeChars_CollapsedToSingleUnderscore()
        {
            string result = FilenameSanitizer.Sanitize("A:?*B");
            Assert.Equal("A_B", result);
        }

        // --- Edge case: dots separated by spaces ---

        [Fact]
        public void Sanitize_DotsSeparatedBySpaces_ReturnsUnnamed()
        {
            // ". . ." after trim/dot-trim loop should reduce to empty
            string result = FilenameSanitizer.Sanitize(". . .");
            Assert.Equal("_unnamed_", result);
        }

        // --- Whitespace and dot trimming ---

        [Fact]
        public void Sanitize_LeadingTrailingWhitespace_Trimmed()
        {
            string result = FilenameSanitizer.Sanitize("  Hello  ");
            Assert.Equal("Hello", result);
        }

        [Fact]
        public void Sanitize_LeadingTrailingDots_Trimmed()
        {
            string result = FilenameSanitizer.Sanitize("..Game..");
            Assert.Equal("Game", result);
        }

        [Fact]
        public void Sanitize_WhitespaceOnly_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize("   ");
            Assert.Equal("_unnamed_", result);
        }

        // --- Safe characters preserved ---

        [Fact]
        public void Sanitize_SafeCharacters_PreservedUnchanged()
        {
            string result = FilenameSanitizer.Sanitize("My Game (2024) - Special Edition");
            Assert.Equal("My Game (2024) - Special Edition", result);
        }
    }
}