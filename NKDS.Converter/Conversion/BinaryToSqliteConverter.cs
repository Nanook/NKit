using NKDS.Converter.Binary;
using NKDS.Converter.Sqlite;
using NKitDataStore;
using NKitDataStore.Binary;
using System.Diagnostics;

namespace NKDS.Converter.Conversion;

/// <summary>
/// Orchestrates Binary → SQLite conversion.
/// Reads all data from a binary index file and writes it to a new SQLite database
/// with the standard schema. Processes images one at a time and streams block
/// index entries in batches for memory efficiency.
/// </summary>
internal class BinaryToSqliteConverter
{
    /// <summary>
    /// Converts a binary index file to SQLite format.
    /// </summary>
    /// <param name="sourcePath">Path to the source binary index file.</param>
    /// <param name="outputPath">Path for the output SQLite database file.</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <returns>A ConversionResult with counts of all transferred records.</returns>
    public ConversionResult Convert(string sourcePath, string outputPath, IProgress<string>? progress = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        int imageCount = 0;
        long areaCount = 0;
        long offsetCount = 0;
        long blockCount = 0;
        long fileCount = 0;

        string tempPath = outputPath + ".tmp";

        try
        {
            using BinaryIndexReader reader = new BinaryIndexReader(sourcePath);

            progress?.Report("Creating SQLite database schema...");
            using SqliteIndexWriter writer = new SqliteIndexWriter(tempPath);
            writer.CreateSchema();

            // Write info record from binary header
            InfoRecord info = reader.ReadInfo();
            writer.WriteInfo(info);
            progress?.Report("Wrote info record.");

            // Read image directory
            IReadOnlyList<ImageDirectoryEntry> directory = reader.ReadDirectory();
            imageCount = directory.Count;
            progress?.Report($"Read image directory: {imageCount} images.");

            // Process each image (ordered by ImageId from ReadDirectory)
            int processedImages = 0;
            foreach (ImageDirectoryEntry entry in directory)
            {
                try
                {
                    // Construct ImageRecord from directory entry and write to SQLite (Req 7.2: preserve id)
                    ImageRecord imageRecord = new ImageRecord
                    {
                        Id = entry.ImageId,
                        Name = entry.Name,
                        Size = entry.Size,
                        Crc32 = entry.Crc32,
                        XxHash64 = entry.XxHash64,
                        System = entry.System,
                        Format = entry.Format,
                        RollbackFileId = entry.RollbackFileId,
                        RollbackOffset = entry.RollbackOffset,
                        Removed = entry.Removed
                    };
                    writer.WriteImage(imageRecord);

                    // Read and write metadata section (areas + files)
                    (List<AreaRecord>? areas, List<FileRecord>? files) = reader.ReadImageMetadata(entry.ImageId);
                    writer.WriteAreas(entry.ImageId, areas);
                    writer.WriteFiles(entry.ImageId, files);
                    areaCount += areas.Count;
                    fileCount += files.Count;

                    // Read and write block map section (offsets)
                    (List<OffsetRecord>? offsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> _) = reader.ReadImageBlockMap(entry.ImageId);
                    writer.WriteOffsets(entry.ImageId, offsets);
                    offsetCount += offsets.Count;
                }
                catch (Exception ex) when (ex is not IOException and not InvalidOperationException and not OutOfMemoryException)
                {
                    throw new InvalidOperationException(
                        $"Failed to parse image_metadata/image_blockmap section for image_id={entry.ImageId} ('{entry.Name}'): {ex.Message}", ex);
                }

                processedImages++;
                progress?.Report($"Processed image {processedImages}/{imageCount}: {entry.Name}");
            }

            // Load block index and stream entries into SQLite block table
            progress?.Report("Loading block index...");
            InMemoryBlockIndex blockIndex = reader.LoadBlockIndex();
            BlockIndexEntry[] entries = blockIndex.GetEntries();
            blockCount = entries.Length;

            progress?.Report($"Writing {blockCount} block entries...");
            writer.WriteBlocks(entries);

            // Validate image count matches (Req 4.3)
            if (processedImages != imageCount)
            {
                throw new InvalidOperationException(
                    $"Validation failed: expected {imageCount} images, got {processedImages}");
            }

            // Commit transaction
            writer.Commit();
            progress?.Report("Transaction committed.");
        }
        catch
        {
            // Clean up partial output on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        // Atomic rename: move temp file to final output path
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        File.Move(tempPath, outputPath);

        stopwatch.Stop();
        progress?.Report($"Conversion complete in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return new ConversionResult(
            ImageCount: imageCount,
            AreaCount: areaCount,
            OffsetCount: offsetCount,
            BlockCount: blockCount,
            FileCount: fileCount,
            Duration: stopwatch.Elapsed);
    }
}