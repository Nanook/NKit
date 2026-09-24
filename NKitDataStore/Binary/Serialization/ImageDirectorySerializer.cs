using Nanook.GrindCore;
using System.Buffers.Binary;
using System.Text;

namespace NKitDataStore.Binary.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of the Image_Directory structure.
    /// The binary layout is:
    ///   [StructureVersion: uint16]
    ///   [EntryCount: uint32]
    ///   [Entries: ImageDirectoryEntry[]]
    ///
    /// Each entry has variable length due to Name and System string fields.
    /// All multi-byte integers are big-endian.
    /// </summary>
    internal static class ImageDirectorySerializer
    {
        /// <summary>
        /// Current structure version for the Image_Directory format.
        /// </summary>
        private const ushort CurrentStructureVersion = 2;

        /// <summary>
        /// Sentinel value indicating a null RollbackFileId.
        /// </summary>
        private const int NullRollbackFileId = -1;

        /// <summary>
        /// Sentinel value indicating a null RollbackOffset.
        /// </summary>
        private const long NullRollbackOffset = -1L;

        /// <summary>
        /// Serializes a collection of <see cref="ImageDirectoryEntry"/> objects into a byte array.
        /// </summary>
        /// <param name="entries">The directory entries to serialize.</param>
        /// <returns>The serialized byte array containing the full Image_Directory structure.</returns>
        public static byte[] Serialize(IEnumerable<ImageDirectoryEntry> entries)
        {
            // Materialize to list for count and iteration
            IList<ImageDirectoryEntry> entryList = entries as IList<ImageDirectoryEntry> ?? entries.ToList();

            // Calculate total size needed
            int totalSize = 2 + 4; // StructureVersion (uint16) + EntryCount (uint32)
            foreach (ImageDirectoryEntry entry in entryList)
            {
                totalSize += CalculateEntrySize(entry);
            }

            byte[] buffer = new byte[totalSize];
            Span<byte> span = buffer.AsSpan();
            int offset = 0;

            // Write structure version
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), CurrentStructureVersion);
            offset += 2;

            // Write entry count
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)entryList.Count);
            offset += 4;

            // Write each entry
            foreach (ImageDirectoryEntry entry in entryList)
            {
                offset = WriteEntry(span, offset, entry);
            }

            return buffer;
        }

        /// <summary>
        /// Deserializes an Image_Directory from a byte span.
        /// </summary>
        /// <param name="data">The raw bytes containing the serialized Image_Directory.</param>
        /// <returns>A list of deserialized <see cref="ImageDirectoryEntry"/> objects.</returns>
        /// <exception cref="NotSupportedException">Thrown if the structure version is unsupported.</exception>
        public static List<ImageDirectoryEntry> Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.Length < 6) // Minimum: version (2) + count (4)
                throw new InvalidDataException("Image_Directory data is too short to contain header.");

            int offset = 0;

            // Read and validate structure version
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
            offset += 2;

            if (version > CurrentStructureVersion)
                throw new NotSupportedException(
                    $"Image_Directory structure version {version} is not supported. Maximum supported version is {CurrentStructureVersion}.");

            // Read entry count
            uint entryCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            List<ImageDirectoryEntry> entries = new List<ImageDirectoryEntry>((int)entryCount);

            for (uint i = 0; i < entryCount; i++)
            {
                ImageDirectoryEntry entry = ReadEntry(data, ref offset, version);
                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// Calculates the serialized size of a single entry.
        /// </summary>
        private static int CalculateEntrySize(ImageDirectoryEntry entry)
        {
            int nameByteCount = Encoding.UTF8.GetByteCount(entry.Name);
            int systemByteCount = entry.System != null ? Encoding.UTF8.GetByteCount(entry.System) : 0;

            return 8   // ImageId (int64)
                 + 2   // NameLength (uint16)
                 + nameByteCount // NameBytes (variable)
                 + 8   // Size (int64)
                 + 4   // Crc32 (uint32)
                 + 8   // XxHash64 (uint64)
                 + 1   // SystemLength (uint8, 0 = null)
                 + systemByteCount // SystemBytes (variable, if non-null)
                 + 1   // Format (byte)
                 + 4   // RollbackFileId (int32, -1 = null)
                 + 8   // RollbackOffset (int64, -1 = null)
                 + 1   // Removed (byte)
                 + 8   // MetadataSectionOffset (int64)
                 + 4   // MetadataSectionCompressedSize (int32)
                 + 8   // BlockMapSectionOffset (int64)
                 + 4   // BlockMapSectionCompressedSize (int32)
                 + 4   // MetadataSectionUncompressedSize (int32)
                 + 4;  // BlockMapSectionUncompressedSize (int32)
        }

        /// <summary>
        /// Writes a single <see cref="ImageDirectoryEntry"/> to the buffer at the given offset.
        /// </summary>
        /// <returns>The new offset after writing.</returns>
        private static int WriteEntry(Span<byte> buffer, int offset, ImageDirectoryEntry entry)
        {
            // ImageId (int64)
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(offset), entry.ImageId);
            offset += 8;

            // NameLength (uint16) + NameBytes (variable)
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(offset), (ushort)nameBytes.Length);
            offset += 2;
            nameBytes.CopyTo(buffer.Slice(offset));
            offset += nameBytes.Length;

            // Size (int64)
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(offset), entry.Size);
            offset += 8;

            // Crc32 (uint32)
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(offset), entry.Crc32);
            offset += 4;

            // XxHash64 (uint64)
            BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(offset), entry.XxHash64);
            offset += 8;

            // SystemLength (uint8) + SystemBytes (variable)
            if (entry.System == null)
            {
                buffer[offset] = 0;
                offset += 1;
            }
            else
            {
                byte[] systemBytes = Encoding.UTF8.GetBytes(entry.System);
                buffer[offset] = (byte)systemBytes.Length;
                offset += 1;
                systemBytes.CopyTo(buffer.Slice(offset));
                offset += systemBytes.Length;
            }

            // Format (byte)
            buffer[offset] = (byte)entry.Format;
            offset += 1;

            // RollbackFileId (int32, -1 = null)
            int rollbackFileId = entry.RollbackFileId ?? NullRollbackFileId;
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), rollbackFileId);
            offset += 4;

            // RollbackOffset (int64, -1 = null)
            long rollbackOffset = entry.RollbackOffset ?? NullRollbackOffset;
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(offset), rollbackOffset);
            offset += 8;

            // Removed (byte)
            buffer[offset] = entry.Removed ? (byte)1 : (byte)0;
            offset += 1;

            // MetadataSectionOffset (int64)
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(offset), entry.MetadataSectionOffset);
            offset += 8;

            // MetadataSectionCompressedSize (int32)
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), entry.MetadataSectionCompressedSize);
            offset += 4;

            // BlockMapSectionOffset (int64)
            BinaryPrimitives.WriteInt64BigEndian(buffer.Slice(offset), entry.BlockMapSectionOffset);
            offset += 8;

            // BlockMapSectionCompressedSize (int32)
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), entry.BlockMapSectionCompressedSize);
            offset += 4;

            // MetadataSectionUncompressedSize (int32) — added in version 2
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), entry.MetadataSectionUncompressedSize);
            offset += 4;

            // BlockMapSectionUncompressedSize (int32) — added in version 2
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), entry.BlockMapSectionUncompressedSize);
            offset += 4;

            return offset;
        }

        /// <summary>
        /// Reads a single <see cref="ImageDirectoryEntry"/> from the data at the given offset.
        /// </summary>
        /// <returns>The deserialized entry.</returns>
        private static ImageDirectoryEntry ReadEntry(ReadOnlySpan<byte> data, ref int offset, ushort version)
        {
            ImageDirectoryEntry entry = new ImageDirectoryEntry();

            // ImageId (int64)
            entry.ImageId = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset));
            offset += 8;

            // NameLength (uint16) + NameBytes (variable)
            ushort nameLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
            offset += 2;
            entry.Name = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
            offset += nameLength;

            // Size (int64)
            entry.Size = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset));
            offset += 8;

            // Crc32 (uint32)
            entry.Crc32 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            offset += 4;

            // XxHash64 (uint64)
            entry.XxHash64 = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset));
            offset += 8;

            // SystemLength (uint8) + SystemBytes (variable)
            byte systemLength = data[offset];
            offset += 1;
            if (systemLength == 0)
            {
                entry.System = null;
            }
            else
            {
                entry.System = Encoding.UTF8.GetString(data.Slice(offset, systemLength));
                offset += systemLength;
            }

            // Format (byte)
            entry.Format = (ImageFormat)data[offset];
            offset += 1;

            // RollbackFileId (int32, -1 = null)
            int rollbackFileId = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
            offset += 4;
            entry.RollbackFileId = rollbackFileId == NullRollbackFileId ? null : rollbackFileId;

            // RollbackOffset (int64, -1 = null)
            long rollbackOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset));
            offset += 8;
            entry.RollbackOffset = rollbackOffset == NullRollbackOffset ? null : rollbackOffset;

            // Removed (byte)
            entry.Removed = data[offset] != 0;
            offset += 1;

            // MetadataSectionOffset (int64)
            entry.MetadataSectionOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset));
            offset += 8;

            // MetadataSectionCompressedSize (int32)
            entry.MetadataSectionCompressedSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
            offset += 4;

            // BlockMapSectionOffset (int64)
            entry.BlockMapSectionOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset));
            offset += 8;

            // BlockMapSectionCompressedSize (int32)
            entry.BlockMapSectionCompressedSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
            offset += 4;

            // Version 2+ fields: uncompressed sizes
            if (version >= 2)
            {
                entry.MetadataSectionUncompressedSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
                offset += 4;
                entry.BlockMapSectionUncompressedSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
                offset += 4;
            }

            return entry;
        }

        /// <summary>
        /// Zstd compression level used for directory compression.
        /// </summary>
        private const int ZstdCompressionLevel = 19;

        /// <summary>
        /// Compresses a serialized Image Directory using zstd.
        /// </summary>
        /// <param name="serialized">The uncompressed serialized directory bytes.</param>
        /// <returns>The zstd-compressed bytes.</returns>
        public static byte[] CompressDirectory(byte[] serialized)
        {
            int maxCompressedSize = serialized.Length + 0x100;
            byte[] compressedBuffer = new byte[maxCompressedSize];
            int compressedSize = maxCompressedSize;

            using (CompressionBlock compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = serialized.Length,
                    Type = (Nanook.GrindCore.CompressionType)ZstdCompressionLevel
                }))
            {
                compressor.Compress(serialized, 0, serialized.Length, compressedBuffer, 0, ref compressedSize);
            }

            byte[] compressed = new byte[compressedSize];
            Buffer.BlockCopy(compressedBuffer, 0, compressed, 0, compressedSize);
            return compressed;
        }

        /// <summary>
        /// Decompresses a zstd-compressed Image Directory into a pre-allocated buffer.
        /// </summary>
        /// <param name="compressed">The zstd-compressed directory bytes.</param>
        /// <param name="uncompressedSize">The known uncompressed size (from the file header).</param>
        /// <returns>The decompressed directory bytes.</returns>
        public static byte[] DecompressDirectory(byte[] compressed, int uncompressedSize)
        {
            byte[] buffer = new byte[uncompressedSize];
            int decompressedSize = buffer.Length;

            using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = uncompressedSize,
                    Type = Nanook.GrindCore.CompressionType.Level19 // doesn't matter for decompress
                }))
            {
                decompressor.Decompress(compressed, 0, compressed.Length, buffer, 0, ref decompressedSize);
            }

            if (decompressedSize < buffer.Length)
                return buffer.AsSpan(0, decompressedSize).ToArray();

            return buffer;
        }
    }
}