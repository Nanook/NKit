using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for the Sector Padding Pack feature.
    ///
    /// Feature: sector-padding-pack
    /// </summary>
    public class SectorPaddingPackPropertyTests
    {
        private const int RawSectorSize = 0x930; // 2352 bytes
        private const int MaxLba = 449999; // Full 80-minute CD

        /// <summary>Standard CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00</summary>
        private static readonly byte[] SyncPattern = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        /// <summary>
        /// Builds a fully conforming Mode1 sector at the given LBA.
        /// The sector has the provided user data (2048 bytes at offset 0x10),
        /// then ReconstructPrefix sets correct sync + MSF + mode,
        /// and ReconstructEcc sets correct EDC + reserved zeros + ECC.
        /// </summary>
        private static byte[] BuildConformingMode1Sector(long lba, byte[] userData)
        {
            byte[] sector = new byte[RawSectorSize];

            // Place user data at offset 0x10 (2048 bytes)
            int userDataLength = Math.Min(userData.Length, 2048);
            Array.Copy(userData, 0, sector, 0x10, userDataLength);

            // Set correct sync + MSF + mode via ReconstructPrefix
            Ecm.ReconstructPrefix(sector, 0, true, lba);

            // Set correct EDC + reserved + ECC via ReconstructEcc
            Ecm.ReconstructEcc(sector, 0, true, false, false);

            return sector;
        }

        /// <summary>
        /// Builds a conforming Mode2Form1 sector at the given LBA with the specified subheader bytes.
        /// Mode byte at 0x0F = 0x02, subheader at 0x10-0x17 with submode byte (0x12) having bit 5 clear.
        /// Applies Ecm.ReconstructPrefix and Ecm.ReconstructEcc.
        /// </summary>
        private static byte[] BuildMode2Form1Sector(long lba, byte[] subheaderBytes)
        {
            byte[] sector = new byte[RawSectorSize];

            // Fill user data area with some pattern (offset 0x18 for Mode2Form1, 2048 bytes)
            for (int i = 0x18; i < 0x818; i++)
                sector[i] = (byte)(i & 0xFF);

            // Set mode byte
            sector[0x0F] = 0x02;

            // Write subheader (8 bytes at 0x10-0x17)
            // Ensure submode byte (0x12) has bit 5 clear (not Form2)
            Array.Copy(subheaderBytes, 0, sector, 0x10, 8);
            sector[0x12] &= 0xDF; // Clear bit 5 to ensure Form1
            // Duplicate subheader at 0x14 (Mode2 subheader is repeated)
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Apply ReconstructPrefix (writes sync + MSF + mode, and copies subheader 0x14->0x10 for mode2)
            Ecm.ReconstructPrefix(sector, 0, false, lba);

            // Apply ReconstructEcc (computes EDC and ECC)
            Ecm.ReconstructEcc(sector, 0, false, true, false);

            return sector;
        }

        /// <summary>
        /// Builds a conforming Mode2Form2 sector at the given LBA with the specified subheader
        /// and extended user data bytes.
        /// Mode byte at 0x0F = 0x02, subheader at 0x10-0x17 with submode byte (0x12) having bit 5 set.
        /// Applies Ecm.ReconstructPrefix and Ecm.ReconstructEcc.
        /// </summary>
        private static byte[] BuildMode2Form2Sector(long lba, byte[] subheaderBytes, byte[] extUserData)
        {
            byte[] sector = new byte[RawSectorSize];

            // Fill user data area (offset 0x18, 2048 bytes)
            for (int i = 0x18; i < 0x818; i++)
                sector[i] = (byte)(i & 0xFF);

            // Write extended user data (280 bytes at offset 0x818-0x92B)
            Array.Copy(extUserData, 0, sector, 0x818, 280);

            // Set mode byte
            sector[0x0F] = 0x02;

            // Write subheader (8 bytes at 0x10-0x17)
            // Ensure submode byte (0x12) has bit 5 set (Form2)
            Array.Copy(subheaderBytes, 0, sector, 0x10, 8);
            sector[0x12] |= 0x20; // Set bit 5 to ensure Form2
            // Duplicate subheader at 0x14
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Apply ReconstructPrefix (writes sync + MSF + mode, and copies subheader 0x14->0x10 for mode2)
            Ecm.ReconstructPrefix(sector, 0, false, lba);

            // Apply ReconstructEcc (computes EDC for Mode2Form2)
            Ecm.ReconstructEcc(sector, 0, false, false, true);

            return sector;
        }

        /// <summary>
        /// Parses the pack output to extract the flag byte for the first (and only) sector.
        /// Returns the flag byte, or null if the sector has no presence bit set.
        /// </summary>
        private static byte? GetFlagByteForSingleSector(byte[] packData)
        {
            // Pack format: [version 1][sectorCount 2 LE][bitmap ceil(N/8)][flag+payload...]
            // For 1 sector: bitmap is 1 byte
            if (packData == null || packData.Length < 4)
                return null;

            byte bitmap = packData[3]; // bitmap byte for sector 0
            if ((bitmap & 0x01) == 0)
                return null; // presence bit not set

            // Flag byte is immediately after the bitmap
            return packData[4];
        }

        /// <summary>
        /// Extracts the payload bytes after the flag byte for a single-sector pack.
        /// </summary>
        private static byte[] GetPayloadForSingleSector(byte[] packData)
        {
            // Header: version(1) + sectorCount(2) + bitmap(1) = 4 bytes
            // Then flag byte(1) + payload
            if (packData == null || packData.Length <= 5)
                return Array.Empty<byte>();

            byte[] payload = new byte[packData.Length - 5];
            Array.Copy(packData, 5, payload, 0, payload.Length);
            return payload;
        }

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4, 5.2, 5.3**
        ///
        /// Property 5: Mode2 Unconditional Component Storage - Mode2Form1 subheader.
        ///
        /// For any Mode2Form1 sector with conforming sync/MSF/EDC/ECC, the Packer SHALL
        /// set the presence bit, set bit 4 (Subheader) in the flag byte, and store the
        /// 8-byte subheader in the payload.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form1_AlwaysStoresSubheader_WhenConforming(NonNegativeInt lbaSeed, byte[] subheaderRaw)
        {
            long lba = lbaSeed.Get % 450000;

            // Ensure we have 8 bytes for subheader
            byte[] subheader = new byte[8];
            if (subheaderRaw != null && subheaderRaw.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheader[i] = subheaderRaw[i % subheaderRaw.Length];
            }

            byte[] sector = BuildMode2Form1Sector(lba, subheader);
            byte[] packData = SectorPaddingPacker.Pack(sector, 1, lba);

            if (packData == null)
                return false;

            // Presence bit must be set (Mode2Form1 always has subheader to store)
            byte bitmap = packData[3];
            if ((bitmap & 0x01) == 0)
                return false;

            // Flag byte must have bit 4 (Subheader) set
            byte? flagByte = GetFlagByteForSingleSector(packData);
            if (flagByte == null)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.Subheader) == 0)
                return false;

            // Sector type bits should be Mode2Form1 (0x01)
            if (((byte)flagByte & (byte)SectorFlags.TypeMask) != (byte)SectorFlags.Mode2Form1)
                return false;

            // The payload must contain the 8-byte subheader
            // Payload starts after flag byte. Since this is a conforming sector,
            // only bit 4 (Subheader) should be set among component bits.
            // The subheader payload is the 8 bytes from sector offset 0x10.
            byte[] payload = GetPayloadForSingleSector(packData);
            if (payload.Length < 8)
                return false;

            // Verify the stored subheader matches the sector's subheader
            for (int i = 0; i < 8; i++)
            {
                if (payload[i] != sector[0x10 + i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4, 5.2, 5.3**
        ///
        /// Property 5: Mode2 Unconditional Component Storage - Mode2Form2 subheader and extended user data.
        ///
        /// For any Mode2Form2 sector with conforming sync/MSF/EDC, the Packer SHALL
        /// set the presence bit, set bit 4 (Subheader) AND bit 7 (ExtUserData) in the flag byte,
        /// and store both the 8-byte subheader and 280-byte extended user data in the payload.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form2_AlwaysStoresSubheaderAndExtUserData_WhenConforming(
            NonNegativeInt lbaSeed, byte[] subheaderRaw, byte[] extUserDataRaw)
        {
            long lba = lbaSeed.Get % 450000;

            // Ensure we have 8 bytes for subheader
            byte[] subheader = new byte[8];
            if (subheaderRaw != null && subheaderRaw.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheader[i] = subheaderRaw[i % subheaderRaw.Length];
            }

            // Ensure we have 280 bytes for extended user data
            byte[] extUserData = new byte[280];
            if (extUserDataRaw != null && extUserDataRaw.Length > 0)
            {
                for (int i = 0; i < 280; i++)
                    extUserData[i] = extUserDataRaw[i % extUserDataRaw.Length];
            }

            byte[] sector = BuildMode2Form2Sector(lba, subheader, extUserData);
            byte[] packData = SectorPaddingPacker.Pack(sector, 1, lba);

            if (packData == null)
                return false;

            // Presence bit must be set
            byte bitmap = packData[3];
            if ((bitmap & 0x01) == 0)
                return false;

            // Flag byte must have bit 4 (Subheader) AND bit 7 (ExtUserData) set
            byte? flagByte = GetFlagByteForSingleSector(packData);
            if (flagByte == null)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.Subheader) == 0)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.ExtUserData) == 0)
                return false;

            // Sector type bits should be Mode2Form2 (0x02)
            if (((byte)flagByte & (byte)SectorFlags.TypeMask) != (byte)SectorFlags.Mode2Form2)
                return false;

            // The payload must contain 8-byte subheader + 280-byte extended user data = 288 bytes
            byte[] payload = GetPayloadForSingleSector(packData);
            if (payload.Length < 288)
                return false;

            // Verify the stored subheader matches the sector's subheader (first 8 bytes of payload)
            for (int i = 0; i < 8; i++)
            {
                if (payload[i] != sector[0x10 + i])
                    return false;
            }

            // Verify the stored extended user data matches (next 280 bytes of payload)
            for (int i = 0; i < 280; i++)
            {
                if (payload[8 + i] != sector[0x818 + i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4, 5.2, 5.3**
        ///
        /// Property 5: Mode2 Unconditional Component Storage - regardless of non-conforming components.
        ///
        /// For any Mode2Form1 sector where sync and/or MSF are non-conforming,
        /// the Packer SHALL still set bit 4 (Subheader) and store the 8-byte subheader.
        /// The subheader storage is unconditional regardless of other component conformance.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form1_StoresSubheader_EvenWhenOtherComponentsNonConforming(
            NonNegativeInt lbaSeed, byte[] subheaderRaw, bool corruptSync, bool corruptMsf)
        {
            long lba = lbaSeed.Get % 450000;

            byte[] subheader = new byte[8];
            if (subheaderRaw != null && subheaderRaw.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheader[i] = subheaderRaw[i % subheaderRaw.Length];
            }

            byte[] sector = BuildMode2Form1Sector(lba, subheader);

            // Corrupt sync if requested
            if (corruptSync)
                sector[1] = (byte)(sector[1] ^ 0xFF); // Flip sync byte

            // Corrupt MSF if requested
            if (corruptMsf)
                sector[0x0C] = (byte)(sector[0x0C] ^ 0xFF); // Flip MSF byte

            byte[] packData = SectorPaddingPacker.Pack(sector, 1, lba);

            if (packData == null)
                return false;

            // Presence bit must be set
            byte bitmap = packData[3];
            if ((bitmap & 0x01) == 0)
                return false;

            // Flag byte must have bit 4 (Subheader) set regardless of other corruption
            byte? flagByte = GetFlagByteForSingleSector(packData);
            if (flagByte == null)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.Subheader) == 0)
                return false;

            // Sector type bits should still be Mode2Form1 (0x01)
            if (((byte)flagByte & (byte)SectorFlags.TypeMask) != (byte)SectorFlags.Mode2Form1)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 2.3, 2.4, 5.2, 5.3**
        ///
        /// Property 5: Mode2 Unconditional Component Storage - Mode2Form2 regardless of non-conforming components.
        ///
        /// For any Mode2Form2 sector where sync and/or MSF are non-conforming,
        /// the Packer SHALL still set bit 4 (Subheader) AND bit 7 (ExtUserData) and store
        /// both the 8-byte subheader and 280-byte extended user data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form2_StoresSubheaderAndExtUserData_EvenWhenOtherComponentsNonConforming(
            NonNegativeInt lbaSeed, byte[] subheaderRaw, byte[] extUserDataRaw, bool corruptSync, bool corruptMsf)
        {
            long lba = lbaSeed.Get % 450000;

            byte[] subheader = new byte[8];
            if (subheaderRaw != null && subheaderRaw.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheader[i] = subheaderRaw[i % subheaderRaw.Length];
            }

            byte[] extUserData = new byte[280];
            if (extUserDataRaw != null && extUserDataRaw.Length > 0)
            {
                for (int i = 0; i < 280; i++)
                    extUserData[i] = extUserDataRaw[i % extUserDataRaw.Length];
            }

            byte[] sector = BuildMode2Form2Sector(lba, subheader, extUserData);

            // Corrupt sync if requested
            if (corruptSync)
                sector[1] = (byte)(sector[1] ^ 0xFF);

            // Corrupt MSF if requested
            if (corruptMsf)
                sector[0x0C] = (byte)(sector[0x0C] ^ 0xFF);

            byte[] packData = SectorPaddingPacker.Pack(sector, 1, lba);

            if (packData == null)
                return false;

            // Presence bit must be set
            byte bitmap = packData[3];
            if ((bitmap & 0x01) == 0)
                return false;

            // Flag byte must have bit 4 (Subheader) AND bit 7 (ExtUserData) set
            byte? flagByte = GetFlagByteForSingleSector(packData);
            if (flagByte == null)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.Subheader) == 0)
                return false;

            if (((byte)flagByte & (byte)SectorFlags.ExtUserData) == 0)
                return false;

            // Sector type bits should still be Mode2Form2 (0x02)
            if (((byte)flagByte & (byte)SectorFlags.TypeMask) != (byte)SectorFlags.Mode2Form2)
                return false;

            return true;
        }

        // ===================================================================
        // Property 4: Conforming Sectors Produce Zero Storage
        // ===================================================================

        /// <summary>
        /// **Validates: Requirements 1.4, 2.2, 2.9, 3.3**
        ///
        /// Property 4: Conforming Sectors Produce Zero Storage.
        ///
        /// For any fully conforming Mode1 sector (where sync, MSF, mode, EDC, and ECC
        /// all match computed values), the corresponding bit in the Presence_Bitmap SHALL
        /// be zero and no Flag_Byte or payload bytes SHALL be stored for that sector.
        /// Consequently, for any section where all sectors are fully conforming Mode1,
        /// the pack SHALL consist of only the version byte and Pack_Header with zero
        /// payload bytes.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ConformingMode1Sectors_ProduceZeroStorage(
            NonNegativeInt lbaSeed,
            NonNegativeInt sectorCountSeed,
            byte[] userDataSeed)
        {
            // LBA range: 0 to 449999 (full 80-minute CD)
            long startLba = lbaSeed.Get % (MaxLba + 1);

            // Sector count: 1 to 16
            int sectorCount = (sectorCountSeed.Get % 16) + 1;

            // Ensure we have user data to work with
            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0xAB, 0xCD, 0xEF, 0x01 };

            // Build section data with all conforming Mode1 sectors
            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
            {
                // Vary user data per sector by XORing with sector index
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ (byte)i);

                byte[] sector = BuildConformingMode1Sector(startLba + i, sectorUserData);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Pack the section
            byte[] pack = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);

            if (pack == null)
                return false;

            // Expected pack size: 1 (version) + 2 (sector count) + ceil(sectorCount/8) (bitmap)
            int bitmapLength = (sectorCount + 7) / 8;
            int expectedSize = 1 + 2 + bitmapLength;

            // Verify pack size equals header only (no flag bytes or payload)
            if (pack.Length != expectedSize)
                return false;

            // Verify version byte is 0x01
            if (pack[0] != 0x01)
                return false;

            // Verify sector count is correct (little-endian)
            int storedSectorCount = pack[1] | (pack[2] << 8);
            if (storedSectorCount != sectorCount)
                return false;

            // Verify all presence bitmap bits are zero
            for (int i = 0; i < bitmapLength; i++)
            {
                if (pack[3 + i] != 0x00)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.4, 2.2, 2.9, 3.3**
        ///
        /// Property 4: Conforming Sectors Produce Zero Storage — single sector minimal pack.
        ///
        /// For a single fully conforming Mode1 sector, the pack SHALL be exactly
        /// 4 bytes: 1 (version) + 2 (count=1) + 1 (bitmap with bit 0 clear).
        /// </summary>
        [Property(MaxTest = 200)]
        public bool SingleConformingMode1Sector_ProducesMinimalPack(
            NonNegativeInt lbaSeed,
            byte[] userDataSeed)
        {
            long lba = lbaSeed.Get % (MaxLba + 1);

            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0x42 };

            // Pad or truncate to 2048 bytes
            byte[] sectorUserData = new byte[2048];
            for (int j = 0; j < 2048; j++)
                sectorUserData[j] = userData[j % userData.Length];

            byte[] sector = BuildConformingMode1Sector(lba, sectorUserData);

            byte[] pack = SectorPaddingPacker.Pack(sector, 1, lba);

            if (pack == null)
                return false;

            // Expected: 1 (version) + 2 (count) + 1 (bitmap) = 4 bytes
            if (pack.Length != 4)
                return false;

            // Bitmap byte must be zero (no stored data)
            if (pack[3] != 0x00)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.4, 2.2, 2.9, 3.3**
        ///
        /// Property 4: Conforming Sectors Produce Zero Storage — 16 sectors produce 5-byte pack.
        ///
        /// For exactly 16 fully conforming Mode1 sectors, the pack SHALL be exactly
        /// 5 bytes: 1 (version) + 2 (count=16) + 2 (bitmap with all bits clear).
        /// This matches the design document size example.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool SixteenConformingMode1Sectors_Produce5BytePack(
            NonNegativeInt lbaSeed,
            byte[] userDataSeed)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);

            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0x13, 0x37 };

            byte[] sectionData = new byte[16 * RawSectorSize];
            for (int i = 0; i < 16; i++)
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ (byte)i);

                byte[] sector = BuildConformingMode1Sector(startLba + i, sectorUserData);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            byte[] pack = SectorPaddingPacker.Pack(sectionData, 16, startLba);

            if (pack == null)
                return false;

            // Expected: 1 (version) + 2 (count=16) + 2 (bitmap) = 5 bytes
            if (pack.Length != 5)
                return false;

            // Both bitmap bytes must be zero
            if (pack[3] != 0x00 || pack[4] != 0x00)
                return false;

            return true;
        }

        // ===================================================================
        // Property 6: Unsupported Version Rejection
        // ===================================================================

        /// <summary>
        /// **Validates: Requirements 1.3**
        ///
        /// Property 6: Unsupported Version Rejection (ReadHeader)
        ///
        /// For any byte value other than 0x01 used as the version byte in a
        /// Sector_Padding_Pack, the Unpacker SHALL throw an error indicating an
        /// unsupported format version rather than attempting to parse the remaining data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool ReadHeader_ThrowsForUnsupportedVersion(byte versionByte)
        {
            // Only test version bytes that are NOT the supported version (0x01)
            if (versionByte == 0x01)
                return true; // Skip the supported version — not under test

            // Build a minimal pack data array with the invalid version byte
            // followed by valid-looking header data (sector count = 1, bitmap = 0x00)
            byte[] packData = new byte[]
            {
                versionByte, // invalid version byte
                0x01, 0x00, // sector count = 1 (little-endian)
                0x00        // bitmap byte (1 sector, bit clear)
            };

            try
            {
                SectorPaddingUnpacker.ReadHeader(packData, out _, out _);
                // If we get here, the method did NOT throw — property violated
                return false;
            }
            catch (InvalidDataException)
            {
                // Expected: unsupported version throws InvalidDataException
                return true;
            }
        }

        /// <summary>
        /// **Validates: Requirements 1.3**
        ///
        /// Property 6: Unsupported Version Rejection (Unpack)
        ///
        /// For any byte value other than 0x01 used as the version byte in a
        /// Sector_Padding_Pack, the Unpack method SHALL also throw an error
        /// indicating an unsupported format version.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Unpack_ThrowsForUnsupportedVersion(byte versionByte)
        {
            // Only test version bytes that are NOT the supported version (0x01)
            if (versionByte == 0x01)
                return true; // Skip the supported version — not under test

            // Build a minimal pack data array with the invalid version byte
            // followed by valid-looking header data (sector count = 1, bitmap = 0x00)
            byte[] packData = new byte[]
            {
                versionByte, // invalid version byte
                0x01, 0x00, // sector count = 1 (little-endian)
                0x00        // bitmap byte (1 sector, bit clear)
            };

            // Provide a buffer large enough for 1 sector (2352 bytes)
            byte[] buffer = new byte[RawSectorSize];

            try
            {
                SectorPaddingUnpacker.Unpack(packData, buffer, 0, 1);
                // If we get here, the method did NOT throw — property violated
                return false;
            }
            catch (InvalidDataException)
            {
                // Expected: unsupported version throws InvalidDataException
                return true;
            }
        }

        // ===================================================================
        // Property 1: Pack/Unpack Round-Trip Integrity
        // ===================================================================

        /// <summary>
        /// Enum representing the type of sector to generate for round-trip testing.
        /// </summary>
        private enum SectorType { Mode1Conforming, Mode2Form1, Mode2Form2, Mode1Corrupted }

        /// <summary>
        /// Builds a sector with selectively corrupted components for round-trip testing.
        /// Starts with a conforming Mode1 sector, then corrupts the specified components.
        /// </summary>
        private static byte[] BuildCorruptedMode1Sector(long lba, byte[] userData, bool corruptSync, bool corruptMsf, bool corruptEdc, bool corruptEcc)
        {
            byte[] sector = BuildConformingMode1Sector(lba, userData);

            if (corruptSync)
            {
                // Corrupt a sync byte (make it non-standard)
                sector[1] = (byte)(sector[1] ^ 0xAA);
            }

            if (corruptMsf)
            {
                // Corrupt MSF byte
                sector[0x0C] = (byte)(sector[0x0C] ^ 0x55);
            }

            if (corruptEdc)
            {
                // Corrupt EDC (4 bytes at 0x810 for Mode1)
                sector[0x810] = (byte)(sector[0x810] ^ 0xFF);
            }

            if (corruptEcc)
            {
                // Corrupt ECC (at 0x814 for Mode1, within the reserved+ECC region)
                sector[0x81C] = (byte)(sector[0x81C] ^ 0xFF);
            }

            return sector;
        }

        /// <summary>
        /// Simulates the reconstruction pipeline for a single sector:
        /// 1. Copies user data into a fresh buffer
        /// 2. Applies ReconstructPrefix (sync + MSF + mode)
        /// 3. Applies ReconstructEcc (EDC + ECC)
        /// This produces the "regenerated" buffer that the Unpacker overlays onto.
        /// </summary>
        private static void RegenerateSector(byte[] originalSector, byte[] buffer, int bufferOffset, long lba)
        {
            // Determine sector type from original data
            byte modeByte = originalSector[0x0F];
            bool isMode1 = modeByte != 0x02;
            bool isMode2Form1 = false;
            bool isMode2Form2 = false;

            if (modeByte == 0x02)
            {
                byte subMode = originalSector[0x12];
                isMode2Form2 = (subMode & 0x20) != 0;
                isMode2Form1 = !isMode2Form2;
            }

            // Copy user data into the regenerated buffer at the correct offset
            if (isMode1)
            {
                // Mode1: user data at 0x10, 2048 bytes
                Array.Copy(originalSector, 0x10, buffer, bufferOffset + 0x10, 2048);
            }
            else
            {
                // Mode2Form1/Mode2Form2: user data at 0x18, 2048 bytes
                Array.Copy(originalSector, 0x18, buffer, bufferOffset + 0x18, 2048);
            }

            // For Mode2 sectors, copy the subheader at 0x14-0x17 so ReconstructPrefix
            // can copy it to 0x10-0x13 (ReconstructPrefix copies 0x14->0x10 for mode2)
            if (!isMode1)
            {
                Array.Copy(originalSector, 0x14, buffer, bufferOffset + 0x14, 4);
            }

            // Apply ReconstructPrefix: writes standard sync + computed MSF + mode
            Ecm.ReconstructPrefix(buffer, bufferOffset, isMode1, lba);

            // Apply ReconstructEcc: computes EDC + ECC
            Ecm.ReconstructEcc(buffer, bufferOffset, isMode1, isMode2Form1, isMode2Form2);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 6.2**
        ///
        /// Property 1: Pack/Unpack Round-Trip Integrity — All conforming Mode1 sectors.
        ///
        /// For any valid section of conforming Mode1 sectors at any valid starting LBA,
        /// packing then unpacking onto a buffer pre-filled with deterministically
        /// regenerated sectors SHALL produce bytes identical to the original section data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_ConformingMode1_ProducesIdenticalOutput(
            NonNegativeInt lbaSeed,
            NonNegativeInt sectorCountSeed,
            byte[] userDataSeed)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);
            int sectorCount = (sectorCountSeed.Get % 8) + 1; // 1-8 sectors

            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            // Build original section data
            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ (byte)i);

                byte[] sector = BuildConformingMode1Sector(startLba + i, sectorUserData);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            return VerifyRoundTrip(sectionData, sectorCount, startLba);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 6.2**
        ///
        /// Property 1: Pack/Unpack Round-Trip Integrity — All Mode2Form1 sectors.
        ///
        /// For any valid section of Mode2Form1 sectors with arbitrary subheaders,
        /// packing then unpacking onto a regenerated buffer SHALL produce bytes
        /// identical to the original section data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_Mode2Form1_ProducesIdenticalOutput(
            NonNegativeInt lbaSeed,
            NonNegativeInt sectorCountSeed,
            byte[] subheaderSeed)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);
            int sectorCount = (sectorCountSeed.Get % 8) + 1; // 1-8 sectors

            byte[] subheaderBase = new byte[8];
            if (subheaderSeed != null && subheaderSeed.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheaderBase[i] = subheaderSeed[i % subheaderSeed.Length];
            }

            // Build original section data
            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
            {
                byte[] subheader = new byte[8];
                Array.Copy(subheaderBase, subheader, 8);
                // Vary subheader per sector
                subheader[0] = (byte)(subheader[0] ^ (byte)i);

                byte[] sector = BuildMode2Form1Sector(startLba + i, subheader);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            return VerifyRoundTrip(sectionData, sectorCount, startLba);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 6.2**
        ///
        /// Property 1: Pack/Unpack Round-Trip Integrity — All Mode2Form2 sectors.
        ///
        /// For any valid section of Mode2Form2 sectors with arbitrary subheaders and
        /// extended user data, packing then unpacking onto a regenerated buffer SHALL
        /// produce bytes identical to the original section data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_Mode2Form2_ProducesIdenticalOutput(
            NonNegativeInt lbaSeed,
            NonNegativeInt sectorCountSeed,
            byte[] subheaderSeed,
            byte[] extUserDataSeed)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);
            int sectorCount = (sectorCountSeed.Get % 8) + 1; // 1-8 sectors

            byte[] subheaderBase = new byte[8];
            if (subheaderSeed != null && subheaderSeed.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheaderBase[i] = subheaderSeed[i % subheaderSeed.Length];
            }

            byte[] extUserDataBase = new byte[280];
            if (extUserDataSeed != null && extUserDataSeed.Length > 0)
            {
                for (int i = 0; i < 280; i++)
                    extUserDataBase[i] = extUserDataSeed[i % extUserDataSeed.Length];
            }

            // Build original section data
            byte[] sectionData = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
            {
                byte[] subheader = new byte[8];
                Array.Copy(subheaderBase, subheader, 8);
                subheader[0] = (byte)(subheader[0] ^ (byte)i);

                byte[] extUserData = new byte[280];
                Array.Copy(extUserDataBase, extUserData, 280);
                extUserData[0] = (byte)(extUserData[0] ^ (byte)i);

                byte[] sector = BuildMode2Form2Sector(startLba + i, subheader, extUserData);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            return VerifyRoundTrip(sectionData, sectorCount, startLba);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 6.2**
        ///
        /// Property 1: Pack/Unpack Round-Trip Integrity — Sectors with corrupted components.
        ///
        /// For any Mode1 sector with selectively corrupted sync, MSF, EDC, or ECC,
        /// packing then unpacking onto a regenerated buffer SHALL produce bytes
        /// identical to the original section data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_CorruptedMode1_ProducesIdenticalOutput(
            NonNegativeInt lbaSeed,
            byte[] userDataSeed,
            bool corruptSync,
            bool corruptMsf,
            bool corruptEdc,
            bool corruptEcc)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);

            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0xCA, 0xFE };

            byte[] sectorUserData = new byte[2048];
            for (int j = 0; j < 2048; j++)
                sectorUserData[j] = userData[j % userData.Length];

            byte[] sector = BuildCorruptedMode1Sector(startLba, sectorUserData, corruptSync, corruptMsf, corruptEdc, corruptEcc);

            byte[] sectionData = new byte[RawSectorSize];
            Array.Copy(sector, 0, sectionData, 0, RawSectorSize);

            return VerifyRoundTrip(sectionData, 1, startLba);
        }

        /// <summary>
        /// **Validates: Requirements 7.1, 7.2, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 6.2**
        ///
        /// Property 1: Pack/Unpack Round-Trip Integrity — Mixed multi-sector sections.
        ///
        /// For any section containing a mix of Mode1, Mode2Form1, and Mode2Form2 sectors
        /// (some conforming, some with corrupted components), packing then unpacking onto
        /// a regenerated buffer SHALL produce bytes identical to the original section data.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool RoundTrip_MixedSectors_ProducesIdenticalOutput(
            NonNegativeInt lbaSeed,
            byte[] userDataSeed,
            byte[] subheaderSeed,
            byte[] extUserDataSeed,
            bool includeCorruptedMode1,
            bool includeMode2Form1,
            bool includeMode2Form2)
        {
            long startLba = lbaSeed.Get % (MaxLba + 1);

            byte[] userData = (userDataSeed != null && userDataSeed.Length > 0)
                ? userDataSeed
                : new byte[] { 0x11, 0x22, 0x33 };

            byte[] subheaderBase = new byte[8];
            if (subheaderSeed != null && subheaderSeed.Length > 0)
            {
                for (int i = 0; i < 8; i++)
                    subheaderBase[i] = subheaderSeed[i % subheaderSeed.Length];
            }

            byte[] extUserDataBase = new byte[280];
            if (extUserDataSeed != null && extUserDataSeed.Length > 0)
            {
                for (int i = 0; i < 280; i++)
                    extUserDataBase[i] = extUserDataSeed[i % extUserDataSeed.Length];
            }

            // Build a mixed section with at least 4 sectors:
            // Sector 0: conforming Mode1
            // Sector 1: Mode2Form1 (if includeMode2Form1) or conforming Mode1
            // Sector 2: Mode2Form2 (if includeMode2Form2) or conforming Mode1
            // Sector 3: corrupted Mode1 (if includeCorruptedMode1) or conforming Mode1
            int sectorCount = 4;
            byte[] sectionData = new byte[sectorCount * RawSectorSize];

            // Sector 0: conforming Mode1
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = userData[j % userData.Length];
                byte[] sector = BuildConformingMode1Sector(startLba, sectorUserData);
                Array.Copy(sector, 0, sectionData, 0, RawSectorSize);
            }

            // Sector 1: Mode2Form1 or conforming Mode1
            if (includeMode2Form1)
            {
                byte[] subheader = new byte[8];
                Array.Copy(subheaderBase, subheader, 8);
                byte[] sector = BuildMode2Form1Sector(startLba + 1, subheader);
                Array.Copy(sector, 0, sectionData, RawSectorSize, RawSectorSize);
            }
            else
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ 0x01);
                byte[] sector = BuildConformingMode1Sector(startLba + 1, sectorUserData);
                Array.Copy(sector, 0, sectionData, RawSectorSize, RawSectorSize);
            }

            // Sector 2: Mode2Form2 or conforming Mode1
            if (includeMode2Form2)
            {
                byte[] subheader = new byte[8];
                Array.Copy(subheaderBase, subheader, 8);
                subheader[0] ^= 0x02;
                byte[] extUserData = new byte[280];
                Array.Copy(extUserDataBase, extUserData, 280);
                byte[] sector = BuildMode2Form2Sector(startLba + 2, subheader, extUserData);
                Array.Copy(sector, 0, sectionData, 2 * RawSectorSize, RawSectorSize);
            }
            else
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ 0x02);
                byte[] sector = BuildConformingMode1Sector(startLba + 2, sectorUserData);
                Array.Copy(sector, 0, sectionData, 2 * RawSectorSize, RawSectorSize);
            }

            // Sector 3: corrupted Mode1 or conforming Mode1
            if (includeCorruptedMode1)
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ 0x03);
                byte[] sector = BuildCorruptedMode1Sector(startLba + 3, sectorUserData, true, true, true, true);
                Array.Copy(sector, 0, sectionData, 3 * RawSectorSize, RawSectorSize);
            }
            else
            {
                byte[] sectorUserData = new byte[2048];
                for (int j = 0; j < 2048; j++)
                    sectorUserData[j] = (byte)(userData[j % userData.Length] ^ 0x03);
                byte[] sector = BuildConformingMode1Sector(startLba + 3, sectorUserData);
                Array.Copy(sector, 0, sectionData, 3 * RawSectorSize, RawSectorSize);
            }

            return VerifyRoundTrip(sectionData, sectorCount, startLba);
        }

        /// <summary>
        /// Core round-trip verification helper.
        /// Packs the section, creates a regenerated buffer, unpacks onto it,
        /// and verifies byte-for-byte equality with the original section data.
        /// </summary>
        private bool VerifyRoundTrip(byte[] sectionData, int sectorCount, long startLba)
        {
            // Step 1: Pack the section
            byte[] packData = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);
            if (packData == null)
                return false;

            // Step 2: Create a "regenerated" buffer by simulating the reconstruction pipeline
            byte[] regeneratedBuffer = new byte[sectorCount * RawSectorSize];

            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * RawSectorSize;
                long lba = startLba + i;

                // Extract the original sector for reference
                byte[] originalSector = new byte[RawSectorSize];
                Array.Copy(sectionData, sectorOffset, originalSector, 0, RawSectorSize);

                // Regenerate the sector (simulates ReconstructPrefix + ReconstructEcc)
                RegenerateSector(originalSector, regeneratedBuffer, sectorOffset, lba);
            }

            // Step 3: Unpack (overlay non-recreatable bytes onto the regenerated buffer)
            SectorPaddingUnpacker.Unpack(packData, regeneratedBuffer, 0, sectorCount);

            // Step 4: Compare regenerated buffer against original section data byte-by-byte
            for (int i = 0; i < sectionData.Length; i++)
            {
                if (regeneratedBuffer[i] != sectionData[i])
                    return false;
            }

            return true;
        }
    }
}