using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for ISO9660 sector reconstruction round-trip.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 1: ISO9660 Sector Reconstruction Round-Trip
    /// **Validates: Requirements 3.7, 5.2, 5.3, 5.4**
    ///
    /// For any valid 2048-byte user data block and any valid LBA value, reconstructing a full
    /// 2352-byte sector (by generating sync pattern, MSF from LBA, mode byte, EDC, and ECC)
    /// and then extracting the user data at the correct offset SHALL produce the original
    /// user data unchanged. The same holds for Mode2Form1 and Mode2Form2.
    /// </summary>
    public class Iso9660SectorReconstructionPropertyTests
    {
        private const int RawSectorSize = 0x930; // 2352 bytes
        private const int Mode1UserDataOffset = 0x10;
        private const int Mode1UserDataLength = 0x800; // 2048 bytes
        private const int Mode2UserDataOffset = 0x18;
        private const int Mode2Form1UserDataLength = 0x800; // 2048 bytes
        private const int Mode2Form2UserDataLength = 0x914; // 2324 bytes

        // Valid LBA range: 0 to 449999 (full 80-minute CD)
        private const int MaxLba = 449999;

        /// <summary>
        /// **Validates: Requirements 3.7, 5.2, 5.3, 5.4**
        ///
        /// Property 1: ISO9660 Sector Reconstruction Round-Trip — Mode1
        ///
        /// For any valid 2048-byte user data and any valid LBA value (0-449999),
        /// placing user data at offset 0x10 in a 2352-byte sector buffer, calling
        /// ReconstructPrefix (mode1=true) and ReconstructEcc (mode1=true), then
        /// extracting bytes at offset 0x10 with length 0x800 SHALL produce the
        /// original user data unchanged.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode1_ReconstructAndExtract_PreservesUserData(byte[] arbitraryData, NonNegativeInt lbaInt)
        {
            if (arbitraryData == null || arbitraryData.Length == 0)
                return true; // trivial case, skip

            // Generate exactly 2048 bytes of user data from arbitrary input
            byte[] userData = normalizeToLength(arbitraryData, Mode1UserDataLength);
            long lba = (long)(lbaInt.Get % (MaxLba + 1));

            // Create a 2352-byte sector buffer and place user data at Mode1 offset
            byte[] sector = new byte[RawSectorSize];
            Array.Copy(userData, 0, sector, Mode1UserDataOffset, Mode1UserDataLength);

            // Reconstruct the sector header (sync, MSF, mode byte)
            Ecm.ReconstructPrefix(sector, 0, mode1: true, lba);

            // Reconstruct EDC and ECC
            Ecm.ReconstructEcc(sector, 0, mode1: true, mode2Form1: false, mode2Form2: false);

            // Extract user data back from the reconstructed sector
            byte[] extracted = new byte[Mode1UserDataLength];
            Array.Copy(sector, Mode1UserDataOffset, extracted, 0, Mode1UserDataLength);

            // Verify identity
            return userData.SequenceEqual(extracted);
        }

        /// <summary>
        /// **Validates: Requirements 3.7, 5.2, 5.3, 5.4**
        ///
        /// Property 1: ISO9660 Sector Reconstruction Round-Trip — Mode2Form1
        ///
        /// For any valid 2048-byte user data and any valid LBA value (0-449999),
        /// placing user data at offset 0x18 in a 2352-byte sector buffer with a valid
        /// Mode2Form1 subheader, calling ReconstructPrefix (mode1=false) and
        /// ReconstructEcc (mode2Form1=true), then extracting bytes at offset 0x18
        /// with length 0x800 SHALL produce the original user data unchanged.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form1_ReconstructAndExtract_PreservesUserData(byte[] arbitraryData, NonNegativeInt lbaInt, byte subHeaderByte)
        {
            if (arbitraryData == null || arbitraryData.Length == 0)
                return true; // trivial case, skip

            // Generate exactly 2048 bytes of user data from arbitrary input
            byte[] userData = normalizeToLength(arbitraryData, Mode2Form1UserDataLength);
            long lba = (long)(lbaInt.Get % (MaxLba + 1));

            // Create a 2352-byte sector buffer
            byte[] sector = new byte[RawSectorSize];

            // Set up Mode2Form1 subheader (8 bytes at offset 0x10-0x17)
            // Subheader byte at 0x12 must NOT have bit 5 set (Form1)
            byte subMode = (byte)(subHeaderByte & ~0x20); // Clear bit 5 for Form1
            sector[0x10] = 0x00; // File number
            sector[0x11] = 0x00; // Channel number
            sector[0x12] = subMode; // Sub-mode (Form1: bit 5 = 0)
            sector[0x13] = 0x00; // Coding info
            // Duplicate subheader (bytes 0x14-0x17 mirror 0x10-0x13)
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Place user data at Mode2 offset
            Array.Copy(userData, 0, sector, Mode2UserDataOffset, Mode2Form1UserDataLength);

            // Reconstruct the sector header (sync, MSF, mode byte)
            // Note: ReconstructPrefix for mode2 copies sector[0x14] to sector[0x10] (flags)
            Ecm.ReconstructPrefix(sector, 0, mode1: false, lba);

            // Reconstruct EDC and ECC
            Ecm.ReconstructEcc(sector, 0, mode1: false, mode2Form1: true, mode2Form2: false);

            // Extract user data back from the reconstructed sector
            byte[] extracted = new byte[Mode2Form1UserDataLength];
            Array.Copy(sector, Mode2UserDataOffset, extracted, 0, Mode2Form1UserDataLength);

            // Verify identity
            return userData.SequenceEqual(extracted);
        }

        /// <summary>
        /// **Validates: Requirements 3.7, 5.2, 5.3, 5.4**
        ///
        /// Property 1: ISO9660 Sector Reconstruction Round-Trip — Mode2Form2
        ///
        /// For any valid 2324-byte user data and any valid LBA value (0-449999),
        /// placing user data at offset 0x18 in a 2352-byte sector buffer with a valid
        /// Mode2Form2 subheader, calling ReconstructPrefix (mode1=false) and
        /// ReconstructEcc (mode2Form2=true), then extracting bytes at offset 0x18
        /// with length 0x914 SHALL produce the original user data unchanged.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool Mode2Form2_ReconstructAndExtract_PreservesUserData(byte[] arbitraryData, NonNegativeInt lbaInt, byte subHeaderByte)
        {
            if (arbitraryData == null || arbitraryData.Length == 0)
                return true; // trivial case, skip

            // Generate exactly 2324 bytes of user data from arbitrary input
            byte[] userData = normalizeToLength(arbitraryData, Mode2Form2UserDataLength);
            long lba = (long)(lbaInt.Get % (MaxLba + 1));

            // Create a 2352-byte sector buffer
            byte[] sector = new byte[RawSectorSize];

            // Set up Mode2Form2 subheader (8 bytes at offset 0x10-0x17)
            // Subheader byte at 0x12 must have bit 5 set (Form2)
            byte subMode = (byte)(subHeaderByte | 0x20); // Set bit 5 for Form2
            sector[0x10] = 0x00; // File number
            sector[0x11] = 0x00; // Channel number
            sector[0x12] = subMode; // Sub-mode (Form2: bit 5 = 1)
            sector[0x13] = 0x00; // Coding info
            // Duplicate subheader (bytes 0x14-0x17 mirror 0x10-0x13)
            sector[0x14] = sector[0x10];
            sector[0x15] = sector[0x11];
            sector[0x16] = sector[0x12];
            sector[0x17] = sector[0x13];

            // Place user data at Mode2 offset (2324 bytes)
            Array.Copy(userData, 0, sector, Mode2UserDataOffset, Mode2Form2UserDataLength);

            // Reconstruct the sector header (sync, MSF, mode byte)
            // Note: ReconstructPrefix for mode2 copies sector[0x14] to sector[0x10] (flags)
            Ecm.ReconstructPrefix(sector, 0, mode1: false, lba);

            // Reconstruct EDC (no ECC for Mode2Form2)
            Ecm.ReconstructEcc(sector, 0, mode1: false, mode2Form1: false, mode2Form2: true);

            // Extract user data back from the reconstructed sector
            byte[] extracted = new byte[Mode2Form2UserDataLength];
            Array.Copy(sector, Mode2UserDataOffset, extracted, 0, Mode2Form2UserDataLength);

            // Verify identity
            return userData.SequenceEqual(extracted);
        }

        /// <summary>
        /// Normalizes arbitrary byte data to a specific length by repeating/truncating.
        /// This ensures we always have exactly the required number of bytes for user data.
        /// </summary>
        private static byte[] normalizeToLength(byte[] source, int targetLength)
        {
            byte[] result = new byte[targetLength];
            for (int i = 0; i < targetLength; i++)
                result[i] = source[i % source.Length];
            return result;
        }
    }
}