using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for LBA-to-MSF computation correctness.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 2: LBA-to-MSF Computation Correctness
    /// **Validates: Requirements 3.6, 5.5**
    ///
    /// For any valid LBA value (0 to 449999, covering a full 80-minute CD),
    /// converting LBA to MSF via (LBA+150)/75/60, (LBA+150)/75%60, (LBA+150)%75
    /// and then converting back via ((M*60)+S)*75+F-150 SHALL produce the original
    /// LBA value (round-trip identity).
    /// </summary>
    public class LbaToMsfPropertyTests
    {
        /// <summary>
        /// **Validates: Requirements 3.6, 5.5**
        ///
        /// Property 2: LBA-to-MSF round-trip identity.
        ///
        /// For any valid LBA in [0, 449999], converting to MSF and back produces
        /// the original LBA value.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool LbaToMsf_RoundTrip_ProducesOriginalLba(NonNegativeInt lbaSeed)
        {
            long lba = lbaSeed.Get % 450000;

            // Convert LBA to MSF using Ecm.LbaToMsf
            (byte minute, byte second, byte frame) = Ecm.LbaToMsf(lba);

            // Convert MSF back to LBA using the inverse formula
            long roundTripped = Ecm.MsfToLba(minute, second, frame);

            return roundTripped == lba;
        }

        /// <summary>
        /// **Validates: Requirements 3.6, 5.5**
        ///
        /// Property 2: MSF components are within valid CD ranges.
        ///
        /// For any valid LBA in [0, 449999], the resulting MSF values must satisfy:
        /// minute in [0, 99], second in [0, 59], frame in [0, 74].
        /// </summary>
        [Property(MaxTest = 200)]
        public bool LbaToMsf_ProducesValidMsfRanges(NonNegativeInt lbaSeed)
        {
            long lba = lbaSeed.Get % 450000;

            (byte minute, byte second, byte frame) = Ecm.LbaToMsf(lba);

            return minute <= 99 && second <= 59 && frame <= 74;
        }

        /// <summary>
        /// **Validates: Requirements 3.6, 5.5**
        ///
        /// Property 2: MSF-to-LBA round-trip identity (reverse direction).
        ///
        /// For any valid MSF tuple where M in [0,79], S in [0,59], F in [0,74],
        /// converting to LBA and back to MSF produces the original MSF values.
        /// This confirms the conversion is bijective over the valid domain.
        /// </summary>
        [Property(MaxTest = 200)]
        public bool MsfToLba_RoundTrip_ProducesOriginalMsf(
            NonNegativeInt minuteSeed,
            NonNegativeInt secondSeed,
            NonNegativeInt frameSeed)
        {
            // Constrain to valid CD MSF ranges
            byte minute = (byte)(minuteSeed.Get % 80);
            byte second = (byte)(secondSeed.Get % 60);
            byte frame = (byte)(frameSeed.Get % 75);

            // Convert MSF to LBA
            long lba = Ecm.MsfToLba(minute, second, frame);

            // Only test if the resulting LBA is in valid range
            if (lba < 0 || lba >= 450000)
                return true; // Skip out-of-range values (vacuously true)

            // Convert back to MSF
            (byte m, byte s, byte f) = Ecm.LbaToMsf(lba);

            return m == minute && s == second && f == frame;
        }
    }
}