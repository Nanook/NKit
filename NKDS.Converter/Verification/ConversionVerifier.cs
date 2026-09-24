using NKDS.Converter.Binary;
using NKDS.Converter.Sqlite;
using NKitDataStore;
using NKitDataStore.Binary;

namespace NKDS.Converter.Verification;

/// <summary>
/// Performs post-conversion verification by re-reading both source and output files
/// and comparing all records field-by-field.
/// </summary>
internal class ConversionVerifier
{
    /// <summary>
    /// Represents a single field-level mismatch between source and output.
    /// </summary>
    public record Mismatch(string Table, string RecordId, string Field, string Expected, string Actual);

    /// <summary>
    /// Verifies that the output file contains the same logical data as the source file.
    /// </summary>
    /// <param name="sourcePath">Path to the source file.</param>
    /// <param name="outputPath">Path to the converted output file.</param>
    /// <param name="direction">"to-binary" (source=SQLite, output=Binary) or "to-sqlite" (source=Binary, output=SQLite).</param>
    /// <returns>A list of mismatches. Empty list means verification passed.</returns>
    public List<Mismatch> Verify(string sourcePath, string outputPath, string direction)
    {
        List<Mismatch> mismatches = new List<Mismatch>();

        if (direction == "to-binary")
        {
            // Source is SQLite, output is Binary
            using SqliteIndexReader source = new SqliteIndexReader(sourcePath);
            using BinaryIndexReader output = new BinaryIndexReader(outputPath);
            CompareData(source, output, mismatches);
        }
        else
        {
            // Source is Binary, output is SQLite
            using BinaryIndexReader source = new BinaryIndexReader(sourcePath);
            using SqliteIndexReader output = new SqliteIndexReader(outputPath);
            CompareData(source, output, mismatches);
        }

        return mismatches;
    }

    /// <summary>
    /// Compares data when source is SQLite and output is Binary.
    /// </summary>
    private void CompareData(SqliteIndexReader source, BinaryIndexReader output, List<Mismatch> mismatches)
    {
        // Compare info
        InfoRecord sourceInfo = source.ReadInfo();
        InfoRecord outputInfo = output.ReadInfo();
        CompareInfo(sourceInfo, outputInfo, mismatches);

        // Compare images
        List<ImageRecord> sourceImages = source.ReadAllImages().ToList();
        IReadOnlyList<ImageDirectoryEntry> outputDirectory = output.ReadDirectory();

        CompareImageCounts(sourceImages.Count, outputDirectory.Count, mismatches);

        foreach (ImageRecord sourceImage in sourceImages)
        {
            ImageDirectoryEntry? dirEntry = outputDirectory.FirstOrDefault(d => d.ImageId == sourceImage.Id);
            if (dirEntry == null)
            {
                mismatches.Add(new Mismatch("image", $"id={sourceImage.Id}", "existence", "present", "missing"));
                continue;
            }

            CompareImageToDirectory(sourceImage, dirEntry, mismatches);

            // Compare areas and files from metadata section
            (List<AreaRecord>? outputAreas, List<FileRecord>? outputFiles) = output.ReadImageMetadata(sourceImage.Id);
            List<AreaRecord> sourceAreas = source.ReadAreasForImage(sourceImage.Id).ToList();
            List<FileRecord> sourceFiles = source.ReadFilesForImage(sourceImage.Id).ToList();

            CompareAreas(sourceImage.Id, sourceAreas, outputAreas, mismatches);
            CompareFiles(sourceImage.Id, sourceFiles, outputFiles, mismatches);

            // Compare offsets from block map section
            (List<OffsetRecord>? outputOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> _) = output.ReadImageBlockMap(sourceImage.Id);
            List<OffsetRecord> sourceOffsets = source.ReadOffsetsForImage(sourceImage.Id).ToList();

            CompareOffsets(sourceImage.Id, sourceOffsets, outputOffsets, mismatches);
        }

        // Compare blocks
        CompareBlocksSqliteVsBinary(source, output, mismatches);
    }

    /// <summary>
    /// Compares data when source is Binary and output is SQLite.
    /// </summary>
    private void CompareData(BinaryIndexReader source, SqliteIndexReader output, List<Mismatch> mismatches)
    {
        // Compare info
        InfoRecord sourceInfo = source.ReadInfo();
        InfoRecord outputInfo = output.ReadInfo();
        CompareInfo(sourceInfo, outputInfo, mismatches);

        // Compare images
        IReadOnlyList<ImageDirectoryEntry> sourceDirectory = source.ReadDirectory();
        List<ImageRecord> outputImages = output.ReadAllImages().ToList();

        CompareImageCounts(sourceDirectory.Count, outputImages.Count, mismatches);

        foreach (ImageDirectoryEntry dirEntry in sourceDirectory)
        {
            ImageRecord? outputImage = outputImages.FirstOrDefault(i => i.Id == dirEntry.ImageId);
            if (outputImage == null)
            {
                mismatches.Add(new Mismatch("image", $"id={dirEntry.ImageId}", "existence", "present", "missing"));
                continue;
            }

            CompareImageToDirectory(outputImage, dirEntry, mismatches, reversed: true);

            // Compare areas and files
            (List<AreaRecord>? sourceAreas, List<FileRecord>? sourceFiles) = source.ReadImageMetadata(dirEntry.ImageId);
            List<AreaRecord> outputAreas = output.ReadAreasForImage(dirEntry.ImageId).ToList();
            List<FileRecord> outputFiles = output.ReadFilesForImage(dirEntry.ImageId).ToList();

            CompareAreas(dirEntry.ImageId, sourceAreas, outputAreas, mismatches);
            CompareFiles(dirEntry.ImageId, sourceFiles, outputFiles, mismatches);

            // Compare offsets
            (List<OffsetRecord>? sourceOffsets, Dictionary<BlockKey, (int FileId, long Offset, int Size)> _) = source.ReadImageBlockMap(dirEntry.ImageId);
            List<OffsetRecord> outputOffsets = output.ReadOffsetsForImage(dirEntry.ImageId).ToList();

            CompareOffsets(dirEntry.ImageId, sourceOffsets, outputOffsets, mismatches);
        }

        // Compare blocks
        CompareBlocksBinaryVsSqlite(source, output, mismatches);
    }

    private void CompareInfo(InfoRecord source, InfoRecord output, List<Mismatch> mismatches)
    {
        if (source.ShardSize != output.ShardSize)
            mismatches.Add(new Mismatch("info", "row=1", "shard_size", source.ShardSize.ToString(), output.ShardSize.ToString()));
        if (source.BlockSize != output.BlockSize)
            mismatches.Add(new Mismatch("info", "row=1", "block_size", source.BlockSize.ToString(), output.BlockSize.ToString()));
        if (source.MaxOffsetBlocks != output.MaxOffsetBlocks)
            mismatches.Add(new Mismatch("info", "row=1", "max_offset_blocks", source.MaxOffsetBlocks.ToString(), output.MaxOffsetBlocks.ToString()));
    }

    private void CompareImageCounts(int sourceCount, int outputCount, List<Mismatch> mismatches)
    {
        if (sourceCount != outputCount)
            mismatches.Add(new Mismatch("image", "count", "total", sourceCount.ToString(), outputCount.ToString()));
    }

    private void CompareImageToDirectory(ImageRecord image, ImageDirectoryEntry dir, List<Mismatch> mismatches, bool reversed = false)
    {
        string id = $"id={image.Id}";

        // When reversed=true, the directory entry is the "source" (expected) and image is the "actual" (output)
        // But we always compare the same fields, just swap expected/actual labels
        string expectedName = reversed ? dir.Name : image.Name;
        string actualName = reversed ? image.Name : dir.Name;
        if (expectedName != actualName)
            mismatches.Add(new Mismatch("image", id, "name", expectedName, actualName));

        long expectedSize = reversed ? dir.Size : image.Size;
        long actualSize = reversed ? image.Size : dir.Size;
        if (expectedSize != actualSize)
            mismatches.Add(new Mismatch("image", id, "size", expectedSize.ToString(), actualSize.ToString()));

        uint expectedCrc32 = reversed ? dir.Crc32 : image.Crc32;
        uint actualCrc32 = reversed ? image.Crc32 : dir.Crc32;
        if (expectedCrc32 != actualCrc32)
            mismatches.Add(new Mismatch("image", id, "crc32", $"0x{expectedCrc32:X8}", $"0x{actualCrc32:X8}"));

        ulong expectedXxHash = reversed ? dir.XxHash64 : image.XxHash64;
        ulong actualXxHash = reversed ? image.XxHash64 : dir.XxHash64;
        if (expectedXxHash != actualXxHash)
            mismatches.Add(new Mismatch("image", id, "xxhash64", $"0x{expectedXxHash:X16}", $"0x{actualXxHash:X16}"));

        string? expectedSystem = reversed ? dir.System : image.System;
        string? actualSystem = reversed ? image.System : dir.System;
        if (expectedSystem != actualSystem)
            mismatches.Add(new Mismatch("image", id, "system", expectedSystem ?? "null", actualSystem ?? "null"));

        ImageFormat expectedFormat = reversed ? dir.Format : image.Format;
        ImageFormat actualFormat = reversed ? image.Format : dir.Format;
        if (expectedFormat != actualFormat)
            mismatches.Add(new Mismatch("image", id, "format", expectedFormat.ToString(), actualFormat.ToString()));

        int? expectedRollbackFileId = reversed ? dir.RollbackFileId : image.RollbackFileId;
        int? actualRollbackFileId = reversed ? image.RollbackFileId : dir.RollbackFileId;
        if (expectedRollbackFileId != actualRollbackFileId)
            mismatches.Add(new Mismatch("image", id, "rollback_file_id", expectedRollbackFileId?.ToString() ?? "null", actualRollbackFileId?.ToString() ?? "null"));

        long? expectedRollbackOffset = reversed ? dir.RollbackOffset : image.RollbackOffset;
        long? actualRollbackOffset = reversed ? image.RollbackOffset : dir.RollbackOffset;
        if (expectedRollbackOffset != actualRollbackOffset)
            mismatches.Add(new Mismatch("image", id, "rollback_offset", expectedRollbackOffset?.ToString() ?? "null", actualRollbackOffset?.ToString() ?? "null"));

        bool expectedRemoved = reversed ? dir.Removed : image.Removed;
        bool actualRemoved = reversed ? image.Removed : dir.Removed;
        if (expectedRemoved != actualRemoved)
            mismatches.Add(new Mismatch("image", id, "removed", expectedRemoved.ToString(), actualRemoved.ToString()));
    }

    private void CompareAreas(long imageId, List<AreaRecord> source, List<AreaRecord> output, List<Mismatch> mismatches)
    {
        if (source.Count != output.Count)
        {
            mismatches.Add(new Mismatch("area", $"image_id={imageId}", "count", source.Count.ToString(), output.Count.ToString()));
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            AreaRecord s = source[i];
            AreaRecord o = output[i];
            string id = $"image_id={imageId},index={i}";

            if (s.Offset != o.Offset)
                mismatches.Add(new Mismatch("area", id, "offset", s.Offset.ToString(), o.Offset.ToString()));
            if (s.Size != o.Size)
                mismatches.Add(new Mismatch("area", id, "size", s.Size.ToString(), o.Size.ToString()));
            if (s.StrideBlockSize != o.StrideBlockSize)
                mismatches.Add(new Mismatch("area", id, "stride_block_size", s.StrideBlockSize.ToString(), o.StrideBlockSize.ToString()));
            if (s.StrideDataOffset != o.StrideDataOffset)
                mismatches.Add(new Mismatch("area", id, "stride_data_offset", s.StrideDataOffset.ToString(), o.StrideDataOffset.ToString()));
            if (s.StrideDataLength != o.StrideDataLength)
                mismatches.Add(new Mismatch("area", id, "stride_data_length", s.StrideDataLength.ToString(), o.StrideDataLength.ToString()));
            if (s.SectionSize != o.SectionSize)
                mismatches.Add(new Mismatch("area", id, "section_size", s.SectionSize.ToString(), o.SectionSize.ToString()));
            if (s.Crc32 != o.Crc32)
                mismatches.Add(new Mismatch("area", id, "crc32", $"0x{s.Crc32:X8}", $"0x{o.Crc32:X8}"));
            if (s.XxHash64 != o.XxHash64)
                mismatches.Add(new Mismatch("area", id, "xxhash64", $"0x{s.XxHash64:X16}", $"0x{o.XxHash64:X16}"));

            // Compare metadata blobs
            byte[] sourceBlob = s.Metadata.ToBlob();
            byte[] outputBlob = o.Metadata.ToBlob();
            if (!sourceBlob.SequenceEqual(outputBlob))
                mismatches.Add(new Mismatch("area", id, "metadata", Convert.ToHexString(sourceBlob), Convert.ToHexString(outputBlob)));
        }
    }

    private void CompareOffsets(long imageId, List<OffsetRecord> source, List<OffsetRecord> output, List<Mismatch> mismatches)
    {
        if (source.Count != output.Count)
        {
            mismatches.Add(new Mismatch("offset", $"image_id={imageId}", "count", source.Count.ToString(), output.Count.ToString()));
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            OffsetRecord s = source[i];
            OffsetRecord o = output[i];
            string id = $"image_id={imageId},offset={s.Offset}";

            if (s.Offset != o.Offset)
                mismatches.Add(new Mismatch("offset", id, "offset", s.Offset.ToString(), o.Offset.ToString()));
            if (s.Size != o.Size)
                mismatches.Add(new Mismatch("offset", id, "size", s.Size.ToString(), o.Size.ToString()));
            if (s.Type != o.Type)
                mismatches.Add(new Mismatch("offset", id, "type_id", ((int)s.Type).ToString(), ((int)o.Type).ToString()));
            if (s.OffsetStart != o.OffsetStart)
                mismatches.Add(new Mismatch("offset", id, "offset_start", s.OffsetStart.ToString(), o.OffsetStart.ToString()));

            // Compare blocks lists
            List<BlockKey> sourceBlocks = s.Blocks ?? new List<BlockKey>();
            List<BlockKey> outputBlocks = o.Blocks ?? new List<BlockKey>();

            if (sourceBlocks.Count != outputBlocks.Count)
            {
                mismatches.Add(new Mismatch("offset", id, "blocks.count", sourceBlocks.Count.ToString(), outputBlocks.Count.ToString()));
            }
            else
            {
                for (int b = 0; b < sourceBlocks.Count; b++)
                {
                    if (sourceBlocks[b] != outputBlocks[b])
                    {
                        mismatches.Add(new Mismatch("offset", id, $"blocks[{b}]", sourceBlocks[b].ToString(), outputBlocks[b].ToString()));
                    }
                }
            }
        }
    }

    private void CompareFiles(long imageId, List<FileRecord> source, List<FileRecord> output, List<Mismatch> mismatches)
    {
        if (source.Count != output.Count)
        {
            mismatches.Add(new Mismatch("file", $"image_id={imageId}", "count", source.Count.ToString(), output.Count.ToString()));
            return;
        }

        // Sort both by name for consistent comparison
        List<FileRecord> sortedSource = source.OrderBy(f => f.Name).ToList();
        List<FileRecord> sortedOutput = output.OrderBy(f => f.Name).ToList();

        for (int i = 0; i < sortedSource.Count; i++)
        {
            FileRecord s = sortedSource[i];
            FileRecord o = sortedOutput[i];
            string id = $"image_id={imageId},name={s.Name}";

            if (s.Name != o.Name)
                mismatches.Add(new Mismatch("file", id, "name", s.Name, o.Name));
            if (s.FileId != o.FileId)
                mismatches.Add(new Mismatch("file", id, "file_id", s.FileId.ToString(), o.FileId.ToString()));
            if (s.Offset != o.Offset)
                mismatches.Add(new Mismatch("file", id, "offset", s.Offset.ToString(), o.Offset.ToString()));
            if (s.Size != o.Size)
                mismatches.Add(new Mismatch("file", id, "size", s.Size.ToString(), o.Size.ToString()));
            if (s.UncompressedSize != o.UncompressedSize)
                mismatches.Add(new Mismatch("file", id, "uncompressed_size", s.UncompressedSize.ToString(), o.UncompressedSize.ToString()));
            if (s.IsSystem != o.IsSystem)
                mismatches.Add(new Mismatch("file", id, "is_system", s.IsSystem.ToString(), o.IsSystem.ToString()));
        }
    }

    private void CompareBlocksSqliteVsBinary(SqliteIndexReader source, BinaryIndexReader output, List<Mismatch> mismatches)
    {
        // Build a dictionary from the binary block index for lookup
        InMemoryBlockIndex blockIndex = output.LoadBlockIndex();
        BlockIndexEntry[] outputEntries = blockIndex.GetEntries();
        Dictionary<BlockKey, (int FileId, long Offset, int Size)> outputDict = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
        foreach (BlockIndexEntry entry in outputEntries)
        {
            outputDict[entry.Key] = (entry.FileId, entry.Offset, entry.Size);
        }

        // Read all blocks from SQLite source and compare
        int sourceCount = 0;
        foreach (BlockIndexEntry sourceEntry in source.ReadAllBlocks())
        {
            sourceCount++;
            string id = $"key={sourceEntry.Key}";

            if (!outputDict.TryGetValue(sourceEntry.Key, out (int FileId, long Offset, int Size) outputLoc))
            {
                mismatches.Add(new Mismatch("block", id, "existence", "present", "missing"));
                continue;
            }

            if (sourceEntry.FileId != outputLoc.FileId)
                mismatches.Add(new Mismatch("block", id, "file_id", sourceEntry.FileId.ToString(), outputLoc.FileId.ToString()));
            if (sourceEntry.Offset != outputLoc.Offset)
                mismatches.Add(new Mismatch("block", id, "offset", sourceEntry.Offset.ToString(), outputLoc.Offset.ToString()));
            if (sourceEntry.Size != outputLoc.Size)
                mismatches.Add(new Mismatch("block", id, "size", sourceEntry.Size.ToString(), outputLoc.Size.ToString()));
        }

        if (sourceCount != outputEntries.Length)
            mismatches.Add(new Mismatch("block", "count", "total", sourceCount.ToString(), outputEntries.Length.ToString()));
    }

    private void CompareBlocksBinaryVsSqlite(BinaryIndexReader source, SqliteIndexReader output, List<Mismatch> mismatches)
    {
        // Build a dictionary from the SQLite output for lookup
        Dictionary<BlockKey, (int FileId, long Offset, int Size)> outputDict = new Dictionary<BlockKey, (int FileId, long Offset, int Size)>();
        foreach (BlockIndexEntry entry in output.ReadAllBlocks())
        {
            outputDict[entry.Key] = (entry.FileId, entry.Offset, entry.Size);
        }

        // Read all blocks from binary source and compare
        InMemoryBlockIndex blockIndex = source.LoadBlockIndex();
        BlockIndexEntry[] sourceEntries = blockIndex.GetEntries();

        foreach (BlockIndexEntry sourceEntry in sourceEntries)
        {
            string id = $"key={sourceEntry.Key}";

            if (!outputDict.TryGetValue(sourceEntry.Key, out (int FileId, long Offset, int Size) outputLoc))
            {
                mismatches.Add(new Mismatch("block", id, "existence", "present", "missing"));
                continue;
            }

            if (sourceEntry.FileId != outputLoc.FileId)
                mismatches.Add(new Mismatch("block", id, "file_id", sourceEntry.FileId.ToString(), outputLoc.FileId.ToString()));
            if (sourceEntry.Offset != outputLoc.Offset)
                mismatches.Add(new Mismatch("block", id, "offset", sourceEntry.Offset.ToString(), outputLoc.Offset.ToString()));
            if (sourceEntry.Size != outputLoc.Size)
                mismatches.Add(new Mismatch("block", id, "size", sourceEntry.Size.ToString(), outputLoc.Size.ToString()));
        }

        if (sourceEntries.Length != outputDict.Count)
            mismatches.Add(new Mismatch("block", "count", "total", sourceEntries.Length.ToString(), outputDict.Count.ToString()));
    }
}