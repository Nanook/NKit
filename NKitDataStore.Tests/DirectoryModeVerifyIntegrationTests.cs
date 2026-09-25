using NKDS;
using NKDS.Models;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration tests for the AddDirAsync → VerifyAsync round-trip.
    /// Validates that directories ingested via NkdsOperations can be verified successfully.
    ///
    /// **Validates: Requirements 3.1, 3.2, 4.1, 5.1**
    /// </summary>
    public class DirectoryModeVerifyIntegrationTests : IDisposable
    {
        private readonly string _tempBase;

        public DirectoryModeVerifyIntegrationTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), "NKitDirVerifyTests", Path.GetRandomFileName());
            Directory.CreateDirectory(_tempBase);
        }

        public void Dispose()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(_tempBase, recursive: true); } catch { }
        }

        /// <summary>
        /// Creates a temp directory with known files, runs AddDirAsync, then VerifyAsync,
        /// and asserts VerifySuccess (no errors).
        /// </summary>
        [Fact]
        public async Task AddDir_ThenVerify_ReportsSuccess()
        {
            // Arrange
            string dataStorePath = Path.Combine(_tempBase, "store1");
            string sourceDir = Path.Combine(_tempBase, "source1");
            Directory.CreateDirectory(dataStorePath);
            Directory.CreateDirectory(sourceDir);

            // Create known files
            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "hello");
            string subDir = Path.Combine(sourceDir, "subdir");
            Directory.CreateDirectory(subDir);
            File.WriteAllBytes(Path.Combine(subDir, "file2.bin"), new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });

            const string setName = "TestSet";

            // Act - Add directory
            using NkdsOperations ops = new NkdsOperations();
            OperationResult addResult = await ops.AddDirAsync(dataStorePath, setName, new[] { sourceDir });

            // Assert add succeeded
            Assert.True(addResult.Success, $"AddDirAsync failed: {string.Join("; ", addResult.Errors.Select(e => e.Reason))}");

            // Get the image ID
            long imageId;
            using (DataStore ds = new DataStore(dataStorePath))
            {
                List<ImageRecord> images = ds.ListImagesInSet(setName);
                Assert.Single(images);
                imageId = images[0].Id;

                // Assert image has non-zero checksums
                Assert.True(images[0].Size > 0);
                Assert.NotEqual(0u, images[0].Crc32);
                Assert.NotEqual(0ul, images[0].XxHash64);
            }

            // Act - Verify
            bool? verifiedResult = null;
            OperationResult verifyResult = await ops.VerifyAsync(dataStorePath, setName, new[] { imageId },
                onImageVerified: (id, success) => verifiedResult = success);

            // Assert verify succeeded
            Assert.True(verifyResult.Success, $"VerifyAsync failed: {string.Join("; ", verifyResult.Errors.Select(e => e.Reason))}");
            Assert.Equal(0, verifyResult.ItemsFailed);
            Assert.True(verifiedResult == true, "onImageVerified should have been called with success=true");
        }

        /// <summary>
        /// Tests empty directory round-trip: zero files → VerifySuccess.
        /// </summary>
        [Fact]
        public async Task AddDir_EmptyDirectory_ThenVerify_ReportsSuccess()
        {
            // Arrange
            string dataStorePath = Path.Combine(_tempBase, "store2");
            string sourceDir = Path.Combine(_tempBase, "source2");
            Directory.CreateDirectory(dataStorePath);
            Directory.CreateDirectory(sourceDir);
            // sourceDir is intentionally empty

            const string setName = "EmptySet";

            // Act - Add empty directory
            using NkdsOperations ops = new NkdsOperations();
            OperationResult addResult = await ops.AddDirAsync(dataStorePath, setName, new[] { sourceDir });

            // Assert add succeeded
            Assert.True(addResult.Success, $"AddDirAsync failed: {string.Join("; ", addResult.Errors.Select(e => e.Reason))}");

            // Get the image
            long imageId;
            using (DataStore ds = new DataStore(dataStorePath))
            {
                List<ImageRecord> images = ds.ListImagesInSet(setName);
                Assert.Single(images);
                imageId = images[0].Id;

                // Assert image has zero checksums (empty directory)
                Assert.Equal(0, images[0].Size);
                Assert.Equal(0u, images[0].Crc32);
                Assert.Equal(0ul, images[0].XxHash64);
            }

            // Act - Verify
            bool? verifiedResult = null;
            OperationResult verifyResult = await ops.VerifyAsync(dataStorePath, setName, new[] { imageId },
                onImageVerified: (id, success) => verifiedResult = success);

            // Assert verify succeeded (empty directory short-circuit)
            Assert.True(verifyResult.Success, $"VerifyAsync failed: {string.Join("; ", verifyResult.Errors.Select(e => e.Reason))}");
            Assert.Equal(0, verifyResult.ItemsFailed);
            Assert.True(verifiedResult == true, "onImageVerified should have been called with success=true for empty directory");
        }

        /// <summary>
        /// Tests that re-adding the same directory produces a new image with identical checksums.
        /// Note: The DataStore deduplicates images with identical name+CRC32+XxHash64,
        /// so re-adding the same directory results in the same image being retained.
        /// This test verifies the checksums are deterministic across adds.
        /// </summary>
        [Fact]
        public async Task AddDir_SameDirectoryTwice_ProducesIdenticalChecksums()
        {
            // Arrange
            string dataStorePath = Path.Combine(_tempBase, "store3");
            string sourceDir = Path.Combine(_tempBase, "source3");
            Directory.CreateDirectory(dataStorePath);
            Directory.CreateDirectory(sourceDir);

            // Create known files
            File.WriteAllText(Path.Combine(sourceDir, "alpha.txt"), "deterministic content");
            string subDir = Path.Combine(sourceDir, "nested");
            Directory.CreateDirectory(subDir);
            File.WriteAllBytes(Path.Combine(subDir, "data.bin"), new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 });

            const string setName = "DeterministicSet";

            // Act - Add directory first time
            using NkdsOperations ops = new NkdsOperations();
            OperationResult addResult1 = await ops.AddDirAsync(dataStorePath, setName, new[] { sourceDir });
            Assert.True(addResult1.Success, $"First AddDirAsync failed: {string.Join("; ", addResult1.Errors.Select(e => e.Reason))}");

            // Get first image record
            ImageRecord firstImage;
            using (DataStore ds = new DataStore(dataStorePath))
            {
                List<ImageRecord> images = ds.ListImagesInSet(setName).Where(i => !i.Removed).ToList();
                Assert.Single(images);
                firstImage = images[0];
            }

            // Act - Add directory second time (DataStore deduplicates identical images)
            OperationResult addResult2 = await ops.AddDirAsync(dataStorePath, setName, new[] { sourceDir });
            Assert.True(addResult2.Success, $"Second AddDirAsync failed: {string.Join("; ", addResult2.Errors.Select(e => e.Reason))}");

            // Get image record after second add
            ImageRecord secondImage;
            using (DataStore ds = new DataStore(dataStorePath))
            {
                // Due to deduplication, there should still be exactly one non-removed image
                List<ImageRecord> images = ds.ListImagesInSet(setName).Where(i => !i.Removed).ToList();
                Assert.Single(images);
                secondImage = images[0];
            }

            // Assert - Checksums are identical (deterministic computation)
            Assert.Equal(firstImage.Size, secondImage.Size);
            Assert.Equal(firstImage.Crc32, secondImage.Crc32);
            Assert.Equal(firstImage.XxHash64, secondImage.XxHash64);

            // Assert - Non-zero checksums were computed
            Assert.True(firstImage.Size > 0);
            Assert.NotEqual(0u, firstImage.Crc32);
            Assert.NotEqual(0ul, firstImage.XxHash64);
        }
    }
}