using NKitDataStore.Compression;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Integration tests to verify the new IBlockCompressor architecture.
    /// Validates that compression/decompression work correctly and that
    /// the check-before-compress optimization functions as expected.
    /// </summary>
    public class CompressionIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly DataStore _dataStore;

        private void ensureSet(string setName)
        {
            if (_dataStore.GetSetInfo(setName) == null)
                _dataStore.CreateSet(setName);
        }

        public CompressionIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"NKitTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _dataStore = new DataStore(_tempDir);
        }

        public void Dispose()
        {
            _dataStore?.Dispose();
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Fact]
        public void BlockCompressor_CompressAndDecompress_RoundTripsCorrectly()
        {
            // Arrange
            int blockSize = 65536;
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            // Use highly compressible data (repeating pattern)
            byte[] originalData = new byte[blockSize];
            for (int i = 0; i < originalData.Length; i++)
            {
                originalData[i] = (byte)(i % 256);
            }

            // Act - Compress
            ReadOnlyMemory<byte> compressed = compressor.Compress(originalData, 0, originalData.Length, out CompressionType compressionType);
            // Compression may or may not occur depending on data, but decompression should work either way

            // Act - Decompress
            byte[] decompressed = new byte[blockSize];
            int decompressedSize = compressor.Decompress(compressed.ToArray(), 0, compressed.Length, decompressed, 0);

            // Assert
            Assert.Equal(blockSize, decompressedSize);
            Assert.Equal(originalData, decompressed);
        }

        [Fact]
        public void BlockCompressor_SmallCompressibleData_UsesCompression()
        {
            // Arrange
            int blockSize = 65536;
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            byte[] data = Enumerable.Repeat((byte)0x42, blockSize).ToArray(); // Highly compressible

            // Act
            ReadOnlyMemory<byte> compressed = compressor.Compress(data, 0, data.Length, out CompressionType compressionType);

            // Assert
            Assert.Equal(CompressionType.Zstd, compressionType);
            Assert.True(compressed.Length < data.Length);
        }

        [Fact]
        public void BlockCompressor_ZstdCompressedData_StripsFrameMagicFromStoredPayload()
        {
            // Arrange
            int blockSize = 65536;
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            byte[] data = Enumerable.Repeat((byte)0x42, blockSize).ToArray();

            // Act
            byte[] stored = compressor.Compress(data, 0, data.Length, out CompressionType compressionType).ToArray();

            // Assert
            Assert.Equal(CompressionType.Zstd, compressionType);
            Assert.True(stored.Length > 5);
            Assert.False(stored.Skip(1).Take(4).SequenceEqual(new byte[] { 0x28, 0xB5, 0x2F, 0xFD }));

            byte[] decompressed = new byte[blockSize];
            int decompressedSize = compressor.Decompress(stored, 0, stored.Length, decompressed, 0);
            Assert.Equal(blockSize, decompressedSize);
            Assert.Equal(data, decompressed);
        }

        [Fact]
        public void BlockCompressor_IncompressibleData_UsesUncompressed()
        {
            // Arrange
            int blockSize = 1024;
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            byte[] randomData = new byte[blockSize];
            new Random(42).NextBytes(randomData); // Random data doesn't compress well

            // Act
            ReadOnlyMemory<byte> compressed = compressor.Compress(randomData, 0, randomData.Length, out CompressionType compressionType);

            // Assert
            Assert.Equal(CompressionType.None, compressionType);
            Assert.Equal(randomData.Length, compressed.Length);
            Assert.Equal(randomData, compressed.ToArray());

            byte[] decompressed = new byte[blockSize];
            int size = compressor.Decompress(compressed.ToArray(), 0, compressed.Length, decompressed, 0);
            Assert.Equal(blockSize, size);
            Assert.Equal(randomData, decompressed);
        }

        [Fact]
        public void BlockCompressor_UncompressedData_IsStoredWithoutPrefixByte()
        {
            // Arrange
            int blockSize = 1024;
            using BlockCompressor compressor = new BlockCompressor(blockSize);
            byte[] randomData = new byte[blockSize];
            new Random(123).NextBytes(randomData);

            // Act
            ReadOnlyMemory<byte> stored = compressor.Compress(randomData, 0, randomData.Length, out CompressionType compressionType);

            // Assert
            Assert.Equal(CompressionType.None, compressionType);
            Assert.Equal(randomData.Length, stored.Length);
            Assert.Equal(randomData, stored.ToArray());
        }

        [Fact]
        public void ImageWriter_DuplicateBlocks_CheckBeforeCompressOptimization()
        {
            // This test verifies that duplicate blocks benefit from check-before-compress
            // We can't directly observe the optimization, but we can verify the behavior is correct

            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage";
            byte[] blockData = new byte[65536];
            Array.Fill(blockData, (byte)0xAA); // Compressible data
            ensureSet(setName);

            // Act - Write the same block twice
            using (IImageWriter writer = _dataStore.AddImage(setName, imageName))
            {
                writer.WriteData(0, blockData, BlockType.File);
                writer.WriteData(65536, blockData, BlockType.File); // Same data, different offset
                writer.FinalizeImage(blockData.Length * 2, 0x12345678, 0xABCDEF0123456789);
            }

            // Assert - Verify deduplication occurred
            DataStoreStatistics stats = _dataStore.GetSetStatistics(setName, includePerImageStats: false);
            Assert.NotNull(stats);
            Assert.Equal(1, stats.UniqueBlocksStored); // Only 1 unique block should be stored
            Assert.Equal(2, stats.TotalBlockReferences); // But referenced twice
        }

        [Fact]
        public void ImageWriter_MultipleUniqueBlocks_StoresAllBlocks()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage";
            byte[] block1 = new byte[65536];
            byte[] block2 = new byte[65536];
            Array.Fill(block1, (byte)0xAA);
            Array.Fill(block2, (byte)0xBB);

            // Act
            using (IImageWriter writer = TestDataStoreHelper.AddImage(_dataStore, setName, imageName))
            {
                writer.WriteData(0, block1, BlockType.File);
                writer.WriteData(65536, block2, BlockType.File);
                writer.FinalizeImage(block1.Length + block2.Length, 0x12345678, 0xABCDEF0123456789);
            }

            // Assert
            DataStoreStatistics stats = _dataStore.GetSetStatistics(setName, includePerImageStats: false);
            Assert.NotNull(stats);
            Assert.Equal(2, stats.UniqueBlocksStored); // 2 unique blocks
            Assert.Equal(2, stats.TotalBlockReferences); // 2 references
        }

        [Fact]
        public void ImageReaderWriter_CompressedBlocks_ReadBackCorrectly()
        {
            // Arrange
            const string setName = "TestSet";
            const string imageName = "TestImage";
            byte[] originalData = new byte[131072]; // 2 blocks
            new Random(12345).NextBytes(originalData);

            // Act - Write
            using (IImageWriter writer = TestDataStoreHelper.AddImage(_dataStore, setName, imageName))
            {
                writer.WriteData(0, originalData, BlockType.File);
                writer.FinalizeImage(originalData.Length, 0x12345678, 0xABCDEF0123456789);
            }

            // Wait for background compression tasks to complete before opening reader in tests.
            TestDataStoreHelper.WaitForSetIdle(_dataStore, setName);

            // Act - Read back
            GlobalImageKey imageKey = new GlobalImageKey(setName, 1);
            byte[] readData = new byte[originalData.Length];
            using (IImageReader reader = _dataStore.OpenImageReader(imageKey))
            using (Stream stream = reader.OpenStream(0))
            {
                int totalRead = 0;
                while (totalRead < readData.Length)
                {
                    int read = stream.Read(readData, totalRead, readData.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                Assert.Equal(originalData.Length, totalRead);
            }

            // Assert
            Assert.Equal(originalData, readData);
        }

        [Fact]
        public void BlockCompressor_BufferReuse_WorksCorrectly()
        {
            // This test verifies that buffer reuse doesn't cause data corruption
            // Arrange
            int blockSize = 65536;
            using BlockCompressor compressor = new BlockCompressor(blockSize);

            byte[] data1 = new byte[blockSize];
            byte[] data2 = new byte[blockSize];
            Array.Fill(data1, (byte)0x11);
            Array.Fill(data2, (byte)0x22);

            // Act - Compress two different blocks (buffer reuse between calls)
            byte[] compressed1 = compressor.Compress(data1, 0, data1.Length, out CompressionType type1).ToArray();
            byte[] compressed2 = compressor.Compress(data2, 0, data2.Length, out CompressionType type2).ToArray();

            // Decompress
            byte[] decompressed1 = new byte[blockSize];
            byte[] decompressed2 = new byte[blockSize];
            compressor.Decompress(compressed1, 0, compressed1.Length, decompressed1, 0);
            compressor.Decompress(compressed2, 0, compressed2.Length, decompressed2, 0);

            // Assert - Each should decompress to its original data (no cross-contamination)
            Assert.Equal(data1, decompressed1);
            Assert.Equal(data2, decompressed2);
            Assert.NotEqual(decompressed1, decompressed2);
        }

        [Fact]
        public void BlockCompressor_SupportedTypes_ReturnsExpectedFormats()
        {
            // Arrange
            using BlockCompressor compressor = new BlockCompressor(65536);

            // Act
            IReadOnlyList<CompressionType> supportedTypes = compressor.SupportedTypes;

            // Assert
            Assert.NotNull(supportedTypes);
            Assert.Contains(CompressionType.Zstd, supportedTypes);
            Assert.Contains(CompressionType.None, supportedTypes);
        }
    }
}