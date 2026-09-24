using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Tests for duplicate image detection during finalization.
    /// </summary>
    public class DuplicateImageDetectionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly DataStore _dataStore;

        private void ensureSet(string setName)
        {
            if (_dataStore.GetSetInfo(setName) == null)
                _dataStore.CreateSet(setName);
        }

        public DuplicateImageDetectionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _dataStore = new DataStore(_tempDir);
        }

        public void Dispose()
        {
            _dataStore?.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(100); // Give time for cleanup

            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        [Fact]
        public void FinalizeImage_DuplicateImageExists_RollsBack()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage.iso";
            const uint crc32 = 0x12345678;
            const ulong xxhash64 = 0xABCDEF0123456789;
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet(setName);

            // Act - Write first image
            using (IImageWriter writer1 = _dataStore.AddImage(setName, imageName))
            {
                writer1.WriteData(0, testData, BlockType.File);
                writer1.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Assert - First image should be in database
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Single(images);
            ImageRecord firstImage = Assert.Single(images);
            Assert.Equal(imageName, firstImage.Name);
            Assert.Equal(crc32, firstImage.Crc32);
            Assert.Equal(xxhash64, firstImage.XxHash64);

            // Act - Try to write duplicate image (same name, crc32, xxhash64)
            using (IImageWriter writer2 = _dataStore.AddImage(setName, imageName))
            {
                writer2.WriteData(0, testData, BlockType.File);
                writer2.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Assert - Still only one image (duplicate was rolled back)
            IEnumerable<ImageRecord> imagesAfter = _dataStore.ListAllImages();
            Assert.Single(imagesAfter);
        }

        [Fact]
        public void FinalizeImage_DifferentName_DoesNotRollback()
        {
            // Arrange
            const string setName = "TestSet";
            const uint crc32 = 0x12345678;
            const ulong xxhash64 = 0xABCDEF0123456789;
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet(setName);

            // Act - Write first image
            using (IImageWriter writer1 = _dataStore.AddImage(setName, "Image1.iso"))
            {
                writer1.WriteData(0, testData, BlockType.File);
                writer1.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Act - Write second image with different name (same hashes)
            using (IImageWriter writer2 = _dataStore.AddImage(setName, "Image2.iso"))
            {
                writer2.WriteData(0, testData, BlockType.File);
                writer2.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Assert - Both images should exist (different names)
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Equal(2, images.Count());
        }

        [Fact]
        public void FinalizeImage_DifferentCrc32_DoesNotRollback()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage.iso";
            const ulong xxhash64 = 0xABCDEF0123456789;
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet(setName);

            // Act - Write first image
            using (IImageWriter writer1 = _dataStore.AddImage(setName, imageName))
            {
                writer1.WriteData(0, testData, BlockType.File);
                writer1.FinalizeImage(testData.Length, 0x11111111, xxhash64);
            }

            // Act - Write second image with different CRC32 (same name and xxhash64)
            using (IImageWriter writer2 = _dataStore.AddImage(setName, imageName))
            {
                writer2.WriteData(0, testData, BlockType.File);
                writer2.FinalizeImage(testData.Length, 0x22222222, xxhash64);
            }

            // Assert - Both images should exist (different CRC32)
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Equal(2, images.Count());
        }

        [Fact]
        public void FinalizeImage_DifferentXxHash64_DoesNotRollback()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage.iso";
            const uint crc32 = 0x12345678;
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet(setName);

            // Act - Write first image
            using (IImageWriter writer1 = _dataStore.AddImage(setName, imageName))
            {
                writer1.WriteData(0, testData, BlockType.File);
                writer1.FinalizeImage(testData.Length, crc32, 0x1111111111111111);
            }

            // Act - Write second image with different XXHash64 (same name and crc32)
            using (IImageWriter writer2 = _dataStore.AddImage(setName, imageName))
            {
                writer2.WriteData(0, testData, BlockType.File);
                writer2.FinalizeImage(testData.Length, crc32, 0x2222222222222222);
            }

            // Assert - Both images should exist (different XXHash64)
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Equal(2, images.Count());
        }

        [Fact]
        public void FinalizeImage_DuplicateInDifferentSet_DoesNotRollback()
        {
            // Arrange
            const string imageName = "TestImage.iso";
            const uint crc32 = 0x12345678;
            const ulong xxhash64 = 0xABCDEF0123456789;
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet("Set1");
            ensureSet("Set2");

            // Act - Write image in first set
            using (IImageWriter writer1 = _dataStore.AddImage("Set1", imageName))
            {
                writer1.WriteData(0, testData, BlockType.File);
                writer1.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Act - Write identical image in second set
            using (IImageWriter writer2 = _dataStore.AddImage("Set2", imageName))
            {
                writer2.WriteData(0, testData, BlockType.File);
                writer2.FinalizeImage(testData.Length, crc32, xxhash64);
            }

            // Assert - Both images should exist (different sets)
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Equal(2, images.Count());
        }

        [Fact]
        public void WriteWithoutFinalize_AlwaysRollsBack()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage.iso";
            byte[] testData = new byte[65536];
            new Random(42).NextBytes(testData);
            ensureSet(setName);

            // Act - Write image but don't finalize
            using (IImageWriter writer = _dataStore.AddImage(setName, imageName))
            {
                writer.WriteData(0, testData, BlockType.File);
                // Intentionally not calling FinalizeImage
            }

            // Assert - Image should not exist (transaction rolled back)
            IEnumerable<ImageRecord> images = _dataStore.ListAllImages();
            Assert.Empty(images);
        }
    }
}