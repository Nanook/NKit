using System.Text;

namespace NKitDataStore
{
    /// <summary>
    /// Comprehensive statistics about a data store set.
    /// Provides insights into storage efficiency, deduplication, and compression effectiveness.
    /// </summary>
    public record DataStoreStatistics
    {
        /// <summary>
        /// Name of the data set.
        /// </summary>
        public string SetName { get; set; } = string.Empty;

        /// <summary>
        /// Total number of images in the set.
        /// </summary>
        public int ImageCount { get; set; }

        /// <summary>
        /// Total logical size of all images (sum of Image.Size).
        /// This is the total size of data as it would appear when extracted.
        /// </summary>
        public long TotalImageDataSize { get; set; }

        /// <summary>
        /// Total number of unique blocks physically stored.
        /// </summary>
        public long UniqueBlocksStored { get; set; }

        /// <summary>
        /// Total number of block references across all offset records.
        /// This includes duplicates (same block referenced multiple times).
        /// </summary>
        public long TotalBlockReferences { get; set; }

        /// <summary>
        /// Number of compressed blocks (CompressionType.Zstd).
        /// </summary>
        public long CompressedBlocks { get; set; }

        /// <summary>
        /// Number of uncompressed blocks (CompressionType.None).
        /// </summary>
        public long UncompressedBlocks { get; set; }

        /// <summary>
        /// Total physical storage used by all blocks (compressed + uncompressed).
        /// This is the actual disk space used for block data.
        /// </summary>
        public long TotalPhysicalBlockStorage { get; set; }

        /// <summary>
        /// Average physical size of compressed blocks (bytes).
        /// </summary>
        public double AverageCompressedBlockSize { get; set; }

        /// <summary>
        /// Average physical size of uncompressed blocks (bytes).
        /// </summary>
        public double AverageUncompressedBlockSize { get; set; }

        /// <summary>
        /// Block size configured for this set (typically 64KB).
        /// </summary>
        public int BlockSize { get; set; }

        /// <summary>
        /// Estimated logical size of all referenced blocks before compression.
        /// Calculated as: TotalBlockReferences � BlockSize
        /// </summary>
        public long EstimatedUncompressedSize => TotalBlockReferences * BlockSize;

        /// <summary>
        /// Deduplication ratio: How many times blocks are reused on average.
        /// Calculated as: TotalBlockReferences / UniqueBlocksStored
        /// Higher is better (more sharing).
        /// </summary>
        public double DeduplicationRatio => UniqueBlocksStored > 0
            ? (double)TotalBlockReferences / UniqueBlocksStored
            : 0;

        /// <summary>
        /// Storage saved through deduplication (bytes).
        /// Calculated as: (TotalBlockReferences - UniqueBlocksStored) � BlockSize
        /// Returns 0 if no deduplication occurred.
        /// </summary>
        public long DeduplicationSavings
        {
            get
            {
                // If we have no block references, there's no deduplication
                if (TotalBlockReferences == 0 || UniqueBlocksStored == 0)
                    return 0;

                // Calculate savings - can't be negative
                long savings = (TotalBlockReferences - UniqueBlocksStored) * BlockSize;
                return Math.Max(0, savings);
            }
        }

        /// <summary>
        /// Compression ratio: Physical storage / Logical storage.
        /// Lower is better (more compression).
        /// </summary>
        public double CompressionRatio => EstimatedUncompressedSize > 0
            ? (double)TotalPhysicalBlockStorage / EstimatedUncompressedSize
            : 0;

        /// <summary>
        /// Storage saved through compression (bytes).
        /// Calculated as: EstimatedUncompressedSize - TotalPhysicalBlockStorage
        /// Returns 0 if no compression occurred or if result would be negative.
        /// </summary>
        public long CompressionSavings
        {
            get
            {
                // If we have no references or estimated size, there's no compression
                if (EstimatedUncompressedSize == 0 || TotalPhysicalBlockStorage == 0)
                    return 0;

                // Calculate savings - can't be negative
                long savings = EstimatedUncompressedSize - TotalPhysicalBlockStorage;
                return Math.Max(0, savings);
            }
        }

        /// <summary>
        /// Overall storage efficiency: Physical storage / Total image data size.
        /// Lower is better (more efficient).
        /// Accounts for both deduplication and compression.
        /// </summary>
        public double OverallStorageEfficiency => TotalImageDataSize > 0
            ? (double)TotalPhysicalBlockStorage / TotalImageDataSize
            : 0;

        /// <summary>
        /// Total storage saved through deduplication and compression (bytes).
        /// Returns 0 if no savings occurred or if result would be negative.
        /// </summary>
        public long TotalStorageSaved
        {
            get
            {
                // If we have no image data, there are no savings
                if (TotalImageDataSize == 0 || TotalPhysicalBlockStorage == 0)
                    return 0;

                // Calculate total savings - can't be negative
                long savings = TotalImageDataSize - TotalPhysicalBlockStorage;
                return Math.Max(0, savings);
            }
        }

        /// <summary>
        /// Percentage of blocks that are compressed.
        /// </summary>
        public double CompressionPercentage => UniqueBlocksStored > 0
            ? (double)CompressedBlocks / UniqueBlocksStored * 100
            : 0;

        /// <summary>
        /// Per-image statistics (optional, for detailed analysis).
        /// </summary>
        public List<ImageStatistics>? ImageDetails { get; set; }

        /// <summary>
        /// Total size of all database files on disk (bytes).
        /// Includes main database and all shard files.
        /// </summary>
        public long TotalDatabaseFileSize { get; set; }

        /// <summary>
        /// Number of database files (main + shards).
        /// </summary>
        public int DatabaseFileCount { get; set; }

        /// <summary>
        /// Formats the statistics as a human-readable YAML-style string.
        /// </summary>
        public string ToYaml()
        {
            StringBuilder yaml = new StringBuilder();
            yaml.AppendLine($"SetName: {SetName}");
            yaml.AppendLine($"ImageCount: {ImageCount}");
            yaml.AppendLine();

            yaml.AppendLine("Database Files:");
            yaml.AppendLine($"  DatabaseFileCount: {DatabaseFileCount}");
            yaml.AppendLine($"  TotalDatabaseFileSize: {formatBytes(TotalDatabaseFileSize)} ({TotalDatabaseFileSize:N0} bytes)");
            yaml.AppendLine();

            yaml.AppendLine("Logical Data Sizes:");
            yaml.AppendLine($"  TotalImageDataSize: {formatBytes(TotalImageDataSize)} ({TotalImageDataSize:N0} bytes)");
            yaml.AppendLine($"  EstimatedUncompressedSize: {formatBytes(EstimatedUncompressedSize)} ({EstimatedUncompressedSize:N0} bytes)");
            yaml.AppendLine();

            yaml.AppendLine("Physical Storage:");
            yaml.AppendLine($"  TotalPhysicalBlockStorage: {formatBytes(TotalPhysicalBlockStorage)} ({TotalPhysicalBlockStorage:N0} bytes)");
            yaml.AppendLine($"  BlockSize: {formatBytes(BlockSize)} ({BlockSize:N0} bytes)");
            yaml.AppendLine();

            yaml.AppendLine("Block Statistics:");
            yaml.AppendLine($"  UniqueBlocksStored: {UniqueBlocksStored:N0}");
            yaml.AppendLine($"  TotalBlockReferences: {TotalBlockReferences:N0}");
            yaml.AppendLine($"  CompressedBlocks: {CompressedBlocks:N0} ({CompressionPercentage:F1}%)");
            yaml.AppendLine($"  UncompressedBlocks: {UncompressedBlocks:N0} ({100 - CompressionPercentage:F1}%)");
            yaml.AppendLine();

            yaml.AppendLine("Compression Statistics:");
            yaml.AppendLine($"  AverageCompressedBlockSize: {formatBytes((long)AverageCompressedBlockSize)} ({AverageCompressedBlockSize:F0} bytes)");
            yaml.AppendLine($"  AverageUncompressedBlockSize: {formatBytes((long)AverageUncompressedBlockSize)} ({AverageUncompressedBlockSize:F0} bytes)");
            yaml.AppendLine($"  CompressionRatio: {CompressionRatio:F3} ({(1 - CompressionRatio) * 100:F1}% reduction)");
            yaml.AppendLine($"  CompressionSavings: {formatBytes(CompressionSavings)} ({CompressionSavings:N0} bytes)");
            yaml.AppendLine();

            yaml.AppendLine("Deduplication Statistics:");
            yaml.AppendLine($"  DeduplicationRatio: {DeduplicationRatio:F2}x (blocks reused {DeduplicationRatio:F2} times on average)");
            yaml.AppendLine($"  DeduplicationSavings: {formatBytes(DeduplicationSavings)} ({DeduplicationSavings:N0} bytes)");
            yaml.AppendLine();

            yaml.AppendLine("Overall Efficiency:");
            yaml.AppendLine($"  OverallStorageEfficiency: {OverallStorageEfficiency:F3} ({(1 - OverallStorageEfficiency) * 100:F1}% total reduction)");
            yaml.AppendLine($"  TotalStorageSaved: {formatBytes(TotalStorageSaved)} ({TotalStorageSaved:N0} bytes)");
            yaml.AppendLine($"  DatabaseOverhead: {formatBytes(TotalDatabaseFileSize - TotalPhysicalBlockStorage)} ({(TotalDatabaseFileSize - TotalPhysicalBlockStorage) / (double)TotalDatabaseFileSize * 100:F1}% of file size)");

            if (ImageDetails != null && ImageDetails.Count > 0)
            {
                yaml.AppendLine();
                yaml.AppendLine("Per-Image Details:");
                foreach (ImageStatistics image in ImageDetails)
                {
                    yaml.AppendLine($"  - Name: {image.ImageName}");
                    yaml.AppendLine($"    Size: {formatBytes(image.Size)} ({image.Size:N0} bytes)");
                    yaml.AppendLine($"    Blocks: {image.BlockReferences:N0} references, {image.UniqueBlocks:N0} unique");
                    yaml.AppendLine($"    DeduplicationRatio: {image.DeduplicationRatio:F2}x");
                }
            }

            return yaml.ToString();
        }

        private static string formatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            // For bytes, show as integer without decimal places
            if (order == 0)
            {
                return $"{(long)len} {sizes[order]}";
            }

            return $"{len:F2} {sizes[order]}";
        }
    }

    /// <summary>
    /// Statistics for a single image within a set.
    /// </summary>
    public record ImageStatistics
    {
        public long ImageId { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public long Size { get; set; }
        public long BlockReferences { get; set; }
        public long UniqueBlocks { get; set; }
        /// <summary>
        /// Total physical storage size (bytes) of all unique blocks belonging to this image.
        /// Computed by summing block.size for each unique block referenced by this image.
        /// </summary>
        public long StoredSize { get; set; }
        /// <summary>
        /// Number of this image's unique blocks that are also referenced by at least one other image.
        /// </summary>
        public long SharedBlocks { get; set; }
        /// <summary>
        /// Total physical size of blocks shared with other images.
        /// </summary>
        public long SharedBlocksSize { get; set; }
        /// <summary>
        /// Apportioned stored size: each block's physical size divided by how many images share it.
        /// This is this image's "fair share" of the storage cost.
        /// </summary>
        public long ApportionedStoredSize { get; set; }
        /// <summary>
        /// Physical size of blocks that are shared with other images (refcount > 1).
        /// </summary>
        public long SharedBlocksPhysicalSize { get; set; }
        /// <summary>
        /// Physical size of non-shared blocks that are compressed (size less than BlockSize).
        /// </summary>
        public long NonSharedCompressedSize { get; set; }
        /// <summary>
        /// Physical size of non-shared blocks that are NOT compressed (size equals BlockSize).
        /// </summary>
        public long NonSharedUncompressedSize { get; set; }
        /// <summary>
        /// Physical size of shared blocks that are compressed.
        /// </summary>
        public long SharedCompressedSize { get; set; }
        /// <summary>
        /// Physical size of shared blocks that are NOT compressed.
        /// </summary>
        public long SharedUncompressedSize { get; set; }
        public double DeduplicationRatio => UniqueBlocks > 0
            ? (double)BlockReferences / UniqueBlocks
            : 0;
    }
}