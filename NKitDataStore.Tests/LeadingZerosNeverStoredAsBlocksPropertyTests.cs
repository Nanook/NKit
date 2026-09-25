using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for Property 7: Leading zeros are never stored as blocks.
    ///
    /// Feature: audio-partition-dedup
    /// Property 7: Leading zeros are never stored as blocks
    /// **Validates: Requirements 2.5**
    ///
    /// For any audio partition with leading zeros exceeding BlockSize, the computed
    /// TrimOffset is positive and all data before TrimOffset consists of zeros,
    /// meaning no meaningful blocks would be stored for that region.
    /// </summary>
    public class LeadingZerosNeverStoredAsBlocksPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 2.5**
        ///
        /// Property 7: Leading zeros are never stored as blocks — FindFirstNonZero returns correct position.
        ///
        /// For any byte array with leading zeros of length L > BlockSize followed by non-zero data,
        /// FindFirstNonZero returns L.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool FindFirstNonZero_ReturnsLeadingZeroLength_WhenLeadingZerosExceedBlockSize(
            PositiveInt exponentSeed,
            PositiveInt extraBlocksSeed,
            PositiveInt trailingBytesSeed,
            byte nonZeroByte)
        {
            if (nonZeroByte == 0)
                nonZeroByte = 1; // ensure non-zero

            // Generate a power-of-two block size between 2^10 (1024) and 2^16 (65536)
            int exponent = (exponentSeed.Get % 7) + 10;
            int blockSize = 1 << exponent;

            // Leading zeros length: at least blockSize + 1, up to blockSize * 5
            int extraBytes = (extraBlocksSeed.Get % (blockSize * 4)) + 1;
            int leadingZeroLength = blockSize + extraBytes;

            // Trailing non-zero data: 1 to blockSize bytes
            int trailingLength = (trailingBytesSeed.Get % blockSize) + 1;

            int totalLength = leadingZeroLength + trailingLength;
            byte[] data = new byte[totalLength];

            // Place non-zero byte at position leadingZeroLength
            data[leadingZeroLength] = nonZeroByte;

            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, totalLength);

            return result == leadingZeroLength;
        }

        /// <summary>
        /// **Validates: Requirements 2.5**
        ///
        /// Property 7: Leading zeros are never stored as blocks — TrimOffset is positive when leading zeros exceed BlockSize.
        ///
        /// For any byte array with leading zeros of length L > BlockSize,
        /// ComputeAlignedTrimOffset(L, BlockSize) returns a value T where T > 0.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_IsPositive_WhenLeadingZerosExceedBlockSize(
            PositiveInt exponentSeed,
            PositiveInt extraBytesSeed)
        {
            // Generate a power-of-two block size between 2^10 (1024) and 2^16 (65536)
            int exponent = (exponentSeed.Get % 7) + 10;
            int blockSize = 1 << exponent;

            // Leading zero length strictly greater than blockSize
            long leadingZeroLength = blockSize + (long)(extraBytesSeed.Get % (blockSize * 4)) + 1;

            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(leadingZeroLength, blockSize);

            return trimOffset > 0;
        }

        /// <summary>
        /// **Validates: Requirements 2.5**
        ///
        /// Property 7: Leading zeros are never stored as blocks — TrimOffset never exceeds first non-zero position.
        ///
        /// For any byte array with leading zeros of length L > BlockSize,
        /// ComputeAlignedTrimOffset(L, BlockSize) returns T where T &lt;= L.
        /// This ensures the trim boundary never cuts into non-zero data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_NeverExceedsFirstNonZeroPosition(
            PositiveInt exponentSeed,
            PositiveInt extraBytesSeed)
        {
            // Generate a power-of-two block size between 2^10 (1024) and 2^16 (65536)
            int exponent = (exponentSeed.Get % 7) + 10;
            int blockSize = 1 << exponent;

            // Leading zero length strictly greater than blockSize
            long leadingZeroLength = blockSize + (long)(extraBytesSeed.Get % (blockSize * 4)) + 1;

            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(leadingZeroLength, blockSize);

            return trimOffset <= leadingZeroLength;
        }

        /// <summary>
        /// **Validates: Requirements 2.5**
        ///
        /// Property 7: Leading zeros are never stored as blocks — All bytes before TrimOffset are zeros.
        ///
        /// For any audio partition with leading zeros exceeding BlockSize followed by non-zero data,
        /// all bytes in the region [0, TrimOffset) are zero-valued. This confirms that no
        /// meaningful block data exists before the trim boundary.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllBytesBeforeTrimOffset_AreZeros(
            PositiveInt exponentSeed,
            PositiveInt extraBlocksSeed,
            PositiveInt trailingBytesSeed,
            byte nonZeroByte)
        {
            if (nonZeroByte == 0)
                nonZeroByte = 1; // ensure non-zero

            // Generate a power-of-two block size between 2^10 (1024) and 2^14 (16384)
            // Use smaller sizes to keep memory reasonable
            int exponent = (exponentSeed.Get % 5) + 10;
            int blockSize = 1 << exponent;

            // Leading zeros length: at least blockSize + 1, up to blockSize * 3
            int extraBytes = (extraBlocksSeed.Get % (blockSize * 2)) + 1;
            int leadingZeroLength = blockSize + extraBytes;

            // Trailing non-zero data: 1 to blockSize bytes
            int trailingLength = (trailingBytesSeed.Get % blockSize) + 1;

            int totalLength = leadingZeroLength + trailingLength;
            byte[] data = new byte[totalLength];

            // Place non-zero byte at position leadingZeroLength
            data[leadingZeroLength] = nonZeroByte;

            // Compute the trim offset
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, totalLength);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Verify all bytes before trimOffset are zero
            for (long i = 0; i < trimOffset; i++)
            {
                if (data[i] != 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.5**
        ///
        /// Property 7: Leading zeros are never stored as blocks — No block boundary before TrimOffset contains non-zero data.
        ///
        /// For any audio partition with leading zeros exceeding BlockSize, verify that
        /// every BlockSize-aligned chunk before TrimOffset is entirely zero-valued.
        /// This directly validates that no block would be stored for positions before
        /// partitionBase + TrimOffset.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NoBlockBeforeTrimOffset_ContainsNonZeroData(
            PositiveInt exponentSeed,
            PositiveInt extraBlocksSeed,
            PositiveInt trailingBytesSeed,
            byte nonZeroByte)
        {
            if (nonZeroByte == 0)
                nonZeroByte = 1; // ensure non-zero

            // Generate a power-of-two block size between 2^10 (1024) and 2^14 (16384)
            int exponent = (exponentSeed.Get % 5) + 10;
            int blockSize = 1 << exponent;

            // Leading zeros length: at least blockSize + 1, up to blockSize * 3
            int extraBytes = (extraBlocksSeed.Get % (blockSize * 2)) + 1;
            int leadingZeroLength = blockSize + extraBytes;

            // Trailing non-zero data: 1 to blockSize bytes
            int trailingLength = (trailingBytesSeed.Get % blockSize) + 1;

            int totalLength = leadingZeroLength + trailingLength;
            byte[] data = new byte[totalLength];

            // Place non-zero byte at position leadingZeroLength
            data[leadingZeroLength] = nonZeroByte;

            // Compute the trim offset
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, totalLength);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Verify each block-aligned chunk before trimOffset is all zeros
            int numberOfBlocksBeforeTrim = (int)(trimOffset / blockSize);
            for (int blockIndex = 0; blockIndex < numberOfBlocksBeforeTrim; blockIndex++)
            {
                int blockStart = blockIndex * blockSize;
                for (int offset = 0; offset < blockSize; offset++)
                {
                    if (data[blockStart + offset] != 0)
                        return false;
                }
            }

            return true;
        }
    }
}