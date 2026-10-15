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

        /// <summary>
        /// Mode2Form2 sector with non-standard ExtUserData (all 0x55 pattern) through the
        /// ReadSectorTypesAndPositions pipeline. This mimics Swap Magic disc sectors where
        /// fill bytes appear in the extended user data + EDC region.
        /// The Mode2Form2 EDC at 0x92C is within ExtUserData and must survive ReconstructEcc.
        /// </summary>
        [Fact]
        public void RoundTrip_Mode2Form2_NonStandardExtUserData_Via_ReadPositions_Pipeline_ByteIdentical()
        {
            const long lba = 25;  // Swap Magic sector 25 pattern
            const int sectorCount = 1;

            // Build a Mode2Form2 sector with all-0x55 subheader and ext user data
            byte[] original = new byte[RawSectorSize];
            // User data at 0x18-0x817
            for (int i = 0; i < 0x800; i++)
                original[0x18 + i] = (byte)(i & 0xFF);
            original[0x0F] = 0x02;
            // Subheader: all 0x55, submode bit5=1 (Form2) — use 0x75 (0x55 | 0x20) for Form2
            original[0x12] = 0x75; original[0x13] = 0x55;
            original[0x10] = 0x55; original[0x11] = 0x55;
            original[0x14] = 0x55; original[0x15] = 0x55;
            original[0x16] = 0x75; original[0x17] = 0x55;
            // Extended user data: all 0x55 (includes the EDC position at 0x92C-0x92F)
            for (int i = 0x818; i < 0x930; i++)
                original[i] = 0x55;
            // ReconstructPrefix mirrors 0x14-0x17 (=0x55) into 0x10-0x13, writes standard sync+MSF
            Ecm.ReconstructPrefix(original, 0, false, lba);
            // Do NOT call ReconstructEcc — the EDC at 0x92C is intentionally non-standard (0x55 fill)

            // Analyse: should set Subheader (always for Mode2) + ExtUserData (always for Form2)
            SectorFlags[] flags = SectorPaddingPacker.Analyse(original, sectorCount, lba);
            Assert.NotNull(flags);
            Assert.Equal(SectorFlags.Mode2Form2, flags[0] & SectorFlags.TypeMask);
            Assert.NotEqual(SectorFlags.None, flags[0] & SectorFlags.Subheader);
            Assert.NotEqual(SectorFlags.None, flags[0] & SectorFlags.ExtUserData);
            byte[] pack = SectorPaddingPacker.Serialize(original, flags);
            Assert.NotNull(pack);

            // Simulate OnBufferComplete pipeline
            byte[] buffer = new byte[RawSectorSize];
            Array.Copy(original, 0x18, buffer, 0x18, 0x800);

            SectorPaddingUnpacker.ReadSectorTypesAndPositions(pack, sectorCount,
                out SectorFlags?[] sectorTypes, out int[] subheaderPositions, out int[] extDataPositions);

            Assert.True(sectorTypes[0].HasValue);
            Assert.Equal(SectorFlags.Mode2Form2, sectorTypes[0]!.Value & SectorFlags.TypeMask);

            bool isMode2Form2 = true;
            Ecm.ReconstructPrefix(buffer, 0, false, lba);
            SectorPaddingUnpacker.ApplySubheaderAndExtData(pack, buffer, 0, subheaderPositions[0], extDataPositions[0]);
            // ReconstructEcc for Mode2Form2 overwrites 0x92C-0x92F with computed value
            Ecm.ReconstructEcc(buffer, 0, false, false, isMode2Form2);

            // The Mode2Form2 EDC (last 4 bytes of ExtUserData) must be re-applied after ReconstructEcc
            // because ReconstructEcc overwrites 0x92C with the computed EDC, not the stored one.
            if (extDataPositions[0] >= 0)
            {
                const int edcOffsetInExtUserData = SectorFlagsHelper.ExtUserDataSize - SectorFlagsHelper.EdcSize;
                Array.Copy(pack, extDataPositions[0] + edcOffsetInExtUserData, buffer, 0x92C, SectorFlagsHelper.EdcSize);
            }

            SectorPaddingUnpacker.UnpackPostEcc(pack, buffer, 0, sectorCount);

            Assert.Equal(original, buffer);
        }

        [Fact]
        public void IsFullyCreatable_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(SectorPaddingPacker.IsFullyCreatable(null));
            Assert.False(SectorPaddingPacker.IsFullyCreatable(Array.Empty<SectorFlags>()));
        }

        // ── Tests for the ReadSectorTypesAndPositions + ApplySubheaderAndExtData pipeline ──
        // These cover the code path actually used by ImageBuilderIso9660Stream.OnBufferComplete
        // (as opposed to the UnpackPreEcc path used by the round-trip simulation tests).

        /// <summary>
        /// Mode2Form1 sector with non-standard subheader (all 0x55) through the
        /// ReadSectorTypesAndPositions + ApplySubheaderAndExtData + ReconstructEcc + UnpackPostEcc
        /// pipeline — byte-identical output required.
        ///
        /// This exercises the exact path used by ImageBuilderIso9660Stream.OnBufferComplete.
        /// A Swap Magic-style sector where every byte of the subheader is a fill value.
        /// </summary>
        [Fact]
        public void RoundTrip_Mode2Form1_NonStandardSubheader_Via_ReadPositions_Pipeline_ByteIdentical()
        {
            const long lba = 25;  // first non-standard sector on Swap Magic disc
            const int sectorCount = 1;

            // Build a Mode2Form1 sector with all-0x55 subheader (Swap Magic style)
            byte[] original = new byte[RawSectorSize];
            // User data at 0x18-0x817
            for (int i = 0; i < 0x800; i++)
                original[0x18 + i] = (byte)(i & 0xFF);
            original[0x0F] = 0x02;
            // Non-standard subheader: all 0x55
            for (int i = 0x10; i < 0x18; i++)
                original[i] = 0x55;
            // ReconstructPrefix mirrors 0x14-0x17 into 0x10-0x13 for Mode2
            Ecm.ReconstructPrefix(original, 0, false, lba);
            // Since we set 0x14-0x17 to 0x55 before calling ReconstructPrefix, it now wrote
            // 0x55 into 0x10-0x13 as well. Full subheader is 55 55 55 55 55 55 55 55.
            Ecm.ReconstructEcc(original, 0, false, true, false);
            // original now has correct EDC/ECC for the 55-filled subheader + user data.

            // Step 1: Analyse + Serialize → pack
            SectorFlags[] flags = SectorPaddingPacker.Analyse(original, sectorCount, lba);
            Assert.NotNull(flags);
            // Subheader must always be stored for Mode2Form1
            Assert.NotEqual(SectorFlags.None, flags[0] & SectorFlags.Subheader);
            byte[] pack = SectorPaddingPacker.Serialize(original, flags);
            Assert.NotNull(pack);

            // Step 2: Simulate the ImageBuilderIso9660Stream.OnBufferComplete pipeline
            // Buffer starts with: user data at 0x18-0x817, everything else zero (gap fill)
            byte[] buffer = new byte[RawSectorSize];
            Array.Copy(original, 0x18, buffer, 0x18, 0x800);

            // Pre-pass: ReadSectorTypesAndPositions (used by OnBufferComplete)
            SectorPaddingUnpacker.ReadSectorTypesAndPositions(pack, sectorCount,
                out SectorFlags?[] sectorTypes, out int[] subheaderPositions, out int[] extDataPositions);

            Assert.True(sectorTypes[0].HasValue, "sectorTypes[0] must be set");
            Assert.Equal(SectorFlags.Mode2Form1, sectorTypes[0].Value & SectorFlags.TypeMask);

            // Per-sector loop: ReconstructPrefix → ApplySubheaderAndExtData → ReconstructEcc
            bool isMode1 = false;
            bool isMode2Form1 = (sectorTypes[0].Value & SectorFlags.TypeMask) == SectorFlags.Mode2Form1;
            bool isMode2Form2 = (sectorTypes[0].Value & SectorFlags.TypeMask) == SectorFlags.Mode2Form2;

            Ecm.ReconstructPrefix(buffer, 0, isMode1, lba);
            SectorPaddingUnpacker.ApplySubheaderAndExtData(pack, buffer, 0, subheaderPositions[0], extDataPositions[0]);
            Ecm.ReconstructEcc(buffer, 0, isMode1, isMode2Form1, isMode2Form2);

            // Post-ECC overlay
            SectorPaddingUnpacker.UnpackPostEcc(pack, buffer, 0, sectorCount);

            // Must be byte-identical to original
            Assert.Equal(original, buffer);
        }

        /// <summary>
        /// Mode2Form1 sector with non-standard subheader AND non-standard EDC
        /// (EDC doesn't match what ReconstructEcc computes — intentional copy-protection pattern).
        /// The full ReadSectorTypesAndPositions pipeline must restore both subheader and EDC.
        /// </summary>
        [Fact]
        public void RoundTrip_Mode2Form1_NonStandardSubheaderAndEdc_Via_ReadPositions_Pipeline_ByteIdentical()
        {
            const long lba = 25;
            const int sectorCount = 1;

            // Build a Mode2Form1 sector with all-0x55 subheader
            byte[] original = new byte[RawSectorSize];
            for (int i = 0; i < 0x800; i++)
                original[0x18 + i] = (byte)(i & 0xFF);
            original[0x0F] = 0x02;
            for (int i = 0x10; i < 0x18; i++)
                original[i] = 0x55;
            Ecm.ReconstructPrefix(original, 0, false, lba);
            Ecm.ReconstructEcc(original, 0, false, true, false);
            // Now forcibly stomp the EDC with a non-standard value (copy-protection)
            original[0x818] = 0x55; original[0x819] = 0x55;
            original[0x81A] = 0x55; original[0x81B] = 0x55;

            // Analyse must detect both Subheader and Edc as non-recreatable
            SectorFlags[] flags = SectorPaddingPacker.Analyse(original, sectorCount, lba);
            Assert.NotNull(flags);
            Assert.NotEqual(SectorFlags.None, flags[0] & SectorFlags.Subheader);
            Assert.NotEqual(SectorFlags.None, flags[0] & SectorFlags.Edc);

            byte[] pack = SectorPaddingPacker.Serialize(original, flags);
            Assert.NotNull(pack);

            // Simulate ImageBuilderIso9660Stream.OnBufferComplete
            byte[] buffer = new byte[RawSectorSize];
            Array.Copy(original, 0x18, buffer, 0x18, 0x800);

            SectorPaddingUnpacker.ReadSectorTypesAndPositions(pack, sectorCount,
                out SectorFlags?[] sectorTypes, out int[] subheaderPositions, out int[] extDataPositions);

            bool isMode1 = false;
            bool isMode2Form1 = (sectorTypes[0]!.Value & SectorFlags.TypeMask) == SectorFlags.Mode2Form1;
            bool isMode2Form2 = false;

            Ecm.ReconstructPrefix(buffer, 0, isMode1, lba);
            SectorPaddingUnpacker.ApplySubheaderAndExtData(pack, buffer, 0, subheaderPositions[0], extDataPositions[0]);
            Ecm.ReconstructEcc(buffer, 0, isMode1, isMode2Form1, isMode2Form2);
            SectorPaddingUnpacker.UnpackPostEcc(pack, buffer, 0, sectorCount);

            Assert.Equal(original, buffer);
        }

        /// <summary>
        /// Multiple Mode2Form1 sectors: some with standard subheader, some with all-0x55
        /// subheader and non-standard EDC. The ReadSectorTypesAndPositions pipeline must
        /// handle per-sector variation correctly (data pointer must stay aligned across sectors).
        /// </summary>
        [Fact]
        public void RoundTrip_Mode2Form1_MixedStandardAndNonStandard_Via_ReadPositions_ByteIdentical()
        {
            const long startLba = 0;
            const int sectorCount = 30;  // mimics the first 30 sectors of Swap Magic section 0

            byte[] original = new byte[sectorCount * RawSectorSize];

            for (int s = 0; s < sectorCount; s++)
            {
                int off = s * RawSectorSize;
                // User data
                for (int i = 0; i < 0x800; i++)
                    original[off + 0x18 + i] = (byte)((s * 3 + i) & 0xFF);
                original[off + 0x0F] = 0x02;

                if (s < 25)
                {
                    // Standard subheader: 00 00 08 00 (Form1, both halves equal)
                    original[off + 0x10] = 0x00; original[off + 0x11] = 0x00;
                    original[off + 0x12] = 0x08; original[off + 0x13] = 0x00;
                    original[off + 0x14] = 0x00; original[off + 0x15] = 0x00;
                    original[off + 0x16] = 0x08; original[off + 0x17] = 0x00;
                    Ecm.ReconstructPrefix(original, off, false, startLba + s);
                    Ecm.ReconstructEcc(original, off, false, true, false);
                    // Standard sectors: EDC is recreatable, Edc flag should NOT be set
                }
                else
                {
                    // Non-standard subheader: all 0x55 (Swap Magic copy-protection)
                    for (int i = 0x10; i < 0x18; i++)
                        original[off + i] = 0x55;
                    Ecm.ReconstructPrefix(original, off, false, startLba + s);
                    Ecm.ReconstructEcc(original, off, false, true, false);
                    // Stomp EDC with non-standard value
                    original[off + 0x818] = 0x55; original[off + 0x819] = 0x55;
                    original[off + 0x81A] = 0x55; original[off + 0x81B] = 0x55;
                }
            }

            // Analyse and serialize
            SectorFlags[] analysedFlags = SectorPaddingPacker.Analyse(original, sectorCount, startLba);
            Assert.NotNull(analysedFlags);

            // Sectors 0-24: should have Subheader only (EDC recreatable given standard subheader)
            for (int s = 0; s < 25; s++)
            {
                Assert.NotEqual(SectorFlags.None, analysedFlags[s] & SectorFlags.Subheader);
                Assert.Equal(SectorFlags.None, analysedFlags[s] & SectorFlags.Edc);
            }
            // Sectors 25-29: should have Subheader + Edc (non-standard EDC)
            for (int s = 25; s < sectorCount; s++)
            {
                Assert.NotEqual(SectorFlags.None, analysedFlags[s] & SectorFlags.Subheader);
                Assert.NotEqual(SectorFlags.None, analysedFlags[s] & SectorFlags.Edc);
            }

            byte[] pack = SectorPaddingPacker.Serialize(original, analysedFlags);
            Assert.NotNull(pack);

            // Simulate ImageBuilderIso9660Stream.OnBufferComplete
            byte[] buffer = new byte[sectorCount * RawSectorSize];
            for (int s = 0; s < sectorCount; s++)
                Array.Copy(original, s * RawSectorSize + 0x18, buffer, s * RawSectorSize + 0x18, 0x800);

            SectorPaddingUnpacker.ReadSectorTypesAndPositions(pack, sectorCount,
                out SectorFlags?[] sectorTypes, out int[] subheaderPositions, out int[] extDataPositions);

            for (int s = 0; s < sectorCount; s++)
            {
                int sectorOffset = s * RawSectorSize;
                long lba = startLba + s;

                Assert.True(sectorTypes[s].HasValue, $"sectorTypes[{s}] must be set");
                Assert.Equal(SectorFlags.Mode2Form1, sectorTypes[s]!.Value & SectorFlags.TypeMask);

                Ecm.ReconstructPrefix(buffer, sectorOffset, false, lba);
                SectorPaddingUnpacker.ApplySubheaderAndExtData(pack, buffer, sectorOffset,
                    subheaderPositions[s], extDataPositions[s]);
                Ecm.ReconstructEcc(buffer, sectorOffset, false, true, false);
            }

            SectorPaddingUnpacker.UnpackPostEcc(pack, buffer, 0, sectorCount);

            Assert.Equal(original, buffer);
        }
    }
}