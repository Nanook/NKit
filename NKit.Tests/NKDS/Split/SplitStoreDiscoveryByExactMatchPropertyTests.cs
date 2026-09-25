using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using System;
using System.IO;
using System.Text;
using Xunit;


namespace NKit.Tests.NKDS.Split
{
    /// <summary>
    /// Property-based tests for split store discovery by exact filename match.
    /// Feature: aux-split-mode, Property 15: Split Store Discovery by Exact Match
    ///
    /// For any set name, ResolveSplitSetName SHALL discover a split store if and only if
    /// the file {setName}.split.nkds exists in the base directory (exact filename match, no glob).
    ///
    /// Tests the actual DataStore.ResolveSplitSetName internal static method against real
    /// file system state to ensure deterministic, exact-match-only discovery.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Split")]
    public class SplitStoreDiscoveryByExactMatchPropertyTests : IDisposable
    {
        private readonly string _tempDir;

        public SplitStoreDiscoveryByExactMatchPropertyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_SplitDiscovery_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        #region Property Tests

        /// <summary>
        /// Property 15: Split Store Discovery by Exact Match — Returns non-null when file exists
        ///
        /// For any valid set name, when the file {setName}.split.nkds exists in the base
        /// directory, ResolveSplitSetName SHALL return a non-null result equal to "{setName}.split".
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property]
        public bool ResolveSplitSetName_ReturnsNonNull_WhenExactFileExists(NonEmptyString setNameRaw)
        {
            string setName = SanitizeSetName(setNameRaw.Get);
            if (setName == null)
                return true; // Skip degenerate input

            // Create a unique subdirectory per property invocation to avoid cross-test interference
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create the exact split file: {setName}.split.nkds
            string splitFileName = setName + ".split.nkds";
            string splitPath = Path.Combine(testDir, splitFileName);
            File.WriteAllBytes(splitPath, Array.Empty<byte>());

            // Act
            string result = DataStore.ResolveSplitSetName(testDir, setName);

            // Assert: must return "{setName}.split"
            return result != null && result == setName + ".split";
        }

        /// <summary>
        /// Property 15: Split Store Discovery by Exact Match — Returns null when file does not exist
        ///
        /// For any valid set name, when the file {setName}.split.nkds does NOT exist in
        /// the base directory, ResolveSplitSetName SHALL return null.
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property]
        public bool ResolveSplitSetName_ReturnsNull_WhenFileDoesNotExist(NonEmptyString setNameRaw)
        {
            string setName = SanitizeSetName(setNameRaw.Get);
            if (setName == null)
                return true; // Skip degenerate input

            // Create an empty directory — no split file present
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Act
            string result = DataStore.ResolveSplitSetName(testDir, setName);

            // Assert: must return null
            return result == null;
        }

        /// <summary>
        /// Property 15: Split Store Discovery by Exact Match — Does not match near-miss filenames
        ///
        /// For any valid set name, when the directory contains files with similar but non-exact
        /// names (e.g., "{setName}.split.nkds.bak", "{setName}X.split.nkds", "X{setName}.split.nkds"),
        /// ResolveSplitSetName SHALL still return null because only exact filename match counts.
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property]
        public bool ResolveSplitSetName_ReturnsNull_WhenOnlyNearMissFilesExist(NonEmptyString setNameRaw)
        {
            string setName = SanitizeSetName(setNameRaw.Get);
            if (setName == null)
                return true; // Skip degenerate input

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create near-miss files that should NOT match
            File.WriteAllBytes(Path.Combine(testDir, setName + ".split.nkds.bak"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(testDir, setName + "X.split.nkds"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(testDir, "X" + setName + ".split.nkds"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(testDir, setName + ".aux.nkds"), Array.Empty<byte>());

            // Act
            string result = DataStore.ResolveSplitSetName(testDir, setName);

            // Assert: near-miss files should NOT trigger discovery
            return result == null;
        }

        /// <summary>
        /// Property 15: Split Store Discovery by Exact Match — Biconditional (if and only if)
        ///
        /// For any valid set name and a boolean controlling file presence, ResolveSplitSetName
        /// returns non-null if and only if the exact file exists. This is the core biconditional
        /// property: existence ↔ discovery.
        ///
        /// **Validates: Requirements 5.1**
        /// </summary>
        [Property]
        public bool ResolveSplitSetName_Biconditional_ExistenceEqualsDiscovery(
            NonEmptyString setNameRaw,
            bool fileExists)
        {
            string setName = SanitizeSetName(setNameRaw.Get);
            if (setName == null)
                return true; // Skip degenerate input

            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Conditionally create the exact split file
            if (fileExists)
            {
                string splitPath = Path.Combine(testDir, setName + ".split.nkds");
                File.WriteAllBytes(splitPath, Array.Empty<byte>());
            }

            // Act
            string result = DataStore.ResolveSplitSetName(testDir, setName);

            // Assert: non-null result ↔ file exists
            bool discovered = result != null;
            return discovered == fileExists;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Sanitizes a generated string to be a valid set name (no path separators, no extensions).
        /// </summary>
        private static string SanitizeSetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Remove characters invalid in filenames and path separators
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder cleaned = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (Array.IndexOf(invalid, c) < 0 && c != '.' && c != ' ')
                    cleaned.Append(c);
            }

            string result = cleaned.ToString();
            return result.Length > 0 ? result.Substring(0, Math.Min(result.Length, 50)).ToLower() : null;
        }

        #endregion
    }
}