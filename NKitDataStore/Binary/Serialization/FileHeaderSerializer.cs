using Nanook.GrindCore.XXHash;
using System.Buffers.Binary;

namespace NKitDataStore.Binary.Serialization
{
    /// <summary>
    /// Handles reading and writing the 256-byte file header.
    /// All fields are stored in big-endian byte order at fixed offsets.
    /// A XXHash64 checksum at offset 0x50 covers bytes 0x00–0x4F.
    /// </summary>
    internal static class FileHeaderSerializer
    {
        private const int ChecksumRegionSize = 0x54; // Bytes 0x00–0x53 (84 bytes)
        private const int ChecksumOffset = 0x54;

        /// <summary>
        /// Deserializes a <see cref="FileHeader"/> from a 256-byte buffer.
        /// Validates the XXHash64 checksum before returning.
        /// </summary>
        /// <param name="data">A read-only span of at least <see cref="FileHeader.HeaderSize"/> bytes.</param>
        /// <returns>The deserialized <see cref="FileHeader"/>.</returns>
        /// <exception cref="InvalidDataException">Thrown if the checksum does not match.</exception>
        public static FileHeader Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < FileHeader.HeaderSize)
                throw new ArgumentException($"Buffer must be at least {FileHeader.HeaderSize} bytes.", nameof(data));

            // Validate checksum: XXHash64 of bytes 0x00–0x4F stored at offset 0x50
            ulong storedChecksum = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(ChecksumOffset));
            ulong computedChecksum = ComputeChecksum(data.Slice(0, ChecksumRegionSize));

            if (storedChecksum != computedChecksum)
                throw new InvalidDataException(
                    $"Header checksum mismatch. Stored: 0x{storedChecksum:X16}, Computed: 0x{computedChecksum:X16}");

            FileHeader header = new FileHeader();

            header.Magic = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0x00));
            header.MajorVersion = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0x04));
            header.MinorVersion = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0x06));
            header.ImageDirectoryOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x08));
            header.ImageDirectorySize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x10));
            header.ImageDirectoryUncompressedSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x14));
            header.BlockIndexOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x18));
            header.BlockIndexSize = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x20));
            header.BlockIndexDeltaCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x28));
            header.BlockIndexDeltaHeadOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x2C));
            header.FileEndOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x34));
            header.ShardSize = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0x3C));
            header.BlockSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x44));
            header.MaxOffsetBlocks = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x48));
            header.ImageCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x4C));
            header.DirectoryRegionCapacity = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x50));
            // 0x54: checksum (8 bytes) - already validated above
            // 0x5C–0xFF: reserved (164 bytes)

            return header;
        }

        /// <summary>
        /// Serializes a <see cref="FileHeader"/> into a 256-byte buffer.
        /// The buffer is zero-filled first, then fields are written at their fixed offsets,
        /// and finally the XXHash64 checksum is computed and written at offset 0x50.
        /// </summary>
        /// <param name="buffer">A span of at least <see cref="FileHeader.HeaderSize"/> bytes.</param>
        /// <param name="header">The header to serialize.</param>
        public static void Write(Span<byte> buffer, FileHeader header)
        {
            if (buffer.Length < FileHeader.HeaderSize)
                throw new ArgumentException($"Buffer must be at least {FileHeader.HeaderSize} bytes.", nameof(buffer));

            // Zero-fill the entire 256-byte buffer
            buffer.Slice(0, FileHeader.HeaderSize).Clear();

            // Write fields at their fixed offsets
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(0x00), header.Magic);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0x04), header.MajorVersion);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0x06), header.MinorVersion);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x08), header.ImageDirectoryOffset);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x10), header.ImageDirectorySize);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x14), header.ImageDirectoryUncompressedSize);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x18), header.BlockIndexOffset);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x20), header.BlockIndexSize);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x28), header.BlockIndexDeltaCount);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x2C), header.BlockIndexDeltaHeadOffset);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x34), header.FileEndOffset);
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(0x3C), header.ShardSize);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x44), header.BlockSize);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x48), header.MaxOffsetBlocks);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x4C), header.ImageCount);
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(0x50), header.DirectoryRegionCapacity);

            // Compute and write XXHash64 checksum of bytes 0x00–0x53 at offset 0x54
            ulong checksum = ComputeChecksum(buffer.Slice(0, ChecksumRegionSize));
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(ChecksumOffset), checksum);

            // 0x5C–0xFF: reserved (164 bytes) - already zeroed
        }

        /// <summary>
        /// Computes the XXHash64 checksum over the given data region.
        /// </summary>
        private static ulong ComputeChecksum(ReadOnlySpan<byte> data)
        {
            byte[] array = data.ToArray();
            return XXHash64.Compute(array, 0, array.Length);
        }
    }
}