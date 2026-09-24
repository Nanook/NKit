using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Ogmr;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for FilenameSanitizer.
    ///
    /// Feature: nkds-1gmr-command, Property 1: Filename sanitization produces valid filenames
    /// **Validates: Requirements 4.2**
    /// </summary>
    public class FilenameSanitizerPropertyTests
    {
        /// <summary>
        /// The set of filesystem-unsafe characters that must never appear in sanitized output.
        /// </summary>
        private static readonly char[] UnsafeChars = { ':', '?', '*', '<', '>', '|', '"', '/', '\\' };

        /// <summary>
        /// **Validates: Requirements 4.2**
        ///
        /// Property 1: Filename sanitization produces valid filenames.
        /// For any input string (including strings with unsafe characters, unicode,
        /// whitespace, and dots), Sanitize() SHALL produce a non-empty string
        /// that contains none of the filesystem-unsafe characters and is a valid filename.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SanitizedOutput_IsNonEmpty_AndContainsNoUnsafeChars(string input)
        {
            string result = FilenameSanitizer.Sanitize(input);

            // Result must be non-empty
            if (string.IsNullOrEmpty(result))
                return false;

            // Result must not contain any unsafe characters
            if (result.IndexOfAny(UnsafeChars) >= 0)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.2**
        ///
        /// Property 1 (supplementary): Sanitized output does not start or end with
        /// whitespace or dots, which would make it an invalid filename on Windows.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SanitizedOutput_DoesNotStartOrEndWithWhitespaceOrDots(string input)
        {
            string result = FilenameSanitizer.Sanitize(input);

            // Must not start or end with whitespace
            if (result != result.Trim())
                return false;

            // Must not start or end with dots
            if (result.StartsWith('.') || result.EndsWith('.'))
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.2**
        ///
        /// Property 1 (supplementary): Sanitized output never contains consecutive underscores.
        /// The sanitizer collapses multiple underscores into a single one.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool SanitizedOutput_NeverContainsConsecutiveUnderscores(string input)
        {
            string result = FilenameSanitizer.Sanitize(input);

            // Must not contain consecutive underscores
            if (result.Contains("__"))
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.2**
        ///
        /// Property 1 (supplementary): Null input produces a valid non-empty result.
        /// The sanitizer must handle null gracefully by returning the fallback name.
        /// </summary>
        [Fact]
        public void SanitizedOutput_NullInput_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize(null!);

            Assert.False(string.IsNullOrEmpty(result));
            Assert.Equal(-1, result.IndexOfAny(UnsafeChars));
        }

        /// <summary>
        /// **Validates: Requirements 4.2**
        ///
        /// Property 1 (supplementary): Empty input produces a valid non-empty result.
        /// The sanitizer must handle empty strings by returning the fallback name.
        /// </summary>
        [Fact]
        public void SanitizedOutput_EmptyInput_ReturnsUnnamed()
        {
            string result = FilenameSanitizer.Sanitize(string.Empty);

            Assert.False(string.IsNullOrEmpty(result));
            Assert.Equal(-1, result.IndexOfAny(UnsafeChars));
        }
    }
}