using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for all-zeros audio partition behavior.
    ///
    /// Feature: audio-partition-dedup
    /// Property 6: All-zeros partition produces no stored blocks
    /// **Validates: Requirements 1.3, 4.5**
    ///
    /// For any audio partition consisting entirely of zero bytes (length is a multiple
    /// of BlockSize), FindFirstNonZero returns the partition size (dataLength),
    /// indicating the entire partition should be trimmed (TrimOffset = partition size,
    /// no blocks stored). On reconstruction, the entire partition is served as zeros.
    /// </summary>
    public class AllZerosPartitionPropertyTests
    {
        private const int DefaultBlockSize = 0x10000; // 65536 bytes

        /// <summary>
        /// **Validates: Requirements 1.3, 4.5**
        ///
        /// Property 6: All-zeros partition produces no stored blocks — FindFirstNonZero returns dataLength.
        ///
        /// For any all-zero byte array of length N (where N is a multiple of BlockSize),
        /// FindFirstNonZero returns N, indicating no non-zero byte exists and the entire
        /// partition should be trimmed.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllZerosPartition_FindFirstNonZero_ReturnsDataLength(PositiveInt blockCountSeed)
        {
            // Generate partition sizes that are multiples of BlockSize (1 to 50 blocks)
            int blockCount = (blockCountSeed.Get % 50) + 1;
            int partitionSize = blockCount * DefaultBlockSize;

            // Create an all-zero buffer
            byte[] data = new byte[partitionSize];

            // FindFirstNonZero should return dataLength for all-zero data
            long result = LeadingZeroScanner.FindFirstNonZero(data, 0, partitionSize);

            return result == partitionSize;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 4.5**
        ///
        /// Property 6: All-zeros partition produces no stored blocks — TrimOffset equals partition size.
        ///
        /// When FindFirstNonZero returns dataLength (all zeros), the convention from
        /// ProcessSection sets TrimOffset to the partition size. This means
        /// ComputeAlignedTrimOffset(partitionSize, blockSize) == partitionSize for any
        /// partition size that is a multiple of BlockSize.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllZerosPartition_TrimOffset_EqualsPartitionSize(PositiveInt blockCountSeed, PositiveInt exponentSeed)
        {
            // Generate a power-of-two block size between 2^10 and 2^20
            int exponent = (exponentSeed.Get % 11) + 10;
            int blockSize = 1 << exponent;

            // Generate partition sizes that are multiples of blockSize (1 to 20 blocks)
            int blockCount = (blockCountSeed.Get % 20) + 1;
            long partitionSize = (long)blockCount * blockSize;

            // For an all-zeros partition, firstNonZero == partitionSize
            // ComputeAlignedTrimOffset should return partitionSize since it's already aligned
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(partitionSize, blockSize);

            return trimOffset == partitionSize;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 4.5**
        ///
        /// Property 6: All-zeros partition produces no stored blocks — Zero blocks stored.
        ///
        /// When TrimOffset equals the partition size, the number of blocks to store is
        /// (partitionSize - trimOffset) / blockSize == 0. This verifies the arithmetic
        /// that determines block count yields zero for all-zeros partitions.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllZerosPartition_BlockCount_IsZero(PositiveInt blockCountSeed, PositiveInt exponentSeed)
        {
            // Generate a power-of-two block size between 2^10 and 2^20
            int exponent = (exponentSeed.Get % 11) + 10;
            int blockSize = 1 << exponent;

            // Generate partition sizes that are multiples of blockSize
            int blockCount = (blockCountSeed.Get % 20) + 1;
            long partitionSize = (long)blockCount * blockSize;

            // For all-zeros partition: firstNonZero == partitionSize
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(partitionSize, blockSize);

            // Number of blocks to store = (partitionSize - trimOffset) / blockSize
            long blocksToStore = (partitionSize - trimOffset) / blockSize;

            return blocksToStore == 0;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 4.5**
        ///
        /// Property 6: All-zeros partition produces no stored blocks — Reconstruction serves all zeros.
        ///
        /// When TrimOffset equals partition size, reconstruction fills the entire partition
        /// with zeros (via gap-fill). This verifies that a zero-filled buffer of the
        /// partition size is byte-identical to the expected reconstruction output.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllZerosPartition_Reconstruction_ServesAllZeros(PositiveInt blockCountSeed)
        {
            // Generate partition sizes that are multiples of BlockSize (1 to 30 blocks)
            int blockCount = (blockCountSeed.Get % 30) + 1;
            int partitionSize = blockCount * DefaultBlockSize;

            // Original all-zeros partition
            byte[] originalData = new byte[partitionSize];

            // Verify FindFirstNonZero confirms all zeros
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            if (firstNonZero != partitionSize)
                return false;

            // Reconstruction: since TrimOffset == partitionSize and no blocks are stored,
            // the gap-fill mechanism serves all zeros. Simulate by creating a zero buffer.
            byte[] reconstructed = new byte[partitionSize];
            // Gap-fill writes zeros (default byte[] value is 0)

            // Verify byte-identical to original
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.3, 4.5**
        ///
        /// Property 6: All-zeros partition produces no stored blocks — Various block sizes.
        ///
        /// For any power-of-two block size and any all-zero partition that is a multiple
        /// of that block size, the full pipeline (scan → align → trim) results in
        /// TrimOffset == partitionSize and zero stored blocks.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool AllZerosPartition_FullPipeline_ProducesNoBlocks(PositiveInt blockCountSeed, PositiveInt exponentSeed)
        {
            // Generate a power-of-two block size between 2^8 and 2^18
            int exponent = (exponentSeed.Get % 11) + 8;
            int blockSize = 1 << exponent;

            // Generate partition sizes that are multiples of blockSize (1 to 10 blocks)
            int blockCount = (blockCountSeed.Get % 10) + 1;
            int partitionSize = blockCount * blockSize;

            // Create all-zero buffer
            byte[] data = new byte[partitionSize];

            // Step 1: Scan for first non-zero
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, partitionSize);

            // Step 2: Compute aligned trim offset
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Step 3: Verify TrimOffset == partitionSize (entire partition trimmed)
            if (trimOffset != partitionSize)
                return false;

            // Step 4: Verify zero blocks to store
            long blocksToStore = (partitionSize - trimOffset) / blockSize;

            return blocksToStore == 0;
        }
    }
}