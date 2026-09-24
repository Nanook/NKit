using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for convention-based aux discovery.
    ///
    /// Feature: sidecar-datastore
    /// Property 1: Convention Discovery Determinism
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    public class AuxDiscoveryPropertyTests : IDisposable
    {
        private readonly string _baseTestDirectory;

        public AuxDiscoveryPropertyTests()
        {
            _baseTestDirectory = Path.Combine(Path.GetTempPath(), $"NKitAuxDiscoveryPropTest_{Guid.NewGuid():N}");
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
        /// **Validates: Requirements 1.1, 1.2, 1.3**
        ///
        /// Property 1: Convention Discovery Determinism.
        /// For any valid set name and directory layout, aux is enabled if and only if
        /// {name}.aux.nkds exists in the same directory. No other configuration,
        /// CLI flag, or setting is consulted.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ConventionDiscovery_AuxEnabledIffAuxFileExists(
            NonNegativeInt setNameSeed,
            bool auxFilePresent)
        {
            // Generate a valid alphanumeric set name from the seed
            string setName = $"set{setNameSeed.Get}";

            // Create a unique subdirectory per test iteration to avoid collisions
            string testDir = Path.Combine(_baseTestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create the primary .nkds file (always present)
            string primaryPath = Path.Combine(testDir, $"{setName}{DataStore.DatabaseFileExtension}");
            File.WriteAllBytes(primaryPath, Array.Empty<byte>());

            // Conditionally create the aux .aux.nkds file
            if (auxFilePresent)
            {
                string auxPath = Path.Combine(testDir, $"{setName}{DataStore._AuxSetSuffix}{DataStore.DatabaseFileExtension}");
                File.WriteAllBytes(auxPath, Array.Empty<byte>());
            }

            // Act: resolve aux set name
            string result = DataStore.ResolveAuxSetName(primaryPath);

            // Property: result is non-null if and only if the aux file exists
            if (auxFilePresent)
            {
                // Aux file present → must return the expected aux set name
                return result != null
                    && result == $"{setName}{DataStore._AuxSetSuffix}";
            }
            else
            {
                // Aux file absent → must return null
                return result == null;
            }
        }
    }
}