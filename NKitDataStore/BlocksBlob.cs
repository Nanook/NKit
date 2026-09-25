using System.Buffers.Binary;

namespace NKitDataStore
{
    /// <summary>
    /// Provides binary encoding/decoding for block key arrays stored in the offset.blocks BLOB column.
    /// Format: [xxhash64: 8 bytes][crc32: 4 bytes][xxhash64: 8 bytes][crc32: 4 bytes]...
    /// Total: 12 bytes per block (Big-Endian)
    /// </summary>
    public static class BlocksBlob
    {
        private const int _BytesPerBlock = 0x8 + 0x4; // xxhash64 + crc32

        /// <summary>
        /// Encodes a list of block keys into a binary blob.
        /// </summary>
        public static byte[] Encode(List<BlockKey> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return Array.Empty<byte>();

            byte[] blob = new byte[blocks.Count * _BytesPerBlock];
            int offset = 0;

            foreach (BlockKey block in blocks)
            {
                // Write xxhash64 (8 bytes, big-endian)
                BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(offset), block.XxHash64);
                offset += 8;

                // Write crc32 (4 bytes, big-endian)
                BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(offset), block.Crc32);
                offset += 4;
            }

            return blob;
        }

        /// <summary>
        /// Decodes a binary blob into a list of block keys.
        /// </summary>
        public static List<BlockKey> Decode(byte[]? blob)
        {
            if (blob == null || blob.Length == 0)
                return new List<BlockKey>();

            if (blob.Length % _BytesPerBlock != 0)
                throw new InvalidOperationException($"Invalid blocks blob length: {blob.Length} (must be multiple of {_BytesPerBlock})");

            int blockCount = blob.Length / _BytesPerBlock;
            List<BlockKey> blocks = new List<BlockKey>(blockCount);
            int offset = 0;

            for (int i = 0; i < blockCount; i++)
            {
                // Read xxhash64 (8 bytes, big-endian)
                ulong xxhash64 = BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(offset, 8));
                offset += 8;

                // Read crc32 (4 bytes, big-endian)
                uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset, 4));
                offset += 4;

                blocks.Add(new BlockKey(xxhash64, crc32));
            }

            return blocks;
        }

        /// <summary>
        /// Gets the number of blocks encoded in a blob without fully decoding it.
        /// </summary>
        public static int GetBlockCount(byte[]? blob)
        {
            if (blob == null || blob.Length == 0)
                return 0;
            return blob.Length / _BytesPerBlock;
        }

        /// <summary>
        /// Gets a specific block key from a blob without decoding the entire blob.
        /// </summary>
        /// <param name="blob">The encoded blocks blob.</param>
        /// <param name="index">Zero-based index of the block to retrieve.</param>
        /// <returns>The block key at the specified index.</returns>
        public static BlockKey GetBlockAt(byte[] blob, int index)
        {
            int blockCount = GetBlockCount(blob);
            if (index < 0 || index >= blockCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            int offset = index * _BytesPerBlock;
            // Read in big-endian to match Encode/Decode format
            ulong xxhash64 = BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(offset, 8));
            uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset + 8, 4));

            return new BlockKey(xxhash64, crc32);
        }

        /// <summary>
        /// Finds the block key at a specific offset within the segment.
        /// </summary>
        /// <param name="blob">The encoded blocks blob.</param>
        /// <param name="offsetWithinSegment">Offset relative to the start of this offset segment.</param>
        /// <param name="blockSize">The size of each block (typically 64KB).</param>
        /// <returns>The block key and the offset within that block.</returns>
        public static (BlockKey blockKey, int offsetWithinBlock) GetBlockAtOffset(byte[] blob, long offsetWithinSegment, int blockSize)
        {
            int blockIndex = (int)(offsetWithinSegment / blockSize);
            int offsetWithinBlock = (int)(offsetWithinSegment % blockSize);

            BlockKey blockKey = GetBlockAt(blob, blockIndex);
            return (blockKey, offsetWithinBlock);
        }
    }
}