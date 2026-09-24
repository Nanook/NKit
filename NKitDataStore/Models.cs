namespace NKitDataStore
{
    /// <summary>
    /// Uniquely identifies a block by its content hashes.
    /// </summary>
    public readonly struct BlockKey : IEquatable<BlockKey>
    {
        /// <summary>
        /// The 64-bit xxHash of the uncompressed block data.
        /// </summary>
        public ulong XxHash64 { get; }

        /// <summary>
        /// The 32-bit CRC of the uncompressed block data.
        /// </summary>
        public uint Crc32 { get; }

        public BlockKey(ulong xxHash64, uint crc32)
        {
            XxHash64 = xxHash64;
            Crc32 = crc32;
        }

        public bool Equals(BlockKey other) => XxHash64 == other.XxHash64 && Crc32 == other.Crc32;
        public override bool Equals(object? obj) => obj is BlockKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(XxHash64, Crc32);
        public static bool operator ==(BlockKey left, BlockKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BlockKey left, BlockKey right)
        {
            return !left.Equals(right);
        }

        public override string ToString() => $"BlockKey(XxHash64: 0x{XxHash64:X16}, Crc32: 0x{Crc32:X8})";
    }

    /// <summary>
    /// Uniquely identifies an image within the entire data store.
    /// </summary>
    public readonly struct GlobalImageKey : IEquatable<GlobalImageKey>
    {
        /// <summary>
        /// The name of the set the image belongs to.
        /// </summary>
        public string SetName { get; }

        /// <summary>
        /// The unique ID of the image within its set.
        /// </summary>
        public long ImageId { get; }

        public GlobalImageKey(string setName, long imageId)
        {
            SetName = setName ?? string.Empty;
            ImageId = imageId;
        }

        public bool Equals(GlobalImageKey other) => SetName == other.SetName && ImageId == other.ImageId;
        public override bool Equals(object? obj) => obj is GlobalImageKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SetName, ImageId);
        public static bool operator ==(GlobalImageKey left, GlobalImageKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GlobalImageKey left, GlobalImageKey right)
        {
            return !left.Equals(right);
        }

        public override string ToString() => $"GlobalImageKey(SetName: {SetName}, ImageId: {ImageId})";
    }

    /// <summary>
    /// Represents a physical block of data stored in the database.
    /// </summary>
    public class BlockRecord
    {
        public BlockKey Key { get; set; }
        public CompressionType CompressionType { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();

        public BlockRecord() { }

        public BlockRecord(BlockKey key, CompressionType compressionType, byte[] data)
        {
            Key = key;
            CompressionType = compressionType;
            Data = data;
        }
    }

    /// <summary>
    /// Represents metadata for a single logical image.
    /// </summary>
    public class ImageRecord
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public uint Crc32 { get; set; }
        public ulong XxHash64 { get; set; }
        public string SetName { get; set; } = string.Empty;
        public string? System { get; set; }
        public ImageFormat Format { get; set; }
        // Rollback checkpoint: shard file id and offset at time of image insertion
        public int? RollbackFileId { get; set; }
        public long? RollbackOffset { get; set; }
        public bool Removed { get; set; }
    }

    /// <summary>
    /// Represents the info table metadata for a data set.
    /// Contains schema version and storage configuration.
    /// </summary>
    public class InfoRecord
    {
        /// <summary>
        /// Schema version as a packed integer (major << 16 | minor << 8 | revision).
        /// Example: 0x020100 = version 2.1.0
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        /// Size of the Shard file before creating a new one.
        /// </summary>
        public long ShardSize { get; set; }

        /// <summary>
        /// Size of each data block in bytes (typically 65536 = 64KB).
        /// </summary>
        public int BlockSize { get; set; }

        /// <summary>
        /// Maximum number of block references per offset record.
        /// Used to split large files across multiple offset rows.
        /// </summary>
        public int MaxOffsetBlocks { get; set; }

    }

    /// <summary>
    /// Represents a file stored alongside block data in the shard files.
    /// Tracked by the file table, independent of the block/offset pipeline.
    /// </summary>
    public class FileRecord
    {
        public long ImageId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int FileId { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public long UncompressedSize { get; set; }
        public bool IsSystem { get; set; }
    }

    /// <summary>
    /// Represents a contiguous segment of an image, mapping it to either physical or virtual data.
    /// </summary>
    public class OffsetRecord
    {
        public long ImageId { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public BlockType Type { get; set; }

        /// <summary>
        /// The offset of the first chunk in this logical file/group.
        /// All offset records with the same OffsetStart belong to the same file.
        /// Defaults to the offset value for single-chunk files.
        /// </summary>
        public long OffsetStart { get; set; }

        /// <summary>
        /// List of block keys that make up this offset segment.
        /// For physical data: Contains 1+ block keys (each block is typically 64KB).
        /// For virtual data (Fill00, FillFF, NJunk): Empty or null list.
        /// </summary>
        public List<BlockKey>? Blocks { get; set; }

        /// <summary>
        /// Gets whether this offset has any physical blocks.
        /// Returns false for virtual data (Fill00, FillFF, NJunk).
        /// </summary>
        public bool HasBlocks => Blocks != null && Blocks.Count > 0;

        /// <summary>
        /// Gets the first block key (for single-block segments or backward compatibility).
        /// Returns null for virtual data or empty segments.
        /// </summary>
        public BlockKey? FirstBlock => Blocks?.Count > 0 ? Blocks[0] : null;

        /// <summary>
        /// Gets the number of blocks in this offset segment.
        /// </summary>
        public int BlockCount => Blocks?.Count ?? 0;

        /// <summary>
        /// Gets a specific block key by index.
        /// </summary>
        /// <param name="index">Zero-based index of the block.</param>
        /// <returns>The block key at the specified index.</returns>
        public BlockKey GetBlockAt(int index)
        {
            if (Blocks == null || index < 0 || index >= Blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Blocks[index];
        }

        /// <summary>
        /// Finds the block key that contains data at the specified offset within this segment.
        /// </summary>
        /// <param name="offsetWithinSegment">Offset relative to this segment's start (0-based).</param>
        /// <param name="blockSize">The size of each block (typically 64KB).</param>
        /// <returns>The block key and the offset within that block.</returns>
        public (BlockKey blockKey, int offsetWithinBlock) GetBlockAtOffset(long offsetWithinSegment, int blockSize)
        {
            if (Blocks == null || Blocks.Count == 0)
                throw new InvalidOperationException("Cannot get block at offset for virtual data.");

            int blockIndex = (int)(offsetWithinSegment / blockSize);
            int offsetWithinBlock = (int)(offsetWithinSegment % blockSize);

            if (blockIndex >= Blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(offsetWithinSegment));

            return (Blocks[blockIndex], offsetWithinBlock);
        }
    }

    /// <summary>
    /// Provides summary information about a data set.
    /// </summary>
    public class SetInfo
    {
        public string SetName { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public long ShardSize { get; set; }
        public int BlockSize { get; set; }
        public int MaxOffsetBlocks { get; set; }
        public long ImageCount { get; set; }
        public long TotalSize { get; set; }
        public BlockStorageInfo BlockStorage { get; set; } = new();
    }

    /// <summary>
    /// Describes the physical layout of data within a source stream for strided reads.
    /// Used to extract clean data from sectors/blocks that include padding, headers, or hashes.
    /// </summary>
    public class DataStride : IEquatable<DataStride>
    {
        /// <summary>
        /// The size of a single physical block in the source stream (e.g., a sector size).
        /// </summary>
        public int SourceBlockSize { get; set; }

        /// <summary>
        /// The starting offset of the clean data within the physical block.
        /// </summary>
        public int DataOffset { get; set; }

        /// <summary>
        /// The length of the clean data within the physical block.
        /// </summary>
        public int DataLength { get; set; }

        public long OffsetToClean(long o)
        {
            if (SourceBlockSize == DataLength)
                return o;

            return (o / SourceBlockSize * DataLength) + ((o % SourceBlockSize) > DataOffset ? (o % SourceBlockSize) - DataOffset : 0L);
        }

        public long CleanToOffset(long o, bool blockPin)
        {
            if (SourceBlockSize == DataLength)
                return o;

            long rm = o % DataLength;
            long offset = o / DataLength * SourceBlockSize;

            if (rm != 0)
                offset += DataOffset + rm;
            else if (!blockPin) //is it the start of the next sector (0x400 for wii)
                offset += DataOffset; //pull it back so we don't affect the following sector

            return offset;
        }

        /// <summary>
        /// Calculates the clean data size from a strided (with padding) size.
        /// </summary>
        /// <param name="offset">Offset for accurate calculations.</param>
        /// <param name="stridedSize">The size of data in strided format (with padding/hashes).</param>
        /// <returns>The size of clean data after padding is removed.</returns>
        /// <example>
        /// <code>
        /// var stride = DataStride.Wii;
        /// long stridedSize = 10 * 0x8000; // 10 Wii blocks with hashes
        /// long cleanSize = stride.GetCleanSize(stridedSize); // 10 * 0x7C00
        /// </code>
        /// </example>
        public long GetCleanSize(long offset, long stridedSize)
        {
            if (stridedSize < 0)
                throw new ArgumentOutOfRangeException(nameof(stridedSize), "Strided size cannot be negative");

            if (stridedSize == 0)
                return 0;

            long start = OffsetToClean(offset);
            long end = OffsetToClean(offset + stridedSize);
            return end - start;
        }

        /// <summary>
        /// Calculates the strided (with padding) size from a clean data size.
        /// </summary>
        /// <param name="offset">Offset for accurate calculations.</param>
        /// <param name="cleanSize">The size of clean data (without padding/hashes).</param>
        /// <returns>The size of data in strided format (with padding/hashes).</returns>
        /// <example>
        /// <code>
        /// var stride = DataStride.Wii;
        /// long cleanSize = 10 * 0x7C00; // 10 Wii blocks of clean data
        /// long stridedSize = stride.GetStridedSize(cleanSize); // 10 * 0x8000
        /// </code>
        /// </example>
        public long GetStridedSize(long offset, long cleanSize, bool offsetIsStrided = false)
        {
            if (cleanSize < 0)
                throw new ArgumentOutOfRangeException(nameof(cleanSize), "Clean size cannot be negative");

            if (cleanSize == 0)
                return 0;

            long cleanStart = !offsetIsStrided ? offset : OffsetToClean(offset);
            long stridesStart = offsetIsStrided ? offset : CleanToOffset(offset, false); // no block pin, we want the start of the block

            long stridedEnd = CleanToOffset(cleanStart + cleanSize, true); // we want the end of the data
            return stridedEnd - stridesStart;
        }

        /// <summary>
        /// Calculates how many complete stride blocks are contained in the given strided size.
        /// </summary>
        /// <param name="stridedSize">The size of data in strided format.</param>
        /// <returns>The number of complete stride blocks.</returns>
        public long GetCompleteBlockCount(long stridedSize)
        {
            if (stridedSize < 0)
                throw new ArgumentOutOfRangeException(nameof(stridedSize), "Strided size cannot be negative");

            return stridedSize / SourceBlockSize;
        }

        /// <summary>
        /// Calculates the padding overhead for a given clean data size.
        /// </summary>
        /// <param name="cleanSize">The size of clean data.</param>
        /// <returns>The number of padding bytes that will be added in strided format.</returns>
        /// <example>
        /// <code>
        /// var stride = DataStride.Wii;
        /// long cleanSize = 10 * 0x7C00;
        /// long overhead = stride.GetPaddingOverhead(cleanSize); // 10 * 0x400
        /// </code>
        /// </example>
        public long GetPaddingOverhead(long offset, long cleanSize) => GetStridedSize(offset, cleanSize) - cleanSize;

        /// <summary>
        /// Gets the efficiency ratio (clean data / total strided size).
        /// </summary>
        /// <returns>A value between 0 and 1 representing the efficiency.</returns>
        /// <example>
        /// <code>
        /// var wiiStride = DataStride.Wii;
        /// double efficiency = wiiStride.GetEfficiency(); // ~0.968 (96.8%)
        /// 
        /// var cdStride = DataStride.CdMode1;
        /// double efficiency = cdStride.GetEfficiency(); // ~0.871 (87.1%)
        /// </code>
        /// </example>
        public double GetEfficiency() => (double)DataLength / SourceBlockSize;

        public bool Equals(DataStride? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return SourceBlockSize == other.SourceBlockSize &&
                   DataOffset == other.DataOffset &&
                   DataLength == other.DataLength;
        }

        public override bool Equals(object? obj) => obj is DataStride other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SourceBlockSize, DataOffset, DataLength);

        public static bool operator ==(DataStride? left, DataStride? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(DataStride? left, DataStride? right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Creates a DataStride for standard CD sectors (Mode 1).
        /// Extracts 2048 bytes of data from 2352-byte sectors (skipping sync, header, and ECC/EDC).
        /// </summary>
        public static DataStride CdMode1 => new DataStride
        {
            SourceBlockSize = 2352,  // Full CD sector
            DataOffset = 16,         // Skip sync (12) + header (4)
            DataLength = 2048        // User data only
        };

        /// <summary>
        /// Creates a DataStride for Wii disc blocks.
        /// Extracts 31KB of data from 32KB blocks (skipping 0x400 byte hash at start).
        /// </summary>
        public static DataStride Wii => new DataStride
        {
            SourceBlockSize = 0x8000,  // 32KB block
            DataOffset = 0x400,         // Skip 1KB hash
            DataLength = 0x7C00         // 31KB data
        };

        /// <summary>
        /// Creates a DataStride for WiiU disc blocks (32KB variant).
        /// Extracts 31KB of data from 32KB blocks (skipping 0x400 byte hash at start).
        /// </summary>
        public static DataStride WiiU32KB => new DataStride
        {
            SourceBlockSize = 0x8000,  // 32KB block
            DataOffset = 0x400,         // Skip 1KB hash
            DataLength = 0x7C00         // 31KB data
        };

        /// <summary>
        /// Creates a DataStride for WiiU disc blocks (64KB variant).
        /// Extracts 63KB of data from 64KB blocks (skipping 0x400 byte hash at start).
        /// </summary>
        public static DataStride WiiU64KB => new DataStride
        {
            SourceBlockSize = 0x10000, // 64KB block
            DataOffset = 0x400,         // Skip 1KB hash
            DataLength = 0xFC00         // 63KB data
        };
    }

    /// <summary>
    /// Provides statistics about the block storage for a set.
    /// </summary>
    public class BlockStorageInfo
    {
        public long BlockCount { get; set; }
        public long TotalRawSize { get; set; }
        public long TotalCompressedSize { get; set; }
        public double CompressionRatio => TotalRawSize > 0 ? (double)TotalCompressedSize / TotalRawSize : 0;
    }

    /// <summary>
    /// Represents a logical area within a disc image.
    /// Areas define regions of the disc with specific characteristics (e.g., data partition, system area).
    /// </summary>
    public class AreaRecord
    {
        public long Id { get; set; }
        public long ImageId { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }

        /// <summary>
        /// Zero-based index of this area within the image (ordered by offset).
        /// Populated when the area is cached in ImageReader.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The size of each strided block in the source data (e.g., 2352 for CD Mode 1, 0x8000 for Wii).
        /// Null if the area does not use striding.
        /// </summary>
        public int StrideBlockSize { get; set; }

        /// <summary>
        /// The offset within each strided block where actual data starts (e.g., 16 for CD Mode 1 header, 0x400 for Wii hash).
        /// Null if the area does not use striding.
        /// </summary>
        public int StrideDataOffset { get; set; }

        /// <summary>
        /// The length of actual data within each strided block (e.g., 2048 for CD Mode 1, 0x7C00 for Wii).
        /// Null if the area does not use striding.
        /// </summary>
        public int StrideDataLength { get; set; }

        /// <summary>
        /// The section size used for offset management (buffer size for OffsetsManager).
        /// Typically matches the buffer size used during write (e.g., 0x200000 = 2MB).
        /// </summary>
        public int SectionSize { get; set; }

        public uint Crc32 { get; set; }
        public ulong XxHash64 { get; set; }

        /// <summary>
        /// Metadata key-value pairs for this area, stored as a compact binary blob.
        /// </summary>
        public AreaMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Creates a DataStride object from this area's stride properties if they are set.
        /// Returns a passthrough stride (no transformation) if the area does not use striding.
        /// </summary>
        public DataStride GetDataStride()
        {
            if (StrideBlockSize <= 0)
            {
                // No striding — return a passthrough stride where SourceBlockSize == DataLength
                // This triggers the early-return path in OffsetToClean/CleanToOffset (return o)
                return new DataStride
                {
                    SourceBlockSize = 1,
                    DataOffset = 0,
                    DataLength = 1
                };
            }

            return new DataStride
            {
                SourceBlockSize = StrideBlockSize,
                DataOffset = StrideDataOffset,
                DataLength = StrideDataLength
            };
        }
    }
}