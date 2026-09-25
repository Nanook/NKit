using NKitDataStore;
using System.Buffers.Binary;

namespace NKDS.Converter.Sqlite;

/// <summary>
/// Encodes/decodes the offset.blocks BLOB column.
/// Format: N × 12 bytes = [xxhash64: 8 bytes BE][crc32: 4 bytes BE] repeated.
/// </summary>
public static class BlocksBlob
{
    private const int EntrySize = 12;

    /// <summary>
    /// Decodes a BLOB into a list of BlockKey values.
    /// null/empty BLOB → empty list.
    /// </summary>
    public static List<BlockKey> Decode(byte[]? blob)
    {
        if (blob is null || blob.Length == 0)
            return new List<BlockKey>();

        int count = blob.Length / EntrySize;
        List<BlockKey> keys = new List<BlockKey>(count);

        for (int i = 0; i < count; i++)
        {
            int offset = i * EntrySize;
            ulong xxhash64 = BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(offset, 8));
            uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(offset + 8, 4));
            keys.Add(new BlockKey(xxhash64, crc32));
        }

        return keys;
    }

    /// <summary>
    /// Encodes a list of BlockKey values into a BLOB.
    /// null or empty list → null BLOB.
    /// </summary>
    public static byte[]? Encode(List<BlockKey>? keys)
    {
        if (keys is null || keys.Count == 0)
            return null;

        byte[] blob = new byte[keys.Count * EntrySize];

        for (int i = 0; i < keys.Count; i++)
        {
            int offset = i * EntrySize;
            BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(offset, 8), keys[i].XxHash64);
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(offset + 8, 4), keys[i].Crc32);
        }

        return blob;
    }
}