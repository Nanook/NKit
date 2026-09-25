using Nanook.GrindCore;
using System.Buffers.Binary;

namespace NKitDataStore.Binary.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of the Image_BlockMap_Section.
    /// The section stores OffsetRecords and a block location table, compressed with zstd level 19.
    /// 
    /// On-disk format: [compressed_size: uint32][zstd_compressed_payload]
    /// 
    /// Uncompressed payload layout:
    ///   [StructureVersion: uint16]
    ///   [OffsetCount: uint32]
    ///   [BlockLocationCount: uint32]
    ///   [BlockLocations[]: (XxHash64:uint64, Crc32:uint32, FileId:int32, Offset:int64, Size:int32) × BlockLocationCount]
    ///   [Offsets[]: serialized OffsetRecord array with block keys as uint32 indices into BlockLocations]
    /// </summary>
    internal static class ImageBlockMapSectionSerializer
    {
        private const ushort CurrentStructureVersion = 1;

        // Each BlockLocation entry: XxHash64(8) + Crc32(4) + FileId(4) + Offset(8) + Size(4) = 28 bytes
        private const int BlockLocationEntrySize = 28;

        /// <summary>
        /// Serializes OffsetRecords and their associated block locations into a compressed section.
        /// Returns (compressedBytes, uncompressedSize).
        /// </summary>
        public static (byte[] CompressedData, int UncompressedSize) Serialize(IEnumerable<OffsetRecord> offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations)
        {
            List<OffsetRecord> offsetList = offsets as List<OffsetRecord> ?? offsets.ToList();

            // Build the block location table and index lookup
            // We need a stable ordering for the block location table
            List<KeyValuePair<BlockKey, (int FileId, long Offset, int Size)>> locationEntries = blockLocations.ToList();
            Dictionary<BlockKey, uint> blockKeyToIndex = new Dictionary<BlockKey, uint>(locationEntries.Count);
            for (int i = 0; i < locationEntries.Count; i++)
            {
                blockKeyToIndex[locationEntries[i].Key] = (uint)i;
            }

            // Calculate uncompressed payload size
            int payloadSize = CalculatePayloadSize(offsetList, locationEntries.Count);

            // Write uncompressed payload
            byte[] payload = new byte[payloadSize];
            int pos = 0;

            // StructureVersion (uint16)
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(pos), CurrentStructureVersion);
            pos += 2;

            // OffsetCount (uint32)
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(pos), (uint)offsetList.Count);
            pos += 4;

            // BlockLocationCount (uint32)
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(pos), (uint)locationEntries.Count);
            pos += 4;

            // BlockLocations[]
            for (int i = 0; i < locationEntries.Count; i++)
            {
                KeyValuePair<BlockKey, (int FileId, long Offset, int Size)> entry = locationEntries[i];
                BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(pos), entry.Key.XxHash64);
                pos += 8;
                BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(pos), entry.Key.Crc32);
                pos += 4;
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(pos), entry.Value.FileId);
                pos += 4;
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(pos), entry.Value.Offset);
                pos += 8;
                BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(pos), entry.Value.Size);
                pos += 4;
            }

            // Offsets[]
            foreach (OffsetRecord offset in offsetList)
            {
                // Offset (int64)
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(pos), offset.Offset);
                pos += 8;

                // Size (int64)
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(pos), offset.Size);
                pos += 8;

                // Type (byte)
                payload[pos] = (byte)offset.Type;
                pos += 1;

                // OffsetStart (int64)
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(pos), offset.OffsetStart);
                pos += 8;

                // BlockCount (uint16)
                ushort blockCount = (ushort)(offset.Blocks?.Count ?? 0);
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(pos), blockCount);
                pos += 2;

                // BlockIndices[] (uint32 indices into BlockLocations)
                if (offset.Blocks != null)
                {
                    foreach (BlockKey blockKey in offset.Blocks)
                    {
                        uint index = blockKeyToIndex[blockKey];
                        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(pos), index);
                        pos += 4;
                    }
                }
            }

            // Compress with zstd level 19 using CompressionBlock
            int maxCompressed = payload.Length + 0x100;
            byte[] compressedBuffer = new byte[maxCompressed];
            int compressedSize = maxCompressed;

            using (CompressionBlock compressor = CompressionBlockFactory.Create(
                CompressionAlgorithm.ZStd,
                new CompressionOptions
                {
                    BlockSize = payload.Length,
                    Type = (Nanook.GrindCore.CompressionType)19
                }))
            {
                compressor.Compress(payload, 0, payload.Length, compressedBuffer, 0, ref compressedSize);
            }

            byte[] compressed = new byte[compressedSize];
            Buffer.BlockCopy(compressedBuffer, 0, compressed, 0, compressedSize);

            return (compressed, payload.Length);
        }

        /// <summary>
        /// Deserializes a compressed Image_BlockMap_Section.
        /// Takes the raw zstd compressed payload (no size prefix).
        /// </summary>
        public static (List<OffsetRecord> Offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> BlockLocations) Deserialize(ReadOnlySpan<byte> compressedData, int uncompressedSize = 0)
        {
            if (compressedData.Length == 0)
                throw new InvalidDataException("Image_BlockMap_Section compressed data is empty.");

            byte[] compressed = compressedData.ToArray();

            // Estimate uncompressed size if not provided
            int estimatedSize = uncompressedSize > 0 ? uncompressedSize : Math.Max(compressed.Length * 4, 1024);
            byte[] payload;

            try
            {
                byte[] buffer = new byte[estimatedSize];
                int decompressedSize = buffer.Length;

                using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = estimatedSize,
                        Type = (Nanook.GrindCore.CompressionType)19
                    }))
                {
                    decompressor.Decompress(compressed, 0, compressed.Length, buffer, 0, ref decompressedSize);
                }

                payload = decompressedSize < buffer.Length
                    ? buffer.AsSpan(0, decompressedSize).ToArray()
                    : buffer;
            }
            catch
            {
                // Retry with larger buffer
                estimatedSize = compressed.Length * 20;
                byte[] buffer = new byte[estimatedSize];
                int decompressedSize = buffer.Length;

                using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = estimatedSize,
                        Type = (Nanook.GrindCore.CompressionType)19
                    }))
                {
                    decompressor.Decompress(compressed, 0, compressed.Length, buffer, 0, ref decompressedSize);
                }

                payload = new byte[decompressedSize];
                Buffer.BlockCopy(buffer, 0, payload, 0, decompressedSize);
            }

            // Parse uncompressed payload
            int pos = 0;

            // StructureVersion (uint16)
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(pos));
            pos += 2;

            if (version > CurrentStructureVersion)
                throw new NotSupportedException($"Image_BlockMap_Section structure version {version} is not supported. Maximum supported version is {CurrentStructureVersion}.");

            // OffsetCount (uint32)
            uint offsetCount = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos));
            pos += 4;

            // BlockLocationCount (uint32)
            uint blockLocationCount = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos));
            pos += 4;

            // BlockLocations[]
            BlockKey[] blockKeys = new BlockKey[blockLocationCount];
            (int FileId, long Offset, int Size)[] locations = new (int, long, int)[blockLocationCount];
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations = new Dictionary<BlockKey, (int, long, int)>((int)blockLocationCount);

            for (uint i = 0; i < blockLocationCount; i++)
            {
                ulong xxHash64 = BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(pos));
                pos += 8;
                uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos));
                pos += 4;
                int fileId = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(pos));
                pos += 4;
                long offset = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(pos));
                pos += 8;
                int size = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(pos));
                pos += 4;

                BlockKey key = new BlockKey(xxHash64, crc32);
                blockKeys[i] = key;
                locations[i] = (fileId, offset, size);
                blockLocations[key] = (fileId, offset, size);
            }

            // Offsets[]
            List<OffsetRecord> offsets = new List<OffsetRecord>((int)offsetCount);
            for (uint i = 0; i < offsetCount; i++)
            {
                OffsetRecord record = new OffsetRecord();

                // Offset (int64)
                record.Offset = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(pos));
                pos += 8;

                // Size (int64)
                record.Size = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(pos));
                pos += 8;

                // Type (byte)
                record.Type = (BlockType)payload[pos];
                pos += 1;

                // OffsetStart (int64)
                record.OffsetStart = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(pos));
                pos += 8;

                // BlockCount (uint16)
                ushort blockCount = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(pos));
                pos += 2;

                // BlockIndices[] (uint32 indices into BlockLocations)
                if (blockCount > 0)
                {
                    record.Blocks = new List<BlockKey>(blockCount);
                    for (int b = 0; b < blockCount; b++)
                    {
                        uint blockIndex = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos));
                        pos += 4;
                        record.Blocks.Add(blockKeys[blockIndex]);
                    }
                }
                else
                {
                    record.Blocks = new List<BlockKey>();
                }

                offsets.Add(record);
            }

            return (offsets, blockLocations);
        }

        /// <summary>
        /// Calculates the total uncompressed payload size for the given data.
        /// </summary>
        private static int CalculatePayloadSize(List<OffsetRecord> offsets, int blockLocationCount)
        {
            // Header: StructureVersion(2) + OffsetCount(4) + BlockLocationCount(4) = 10
            int size = 10;

            // BlockLocations: 28 bytes each
            size += blockLocationCount * BlockLocationEntrySize;

            // Offsets: each has fixed part + variable BlockIndices
            foreach (OffsetRecord offset in offsets)
            {
                // Offset(8) + Size(8) + Type(1) + OffsetStart(8) + BlockCount(2) = 27
                size += 27;
                // BlockIndices: uint32 each
                size += (offset.Blocks?.Count ?? 0) * 4;
            }

            return size;
        }

    }
}