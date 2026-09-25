using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for ISO9660 stride computation correctness.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 3: ISO9660 Stride Computation Correctness
    /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
    ///
    /// For any track with a recognized block size and mode, the DataStride computed
    /// by the ISO9660 formatter SHALL satisfy: OffsetToClean(stridedOffset) and
    /// CleanToOffset(cleanOffset) SHALL be mutual inverses for all valid offsets.
    /// </summary>
    public class Iso9660StrideComputationPropertyTests
    {
        /// <summary>
        /// All recognized ISO9660 stride configurations as defined in the design document.
        /// Audio stride (SourceBlockSize=0x930, DataOffset=0, DataLength=0x930) is a no-op
        /// stride where SourceBlockSize == DataLength, so OffsetToClean/CleanToOffset are identity.
        /// </summary>
        private static readonly DataStride[] Iso9660Strides = new[]
        {
            // Mode1Raw: SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800
            new DataStride { SourceBlockSize = 0x930, DataOffset = 0x10, DataLength = 0x800 },
            // Mode2/Mode2Form1: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800
            new DataStride { SourceBlockSize = 0x930, DataOffset = 0x18, DataLength = 0x800 },
            // Mode2Form2: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914
            new DataStride { SourceBlockSize = 0x930, DataOffset = 0x18, DataLength = 0x914 },
            // Audio: SourceBlockSize=0x930, DataOffset=0, DataLength=0x930 (identity)
            new DataStride { SourceBlockSize = 0x930, DataOffset = 0, DataLength = 0x930 },
        };

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — CleanToOffset then OffsetToClean
        /// is identity for all valid clean offsets.
        ///
        /// For any valid clean offset (aligned to data within blocks), converting to a strided
        /// offset via CleanToOffset and back via OffsetToClean SHALL produce the original
        /// clean offset.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool CleanToOffset_ThenOffsetToClean_IsIdentity_ForAllStrides(NonNegativeInt sectorIndexInt, NonNegativeInt intraOffsetInt)
        {
            // Test across all recognized stride configurations
            foreach (DataStride stride in Iso9660Strides)
            {
                // Generate a valid clean offset: block-aligned start + intra-block offset within DataLength
                int sectorIndex = sectorIndexInt.Get % 1000; // Up to 1000 sectors
                int intraOffset = intraOffsetInt.Get % stride.DataLength; // Within data region

                long cleanOffset = ((long)sectorIndex * stride.DataLength) + intraOffset;

                // CleanToOffset with blockPin=false (standard usage for data access)
                long stridedOffset = stride.CleanToOffset(cleanOffset, false);

                // OffsetToClean should return the original clean offset
                long roundTripped = stride.OffsetToClean(stridedOffset);

                if (roundTripped != cleanOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — OffsetToClean then CleanToOffset
        /// is identity for offsets within the data region of each block.
        ///
        /// For any strided offset that falls within the data region of a block
        /// (i.e., offset % SourceBlockSize >= DataOffset and
        /// offset % SourceBlockSize &lt; DataOffset + DataLength),
        /// converting to clean via OffsetToClean and back via CleanToOffset SHALL produce
        /// the original strided offset.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool OffsetToClean_ThenCleanToOffset_IsIdentity_ForDataRegionOffsets(NonNegativeInt sectorIndexInt, NonNegativeInt intraDataOffsetInt)
        {
            foreach (DataStride stride in Iso9660Strides)
            {
                int sectorIndex = sectorIndexInt.Get % 1000;
                // Generate an offset within the data region of the block
                int intraDataOffset = intraDataOffsetInt.Get % stride.DataLength;

                // Strided offset pointing into the data region of a sector
                long stridedOffset = ((long)sectorIndex * stride.SourceBlockSize) + stride.DataOffset + intraDataOffset;

                // Convert to clean
                long cleanOffset = stride.OffsetToClean(stridedOffset);

                // Convert back to strided (blockPin=false for non-zero remainder, which is the common case)
                long roundTripped = stride.CleanToOffset(cleanOffset, false);

                if (roundTripped != stridedOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — Block-aligned clean offsets
        /// round-trip correctly.
        ///
        /// For any clean offset that is exactly at a block boundary (multiple of DataLength),
        /// CleanToOffset(cleanOffset, false) followed by OffsetToClean SHALL return the
        /// original clean offset.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool BlockAlignedCleanOffsets_RoundTrip_Correctly(NonNegativeInt sectorIndexInt)
        {
            foreach (DataStride stride in Iso9660Strides)
            {
                int sectorIndex = sectorIndexInt.Get % 1000;
                long cleanOffset = (long)sectorIndex * stride.DataLength;

                // CleanToOffset with blockPin=false for block-aligned offsets
                long stridedOffset = stride.CleanToOffset(cleanOffset, false);

                // Should point to the start of data within the block
                long expectedStrided = ((long)sectorIndex * stride.SourceBlockSize) + stride.DataOffset;
                if (stridedOffset != expectedStrided)
                    return false;

                // Round-trip back
                long roundTripped = stride.OffsetToClean(stridedOffset);
                if (roundTripped != cleanOffset)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — SourceBlockSize * N bytes
        /// of raw sector data contains exactly DataLength * N bytes of user data.
        ///
        /// For N complete sectors, the clean size computed from the strided size SHALL equal
        /// DataLength * N for each recognized stride configuration.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool NBlocks_ContainExactly_NTimesDataLength_CleanBytes(PositiveInt sectorCountInt)
        {
            foreach (DataStride stride in Iso9660Strides)
            {
                int sectorCount = (sectorCountInt.Get % 500) + 1;

                long stridedSize = (long)sectorCount * stride.SourceBlockSize;
                long expectedCleanSize = (long)sectorCount * stride.DataLength;

                // OffsetToClean of the end of N blocks should equal N * DataLength
                long computedCleanSize = stride.OffsetToClean(stridedSize);

                if (computedCleanSize != expectedCleanSize)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — Mode1Raw specific configuration.
        ///
        /// For Mode1Raw stride (SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800),
        /// OffsetToClean and CleanToOffset are mutual inverses for all valid offsets.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode1Raw_OffsetToClean_CleanToOffset_MutualInverses(NonNegativeInt cleanOffsetInt)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x930, DataOffset = 0x10, DataLength = 0x800 };
            long cleanOffset = (long)(cleanOffsetInt.Get % (1000 * stride.DataLength));

            long stridedOffset = stride.CleanToOffset(cleanOffset, false);
            long roundTripped = stride.OffsetToClean(stridedOffset);

            return roundTripped == cleanOffset;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — Mode2/Mode2Form1 specific configuration.
        ///
        /// For Mode2/Mode2Form1 stride (SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800),
        /// OffsetToClean and CleanToOffset are mutual inverses for all valid offsets.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form1_OffsetToClean_CleanToOffset_MutualInverses(NonNegativeInt cleanOffsetInt)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x930, DataOffset = 0x18, DataLength = 0x800 };
            long cleanOffset = (long)(cleanOffsetInt.Get % (1000 * stride.DataLength));

            long stridedOffset = stride.CleanToOffset(cleanOffset, false);
            long roundTripped = stride.OffsetToClean(stridedOffset);

            return roundTripped == cleanOffset;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — Mode2Form2 specific configuration.
        ///
        /// For Mode2Form2 stride (SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914),
        /// OffsetToClean and CleanToOffset are mutual inverses for all valid offsets.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form2_OffsetToClean_CleanToOffset_MutualInverses(NonNegativeInt cleanOffsetInt)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x930, DataOffset = 0x18, DataLength = 0x914 };
            long cleanOffset = (long)(cleanOffsetInt.Get % (1000 * stride.DataLength));

            long stridedOffset = stride.CleanToOffset(cleanOffset, false);
            long roundTripped = stride.OffsetToClean(stridedOffset);

            return roundTripped == cleanOffset;
        }

        /// <summary>
        /// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4**
        ///
        /// Property 3: ISO9660 Stride Computation Correctness — Audio stride (identity).
        ///
        /// For Audio stride (SourceBlockSize=0x930, DataOffset=0, DataLength=0x930),
        /// OffsetToClean and CleanToOffset are identity functions (no striding occurs).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Audio_OffsetToClean_CleanToOffset_AreIdentity(NonNegativeInt offsetInt)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x930, DataOffset = 0, DataLength = 0x930 };
            long offset = (long)(offsetInt.Get % (1000 * stride.DataLength));

            // For audio, SourceBlockSize == DataLength, so both methods should be identity
            long cleanOffset = stride.OffsetToClean(offset);
            long stridedOffset = stride.CleanToOffset(offset, false);

            return cleanOffset == offset && stridedOffset == offset;
        }
    }
}