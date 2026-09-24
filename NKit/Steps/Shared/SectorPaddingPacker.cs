using System;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Produces a Sector_Padding_Pack from a validated section of raw ISO9660 sectors.
    /// Detects non-recreatable bytes by comparing actual sector data against values
    /// computed by Ecm.ReconstructPrefix and Ecm.ReconstructEcc.
    ///
    /// The per-sector reconstruct-and-compare (<see cref="Analyse"/>) is the expensive part and is
    /// intended to run ONCE, in parallel, in the ISO9660 SectionProcessor. <see cref="Serialize"/>
    /// then builds the compact on-disk pack from the precomputed flags (cheap). <see cref="Pack"/>
    /// remains as Analyse+Serialize for callers/tests that want the whole operation in one step.
    /// </summary>
    internal static class SectorPaddingPacker
    {
        public const byte FormatVersion = 0x01;
        public const int RawSectorSize = 0x930; // 2352 bytes

        /// <summary>
        /// Analyses a section of raw sectors and returns the per-sector <see cref="SectorFlags"/>
        /// describing sector type (bits 0-1) and which non-recreatable components differ from the
        /// deterministically computed values (bits 2-7). This is the CPU-heavy reconstruct-and-compare
        /// pass; run it once (in the parallel SectionProcessor) and hand the result to
        /// <see cref="Serialize"/>. Returns null if input is invalid.
        /// </summary>
        /// <param name="sectionData">The decrypted section byte array containing raw 2352-byte sectors.</param>
        /// <param name="sectorCount">Number of sectors in the section.</param>
        /// <param name="startLba">The starting LBA for the first sector (from PhysicalOffset).</param>
        public static SectorFlags[] Analyse(byte[] sectionData, int sectorCount, long startLba)
        {
            if (sectionData == null || sectionData.Length == 0 || sectorCount <= 0)
                return null;

            SectorFlags[] sectorFlags = new SectorFlags[sectorCount];

            // Temp buffer for computing expected (reconstructed) sector values
            byte[] reconstructed = new byte[RawSectorSize];

            for (int i = 0; i < sectorCount; i++)
            {
                int sectorOffset = i * RawSectorSize;
                long lba = startLba + i;

                // Determine sector type from raw bytes (mode byte at 0x0F, submode at 0x12)
                byte modeByte = sectionData[sectorOffset + 0x0F];
                bool isMode1 = false;
                bool isMode2Form1 = false;
                bool isMode2Form2 = false;

                if (modeByte == 0x02)
                {
                    byte subMode = sectionData[sectorOffset + 0x12];
                    isMode2Form2 = (subMode & 0x20) != 0;
                    isMode2Form1 = !isMode2Form2;
                }
                else
                {
                    isMode1 = true;
                }

                // Set sector type bits (0-1)
                SectorFlags flags = SectorFlags.None;
                if (isMode1)
                    flags |= SectorFlags.Mode1;
                else if (isMode2Form1)
                    flags |= SectorFlags.Mode2Form1;
                else if (isMode2Form2)
                    flags |= SectorFlags.Mode2Form2;

                // Build the "reconstructed" sector to compare against.
                // Copy the original sector data as a base (user data is preserved).
                Array.Copy(sectionData, sectorOffset, reconstructed, 0, RawSectorSize);

                // Apply ReconstructPrefix: writes standard sync + computed MSF + mode
                Ecm.ReconstructPrefix(reconstructed, 0, isMode1, lba);

                // Check sync (12 bytes at offset 0x00)
                bool syncMismatch = false;
                for (int s = 0; s < 12; s++)
                {
                    if (sectionData[sectorOffset + s] != reconstructed[s])
                    {
                        syncMismatch = true;
                        break;
                    }
                }
                if (syncMismatch)
                    flags |= SectorFlags.Sync;

                // Check MSF + mode (4 bytes at offset 0x0C)
                bool msfModeMismatch = false;
                for (int m = 0x0C; m < 0x10; m++)
                {
                    if (sectionData[sectorOffset + m] != reconstructed[m])
                    {
                        msfModeMismatch = true;
                        break;
                    }
                }
                if (msfModeMismatch)
                    flags |= SectorFlags.MsfMode;

                // Mode2 sectors: unconditionally store subheader (8 bytes at offset 0x10)
                if (isMode2Form1 || isMode2Form2)
                    flags |= SectorFlags.Subheader;

                // Now compute expected EDC and ECC.
                // ReconstructEcc uses the sector's current prefix bytes for computation,
                // so we apply it to the reconstructed buffer (which has correct prefix).
                Ecm.ReconstructEcc(reconstructed, 0, isMode1, isMode2Form1, isMode2Form2);

                // Check EDC (4 bytes at type-specific offset)
                int edcOffset = GetEdcOffset(flags);
                bool edcMismatch = false;
                for (int e = 0; e < 4; e++)
                {
                    if (sectionData[sectorOffset + edcOffset + e] != reconstructed[edcOffset + e])
                    {
                        edcMismatch = true;
                        break;
                    }
                }
                if (edcMismatch)
                    flags |= SectorFlags.Edc;

                // Check ECC region - only for Mode1 and Mode2Form1
                // For Mode1: check 276 bytes from 0x814 (8 reserved + 268 ECC bytes)
                //   ReconstructEcc zeros 0x814-0x81B and writes ECC at 0x81C
                // For Mode2Form1: check 276 bytes from 0x81C (ECC bytes)
                if (isMode1 || isMode2Form1)
                {
                    int eccOff = GetEccOffset(flags);
                    bool eccMismatch = false;
                    for (int e = 0; e < SectorFlagsHelper.GetEccSize(flags); e++)
                    {
                        if (sectionData[sectorOffset + eccOff + e] != reconstructed[eccOff + e])
                        {
                            eccMismatch = true;
                            break;
                        }
                    }
                    if (eccMismatch)
                        flags |= SectorFlags.Ecc;
                }

                // Mode2Form2: unconditionally store extended user data (280 bytes at offset 0x818)
                if (isMode2Form2)
                    flags |= SectorFlags.ExtUserData;

                sectorFlags[i] = flags;
            }

            return sectorFlags;
        }

        /// <summary>
        /// Returns true if the precomputed per-sector flags indicate the whole section is
        /// fully recreatable — i.e. NO sector needs sync, MSF/mode, EDC, ECC, subheader, or
        /// extended user data stored.
        ///
        /// Subheader (Mode2 CD-XA, 8 bytes at 0x10) and ExtUserData (Mode2Form2, 280 bytes at 0x818)
        /// are CONTENT that CANNOT be regenerated from the LBA + user data — and the Mode2 subheader
        /// is itself an INPUT to the EDC/ECC computation. They MUST therefore be persisted in the
        /// pack. A section that contains any of them is NOT fully creatable: skipping its pack would
        /// drop the subheader/ext-data and the reader would reconstruct EDC/ECC over a zeroed
        /// subheader, producing bytes that differ from the original (a Mode2/2352 round-trip failure).
        /// Only Mode1/audio sections with all-standard sync/MSF/EDC/ECC are truly creatable.
        /// Returns false if <paramref name="sectorFlags"/> is null/empty.
        /// </summary>
        public static bool IsFullyCreatable(SectorFlags[] sectorFlags)
        {
            if (sectorFlags == null || sectorFlags.Length == 0)
                return false;

            const SectorFlags nonRecreatable = SectorFlags.Sync | SectorFlags.MsfMode | SectorFlags.Edc | SectorFlags.Ecc
                                             | SectorFlags.Subheader | SectorFlags.ExtUserData;
            foreach (SectorFlags f in sectorFlags)
            {
                if ((f & nonRecreatable) != 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Serializes precomputed per-sector flags into the compact Sector_Padding_Pack byte array
        /// (version + header + presence bitmap + per-present-sector flag byte and payloads).
        /// Cheap: no reconstruction — reads the stored payload bytes directly from
        /// <paramref name="sectionData"/> for the components each flag marks. Returns null if input
        /// is invalid.
        /// </summary>
        public static byte[] Serialize(byte[] sectionData, SectorFlags[] sectorFlags)
        {
            if (sectionData == null || sectionData.Length == 0 || sectorFlags == null || sectorFlags.Length == 0)
                return null;

            int sectorCount = sectorFlags.Length;
            int bitmapLength = (sectorCount + 7) / 8;
            int headerSize = 1 + 2 + bitmapLength;

            byte[] bitmap = new byte[bitmapLength];

            using (MemoryStream payloadStream = new MemoryStream())
            {
                // First pass: build presence bitmap (a sector has stored data if any component bits 2-7 are set)
                for (int i = 0; i < sectorCount; i++)
                {
                    if (((byte)sectorFlags[i] & 0xFC) != 0)
                        bitmap[i / 8] |= (byte)(1 << (i % 8));
                }

                // Second pass: write flag bytes and payloads for sectors with presence bit set
                for (int i = 0; i < sectorCount; i++)
                {
                    if ((bitmap[i / 8] & (1 << (i % 8))) == 0)
                        continue;

                    int sectorOffset = i * RawSectorSize;
                    SectorFlags flags = sectorFlags[i];

                    // Write flag byte
                    payloadStream.WriteByte((byte)flags);

                    // Write payloads in bit order (bits 2-7)

                    // Bit 2: Sync (12 bytes at offset 0x00)
                    if ((flags & SectorFlags.Sync) != 0)
                        payloadStream.Write(sectionData, sectorOffset + 0x00, SectorFlagsHelper.SyncSize);

                    // Bit 3: MSF + Mode (4 bytes at offset 0x0C)
                    if ((flags & SectorFlags.MsfMode) != 0)
                        payloadStream.Write(sectionData, sectorOffset + 0x0C, SectorFlagsHelper.MsfModeSize);

                    // Bit 4: Subheader (8 bytes at offset 0x10)
                    if ((flags & SectorFlags.Subheader) != 0)
                        payloadStream.Write(sectionData, sectorOffset + 0x10, SectorFlagsHelper.SubheaderSize);

                    // Bit 5: EDC (4 bytes at type-specific offset)
                    if ((flags & SectorFlags.Edc) != 0)
                    {
                        int edcOff = GetEdcOffset(flags);
                        payloadStream.Write(sectionData, sectorOffset + edcOff, SectorFlagsHelper.EdcSize);
                    }

                    // Bit 6: ECC (276 bytes at type-specific offset)
                    if ((flags & SectorFlags.Ecc) != 0)
                    {
                        int eccOff = GetEccOffset(flags);
                        payloadStream.Write(sectionData, sectorOffset + eccOff, SectorFlagsHelper.GetEccSize(flags));
                    }

                    // Bit 7: Extended User Data (280 bytes at offset 0x818)
                    if ((flags & SectorFlags.ExtUserData) != 0)
                        payloadStream.Write(sectionData, sectorOffset + 0x818, SectorFlagsHelper.ExtUserDataSize);
                }

                // Build the final pack
                byte[] payload = payloadStream.ToArray();
                byte[] result = new byte[headerSize + payload.Length];

                // Write version byte
                result[0] = FormatVersion;

                // Write sector count (2 bytes, little-endian)
                result[1] = (byte)(sectorCount & 0xFF);
                result[2] = (byte)((sectorCount >> 8) & 0xFF);

                // Write presence bitmap
                Array.Copy(bitmap, 0, result, 3, bitmapLength);

                // Write flag bytes and payloads
                Array.Copy(payload, 0, result, headerSize, payload.Length);

                return result;
            }
        }

        /// <summary>
        /// Packs a section of raw sectors into a Sector_Padding_Pack byte array in one step
        /// (Analyse + Serialize). Prefer the split path (Analyse in the parallel SectionProcessor,
        /// Serialize in the formatter) to avoid a second full per-sector pass. Returns null if input
        /// is invalid.
        /// </summary>
        /// <param name="sectionData">The decrypted section byte array containing raw 2352-byte sectors.</param>
        /// <param name="sectorCount">Number of sectors in the section.</param>
        /// <param name="startLba">The starting LBA for the first sector (from PhysicalOffset).</param>
        /// <returns>The packed byte array (version + header + flags + payload), or null if input is invalid.</returns>
        public static byte[] Pack(byte[] sectionData, int sectorCount, long startLba)
        {
            SectorFlags[] flags = Analyse(sectionData, sectorCount, startLba);
            if (flags == null)
                return null;
            return Serialize(sectionData, flags);
        }

        /// <summary>
        /// Gets the EDC offset within a sector based on the sector type encoded in the flags.
        /// </summary>
        private static int GetEdcOffset(SectorFlags flags)
        {
            SectorFlags type = flags & SectorFlags.TypeMask;
            return type switch
            {
                SectorFlags.Mode1 => 0x810,
                SectorFlags.Mode2Form1 => 0x818,
                SectorFlags.Mode2Form2 => 0x92C,
                _ => 0x810
            };
        }

        /// <summary>
        /// Gets the ECC offset within a sector based on the sector type encoded in the flags.
        /// For Mode1: 0x814 (reserved bytes + ECC region).
        /// For Mode2Form1: 0x81C.
        /// </summary>
        private static int GetEccOffset(SectorFlags flags)
        {
            SectorFlags type = flags & SectorFlags.TypeMask;
            return type switch
            {
                SectorFlags.Mode1 => 0x814,
                SectorFlags.Mode2Form1 => 0x81C,
                _ => 0x81C
            };
        }
    }
}