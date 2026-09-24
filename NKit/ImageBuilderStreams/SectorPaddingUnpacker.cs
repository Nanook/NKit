using System;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Reads a Sector_Padding_Pack and overlays non-recreatable bytes onto
    /// regenerated sectors in the output buffer.
    /// </summary>
    internal static class SectorPaddingUnpacker
    {
        /// <summary>Format version supported by this unpacker.</summary>
        private const byte SupportedVersion = 0x01;

        /// <summary>Raw sector size in bytes (2352).</summary>
        private const int RawSectorSize = 0x930;

        // Sector offsets for overlay writes
        private const int SyncOffset = 0x00;
        private const int MsfModeOffset = 0x0C;
        private const int SubheaderOffset = 0x10;
        private const int ExtUserDataOffset = 0x818;

        // EDC offsets by sector type
        private const int EdcOffsetMode1 = 0x810;
        private const int EdcOffsetMode2Form1 = 0x818;
        private const int EdcOffsetMode2Form2 = 0x92C;

        // ECC offsets by sector type
        private const int EccOffsetMode1 = 0x814;
        private const int EccOffsetMode2Form1 = 0x81C;

        /// <summary>
        /// Validates the pack header and returns the sector count.
        /// Throws <see cref="InvalidDataException"/> if the version is unsupported.
        /// </summary>
        /// <param name="packData">The raw Sector_Padding_Pack bytes from BlockPadding record.</param>
        /// <param name="bitmapOffset">Receives the byte offset where the presence bitmap starts.</param>
        /// <param name="bitmapLength">Receives the length of the presence bitmap in bytes.</param>
        /// <returns>The sector count read from the header.</returns>
        public static int ReadHeader(byte[] packData, out int bitmapOffset, out int bitmapLength)
        {
            if (packData == null || packData.Length < 3)
                throw new InvalidDataException("Pack data is too short to contain a valid header.");

            byte version = packData[0];
            if (version != SupportedVersion)
                throw new InvalidDataException($"Unsupported Sector_Padding_Pack version: 0x{version:X2}. Expected 0x{SupportedVersion:X2}.");

            // Sector count is stored as 2-byte little-endian unsigned integer at offset 1
            int sectorCount = packData[1] | (packData[2] << 8);

            // Bitmap starts immediately after version (1) + sector count (2) = offset 3
            bitmapOffset = 3;
            bitmapLength = (sectorCount + 7) / 8;

            return sectorCount;
        }

        /// <summary>
        /// Unpacks stored non-recreatable bytes and overlays them onto the buffer.
        /// </summary>
        /// <param name="packData">The raw Sector_Padding_Pack bytes from BlockPadding record.</param>
        /// <param name="buffer">The output buffer containing regenerated sectors.</param>
        /// <param name="bufferOffset">Offset within buffer where this section starts.</param>
        /// <param name="sectorCount">Expected number of sectors (for validation).</param>
        public static void Unpack(byte[] packData, byte[] buffer, int bufferOffset, int sectorCount)
        {
            int headerSectorCount = ReadHeader(packData, out int bitmapOffset, out int bitmapLength);

            // Process the minimum of header sector count and provided sector count
            int sectorsToProcess = Math.Min(headerSectorCount, sectorCount);

            // Data pointer starts after the bitmap
            int dataPos = bitmapOffset + bitmapLength;

            for (int i = 0; i < sectorsToProcess; i++)
            {
                // Check presence bitmap: bit i
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                bool hasData = (packData[bitmapOffset + byteIndex] & (1 << bitIndex)) != 0;

                if (!hasData)
                    continue;

                // Read flag byte
                if (dataPos >= packData.Length)
                    throw new InvalidDataException($"Pack data truncated at byte position {dataPos} while reading flag byte for sector {i}.");

                SectorFlags flags = (SectorFlags)packData[dataPos++];
                SectorFlags sectorType = flags & SectorFlags.TypeMask;

                // Calculate the base offset for this sector in the output buffer
                int sectorBase = bufferOffset + (i * RawSectorSize);

                // Overlay stored bytes in bit order (bits 2-7)

                // Bit 2: Sync (12 bytes at offset 0x00)
                if ((flags & SectorFlags.Sync) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + SyncOffset, SectorFlagsHelper.SyncSize);
                    dataPos += SectorFlagsHelper.SyncSize;
                }

                // Bit 3: MSF/mode (4 bytes at offset 0x0C)
                if ((flags & SectorFlags.MsfMode) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + MsfModeOffset, SectorFlagsHelper.MsfModeSize);
                    dataPos += SectorFlagsHelper.MsfModeSize;
                }

                // Bit 4: Subheader (8 bytes at offset 0x10)
                if ((flags & SectorFlags.Subheader) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + SubheaderOffset, SectorFlagsHelper.SubheaderSize);
                    dataPos += SectorFlagsHelper.SubheaderSize;
                }

                // Bit 5: EDC (4 bytes at type-dependent offset)
                if ((flags & SectorFlags.Edc) != 0)
                {
                    int edcOffset = GetEdcOffset(sectorType);
                    Array.Copy(packData, dataPos, buffer, sectorBase + edcOffset, SectorFlagsHelper.EdcSize);
                    dataPos += SectorFlagsHelper.EdcSize;
                }

                // Bit 6: ECC (276/284 bytes at type-dependent offset)
                if ((flags & SectorFlags.Ecc) != 0)
                {
                    int eccSize = SectorFlagsHelper.GetEccSize(sectorType);
                    int eccOffset = GetEccOffset(sectorType);
                    Array.Copy(packData, dataPos, buffer, sectorBase + eccOffset, eccSize);
                    dataPos += eccSize;
                }

                // Bit 7: Extended user data (280 bytes at offset 0x818, Mode2Form2 only)
                if ((flags & SectorFlags.ExtUserData) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + ExtUserDataOffset, SectorFlagsHelper.ExtUserDataSize);
                    dataPos += SectorFlagsHelper.ExtUserDataSize;
                }
            }
        }

        /// <summary>
        /// Pre-ECC phase: applies only subheader (bit 4) and extended user data (bit 7) from the pack
        /// onto the buffer. These must be written BEFORE ReconstructEcc is called because EDC/ECC
        /// computation for Mode2 sectors includes the subheader and extended user data bytes.
        /// Also populates sectorTypes array so the caller knows each sector's type from the pack
        /// (needed because the buffer may not have subheader bytes yet for Form1/Form2 detection).
        /// </summary>
        /// <param name="packData">The raw Sector_Padding_Pack bytes from BlockPadding record.</param>
        /// <param name="buffer">The output buffer containing regenerated sectors.</param>
        /// <param name="bufferOffset">Offset within buffer where this section starts.</param>
        /// <param name="sectorCount">Expected number of sectors (for validation).</param>
        /// <param name="sectorTypes">Output array receiving the sector type for each sector from the pack flags.
        /// Null entries mean the sector had no presence bit (conforming Mode1).</param>
        public static void UnpackPreEcc(byte[] packData, byte[] buffer, int bufferOffset, int sectorCount, out SectorFlags?[] sectorTypes)
        {
            int headerSectorCount = ReadHeader(packData, out int bitmapOffset, out int bitmapLength);
            int sectorsToProcess = Math.Min(headerSectorCount, sectorCount);

            sectorTypes = new SectorFlags?[sectorCount];

            // Data pointer starts after the bitmap
            int dataPos = bitmapOffset + bitmapLength;

            for (int i = 0; i < sectorsToProcess; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                bool hasData = (packData[bitmapOffset + byteIndex] & (1 << bitIndex)) != 0;

                if (!hasData)
                    continue;

                if (dataPos >= packData.Length)
                    throw new InvalidDataException($"Pack data truncated at byte position {dataPos} while reading flag byte for sector {i}.");

                SectorFlags flags = (SectorFlags)packData[dataPos++];
                sectorTypes[i] = flags;

                int sectorBase = bufferOffset + (i * RawSectorSize);

                // Skip sync payload (bit 2)
                if ((flags & SectorFlags.Sync) != 0)
                    dataPos += SectorFlagsHelper.SyncSize;

                // Skip MSF/mode payload (bit 3)
                if ((flags & SectorFlags.MsfMode) != 0)
                    dataPos += SectorFlagsHelper.MsfModeSize;

                // Bit 4: Subheader — APPLY NOW (needed for EDC/ECC computation)
                if ((flags & SectorFlags.Subheader) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + SubheaderOffset, SectorFlagsHelper.SubheaderSize);
                    dataPos += SectorFlagsHelper.SubheaderSize;
                }

                // Skip EDC payload (bit 5)
                if ((flags & SectorFlags.Edc) != 0)
                    dataPos += SectorFlagsHelper.EdcSize;

                // Skip ECC payload (bit 6)
                if ((flags & SectorFlags.Ecc) != 0)
                    dataPos += SectorFlagsHelper.GetEccSize(flags);

                // Bit 7: Extended user data — APPLY NOW (needed for Mode2Form2 EDC computation)
                if ((flags & SectorFlags.ExtUserData) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + ExtUserDataOffset, SectorFlagsHelper.ExtUserDataSize);
                    dataPos += SectorFlagsHelper.ExtUserDataSize;
                }
            }
        }

        /// <summary>
        /// Post-ECC phase: applies sync (bit 2), MSF/mode (bit 3), EDC (bit 5), and ECC (bit 6)
        /// overlays from the pack. Called AFTER ReconstructEcc so that non-standard EDC/ECC
        /// values overwrite the computed ones.
        /// Subheader (bit 4) and ExtUserData (bit 7) are skipped since they were already applied
        /// in the pre-ECC phase.
        /// </summary>
        public static void UnpackPostEcc(byte[] packData, byte[] buffer, int bufferOffset, int sectorCount)
        {
            int headerSectorCount = ReadHeader(packData, out int bitmapOffset, out int bitmapLength);
            int sectorsToProcess = Math.Min(headerSectorCount, sectorCount);

            int dataPos = bitmapOffset + bitmapLength;

            for (int i = 0; i < sectorsToProcess; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                bool hasData = (packData[bitmapOffset + byteIndex] & (1 << bitIndex)) != 0;

                if (!hasData)
                    continue;

                if (dataPos >= packData.Length)
                    throw new InvalidDataException($"Pack data truncated at byte position {dataPos} while reading flag byte for sector {i}.");

                SectorFlags flags = (SectorFlags)packData[dataPos++];
                SectorFlags sectorType = flags & SectorFlags.TypeMask;

                int sectorBase = bufferOffset + (i * RawSectorSize);

                // Bit 2: Sync — APPLY NOW
                if ((flags & SectorFlags.Sync) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + SyncOffset, SectorFlagsHelper.SyncSize);
                    dataPos += SectorFlagsHelper.SyncSize;
                }

                // Bit 3: MSF/mode — APPLY NOW
                if ((flags & SectorFlags.MsfMode) != 0)
                {
                    Array.Copy(packData, dataPos, buffer, sectorBase + MsfModeOffset, SectorFlagsHelper.MsfModeSize);
                    dataPos += SectorFlagsHelper.MsfModeSize;
                }

                // Bit 4: Subheader — SKIP (already applied in pre-ECC phase)
                if ((flags & SectorFlags.Subheader) != 0)
                    dataPos += SectorFlagsHelper.SubheaderSize;

                // Bit 5: EDC — APPLY NOW (overwrites computed EDC with non-standard value)
                if ((flags & SectorFlags.Edc) != 0)
                {
                    int edcOffset = GetEdcOffset(sectorType);
                    Array.Copy(packData, dataPos, buffer, sectorBase + edcOffset, SectorFlagsHelper.EdcSize);
                    dataPos += SectorFlagsHelper.EdcSize;
                }

                // Bit 6: ECC — APPLY NOW (overwrites computed ECC with non-standard value)
                if ((flags & SectorFlags.Ecc) != 0)
                {
                    int eccSize = SectorFlagsHelper.GetEccSize(sectorType);
                    int eccOffset = GetEccOffset(sectorType);
                    Array.Copy(packData, dataPos, buffer, sectorBase + eccOffset, eccSize);
                    dataPos += eccSize;
                }

                // Bit 7: Extended user data — SKIP (already applied in pre-ECC phase)
                if ((flags & SectorFlags.ExtUserData) != 0)
                    dataPos += SectorFlagsHelper.ExtUserDataSize;
            }
        }

        /// <summary>
        /// Returns the EDC write offset within a sector based on sector type.
        /// </summary>
        private static int GetEdcOffset(SectorFlags sectorType)
        {
            return sectorType switch
            {
                SectorFlags.Mode1 => EdcOffsetMode1,
                SectorFlags.Mode2Form1 => EdcOffsetMode2Form1,
                SectorFlags.Mode2Form2 => EdcOffsetMode2Form2,
                _ => EdcOffsetMode1, // fallback for Audio or unknown
            };
        }

        /// <summary>
        /// Returns the ECC write offset within a sector based on sector type.
        /// </summary>
        private static int GetEccOffset(SectorFlags sectorType)
        {
            return sectorType switch
            {
                SectorFlags.Mode1 => EccOffsetMode1,
                SectorFlags.Mode2Form1 => EccOffsetMode2Form1,
                _ => EccOffsetMode1, // fallback; ECC not applicable to Mode2Form2/Audio
            };
        }
    }
}