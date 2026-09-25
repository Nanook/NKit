using System.Buffers.Binary;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// Represents the 12-byte footer at the end of an embedded-mode .nkds file.
    /// Layout: [IndexSize: int64 BE (8 bytes)] [FooterMagic: uint32 BE (4 bytes)]
    /// The footer enables readers to locate the embedded binary index by reading
    /// the last 12 bytes of the file.
    /// </summary>
    internal readonly struct EmbeddedFooter
    {
        /// <summary>
        /// Fixed size of the footer in bytes.
        /// </summary>
        public const int FooterSize = 12;

        /// <summary>
        /// Magic value identifying the file as containing an embedded binary index.
        /// ASCII "NKDS" (0x4E4B4453) stored as big-endian uint32.
        /// </summary>
        public const uint FooterMagic = 0x4E4B4453;

        /// <summary>
        /// The byte length of the embedded binary index region (excludes the 12-byte footer).
        /// </summary>
        public long IndexSize { get; }

        public EmbeddedFooter(long indexSize)
        {
            IndexSize = indexSize;
        }

        /// <summary>
        /// Serializes the footer to exactly 12 bytes:
        /// [IndexSize: int64 big-endian (8 bytes)] [FooterMagic: 4 bytes (0x4E, 0x4B, 0x44, 0x53)]
        /// </summary>
        public byte[] Serialize()
        {
            byte[] buffer = new byte[FooterSize];
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(0, 8), IndexSize);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), FooterMagic);
            return buffer;
        }

        /// <summary>
        /// Deserializes a 12-byte buffer into an EmbeddedFooter.
        /// Returns null if the data is too short or the magic bytes don't match.
        /// </summary>
        public static EmbeddedFooter? Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.Length < FooterSize)
                return null;

            if (!IsMagicValid(data.Slice(8, 4)))
                return null;

            long indexSize = BinaryPrimitives.ReadInt64BigEndian(data.Slice(0, 8));
            return new EmbeddedFooter(indexSize);
        }

        /// <summary>
        /// Checks if the given 4 bytes match the FooterMagic (0x4E, 0x4B, 0x44, 0x53).
        /// </summary>
        public static bool IsMagicValid(ReadOnlySpan<byte> lastFourBytes)
        {
            if (lastFourBytes.Length < 4)
                return false;

            uint value = BinaryPrimitives.ReadUInt32BigEndian(lastFourBytes);
            return value == FooterMagic;
        }
    }
}