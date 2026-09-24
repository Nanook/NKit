using NKitDataStore;
using System.Buffers.Binary;

namespace NKDS.Converter.Sqlite;

/// <summary>
/// Encodes/decodes the block.hash BLOB column.
/// Format: 12 bytes = [xxhash64: 8 bytes BE][crc32: 4 bytes BE].
/// </summary>
public static class HashBlob
{
    private const int BlobSize = 12;

    /// <summary>
    /// Decodes a 12-byte BLOB into a BlockKey.
    /// </summary>
    public static BlockKey Decode(byte[] blob)
    {
        if (blob is null)
            throw new ArgumentNullException(nameof(blob));
        if (blob.Length < BlobSize)
            throw new ArgumentException($"BLOB must be at least {BlobSize} bytes, got {blob.Length}.", nameof(blob));

        ulong xxhash64 = BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(0, 8));
        uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(8, 4));
        return new BlockKey(xxhash64, crc32);
    }

    /// <summary>
    /// Encodes a BlockKey into a 12-byte BLOB.
    /// </summary>
    public static byte[] Encode(BlockKey key)
    {
        byte[] blob = new byte[BlobSize];
        BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(0, 8), key.XxHash64);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(8, 4), key.Crc32);
        return blob;
    }
}