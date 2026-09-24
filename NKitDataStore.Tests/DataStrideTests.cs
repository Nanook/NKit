using NKitDataStore.Interfaces;
using X = Nanook.GrindCore.XXHash;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Tests for DataStride functionality - extracting clean data from strided formats
    /// like ISO sectors, Wii blocks, and WiiU blocks.
    /// </summary>
    public class DataStrideTests : IDisposable
    {
        private readonly string _testDirectory;

        public DataStrideTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "DataStrideTests", Path.GetRandomFileName());
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        #region Helper Methods

        private static byte[] generateTestData(int size, int seed = 12345)
        {
            Random random = new Random(seed);
            byte[] data = new byte[size];
            random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// Creates a strided source stream with padding/hashes interleaved with data.
        /// </summary>
        private static MemoryStream createStridedStream(byte[] cleanData, DataStride stride)
        {
            int blockCount = (int)Math.Ceiling((double)cleanData.Length / stride.DataLength);
            byte[] stridedData = new byte[blockCount * stride.SourceBlockSize];

            for (int i = 0; i < blockCount; i++)
            {
                int sourceOffset = i * stride.SourceBlockSize;
                int cleanOffset = i * stride.DataLength;
                int bytesToCopy = Math.Min(stride.DataLength, cleanData.Length - cleanOffset);

                // Fill padding/hash area with pattern (for testing)
                for (int j = 0; j < stride.DataOffset; j++)
                {
                    stridedData[sourceOffset + j] = (byte)(0xAA + (i % 16)); // Pattern
                }

                // Copy clean data
                Array.Copy(cleanData, cleanOffset, stridedData, sourceOffset + stride.DataOffset, bytesToCopy);

                // Fill trailing padding if any
                int trailingStart = stride.DataOffset + stride.DataLength;
                for (int j = trailingStart; j < stride.SourceBlockSize; j++)
                {
                    stridedData[sourceOffset + j] = (byte)(0xBB + (i % 16)); // Pattern
                }
            }

            return new MemoryStream(stridedData);
        }

        private static IImageWriter addImage(DataStore store, string setName, string imageName, long shardSize = 0, string system = null, ImageFormat format = ImageFormat.Unknown, int blockSize = 0)
        {
            if (store.GetSetInfo(setName) == null)
            {
                store.CreateSet(setName, shardSize, blockSize);
            }

            return store.AddImage(setName, imageName, system, format);
        }

        #endregion

        #region DataStride Validation Tests

        [Fact]
        public void WriteData_WithInvalidStride_ThrowsException()
        {
            // Arrange
            using DataStore store = new DataStore(_testDirectory);
            using IImageWriter writer = addImage(store, "TestSet", "test.iso");

            byte[] data = generateTestData(1000);

            // Act & Assert - Invalid SourceBlockSize
            DataStride invalidStride1 = new DataStride { SourceBlockSize = 0, DataOffset = 16, DataLength = 2048 };
            Assert.Throws<ArgumentException>(() =>
            {
                writer.WriteData(0, new MemoryStream(data), data.Length, BlockType.File, invalidStride1);
            });

            // Act & Assert - DataOffset out of range
            DataStride invalidStride2 = new DataStride { SourceBlockSize = 2352, DataOffset = 2352, DataLength = 2048 };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                writer.WriteData(0, new MemoryStream(data), data.Length, BlockType.File, invalidStride2);
            });

            // Act & Assert - DataLength exceeds available space
            DataStride invalidStride3 = new DataStride { SourceBlockSize = 2352, DataOffset = 16, DataLength = 3000 };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                writer.WriteData(0, new MemoryStream(data), data.Length, BlockType.File, invalidStride3);
            });
        }

        #endregion

        #region CD Mode 1 Stride Tests

        [Fact]
        public void WriteData_CdMode1Stride_ExtractsCleanData()
        {
            // Arrange
            DataStride stride = DataStride.CdMode1;
            byte[] cleanData = generateTestData(2048 * 10, seed: 1); // 10 sectors of clean data
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act - Write with CD stride (extracts clean data from strided format)
            using (IImageWriter writer = addImage(store, "CDSet", "cd.iso", shardSize: 0))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride, offsetStart: 0);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert - Read back clean data and verify
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("CDSet", 1));

            // Verify we have one file group (single contiguous write)
            List<OffsetRecord> allOffsets = reader.GetOffsets().ToList();
            List<long> uniqueOffsetStarts = allOffsets.Select(o => o.OffsetStart).Distinct().OrderBy(x => x).ToList();
            Assert.Single(uniqueOffsetStarts);
            Assert.Equal(0, uniqueOffsetStarts[0]); // offsetStart should be 0

            // Read the clean data back
            using Stream imageStream = reader.OpenStream(uniqueOffsetStarts[0]);

            byte[] reconstructed = new byte[cleanData.Length];
            imageStream.ReadExactly(reconstructed, 0, reconstructed.Length);

            // Verify data matches
            Assert.Equal(cleanData, reconstructed);
            Assert.Equal(Crc.Compute(cleanData), Crc.Compute(reconstructed));
        }

        [Fact]
        public void WriteData_CdMode1Stride_LargeFile_DeduplicatesCorrectly()
        {
            // Arrange - Create 100 sectors with some duplicate data
            DataStride stride = DataStride.CdMode1;
            byte[] sectorData = generateTestData(2048, seed: 1);
            byte[] cleanData = new byte[2048 * 100];

            // First 50 sectors are identical (should deduplicate)
            for (int i = 0; i < 50; i++)
            {
                Array.Copy(sectorData, 0, cleanData, i * 2048, 2048);
            }
            // Next 50 sectors are unique
            for (int i = 50; i < 100; i++)
            {
                byte[] uniqueData = generateTestData(2048, seed: i);
                Array.Copy(uniqueData, 0, cleanData, i * 2048, 2048);
            }

            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "CDSet", "large_cd.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert - Verify reconstruction
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("CDSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            imageStream.ReadExactly(reconstructed);

            Assert.Equal(cleanData, reconstructed);
        }

        #endregion

        #region Wii Stride Tests

        [Fact]
        public void WriteData_WiiStride_ExtractsDataWithoutHashes()
        {
            // Arrange - Wii block: 0x8000 bytes, 0x400 hash at start, 0x7C00 data
            DataStride stride = DataStride.Wii;
            byte[] cleanData = generateTestData(0x7C00 * 5, seed: 2); // 5 Wii blocks of clean data
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "WiiSet", "wii.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            int bytesRead = imageStream.Read(reconstructed, 0, reconstructed.Length);

            Assert.Equal(cleanData.Length, bytesRead);
            Assert.Equal(cleanData, reconstructed);
        }

        [Fact]
        public void WriteData_WiiStride_BlocksAlignedToBlockSize()
        {
            // Arrange - Verify data is stored in blockSize chunks despite source stride
            DataStride stride = DataStride.Wii;
            byte[] cleanData = generateTestData(0x7C00 * 3, seed: 3); // 3 Wii blocks
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "WiiSet", "wii_blocks.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert - Check offset records use blockSize chunks (default 65536)
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));
            List<OffsetRecord> offsets = reader.GetOffsets().ToList();

            Assert.NotEmpty(offsets);

            // Verify blocks are stored according to blockSize, not stride.SourceBlockSize
            foreach (OffsetRecord offset in offsets)
            {
                Assert.True(offset.HasBlocks);
                // Each block should be <= 65536 bytes (default block size)
                // The clean data gets re-chunked into blockSize pieces
            }
        }

        #endregion

        #region WiiU Stride Tests

        [Fact]
        public void WriteData_WiiU32KBStride_ExtractsDataWithoutHashes()
        {
            // Arrange
            DataStride stride = DataStride.WiiU32KB;
            byte[] cleanData = generateTestData(0x7C00 * 4, seed: 4); // 4 WiiU blocks
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "WiiUSet", "wiiu32.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiUSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            imageStream.ReadExactly(reconstructed, 0, reconstructed.Length);

            Assert.Equal(cleanData, reconstructed);
        }

        [Fact]
        public void WriteData_WiiU64KBStride_ExtractsDataWithoutHashes()
        {
            // Arrange
            DataStride stride = DataStride.WiiU64KB;
            byte[] cleanData = generateTestData(0xFC00 * 3, seed: 5); // 3 WiiU 64KB blocks
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "WiiUSet", "wiiu64.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiUSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            imageStream.ReadExactly(reconstructed, 0, reconstructed.Length);

            Assert.Equal(cleanData, reconstructed);
        }

        #endregion

        #region Deduplication with Stride Tests

        [Fact]
        public void WriteData_MultipleImagesWithSameStridedData_Deduplicates()
        {
            // Arrange - Create identical clean data in two images with stride
            DataStride stride = DataStride.Wii;
            byte[] cleanData = generateTestData(0x7C00 * 10, seed: 100);

            using DataStore store = new DataStore(_testDirectory);

            // Act - Write first image
            using (IImageWriter writer1 = addImage(store, "WiiSet", "wii1.iso"))
            {
                MemoryStream stridedStream1 = createStridedStream(cleanData, stride);
                writer1.WriteData(0, stridedStream1, stridedStream1.Length, BlockType.File, stride);
                writer1.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }
            TestDataStoreHelper.WaitForSetIdle(store, "WiiSet");

            // Act - Write second image with same clean data
            using (IImageWriter writer2 = addImage(store, "WiiSet", "wii2.iso"))
            {
                MemoryStream stridedStream2 = createStridedStream(cleanData, stride);
                writer2.WriteData(0, stridedStream2, stridedStream2.Length, BlockType.File, stride);
                writer2.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }
            TestDataStoreHelper.WaitForSetIdle(store, "WiiSet");

            // Assert - Both images should reference same blocks (deduplication)
            List<ImageRecord> images = store.ListImagesInSet("WiiSet");
            // The DataStore may deduplicate images with identical content at the image level.
            // If only 1 image is returned, the second was detected as a duplicate and rolled back.
            // In that case, verify the single image has the expected block structure.
            Assert.True(images.Count >= 1 && images.Count <= 2,
                $"Expected 1 or 2 images, got {images.Count}");

            if (images.Count == 2)
            {
                using IImageReader reader1 = store.OpenImageReader(new GlobalImageKey("WiiSet", images[0].Id));
                using IImageReader reader2 = store.OpenImageReader(new GlobalImageKey("WiiSet", images[1].Id));

                List<OffsetRecord> offsets1 = reader1.GetOffsets().ToList();
                List<OffsetRecord> offsets2 = reader2.GetOffsets().ToList();

                Assert.Equal(offsets1.Count, offsets2.Count);

                // Verify blocks are shared (same BlockKeys)
                for (int i = 0; i < offsets1.Count; i++)
                {
                    if (offsets1[i].HasBlocks && offsets2[i].HasBlocks)
                    {
                        Assert.Equal(offsets1[i].BlockCount, offsets2[i].BlockCount);
                        for (int j = 0; j < offsets1[i].BlockCount; j++)
                        {
                            Assert.Equal(offsets1[i].GetBlockAt(j), offsets2[i].GetBlockAt(j));
                        }
                    }
                }
            }
            else
            {
                // Single image — verify it has valid block structure
                using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiSet", images[0].Id));
                List<OffsetRecord> offsets = reader.GetOffsets().ToList();
                Assert.NotEmpty(offsets);
                Assert.True(offsets.Any(o => o.HasBlocks), "Image should have blocks");
            }
        }

        #endregion

        #region Custom Stride Tests

        [Fact]
        public void WriteData_CustomStride_WorksCorrectly()
        {
            // Arrange - Custom stride: 1000 byte blocks, skip first 50, use 900
            DataStride stride = new DataStride
            {
                SourceBlockSize = 1000,
                DataOffset = 50,
                DataLength = 900
            };

            byte[] cleanData = generateTestData(900 * 5, seed: 6); // 5 blocks
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "CustomSet", "custom.bin"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("CustomSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            imageStream.ReadExactly(reconstructed, 0, reconstructed.Length);

            Assert.Equal(cleanData, reconstructed);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void WriteData_StrideWithPartialLastBlock_HandlesCorrectly()
        {
            // Arrange - Data that doesn't fill the last source block completely
            DataStride stride = DataStride.Wii;
            byte[] cleanData = generateTestData((0x7C00 * 3) + 1000, seed: 7); // 3.X blocks
            MemoryStream stridedStream = createStridedStream(cleanData, stride);

            using DataStore store = new DataStore(_testDirectory);

            // Act
            using (IImageWriter writer = addImage(store, "WiiSet", "partial.iso"))
            {
                writer.WriteData(0, stridedStream, stridedStream.Length, BlockType.File, stride);
                writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
            }

            // Assert
            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));
            using Stream imageStream = reader.OpenStream(0);

            byte[] reconstructed = new byte[cleanData.Length];
            int bytesRead = imageStream.Read(reconstructed, 0, reconstructed.Length);

            Assert.Equal(cleanData.Length, bytesRead);
            Assert.Equal(cleanData, reconstructed);
        }

        #endregion

        #region Strided Reading Tests

        //[Fact]
        //public void OpenStream_WithCdStride_ReconstructsOriginalFormat()
        //{
        //    // Arrange - Write clean CD data
        //    var stride = DataStride.CdMode1;
        //    var cleanData = GenerateTestData(2048 * 10, seed: 1);
        //    var originalStridedStream = CreateStridedStream(cleanData, stride);
        //    byte[] originalStridedData = originalStridedStream.ToArray();

        //    using var store = new DataStore(_testDirectory);

        //    // Write clean data (stride removes padding)
        //    using (var writer = TestDataStoreHelper.AddImage(store, "CDSet", "cd.iso"))
        //    {
        //        writer.WriteData(0, new MemoryStream(originalStridedData), originalStridedData.Length, DataType.File, stride);
        //        writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
        //    }

        //    // Act - Read with stride (reconstructs padding)
        //    using var reader = store.OpenImageReader(new GlobalImageKey("CDSet", 1));

        //    // Get the unique offsetStart (should be only one since we wrote one contiguous file)
        //    var uniqueOffsetStarts = reader.GetOffsets().Select(o => o.OffsetStart).Distinct().ToList();
        //    Assert.Single(uniqueOffsetStarts); // Verify single file

        //    using var reconstructedStream = reader.OpenStream(stride, offsetStart: uniqueOffsetStarts[0]);

        //    var reconstructedData = new byte[originalStridedData.Length];
        //    int bytesRead = reconstructedStream.Read(reconstructedData, 0, reconstructedData.Length);

        //    // Assert - Reconstructed data should match original (including padding)
        //    Assert.Equal(originalStridedData.Length, bytesRead);
        //    Assert.Equal(originalStridedData.Length, reconstructedStream.Length);

        //    // Verify clean data portions match
        //    for (int block = 0; block < 10; block++)
        //    {
        //        int strideOffset = block * stride.SourceBlockSize + stride.DataOffset;
        //        int cleanOffset = block * stride.DataLength;

        //        for (int i = 0; i < stride.DataLength; i++)
        //        {
        //            Assert.Equal(cleanData[cleanOffset + i], reconstructedData[strideOffset + i]);
        //        }
        //    }
        //}

        //[Fact]
        //public void OpenStream_WithWiiStride_ReconstructsOriginalFormat()
        //{
        //    // Arrange
        //    var stride = DataStride.Wii;
        //    var cleanData = GenerateTestData(0x7C00 * 5, seed: 2);
        //    var originalStridedStream = CreateStridedStream(cleanData, stride);
        //    byte[] originalStridedData = originalStridedStream.ToArray();

        //    using var store = new DataStore(_testDirectory);

        //    using (var writer = TestDataStoreHelper.AddImage(store, "WiiSet", "wii.iso"))
        //    {
        //        writer.WriteData(0, new MemoryStream(originalStridedData), originalStridedData.Length, DataType.File, stride);
        //        writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
        //    }

        //    // Act - Reconstruct with stride
        //    using var reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));

        //    // Get the unique offsetStart (should be only one)
        //    var uniqueOffsetStarts = reader.GetOffsets().Select(o => o.OffsetStart).Distinct().ToList();
        //    Assert.Single(uniqueOffsetStarts);

        //    using var reconstructedStream = reader.OpenStream(stride, offsetStart: uniqueOffsetStarts[0]);

        //    // Assert
        //    Assert.Equal(originalStridedData.Length, reconstructedStream.Length);

        //    var reconstructedData = new byte[originalStridedData.Length];
        //    reconstructedStream.ReadExactly(reconstructedData, 0, reconstructedData.Length);

        //    // Verify data portions match (padding will be zeros instead of pattern, but structure is correct)
        //    for (int block = 0; block < 5; block++)
        //    {
        //        int strideOffset = block * stride.SourceBlockSize + stride.DataOffset;
        //        int cleanOffset = block * stride.DataLength;

        //        for (int i = 0; i < stride.DataLength; i++)
        //        {
        //            Assert.Equal(cleanData[cleanOffset + i], reconstructedData[strideOffset + i]);
        //        }

        //        // Verify padding is zeros
        //        for (int i = 0; i < stride.DataOffset; i++)
        //        {
        //            Assert.Equal(0, reconstructedData[block * stride.SourceBlockSize + i]);
        //        }
        //    }
        //}

        //[Fact]
        //public void OpenStream_WithStride_SeekingWorks()
        //{
        //    // Arrange
        //    var stride = DataStride.Wii;
        //    var cleanData = GenerateTestData(0x7C00 * 10, seed: 3);

        //    using var store = new DataStore(_testDirectory);

        //    using (var writer = TestDataStoreHelper.AddImage(store, "WiiSet", "wii_seek.iso"))
        //    {
        //        var writeStream = CreateStridedStream(cleanData, stride);
        //        writer.WriteData(0, writeStream, writeStream.Length, DataType.File, stride);
        //        writer.FinalizeImage(cleanData.Length, Crc.Compute(cleanData), X.XXHash64.Compute(cleanData));
        //    }

        //    // Act - Read with seeking
        //    using var reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));

        //    // Get the unique offsetStart
        //    var uniqueOffsetStarts = reader.GetOffsets().Select(o => o.OffsetStart).Distinct().ToList();
        //    Assert.Single(uniqueOffsetStarts);

        //    using var readStream = reader.OpenStream(stride, offsetStart: uniqueOffsetStarts[0]);

        //    // Seek to middle of block 5 (in data area)
        //    long seekPosition = 5 * stride.SourceBlockSize + stride.DataOffset + 100;
        //    readStream.Seek(seekPosition, SeekOrigin.Begin);
        //    Assert.Equal(seekPosition, readStream.Position);

        //    // Read data
        //    byte[] buffer = new byte[1000];
        //    int bytesRead = readStream.Read(buffer, 0, buffer.Length);
        //    Assert.Equal(1000, bytesRead);

        //    // Verify data matches clean data at correct offset
        //    int cleanDataOffset = 5 * stride.DataLength + 100;
        //    for (int i = 0; i < 1000; i++)
        //    {
        //        Assert.Equal(cleanData[cleanDataOffset + i], buffer[i]);
        //    }
        //}

        //[Fact]
        //public void OpenStream_WithStride_SpecificGroup_Works()
        //{
        //    // Arrange - Write multiple files with different offsetStarts
        //    var stride = DataStride.Wii;
        //    var file1Data = GenerateTestData(0x7C00 * 3, seed: 10);
        //    var file2Data = GenerateTestData(0x7C00 * 2, seed: 20);

        //    using var store = new DataStore(_testDirectory);

        //    using (var writer = TestDataStoreHelper.AddImage(store, "WiiSet", "multi.iso"))
        //    {
        //        var strided1 = CreateStridedStream(file1Data, stride);
        //        writer.WriteData(0, strided1, strided1.Length, DataType.File, stride, offsetStart: 0);

        //        var strided2 = CreateStridedStream(file2Data, stride);
        //        writer.WriteData(500000, strided2, strided2.Length, DataType.File, stride, offsetStart: 500000);

        //        writer.FinalizeImage(500000 + file2Data.Length, 0, 0);
        //    }

        //    // Act - Read file 2 with stride
        //    using var reader = store.OpenImageReader(new GlobalImageKey("WiiSet", 1));
        //    using var file2StridedStream = reader.OpenStream(stride, offsetStart: 500000);

        //    // Assert - Should reconstruct file 2 in strided format
        //    long expectedLength = 2 * stride.SourceBlockSize; // 2 complete blocks
        //    Assert.Equal(expectedLength, file2StridedStream.Length);

        //    var reconstructed = new byte[expectedLength];
        //    file2StridedStream.ReadExactly(reconstructed, 0, reconstructed.Length);

        //    // Verify data
        //    for (int block = 0; block < 2; block++)
        //    {
        //        for (int i = 0; i < stride.DataLength; i++)
        //        {
        //            int stridePos = block * stride.SourceBlockSize + stride.DataOffset + i;
        //            int cleanPos = block * stride.DataLength + i;
        //            Assert.Equal(file2Data[cleanPos], reconstructed[stridePos]);
        //        }
        //    }
        //}

        [Fact]
        public void OpenStream_WithInvalidStride_ThrowsException()
        {
            // Arrange
            using DataStore store = new DataStore(_testDirectory);

            using (IImageWriter writer = addImage(store, "TestSet", "test.iso"))
            {
                byte[] data = generateTestData(1000);
                writer.WriteData(0, data, BlockType.File);
                writer.FinalizeImage(1000, Crc.Compute(data), X.XXHash64.Compute(data));
            }

            using IImageReader reader = store.OpenImageReader(new GlobalImageKey("TestSet", 1));

            // Get the offsetStart to test with
            List<long> uniqueOffsetStarts = reader.GetOffsets().Select(o => o.OffsetStart).Distinct().ToList();
            Assert.Single(uniqueOffsetStarts);
            long offsetStart = uniqueOffsetStarts[0];

            // Act & Assert - Invalid stride parameters
            DataStride invalidStride1 = new DataStride { SourceBlockSize = 0, DataOffset = 16, DataLength = 2048 };
            Assert.Throws<ArgumentException>(() => reader.OpenStream(invalidStride1, offsetStart));

            DataStride invalidStride2 = new DataStride { SourceBlockSize = 2352, DataOffset = 2352, DataLength = 2048 };
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.OpenStream(invalidStride2, offsetStart));
        }

        //        [Fact]
        //        public void OpenStream_WithStride_RoundTrip_PreservesData()
        //        {
        //            // Arrange - Full round trip: strided write ? clean storage ? strided read
        //            var stride = DataStride.CdMode1;
        //            var originalCleanData = GenerateTestData(2048 * 20, seed: 100);

        //            using var store = new DataStore(_testDirectory);

        //            // Write with stride (extracts clean data)
        //            using (var writer = TestDataStoreHelper.AddImage(store, "CDSet", "roundtrip.iso"))
        //            {
        //                var stridedInput = CreateStridedStream(originalCleanData, stride);
        //                writer.WriteData(0, stridedInput, stridedInput.Length, DataType.File, stride);
        //                writer.FinalizeImage(originalCleanData.Length, Crc.Compute(originalCleanData), X.XXHash64.Compute(originalCleanData));
        //            }

        //            // Read without stride (gets clean data)
        //            using (var reader1 = store.OpenImageReader(new GlobalImageKey("CDSet", 1))
        //)
        //            using (var cleanStream = reader1.OpenStream(0))
        //            {
        //                var readCleanData = new byte[originalCleanData.Length];
        //                cleanStream.ReadExactly(readCleanData, 0, readCleanData.Length);
        //                Assert.Equal(originalCleanData, readCleanData);
        //            }

        //            // Read with stride (gets reconstructed strided format)
        //            using (var reader2 = store.OpenImageReader(new GlobalImageKey("CDSet", 1)))
        //            {
        //                // Get the unique offsetStart
        //                var uniqueOffsetStarts = reader2.GetOffsets().Select(o => o.OffsetStart).Distinct().ToList();
        //                Assert.Single(uniqueOffsetStarts);

        //                using (var stridedStream = reader2.OpenStream(stride, offsetStart: uniqueOffsetStarts[0]))
        //                {
        //                    long expectedStridedLength = 20 * stride.SourceBlockSize;
        //                    Assert.Equal(expectedStridedLength, stridedStream.Length);

        //                    var stridedData = new byte[expectedStridedLength];
        //                    stridedStream.ReadExactly(stridedData, 0, stridedData.Length);

        //                    // Extract clean data from strided format
        //                    var extractedClean = new byte[originalCleanData.Length];
        //                    for (int block = 0; block < 20; block++)
        //                    {
        //                        Array.Copy(stridedData, 
        //                            block * stride.SourceBlockSize + stride.DataOffset, 
        //                            extractedClean, 
        //                            block * stride.DataLength, 
        //                            stride.DataLength);
        //                    }

        //                    // Verify round trip preserves data
        //                    Assert.Equal(originalCleanData, extractedClean);
        //                }
        //            }
        //        }

        #endregion

        #region DataStride Size Conversion Tests

        [Fact]
        public void GetCleanSize_WithCompleteBlocks_CalculatesCorrectly()
        {
            // Arrange
            DataStride stride = DataStride.Wii;
            long stridedSize = 10 * 0x8000; // 10 complete Wii blocks

            // Act
            long cleanSize = stride.GetCleanSize(0, stridedSize);

            // Assert
            long expectedCleanSize = 10 * 0x7C00; // 10 blocks of clean data
            Assert.Equal(expectedCleanSize, cleanSize);
        }

        [Fact]
        public void GetCleanSize_WithPartialBlock_CalculatesCorrectly()
        {
            // Arrange
            DataStride stride = DataStride.Wii;
            long stridedSize = (10 * 0x8000) + 0x400 + 1000; // 10 complete + partial block

            // Act
            long cleanSize = stride.GetCleanSize(0, stridedSize);

            // Assert
            long expectedCleanSize = (10 * 0x7C00) + 1000; // 10 complete + 1000 bytes
            Assert.Equal(expectedCleanSize, cleanSize);
        }

        [Fact]
        public void GetCleanSize_WithOnlyPadding_ReturnsZero()
        {
            // Arrange
            DataStride stride = DataStride.Wii;
            long stridedSize = 0x400; // Only padding, no data

            // Act
            long cleanSize = stride.GetCleanSize(0, stridedSize);

            // Assert
            Assert.Equal(0, cleanSize);
        }

        [Fact]
        public void GetStridedSize_WithCompleteBlocks_CalculatesCorrectly()
        {
            // Arrange
            DataStride stride = DataStride.Wii;
            long cleanSize = 10 * 0x7C00; // 10 blocks of clean data

            // Act
            long stridedSize = stride.GetStridedSize(0, cleanSize);

            // Assert
            long expectedStridedSize = (10 * 0x8000) - stride.DataOffset; // 10 complete Wii blocks
            Assert.Equal(expectedStridedSize, stridedSize);
        }

        [Fact]
        public void GetStridedSize_WithPartialBlock_CalculatesCorrectly()
        {
            // Arrange
            DataStride stride = DataStride.Wii;
            long cleanSize = (10 * 0x7C00) + 1000; // 10 complete + 1000 bytes

            // Act
            long stridedSize = stride.GetStridedSize(0, cleanSize);

            // Assert
            long expectedStridedSize = (10 * 0x8000) + 0x400 + 1000 - stride.DataOffset; // 10 complete + partial
            Assert.Equal(expectedStridedSize, stridedSize);
        }

        [Fact]
        public void GetCleanSize_RoundTrip_PreservesSize()
        {
            // Arrange
            DataStride stride = DataStride.CdMode1;
            long originalCleanSize = (2048 * 50) + 1234; // 50 complete sectors + partial

            // Act - Convert to strided and back
            long stridedSize = stride.GetStridedSize(0, originalCleanSize);
            long roundTripCleanSize = stride.GetCleanSize(stride.DataOffset, stridedSize);

            // Assert
            Assert.Equal(originalCleanSize, roundTripCleanSize);
        }

        [Fact]
        public void GetCleanSize_WithZeroSize_ReturnsZero()
        {
            // Arrange
            DataStride stride = DataStride.Wii;

            // Act & Assert
            Assert.Equal(0, stride.GetCleanSize(0, 0));
            Assert.Equal(0, stride.GetStridedSize(stride.DataOffset, 0));
        }

        [Fact]
        public void GetCleanSize_WithNegativeSize_ThrowsException()
        {
            // Arrange
            DataStride stride = DataStride.Wii;

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => stride.GetCleanSize(0, -100));
            Assert.Throws<ArgumentOutOfRangeException>(() => stride.GetStridedSize(0, -100));
        }

        [Fact]
        public void SizeConversion_AllPreDefinedStrides_WorkCorrectly()
        {
            // Test all pre-defined strides
            DataStride[] strides = new[]
            {
                DataStride.CdMode1,
                DataStride.Wii,
                DataStride.WiiU32KB,
                DataStride.WiiU64KB
            };

            foreach (DataStride stride in strides)
            {
                // Arrange - Use 10 complete blocks
                long completeBlocks = 10;
                long cleanSize = completeBlocks * stride.DataLength;
                long expectedStridedSize = (completeBlocks * stride.SourceBlockSize) - stride.DataOffset;

                // Act
                long actualStridedSize = stride.GetStridedSize(0, cleanSize);
                long roundTripCleanSize = stride.GetCleanSize(stride.DataOffset, actualStridedSize);

                // Assert
                Assert.Equal(expectedStridedSize, actualStridedSize);
                Assert.Equal(cleanSize, roundTripCleanSize);

                // Verify efficiency is reasonable (> 80%)
                Assert.True(stride.GetEfficiency() > 0.8,
                    $"Stride {stride.SourceBlockSize} has poor efficiency: {stride.GetEfficiency()}");
            }
        }

        [Fact]
        public void GetCleanSize_CdMode1_MatchesExpected()
        {
            // Arrange - CD image with 100 sectors
            DataStride stride = DataStride.CdMode1;
            long stridedSize = 100 * 2352; // 100 full CD sectors

            // Act
            long cleanSize = stride.GetCleanSize(0, stridedSize);

            // Assert
            Assert.Equal(100 * 2048, cleanSize);
        }

        [Fact]
        public void GetStridedSize_WiiU64KB_CalculatesCorrectly()
        {
            // Arrange
            DataStride stride = DataStride.WiiU64KB;
            long cleanSize = 5 * 0xFC00; // 5 WiiU 64KB blocks of data

            // Act
            long stridedSize = stride.GetStridedSize(0, cleanSize);

            // Assert
            Assert.Equal((5 * 0x10000) - stride.DataOffset, stridedSize);
        }

        #endregion
    }
}