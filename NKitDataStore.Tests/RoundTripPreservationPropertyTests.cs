using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for round-trip preservation.
    ///
    /// Feature: audio-partition-dedup
    /// Property 1: Round-trip preservation
    /// **Validates: Requirements 4.6, 1.1, 2.1**
    ///
    /// For any audio partition byte array (of arbitrary content and length that is a
    /// multiple of BlockSize), ingesting it with leading-zero trimming and then
    /// reconstructing it via the ImageBuilder SHALL produce output that is byte-identical
    /// to the original input.
    ///
    /// The round-trip is modeled at the logical level:
    /// 1. Given an arbitrary audio partition byte array (length = N * BlockSize, with some leading zeros)
    /// 2. Simulate ingestion: call FindFirstNonZero, ComputeAlignedTrimOffset, determine stored data
    /// 3. Simulate reconstruction: create output buffer, fill [0, trimOffset) with zeros, copy stored data
    /// 4. Verify output is byte-identical to original input
    /// </summary>
    public class RoundTripPreservationPropertyTests
    {
        private const int DefaultBlockSize = 0x10000; // 65536 bytes

        /// <summary>
        /// **Validates: Requirements 4.6, 1.1, 2.1**
        ///
        /// Property 1: Round-trip preservation — Arbitrary partition data round-trips correctly.
        ///
        /// For any audio partition byte array with arbitrary content (length is a multiple
        /// of BlockSize), the trim-and-reconstruct cycle produces byte-identical output.
        /// Uses a smaller block size to keep memory usage reasonable during property testing.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_ArbitraryData_IsPreserved(
            PositiveInt blockCountSeed,
            PositiveInt leadingZeroBlocksSeed,
            byte[] trailingData)
        {
            // Use a smaller block size for property testing to keep memory reasonable
            int blockSize = 1024;

            // Generate partition with 1 to 8 blocks
            int blockCount = (blockCountSeed.Get % 8) + 1;
            int partitionSize = blockCount * blockSize;

            // Leading zero blocks: 0 to blockCount-1 full blocks of zeros
            int leadingZeroBlocks = leadingZeroBlocksSeed.Get % blockCount;
            int leadingZeroLength = leadingZeroBlocks * blockSize;

            // Create the partition data
            byte[] originalData = new byte[partitionSize];

            // Fill the region after leading zeros with arbitrary data
            if (trailingData != null && trailingData.Length > 0)
            {
                int copyLength = Math.Min(trailingData.Length, partitionSize - leadingZeroLength);
                Array.Copy(trailingData, 0, originalData, leadingZeroLength, copyLength);
            }

            // Ensure there's at least one non-zero byte after leading zeros if we have trailing space
            if (leadingZeroLength < partitionSize && originalData[leadingZeroLength] == 0)
                originalData[leadingZeroLength] = 0x42;

            // === INGESTION SIMULATION ===
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Stored data: bytes from trimOffset to end
            int storedLength = partitionSize - (int)trimOffset;
            byte[] storedData = new byte[storedLength];
            Array.Copy(originalData, (int)trimOffset, storedData, 0, storedLength);

            // === RECONSTRUCTION SIMULATION ===
            byte[] reconstructed = new byte[partitionSize];
            // [0, trimOffset) is filled with zeros (default byte[] value)
            // [trimOffset, end) is filled with stored data
            Array.Copy(storedData, 0, reconstructed, (int)trimOffset, storedLength);

            // === VERIFICATION ===
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.6, 1.1, 2.1**
        ///
        /// Property 1: Round-trip preservation — Partition with non-aligned leading zeros.
        ///
        /// For any audio partition where the first non-zero byte falls at a non-aligned
        /// position (not on a BlockSize boundary), the round-trip still preserves all data
        /// because the trim offset rounds down, keeping the partial-block zeros in the stored data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_NonAlignedLeadingZeros_IsPreserved(
            PositiveInt blockCountSeed,
            PositiveInt leadingZerosSeed,
            byte nonZeroByte)
        {
            if (nonZeroByte == 0)
                nonZeroByte = 0xAB;

            // Use a smaller block size for property testing
            int blockSize = 1024;

            // Generate partition with 2 to 10 blocks (need at least 2 for non-aligned scenario)
            int blockCount = (blockCountSeed.Get % 9) + 2;
            int partitionSize = blockCount * blockSize;

            // Leading zeros: between blockSize and partitionSize - 1 (non-aligned)
            int leadingZeroLength = blockSize + (leadingZerosSeed.Get % (partitionSize - blockSize - 1));

            // Create the partition data
            byte[] originalData = new byte[partitionSize];
            // Place non-zero byte at the first non-zero position
            originalData[leadingZeroLength] = nonZeroByte;
            // Fill remaining with a pattern
            for (int i = leadingZeroLength + 1; i < partitionSize; i++)
                originalData[i] = (byte)(((i * 7) + 13) & 0xFF);

            // === INGESTION SIMULATION ===
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Stored data: bytes from trimOffset to end
            int storedLength = partitionSize - (int)trimOffset;
            byte[] storedData = new byte[storedLength];
            Array.Copy(originalData, (int)trimOffset, storedData, 0, storedLength);

            // === RECONSTRUCTION SIMULATION ===
            byte[] reconstructed = new byte[partitionSize];
            // [0, trimOffset) is filled with zeros (default byte[] value)
            // [trimOffset, end) is filled with stored data
            Array.Copy(storedData, 0, reconstructed, (int)trimOffset, storedLength);

            // === VERIFICATION ===
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.6, 1.1, 2.1**
        ///
        /// Property 1: Round-trip preservation — Partition with no leading zeros.
        ///
        /// When the first byte is non-zero (no leading zeros), TrimOffset is 0 and the
        /// entire partition is stored verbatim. Round-trip produces identical output.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_NoLeadingZeros_IsPreserved(
            PositiveInt blockCountSeed,
            byte firstByte)
        {
            if (firstByte == 0)
                firstByte = 0x01;

            int blockSize = 1024;

            // Generate partition with 1 to 8 blocks
            int blockCount = (blockCountSeed.Get % 8) + 1;
            int partitionSize = blockCount * blockSize;

            // Create partition with non-zero first byte
            byte[] originalData = new byte[partitionSize];
            originalData[0] = firstByte;
            for (int i = 1; i < partitionSize; i++)
                originalData[i] = (byte)(((i * 3) + 5) & 0xFF);

            // === INGESTION SIMULATION ===
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // TrimOffset should be 0 since first byte is non-zero
            if (trimOffset != 0)
                return false;

            // Stored data: entire partition
            int storedLength = partitionSize - (int)trimOffset;
            byte[] storedData = new byte[storedLength];
            Array.Copy(originalData, (int)trimOffset, storedData, 0, storedLength);

            // === RECONSTRUCTION SIMULATION ===
            byte[] reconstructed = new byte[partitionSize];
            Array.Copy(storedData, 0, reconstructed, (int)trimOffset, storedLength);

            // === VERIFICATION ===
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.6, 1.1, 2.1**
        ///
        /// Property 1: Round-trip preservation — All-zeros partition round-trips correctly.
        ///
        /// When the entire partition is zeros, TrimOffset equals partition size, no data
        /// is stored, and reconstruction fills the entire buffer with zeros (matching original).
        /// </summary>
        [Property(MaxTest = 100)]
        public bool RoundTrip_AllZerosPartition_IsPreserved(PositiveInt blockCountSeed)
        {
            int blockSize = 1024;

            // Generate partition with 1 to 20 blocks
            int blockCount = (blockCountSeed.Get % 20) + 1;
            int partitionSize = blockCount * blockSize;

            // Create all-zeros partition
            byte[] originalData = new byte[partitionSize];

            // === INGESTION SIMULATION ===
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // For all-zeros: firstNonZero == partitionSize, trimOffset == partitionSize
            int storedLength = partitionSize - (int)trimOffset;
            byte[] storedData = new byte[storedLength]; // empty array when trimOffset == partitionSize

            // === RECONSTRUCTION SIMULATION ===
            byte[] reconstructed = new byte[partitionSize];
            // [0, trimOffset) filled with zeros (entire buffer since trimOffset == partitionSize)
            if (storedLength > 0)
                Array.Copy(storedData, 0, reconstructed, (int)trimOffset, storedLength);

            // === VERIFICATION ===
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 4.6, 1.1, 2.1**
        ///
        /// Property 1: Round-trip preservation — Default BlockSize (64 KiB) round-trip.
        ///
        /// Verifies the round-trip property using the default 64 KiB BlockSize to confirm
        /// correctness at the actual production block size.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool RoundTrip_DefaultBlockSize_IsPreserved(
            PositiveInt blockCountSeed,
            PositiveInt leadingZerosSeed,
            byte nonZeroByte)
        {
            if (nonZeroByte == 0)
                nonZeroByte = 0xFF;

            // Use the default 64 KiB block size
            int blockSize = DefaultBlockSize;

            // Generate partition with 1 to 4 blocks (keep memory reasonable at 64 KiB per block)
            int blockCount = (blockCountSeed.Get % 4) + 1;
            int partitionSize = blockCount * blockSize;

            // Leading zeros: 0 to partitionSize - 1
            int leadingZeroLength = leadingZerosSeed.Get % partitionSize;

            // Create the partition data
            byte[] originalData = new byte[partitionSize];
            // Place non-zero byte at the first non-zero position
            if (leadingZeroLength < partitionSize)
            {
                originalData[leadingZeroLength] = nonZeroByte;
                // Fill remaining with a deterministic pattern
                for (int i = leadingZeroLength + 1; i < partitionSize; i++)
                    originalData[i] = (byte)(((i * 11) + 3) & 0xFF);
            }

            // === INGESTION SIMULATION ===
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(originalData, 0, partitionSize);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, blockSize);

            // Stored data: bytes from trimOffset to end
            int storedLength = partitionSize - (int)trimOffset;
            byte[] storedData = new byte[storedLength];
            if (storedLength > 0)
                Array.Copy(originalData, (int)trimOffset, storedData, 0, storedLength);

            // === RECONSTRUCTION SIMULATION ===
            byte[] reconstructed = new byte[partitionSize];
            // [0, trimOffset) is filled with zeros (default byte[] value)
            // [trimOffset, end) is filled with stored data
            if (storedLength > 0)
                Array.Copy(storedData, 0, reconstructed, (int)trimOffset, storedLength);

            // === VERIFICATION ===
            for (int i = 0; i < partitionSize; i++)
            {
                if (reconstructed[i] != originalData[i])
                    return false;
            }

            return true;
        }
    }
}