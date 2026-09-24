namespace NKitDataStore.Tests
{
    public class ModelsTests
    {
        [Theory]
        [InlineData(ImageFormat.Unknown, ".iso")]
        [InlineData(ImageFormat.Iso, ".iso")]
        [InlineData(ImageFormat.Bin, ".bin")]
        [InlineData(ImageFormat.App, ".app")]
        [InlineData(ImageFormat.Cdn, ".cdn")]
        [InlineData(ImageFormat.Gdi, ".gdi")]
        public void ImageFormat_GetFileExtension_ReturnsExpectedExtension(ImageFormat format, string expectedExtension) => Assert.Equal(expectedExtension, format.GetFileExtension());

        #region BlockKey Tests

        [Fact]
        public void BlockKey_Constructor_SetsProperties()
        {
            // Act
            BlockKey key = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);

            // Assert
            Assert.Equal(0x1234567890ABCDEFul, key.XxHash64);
            Assert.Equal(0xDEADBEEFu, key.Crc32);
        }

        [Fact]
        public void BlockKey_Equality_WorksCorrectly()
        {
            // Arrange
            BlockKey key1 = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);
            BlockKey key2 = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);
            BlockKey key3 = new BlockKey(0x1234567890ABCDEF, 0xCAFEBABE);

            // Assert
            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
            Assert.True(key1 == key2);
            Assert.False(key1 == key3);
            Assert.False(key1 != key2);
            Assert.True(key1 != key3);
        }

        [Fact]
        public void BlockKey_HashCode_SameForEqualKeys()
        {
            // Arrange
            BlockKey key1 = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);
            BlockKey key2 = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);

            // Assert
            Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void BlockKey_CanBeUsedInDictionary()
        {
            // Arrange
            Dictionary<BlockKey, string> dict = new Dictionary<BlockKey, string>();
            BlockKey key1 = new BlockKey(0x1111111111111111, 0x11111111);
            BlockKey key2 = new BlockKey(0x2222222222222222, 0x22222222);

            // Act
            dict[key1] = "First";
            dict[key2] = "Second";

            // Assert
            Assert.Equal("First", dict[key1]);
            Assert.Equal("Second", dict[key2]);
            Assert.Equal(2, dict.Count);
        }

        [Fact]
        public void BlockKey_CanBeUsedInHashSet()
        {
            // Arrange
            HashSet<BlockKey> set = new HashSet<BlockKey>();
            BlockKey key1 = new BlockKey(0x1111111111111111, 0x11111111);
            BlockKey key2 = new BlockKey(0x1111111111111111, 0x11111111); // duplicate
            BlockKey key3 = new BlockKey(0x2222222222222222, 0x22222222);

            // Act
            set.Add(key1);
            set.Add(key2); // should not add duplicate
            set.Add(key3);

            // Assert
            Assert.Equal(2, set.Count);
            Assert.Contains(key1, set);
            Assert.Contains(key3, set);
        }

        [Fact]
        public void BlockKey_WithZeroValues_WorksCorrectly()
        {
            // Arrange & Act
            BlockKey key = new BlockKey(0, 0);

            // Assert
            Assert.Equal(0ul, key.XxHash64);
            Assert.Equal(0u, key.Crc32);
        }

        [Fact]
        public void BlockKey_WithMaxValues_WorksCorrectly()
        {
            // Arrange & Act
            BlockKey key = new BlockKey(ulong.MaxValue, uint.MaxValue);

            // Assert
            Assert.Equal(ulong.MaxValue, key.XxHash64);
            Assert.Equal(uint.MaxValue, key.Crc32);
        }

        [Fact]
        public void BlockKey_ToString_ReturnsReadableFormat()
        {
            // Arrange
            BlockKey key = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);

            // Act
            string result = key.ToString();

            // Assert
            Assert.NotEmpty(result);
            // Record struct should have automatic ToString implementation
        }

        #endregion

        #region GlobalImageKey Tests

        [Fact]
        public void GlobalImageKey_Constructor_SetsProperties()
        {
            // Act
            GlobalImageKey key = new GlobalImageKey("TestSet", 123);

            // Assert
            Assert.Equal("TestSet", key.SetName);
            Assert.Equal(123, key.ImageId);
        }

        [Fact]
        public void GlobalImageKey_Equality_WorksCorrectly()
        {
            // Arrange
            GlobalImageKey key1 = new GlobalImageKey("TestSet", 123);
            GlobalImageKey key2 = new GlobalImageKey("TestSet", 123);
            GlobalImageKey key3 = new GlobalImageKey("TestSet", 456);
            GlobalImageKey key4 = new GlobalImageKey("OtherSet", 123);

            // Assert
            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
            Assert.NotEqual(key1, key4);
            Assert.True(key1 == key2);
            Assert.False(key1 == key3);
        }

        [Fact]
        public void GlobalImageKey_HashCode_SameForEqualKeys()
        {
            // Arrange
            GlobalImageKey key1 = new GlobalImageKey("TestSet", 123);
            GlobalImageKey key2 = new GlobalImageKey("TestSet", 123);

            // Assert
            Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }

        [Fact]
        public void GlobalImageKey_CanBeUsedInDictionary()
        {
            // Arrange
            Dictionary<GlobalImageKey, string> dict = new Dictionary<GlobalImageKey, string>();
            GlobalImageKey key1 = new GlobalImageKey("Set1", 1);
            GlobalImageKey key2 = new GlobalImageKey("Set2", 2);

            // Act
            dict[key1] = "First Image";
            dict[key2] = "Second Image";

            // Assert
            Assert.Equal("First Image", dict[key1]);
            Assert.Equal("Second Image", dict[key2]);
        }

        [Fact]
        public void GlobalImageKey_WithEmptySetName_WorksCorrectly()
        {
            // Arrange & Act
            GlobalImageKey key = new GlobalImageKey("", 123);

            // Assert
            Assert.Equal("", key.SetName);
            Assert.Equal(123, key.ImageId);
        }

        [Fact]
        public void GlobalImageKey_CaseSensitiveSetName()
        {
            // Arrange
            GlobalImageKey key1 = new GlobalImageKey("TestSet", 1);
            GlobalImageKey key2 = new GlobalImageKey("testset", 1);

            // Assert
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void GlobalImageKey_ToString_ReturnsReadableFormat()
        {
            // Arrange
            GlobalImageKey key = new GlobalImageKey("TestSet", 123);

            // Act
            string result = key.ToString();

            // Assert
            Assert.Contains("TestSet", result);
            Assert.Contains("123", result);
        }

        #endregion

        #region DataStride Tests

        [Fact]
        public void DataStride_Properties_CanBeSet()
        {
            // Act
            DataStride stride = new DataStride
            {
                SourceBlockSize = 2352,
                DataOffset = 16,
                DataLength = 2048
            };

            // Assert
            Assert.Equal(2352, stride.SourceBlockSize);
            Assert.Equal(16, stride.DataOffset);
            Assert.Equal(2048, stride.DataLength);
        }

        [Fact]
        public void DataStride_CdSectorExample_WorksCorrectly()
        {
            // Arrange - CD-ROM sector: 2352 bytes with 16 byte header, 2048 data, rest ECC
            DataStride stride = new DataStride
            {
                SourceBlockSize = 2352,
                DataOffset = 16,
                DataLength = 2048
            };

            // Assert
            Assert.Equal(2352, stride.SourceBlockSize);
            Assert.Equal(16, stride.DataOffset);
            Assert.Equal(2048, stride.DataLength);
            // Verify the math: 16 + 2048 = 2064, leaving 288 bytes for ECC/EDC
            Assert.True(stride.DataOffset + stride.DataLength <= stride.SourceBlockSize);
        }

        [Fact]
        public void DataStride_Equality_WorksCorrectly()
        {
            // Arrange
            DataStride stride1 = new DataStride { SourceBlockSize = 2352, DataOffset = 16, DataLength = 2048 };
            DataStride stride2 = new DataStride { SourceBlockSize = 2352, DataOffset = 16, DataLength = 2048 };
            DataStride stride3 = new DataStride { SourceBlockSize = 2352, DataOffset = 24, DataLength = 2048 };

            // Assert
            Assert.Equal(stride1, stride2);
            Assert.NotEqual(stride1, stride3);
        }

        [Fact]
        public void DataStride_WithZeroValues_WorksCorrectly()
        {
            // Arrange & Act
            DataStride stride = new DataStride
            {
                SourceBlockSize = 0,
                DataOffset = 0,
                DataLength = 0
            };

            // Assert
            Assert.Equal(0, stride.SourceBlockSize);
            Assert.Equal(0, stride.DataOffset);
            Assert.Equal(0, stride.DataLength);
        }

        [Fact]
        public void DataStride_InitSyntax_WorksCorrectly()
        {
            // Act
            DataStride stride = new DataStride
            {
                SourceBlockSize = 4096,
                DataOffset = 128,
                DataLength = 3968
            };

            // Assert
            Assert.Equal(4096, stride.SourceBlockSize);
            Assert.Equal(128, stride.DataOffset);
            Assert.Equal(3968, stride.DataLength);
        }

        #endregion

        #region BlockStorageInfo Tests

        [Fact]
        public void BlockStorageInfo_Properties_CanBeSet()
        {
            // Act
            BlockStorageInfo info = new BlockStorageInfo
            {
                BlockCount = 100,
                TotalRawSize = 1000000,
                TotalCompressedSize = 500000
            };

            // Assert
            Assert.Equal(100, info.BlockCount);
            Assert.Equal(1000000, info.TotalRawSize);
            Assert.Equal(500000, info.TotalCompressedSize);
        }

        [Fact]
        public void BlockStorageInfo_CompressionRatio_CalculatesCorrectly()
        {
            // Arrange
            BlockStorageInfo info = new BlockStorageInfo
            {
                TotalRawSize = 1000000,
                TotalCompressedSize = 500000
            };

            // Act
            double ratio = info.CompressionRatio;

            // Assert
            Assert.Equal(0.5, ratio);
        }

        [Fact]
        public void BlockStorageInfo_CompressionRatio_WithZeroRaw_ReturnsZero()
        {
            // Arrange
            BlockStorageInfo info = new BlockStorageInfo
            {
                TotalRawSize = 0,
                TotalCompressedSize = 500000
            };

            // Act
            double ratio = info.CompressionRatio;

            // Assert
            Assert.Equal(0.0, ratio);
        }

        [Fact]
        public void BlockStorageInfo_CompressionRatio_NoCompression_ReturnsOne()
        {
            // Arrange
            BlockStorageInfo info = new BlockStorageInfo
            {
                TotalRawSize = 1000000,
                TotalCompressedSize = 1000000
            };

            // Act
            double ratio = info.CompressionRatio;

            // Assert
            Assert.Equal(1.0, ratio);
        }

        [Fact]
        public void BlockStorageInfo_CompressionRatio_GoodCompression_ReturnsLowRatio()
        {
            // Arrange
            BlockStorageInfo info = new BlockStorageInfo
            {
                TotalRawSize = 1000000,
                TotalCompressedSize = 200000
            };

            // Act
            double ratio = info.CompressionRatio;

            // Assert
            Assert.Equal(0.2, ratio); // 20% of original size
        }

        #endregion

        #region SetInfo Tests

        [Fact]
        public void SetInfo_DefaultValues_AreCorrect()
        {
            // Act
            SetInfo info = new SetInfo();

            // Assert
            Assert.Equal(string.Empty, info.SetName);
            Assert.NotNull(info.FilePaths);
            Assert.Empty(info.FilePaths);
            Assert.NotNull(info.BlockStorage);
        }

        [Fact]
        public void SetInfo_Properties_CanBeSet()
        {
            // Act
            SetInfo info = new SetInfo
            {
                SetName = "TestSet",
                FilePaths = new List<string> { "test1.db", "test2.db" },
                ShardSize = 20 * 1024, // 20 KiB
                ImageCount = 10,
                TotalSize = 1000000
            };

            // Assert
            Assert.Equal("TestSet", info.SetName);
            Assert.Equal(2, info.FilePaths.Count);
            Assert.Equal(20 * 1024, info.ShardSize); // Should match the 20 KiB we set
            Assert.Equal(10, info.ImageCount);
            Assert.Equal(1000000, info.TotalSize);
        }

        #endregion

        #region OffsetRecord Tests

        [Fact]
        public void OffsetRecord_HasBlocks_ReturnsTrueWhenBlocksExist()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey> { new BlockKey(1, 2) }
            };

            // Assert
            Assert.True(offset.HasBlocks);
        }

        [Fact]
        public void OffsetRecord_HasBlocks_ReturnsFalseWhenEmpty()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey>()
            };

            // Assert
            Assert.False(offset.HasBlocks);
        }

        [Fact]
        public void OffsetRecord_HasBlocks_ReturnsFalseWhenNull()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = null
            };

            // Assert
            Assert.False(offset.HasBlocks);
        }

        [Fact]
        public void OffsetRecord_FirstBlock_ReturnsFirstBlock()
        {
            // Arrange
            BlockKey key1 = new BlockKey(1, 2);
            BlockKey key2 = new BlockKey(3, 4);
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey> { key1, key2 }
            };

            // Assert
            Assert.Equal(key1, offset.FirstBlock);
        }

        [Fact]
        public void OffsetRecord_FirstBlock_ReturnsNullWhenEmpty()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey>()
            };

            // Assert
            Assert.Null(offset.FirstBlock);
        }

        [Fact]
        public void OffsetRecord_BlockCount_ReturnsCorrectCount()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey>
                {
                    new BlockKey(1, 2),
                    new BlockKey(3, 4),
                    new BlockKey(5, 6)
                }
            };

            // Assert
            Assert.Equal(3, offset.BlockCount);
        }

        [Fact]
        public void OffsetRecord_BlockCount_ReturnsZeroWhenNull()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord { Blocks = null };

            // Assert
            Assert.Equal(0, offset.BlockCount);
        }

        [Fact]
        public void OffsetRecord_GetBlockAt_ReturnsCorrectBlock()
        {
            // Arrange
            BlockKey key0 = new BlockKey(1, 2);
            BlockKey key1 = new BlockKey(3, 4);
            BlockKey key2 = new BlockKey(5, 6);
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey> { key0, key1, key2 }
            };

            // Assert
            Assert.Equal(key0, offset.GetBlockAt(0));
            Assert.Equal(key1, offset.GetBlockAt(1));
            Assert.Equal(key2, offset.GetBlockAt(2));
        }

        [Fact]
        public void OffsetRecord_GetBlockAt_ThrowsOnInvalidIndex()
        {
            // Arrange
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey> { new BlockKey(1, 2) }
            };

            // Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => offset.GetBlockAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => offset.GetBlockAt(1));
        }

        [Fact]
        public void OffsetRecord_GetBlockAtOffset_ReturnsCorrectBlockAndOffset()
        {
            // Arrange
            BlockKey key0 = new BlockKey(1, 2);
            BlockKey key1 = new BlockKey(3, 4);
            OffsetRecord offset = new OffsetRecord
            {
                Blocks = new List<BlockKey> { key0, key1 }
            };

            // Act - 70000 bytes = block 1 (65536 bytes) + 4464 bytes
            (BlockKey blockKey, int offsetWithinBlock) = offset.GetBlockAtOffset(70000, 65536);

            // Assert
            Assert.Equal(key1, blockKey);
            Assert.Equal(4464, offsetWithinBlock);
        }

        #endregion
    }
}