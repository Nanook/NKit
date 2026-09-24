using Nanook.GrindCore;
using System.Buffers.Binary;

namespace NKitDataStore.Binary.Serialization
{
    /// <summary>
    /// Handles serialization and deserialization of the Block_Index.
    /// The Block_Index is stored as independently-decompressible zstd sections,
    /// each containing a flat array of 28-byte BlockIndexEntry structs.
    /// 
    /// Binary layout:
    /// [StructureVersion: uint16]
    /// [SectionCount: uint32]
    /// [Section 0: CompressedSize(uint32 BE), UncompressedSize(uint32 BE), ZstdData...]
    /// [Section 1: CompressedSize(uint32 BE), UncompressedSize(uint32 BE), ZstdData...]
    /// ...
    /// 
    /// Each section's uncompressed data is a flat array of BlockIndexEntry (28 bytes each):
    /// XxHash64(8) + Crc32(4) + FileId(4) + Offset(8) + Size(4)
    /// 
    /// Entries are sorted by (XxHash64, Crc32) ascending.
    /// </summary>
    internal static class BlockIndexSerializer
    {
        /// <summary>Current structure version for the Block_Index.</summary>
        private const ushort CurrentStructureVersion = 1;

        /// <summary>Size of a single BlockIndexEntry in bytes.</summary>
        private const int EntrySize = 28;

        /// <summary>Zstd compression level used for sections.</summary>
        private const int CompressionLevel = 6;

        /// <summary>
        /// Serializes an array of BlockIndexEntry into the sectioned binary format.
        /// Entries are sorted by (XxHash64, Crc32) ascending before serialization.
        /// </summary>
        /// <param name="entries">The block index entries to serialize.</param>
        /// <param name="sectionSize">
        /// The uncompressed section size in bytes. Default is 65536 (2340 entries at 28 bytes each).
        /// Each section is independently compressed with zstd.
        /// </param>
        /// <param name="progress">Optional progress reporter (0-100 percentage).</param>
        /// <returns>The serialized byte array containing the full Block_Index.</returns>
        public static byte[] Serialize(BlockIndexEntry[] entries, int sectionSize = 65536, IProgress<int>? progress = null)
        {
            using MemoryStream output = new MemoryStream();
            SerializeTo(output, entries, sectionSize, progress);
            return output.ToArray();
        }

        /// <summary>
        /// Serializes an array of BlockIndexEntry directly to a stream.
        /// Entries are sorted by (XxHash64, Crc32) ascending before serialization.
        /// Each section is compressed and written immediately, avoiding large intermediate buffers.
        /// </summary>
        /// <param name="output">The stream to write the serialized Block_Index to.</param>
        /// <param name="entries">The block index entries to serialize.</param>
        /// <param name="sectionSize">The uncompressed section size in bytes.</param>
        /// <param name="progress">Optional progress reporter (0-100 percentage).</param>
        /// <returns>The total number of bytes written.</returns>
        public static long SerializeTo(Stream output, BlockIndexEntry[] entries, int sectionSize = 65536, IProgress<int>? progress = null)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            long startPosition = output.Position;

            // Sort entries by (XxHash64, Crc32) ascending
            BlockIndexEntry[] sorted = new BlockIndexEntry[entries.Length];
            Array.Copy(entries, sorted, entries.Length);
            Array.Sort(sorted);

            // Calculate entries per section based on sectionSize
            int entriesPerSection = sectionSize / EntrySize;
            if (entriesPerSection < 1)
                entriesPerSection = 1;

            int sectionCount = sorted.Length == 0
                ? 0
                : (sorted.Length + entriesPerSection - 1) / entriesPerSection;

            // Write structure version + section count header (6 bytes)
            Span<byte> headerBuf = stackalloc byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(headerBuf, CurrentStructureVersion);
            BinaryPrimitives.WriteUInt32BigEndian(headerBuf.Slice(2), (uint)sectionCount);
            output.Write(headerBuf);

            // Serialize and compress each section directly to output
            Span<byte> sectionHeaderBuf = stackalloc byte[8];

            for (int s = 0; s < sectionCount; s++)
            {
                int startIdx = s * entriesPerSection;
                int endIdx = Math.Min(startIdx + entriesPerSection, sorted.Length);
                int count = endIdx - startIdx;
                int uncompressedSize = count * EntrySize;

                // Serialize entries to flat byte array
                byte[] uncompressed = new byte[uncompressedSize];
                SerializeEntries(sorted.AsSpan(startIdx, count), uncompressed);

                // Compress with zstd
                int maxCompressed = uncompressedSize + 0x100; // worst-case overhead
                byte[] compressedBuffer = new byte[maxCompressed];
                int compressedSize = maxCompressed;

                using (CompressionBlock compressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = uncompressedSize,
                        Type = (Nanook.GrindCore.CompressionType)CompressionLevel
                    }))
                {
                    compressor.Compress(uncompressed, 0, uncompressedSize,
                        compressedBuffer, 0, ref compressedSize);
                }

                // Write section header: CompressedSize(uint32 BE), UncompressedSize(uint32 BE)
                BinaryPrimitives.WriteUInt32BigEndian(sectionHeaderBuf, (uint)compressedSize);
                BinaryPrimitives.WriteUInt32BigEndian(sectionHeaderBuf.Slice(4), (uint)uncompressedSize);
                output.Write(sectionHeaderBuf);

                // Write compressed data
                output.Write(compressedBuffer, 0, compressedSize);

                // Report progress per section
                progress?.Report((s + 1) * 100 / sectionCount);
            }

            return output.Position - startPosition;
        }

        /// <summary>
        /// Deserializes a Block_Index from its binary representation.
        /// Sections are decompressed sequentially.
        /// </summary>
        /// <param name="data">The raw Block_Index bytes.</param>
        /// <returns>A sorted array of BlockIndexEntry.</returns>
        public static BlockIndexEntry[] Deserialize(ReadOnlySpan<byte> data)
        {
            if (data.Length < 6)
                throw new InvalidDataException("Block_Index data is too short to contain header.");

            ushort version = BinaryPrimitives.ReadUInt16BigEndian(data);
            if (version > CurrentStructureVersion)
                throw new NotSupportedException(
                    $"Block_Index structure version {version} is not supported. Maximum supported version is {CurrentStructureVersion}.");

            uint sectionCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2));

            if (sectionCount == 0)
                return Array.Empty<BlockIndexEntry>();

            // Collect all entries from all sections
            List<BlockIndexEntry> allEntries = new List<BlockIndexEntry>();
            int offset = 6; // past version + section count

            for (uint s = 0; s < sectionCount; s++)
            {
                if (offset + 8 > data.Length)
                    throw new InvalidDataException(
                        $"Block_Index section {s}: insufficient data for section header at offset {offset}.");

                uint compressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                uint uncompressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4));
                offset += 8;

                if (offset + (int)compressedSize > data.Length)
                    throw new InvalidDataException(
                        $"Block_Index section {s}: compressed data extends beyond buffer. " +
                        $"Offset: {offset}, CompressedSize: {compressedSize}, BufferLength: {data.Length}.");

                // Decompress section
                byte[] compressedData = data.Slice(offset, (int)compressedSize).ToArray();
                byte[] decompressed = new byte[uncompressedSize];
                int decompressedSize = (int)uncompressedSize;

                using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = (int)uncompressedSize
                    }))
                {
                    decompressor.Decompress(compressedData, 0, (int)compressedSize,
                        decompressed, 0, ref decompressedSize);
                }

                // Parse entries from decompressed data
                DeserializeEntries(decompressed.AsSpan(0, decompressedSize), allEntries);

                offset += (int)compressedSize;
            }

            // Entries should already be sorted (they were sorted on serialize and sections are in order)
            // but verify/ensure sort for safety
            BlockIndexEntry[] result = allEntries.ToArray();
            Array.Sort(result);
            return result;
        }

        /// <summary>
        /// Deserializes a Block_Index from its binary representation using parallel decompression.
        /// Each section is decompressed on a separate thread for faster loading.
        /// </summary>
        /// <param name="data">The raw Block_Index bytes.</param>
        /// <returns>A sorted array of BlockIndexEntry.</returns>
        public static BlockIndexEntry[] DeserializeParallel(ReadOnlySpan<byte> data)
        {
            if (data.Length < 6)
                throw new InvalidDataException("Block_Index data is too short to contain header.");

            ushort version = BinaryPrimitives.ReadUInt16BigEndian(data);
            if (version > CurrentStructureVersion)
                throw new NotSupportedException(
                    $"Block_Index structure version {version} is not supported. Maximum supported version is {CurrentStructureVersion}.");

            uint sectionCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2));

            if (sectionCount == 0)
                return Array.Empty<BlockIndexEntry>();

            // First pass: locate all sections (must be sequential since sizes are variable)
            (byte[] CompressedData, uint UncompressedSize)[] sectionInfos = new (byte[] CompressedData, uint UncompressedSize)[sectionCount];
            int offset = 6;

            for (uint s = 0; s < sectionCount; s++)
            {
                if (offset + 8 > data.Length)
                    throw new InvalidDataException(
                        $"Block_Index section {s}: insufficient data for section header at offset {offset}.");

                uint compressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
                uint uncompressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4));
                offset += 8;

                if (offset + (int)compressedSize > data.Length)
                    throw new InvalidDataException(
                        $"Block_Index section {s}: compressed data extends beyond buffer. " +
                        $"Offset: {offset}, CompressedSize: {compressedSize}, BufferLength: {data.Length}.");

                // Copy compressed data for parallel processing (ReadOnlySpan can't cross thread boundaries)
                sectionInfos[s] = (data.Slice(offset, (int)compressedSize).ToArray(), uncompressedSize);
                offset += (int)compressedSize;
            }

            // Parallel decompression: each section produces its own entry array
            BlockIndexEntry[][] sectionResults = new BlockIndexEntry[sectionCount][];

            Parallel.For(0, (int)sectionCount, s =>
            {
                (byte[]? compressedData, uint uncompressedSize) = sectionInfos[s];
                byte[] decompressed = new byte[uncompressedSize];
                int decompressedSize = (int)uncompressedSize;

                using (CompressionBlock decompressor = CompressionBlockFactory.Create(
                    CompressionAlgorithm.ZStd,
                    new CompressionOptions
                    {
                        BlockSize = (int)uncompressedSize
                    }))
                {
                    decompressor.Decompress(compressedData, 0, compressedData.Length,
                        decompressed, 0, ref decompressedSize);
                }

                // Parse entries
                List<BlockIndexEntry> entries = new List<BlockIndexEntry>();
                DeserializeEntries(decompressed.AsSpan(0, decompressedSize), entries);
                sectionResults[s] = entries.ToArray();
            });

            // Concatenate all section results in order (they're already sorted within and across sections)
            int totalCount = 0;
            for (int s = 0; s < sectionCount; s++)
                totalCount += sectionResults[s].Length;

            BlockIndexEntry[] result = new BlockIndexEntry[totalCount];
            int destIdx = 0;
            for (int s = 0; s < sectionCount; s++)
            {
                Array.Copy(sectionResults[s], 0, result, destIdx, sectionResults[s].Length);
                destIdx += sectionResults[s].Length;
            }

            // Entries should already be sorted, but ensure for safety
            Array.Sort(result);
            return result;
        }

        /// <summary>
        /// Serializes a span of BlockIndexEntry structs into a flat byte buffer.
        /// Each entry is 28 bytes: XxHash64(8) + Crc32(4) + FileId(4) + Offset(8) + Size(4).
        /// </summary>
        private static void SerializeEntries(ReadOnlySpan<BlockIndexEntry> entries, Span<byte> destination)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                int pos = i * EntrySize;
                ref readonly BlockIndexEntry entry = ref entries[i];

                BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(pos), entry.Key.XxHash64);
                BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(pos + 8), entry.Key.Crc32);
                BinaryPrimitives.WriteInt32BigEndian(destination.Slice(pos + 12), entry.FileId);
                BinaryPrimitives.WriteInt64BigEndian(destination.Slice(pos + 16), entry.Offset);
                BinaryPrimitives.WriteInt32BigEndian(destination.Slice(pos + 24), entry.Size);
            }
        }

        /// <summary>
        /// Deserializes BlockIndexEntry structs from a flat byte buffer into a list.
        /// </summary>
        private static void DeserializeEntries(ReadOnlySpan<byte> data, List<BlockIndexEntry> entries)
        {
            int entryCount = data.Length / EntrySize;
            for (int i = 0; i < entryCount; i++)
            {
                int pos = i * EntrySize;

                BlockIndexEntry entry = new BlockIndexEntry
                {
                    Key = new BlockKey(
                        BinaryPrimitives.ReadUInt64BigEndian(data.Slice(pos)),
                        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos + 8))),
                    FileId = BinaryPrimitives.ReadInt32BigEndian(data.Slice(pos + 12)),
                    Offset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(pos + 16)),
                    Size = BinaryPrimitives.ReadInt32BigEndian(data.Slice(pos + 24))
                };

                entries.Add(entry);
            }
        }
    }
}