using Nanook.NKit;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Validates Crc.ComputeZeros (the O(log n) CRC-of-N-zero-bytes helper used by the Wii update
    /// brute-force to account for null padding after an inserted update partition) against the
    /// straightforward byte-computed CRC.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class CrcZerosTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(0x8000)]
        [InlineData(0x200000)]   // matches the scan's null-block CRC (8D89877E)
        [InlineData(0x2CF8000)]  // the Mario update null-padding size
        public void ComputeZerosMatchesByteComputed(long length)
        {
            uint expected = Crc.Compute(new byte[length]);
            uint actual = Crc.ComputeZeros(length);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ComputeZerosKnownBlockCrc() =>
            // A 0x200000 null block has CRC 8D89877E (as seen in Wii scan null Sections).
            Assert.Equal(0x8D89877Eu, Crc.ComputeZeros(0x200000));

    }
}