using System;
using System.Linq;
using Nanook.NKit;
using Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Regression tests for the mixed Mode2Form1/Form2 section pack/unpack round-trip.
    ///
    /// Scenario: a disc section contains N Mode2Form2 sectors (submode 0x20) followed by a
    /// single Mode2Form1 sector (submode clears bit 5). The Mode2Form1 sector has a
    /// non-standard subheader (first half ≠ second half) that must survive the DataStore
    /// round-trip unchanged.
    ///
    /// The bug: if the Mode2Form1 sector's MSF happens to match the standard LBA-derived value,
    /// the packer would not set MsfMode, and the form-type bits in the flag byte are the only
    /// indicator that the sector is Form1. The unpacker must read those bits and reconstruct
    /// the sector as Form1 — not Form2.
    /// </summary>
    public class MixedForm1Form2SectionRoundTripTests
    {
        private const int RawSectorSize = 0x930;

        /// <summary>
        /// Builds a Mode2Form2 sector at the given LBA with a standard subheader
        /// (both halves of subheader set to the same value, bit 5 of submode set).
        /// User data is all zeros. EDC is computed.
        /// </summary>
        private static byte[] BuildMode2Form2Sector(long lba, byte submodeByte = 0x20)
        {
            byte[] sector = new byte[RawSectorSize];
            sector[0x0F] = 0x02;

            // Subheader: first and second halves identical, bit 5 set for Form2
            sector[0x10] = 0x00;
            sector[0x11] = 0x00;
            sector[0x12] = (byte)(submodeByte | 0x20); // Form2 bit
            sector[0x13] = 0x00;
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // ExtUserData (0x818-0x92B) all zero
            Ecm.ReconstructPrefix(sector, 0, false, lba);
            Ecm.ReconstructEcc(sector, 0, false, false, true);
            return sector;
        }

        /// <summary>
        /// Builds a Mode2Form1 sector at the given LBA with a non-standard subheader
        /// (first half deliberately differs from second half, bit 5 of submode clear in BOTH halves).
        /// ReconstructPrefix mirrors the second half to the first, so both halves must have bit 5 clear.
        /// </summary>
        private static byte[] BuildMode2Form1SectorWithNonStandardSubheader(long lba)
        {
            byte[] sector = new byte[RawSectorSize];
            sector[0x0F] = 0x02;

            // Non-standard subheader: two halves differ, but NEITHER has bit 5 set (both Form1).
            // ReconstructPrefix will copy [0x14-0x17] to [0x10-0x13], so set both halves.
            // First half (after mirror): byte [0x12] must be 0x01 (Form1)
            // Second half (source for mirror): [0x16] must also NOT have bit 5 set
            sector[0x14] = 0x00;
            sector[0x15] = 0x00;
            sector[0x16] = 0x01; // Form1: bit 5 NOT set — this gets mirrored to [0x12]
            sector[0x17] = 0xFE;

            // User data: some non-trivial content
            for (int i = 0x18; i < 0x818; i++)
                sector[i] = (byte)((i ^ 0xA5) & 0xFF);

            Ecm.ReconstructPrefix(sector, 0, false, lba); // mirrors [0x14-0x17] to [0x10-0x13]
            Ecm.ReconstructEcc(sector, 0, false, true, false);
            return sector;
        }

        /// <summary>
        /// Simulates the Unpack round-trip:
        /// 1. Pack a section with Analyse + Serialize.
        /// 2. Simulate reconstruction by zero-filling a buffer (as OnGapFill would).
        /// 3. Apply UnpackPreEcc to restore subheaders and populate sectorTypes.
        /// 4. Apply ReconstructPrefix+Ecc for each sector using sectorTypes as authoritative.
        /// 5. Apply UnpackPostEcc to restore non-standard sync/MSF/EDC/ECC.
        /// 6. Assert output matches original.
        /// </summary>
        private static byte[] SimulatePackUnpackRoundTrip(byte[] sectionData, int sectorCount, long startLba)
        {
            // Pack
            SectorFlags[] flags = SectorPaddingPacker.Analyse(sectionData, sectorCount, startLba);
            byte[] pack = SectorPaddingPacker.Serialize(sectionData, flags);

            // Simulate reconstruction buffer (zeros — as if gap fill occurred)
            byte[] buffer = new byte[sectorCount * RawSectorSize];

            // Copy user data into buffer (OnGapFill writes zeros; actual data blocks write
            // user data to 0x18..0x817. For this test we copy the user data region only.)
            for (int i = 0; i < sectorCount; i++)
            {
                int srcOff = i * RawSectorSize;
                int dstOff = i * RawSectorSize;
                // Copy user data (0x18-0x817, 2048 bytes)
                Array.Copy(sectionData, srcOff + 0x18, buffer, dstOff + 0x18, 0x800);
            }

            if (pack != null)
            {
                // Phase 1: apply subheader + ext user data before ECC
                SectorPaddingUnpacker.UnpackPreEcc(pack, buffer, 0, sectorCount, out SectorFlags?[] sectorTypes);

                // Reconstruct each sector's headers and ECC
                for (int i = 0; i < sectorCount; i++)
                {
                    int sectorOffset = i * RawSectorSize;
                    long lba = startLba + i;

                    bool isMode1, isMode2Form1 = false, isMode2Form2 = false;
                    if (sectorTypes != null && sectorTypes[i].HasValue)
                    {
                        SectorFlags type = sectorTypes[i].Value & SectorFlags.TypeMask;
                        isMode1 = type == SectorFlags.Mode1;
                        isMode2Form1 = type == SectorFlags.Mode2Form1;
                        isMode2Form2 = type == SectorFlags.Mode2Form2;
                    }
                    else
                    {
                        // Fallback: inspect buffer subheader
                        isMode1 = false;
                        byte subMode = buffer[sectorOffset + 0x12];
                        isMode2Form2 = (subMode & 0x20) != 0;
                        isMode2Form1 = !isMode2Form2;
                    }

                    Ecm.ReconstructPrefix(buffer, sectorOffset, isMode1, lba);

                    if (isMode1 || isMode2Form1)
                        Ecm.ReconstructEcc(buffer, sectorOffset, isMode1, isMode2Form1, false);
                    else if (isMode2Form2)
                        Ecm.ReconstructEcc(buffer, sectorOffset, false, false, true);
                }

                // Phase 2: restore non-standard sync/MSF/EDC/ECC
                SectorPaddingUnpacker.UnpackPostEcc(pack, buffer, 0, sectorCount);
            }

            return buffer;
        }

        /// <summary>
        /// Core regression: 50 Mode2Form2 sectors + 1 Mode2Form1 sector.
        /// The Form1 sector's subheader (first half ≠ second half) must be preserved exactly.
        /// </summary>
        [Fact]
        public void RoundTrip_MixedForm2ThenForm1_PreservesForm1Subheader()
        {
            const int form2Count = 50;
            const long startLba = 8920;
            int totalSectors = form2Count + 1;

            byte[] sectionData = new byte[totalSectors * RawSectorSize];

            // Build 50 Form2 sectors
            for (int i = 0; i < form2Count; i++)
            {
                byte[] s = BuildMode2Form2Sector(startLba + i);
                Array.Copy(s, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            // Build 1 Form1 sector with non-standard subheader
            byte[] form1Sector = BuildMode2Form1SectorWithNonStandardSubheader(startLba + form2Count);
            Array.Copy(form1Sector, 0, sectionData, form2Count * RawSectorSize, RawSectorSize);

            // Round-trip
            byte[] reconstructed = SimulatePackUnpackRoundTrip(sectionData, totalSectors, startLba);

            // Assert: the Form1 sector at index 50 must match the original exactly
            int form1SectorOffset = form2Count * RawSectorSize;
            for (int b = 0; b < RawSectorSize; b++)
            {
                Assert.True(
                    sectionData[form1SectorOffset + b] == reconstructed[form1SectorOffset + b],
                    $"Byte 0x{b:X3} of the Form1 sector differs: " +
                    $"expected 0x{sectionData[form1SectorOffset + b]:X2}, " +
                    $"got 0x{reconstructed[form1SectorOffset + b]:X2}");
            }
        }

        /// <summary>
        /// The Form1 sector's subheader first half specifically must not be replaced
        /// with the second half (the exact corruption observed in the PS2 disc).
        /// </summary>
        [Fact]
        public void RoundTrip_MixedForm2ThenForm1_SubheaderFirstHalfNotCorrupted()
        {
            const int form2Count = 50;
            const long startLba = 8920;
            int totalSectors = form2Count + 1;

            byte[] sectionData = new byte[totalSectors * RawSectorSize];

            for (int i = 0; i < form2Count; i++)
            {
                byte[] s = BuildMode2Form2Sector(startLba + i);
                Array.Copy(s, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            byte[] form1Sector = BuildMode2Form1SectorWithNonStandardSubheader(startLba + form2Count);
            Array.Copy(form1Sector, 0, sectionData, form2Count * RawSectorSize, RawSectorSize);

            byte[] reconstructed = SimulatePackUnpackRoundTrip(sectionData, totalSectors, startLba);

            int form1Off = form2Count * RawSectorSize;

            // Original subheader: 00 00 01 FE | 80 40 20 00
            // Corrupted form:     80 40 20 00 | 80 40 20 00  (second half duplicated into first)
            byte[] origSubhdr = sectionData.Skip(form1Off + 0x10).Take(8).ToArray();
            byte[] reconSubhdr = reconstructed.Skip(form1Off + 0x10).Take(8).ToArray();

            Assert.Equal(
                string.Join(" ", origSubhdr.Select(b => b.ToString("X2"))),
                string.Join(" ", reconSubhdr.Select(b => b.ToString("X2"))));
        }

        /// <summary>
        /// The reconstructed Form1 sector must be identified as Mode2Form1, not Mode2Form2.
        /// Validates that bit 5 of submode[0x12] is preserved correctly.
        /// </summary>
        [Fact]
        public void RoundTrip_MixedForm2ThenForm1_SectorTypeIsForm1()
        {
            const int form2Count = 50;
            const long startLba = 8920;
            int totalSectors = form2Count + 1;

            byte[] sectionData = new byte[totalSectors * RawSectorSize];

            for (int i = 0; i < form2Count; i++)
            {
                byte[] s = BuildMode2Form2Sector(startLba + i);
                Array.Copy(s, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            byte[] form1Sector = BuildMode2Form1SectorWithNonStandardSubheader(startLba + form2Count);
            Array.Copy(form1Sector, 0, sectionData, form2Count * RawSectorSize, RawSectorSize);

            byte[] reconstructed = SimulatePackUnpackRoundTrip(sectionData, totalSectors, startLba);

            int form1Off = form2Count * RawSectorSize;
            byte reconSubmode = reconstructed[form1Off + 0x12];

            Assert.False(
                (reconSubmode & 0x20) != 0,
                $"Reconstructed sector 50 has submode 0x{reconSubmode:X2} — bit 5 set means Form2, " +
                $"but the original is Form1 (0x01). The subheader was not correctly restored.");
        }

        /// <summary>
        /// All 50 Form2 sectors must also survive the round-trip unchanged.
        /// </summary>
        [Fact]
        public void RoundTrip_MixedForm2ThenForm1_Form2SectorsAlsoPreserved()
        {
            const int form2Count = 50;
            const long startLba = 8920;
            int totalSectors = form2Count + 1;

            byte[] sectionData = new byte[totalSectors * RawSectorSize];

            for (int i = 0; i < form2Count; i++)
            {
                byte[] s = BuildMode2Form2Sector(startLba + i);
                Array.Copy(s, 0, sectionData, i * RawSectorSize, RawSectorSize);
            }

            byte[] form1Sector = BuildMode2Form1SectorWithNonStandardSubheader(startLba + form2Count);
            Array.Copy(form1Sector, 0, sectionData, form2Count * RawSectorSize, RawSectorSize);

            byte[] reconstructed = SimulatePackUnpackRoundTrip(sectionData, totalSectors, startLba);

            for (int s = 0; s < form2Count; s++)
            {
                int off = s * RawSectorSize;
                for (int b = 0; b < RawSectorSize; b++)
                {
                    Assert.True(
                        sectionData[off + b] == reconstructed[off + b],
                        $"Form2 sector {s}, byte 0x{b:X3}: " +
                        $"expected 0x{sectionData[off + b]:X2}, got 0x{reconstructed[off + b]:X2}");
                }
            }
        }

    // ── Diagnostic ─────────────────────────────────────────────────────────

    /// <summary>
    /// Diagnostic: directly inspect what sectorTypes[50] contains after UnpackPreEcc.
    /// This pinpoints whether the bug is in the pack (wrong flags stored) or unpack (dataPos drift).
    /// </summary>
    [Fact]
    public void Diagnostic_SectorType50FromPackIsForm1()
    {
        const int form2Count = 50;
        const long startLba = 8920;
        int totalSectors = form2Count + 1;

        byte[] sectionData = new byte[totalSectors * RawSectorSize];
        for (int i = 0; i < form2Count; i++)
        {
            byte[] s = BuildMode2Form2Sector(startLba + i);
            Array.Copy(s, 0, sectionData, i * RawSectorSize, RawSectorSize);
        }
        byte[] form1Sector = BuildMode2Form1SectorWithNonStandardSubheader(startLba + form2Count);
        Array.Copy(form1Sector, 0, sectionData, form2Count * RawSectorSize, RawSectorSize);

        // Analyse and check the flags for sector 50
        SectorFlags[] analyzedFlags = SectorPaddingPacker.Analyse(sectionData, totalSectors, startLba);
        Assert.NotNull(analyzedFlags);
        SectorFlags s50Flags = analyzedFlags[form2Count];
        SectorFlags s50Type = s50Flags & SectorFlags.TypeMask;

        Assert.True(s50Type == SectorFlags.Mode2Form1,
            $"Analyse: sector 50 type = 0x{(byte)s50Type:X2}, expected Mode2Form1 (0x01). Full flags = 0x{(byte)s50Flags:X2}");

        // Serialize and inspect via UnpackPreEcc
        byte[] pack = SectorPaddingPacker.Serialize(sectionData, analyzedFlags);
        Assert.NotNull(pack);

        byte[] buffer = new byte[totalSectors * RawSectorSize];
        // Copy user data only
        for (int i = 0; i < totalSectors; i++)
            Array.Copy(sectionData, i * RawSectorSize + 0x18, buffer, i * RawSectorSize + 0x18, 0x800);

        SectorPaddingUnpacker.UnpackPreEcc(pack, buffer, 0, totalSectors, out SectorFlags?[] sectorTypes);

        Assert.True(sectorTypes[form2Count].HasValue,
            "sectorTypes[50] should have a value (presence bit set)");

        SectorFlags unpacked50Type = sectorTypes[form2Count].Value & SectorFlags.TypeMask;
        Assert.True(unpacked50Type == SectorFlags.Mode2Form1,
            $"UnpackPreEcc: sectorTypes[50] type = 0x{(byte)unpacked50Type:X2}, expected Mode2Form1 (0x01). Full = 0x{(byte)sectorTypes[form2Count].Value:X2}");

        // Also check what byte got written to buffer[50*0x930 + 0x12]
        byte reconSubmode = buffer[form2Count * RawSectorSize + 0x12];
        Assert.True((reconSubmode & 0x20) == 0,
            $"buffer[sector50 + 0x12] = 0x{reconSubmode:X2} — Form2 bit set. Subheader was not correctly applied.");
    }
}

}
