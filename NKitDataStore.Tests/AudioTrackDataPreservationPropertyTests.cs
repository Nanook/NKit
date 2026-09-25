using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for audio track data preservation.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 8: Audio Track Data Preservation
    /// **Validates: Requirements 7.4, 7.5**
    ///
    /// For any audio track data (2352 bytes per sector), ingesting into the DataStore
    /// with DataStride { SourceBlockSize=0x930, DataOffset=0, DataLength=0x930 } and then
    /// reconstructing SHALL produce byte-for-byte identical output to the original input.
    ///
    /// The stride's OffsetToClean and CleanToOffset are identity functions when
    /// DataOffset=0 and DataLength=SourceBlockSize, meaning the full sector is stored
    /// verbatim with no stripping or regeneration.
    /// </summary>
    public class AudioTrackDataPreservationPropertyTests
    {
        /// <summary>
        /// The audio track DataStride: full 2352-byte sector stored verbatim.
        /// SourceBlockSize=0x930 (2352), DataOffset=0, DataLength=0x930 (2352).
        /// </summary>
        private static readonly DataStride AudioStride = new DataStride
        {
            SourceBlockSize = 0x930,
            DataOffset = 0,
            DataLength = 0x930
        };

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — OffsetToClean is identity.
        ///
        /// For any valid offset within audio track data, OffsetToClean returns the
        /// same offset unchanged, confirming that no data is stripped during ingestion.
        /// </summary>
        [Property(MaxTest = 1000)]
        public bool AudioStride_OffsetToClean_IsIdentity(NonNegativeInt offsetSeed)
        {
            // Generate offsets that are multiples of sector size and arbitrary positions within sectors
            long offset = (long)offsetSeed.Get;

            long cleanOffset = AudioStride.OffsetToClean(offset);

            return cleanOffset == offset;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — CleanToOffset is identity.
        ///
        /// For any valid clean offset, CleanToOffset returns the same offset unchanged,
        /// confirming that reconstruction produces data at the same positions as the original.
        /// </summary>
        [Property(MaxTest = 1000)]
        public bool AudioStride_CleanToOffset_IsIdentity(NonNegativeInt offsetSeed, bool blockPin)
        {
            long offset = (long)offsetSeed.Get;

            long stridedOffset = AudioStride.CleanToOffset(offset, blockPin);

            return stridedOffset == offset;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — Round-trip identity.
        ///
        /// For any offset, applying OffsetToClean followed by CleanToOffset (and vice versa)
        /// produces the original offset, confirming lossless round-trip for audio data.
        /// </summary>
        [Property(MaxTest = 1000)]
        public bool AudioStride_RoundTrip_IsIdentity(NonNegativeInt offsetSeed)
        {
            long offset = (long)offsetSeed.Get;

            // OffsetToClean then CleanToOffset
            long clean = AudioStride.OffsetToClean(offset);
            long backToStrided = AudioStride.CleanToOffset(clean, false);

            if (backToStrided != offset)
                return false;

            // CleanToOffset then OffsetToClean
            long strided = AudioStride.CleanToOffset(offset, false);
            long backToClean = AudioStride.OffsetToClean(strided);

            return backToClean == offset;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — GetCleanSize preserves full sector size.
        ///
        /// For any number of audio sectors, the clean size equals the strided size
        /// (no data is removed), confirming that the full 2352 bytes per sector are preserved.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool AudioStride_GetCleanSize_PreservesFullSectorSize(PositiveInt sectorCountSeed)
        {
            int sectorCount = (sectorCountSeed.Get % 100) + 1;
            long stridedSize = (long)sectorCount * 0x930;

            long cleanSize = AudioStride.GetCleanSize(0, stridedSize);

            return cleanSize == stridedSize;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — GetStridedSize preserves full sector size.
        ///
        /// For any clean data size (multiple of sector size), the strided size equals
        /// the clean size (no padding is added), confirming byte-identical output.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool AudioStride_GetStridedSize_PreservesFullSectorSize(PositiveInt sectorCountSeed)
        {
            int sectorCount = (sectorCountSeed.Get % 100) + 1;
            long cleanSize = (long)sectorCount * 0x930;

            long stridedSize = AudioStride.GetStridedSize(0, cleanSize);

            return stridedSize == cleanSize;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — Zero padding overhead.
        ///
        /// For any audio track data size, the padding overhead is zero, confirming
        /// that no bytes are added or removed during the stride transformation.
        /// </summary>
        [Property(MaxTest = 500)]
        public bool AudioStride_PaddingOverhead_IsZero(PositiveInt sectorCountSeed)
        {
            int sectorCount = (sectorCountSeed.Get % 100) + 1;
            long cleanSize = (long)sectorCount * 0x930;

            long overhead = AudioStride.GetPaddingOverhead(0, cleanSize);

            return overhead == 0;
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — Efficiency is 1.0.
        ///
        /// The audio stride has 100% efficiency (DataLength == SourceBlockSize),
        /// meaning all bytes in the sector are preserved as clean data.
        /// </summary>
        [Fact]
        public void AudioStride_Efficiency_IsOne()
        {
            double efficiency = AudioStride.GetEfficiency();

            Assert.Equal(1.0, efficiency);
        }

        /// <summary>
        /// **Validates: Requirements 7.4, 7.5**
        ///
        /// Property 8: Audio Track Data Preservation — Arbitrary sector data round-trips.
        ///
        /// For any arbitrary 2352-byte audio sector data, the stride transformation
        /// preserves byte positions exactly: data at position P in the source maps to
        /// position P in the clean output (identity mapping).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool AudioStride_ArbitrarySectorData_BytePositionsPreserved(
            NonNegativeInt sectorIndexSeed,
            NonNegativeInt bytePositionSeed)
        {
            int sectorIndex = sectorIndexSeed.Get % 1000;
            int byteWithinSector = bytePositionSeed.Get % 0x930;

            // The absolute offset of a byte within the audio track
            long absoluteOffset = ((long)sectorIndex * 0x930) + byteWithinSector;

            // OffsetToClean should map this to the same position (identity)
            long cleanPosition = AudioStride.OffsetToClean(absoluteOffset);

            // CleanToOffset should map back to the same position (identity)
            long reconstructedPosition = AudioStride.CleanToOffset(cleanPosition, false);

            return cleanPosition == absoluteOffset && reconstructedPosition == absoluteOffset;
        }
    }
}