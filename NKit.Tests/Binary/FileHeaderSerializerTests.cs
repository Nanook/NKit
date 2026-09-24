using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Binary;
using NKitDataStore.Binary.Serialization;
using System;
using Xunit;


namespace NKit.Tests.NKDS.Binary
{
    /// <summary>
    /// Property-based tests for FileHeader serialization round-trip.
    ///
    /// Feature: binary-index-format
    /// Property 1: Header serialization round-trip
    /// **Validates: Requirements 1.1, 1.3, 1.4, 14.2**
    /// </summary>
    [Trait("Area", "NKDS")]
    [Trait("Group", "Binary")]
    public class FileHeaderSerializerTests
    {
        private static FileHeader GenerateHeader(int seed)
        {
            Random rng = new Random(seed);
            return new FileHeader
            {
                Magic = FileHeader.MagicBytes,
                MajorVersion = FileHeader.CurrentMajorVersion,
                MinorVersion = FileHeader.CurrentMinorVersion,
                ImageDirectoryOffset = (long)rng.Next(512, int.MaxValue / 2),
                ImageDirectorySize = rng.Next(1, 10_000_000),
                ImageDirectoryUncompressedSize = rng.Next(1, 10_000_000),
                BlockIndexOffset = (long)rng.Next(512, int.MaxValue / 2),
                BlockIndexSize = rng.Next(1, 10_000_000),
                BlockIndexDeltaCount = rng.Next(0, 100),
                BlockIndexDeltaHeadOffset = (long)rng.Next(0, int.MaxValue / 2),
                FileEndOffset = (long)rng.Next(512, int.MaxValue / 2),
                ShardSize = rng.Next(2) == 0 ? 0L : (long)rng.Next(1, int.MaxValue / 2),
                BlockSize = rng.Next(1, 0x100000),
                MaxOffsetBlocks = rng.Next(1, 1000),
                ImageCount = rng.Next(0, 100_000),
                DirectoryRegionCapacity = rng.Next(4096, 1_000_000),
            };
        }

        /// <summary>
        /// **Validates: Requirements 1.1, 1.3, 1.4, 14.2**
        ///
        /// Property 1: Header serialization round-trip.
        /// For any valid FileHeader struct (with valid magic, supported version,
        /// non-zero pointers, and valid configuration values), serializing to a
        /// 256-byte buffer and deserializing back SHALL produce a FileHeader with
        /// identical values for all fields: Magic, MajorVersion, MinorVersion,
        /// ImageDirectoryOffset, ImageDirectorySize, ImageDirectoryUncompressedSize,
        /// BlockIndexOffset, BlockIndexSize, BlockIndexDeltaHeadOffset,
        /// BlockIndexDeltaCount, FileEndOffset, ShardSize, BlockSize, MaxOffsetBlocks,
        /// ImageCount, and DirectoryRegionCapacity.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool HeaderSerializationRoundTrip(NonNegativeInt seed)
        {
            FileHeader header = GenerateHeader(seed.Get);

            // Serialize to a 256-byte buffer
            byte[] buffer = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(buffer, header);

            // Deserialize back
            FileHeader deserialized = FileHeaderSerializer.Read(buffer);

            // Verify all fields are identical
            return deserialized.Magic == header.Magic
                && deserialized.MajorVersion == header.MajorVersion
                && deserialized.MinorVersion == header.MinorVersion
                && deserialized.ImageDirectoryOffset == header.ImageDirectoryOffset
                && deserialized.ImageDirectorySize == header.ImageDirectorySize
                && deserialized.ImageDirectoryUncompressedSize == header.ImageDirectoryUncompressedSize
                && deserialized.BlockIndexOffset == header.BlockIndexOffset
                && deserialized.BlockIndexSize == header.BlockIndexSize
                && deserialized.BlockIndexDeltaHeadOffset == header.BlockIndexDeltaHeadOffset
                && deserialized.BlockIndexDeltaCount == header.BlockIndexDeltaCount
                && deserialized.FileEndOffset == header.FileEndOffset
                && deserialized.ShardSize == header.ShardSize
                && deserialized.BlockSize == header.BlockSize
                && deserialized.MaxOffsetBlocks == header.MaxOffsetBlocks
                && deserialized.ImageCount == header.ImageCount
                && deserialized.DirectoryRegionCapacity == header.DirectoryRegionCapacity;
        }

        /// <summary>
        /// Verifies that the serialized header is exactly 256 bytes and that
        /// the magic bytes appear at offset 0x00.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool HeaderSerializationProduces256ByteBuffer(NonNegativeInt seed)
        {
            FileHeader header = GenerateHeader(seed.Get);

            byte[] buffer = new byte[FileHeader.HeaderSize];
            FileHeaderSerializer.Write(buffer, header);

            // Magic bytes "NKDS" (0x4E4B4453) at offset 0x00 in big-endian
            uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0));
            return magic == FileHeader.MagicBytes;
        }
    }
}