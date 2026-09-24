using System;

namespace Nanook.NKit
{
    /// <summary>
    /// Constants for the Sector_Padding_Pack flag byte bit assignments.
    /// Bits 0-1 encode the sector type; bits 2-7 indicate which
    /// non-recreatable components are stored in the payload.
    /// </summary>
    [Flags]
    internal enum SectorFlags : byte
    {
        None = 0x00,

        // Bits 0-1: Sector type
        TypeMask = 0x03,
        Mode1 = 0x00,  // 00
        Mode2Form1 = 0x01,  // 01
        Mode2Form2 = 0x02,  // 10
        Audio = 0x03,  // 11

        // Bits 2-7: Stored components
        Sync = 0x04,  // bit 2: 12 bytes
        MsfMode = 0x08,  // bit 3: 4 bytes (MSF + mode)
        Subheader = 0x10,  // bit 4: 8 bytes
        Edc = 0x20,  // bit 5: 4 bytes
        Ecc = 0x40,  // bit 6: 276 bytes (Mode1/Mode2Form1)
        ExtUserData = 0x80,  // bit 7: 280 bytes (Mode2Form2 only)
    }

    /// <summary>
    /// Helper constants and methods for <see cref="SectorFlags"/> payload sizes.
    /// </summary>
    internal static class SectorFlagsHelper
    {
        /// <summary>Payload size for Sync (bit 2): 12 bytes.</summary>
        public const int SyncSize = 12;

        /// <summary>Payload size for MsfMode (bit 3): 4 bytes (MSF 3 + mode 1).</summary>
        public const int MsfModeSize = 4;

        /// <summary>Payload size for Subheader (bit 4): 8 bytes.</summary>
        public const int SubheaderSize = 8;

        /// <summary>Payload size for Edc (bit 5): 4 bytes.</summary>
        public const int EdcSize = 4;

        /// <summary>Payload size for Ecc (bit 6): 276 bytes for Mode2Form1, 284 bytes for Mode1.</summary>
        public const int EccSize = 276;
        /// <summary>Payload size for Ecc (bit 6) for Mode1: 284 bytes (8 reserved + 172 P-parity + 104 Q-parity).</summary>
        public const int EccSizeMode1 = 284;

        /// <summary>Returns the ECC payload size based on sector type (Mode1=284, Mode2Form1=276).</summary>
        public static int GetEccSize(SectorFlags flags)
        {
            SectorFlags type = flags & (SectorFlags)0x03; // TypeMask
            return type == SectorFlags.Mode1 ? EccSizeMode1 : EccSize;
        }

        /// <summary>Payload size for ExtUserData (bit 7): 280 bytes.</summary>
        public const int ExtUserDataSize = 280;

        /// <summary>
        /// Indexed by bit position (2-7). Returns the payload byte count
        /// contributed by each component flag bit.
        /// Index 0 = bit 2 (Sync), index 1 = bit 3 (MsfMode), etc.
        /// </summary>
        private static readonly int[] PayloadSizesByBit = { SyncSize, MsfModeSize, SubheaderSize, EdcSize, EccSize, ExtUserDataSize };

        /// <summary>
        /// Computes the total payload length for a given flag byte by summing
        /// the fixed sizes of each set bit in positions 2-7.
        /// </summary>
        /// <param name="flags">The flag byte value.</param>
        /// <returns>Total payload byte count for the sector.</returns>
        public static int GetPayloadLength(SectorFlags flags)
        {
            int length = 0;
            byte bits = (byte)flags;

            for (int bit = 2; bit <= 7; bit++)
            {
                if ((bits & (1 << bit)) != 0)
                    length += PayloadSizesByBit[bit - 2];
            }

            return length;
        }
    }
}