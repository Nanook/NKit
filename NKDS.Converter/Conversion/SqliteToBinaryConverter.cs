using NKDS.Converter.Binary;
using NKDS.Converter.Sqlite;
using NKitDataStore;
using NKitDataStore.Binary;
using System.Diagnostics;

namespace NKDS.Converter.Conversion;

/// <summary>
/// Orchestrates SQLite → Binary conversion.
/// Reads all data from a SQLite index file and writes it to the binary format,
/// handling both separate and embedded modes.
/// </summary>
internal class SqliteToBinaryConverter
{
    /// <summary>
    /// Converts a SQLite index file to binary format.
    /// </summary>
    /// <param name="sourcePath">Path to the source SQLite index file.</param>
    /// <param name="outputPath">Path for the output binary index file.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <returns>Conversion statistics.</returns>
    public ConversionResult Convert(string sourcePath, string outputPath, IProgress<string>? progress = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        string tempPath = outputPath + ".tmp";

        try
        {
            progress?.Report("Opening source SQLite database...");

            using SqliteIndexReader reader = new SqliteIndexReader(sourcePath);

            // Step 1: Read info record
            InfoRecord info = reader.ReadInfo();
            progress?.Report($"Read info: shard_size={info.ShardSize}, block_size={info.BlockSize}, max_offset_blocks={info.MaxOffsetBlocks}");

            // Step 2: Determine if embedded mode (shard_size == 0)
            bool embedded = info.ShardSize == 0;
            long shardBoundary = 0;

            if (embedded)
            {
                // Find the shard file: same directory as source, {setname}_0000.nkds
                string shardPath = EmbeddedIndexCommitter.GetShardPath(outputPath);

                if (!File.Exists(shardPath))
                    throw new InvalidOperationException(
                        $"Embedded mode (shard_size=0) requires shard file '{shardPath}' but it was not found.");

                shardBoundary = new FileInfo(shardPath).Length;

                progress?.Report($"Embedded mode: copying shard data ({shardBoundary} bytes) to output...");

                // Copy shard file to temp output path as the base for embedded index
                File.Copy(shardPath, tempPath, overwrite: true);
            }

            // Step 3: Create BinaryIndexWriter
            using BinaryIndexWriter writer = new BinaryIndexWriter(tempPath, info, embedded, shardBoundary);

            // Step 4: Build block locations dictionary from the block table
            // We need this to look up (FileId, Offset, Size) for each BlockKey referenced by offsets
            progress?.Report("Loading block table for location lookups...");
            Dictionary<BlockKey, (int FileId, long Offset, int Size)> blockLocations = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();

            foreach (BlockIndexEntry entry in reader.ReadAllBlocks())
            {
                blockLocations[entry.Key] = (entry.FileId, entry.Offset, entry.Size);
            }

            progress?.Report($"Loaded {blockLocations.Count} block locations.");

            // Step 5: Read all images ordered by id ASC and write image sections
            long sourceImageCount = reader.GetImageCount();
            List<ImageDirectoryEntry> directoryEntries = new List<ImageDirectoryEntry>();
            long totalAreas = 0;
            long totalOffsets = 0;
            long totalFiles = 0;
            int imageIndex = 0;

            progress?.Report($"Processing {sourceImageCount} images...");

            foreach (ImageRecord image in reader.ReadAllImages())
            {
                try
                {
                    // Read areas, files, offsets for this image
                    List<AreaRecord> areas = reader.ReadAreasForImage(image.Id).ToList();
                    List<FileRecord> files = reader.ReadFilesForImage(image.Id).ToList();
                    List<OffsetRecord> offsets = reader.ReadOffsetsForImage(image.Id).ToList();

                    // Build per-image BlockLocations dictionary from the offset blocks
                    Dictionary<BlockKey, (int FileId, long Offset, int Size)> imageBlockLocations = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
                    foreach (OffsetRecord offset in offsets)
                    {
                        if (offset.Blocks != null)
                        {
                            foreach (BlockKey blockKey in offset.Blocks)
                            {
                                if (!imageBlockLocations.ContainsKey(blockKey) && blockLocations.TryGetValue(blockKey, out (int FileId, long Offset, int Size) location))
                                {
                                    imageBlockLocations[blockKey] = location;
                                }
                            }
                        }
                    }

                    // Write image sections
                    (long metaOffset, int metaSize, int metaUncompressedSize,
                         long blockMapOffset, int blockMapSize, int blockMapUncompressedSize) =
                        writer.WriteImageSections(areas, files, offsets, imageBlockLocations);

                    // Build directory entry from image record and section offsets
                    ImageDirectoryEntry dirEntry = new ImageDirectoryEntry
                    {
                        ImageId = image.Id,
                        Name = image.Name,
                        Size = image.Size,
                        Crc32 = image.Crc32,
                        XxHash64 = image.XxHash64,
                        System = image.System,
                        Format = image.Format,
                        RollbackFileId = image.RollbackFileId,
                        RollbackOffset = image.RollbackOffset,
                        Removed = image.Removed,
                        MetadataSectionOffset = metaOffset,
                        MetadataSectionCompressedSize = metaSize,
                        MetadataSectionUncompressedSize = metaUncompressedSize,
                        BlockMapSectionOffset = blockMapOffset,
                        BlockMapSectionCompressedSize = blockMapSize,
                        BlockMapSectionUncompressedSize = blockMapUncompressedSize
                    };

                    directoryEntries.Add(dirEntry);

                    totalAreas += areas.Count;
                    totalFiles += files.Count;
                    totalOffsets += offsets.Count;
                }
                catch (Exception ex) when (ex is not IOException and not InvalidOperationException and not OutOfMemoryException)
                {
                    throw new InvalidOperationException(
                        $"Failed to parse area/offset/file records for image_id={image.Id} ('{image.Name}'): {ex.Message}", ex);
                }

                imageIndex++;

                if (imageIndex % 100 == 0 || imageIndex == sourceImageCount)
                {
                    progress?.Report($"Processed {imageIndex}/{sourceImageCount} images...");
                }
            }

            // Step 6: Stream all block rows in batches of 100K, sort, write Block_Index
            progress?.Report("Reading and sorting block index entries...");

            List<BlockIndexEntry> allBlocks = new List<BlockIndexEntry>();
            foreach (BlockIndexEntry entry in reader.ReadAllBlocks())
            {
                allBlocks.Add(entry);
            }

            BlockIndexEntry[] sortedBlocks = allBlocks.ToArray();
            Array.Sort(sortedBlocks);

            progress?.Report($"Writing block index ({sortedBlocks.Length} entries)...");
            writer.WriteBlockIndex(sortedBlocks);

            // Step 7: Write Image_Directory and finalize header
            progress?.Report("Writing image directory and finalizing header...");
            writer.WriteDirectoryAndFinalize(directoryEntries);

            // Step 8: Validate image count matches (Req 4.2)
            int outputImageCount = directoryEntries.Count;
            if (outputImageCount != sourceImageCount)
            {
                throw new InvalidOperationException(
                    $"Validation failed: expected {sourceImageCount} images, got {outputImageCount}");
            }

            // Dispose writer before rename to release file handle
            writer.Dispose();

            // Atomic rename: temp → final output
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            File.Move(tempPath, outputPath);

            stopwatch.Stop();

            progress?.Report($"Conversion complete: {outputImageCount} images, {totalAreas} areas, {totalOffsets} offsets, {sortedBlocks.Length} blocks, {totalFiles} files in {stopwatch.Elapsed.TotalSeconds:F1}s");

            return new ConversionResult(
                ImageCount: outputImageCount,
                AreaCount: totalAreas,
                OffsetCount: totalOffsets,
                BlockCount: sortedBlocks.Length,
                FileCount: totalFiles,
                Duration: stopwatch.Elapsed);
        }
        catch
        {
            // Clean up partial output on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}