namespace Nanook.NKit
{
    /// <summary>
    /// Scans audio partition data to find the first non-zero byte position
    /// and computes a BlockSize-aligned trim offset.
    /// </summary>
    internal static class LeadingZeroScanner
    {
        /// <summary>
        /// Scans the provided data buffer for the first non-zero byte.
        /// Returns the byte offset of the first non-zero byte relative to <paramref name="dataOffset"/>,
        /// or <paramref name="dataLength"/> if all bytes are zero.
        /// </summary>
        /// <param name="data">The audio partition data buffer.</param>
        /// <param name="dataOffset">Start offset within the buffer.</param>
        /// <param name="dataLength">Number of bytes to scan.</param>
        /// <returns>Position of first non-zero byte relative to dataOffset, or dataLength if all zeros.</returns>
        public static long FindFirstNonZero(byte[] data, int dataOffset, int dataLength)
        {
            for (int i = 0; i < dataLength; i++)
            {
                if (data[dataOffset + i] != 0)
                    return i;
            }
            return dataLength;
        }

        /// <summary>
        /// Computes the BlockSize-aligned Trim_Offset from a raw first-non-zero position.
        /// Rounds down to the nearest BlockSize boundary.
        /// Returns 0 if <paramref name="firstNonZeroPosition"/> is less than <paramref name="blockSize"/>.
        /// </summary>
        /// <param name="firstNonZeroPosition">Byte offset of first non-zero byte.</param>
        /// <param name="blockSize">The DataStore set's configured block size.</param>
        /// <returns>The aligned trim offset.</returns>
        public static long ComputeAlignedTrimOffset(long firstNonZeroPosition, int blockSize)
        {
            if (firstNonZeroPosition < blockSize)
                return 0;

            return firstNonZeroPosition / blockSize * blockSize;
        }
    }
}