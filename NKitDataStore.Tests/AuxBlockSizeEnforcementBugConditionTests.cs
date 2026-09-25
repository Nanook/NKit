using System;
using System.IO;
using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit.Vfs;
using Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Bug condition exploration test for aux block size enforcement.
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// Property 1: Bug Condition — Block Size Not Forced to Aux Value on Creation
    ///
    /// This test is EXPECTED TO FAIL on unfixed code. Failure confirms the bug exists:
    /// when a primary set is created in a directory containing an aux store with a
    /// different block size, the primary set should get the aux store's block size
    /// (expected behavior), but instead gets the user-specified block size (bug).
    /// </summary>
    public class AuxBlockSizeEnforcementBugConditionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public AuxBlockSizeEnforcementBugConditionTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitAuxBlockSizeBug_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 2.1, 2.2**
        ///
        /// Property 1: Bug Condition — For any set creation where an aux store exists
        /// with a different block size, the created primary set's BlockSize must equal
        /// the aux store's block size (the system should override the user's value).
        ///
        /// On unfixed code, this FAILS because CreateSet uses the user-specified block
        /// size without checking the aux store's block size.
        ///
        /// The aux block size is varied between 32KiB, 64KiB, and 128KiB using FsCheck.
        /// The primary block size is always chosen to differ from the aux block size.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool CreateSet_BlockSize_MustMatchAuxBlockSize(NonNegativeInt seedWrapper)
        {
            // Choose aux block size from {32KiB, 64KiB, 128KiB}
            int[] auxBlockSizes = { 32768, 65536, 131072 };
            int auxBlockSize = auxBlockSizes[seedWrapper.Get % auxBlockSizes.Length];

            // Choose a primary block size that DIFFERS from the aux block size
            int primaryBlockSize = auxBlockSize == 65536 ? 32768 : 65536;

            // Create a unique subdirectory per iteration to avoid collisions
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Step 1: Create the aux store with the chosen block size
            using (var store = new DataStore(testDir))
            {
                store.CreateSet("wii.aux", blockSize: auxBlockSize);
            }

            // Step 2: Create a primary set with a DIFFERENT block size
            SetInfo primaryInfo;
            using (var store = new DataStore(testDir))
            {
                primaryInfo = store.CreateSet("wii", blockSize: primaryBlockSize);
            }

            // Assertion: the primary set's block size must equal the aux store's block size
            // (the system should have overridden the user's value to match aux)
            bool passed = primaryInfo.BlockSize == auxBlockSize;

            if (!passed)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE: Created primary set with requested block size {primaryBlockSize / 1024}KiB " +
                    $"in directory with aux store block size {auxBlockSize / 1024}KiB — " +
                    $"primary got {primaryInfo.BlockSize / 1024}KiB instead of expected {auxBlockSize / 1024}KiB. " +
                    $"Bug confirmed: system does not override block size to match aux store.");
            }

            return passed;
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 2.1, 2.2**
        ///
        /// Property 1 (ExecuteCreate path): Bug Condition — For any CLI-based set creation
        /// where an aux store exists with a different block size, the created primary set's
        /// BlockSize must equal the aux store's block size.
        ///
        /// On unfixed code, this FAILS because ExecuteCreate passes the user-specified
        /// block size directly to CreateSet without checking the aux store.
        ///
        /// The aux block size is varied between 32KiB, 64KiB, and 128KiB using FsCheck.
        /// </summary>
        [Property(MaxTest = 20)]
        public bool ExecuteCreate_BlockSize_MustMatchAuxBlockSize(NonNegativeInt seedWrapper)
        {
            // Choose aux block size from {32KiB, 64KiB, 128KiB}
            int[] auxBlockSizes = { 32768, 65536, 131072 };
            int auxBlockSize = auxBlockSizes[seedWrapper.Get % auxBlockSizes.Length];

            // Choose a primary block size that DIFFERS from the aux block size
            int primaryBlockSize = auxBlockSize == 65536 ? 32768 : 65536;
            string primaryBlockSizeText = $"{primaryBlockSize / 1024}KiB";

            // Create a unique subdirectory per iteration to avoid collisions
            string testDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            // Step 1: Create the aux store with the chosen block size
            using (var store = new DataStore(testDir))
            {
                store.CreateSet("wii.aux", blockSize: auxBlockSize);
            }

            // Step 2: Call ExecuteCreate with a DIFFERENT block size via CLI path
            var request = new NkdsCommandRequest
            {
                Command = NkdsCommand.Create,
                DataStorePath = testDir,
                SetName = "wii",
                BlockSizeText = primaryBlockSizeText
            };

            // Capture console output (ExecuteCreate writes to Console)
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                NkdsCommandLine.ExecuteCreate(request);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Step 3: Read back the created set's block size
            SetInfo? primaryInfo;
            using (var store = new DataStore(testDir))
            {
                primaryInfo = store.GetSetInfo("wii");
            }

            if (primaryInfo == null)
            {
                _output.WriteLine("COUNTEREXAMPLE: Primary set 'wii' was not created.");
                return false;
            }

            // Assertion: the primary set's block size must equal the aux store's block size
            bool passed = primaryInfo.BlockSize == auxBlockSize;

            if (!passed)
            {
                _output.WriteLine(
                    $"COUNTEREXAMPLE (ExecuteCreate): Created primary set with --block-size {primaryBlockSizeText} " +
                    $"in directory with aux store block size {auxBlockSize / 1024}KiB — " +
                    $"primary got {primaryInfo.BlockSize / 1024}KiB instead of expected {auxBlockSize / 1024}KiB. " +
                    $"Bug confirmed: ExecuteCreate does not override block size to match aux store.");
            }

            return passed;
        }
    }
}
