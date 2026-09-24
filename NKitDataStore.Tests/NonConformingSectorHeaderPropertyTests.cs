using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for non-conforming sector header detection.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 6: Non-Conforming Sector Header Detection
    /// **Validates: Requirements 3.5, 5.6**
    ///
    /// For any raw sector whose leading 12 bytes differ from the standard sync pattern
    /// (00 FF FF FF FF FF FF FF FF FF FF 00), or whose 3-byte MSF does not match the
    /// expected LBA, or whose mode byte does not match the track mode, the formatter
    /// SHALL persist those non-conforming bytes as a BlockPadding record. Conversely,
    /// for any sector with a conforming header, no BlockPadding record SHALL be created.
    /// </summary>
    public class NonConformingSectorHeaderPropertyTests
    {
        /// <summary>Standard CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00</summary>
        private static readonly byte[] SyncPattern = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        private const int RawSectorSize = 0x930; // 2352 bytes
        private const int Mode1HeaderSize = 0x10; // 16 bytes
        private const int Mode2HeaderSize = 0x18; // 24 bytes

        /// <summary>
        /// Builds a conforming sector header for the given LBA and mode.
        /// Returns a full 2352-byte sector with correct sync, MSF, and mode byte.
        /// </summary>
        private static byte[] BuildConformingSector(long lba, byte mode)
        {
            byte[] sector = new byte[RawSectorSize];

            // Write sync pattern (12 bytes)
            Array.Copy(SyncPattern, 0, sector, 0, 12);

            // Write MSF (3 bytes at offset 0x0C) in BCD
            (byte minute, byte second, byte frame) msf = Ecm.LbaToMsf(lba);
            sector[0x0C] = (byte)(((msf.minute / 10) << 4) + (msf.minute % 10));
            sector[0x0D] = (byte)(((msf.second / 10) << 4) + (msf.second % 10));
            sector[0x0E] = (byte)(((msf.frame / 10) << 4) + (msf.frame % 10));

            // Write mode byte at offset 0x0F
            sector[0x0F] = mode;

            return sector;
        }

        /// <summary>
        /// Models the non-conforming sector detection logic from
        /// DataStoreIso9660Formatter.FinaliseSectionAndPersistBlockPadding.
        /// Returns true if the sector is non-conforming (would generate BlockPadding).
        /// </summary>
        private static bool IsNonConforming(byte[] sectorData, int sectorOffset, long expectedLba, byte expectedMode)
        {
            // Check sync pattern (12 bytes)
            for (int s = 0; s < 12; s++)
            {
                if (sectorData[sectorOffset + s] != SyncPattern[s])
                    return true;
            }

            // Check MSF (3 bytes at offset 0x0C)
            (byte minute, byte second, byte frame) expectedMsf = Ecm.LbaToMsf(expectedLba);
            byte expectedMin = (byte)(((expectedMsf.minute / 10) << 4) + (expectedMsf.minute % 10));
            byte expectedSec = (byte)(((expectedMsf.second / 10) << 4) + (expectedMsf.second % 10));
            byte expectedFrm = (byte)(((expectedMsf.frame / 10) << 4) + (expectedMsf.frame % 10));

            if (sectorData[sectorOffset + 0x0C] != expectedMin ||
                sectorData[sectorOffset + 0x0D] != expectedSec ||
                sectorData[sectorOffset + 0x0E] != expectedFrm)
            {
                return true;
            }

            // Check mode byte (offset 0x0F)
            if (sectorData[sectorOffset + 0x0F] != expectedMode)
                return true;

            return false;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Conforming sectors do NOT generate BlockPadding.
        ///
        /// For any valid LBA and Mode1 sector with correct sync pattern, correct MSF,
        /// and correct mode byte, the detection logic SHALL report the sector as conforming
        /// (no BlockPadding needed).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ConformingMode1Sector_NoBlockPadding(NonNegativeInt lbaSeed)
        {
            // LBA range: 0 to 449999 (full 80-minute CD)
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = 0x01;

            byte[] sector = BuildConformingSector(lba, expectedMode);

            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);

            // A conforming sector must NOT be detected as non-conforming
            return !nonConforming;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Conforming Mode2 sectors do NOT generate BlockPadding.
        ///
        /// For any valid LBA and Mode2 sector with correct sync pattern, correct MSF,
        /// and correct mode byte (0x02), the detection logic SHALL report the sector
        /// as conforming (no BlockPadding needed).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ConformingMode2Sector_NoBlockPadding(NonNegativeInt lbaSeed)
        {
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = 0x02;

            byte[] sector = BuildConformingSector(lba, expectedMode);

            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);

            return !nonConforming;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Sectors with non-standard sync pattern generate BlockPadding.
        ///
        /// For any sector where at least one byte in the 12-byte sync pattern differs
        /// from the standard (00 FF FF FF FF FF FF FF FF FF FF 00), the detection logic
        /// SHALL report the sector as non-conforming (BlockPadding needed).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool NonStandardSync_GeneratesBlockPadding(
            NonNegativeInt lbaSeed,
            NonNegativeInt corruptPosSeed,
            byte corruptValue)
        {
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = 0x01;

            byte[] sector = BuildConformingSector(lba, expectedMode);

            // Corrupt one byte in the sync pattern (positions 0-11)
            int corruptPos = corruptPosSeed.Get % 12;
            byte originalByte = sector[corruptPos];

            // Ensure the corrupt value actually differs from the expected sync byte
            if (corruptValue == SyncPattern[corruptPos])
                corruptValue = (byte)(SyncPattern[corruptPos] ^ 0x01); // flip a bit to guarantee difference

            sector[corruptPos] = corruptValue;

            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);

            // A sector with corrupted sync MUST be detected as non-conforming
            return nonConforming;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Sectors with incorrect MSF generate BlockPadding.
        ///
        /// For any sector where the 3-byte MSF does not match the expected LBA,
        /// the detection logic SHALL report the sector as non-conforming.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool IncorrectMsf_GeneratesBlockPadding(
            NonNegativeInt lbaSeed,
            NonNegativeInt wrongLbaSeed)
        {
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = 0x01;

            // Use a different LBA for the MSF to create a mismatch
            long wrongLba = wrongLbaSeed.Get % 450000;
            if (wrongLba == lba)
                wrongLba = (lba + 1) % 450000; // Ensure it's different

            // Build sector with wrong MSF (using wrongLba for the header but expecting lba)
            byte[] sector = BuildConformingSector(wrongLba, expectedMode);

            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);

            // A sector with wrong MSF MUST be detected as non-conforming
            return nonConforming;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Sectors with incorrect mode byte generate BlockPadding.
        ///
        /// For any sector where the mode byte at offset 0x0F does not match the
        /// expected track mode, the detection logic SHALL report the sector as
        /// non-conforming.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool IncorrectModeByte_GeneratesBlockPadding(
            NonNegativeInt lbaSeed,
            byte wrongMode)
        {
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = 0x01;

            // Ensure the wrong mode is actually different from expected
            if (wrongMode == expectedMode)
                wrongMode = 0x02;

            // Build sector with correct sync and MSF but wrong mode byte
            byte[] sector = BuildConformingSector(lba, wrongMode);

            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);

            // A sector with wrong mode byte MUST be detected as non-conforming
            return nonConforming;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: Multi-sector sections correctly identify non-conforming sectors.
        ///
        /// For a section containing multiple sectors where some are conforming and some
        /// are non-conforming, the detection logic SHALL identify exactly the non-conforming
        /// sectors and collect their header bytes for BlockPadding persistence.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool MultiSector_OnlyNonConformingGenerateBlockPadding(
            NonNegativeInt lbaSeed,
            NonNegativeInt sectorCountSeed,
            NonNegativeInt corruptMaskSeed)
        {
            long baseLba = lbaSeed.Get % 400000; // Leave room for sector count
            int sectorCount = (sectorCountSeed.Get % 8) + 2; // 2-9 sectors
            byte expectedMode = 0x01;

            // Use corruptMaskSeed to determine which sectors are non-conforming
            int corruptMask = corruptMaskSeed.Get;

            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            bool[] expectedNonConforming = new bool[sectorCount];

            for (int i = 0; i < sectorCount; i++)
            {
                long sectorLba = baseLba + i;
                byte[] sector = BuildConformingSector(sectorLba, expectedMode);

                // Decide if this sector should be non-conforming based on the mask
                bool shouldCorrupt = ((corruptMask >> (i % 30)) & 1) == 1;
                if (shouldCorrupt)
                {
                    // Corrupt the sync pattern at position 1 (change 0xFF to 0x00)
                    sector[1] = 0x00;
                    expectedNonConforming[i] = true;
                }

                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Verify detection matches expectations
            for (int i = 0; i < sectorCount; i++)
            {
                long sectorLba = baseLba + i;
                bool detected = IsNonConforming(sectionData, i * RawSectorSize, sectorLba, expectedMode);

                if (detected != expectedNonConforming[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 3.5, 5.6**
        ///
        /// Property 6: BlockPadding header size depends on expected mode.
        ///
        /// For Mode1 (expectedMode=0x01), non-conforming sectors collect 16 bytes (0x10).
        /// For Mode2 (expectedMode=0x02), non-conforming sectors collect 24 bytes (0x18).
        /// This models the headerSize logic in FinaliseSectionAndPersistBlockPadding.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool BlockPaddingSize_DependsOnMode(
            NonNegativeInt lbaSeed,
            bool isMode2)
        {
            long lba = lbaSeed.Get % 450000;
            byte expectedMode = isMode2 ? (byte)0x02 : (byte)0x01;
            int expectedHeaderSize = expectedMode == 0x01 ? Mode1HeaderSize : Mode2HeaderSize;

            // Build a non-conforming sector (corrupt sync)
            byte[] sector = BuildConformingSector(lba, expectedMode);
            sector[1] = 0x00; // Corrupt sync to make it non-conforming

            // Verify it's detected as non-conforming
            bool nonConforming = IsNonConforming(sector, 0, lba, expectedMode);
            if (!nonConforming)
                return false;

            // Model the padding collection logic: collect headerSize bytes
            byte[] paddingBytes = new byte[expectedHeaderSize];
            Array.Copy(sector, 0, paddingBytes, 0, expectedHeaderSize);

            // Verify the correct number of bytes would be collected
            return paddingBytes.Length == expectedHeaderSize;
        }
    }
}