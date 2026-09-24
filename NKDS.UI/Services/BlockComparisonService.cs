using NkdsUi.Models;
using NKitDataStore;
using NKitDataStore.Interfaces;
using ReactiveUI.Primitives;

namespace NkdsUi.Services;

/// <summary>
/// Performs block-level comparison between images using only index metadata (BlockKey pairs from OffsetRecords).
/// Accesses IDataStoreDataAccess internally via the DataStore's internal API.
/// </summary>
public class BlockComparisonService : IBlockComparisonService
{
    private readonly IDataStoreService _dataStoreService;

    public BlockComparisonService(IDataStoreService dataStoreService)
    {
        _dataStoreService = dataStoreService;
    }

    /// <inheritdoc />
    public Task<List<ComparisonResultModel>> CompareImageAsync(
        ImageRecord referenceImage,
        IEnumerable<ImageRecord> candidateImages,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Load reference image BlockKeys into a HashSet
            IDataStoreDataAccess? dataAccess = GetDataAccessForImage(referenceImage);
            if (dataAccess == null)
                return new List<ComparisonResultModel>();

            HashSet<BlockKey> referenceBlockKeys = LoadBlockKeysForImage(dataAccess, referenceImage);

            if (referenceBlockKeys.Count == 0)
                return new List<ComparisonResultModel>();

            int referenceBlockCount = referenceBlockKeys.Count;

            // Materialize candidates to get count for progress reporting
            IList<ImageRecord> candidates = candidateImages as IList<ImageRecord> ?? candidateImages.ToList();
            List<ComparisonResultModel> results = new List<ComparisonResultModel>();

            for (int i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ImageRecord candidate = candidates[i];

                // Skip comparing image to itself
                if (candidate.SetName == referenceImage.SetName && candidate.Id == referenceImage.Id)
                {
                    progress?.Report((double)(i + 1) / candidates.Count);
                    continue;
                }

                IDataStoreDataAccess? candidateDataAccess = GetDataAccessForImage(candidate);
                if (candidateDataAccess == null)
                {
                    progress?.Report((double)(i + 1) / candidates.Count);
                    continue;
                }

                // Count intersection efficiently without materializing a new collection
                int sharedCount = CountIntersection(candidateDataAccess, candidate, referenceBlockKeys);

                if (sharedCount > 0)
                {
                    double matchPercentage = (double)sharedCount / referenceBlockCount * 100.0;
                    results.Add(new ComparisonResultModel
                    {
                        MatchedImage = candidate,
                        MatchPercentage = matchPercentage,
                        SharedBlockCount = sharedCount,
                        ReferenceBlockCount = referenceBlockCount
                    });
                }

                progress?.Report((double)(i + 1) / candidates.Count);
            }

            // Sort by match percentage descending and return top 10
            return results
                .OrderByDescending(r => r.MatchPercentage)
                .Take(10)
                .ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<List<CommonFileModel>> FindCommonFilesAsync(
        IReadOnlyList<ImageRecord> selectedImages,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (selectedImages.Count < 2)
                return new List<CommonFileModel>();

            // Step 1: For each image, collect all BlockKeys into a set
            HashSet<BlockKey>? intersectedKeys = null;

            for (int i = 0; i < selectedImages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ImageRecord image = selectedImages[i];
                IDataStoreDataAccess? dataAccess = GetDataAccessForImage(image);
                if (dataAccess == null)
                    return new List<CommonFileModel>();

                HashSet<BlockKey> imageBlockKeys = LoadBlockKeysForImage(dataAccess, image);

                if (intersectedKeys == null)
                    intersectedKeys = imageBlockKeys;
                else
                    intersectedKeys.IntersectWith(imageBlockKeys);

                progress?.Report((double)(i + 1) / (selectedImages.Count + 1));
            }

            if (intersectedKeys == null || intersectedKeys.Count == 0)
                return new List<CommonFileModel>();

            cancellationToken.ThrowIfCancellationRequested();

            // Step 2: Group intersected keys by OffsetStart from the first image
            ImageRecord firstImage = selectedImages[0];
            IDataStoreDataAccess firstDataAccess = GetDataAccessForImage(firstImage)!;
            IEnumerable<OffsetRecord> offsets = firstDataAccess.GetOffsetsForImage(firstImage.SetName, firstImage.Id);

            // Load areas for the first image to get AreaMetadata for display names
            List<AreaRecord> areas = firstDataAccess.GetAreasForImage(firstImage.SetName, firstImage.Id)
                .OrderBy(a => a.Offset)
                .ToList();

            // Group offset records by OffsetStart, collecting intersected block keys and sizes
            Dictionary<long, (int sharedBlockCount, long totalSizeBytes)> fileGroups = new Dictionary<long, (int sharedBlockCount, long totalSizeBytes)>();

            foreach (OffsetRecord offset in offsets)
            {
                if (!offset.HasBlocks)
                    continue;

                int sharedInOffset = 0;
                foreach (BlockKey blockKey in offset.Blocks!)
                {
                    if (intersectedKeys.Contains(blockKey))
                        sharedInOffset++;
                }

                if (sharedInOffset > 0)
                {
                    if (fileGroups.TryGetValue(offset.OffsetStart, out (int sharedBlockCount, long totalSizeBytes) existing))
                    {
                        fileGroups[offset.OffsetStart] = (
                            existing.sharedBlockCount + sharedInOffset,
                            existing.totalSizeBytes + offset.Size);
                    }
                    else
                    {
                        fileGroups[offset.OffsetStart] = (sharedInOffset, offset.Size);
                    }
                }
            }

            // Step 3: Build CommonFileModel results with display names
            List<CommonFileModel> results = new List<CommonFileModel>(fileGroups.Count);

            foreach ((long offsetStart, (int sharedBlockCount, long totalSizeBytes)) in fileGroups)
            {
                string displayName = GetDisplayNameForOffset(offsetStart, areas);

                results.Add(new CommonFileModel
                {
                    DisplayName = displayName,
                    OffsetStart = offsetStart,
                    SharedBlockCount = sharedBlockCount,
                    TotalSizeBytes = totalSizeBytes
                });
            }

            progress?.Report(1.0);

            // Sort by TotalSizeBytes descending
            return results
                .OrderByDescending(r => r.TotalSizeBytes)
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Loads all BlockKeys for an image into a HashSet, skipping OffsetRecords where HasBlocks is false.
    /// </summary>
    private static HashSet<BlockKey> LoadBlockKeysForImage(IDataStoreDataAccess dataAccess, ImageRecord image)
    {
        HashSet<BlockKey> blockKeys = new HashSet<BlockKey>();
        IEnumerable<OffsetRecord> offsets = dataAccess.GetOffsetsForImage(image.SetName, image.Id);

        foreach (OffsetRecord offset in offsets)
        {
            if (!offset.HasBlocks)
                continue;

            foreach (BlockKey blockKey in offset.Blocks!)
            {
                blockKeys.Add(blockKey);
            }
        }

        return blockKeys;
    }

    /// <summary>
    /// Counts how many of a candidate image's BlockKeys are present in the reference HashSet.
    /// Uses an efficient iteration approach without materializing the intersection.
    /// </summary>
    private static int CountIntersection(IDataStoreDataAccess dataAccess, ImageRecord candidate, HashSet<BlockKey> referenceBlockKeys)
    {
        int sharedCount = 0;
        IEnumerable<OffsetRecord> offsets = dataAccess.GetOffsetsForImage(candidate.SetName, candidate.Id);

        foreach (OffsetRecord offset in offsets)
        {
            if (!offset.HasBlocks)
                continue;

            foreach (BlockKey blockKey in offset.Blocks!)
            {
                if (referenceBlockKeys.Contains(blockKey))
                    sharedCount++;
            }
        }

        return sharedCount;
    }

    /// <summary>
    /// Gets the display name for an offset group by finding the containing area's metadata.
    /// Falls back to hex representation of the offset start.
    /// </summary>
    private static string GetDisplayNameForOffset(long offsetStart, List<AreaRecord> areas)
    {
        // Find the area that contains this offset
        AreaRecord? area = areas.FirstOrDefault(a => a.Offset <= offsetStart && offsetStart < a.Offset + a.Size);

        if (area != null)
        {
            // Try to get a meaningful name from AreaMetadata
            string? title = area.Metadata[AreaValueType.Title];
            if (!string.IsNullOrEmpty(title))
                return title;

            string? fileName = area.Metadata[AreaValueType.FileName];
            if (!string.IsNullOrEmpty(fileName))
                return fileName;
        }

        // Fall back to hex offset
        return $"0x{offsetStart:X}";
    }

    /// <summary>
    /// Finds the IDataStoreDataAccess for a given image by looking up the session that contains it.
    /// </summary>
    private IDataStoreDataAccess? GetDataAccessForImage(ImageRecord image)
    {
        // Get current sessions from the BehaviorSubject (synchronous access via FirstAsync)
        IReadOnlyList<ImageSessionModel> sessions = _dataStoreService.Sessions
            .FirstAsync()
            .GetAwaiter()
            .GetResult();

        foreach (ImageSessionModel? session in sessions)
        {
            // Check if this session contains the image
            if (session.Images.Any(img => img.SetName == image.SetName && img.Id == image.Id))
            {
                return session.DataStore.DataAccess;
            }
        }

        return null;
    }
}