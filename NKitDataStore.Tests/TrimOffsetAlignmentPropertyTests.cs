using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for TrimOffset BlockSize alignment.
    ///
    /// Feature: audio-partition-dedup
    /// Property 2: TrimOffset is always BlockSize-aligned
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    ///
    /// For any first-non-zero byte position P ≥ 0 and any valid BlockSize B > 0
    /// (power of two), the computed TrimOffset SHALL equal floor(P / B) * B.
    /// </summary>
    public class TrimOffsetAlignmentPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
        ///
        /// Property 2: TrimOffset is always BlockSize-aligned — General alignment.
        ///
        /// For any first-non-zero byte position P ≥ 0 and any valid BlockSize B > 0
        /// (power of two), ComputeAlignedTrimOffset(P, B) equals floor(P / B) * B.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_AlwaysEquals_FloorDivision(NonNegativeInt positionSeed, PositiveInt exponentSeed)
        {
            long position = (long)positionSeed.Get;
            // Generate a power-of-two block size between 2^1 and 2^20
            int exponent = (exponentSeed.Get % 20) + 1;
            int blockSize = 1 << exponent;

            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, blockSize);
            long expected = position / blockSize * blockSize;

            return result == expected;
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2**
        ///
        /// Property 2: TrimOffset is always BlockSize-aligned — Result is zero when P &lt; B.
        ///
        /// When the first non-zero byte position is less than BlockSize,
        /// the TrimOffset is always 0.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_IsZero_WhenPositionLessThanBlockSize(PositiveInt exponentSeed, NonNegativeInt positionSeed)
        {
            // Generate a power-of-two block size between 2^1 and 2^20
            int exponent = (exponentSeed.Get % 20) + 1;
            int blockSize = 1 << exponent;

            // Ensure position is strictly less than blockSize
            long position = (long)(positionSeed.Get % blockSize);

            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, blockSize);

            return result == 0;
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.3**
        ///
        /// Property 2: TrimOffset is always BlockSize-aligned — Result equals P on boundary.
        ///
        /// When the first non-zero byte position falls exactly on a BlockSize boundary,
        /// the TrimOffset equals that position without further adjustment.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_EqualsPosition_WhenExactlyOnBoundary(PositiveInt multiplierSeed, PositiveInt exponentSeed)
        {
            // Generate a power-of-two block size between 2^1 and 2^20
            int exponent = (exponentSeed.Get % 20) + 1;
            int blockSize = 1 << exponent;

            // Position is an exact multiple of blockSize
            long multiplier = (long)(multiplierSeed.Get % 1000) + 1;
            long position = multiplier * blockSize;

            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, blockSize);

            return result == position;
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.4**
        ///
        /// Property 2: TrimOffset is always BlockSize-aligned — Result is always a multiple of BlockSize.
        ///
        /// For any inputs, the result is always evenly divisible by BlockSize.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_IsAlways_MultipleOfBlockSize(NonNegativeInt positionSeed, PositiveInt exponentSeed)
        {
            long position = (long)positionSeed.Get;
            int exponent = (exponentSeed.Get % 20) + 1;
            int blockSize = 1 << exponent;

            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, blockSize);

            return result % blockSize == 0;
        }

        /// <summary>
        /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
        ///
        /// Property 2: TrimOffset is always BlockSize-aligned — Result never exceeds position.
        ///
        /// The aligned TrimOffset is always less than or equal to the input position
        /// (rounding down never produces a value greater than the input).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool TrimOffset_NeverExceeds_Position(NonNegativeInt positionSeed, PositiveInt exponentSeed)
        {
            long position = (long)positionSeed.Get;
            int exponent = (exponentSeed.Get % 20) + 1;
            int blockSize = 1 << exponent;

            long result = LeadingZeroScanner.ComputeAlignedTrimOffset(position, blockSize);

            return result <= position;
        }
    }
}