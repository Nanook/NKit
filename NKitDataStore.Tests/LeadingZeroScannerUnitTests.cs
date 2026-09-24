using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for <see cref="LeadingZeroScanner"/>.
    /// Validates Requirements 1.1, 1.2, 1.3, 5.1, 5.2, 5.3, 5.4.
    /// </summary>
    public class LeadingZeroScannerUnitTests
    {
        private const int BlockSize = 0x10000; // 65536 bytes

        // ── FindFirstNonZero Tests ───────────────────────────────────────

        /// <summary>
        /// Validates Requirement 1.3: All-zero buffer returns dataLength.
        /// </summary>
        [Fact]
        public void FindFirstNonZero_AllZeroBuffer_ReturnsDataLength()
        {
            // Arrange
            byte[] data = new byte[BlockSize * 3]; // all zeros

            // Act
            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, data.Length);

            // Assert
            Assert.Equal(data.Length, result);
        }

        /// <summary>
        /// Validates Requirement 1.1: Non-zero at position 0 returns 0.
        /// </summary>
        [Fact]
        public void FindFirstNonZero_NonZeroAtPositionZero_ReturnsZero()
        {
            // Arrange
            byte[] data = new byte[BlockSize];
            data[0] = 0xFF;

            // Act
            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, data.Length);

            // Assert
            Assert.Equal(0, result);
        }

        /// <summary>
        /// Validates Requirement 1.1: Non-zero at exact BlockSize boundary.
        /// </summary>
        [Fact]
        public void FindFirstNonZero_NonZeroAtBlockSizeBoundary_ReturnsBoundaryPosition()
        {
            // Arrange
            byte[] data = new byte[BlockSize * 3];
            data[BlockSize] = 0x01; // first non-zero at exactly BlockSize

            // Act
            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, data.Length);

            // Assert
            Assert.Equal(BlockSize, result);
        }

        /// <summary>
        /// Validates Requirement 1.1: Non-zero at arbitrary mid-buffer position.
        /// </summary>
        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(32768)]
        [InlineData(65537)]
        public void FindFirstNonZero_NonZeroAtMidBufferPosition_ReturnsThatPosition(int position)
        {
            // Arrange
            byte[] data = new byte[BlockSize * 3];
            data[position] = 0xAB;

            // Act
            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, data.Length);

            // Assert
            Assert.Equal(position, result);
        }

        /// <summary>
        /// Validates Requirement 1.1: FindFirstNonZero respects dataOffset parameter.
        /// </summary>
        [Fact]
        public void FindFirstNonZero_WithDataOffset_ReturnsPositionRelativeToOffset()
        {
            // Arrange
            byte[] data = new byte[BlockSize * 2];
            data[0] = 0xFF; // non-zero before the offset region
            data[BlockSize + 50] = 0x01; // non-zero within the scanned region

            // Act — scan starting at offset BlockSize
            long result = LeadingZeroScanner.FindFirstNonZero(data, BlockSize, BlockSize);

            // Assert — position 50 relative to dataOffset
            Assert.Equal(50, result);
        }

        // ── ComputeAlignedTrimOffset Tests ───────────────────────────────

        /// <summary>
        /// Validates Requirement 5.2: Position less than BlockSize returns 0.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(500)]
        [InlineData(1200)]
        [InlineData(65535)]
        public void ComputeAlignedTrimOffset_PositionLessThanBlockSize_ReturnsZero(long position)
        {
            // Act
            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, BlockSize);

            // Assert
            Assert.Equal(0, result);
        }

        /// <summary>
        /// Validates Requirement 5.3: Position exactly on boundary returns that position.
        /// </summary>
        [Theory]
        [InlineData(65536)]
        [InlineData(131072)]
        [InlineData(196608)]
        public void ComputeAlignedTrimOffset_PositionExactlyOnBoundary_ReturnsThatPosition(long position)
        {
            // Act
            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, BlockSize);

            // Assert
            Assert.Equal(position, result);
        }

        /// <summary>
        /// Validates Requirement 5.1: Position between boundaries rounds down.
        /// </summary>
        [Theory]
        [InlineData(66000, 65536)]       // 66000 rounds down to 65536
        [InlineData(66500, 65536)]       // 66500 rounds down to 65536
        [InlineData(131073, 131072)]     // just past 2nd boundary rounds down to 131072
        [InlineData(196607, 131072)]     // just before 3rd boundary rounds down to 131072
        public void ComputeAlignedTrimOffset_PositionBetweenBoundaries_RoundsDown(long position, long expected)
        {
            // Act
            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, BlockSize);

            // Assert
            Assert.Equal(expected, result);
        }

        /// <summary>
        /// Validates Requirement 5.4: All-zeros partition (firstNonZero == dataLength)
        /// where dataLength < BlockSize returns 0.
        /// </summary>
        [Fact]
        public void ComputeAlignedTrimOffset_AllZerosSmallPartition_ReturnsZero()
        {
            // A partition smaller than BlockSize that is all zeros
            long firstNonZero = 1000; // partition size, all zeros
            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, BlockSize);

            Assert.Equal(0, result);
        }
    }
}