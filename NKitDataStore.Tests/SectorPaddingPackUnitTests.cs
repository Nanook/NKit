using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SectorPaddingPacker"/> and <see cref="SectorPaddingUnpacker"/>.
    /// Validates Requirements 1.1, 1.2, 1.3, 2.1, 2.9.
    /// </summary>
    public class SectorPaddingPackUnitTests
    {
        private const int RawSectorSize = 0x930; // 2352 bytes

        /// <summary>Standard CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00</summary>
        private static readonly byte[] SyncPattern =
            { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        /// <summary>
        /// Builds a fully conforming Mode1 sector at the given LBA with zero user data.
        /// </summary>
        private static byte[] BuildConformingMode1Sector(long lba)
        {
            byte[] sector = new byte[RawSectorSize];
            // ReconstructPrefix sets sync + MSF + mode
            Ecm.ReconstructPrefix(sector, 0, true, lba);
            // ReconstructEcc sets EDC + reserved zeros + ECC
            Ecm.ReconstructEcc(sector, 0, true, false, false);
            return sector;
        }

        /// <summary>
        /// Builds a conforming Mode2Form2 sector at the given LBA with specified
        /// subheader and extended user data.
        /// </summary>
        private static byte[] BuildConformingMode2Form2Sector(long lba, byte[] subheader, byte[] extUserData)
        {
            byte[] sector = new byte[RawSectorSize];

            // Fill user data area (offset 0x18, 2048 bytes)
            for (int i = 0x18; i < 0x818; i++)
                sector[i] = (byte)(i & 0xFF);

            // Write extended user data (280 bytes at offset 0x818)
            Array.Copy(extUserData, 0, sector, 0x818, 280);

            // Set mode byte
            sector[0x0F] = 0x02;

            // Write subheader (8 bytes at 0x10-0x17)
            Array.Copy(subheader, 0, sector, 0x10, 8);
            // Ensure submode byte (0x12) has bit 5 set (Form2)
            sector[0x12] |= 0x20;
            // Duplicate subheader at 0x14
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Apply ReconstructPrefix (writes sync + MSF + mode, copies subheader 0x14->0x10)
            Ecm.ReconstructPrefix(sector, 0, false, lba);

            // Apply ReconstructEcc (computes EDC for Mode2Form2)
            Ecm.ReconstructEcc(sector, 0, false, false, true);

            return sector;
        }

        // ── Requirement 1.2: Version byte is always 0x01 ────────────────

        /// <summary>
        /// Validates Requirement 1.2: The Packer SHALL write version byte value 0x01.
        /// </summary>
        [Fact]
        public void Pack_VersionByte_IsAlways0x01()
        {
            // Arrange: single conforming Mode1 sector at LBA 150
            byte[] sector = BuildConformingMode1Sector(150);

            // Act
            byte[] pack = SectorPaddingPacker.Pack(sector, 1, 150);

            // Assert
            Assert.NotNull(pack);
            Assert.Equal(0x01, pack[0]);
        }

        // ── Requirement 2.1: Audio section type is skipped ──────────────

        /// <summary>
        /// Validates Requirement 2.1: Audio sections are handled at the formatter level.
        /// The Packer itself processes whatever data it receives. This test verifies
        /// that the Packer returns null for null/empty input (the formatter-level
        /// early return for audio sections passes null/empty data).
        /// </summary>
        [Fact]
        public void Pack_NullInput_ReturnsNull()
        {
            // Act
            byte[] result = SectorPaddingPacker.Pack(null, 0, 0);

            // Assert
            Assert.Null(result);
        }

        /// <summary>
        /// Validates Requirement 2.1: Empty section data returns null (formatter
        /// skips audio sections by not calling Pack, or passing empty data).
        /// </summary>
        [Fact]
        public void Pack_EmptyInput_ReturnsNull()
        {
            // Act
            byte[] result = SectorPaddingPacker.Pack(Array.Empty<byte>(), 0, 0);

            // Assert
            Assert.Null(result);
        }

        // ── Requirement 2.1: Non-raw block size is skipped ──────────────

        /// <summary>
        /// Validates Requirement 2.1: Non-raw block sizes are handled at the formatter
        /// level. The Packer returns null when sectorCount is zero or negative
        /// (the formatter-level early return for non-raw block sizes).
        /// </summary>
        [Fact]
        public void Pack_ZeroSectorCount_ReturnsNull()
        {
            // Arrange: provide some data but zero sector count
            byte[] data = new byte[RawSectorSize];

            // Act
            byte[] result = SectorPaddingPacker.Pack(data, 0, 0);

            // Assert
            Assert.Null(result);
        }

        /// <summary>
        /// Validates Requirement 2.1: Negative sector count returns null.
        /// </summary>
        [Fact]
        public void Pack_NegativeSectorCount_ReturnsNull()
        {
            // Arrange
            byte[] data = new byte[RawSectorSize];

            // Act
            byte[] result = SectorPaddingPacker.Pack(data, -1, 0);

            // Assert
            Assert.Null(result);
        }

        // ── Requirement 1.1, 2.9: Pack header structure for 16 conforming Mode1 sectors → 5 bytes ──

        /// <summary>
        /// Validates Requirements 1.1, 2.9: 16 conforming Mode1 sectors produce a pack
        /// of exactly 5 bytes: 1 (version) + 2 (sector count LE) + 2 (bitmap, all zero).
        /// </summary>
        [Fact]
        public void Pack_16ConformingMode1Sectors_Produces5BytePack()
        {
            // Arrange: build 16 conforming Mode1 sectors starting at LBA 150
            long startLba = 150;
            byte[] sectionData = new byte[16 * RawSectorSize];
            for (int i = 0; i < 16; i++)
            {
                byte[] sector = BuildConformingMode1Sector(startLba + i);
                Array.Copy(sector, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Act
            byte[] pack = SectorPaddingPacker.Pack(sectionData, 16, startLba);

            // Assert
            Assert.NotNull(pack);
            Assert.Equal(5, pack.Length);

            // Version byte
            Assert.Equal(0x01, pack[0]);

            // Sector count = 16 (little-endian)
            Assert.Equal(16, pack[1] | (pack[2] << 8));

            // Bitmap: 2 bytes, all zero (16 sectors / 8 = 2 bytes)
            Assert.Equal(0x00, pack[3]);
            Assert.Equal(0x00, pack[4]);
        }

        // ── Requirement 2.3, 2.4: Single Mode2Form2 sector produces 293 bytes ──

        /// <summary>
        /// Validates Requirements 2.3, 2.4: A single Mode2Form2 sector with conforming
        /// sync/MSF/EDC produces a pack of exactly 293 bytes:
        /// 1 (version) + 2 (count) + 1 (bitmap) + 1 (flag) + 8 (subheader) + 280 (ext user data) = 293.
        /// </summary>
        [Fact]
        public void Pack_SingleMode2Form2Sector_Produces293BytePack()
        {
            // Arrange: build a conforming Mode2Form2 sector at LBA 200
            long lba = 200;
            byte[] subheader = new byte[8] { 0x01, 0x02, 0x20, 0x04, 0x01, 0x02, 0x20, 0x04 };
            byte[] extUserData = new byte[280];
            for (int i = 0; i < 280; i++)
                extUserData[i] = (byte)(i & 0xFF);

            byte[] sector = BuildConformingMode2Form2Sector(lba, subheader, extUserData);

            // Act
            byte[] pack = SectorPaddingPacker.Pack(sector, 1, lba);

            // Assert
            Assert.NotNull(pack);
            Assert.Equal(293, pack.Length);

            // Verify structure:
            // [0] = version 0x01
            Assert.Equal(0x01, pack[0]);

            // [1-2] = sector count = 1 (LE)
            Assert.Equal(1, pack[1] | (pack[2] << 8));

            // [3] = bitmap: bit 0 set (sector has stored data)
            Assert.Equal(0x01, pack[3] & 0x01);

            // [4] = flag byte: type=Mode2Form2 (0x02), bit 4 (Subheader) + bit 7 (ExtUserData)
            byte flagByte = pack[4];
            Assert.Equal((byte)SectorFlags.Mode2Form2, (byte)(flagByte & (byte)SectorFlags.TypeMask));
            Assert.NotEqual(0, flagByte & (byte)SectorFlags.Subheader);
            Assert.NotEqual(0, flagByte & (byte)SectorFlags.ExtUserData);
        }

        // ── Requirement 1.3: Unsupported version throws InvalidDataException ──

        /// <summary>
        /// Validates Requirement 1.3: ReadHeader throws InvalidDataException for version 0x00.
        /// </summary>
        [Fact]
        public void ReadHeader_Version0x00_ThrowsInvalidDataException()
        {
            // Arrange: pack data with version 0x00
            byte[] packData = { 0x00, 0x01, 0x00, 0x00 };

            // Act & Assert
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                SectorPaddingUnpacker.ReadHeader(packData, out _, out _));

            Assert.Contains("0x00", ex.Message);
        }

        /// <summary>
        /// Validates Requirement 1.3: ReadHeader throws InvalidDataException for version 0x02.
        /// </summary>
        [Fact]
        public void ReadHeader_Version0x02_ThrowsInvalidDataException()
        {
            // Arrange: pack data with version 0x02
            byte[] packData = { 0x02, 0x01, 0x00, 0x00 };

            // Act & Assert
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                SectorPaddingUnpacker.ReadHeader(packData, out _, out _));

            Assert.Contains("0x02", ex.Message);
        }

        /// <summary>
        /// Validates Requirement 1.3: ReadHeader throws InvalidDataException for version 0xFF.
        /// </summary>
        [Fact]
        public void ReadHeader_Version0xFF_ThrowsInvalidDataException()
        {
            // Arrange: pack data with version 0xFF
            byte[] packData = { 0xFF, 0x01, 0x00, 0x00 };

            // Act & Assert
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                SectorPaddingUnpacker.ReadHeader(packData, out _, out _));

            Assert.Contains("0xFF", ex.Message);
        }

        /// <summary>
        /// Validates Requirement 1.3: Unpack throws InvalidDataException for unsupported version.
        /// </summary>
        [Fact]
        public void Unpack_UnsupportedVersion_ThrowsInvalidDataException()
        {
            // Arrange: pack data with version 0x05
            byte[] packData = { 0x05, 0x01, 0x00, 0x00 };
            byte[] buffer = new byte[RawSectorSize];

            // Act & Assert
            Assert.Throws<InvalidDataException>(() =>
                SectorPaddingUnpacker.Unpack(packData, buffer, 0, 1));
        }

        /// <summary>
        /// Validates Requirement 1.3: ReadHeader accepts version 0x01 without throwing.
        /// </summary>
        [Fact]
        public void ReadHeader_Version0x01_DoesNotThrow()
        {
            // Arrange: valid pack data with version 0x01
            byte[] packData = { 0x01, 0x01, 0x00, 0x00 };

            // Act & Assert (should not throw)
            int sectorCount = SectorPaddingUnpacker.ReadHeader(packData, out int bitmapOffset, out int bitmapLength);

            Assert.Equal(1, sectorCount);
            Assert.Equal(3, bitmapOffset);
            Assert.Equal(1, bitmapLength);
        }
    }
}