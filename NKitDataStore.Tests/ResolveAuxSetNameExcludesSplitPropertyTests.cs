using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for ResolveAuxSetName split file exclusion.
    ///
    /// Feature: aux-split-mode
    /// Property 14: ResolveAuxSetName Excludes Split Files
    /// **Validates: Requirements 10.1, 10.2, 10.3**
    ///
    /// For any directory containing both {name}.aux.nkds and {name}.split.nkds files,
    /// ResolveAuxSetName SHALL return only the aux set name and never match a split file.
    /// </summary>
    public class ResolveAuxSetNameExcludesSplitPropertyTests : IDisposable
    {
        private readonly string _baseTestDirectory;

        public ResolveAuxSetNameExcludesSplitPropertyTests()
        {
            _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"NKitResolveAuxExcludesSplitPropTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_baseTestDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseTestDirectory))
            {
                try
                {
                    Directory.Delete(_baseTestDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        /// <summary>
        /// **Validates: Requirements 10.1, 10.2, 10.3**
        ///
        /// Property 14: ResolveAuxSetName Excludes Split Files.
        /// For any directory containing both a .aux.nkds file and one or more .split.nkds files,
        /// ResolveAuxSetName SHALL return only the aux set name (matching .aux.nkds) and
        /// SHALL NOT match any .split.nkds file.
        ///
        /// We generate:
        ///   - A valid aux set name (e.g., "xbox360.aux.nkds")
        ///   - One or more split file names (e.g., "game1.split.nkds", "game2.split.nkds")
        ///   - A primary set file (always present for the method to resolve against)
        ///
        /// For each generated scenario we verify:
        ///   1. The returned value is the aux set name (not null)
        ///   2. The returned value does NOT contain ".split"
        ///   3. The returned value ends with ".aux"
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ResolveAuxSetName_WithBothAuxAndSplitFiles_ReturnsOnlyAuxSetName(
            NonNegativeInt auxNameSeed,
            NonNegativeInt splitCountSeed,
            NonNegativeInt splitNameSeed)
        {
            // Generate a valid aux base name
            string auxBaseName = $"system{auxNameSeed.Get % 50}";
            int splitCount = (splitCountSeed.Get % 5) + 1; // 1 to 5 split files

            // Create a unique subdirectory per test iteration
            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create a primary .nkds file (required for ResolveAuxSetName to find the directory)
            string primaryName = "primary";
            string primaryPath = Path.Combine(testDir, $"{primaryName}{DataStore.DatabaseFileExtension}");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());

            // Create the aux file: {auxBaseName}.aux.nkds
            string auxFileName = $"{auxBaseName}{DataStore._AuxSetSuffix}{DataStore.DatabaseFileExtension}";
            string auxFilePath = Path.Combine(testDir, auxFileName);
            File.WriteAllBytes(auxFilePath, Array.Empty<byte>());

            // Create split files: {gameName}.split.nkds
            for (int i = 0; i < splitCount; i++)
            {
                string splitBaseName = $"game{(splitNameSeed.Get + i) % 100}";
                string splitFileName = $"{splitBaseName}{DataStore._SplitSetSuffix}{DataStore.DatabaseFileExtension}";
                string splitFilePath = Path.Combine(testDir, splitFileName);
                File.WriteAllBytes(splitFilePath, Array.Empty<byte>());
            }

            // Act: resolve aux set name
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Property assertions:
            // 1. Result is non-null (aux file exists and should be found)
            if (result == null)
                return false;

            // 2. Result does NOT contain ".split" (split files are excluded)
            if (result.Contains(DataStore._SplitSetSuffix))
                return false;

            // 3. Result ends with ".aux" (it's the aux set name)
            if (!result.EndsWith(DataStore._AuxSetSuffix, StringComparison.OrdinalIgnoreCase))
                return false;

            // 4. Result matches the expected aux set name
            string expectedAuxSetName = $"{auxBaseName}{DataStore._AuxSetSuffix}";
            if (result != expectedAuxSetName)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 10.1, 10.2**
        ///
        /// Property 14 (supplementary): ResolveAuxSetName with ONLY split files returns null.
        /// When a directory contains only .split.nkds files (no .aux.nkds),
        /// ResolveAuxSetName SHALL return null — split files are never matched.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ResolveAuxSetName_WithOnlySplitFiles_ReturnsNull(
            NonNegativeInt splitCountSeed,
            NonNegativeInt splitNameSeed)
        {
            int splitCount = (splitCountSeed.Get % 5) + 1; // 1 to 5 split files

            // Create a unique subdirectory per test iteration
            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create a primary .nkds file
            string primaryPath = Path.Combine(testDir, $"primary{DataStore.DatabaseFileExtension}");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());

            // Create ONLY split files — no aux file
            for (int i = 0; i < splitCount; i++)
            {
                string splitBaseName = $"game{(splitNameSeed.Get + i) % 100}";
                string splitFileName = $"{splitBaseName}{DataStore._SplitSetSuffix}{DataStore.DatabaseFileExtension}";
                string splitFilePath = Path.Combine(testDir, splitFileName);
                File.WriteAllBytes(splitFilePath, Array.Empty<byte>());
            }

            // Act: resolve aux set name
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Property: result must be null — split files are never matched as aux
            return result == null;
        }

        /// <summary>
        /// **Validates: Requirements 10.3**
        ///
        /// Property 14 (supplementary): ResolveAuxSetName correctly distinguishes
        /// files where a name contains "split" as a substring in the base name
        /// but has the .aux.nkds extension. Such files ARE valid aux files.
        /// Only files with the actual .split.nkds suffix pattern should be excluded.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ResolveAuxSetName_AuxFileWithSplitInBaseName_IsNotExcluded(
            NonNegativeInt seed)
        {
            // Create a unique subdirectory per test iteration
            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create a primary .nkds file
            string primaryPath = Path.Combine(testDir, $"primary{DataStore.DatabaseFileExtension}");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());

            // Create an aux file whose base name happens to contain "split" but
            // is NOT a split sidecar — e.g., "splitview.aux.nkds"
            string auxBaseName = $"splitview{seed.Get % 20}";
            string auxFileName = $"{auxBaseName}{DataStore._AuxSetSuffix}{DataStore.DatabaseFileExtension}";
            string auxFilePath = Path.Combine(testDir, auxFileName);
            File.WriteAllBytes(auxFilePath, Array.Empty<byte>());

            // Act: resolve aux set name
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Property: the aux file should be found (the word "split" in the base name
            // does not trigger the exclusion — only ".split." pattern does)
            if (result == null)
                return false;

            string expectedAuxSetName = $"{auxBaseName}{DataStore._AuxSetSuffix}";
            return result == expectedAuxSetName;
        }
    }
}