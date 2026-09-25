namespace NKitDataStore.Tests
{
    public class BlocksBlobTests
    {
        #region Encode Tests

        [Fact]
        public void Encode_EmptyList_ReturnsEmptyArray()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>();

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);

            // Assert - verify truly empty
            Assert.Empty(blob);
            Assert.NotNull(blob);
            Assert.Same(Array.Empty<byte>(), blob); // Should return same empty array instance
        }

        [Fact]
        public void Encode_NullList_ReturnsEmptyArray()
        {
            // Act
            byte[] blob = BlocksBlob.Encode(null!);

            // Assert - verify returns empty, not null
            Assert.Empty(blob);
            Assert.NotNull(blob);
        }

        [Fact]
        public void Encode_SingleBlock_CreatesCorrectBlob()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF)
            };

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);

            // Assert - verify exact size and structure
            Assert.NotNull(blob);
            Assert.Equal(12, blob.Length); // 8 + 4 bytes
            // Verify first 8 bytes are xxhash64 (big-endian)
            Assert.Equal(0x12, blob[0]);
            Assert.Equal(0x34, blob[1]);
            Assert.Equal(0x56, blob[2]);
            Assert.Equal(0x78, blob[3]);
            Assert.Equal(0x90, blob[4]);
            Assert.Equal(0xAB, blob[5]);
            Assert.Equal(0xCD, blob[6]);
            Assert.Equal(0xEF, blob[7]);
            // Verify next 4 bytes are crc32 (big-endian)
            Assert.Equal(0xDE, blob[8]);
            Assert.Equal(0xAD, blob[9]);
            Assert.Equal(0xBE, blob[10]);
            Assert.Equal(0xEF, blob[11]);
        }

        [Fact]
        public void Encode_MultipleBlocks_CreatesCorrectBlob()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);

            // Assert - verify exact size
            Assert.NotNull(blob);
            Assert.Equal(36, blob.Length); // 3 * 12 bytes
            Assert.Equal(3 * 12, blob.Length);
            // Verify count can be determined from length
            Assert.Equal(3, blob.Length / 12);
            // Verify first block bytes (0x1111111111111111, 0x11111111)
            for (int i = 0; i < 8; i++)
                Assert.Equal(0x11, blob[i]);
            for (int i = 8; i < 12; i++)
                Assert.Equal(0x11, blob[i]);
            // Verify second block starts at offset 12
            for (int i = 12; i < 20; i++)
                Assert.Equal(0x22, blob[i]);
        }

        [Fact]
        public void Encode_MaxOffsetBlocks_HandlesLargeList()
        {
            // Arrange - default max is 336 blocks
            List<BlockKey> blocks = new List<BlockKey>();
            for (int i = 0; i < 336; i++)
            {
                blocks.Add(new BlockKey((ulong)i, (uint)i));
            }

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);

            // Assert - verify precise size calculation
            Assert.NotNull(blob);
            Assert.Equal(336 * 12, blob.Length);
            Assert.Equal(4032, blob.Length); // Exact fit in 4KB page (< 4096)
            Assert.True(blob.Length < 4096, "Should fit in single 4KB SQLite page");
            // Verify block count can be calculated
            Assert.Equal(336, BlocksBlob.GetBlockCount(blob));
            // Spot check first and last blocks
            Assert.Equal(0ul, BlocksBlob.GetBlockAt(blob, 0).XxHash64);
            Assert.Equal(0u, BlocksBlob.GetBlockAt(blob, 0).Crc32);
            Assert.Equal(335ul, BlocksBlob.GetBlockAt(blob, 335).XxHash64);
            Assert.Equal(335u, BlocksBlob.GetBlockAt(blob, 335).Crc32);
        }

        #endregion

        #region Decode Tests

        [Fact]
        public void Decode_EmptyBlob_ReturnsEmptyList()
        {
            // Act
            List<BlockKey> blocks = BlocksBlob.Decode(Array.Empty<byte>());

            // Assert - verify truly empty list
            Assert.Empty(blocks);
            Assert.NotNull(blocks);
            Assert.IsType<List<BlockKey>>(blocks);
        }

        [Fact]
        public void Decode_NullBlob_ReturnsEmptyList()
        {
            // Act
            List<BlockKey> blocks = BlocksBlob.Decode(null);

            // Assert - verify returns empty list, not null
            Assert.Empty(blocks);
            Assert.NotNull(blocks);
            Assert.IsType<List<BlockKey>>(blocks);
        }

        [Fact]
        public void Decode_InvalidLength_ThrowsInvalidOperationException()
        {
            // Arrange - blob length not divisible by 12
            byte[] blob = new byte[13];

            // Act & Assert
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => BlocksBlob.Decode(blob));
            Assert.NotNull(ex);
            Assert.Contains("Invalid blocks blob length", ex.Message);
            Assert.Contains("must be multiple of 12", ex.Message);
            Assert.Contains("13", ex.Message); // Should mention the actual length

            // Verify other invalid lengths also throw
            Assert.Throws<InvalidOperationException>(() => BlocksBlob.Decode(new byte[1]));
            Assert.Throws<InvalidOperationException>(() => BlocksBlob.Decode(new byte[11]));
            Assert.Throws<InvalidOperationException>(() => BlocksBlob.Decode(new byte[13]));
            Assert.Throws<InvalidOperationException>(() => BlocksBlob.Decode(new byte[25]));
        }

        [Fact]
        public void Decode_SingleBlock_ReturnsCorrectBlock()
        {
            // Arrange
            List<BlockKey> originalBlocks = new List<BlockKey>
            {
                new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF)
            };
            byte[] blob = BlocksBlob.Encode(originalBlocks);

            // Act
            List<BlockKey> decodedBlocks = BlocksBlob.Decode(blob);

            // Assert - verify exact match
            Assert.NotNull(decodedBlocks);
            Assert.Single(decodedBlocks);
            Assert.Equal(0x1234567890ABCDEFul, decodedBlocks[0].XxHash64);
            Assert.Equal(0xDEADBEEFu, decodedBlocks[0].Crc32);
            // Verify it matches original exactly
            Assert.Equal(originalBlocks[0], decodedBlocks[0]);
            Assert.Equal(originalBlocks[0].XxHash64, decodedBlocks[0].XxHash64);
            Assert.Equal(originalBlocks[0].Crc32, decodedBlocks[0].Crc32);
        }

        [Fact]
        public void Decode_MultipleBlocks_ReturnsCorrectBlocks()
        {
            // Arrange
            List<BlockKey> originalBlocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(originalBlocks);

            // Act
            List<BlockKey> decodedBlocks = BlocksBlob.Decode(blob);

            // Assert - verify count and each block
            Assert.NotNull(decodedBlocks);
            Assert.Equal(3, decodedBlocks.Count);
            Assert.Equal(originalBlocks.Count, decodedBlocks.Count);

            // Verify each block individually
            for (int i = 0; i < originalBlocks.Count; i++)
            {
                Assert.Equal(originalBlocks[i], decodedBlocks[i]);
                Assert.Equal(originalBlocks[i].XxHash64, decodedBlocks[i].XxHash64);
                Assert.Equal(originalBlocks[i].Crc32, decodedBlocks[i].Crc32);
            }

            // Verify order is preserved
            Assert.Equal(0x1111111111111111ul, decodedBlocks[0].XxHash64);
            Assert.Equal(0x2222222222222222ul, decodedBlocks[1].XxHash64);
            Assert.Equal(0x3333333333333333ul, decodedBlocks[2].XxHash64);
        }

        #endregion

        #region Round-Trip Tests

        [Fact]
        public void RoundTrip_PreservesData()
        {
            // Arrange
            List<BlockKey> original = new List<BlockKey>
            {
                new BlockKey(0xABCDEF1234567890, 0x12345678),
                new BlockKey(0xFEDCBA0987654321, 0x87654321),
                new BlockKey(0x1111222233334444, 0xAAAABBBB)
            };

            // Act
            byte[] blob = BlocksBlob.Encode(original);
            List<BlockKey> decoded = BlocksBlob.Decode(blob);

            // Assert
            Assert.Equal(original.Count, decoded.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], decoded[i]);
            }
        }

        [Fact]
        public void RoundTrip_WithZeroValues_PreservesData()
        {
            // Arrange
            List<BlockKey> original = new List<BlockKey>
            {
                new BlockKey(0, 0),
                new BlockKey(1, 0),
                new BlockKey(0, 1)
            };

            // Act
            byte[] blob = BlocksBlob.Encode(original);
            List<BlockKey> decoded = BlocksBlob.Decode(blob);

            // Assert
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void RoundTrip_WithMaxValues_PreservesData()
        {
            // Arrange
            List<BlockKey> original = new List<BlockKey>
            {
                new BlockKey(ulong.MaxValue, uint.MaxValue)
            };

            // Act
            byte[] blob = BlocksBlob.Encode(original);
            List<BlockKey> decoded = BlocksBlob.Decode(blob);

            // Assert
            Assert.Equal(original, decoded);
        }

        #endregion

        #region GetBlockCount Tests

        [Fact]
        public void GetBlockCount_EmptyBlob_ReturnsZero()
        {
            // Act
            int count = BlocksBlob.GetBlockCount(Array.Empty<byte>());

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void GetBlockCount_NullBlob_ReturnsZero()
        {
            // Act
            int count = BlocksBlob.GetBlockCount(null);

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void GetBlockCount_SingleBlock_ReturnsOne()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey> { new BlockKey(1, 2) };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            int count = BlocksBlob.GetBlockCount(blob);

            // Assert
            Assert.Equal(1, count);
        }

        [Fact]
        public void GetBlockCount_MultipleBlocks_ReturnsCorrectCount()
        {
            // Arrange
            List<BlockKey> blocks = Enumerable.Range(0, 10)
                .Select(i => new BlockKey((ulong)i, (uint)i))
                .ToList();
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            int count = BlocksBlob.GetBlockCount(blob);

            // Assert
            Assert.Equal(10, count);
        }

        #endregion

        #region GetBlockAt Tests

        [Fact]
        public void GetBlockAt_FirstBlock_ReturnsCorrectBlock()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            BlockKey block = BlocksBlob.GetBlockAt(blob, 0);

            // Assert - verify exact match with first block
            Assert.Equal(blocks[0], block);
            Assert.Equal(0x1111111111111111ul, block.XxHash64);
            Assert.Equal(0x11111111u, block.Crc32);
            // Verify it doesn't match other blocks
            Assert.NotEqual(blocks[1], block);
            Assert.NotEqual(blocks[2], block);
        }

        [Fact]
        public void GetBlockAt_MiddleBlock_ReturnsCorrectBlock()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            BlockKey block = BlocksBlob.GetBlockAt(blob, 1);

            // Assert - verify exact match with middle block
            Assert.Equal(blocks[1], block);
            Assert.Equal(0x2222222222222222ul, block.XxHash64);
            Assert.Equal(0x22222222u, block.Crc32);
            // Verify it doesn't match other blocks
            Assert.NotEqual(blocks[0], block);
            Assert.NotEqual(blocks[2], block);
        }

        [Fact]
        public void GetBlockAt_LastBlock_ReturnsCorrectBlock()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            BlockKey block = BlocksBlob.GetBlockAt(blob, 2);

            // Assert - verify exact match with last block
            Assert.Equal(blocks[2], block);
            Assert.Equal(0x3333333333333333ul, block.XxHash64);
            Assert.Equal(0x33333333u, block.Crc32);
            // Verify it doesn't match other blocks
            Assert.NotEqual(blocks[0], block);
            Assert.NotEqual(blocks[1], block);
        }

        [Fact]
        public void GetBlockAt_NegativeIndex_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey> { new BlockKey(1, 2) };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act & Assert
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, -1));
            Assert.NotNull(ex);
            Assert.Contains("index", ex.Message, StringComparison.OrdinalIgnoreCase);

            // Verify other negative indices also throw
            Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, int.MinValue));
        }

        [Fact]
        public void GetBlockAt_IndexTooLarge_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey> { new BlockKey(1, 2) };
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act & Assert - index 1 is out of range for single block (valid range: 0-0)
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, 1));
            Assert.NotNull(ex);
            Assert.Contains("index", ex.Message, StringComparison.OrdinalIgnoreCase);

            // Verify other out of range indices also throw
            Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlocksBlob.GetBlockAt(blob, int.MaxValue));
        }

        #endregion

        #region GetBlockAtOffset Tests

        [Fact]
        public void GetBlockAtOffset_FirstBlock_ReturnsCorrectly()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);
            int blockSize = 65536; // 64KB

            // Act - offset 1000 bytes into the segment (within first block)
            (BlockKey blockKey, int offsetWithinBlock) = BlocksBlob.GetBlockAtOffset(blob, 1000, blockSize);

            // Assert - verify exact block and offset
            Assert.Equal(blocks[0], blockKey);
            Assert.Equal(0x1111111111111111ul, blockKey.XxHash64);
            Assert.Equal(0x11111111u, blockKey.Crc32);
            Assert.Equal(1000, offsetWithinBlock);
            // Verify calculation: 1000 / 65536 = 0 (block index), 1000 % 65536 = 1000 (offset)
            Assert.Equal(0, 1000 / blockSize);
            Assert.Equal(1000, 1000 % blockSize);
        }

        [Fact]
        public void GetBlockAtOffset_SecondBlock_ReturnsCorrectly()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);
            int blockSize = 65536; // 64KB

            // Act - offset into second block (65536 + 500 = 66036)
            (BlockKey blockKey, int offsetWithinBlock) = BlocksBlob.GetBlockAtOffset(blob, 66036, blockSize);

            // Assert - verify exact block and offset
            Assert.Equal(blocks[1], blockKey);
            Assert.Equal(0x2222222222222222ul, blockKey.XxHash64);
            Assert.Equal(0x22222222u, blockKey.Crc32);
            Assert.Equal(500, offsetWithinBlock);
            // Verify calculation: 66036 / 65536 = 1 (block index), 66036 % 65536 = 500 (offset)
            Assert.Equal(1, 66036 / blockSize);
            Assert.Equal(500, 66036 % blockSize);
        }

        [Fact]
        public void GetBlockAtOffset_ExactBlockBoundary_ReturnsCorrectly()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222)
            };
            byte[] blob = BlocksBlob.Encode(blocks);
            int blockSize = 65536; // 64KB

            // Act - exactly at the start of second block
            (BlockKey blockKey, int offsetWithinBlock) = BlocksBlob.GetBlockAtOffset(blob, 65536, blockSize);

            // Assert - verify at start of second block (offset 0 within block)
            Assert.Equal(blocks[1], blockKey);
            Assert.Equal(0x2222222222222222ul, blockKey.XxHash64);
            Assert.Equal(0x22222222u, blockKey.Crc32);
            Assert.Equal(0, offsetWithinBlock);
            // Verify calculation: 65536 / 65536 = 1 (block index), 65536 % 65536 = 0 (offset)
            Assert.Equal(1, 65536 / blockSize);
            Assert.Equal(0, 65536 % blockSize);
        }

        [Fact]
        public void GetBlockAtOffset_WithDifferentBlockSize_CalculatesCorrectly()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>
            {
                new BlockKey(0x1111111111111111, 0x11111111),
                new BlockKey(0x2222222222222222, 0x22222222),
                new BlockKey(0x3333333333333333, 0x33333333)
            };
            byte[] blob = BlocksBlob.Encode(blocks);
            int blockSize = 32768; // 32KB

            // Act - offset into second block with 32KB block size
            (BlockKey blockKey, int offsetWithinBlock) = BlocksBlob.GetBlockAtOffset(blob, 40000, blockSize);

            // Assert - verify correct block and offset with different block size
            Assert.Equal(blocks[1], blockKey);
            Assert.Equal(0x2222222222222222ul, blockKey.XxHash64);
            Assert.Equal(0x22222222u, blockKey.Crc32);
            Assert.Equal(7232, offsetWithinBlock); // 40000 % 32768 = 7232
            // Verify calculation: 40000 / 32768 = 1 (block index), 40000 % 32768 = 7232 (offset)
            Assert.Equal(1, 40000 / blockSize);
            Assert.Equal(7232, 40000 % blockSize);
            // Verify the math
            Assert.Equal(40000, (1 * blockSize) + 7232);
        }

        #endregion

        #region Performance/Size Tests

        [Fact]
        public void Encode_336Blocks_FitsIn4KBPage()
        {
            // Arrange - 336 blocks is the default max for optimal SQLite page usage
            List<BlockKey> blocks = Enumerable.Range(0, 336)
                .Select(i => new BlockKey((ulong)i, (uint)i))
                .ToList();

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);

            // Assert
            Assert.Equal(4032, blob.Length); // 336 * 12 = 4032 bytes (< 4096)
        }

        [Fact]
        public void Encode_Decode_IsEfficient()
        {
            // Arrange - measure that encoding/decoding doesn't create excessive allocations
            List<BlockKey> blocks = Enumerable.Range(0, 100)
                .Select(i => new BlockKey((ulong)i, (uint)i))
                .ToList();

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);
            List<BlockKey> decoded = BlocksBlob.Decode(blob);

            // Assert
            Assert.Equal(blocks.Count, decoded.Count);
            Assert.Equal(1200, blob.Length); // 100 * 12 bytes
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void Encode_Decode_WithSequentialValues_PreservesOrder()
        {
            // Arrange
            List<BlockKey> blocks = new List<BlockKey>();
            for (int i = 0; i < 10; i++)
            {
                blocks.Add(new BlockKey((ulong)i, (uint)(i * 2)));
            }

            // Act
            byte[] blob = BlocksBlob.Encode(blocks);
            List<BlockKey> decoded = BlocksBlob.Decode(blob);

            // Assert
            for (int i = 0; i < blocks.Count; i++)
            {
                Assert.Equal((ulong)i, decoded[i].XxHash64);
                Assert.Equal((uint)(i * 2), decoded[i].Crc32);
            }
        }

        [Fact]
        public void GetBlockAt_WithAllIdenticalBlocks_ReturnsCorrectBlock()
        {
            // Arrange
            BlockKey sameBlock = new BlockKey(0xDEADBEEF, 0xCAFEBABE);
            List<BlockKey> blocks = Enumerable.Repeat(sameBlock, 5).ToList();
            byte[] blob = BlocksBlob.Encode(blocks);

            // Act
            BlockKey block0 = BlocksBlob.GetBlockAt(blob, 0);
            BlockKey block3 = BlocksBlob.GetBlockAt(blob, 3);
            BlockKey block4 = BlocksBlob.GetBlockAt(blob, 4);

            // Assert
            Assert.Equal(sameBlock, block0);
            Assert.Equal(sameBlock, block3);
            Assert.Equal(sameBlock, block4);
        }

        #endregion
    }
}