using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Covers the moved IsCreatable / per-sector recreatability analysis:
    /// <see cref="SectorPaddingPacker.Analyse"/> (the single parallel pass now run by the ISO9660
    /// SectionProcessor), <see cref="SectorPaddingPacker.IsFullyCreatable"/> (drives IsCreatable and
    /// the "skip the BlockPadding record" optimization), and the Serialize→Unpack round-trip for the
    /// non-recreatable case (stored bytes overlay back byte-identically).
    ///
    /// Semantics under test:
    ///  - Fully-standard sectors (correct sync + MSF + mode + EDC + ECC) => IsFullyCreatable == true,
    ///    no components stored, no BlockPadding record needed.
    ///  - Non-standard sync / MSF / EDC / ECC => not creatable; those components are stored and
    ///    overlay back exactly.
    ///  - Mode2 subheader / Mode2Form2 ext-user-data are CONTENT (always stored) and do NOT make a
    ///    section non-creatable on their own.
    /// </summary>
    public class SectorPaddingCreatableTests
    {
        private const int RawSectorSize = 0x930;

        private static byte[] BuildConformingMode1Sector(byte[] dest, int offset, long lba, int userSeed)
        {
            for (int i = 0; i < 0x800; i++)
                dest[offset + 0x10 + i] = (byte)((i + userSeed) & 0xFF);
            Ecm.ReconstructPrefix(dest, offset, true, lba);
            Ecm.ReconstructEcc(dest, offset, true, false, false);
            return dest;
        }

        private static byte[] BuildConformingMode2Form1Sector(byte[] dest, int offset, long lba, int userSeed)
        {
            for (int i = 0; i < 0x800; i++)
                dest[offset + 0x18 + i] = (byte)((i + userSeed) & 0xFF);
            dest[offset + 0x0F] = 0x02;
            // Form1 subheader (submode bit 5 clear), duplicated at 0x14
            dest[offset + 0x10] = 0x00; dest[offset + 0x11] = 0x00; dest[offset + 0x12] = 0x08; dest[offset + 0x13] = 0x00;
            dest[offset + 0x14] = dest[offset + 0x10]; dest[offset + 0x15] = dest[offset + 0x11];
            dest[offset + 0x16] = dest[offset + 0x12]; dest[offset + 0x17] = dest[offset + 0x13];
            Ecm.ReconstructPrefix(dest, offset, false, lba);
            Ecm.ReconstructEcc(dest, offset, false, true, false);
            return dest;
        }

        [Fact]
        public void Analyse_AllConformingMode1_IsFullyCreatable_NoStoredComponents()
        {
            const int sectorCount = 8;
            const long startLba = 5000;
            byte[] section = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
                BuildConformingMode1Sector(section, i * RawSectorSize, startLba + i, i * 7);

            SectorFlags[] flags = SectorPaddingPacker.Analyse(section, sectorCount, startLba);

            Assert.NotNull(flags);
            Assert.Equal(sectorCount, flags.Length);
            // No sector needs sync/MSF/EDC/ECC stored -> fully creatable.
            Assert.True(SectorPaddingPacker.IsFullyCreatable(flags),
                "Conforming Mode1 sectors should be fully creatable (no components to store).");
            foreach (SectorFlags f in flags)
            {
                Assert.Equal(SectorFlags.None, f & SectorFlags.Sync);
                Assert.Equal(SectorFlags.None, f & SectorFlags.MsfMode);
                Assert.Equal(SectorFlags.None, f & SectorFlags.Edc);
                Assert.Equal(SectorFlags.None, f & SectorFlags.Ecc);
            }

            // Serialize would produce an all-zero-bitmap pack; the formatter skips it entirely.
            byte[] pack = SectorPaddingPacker.Serialize(section, flags);
            Assert.NotNull(pack);
            int bitmapLen = (sectorCount + 7) / 8;
            for (int b = 0; b < bitmapLen; b++)
                Assert.Equal(0, pack[3 + b]); // presence bitmap all zero
        }

        [Fact]
        public void Analyse_Mode2Form1_SubheaderIsContent_IsNotFullyCreatable()
        {
            // REGRESSION GUARD: a Mode2/2352 (CD-XA) section carries an 8-byte subheader (0x10-0x17)
            // that CANNOT be regenerated from the LBA + user data AND is an INPUT to the Mode2
            // EDC/ECC. It must therefore always be persisted in the SectorPaddingPack. If the
            // section were classified "fully creatable", the formatter would SKIP the pack, the
            // subheader would be lost, and the reader would reconstruct EDC/ECC over a zeroed
            // subheader — producing a different CRC (the Mode2 DataStore round-trip failure this
            // test now guards against). So a Mode2 section is NEVER fully creatable.
            const int sectorCount = 4;
            const long startLba = 12345;
            byte[] section = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
                BuildConformingMode2Form1Sector(section, i * RawSectorSize, startLba + i, i * 3);

            SectorFlags[] flags = SectorPaddingPacker.Analyse(section, sectorCount, startLba);

            Assert.NotNull(flags);
            // The subheader must be flagged for storage on every Mode2 sector.
            foreach (SectorFlags f in flags)
                Assert.NotEqual(SectorFlags.None, f & SectorFlags.Subheader);
            // ...and because it is non-regenerable content, the section must NOT be fully creatable,
            // forcing the pack (with the subheader) to be written.
            Assert.False(SectorPaddingPacker.IsFullyCreatable(flags),
                "A Mode2 section carries a non-regenerable subheader and must NOT be fully creatable — its pack must be stored.");

            // The stored pack must actually contain the subheader payload so reconstruction can
            // restore it before recomputing Mode2 EDC/ECC.
            byte[] pack = SectorPaddingPacker.Serialize(section, flags);
            Assert.NotNull(pack);
            int bitmapLen = (sectorCount + 7) / 8;
            bool anySectorPresent = false;
            for (int b = 0; b < bitmapLen; b++)
                anySectorPresent |= pack[3 + b] != 0;
            Assert.True(anySectorPresent, "The pack presence bitmap must mark the Mode2 subheader-bearing sectors.");
        }

        [Fact]
        public void Analyse_NonStandardSync_NotCreatable_And_RoundTripsExactly()
        {
            const int sectorCount = 4;
            const long startLba = 20000;
            byte[] section = new byte[sectorCount * RawSectorSize];
            for (int i = 0; i < sectorCount; i++)
                BuildConformingMode1Sector(section, i * RawSectorSize, startLba + i, i * 5);

            // Corrupt sector 2's sync pattern (copy-protection style) and its EDC, THEN snapshot the
            // corrupted section as the expected round-trip target.
            int badOffset = 2 * RawSectorSize;
            section[badOffset + 0x03] ^= 0xAA;      // sync byte
            section[badOffset + 0x810] ^= 0x5C;     // EDC byte (Mode1 EDC at 0x810)
            byte[] original = (byte[])section.Clone();

            SectorFlags[] flags = SectorPaddingPacker.Analyse(section, sectorCount, startLba);

            Assert.NotNull(flags);
            Assert.False(SectorPaddingPacker.IsFullyCreatable(flags),
                "A sector with non-standard sync/EDC must make the section non-creatable.");
            Assert.NotEqual(SectorFlags.None, flags[2] & SectorFlags.Sync);
            Assert.NotEqual(SectorFlags.None, flags[2] & SectorFlags.Edc);
            // The untouched sectors remain fully recreatable (no bits stored).
            Assert.Equal(SectorFlags.None, flags[0] & (SectorFlags.Sync | SectorFlags.Edc | SectorFlags.Ecc | SectorFlags.MsfMode));

            // Serialize then reconstruct: regenerate standard sectors, overlay the stored bytes,
            // and confirm the result is byte-identical to the corrupted original.
            byte[] pack = SectorPaddingPacker.Serialize(section, flags);
            Assert.NotNull(pack);

            byte[] rebuilt = new byte[section.Length];
            // Reproduce the reader's regenerate path: pre-ECC overlay (subheader/ext), regenerate
            // sync/MSF/EDC/ECC per sector, then post-ECC overlay (sync/MSF/EDC/ECC).
            SectorPaddingUnpacker.UnpackPreEcc(pack, rebuilt, 0, sectorCount, out SectorFlags?[] types);
            for (int i = 0; i < sectorCount; i++)
            {
                int off = i * RawSectorSize;
                // copy user data region so reconstruction has the payload
                Array.Copy(section, off + 0x10, rebuilt, off + 0x10, 0x800);
                Ecm.ReconstructPrefix(rebuilt, off, true, startLba + i);
                Ecm.ReconstructEcc(rebuilt, off, true, false, false);
            }
            SectorPaddingUnpacker.UnpackPostEcc(pack, rebuilt, 0, sectorCount);

            Assert.Equal(original, rebuilt);
        }

        [Fact]
        public void IsFullyCreatable_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(SectorPaddingPacker.IsFullyCreatable(null));
            Assert.False(SectorPaddingPacker.IsFullyCreatable(Array.Empty<SectorFlags>()));
        }
    }
}