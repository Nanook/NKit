using System.Buffers.Binary;

namespace NKitDataStore
{
    /// <summary>
    /// Provides binary encoding/decoding for a single block hash (xxhash64 + crc32) as a 12-byte blob.
    /// Used for the block_index and block_data tables where hash is stored as a BLOB PRIMARY KEY.
    /// Format: [xxhash64: 8 bytes][crc32: 4 bytes] (big-endian)
    /// </summary>
    public static class HashBlob
    {
        public const int HashBlobSize = 12; // 8 bytes xxhash64 + 4 bytes crc32

        /// <summary>
        /// Encodes a block key (xxhash64 + crc32) into a 12-byte blob.
        /// </summary>
        public static byte[] Encode(BlockKey key)
        {
            byte[] blob = new byte[HashBlobSize];

            // Write xxhash64 (8 bytes, big-endian)
            BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(0), key.XxHash64);

            // Write crc32 (4 bytes, big-endian)
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(8), key.Crc32);

            return blob;
        }

        /// <summary>
        /// Encodes a block key (xxhash64 + crc32) into a 12-byte blob.
        /// </summary>
        public static byte[] Encode(ulong xxhash64, uint crc32) => Encode(new BlockKey(xxhash64, crc32));

        /// <summary>
        /// Decodes a 12-byte blob into a block key.
        /// </summary>
        public static BlockKey Decode(byte[] blob)
        {
            if (blob == null || blob.Length != HashBlobSize)
                throw new ArgumentException($"Hash blob must be exactly {HashBlobSize} bytes, got {blob?.Length ?? 0}", nameof(blob));

            // Read xxhash64 (8 bytes, big-endian)
            ulong xxhash64 = BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(0, 8));

            // Read crc32 (4 bytes, big-endian)
            uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(8, 4));

            return new BlockKey(xxhash64, crc32);
        }
    }
}