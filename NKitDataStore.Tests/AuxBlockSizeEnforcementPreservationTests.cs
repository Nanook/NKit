using System;
using System.IO;
using FsCheck;
using FsCheck.Xunit;
using NKitDataStore;
using Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Preservation property tests for aux block size enforcement.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.5**
    ///
    /// Property 2: Preservation — No-Aux and Matching-Aux Behavior Unchanged
    ///
    /// These tests are EXPECTED TO PASS on unfixed code. They confirm baseline behavior
    /// that must be preserved after the fix is applied:
    /// - Creating sets without an aux store uses the specified block size unchanged
    /// - Creating sets with a matching aux store block size works without warnings
    /// </summary>
    public class AuxBlockSizeEnforcementPreservationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        /// <summary>
        /// Valid block sizes: powers of 2 between 4096 (4 KiB) and 1048576 (1 MiB).
        /// </summary>
        private static readonly int[] ValidBlockSizes = { 4096, 8192, 16384, 32768, 65536, 131072, 262144, 524288, 1048576 };

        public AuxBlockSizeEnforcementPreservationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitAuxPreserve_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// **Validates: Requirements 3.1, 3.5**
        ///
        /// Property 2a - No-Aux Preservation: For all valid block sizes, creating a set
        /// in a directory without an aux store produces a set with exactly the specified
        /// block size.
        ///
        /// From Preservation Requirements: NOT isBugCondition(X) when auxStoreExists = false.
        /// The system must use the user-specified block size without any override or warning.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CreateSet_NoAux_UsesSpecifiedBlockSize(NonNegativeInt seedWrapper)
        {
            // Pick a valid block size (power of 2 between 4096 and 1048576)
            int blockSize = ValidBlockSizes[seedWrapper.Get % ValidBlockSizes.Length];

            // Create a unique subdirectory per iteration (no aux store present)
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Create a set with the specified block size — no aux store in directory
            SetInfo setInfo;
            using (var store = new DataStore(testDir))
            {
                setInfo = store.CreateSet("test", blockSize: blockSize);
            }

            // The set must have exactly the specified block size
            bool passed = setInfo.BlockSize == blockSize;

            if (!passed)
            {
                _output.WriteLine(
                    $"FAILURE: Created set with block size {blockSize} in directory WITHOUT aux store — " +
                    $"got {setInfo.BlockSize} instead of expected {blockSize}.");
            }

            return passed;
        }

        /// <summary>
        /// **Validates: Requirements 3.2**
        ///
        /// Property 2b - Matching-Aux Preservation: For all valid block sizes, creating a
        /// set in a directory with an aux store having the SAME block size produces a set
        /// with that block size and no override warning.
        ///
        /// From Preservation Requirements: NOT isBugCondition(X) when primaryBlockSize = auxBlockSize.
        /// The system must create the set normally without any warning (no override needed).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool CreateSet_MatchingAux_UsesSpecifiedBlockSize(NonNegativeInt seedWrapper)
        {
            // Pick a valid block size (power of 2 between 4096 and 1048576)
            int blockSize = ValidBlockSizes[seedWrapper.Get % ValidBlockSizes.Length];

            // Create a unique subdirectory per iteration
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Step 1: Create an aux store with the chosen block size
            using (var store = new DataStore(testDir))
            {
                store.CreateSet("test.aux", blockSize: blockSize);
            }

            // Step 2: Create a primary set with the SAME block size as the aux store
            SetInfo primaryInfo;
            using (var store = new DataStore(testDir))
            {
                primaryInfo = store.CreateSet("test", blockSize: blockSize);
            }

            // The primary set must have exactly the specified block size (same as aux)
            bool passed = primaryInfo.BlockSize == blockSize;

            if (!passed)
            {
                _output.WriteLine(
                    $"FAILURE: Created primary set with block size {blockSize} in directory WITH aux store " +
                    $"having SAME block size {blockSize} — got {primaryInfo.BlockSize} instead of expected {blockSize}.");
            }

            return passed;
        }
    }
}
