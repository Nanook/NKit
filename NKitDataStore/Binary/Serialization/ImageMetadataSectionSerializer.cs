using Nanook.GrindCore;
using System.Buffers.Binary;
using System.Text;

namespace NKitDataStore.Binary.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of the Image_Metadata_Section.
    /// The section contains AreaRecords and FileRecords for a single image,
    /// compressed with zstd level 19.
    /// 
    /// On-disk format: [compressed_size: uint32][zstd_compressed_payload]
    /// 
    /// Uncompressed payload:
    /// [StructureVersion: uint16][AreaCount: uint16][FileCount: uint16]
    /// [Areas...][Files...]
    /// </summary>
    internal static class ImageMetadataSectionSerializer
    {
        private const ushort CurrentStructureVersion = 1;
        private const int ZstdCompressionLevel = 19;

        /// <summary>
        /// Serializes area and file records into a compressed Image_Metadata_Section.
        /// Returns (compressedBytes, uncompressedSize).
        /// </summary>
        public static (byte[] CompressedData, int UncompressedSize) Serialize(IEnumerable<AreaRecord> areas, IEnumerable<FileRecord> files)
        {
            // Build uncompressed payload
            byte[] uncompressed = BuildUncompressedPayload(areas, files);

            // Compress with zstd level 19 using CompressionBlock
            int maxCompressedSize = uncompressed.Length + 0x100;
            byte[] compressedBuffer = new byte[maxCompressedSize];
            int compressedSize = maxCompressedSize;

            using (CompressionBlock compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = uncompressed.Length,
                    Type = (Nanook.GrindCore.CompressionType)ZstdCompressionLevel
                }))
            {
                compressor.Compress(uncompressed, 0, uncompressed.Length, compressedBuffer, 0, ref compressedSize);
            }

            byte[] compressed = new byte[compressedSize];
            Buffer.BlockCopy(compressedBuffer, 0, compressed, 0, compressedSize);

            return (compressed, uncompressed.Length);
        }

        /// <summary>
        /// Deserializes a compressed Image_Metadata_Section.
        /// Input is the raw zstd compressed payload (no size prefix).
        /// </summary>
        public static (List<AreaRecord> Areas, List<FileRecord> Files) Deserialize(ReadOnlySpan<byte> compressedData, int uncompressedSize = 0)
        {
            if (compressedData.Length == 0)
                throw new InvalidDataException("Image_Metadata_Section compressed data is empty.");

            byte[] compressed = compressedData.ToArray();

            // Use uncompressed size if known, otherwise estimate generously
            int bufferSize = uncompressedSize > 0 ? uncompressedSize : Math.Max(compressed.Length * 10, 4096);
            byte[] buffer = new byte[bufferSize];
            int decompressedSize = buffer.Length;

            using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = bufferSize,
                    Type = Nanook.GrindCore.CompressionType.Level19 //doesn't matter for decompress
                }))
            {
                decompressor.Decompress(compressed, 0, compressed.Length, buffer, 0, ref decompressedSize);
            }

            byte[] uncompressed = decompressedSize < buffer.Length
                ? buffer.AsSpan(0, decompressedSize).ToArray()
                : buffer;

            return ParseUncompressedPayload(uncompressed);
        }

        private static byte[] BuildUncompressedPayload(IEnumerable<AreaRecord> areas, IEnumerable<FileRecord> files)
        {
            List<AreaRecord> areaList = areas as List<AreaRecord> ?? areas.ToList();
            List<FileRecord> fileList = files as List<FileRecord> ?? files.ToList();

            if (areaList.Count > ushort.MaxValue)
                throw new ArgumentException($"Too many area records: {areaList.Count} (max {ushort.MaxValue})");
            if (fileList.Count > ushort.MaxValue)
                throw new ArgumentException($"Too many file records: {fileList.Count} (max {ushort.MaxValue})");

            using MemoryStream ms = new MemoryStream();

            // Header: StructureVersion (2) + AreaCount (2) + FileCount (2) = 6 bytes
            Span<byte> header = stackalloc byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(header, CurrentStructureVersion);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2), (ushort)areaList.Count);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4), (ushort)fileList.Count);
            ms.Write(header);

            // Serialize each AreaRecord
            foreach (AreaRecord area in areaList)
            {
                WriteAreaRecord(ms, area);
            }

            // Serialize each FileRecord
            foreach (FileRecord file in fileList)
            {
                WriteFileRecord(ms, file);
            }

            return ms.ToArray();
        }

        private static void WriteAreaRecord(MemoryStream ms, AreaRecord area)
        {
            // Fixed fields: Offset(8) + Size(8) + StrideBlockSize(4) + StrideDataOffset(4) +
            //               StrideDataLength(4) + SectionSize(4) + Crc32(4) + XxHash64(8) = 44 bytes
            // Then: MetadataLength(2) + MetadataBlob(variable)
            byte[] metadataBlob = area.Metadata?.ToBlob() ?? Array.Empty<byte>();

            if (metadataBlob.Length > ushort.MaxValue)
                throw new InvalidOperationException(
                    $"AreaMetadata blob too large: {metadataBlob.Length} bytes (max {ushort.MaxValue})");

            Span<byte> fixedPart = stackalloc byte[44 + 2]; // 44 fixed + 2 for MetadataLength
            BinaryPrimitives.WriteInt64BigEndian(fixedPart, area.Offset);
            BinaryPrimitives.WriteInt64BigEndian(fixedPart.Slice(8), area.Size);
            BinaryPrimitives.WriteInt32BigEndian(fixedPart.Slice(16), area.StrideBlockSize);
            BinaryPrimitives.WriteInt32BigEndian(fixedPart.Slice(20), area.StrideDataOffset);
            BinaryPrimitives.WriteInt32BigEndian(fixedPart.Slice(24), area.StrideDataLength);
            BinaryPrimitives.WriteInt32BigEndian(fixedPart.Slice(28), area.SectionSize);
            BinaryPrimitives.WriteUInt32BigEndian(fixedPart.Slice(32), area.Crc32);
            BinaryPrimitives.WriteUInt64BigEndian(fixedPart.Slice(36), area.XxHash64);
            BinaryPrimitives.WriteUInt16BigEndian(fixedPart.Slice(44), (ushort)metadataBlob.Length);

            ms.Write(fixedPart);
            if (metadataBlob.Length > 0)
                ms.Write(metadataBlob);
        }

        private static void WriteFileRecord(MemoryStream ms, FileRecord file)
        {
            // NameLength(2) + NameBytes(variable) + FileId(4) + Offset(8) + Size(8) + UncompressedSize(8) + IsSystem(1)
            byte[] nameBytes = Encoding.UTF8.GetBytes(file.Name ?? string.Empty);

            if (nameBytes.Length > ushort.MaxValue)
                throw new InvalidOperationException(
                    $"File name too long: {nameBytes.Length} bytes (max {ushort.MaxValue})");

            // Write NameLength + NameBytes
            Span<byte> nameLenBuf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(nameLenBuf, (ushort)nameBytes.Length);
            ms.Write(nameLenBuf);
            ms.Write(nameBytes);

            // Write fixed fields: FileId(4) + Offset(8) + Size(8) + UncompressedSize(8) + IsSystem(1) = 29 bytes
            Span<byte> fixedPart = stackalloc byte[29];
            BinaryPrimitives.WriteInt32BigEndian(fixedPart, file.FileId);
            BinaryPrimitives.WriteInt64BigEndian(fixedPart.Slice(4), file.Offset);
            BinaryPrimitives.WriteInt64BigEndian(fixedPart.Slice(12), file.Size);
            BinaryPrimitives.WriteInt64BigEndian(fixedPart.Slice(20), file.UncompressedSize);
            fixedPart[28] = file.IsSystem ? (byte)1 : (byte)0;

            ms.Write(fixedPart);
        }

        private static (List<AreaRecord> Areas, List<FileRecord> Files) ParseUncompressedPayload(byte[] data)
        {
            if (data.Length < 6)
                throw new InvalidDataException("Uncompressed payload too short for header.");

            ReadOnlySpan<byte> span = data.AsSpan();
            int offset = 0;

            // Read header
            ushort structureVersion = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
            offset += 2;

            if (structureVersion > CurrentStructureVersion)
                throw new NotSupportedException(
                    $"Image_Metadata_Section structure version {structureVersion} is not supported (max: {CurrentStructureVersion}).");

            ushort areaCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
            offset += 2;

            ushort fileCount = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
            offset += 2;

            // Read areas
            List<AreaRecord> areas = new List<AreaRecord>(areaCount);
            for (int i = 0; i < areaCount; i++)
            {
                (AreaRecord area, int bytesRead) = ReadAreaRecord(span, offset);
                areas.Add(area);
                offset += bytesRead;
            }

            // Read files
            List<FileRecord> files = new List<FileRecord>(fileCount);
            for (int i = 0; i < fileCount; i++)
            {
                (FileRecord file, int bytesRead) = ReadFileRecord(span, offset);
                files.Add(file);
                offset += bytesRead;
            }

            return (areas, files);
        }

        private static (AreaRecord area, int bytesRead) ReadAreaRecord(ReadOnlySpan<byte> span, int offset)
        {
            int startOffset = offset;

            // Fixed fields: 44 bytes + MetadataLength: 2 bytes
            if (span.Length < offset + 46)
                throw new InvalidDataException("Insufficient data for AreaRecord fixed fields.");

            AreaRecord area = new AreaRecord
            {
                Offset = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset)),
                Size = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset + 8)),
                StrideBlockSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 16)),
                StrideDataOffset = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 20)),
                StrideDataLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 24)),
                SectionSize = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset + 28)),
                Crc32 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 32)),
                XxHash64 = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(offset + 36))
            };
            offset += 44;

            ushort metadataLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
            offset += 2;

            if (metadataLength > 0)
            {
                if (span.Length < offset + metadataLength)
                    throw new InvalidDataException("Insufficient data for AreaMetadata blob.");

                byte[] metadataBlob = span.Slice(offset, metadataLength).ToArray();
                area.Metadata = AreaMetadata.FromBlob(metadataBlob);
                offset += metadataLength;
            }
            else
            {
                area.Metadata = new AreaMetadata();
            }

            return (area, offset - startOffset);
        }

        private static (FileRecord file, int bytesRead) ReadFileRecord(ReadOnlySpan<byte> span, int offset)
        {
            int startOffset = offset;

            // NameLength (2 bytes)
            if (span.Length < offset + 2)
                throw new InvalidDataException("Insufficient data for FileRecord name length.");

            ushort nameLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset));
            offset += 2;

            // NameBytes
            if (span.Length < offset + nameLength)
                throw new InvalidDataException("Insufficient data for FileRecord name bytes.");

            string name = Encoding.UTF8.GetString(span.Slice(offset, nameLength));
            offset += nameLength;

            // Fixed fields: FileId(4) + Offset(8) + Size(8) + UncompressedSize(8) + IsSystem(1) = 29 bytes
            if (span.Length < offset + 29)
                throw new InvalidDataException("Insufficient data for FileRecord fixed fields.");

            FileRecord file = new FileRecord
            {
                Name = name,
                FileId = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset)),
                Offset = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset + 4)),
                Size = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset + 12)),
                UncompressedSize = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset + 20)),
                IsSystem = span[offset + 28] != 0
            };
            offset += 29;

            return (file, offset - startOffset);
        }

    }
}