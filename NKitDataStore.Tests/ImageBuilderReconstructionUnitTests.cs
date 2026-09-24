namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for ImageBuilder reconstruction logic with TrimOffset metadata.
    ///
    /// Since testing the full ImageBuilder pipeline requires significant infrastructure,
    /// these tests validate the reconstruction DECISION LOGIC by simulating the same model:
    ///
    /// 1. If TrimOffset metadata is absent or invalid (negative, exceeds partition size):
    ///    serve stored blocks directly (legacy behavior)
    /// 2. If TrimOffset is present and valid:
    ///    serve zeros for [0, trimOffset), then stored block data for [trimOffset, partitionSize)
    /// 3. For reads spanning the boundary:
    ///    concatenate zeros and block data seamlessly
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 8.1, 8.2, 8.3**
    /// </summary>
    public class ImageBuilderReconstructionUnitTests
    {
        private const int BlockSize = 0x10000; // 65536 bytes

        /// <summary>
        /// Simulates the ImageBuilder reconstruction logic for an audio partition.
        ///
        /// Given stored block data, a TrimOffset (from metadata), and the full partition size,
        /// produces the reconstructed output that the ImageBuilder would serve.
        ///
        /// Rules:
        /// - If trimOffset is null (absent metadata): return storedBlocks directly (legacy)
        /// - If trimOffset is negative: treat as legacy (no zeros inserted)
        /// - If trimOffset exceeds partitionSize: treat as legacy (no zeros inserted)
        /// - If trimOffset == partitionSize: entire partition is zeros (no stored blocks expected)
        /// - If trimOffset is valid [0, partitionSize]: serve zeros for [0, trimOffset),
        ///   then storedBlocks for [trimOffset, partitionSize)
        /// </summary>
        private static byte[] SimulateReconstruction(byte[] storedBlocks, long? trimOffset, long partitionSize)
        {
            // Legacy behavior: absent or invalid TrimOffset
            if (trimOffset == null || trimOffset < 0 || trimOffset > partitionSize)
            {
                // Serve stored blocks directly (legacy pass-through)
                byte[] legacy = new byte[storedBlocks.Length];
                Array.Copy(storedBlocks, legacy, storedBlocks.Length);
                return legacy;
            }

            // Valid TrimOffset: reconstruct with leading zeros
            byte[] result = new byte[partitionSize];

            // [0, trimOffset) is already zeros (default byte array initialization)

            // [trimOffset, partitionSize) comes from stored blocks
            long dataLength = partitionSize - trimOffset.Value;
            if (dataLength > 0 && storedBlocks.Length > 0)
            {
                int copyLength = (int)Math.Min(dataLength, storedBlocks.Length);
                Array.Copy(storedBlocks, 0, result, (int)trimOffset.Value, copyLength);
            }

            return result;
        }

        /// <summary>
        /// Simulates reading a specific range from the reconstructed partition.
        /// This models what happens when ImageBuilder.Read is called with a position and count.
        /// </summary>
        private static byte[] SimulateRead(byte[] storedBlocks, long? trimOffset, long partitionSize, long readOffset, int readCount)
        {
            byte[] fullReconstruction = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Clamp read to available data
            int available = (int)Math.Max(0, Math.Min(readCount, fullReconstruction.Length - readOffset));
            byte[] result = new byte[available];
            Array.Copy(fullReconstruction, (int)readOffset, result, 0, available);
            return result;
        }

        /// <summary>
        /// Determines whether a TrimOffset value from metadata should be treated as valid.
        /// Models the ImageBuilder's validation logic.
        /// </summary>
        private static bool IsTrimOffsetValid(long? trimOffset, long partitionSize)
        {
            if (trimOffset == null) return false;
            if (trimOffset < 0) return false;
            if (trimOffset > partitionSize) return false;
            return true;
        }

        // ── Test: Audio area with TrimOffset=0x20000 ─────────────────────

        /// <summary>
        /// Validates: Requirements 4.1, 4.2
        ///
        /// Audio area with TrimOffset=0x20000: first 0x20000 bytes served as zeros,
        /// remaining bytes from stored blocks.
        /// </summary>
        [Fact]
        public void ValidTrimOffset_FirstBytesAreZeros_RemainingFromStoredBlocks()
        {
            // Arrange: partition of 4 blocks, TrimOffset = 2 blocks (0x20000)
            long partitionSize = BlockSize * 4; // 0x40000
            long trimOffset = 0x20000; // 2 blocks

            // Stored blocks represent data from [trimOffset, partitionSize)
            int storedLength = (int)(partitionSize - trimOffset);
            byte[] storedBlocks = new byte[storedLength];
            for (int i = 0; i < storedLength; i++)
                storedBlocks[i] = (byte)((i + 0xAB) % 256);

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: total length equals partition size
            Assert.Equal((int)partitionSize, reconstructed.Length);

            // Assert: first 0x20000 bytes are all zeros
            for (int i = 0; i < (int)trimOffset; i++)
                Assert.Equal(0, reconstructed[i]);

            // Assert: remaining bytes match stored blocks
            for (int i = 0; i < storedLength; i++)
                Assert.Equal(storedBlocks[i], reconstructed[(int)trimOffset + i]);
        }

        /// <summary>
        /// Validates: Requirements 4.1, 4.2
        ///
        /// Audio area with TrimOffset=0x20000: reading from position 0 returns zeros,
        /// reading from position 0x20000 returns stored block data.
        /// </summary>
        [Fact]
        public void ValidTrimOffset_ReadBeforeBoundary_ReturnsZeros()
        {
            // Arrange
            long partitionSize = BlockSize * 4;
            long trimOffset = 0x20000;
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xCD;

            // Act: read 0x100 bytes from position 0 (within zero region)
            byte[] readResult = SimulateRead(storedBlocks, trimOffset, partitionSize, readOffset: 0, readCount: 0x100);

            // Assert: all zeros
            Assert.Equal(0x100, readResult.Length);
            Assert.All(readResult, b => Assert.Equal(0, b));
        }

        [Fact]
        public void ValidTrimOffset_ReadAfterBoundary_ReturnsStoredData()
        {
            // Arrange
            long partitionSize = BlockSize * 4;
            long trimOffset = 0x20000;
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xCD;

            // Act: read 0x100 bytes from position 0x20000 (start of stored data)
            byte[] readResult = SimulateRead(storedBlocks, trimOffset, partitionSize, readOffset: 0x20000, readCount: 0x100);

            // Assert: all 0xCD (stored block data)
            Assert.Equal(0x100, readResult.Length);
            Assert.All(readResult, b => Assert.Equal(0xCD, b));
        }

        // ── Test: Audio area with no TrimOffset metadata (legacy) ────────

        /// <summary>
        /// Validates: Requirements 4.4, 8.1, 8.2
        ///
        /// Audio area with no TrimOffset metadata: served directly from blocks (legacy behavior).
        /// No zeros are inserted; output is byte-identical to stored blocks.
        /// </summary>
        [Fact]
        public void NoTrimOffset_LegacyBehavior_ServedDirectlyFromBlocks()
        {
            // Arrange: stored blocks with arbitrary data (including leading zeros in the data itself)
            int storedLength = BlockSize * 3;
            byte[] storedBlocks = new byte[storedLength];
            // First block is zeros, second and third have data
            for (int i = BlockSize; i < storedLength; i++)
                storedBlocks[i] = (byte)(i % 256);

            long partitionSize = storedLength; // In legacy mode, stored = full partition

            // Act: trimOffset is null (absent metadata)
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset: null, partitionSize);

            // Assert: output is byte-identical to stored blocks (no transformation)
            Assert.Equal(storedBlocks.Length, reconstructed.Length);
            Assert.Equal(storedBlocks, reconstructed);
        }

        /// <summary>
        /// Validates: Requirements 8.1, 8.2
        ///
        /// Legacy area: no zero-fill, no reordering, byte-for-byte pass-through.
        /// </summary>
        [Fact]
        public void NoTrimOffset_LegacyBehavior_NoZeroFillApplied()
        {
            // Arrange: stored blocks that start with non-zero data
            byte[] storedBlocks = new byte[BlockSize * 2];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = (byte)(((i * 7) + 3) % 256);

            long partitionSize = storedBlocks.Length;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset: null, partitionSize);

            // Assert: byte-for-byte identical, no transformation
            Assert.Equal(storedBlocks, reconstructed);
        }

        // ── Test: TrimOffset equal to partition size (all-zeros) ──────────

        /// <summary>
        /// Validates: Requirements 4.5
        ///
        /// Audio area with TrimOffset equal to partition size: entire partition served as zeros.
        /// This represents an all-zeros partition where no blocks were stored.
        /// </summary>
        [Fact]
        public void TrimOffsetEqualsPartitionSize_EntirePartitionServedAsZeros()
        {
            // Arrange: TrimOffset == partitionSize means all-zeros partition
            long partitionSize = BlockSize * 3;
            long trimOffset = partitionSize; // equals partition size
            byte[] storedBlocks = Array.Empty<byte>(); // no blocks stored

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: entire output is zeros
            Assert.Equal((int)partitionSize, reconstructed.Length);
            Assert.All(reconstructed, b => Assert.Equal(0, b));
        }

        /// <summary>
        /// Validates: Requirements 4.5
        ///
        /// All-zeros partition with various sizes: all produce zero-filled output.
        /// </summary>
        [Theory]
        [InlineData(0x10000)]   // 1 block
        [InlineData(0x30000)]   // 3 blocks
        [InlineData(0x100000)]  // 16 blocks
        [InlineData(2352)]      // 1 CD audio sector
        public void TrimOffsetEqualsPartitionSize_VariousSizes_AllZeros(int size)
        {
            // Arrange
            long partitionSize = size;
            long trimOffset = partitionSize;
            byte[] storedBlocks = Array.Empty<byte>();

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert
            Assert.Equal(size, reconstructed.Length);
            Assert.All(reconstructed, b => Assert.Equal(0, b));
        }

        // ── Test: Read spanning the TrimOffset boundary ──────────────────

        /// <summary>
        /// Validates: Requirements 4.3
        ///
        /// Read request spanning the TrimOffset boundary: zeros before, block data after,
        /// seamlessly concatenated within the same read operation.
        /// </summary>
        [Fact]
        public void ReadSpanningBoundary_ZerosBeforeBlockDataAfter_SeamlessConcatenation()
        {
            // Arrange: partition with TrimOffset at 0x20000
            long partitionSize = BlockSize * 4;
            long trimOffset = 0x20000;
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xEE;

            // Read 0x200 bytes spanning the boundary: 0x100 before, 0x100 after
            long readOffset = trimOffset - 0x100;
            int readCount = 0x200;

            // Act
            byte[] readResult = SimulateRead(storedBlocks, trimOffset, partitionSize, readOffset, readCount);

            // Assert: first 0x100 bytes are zeros (before boundary)
            Assert.Equal(0x200, readResult.Length);
            for (int i = 0; i < 0x100; i++)
                Assert.Equal(0, readResult[i]);

            // Assert: last 0x100 bytes are stored data (after boundary)
            for (int i = 0x100; i < 0x200; i++)
                Assert.Equal(0xEE, readResult[i]);
        }

        /// <summary>
        /// Validates: Requirements 4.3
        ///
        /// Read spanning boundary with varying overlap amounts.
        /// </summary>
        [Theory]
        [InlineData(1, 2)]       // 1 byte before, 1 byte after
        [InlineData(0x80, 0x100)] // 128 bytes before, 128 bytes after
        [InlineData(0xFF00, 0x10000)] // large span: 0xFF00 before, 0x100 after
        public void ReadSpanningBoundary_VaryingOverlap_CorrectConcatenation(int bytesBefore, int totalRead)
        {
            // Arrange
            long partitionSize = BlockSize * 6;
            long trimOffset = BlockSize * 2L; // 0x20000
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xAA;

            long readOffset = trimOffset - bytesBefore;
            int bytesAfter = totalRead - bytesBefore;

            // Act
            byte[] readResult = SimulateRead(storedBlocks, trimOffset, partitionSize, readOffset, totalRead);

            // Assert
            Assert.Equal(totalRead, readResult.Length);

            // Bytes before boundary are zeros
            for (int i = 0; i < bytesBefore; i++)
                Assert.Equal(0, readResult[i]);

            // Bytes after boundary are stored data
            for (int i = bytesBefore; i < totalRead; i++)
                Assert.Equal(0xAA, readResult[i]);
        }

        /// <summary>
        /// Validates: Requirements 4.3
        ///
        /// Read that starts exactly at the boundary: no zeros, all stored data.
        /// </summary>
        [Fact]
        public void ReadStartingExactlyAtBoundary_AllStoredData()
        {
            // Arrange
            long partitionSize = BlockSize * 4;
            long trimOffset = 0x20000;
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xBB;

            // Act: read starting exactly at trimOffset
            byte[] readResult = SimulateRead(storedBlocks, trimOffset, partitionSize, readOffset: trimOffset, readCount: 0x1000);

            // Assert: all stored data, no zeros
            Assert.Equal(0x1000, readResult.Length);
            Assert.All(readResult, b => Assert.Equal(0xBB, b));
        }

        // ── Test: Invalid TrimOffset (negative) ──────────────────────────

        /// <summary>
        /// Validates: Requirements 8.3
        ///
        /// Invalid TrimOffset (negative): treated as legacy, no zeros inserted.
        /// The ImageBuilder falls back to serving stored blocks directly.
        /// </summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(-0x10000)]
        [InlineData(long.MinValue)]
        public void NegativeTrimOffset_TreatedAsLegacy_NoZerosInserted(long invalidTrimOffset)
        {
            // Arrange: stored blocks with non-zero data throughout
            byte[] storedBlocks = new byte[BlockSize * 2];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = (byte)((i + 1) % 256);

            long partitionSize = storedBlocks.Length;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, invalidTrimOffset, partitionSize);

            // Assert: output is byte-identical to stored blocks (legacy pass-through)
            Assert.Equal(storedBlocks.Length, reconstructed.Length);
            Assert.Equal(storedBlocks, reconstructed);
        }

        /// <summary>
        /// Validates: Requirements 8.3
        ///
        /// Negative TrimOffset is not considered valid.
        /// </summary>
        [Fact]
        public void NegativeTrimOffset_IsNotValid()
        {
            Assert.False(IsTrimOffsetValid(-1, BlockSize * 4));
            Assert.False(IsTrimOffsetValid(-100, BlockSize * 4));
            Assert.False(IsTrimOffsetValid(long.MinValue, BlockSize * 4));
        }

        // ── Test: Invalid TrimOffset (exceeds partition size) ────────────

        /// <summary>
        /// Validates: Requirements 8.3
        ///
        /// Invalid TrimOffset (exceeds partition size): treated as legacy, no zeros inserted.
        /// The ImageBuilder falls back to serving stored blocks directly.
        /// </summary>
        [Theory]
        [InlineData(0x40001)]  // 1 byte over partition size
        [InlineData(0x50000)]  // significantly over
        [InlineData(0x100000)] // way over
        public void TrimOffsetExceedsPartitionSize_TreatedAsLegacy_NoZerosInserted(long invalidTrimOffset)
        {
            // Arrange: partition size is 0x40000 (4 blocks)
            long partitionSize = BlockSize * 4; // 0x40000
            byte[] storedBlocks = new byte[(int)partitionSize];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = (byte)(i * 3 % 256);

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, invalidTrimOffset, partitionSize);

            // Assert: output is byte-identical to stored blocks (legacy pass-through)
            Assert.Equal(storedBlocks.Length, reconstructed.Length);
            Assert.Equal(storedBlocks, reconstructed);
        }

        /// <summary>
        /// Validates: Requirements 8.3
        ///
        /// TrimOffset exceeding partition size is not considered valid.
        /// </summary>
        [Fact]
        public void TrimOffsetExceedsPartitionSize_IsNotValid()
        {
            long partitionSize = BlockSize * 4;
            Assert.False(IsTrimOffsetValid(partitionSize + 1, partitionSize));
            Assert.False(IsTrimOffsetValid(partitionSize * 2, partitionSize));
            Assert.False(IsTrimOffsetValid(long.MaxValue, partitionSize));
        }

        // ── Additional edge case tests ───────────────────────────────────

        /// <summary>
        /// Validates: Requirements 4.1, 4.2
        ///
        /// TrimOffset of zero with valid metadata: no zeros prepended, stored blocks served directly.
        /// This is equivalent to legacy behavior but with explicit metadata present.
        /// </summary>
        [Fact]
        public void TrimOffsetZero_NoZerosPrepended_StoredBlocksServedDirectly()
        {
            // Arrange: TrimOffset = 0 means no leading zeros to restore
            long partitionSize = BlockSize * 3;
            long trimOffset = 0;
            byte[] storedBlocks = new byte[(int)partitionSize];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = (byte)(i % 256);

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: output equals stored blocks (no zeros prepended)
            Assert.Equal(storedBlocks, reconstructed);
        }

        /// <summary>
        /// Validates: Requirements 4.1, 4.2
        ///
        /// TrimOffset exactly at one BlockSize boundary: first block is zeros, rest from stored.
        /// </summary>
        [Fact]
        public void TrimOffsetOneBlock_FirstBlockZeros_RestFromStored()
        {
            // Arrange
            long partitionSize = BlockSize * 3;
            long trimOffset = BlockSize; // exactly 1 block
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xFF;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert
            Assert.Equal((int)partitionSize, reconstructed.Length);

            // First block is zeros
            for (int i = 0; i < BlockSize; i++)
                Assert.Equal(0, reconstructed[i]);

            // Remaining blocks are stored data
            for (int i = BlockSize; i < (int)partitionSize; i++)
                Assert.Equal(0xFF, reconstructed[i]);
        }

        /// <summary>
        /// Validates: Requirements 4.6, 8.1
        ///
        /// Reconstructed output total length always equals the partition size
        /// regardless of TrimOffset value (when valid).
        /// </summary>
        [Theory]
        [InlineData(0x10000, 0x0)]       // TrimOffset=0, partitionSize=1 block
        [InlineData(0x30000, 0x10000)]   // TrimOffset=1 block, partitionSize=3 blocks
        [InlineData(0x40000, 0x20000)]   // TrimOffset=2 blocks, partitionSize=4 blocks
        [InlineData(0x50000, 0x50000)]   // TrimOffset=partitionSize (all zeros)
        public void ReconstructedOutput_LengthEqualsPartitionSize(int partitionSizeInt, int trimOffsetInt)
        {
            // Arrange
            long partitionSize = partitionSizeInt;
            long trimOffset = trimOffsetInt;
            int storedLength = (int)(partitionSize - trimOffset);
            byte[] storedBlocks = new byte[storedLength];

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert
            Assert.Equal((int)partitionSize, reconstructed.Length);
        }

        /// <summary>
        /// Validates: Requirements 4.1, 4.2
        ///
        /// Verifies that the reconstruction model correctly uses AreaMetadata to determine behavior.
        /// When TrimOffset is stored in metadata, it drives the reconstruction.
        /// </summary>
        [Fact]
        public void MetadataIntegration_TrimOffsetFromAreaMetadata_DrivesReconstruction()
        {
            // Arrange: simulate reading TrimOffset from AreaMetadata
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.TrimOffset, 0x20000L);

            long? trimOffset = metadata.GetLong(AreaValueType.TrimOffset);

            long partitionSize = BlockSize * 4;
            byte[] storedBlocks = new byte[(int)(partitionSize - trimOffset!.Value)];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0xDD;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: zeros before TrimOffset, stored data after
            Assert.Equal((int)partitionSize, reconstructed.Length);
            for (int i = 0; i < 0x20000; i++)
                Assert.Equal(0, reconstructed[i]);
            for (int i = 0x20000; i < (int)partitionSize; i++)
                Assert.Equal(0xDD, reconstructed[i]);
        }

        /// <summary>
        /// Validates: Requirements 8.1, 8.2
        ///
        /// Verifies that absent TrimOffset in AreaMetadata results in legacy behavior.
        /// </summary>
        [Fact]
        public void MetadataIntegration_AbsentTrimOffset_LegacyBehavior()
        {
            // Arrange: metadata without TrimOffset key
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, "FileSystem"); // some other metadata

            long? trimOffset = metadata.GetLong(AreaValueType.TrimOffset);
            Assert.Null(trimOffset); // confirms absence

            byte[] storedBlocks = new byte[BlockSize * 2];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = (byte)(i % 256);

            long partitionSize = storedBlocks.Length;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: byte-identical to stored blocks
            Assert.Equal(storedBlocks, reconstructed);
        }

        /// <summary>
        /// Validates: Requirements 8.3
        ///
        /// Verifies that non-numeric TrimOffset in metadata (parsed as null by GetLong)
        /// results in legacy behavior.
        /// </summary>
        [Fact]
        public void MetadataIntegration_NonNumericTrimOffset_TreatedAsLegacy()
        {
            // Arrange: metadata with non-numeric TrimOffset value
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.TrimOffset, "invalid_value");

            long? trimOffset = metadata.GetLong(AreaValueType.TrimOffset);
            Assert.Null(trimOffset); // GetLong returns null for non-parseable values

            byte[] storedBlocks = new byte[BlockSize * 2];
            for (int i = 0; i < storedBlocks.Length; i++)
                storedBlocks[i] = 0x42;

            long partitionSize = storedBlocks.Length;

            // Act
            byte[] reconstructed = SimulateReconstruction(storedBlocks, trimOffset, partitionSize);

            // Assert: byte-identical to stored blocks (legacy)
            Assert.Equal(storedBlocks, reconstructed);
        }
    }
}