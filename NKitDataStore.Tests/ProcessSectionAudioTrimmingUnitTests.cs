using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for the ProcessSection audio trimming logic in DataStoreIso9660Formatter.
    ///
    /// Since ProcessSection requires significant infrastructure (IImageWriter, DataStore, IStepContext),
    /// these tests validate the trimming DECISION LOGIC by simulating the same code path:
    /// 1. Determine if trimming applies (Audio + CUE/GDI)
    /// 2. Scan for leading zeros via LeadingZeroScanner
    /// 3. Compute aligned TrimOffset
    /// 4. Determine what metadata to store and what data to write
    ///
    /// The simulated model captures the exact decisions ProcessSection makes:
    /// - Whether TrimOffset metadata is stored (and its value)
    /// - The write offset and data range passed to WriteData
    /// - Whether any data is written at all (all-zeros case)
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.3, 2.5, 7.1, 7.2**
    /// </summary>
    public class ProcessSectionAudioTrimmingUnitTests
    {
        private const int BlockSize = 0x10000; // 65536 bytes (standard DataStore block size)

        /// <summary>
        /// Captures the outcome of the ProcessSection audio trimming decision logic.
        /// </summary>
        private class ProcessSectionResult
        {
            /// <summary>Whether TrimOffset metadata was stored.</summary>
            public bool TrimOffsetStored { get; set; }

            /// <summary>The TrimOffset value stored in metadata (if stored).</summary>
            public long TrimOffsetValue { get; set; }

            /// <summary>Whether WriteData was called (false for all-zeros partition).</summary>
            public bool DataWritten { get; set; }

            /// <summary>The image offset passed to WriteData.</summary>
            public long WriteOffset { get; set; }

            /// <summary>The data offset within the buffer passed to WriteData.</summary>
            public int WriteDataOffset { get; set; }

            /// <summary>The length of data passed to WriteData.</summary>
            public int WriteDataLength { get; set; }
        }

        /// <summary>
        /// Simulates the ProcessSection audio trimming logic for CUE/GDI audio partitions.
        /// This mirrors the exact code path in DataStoreIso9660Formatter.ProcessSection.
        /// </summary>
        private static ProcessSectionResult SimulateProcessSectionAudio(
            byte[] data, long imageOffset, int dataStoreBlockSize, bool isCueOrGdi, AreaType areaType)
        {
            ProcessSectionResult result = new ProcessSectionResult();

            // Non-audio areas are always stored verbatim
            if (areaType != AreaType.Audio)
            {
                result.DataWritten = true;
                result.WriteOffset = imageOffset;
                result.WriteDataOffset = 0;
                result.WriteDataLength = data.Length;
                result.TrimOffsetStored = false;
                return result;
            }

            // Non-CUE/GDI audio is stored verbatim without trimming
            if (!isCueOrGdi)
            {
                result.DataWritten = true;
                result.WriteOffset = imageOffset;
                result.WriteDataOffset = 0;
                result.WriteDataLength = data.Length;
                result.TrimOffsetStored = false;
                return result;
            }

            // CUE/GDI audio — apply leading-zero trimming logic
            int size = data.Length;
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, size);
            long trimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, dataStoreBlockSize);

            if (firstNonZero == size)
            {
                // All bytes are zero — store TrimOffset = partition size, write no blocks
                result.TrimOffsetStored = true;
                result.TrimOffsetValue = size;
                result.DataWritten = false;
            }
            else if (trimOffset > 0)
            {
                // Record trim offset in area metadata and write data after the trimmed region
                result.TrimOffsetStored = true;
                result.TrimOffsetValue = trimOffset;
                result.DataWritten = true;
                result.WriteOffset = imageOffset + trimOffset;
                result.WriteDataOffset = (int)trimOffset;
                result.WriteDataLength = size - (int)trimOffset;
            }
            else
            {
                // No trimming needed (leading zeros < BlockSize) — store verbatim
                result.TrimOffsetStored = false;
                result.DataWritten = true;
                result.WriteOffset = imageOffset;
                result.WriteDataOffset = 0;
                result.WriteDataLength = size;
            }

            return result;
        }

        // ── Test: Audio partition with 2 blocks of leading zeros ──────────

        /// <summary>
        /// Validates: Requirements 1.1, 1.2, 2.1, 2.5
        ///
        /// Audio partition with 2 full blocks of leading zeros followed by non-zero data.
        /// TrimOffset should be stored as 2×BlockSize, and data should be written from that offset.
        /// </summary>
        [Fact]
        public void AudioCueGdi_TwoBlocksLeadingZeros_TrimOffsetStoredAndDataWrittenFromOffset()
        {
            // Arrange: 4 blocks total, first 2 are all zeros, last 2 have data
            int totalSize = BlockSize * 4;
            byte[] data = new byte[totalSize];
            // Fill blocks 2 and 3 with non-zero data
            for (int i = BlockSize * 2; i < totalSize; i++)
                data[i] = 0xAB;

            long imageOffset = 0x100000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert
            Assert.True(result.TrimOffsetStored);
            Assert.Equal(BlockSize * 2L, result.TrimOffsetValue);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset + (BlockSize * 2), result.WriteOffset);
            Assert.Equal(BlockSize * 2, result.WriteDataOffset);
            Assert.Equal(BlockSize * 2, result.WriteDataLength);
        }

        /// <summary>
        /// Validates: Requirements 1.1, 1.2, 2.1, 2.5
        ///
        /// Audio partition with 2 blocks of leading zeros where non-zero byte is mid-block.
        /// TrimOffset should still be 2×BlockSize (rounds down to block boundary).
        /// </summary>
        [Fact]
        public void AudioCueGdi_TwoBlocksLeadingZeros_NonZeroMidThirdBlock_TrimOffsetIsTwoBlocks()
        {
            // Arrange: first non-zero byte is at position 2*BlockSize + 500 (mid-third block)
            int totalSize = BlockSize * 4;
            byte[] data = new byte[totalSize];
            data[(BlockSize * 2) + 500] = 0x01;

            long imageOffset = 0x200000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert: TrimOffset rounds down to 2*BlockSize
            Assert.True(result.TrimOffsetStored);
            Assert.Equal(BlockSize * 2L, result.TrimOffsetValue);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset + (BlockSize * 2), result.WriteOffset);
            Assert.Equal(BlockSize * 2, result.WriteDataOffset);
            Assert.Equal(totalSize - (BlockSize * 2), result.WriteDataLength);
        }

        // ── Test: Audio partition with leading zeros less than BlockSize ──

        /// <summary>
        /// Validates: Requirements 1.1, 1.2, 2.3
        ///
        /// Audio partition with leading zeros less than BlockSize.
        /// TrimOffset should be 0 (not stored), data stored verbatim.
        /// </summary>
        [Theory]
        [InlineData(0)]     // non-zero at very start
        [InlineData(100)]   // non-zero at byte 100
        [InlineData(500)]   // non-zero at byte 500
        [InlineData(1200)]  // non-zero at byte 1200
        [InlineData(65535)] // non-zero at last byte before BlockSize boundary
        public void AudioCueGdi_LeadingZerosLessThanBlockSize_NoTrimOffset_StoredVerbatim(int firstNonZeroPos)
        {
            // Arrange
            int totalSize = BlockSize * 3;
            byte[] data = new byte[totalSize];
            data[firstNonZeroPos] = 0xFF;

            long imageOffset = 0x50000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert: No TrimOffset stored, data written verbatim
            Assert.False(result.TrimOffsetStored);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset, result.WriteOffset);
            Assert.Equal(0, result.WriteDataOffset);
            Assert.Equal(totalSize, result.WriteDataLength);
        }

        // ── Test: Audio partition with no leading zeros ──────────────────

        /// <summary>
        /// Validates: Requirements 1.1, 2.3
        ///
        /// Audio partition with non-zero byte at position 0 (no leading zeros at all).
        /// No TrimOffset metadata stored, data stored verbatim.
        /// </summary>
        [Fact]
        public void AudioCueGdi_NoLeadingZeros_NoTrimOffset_StoredVerbatim()
        {
            // Arrange: first byte is non-zero
            int totalSize = BlockSize * 2;
            byte[] data = new byte[totalSize];
            data[0] = 0x42;
            // Fill rest with arbitrary data
            for (int i = 1; i < totalSize; i++)
                data[i] = (byte)(i & 0xFF);

            long imageOffset = 0x10000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert
            Assert.False(result.TrimOffsetStored);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset, result.WriteOffset);
            Assert.Equal(0, result.WriteDataOffset);
            Assert.Equal(totalSize, result.WriteDataLength);
        }

        // ── Test: Audio partition that is all zeros ──────────────────────

        /// <summary>
        /// Validates: Requirements 1.3, 2.5
        ///
        /// Audio partition consisting entirely of zero bytes.
        /// TrimOffset should equal partition size, no blocks written.
        /// </summary>
        [Fact]
        public void AudioCueGdi_AllZeros_TrimOffsetEqualsSize_NoBlocksWritten()
        {
            // Arrange: all zeros
            int totalSize = BlockSize * 3;
            byte[] data = new byte[totalSize]; // all zeros by default

            long imageOffset = 0x80000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert
            Assert.True(result.TrimOffsetStored);
            Assert.Equal((long)totalSize, result.TrimOffsetValue);
            Assert.False(result.DataWritten);
        }

        /// <summary>
        /// Validates: Requirements 1.3, 2.5
        ///
        /// Small all-zeros audio partition (less than one block).
        /// TrimOffset should equal partition size, no blocks written.
        /// </summary>
        [Fact]
        public void AudioCueGdi_SmallAllZeros_TrimOffsetEqualsSize_NoBlocksWritten()
        {
            // Arrange: small all-zeros partition
            int totalSize = 2352; // one CD audio sector
            byte[] data = new byte[totalSize];

            long imageOffset = 0x1000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert: firstNonZero == size, so TrimOffset = size, no data written
            Assert.True(result.TrimOffsetStored);
            Assert.Equal((long)totalSize, result.TrimOffsetValue);
            Assert.False(result.DataWritten);
        }

        // ── Test: Non-audio area stored verbatim with no TrimOffset ──────

        /// <summary>
        /// Validates: Requirements 7.1
        ///
        /// Non-audio area (e.g., AreaType.FileSystem) is stored verbatim with no TrimOffset,
        /// even if the data contains leading zeros.
        /// </summary>
        [Theory]
        [InlineData(AreaType.FileSystem)]
        [InlineData(AreaType.Other)]
        [InlineData(AreaType.ImageHeader)]
        [InlineData(AreaType.PartitionHeader)]
        public void NonAudioArea_StoredVerbatim_NoTrimOffset(AreaType areaType)
        {
            // Arrange: data with leading zeros that would trigger trimming if it were audio
            int totalSize = BlockSize * 3;
            byte[] data = new byte[totalSize];
            data[BlockSize * 2] = 0xFF; // non-zero at 2*BlockSize

            long imageOffset = 0x40000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, areaType);

            // Assert: stored verbatim, no trimming applied
            Assert.False(result.TrimOffsetStored);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset, result.WriteOffset);
            Assert.Equal(0, result.WriteDataOffset);
            Assert.Equal(totalSize, result.WriteDataLength);
        }

        // ── Test: Audio area from non-CUE/GDI format stored verbatim ─────

        /// <summary>
        /// Validates: Requirements 7.2
        ///
        /// Audio area from a non-CUE/GDI format (e.g., ISO) is stored verbatim
        /// with no TrimOffset, even if the data contains leading zeros.
        /// </summary>
        [Fact]
        public void AudioNonCueGdi_StoredVerbatim_NoTrimOffset()
        {
            // Arrange: audio data with leading zeros that would trigger trimming for CUE/GDI
            int totalSize = BlockSize * 3;
            byte[] data = new byte[totalSize];
            data[BlockSize * 2] = 0xFF; // non-zero at 2*BlockSize

            long imageOffset = 0x60000;

            // Act: isCueOrGdi = false (e.g., ISO format)
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: false, AreaType.Audio);

            // Assert: stored verbatim, no trimming applied
            Assert.False(result.TrimOffsetStored);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset, result.WriteOffset);
            Assert.Equal(0, result.WriteDataOffset);
            Assert.Equal(totalSize, result.WriteDataLength);
        }

        // ── Additional edge case tests ───────────────────────────────────

        /// <summary>
        /// Validates: Requirements 1.2, 2.1
        ///
        /// Audio partition where first non-zero byte is exactly at a BlockSize boundary.
        /// TrimOffset should equal that boundary position.
        /// </summary>
        [Fact]
        public void AudioCueGdi_FirstNonZeroExactlyAtBlockBoundary_TrimOffsetEqualsBoundary()
        {
            // Arrange: first non-zero at exactly BlockSize (1 block of leading zeros)
            int totalSize = BlockSize * 3;
            byte[] data = new byte[totalSize];
            data[BlockSize] = 0x01;

            long imageOffset = 0x30000;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert
            Assert.True(result.TrimOffsetStored);
            Assert.Equal((long)BlockSize, result.TrimOffsetValue);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset + BlockSize, result.WriteOffset);
            Assert.Equal(BlockSize, result.WriteDataOffset);
            Assert.Equal(totalSize - BlockSize, result.WriteDataLength);
        }

        /// <summary>
        /// Validates: Requirements 1.2, 2.1
        ///
        /// Verifies that the TrimOffset metadata value matches what would be stored
        /// in AreaMetadata via AreaValueType.TrimOffset.
        /// </summary>
        [Fact]
        public void AudioCueGdi_TrimOffsetMetadata_CanBeStoredAndRetrieved()
        {
            // Arrange: simulate storing TrimOffset in AreaMetadata
            int totalSize = BlockSize * 4;
            byte[] data = new byte[totalSize];
            data[(BlockSize * 2) + 100] = 0xAA; // first non-zero in third block

            // Compute expected TrimOffset
            long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, totalSize);
            long expectedTrimOffset = LeadingZeroScanner.ComputeAlignedTrimOffset(firstNonZero, BlockSize);

            // Act: store in AreaMetadata (as ProcessSection would)
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.TrimOffset, expectedTrimOffset);

            // Assert: can be retrieved correctly
            Assert.Equal(expectedTrimOffset, metadata.GetLong(AreaValueType.TrimOffset));
            Assert.Equal(BlockSize * 2L, expectedTrimOffset);
        }

        /// <summary>
        /// Validates: Requirements 2.5
        ///
        /// Verifies that no data from the leading zero region is included in the write.
        /// The WriteDataOffset must equal TrimOffset, ensuring leading zeros are skipped.
        /// </summary>
        [Theory]
        [InlineData(1)]   // 1 block of leading zeros
        [InlineData(2)]   // 2 blocks of leading zeros
        [InlineData(5)]   // 5 blocks of leading zeros
        [InlineData(10)]  // 10 blocks of leading zeros
        public void AudioCueGdi_LeadingZerosNeverWritten_WriteStartsAtTrimOffset(int leadingZeroBlocks)
        {
            // Arrange
            int totalBlocks = leadingZeroBlocks + 2; // add 2 blocks of actual data
            int totalSize = BlockSize * totalBlocks;
            byte[] data = new byte[totalSize];
            // First non-zero byte at start of block after leading zeros
            data[BlockSize * leadingZeroBlocks] = 0xCD;

            long imageOffset = 0x0;

            // Act
            ProcessSectionResult result = SimulateProcessSectionAudio(data, imageOffset, BlockSize, isCueOrGdi: true, AreaType.Audio);

            // Assert: data written starts exactly at TrimOffset, skipping all leading zeros
            long expectedTrimOffset = (long)BlockSize * leadingZeroBlocks;
            Assert.True(result.TrimOffsetStored);
            Assert.Equal(expectedTrimOffset, result.TrimOffsetValue);
            Assert.True(result.DataWritten);
            Assert.Equal(imageOffset + expectedTrimOffset, result.WriteOffset);
            Assert.Equal((int)expectedTrimOffset, result.WriteDataOffset);
            Assert.Equal(totalSize - (int)expectedTrimOffset, result.WriteDataLength);
        }
    }
}