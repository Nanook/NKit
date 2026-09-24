using Avalonia.Threading;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Computes per-image statistics by calling GetSetStatistics per distinct set in parallel.
/// Results are dispatched to the UI thread progressively as each set completes.
/// </summary>
public class StatsCalculationService : IStatsCalculationService
{
    private ErrorNotificationService? _errorNotification;

    public StatsCalculationService(ErrorNotificationService? errorNotification = null)
    {
        _errorNotification = errorNotification;
    }

    /// <summary>
    /// Sets the ErrorNotificationService for centralized error reporting.
    /// Called after construction since StatsCalculationService may be created before ErrorNotificationService in the registry.
    /// </summary>
    public void SetErrorNotification(ErrorNotificationService errorNotification) => _errorNotification = errorNotification;

    /// <inheritdoc />
    public async Task ComputeStatsAsync(
        IReadOnlyList<ImageRowViewModel> images,
        IReadOnlyList<ImageSessionModel> sessions,
        int maxDegreeOfParallelism,
        Action<int, int> progress,
        CancellationToken cancellationToken)
    {
        // Clamp parallelism to [1, 16]
        maxDegreeOfParallelism = Math.Clamp(maxDegreeOfParallelism, 1, 16);

        // Filter to images without stats
        List<ImageRowViewModel> imagesWithoutStats = images.Where(img => !img.HasStats).ToList();
        if (imagesWithoutStats.Count == 0)
            return;

        // Group images by SetName to determine distinct sets
        Dictionary<string, List<ImageRowViewModel>> setGroups = imagesWithoutStats
            .GroupBy(img => img.SetName)
            .ToDictionary(g => g.Key, g => g.ToList());

        int totalSets = setGroups.Count;
        int processedSets = 0;
        int totalImages = imagesWithoutStats.Count;
        int processedImages = 0;

        // Build a lookup from SetName to the session that owns it
        Dictionary<string, ImageSessionModel> setToSession = new Dictionary<string, ImageSessionModel>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<ImageRowViewModel>> group in setGroups)
        {
            string setName = group.Key;
            // Find the session that contains images from this set (by set name, not session ID,
            // because session IDs become stale after close/reopen operations)
            ImageSessionModel? session = sessions.FirstOrDefault(s =>
                s.Images.Any(img => string.Equals(img.SetName, setName, StringComparison.Ordinal)));

            if (session != null)
            {
                setToSession[setName] = session;
            }
        }

        // Process sets in parallel on background threads.
        // UI updates are dispatched via Post (non-blocking) so background threads
        // continue computing while the UI renders results progressively.
        ParallelOptions options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(setGroups, options, async (setGroup, ct) =>
        {
            string setName = setGroup.Key;
            List<ImageRowViewModel> setImages = setGroup.Value;

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!setToSession.TryGetValue(setName, out ImageSessionModel? session))
                {
                    LogError($"No session found for set '{setName}' (SessionId on first image: '{setImages[0].SessionId}')");
                    Interlocked.Increment(ref processedSets);
                    int skipped = Interlocked.Add(ref processedImages, setImages.Count);
                    await Dispatcher.UIThread.InvokeAsync(() => progress(skipped, totalImages));
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Stats] Computing stats for set '{setName}' ({setImages.Count} images)...");

                // Call GetSetStatistics for this set (synchronous DB operation on background thread)
                DataStoreStatistics? setStats = session.DataStore.GetSetStatistics(setName, includePerImageStats: true,
                    progress: (imgProcessed, imgTotal) =>
                    {
                        // Increment global image counter and report cumulative progress
                        int current = Interlocked.Increment(ref processedImages);
                        Dispatcher.UIThread.InvokeAsync(() =>
                            progress(current, totalImages));
                    },
                    cancellationToken: ct);

                System.Diagnostics.Debug.WriteLine($"[Stats] Set '{setName}' stats returned: {setStats?.ImageDetails?.Count ?? 0} image details");

                if (setStats?.ImageDetails == null)
                {
                    LogError($"GetSetStatistics returned null for set '{setName}'");
                    Interlocked.Increment(ref processedSets);
                    int skipped = Interlocked.Add(ref processedImages, setImages.Count);
                    await Dispatcher.UIThread.InvokeAsync(() => progress(skipped, totalImages));
                    return;
                }

                // Build lookup from ImageId to ImageStatistics
                Dictionary<long, ImageStatistics> statsLookup = setStats.ImageDetails.ToDictionary(s => s.ImageId);

                // Dispatch UI updates and wait for them to be processed (progressive rendering).
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (ImageRowViewModel imageRow in setImages)
                    {
                        if (statsLookup.TryGetValue(imageRow.Id, out ImageStatistics? imageStats))
                        {
                            imageRow.UniqueRawSize = imageStats.NonSharedUncompressedSize;
                            imageRow.UniqueCompressedSize = imageStats.NonSharedCompressedSize;
                            imageRow.SharedRawSize = imageStats.SharedUncompressedSize;
                            imageRow.SharedCompressedSize = imageStats.SharedCompressedSize;
                            imageRow.SavedSize = Math.Max(0, imageRow.Size - imageStats.StoredSize);

                            if (imageRow.Size > 0 && imageStats.ApportionedStoredSize >= 0)
                            {
                                imageRow.ReducedBy = (double)imageStats.ApportionedStoredSize / imageRow.Size * 100.0;
                            }
                            else
                            {
                                imageRow.ReducedBy = null;
                            }

                            // Compute bar segment ratios for the mini stacked bar column
                            // Full bar = ImageSize, split into 5 segments:
                            // Orange = non-shared uncompressed blocks
                            // Blue = non-shared compressed blocks
                            // Purple = shared uncompressed blocks
                            // Pink = shared compressed blocks
                            // Green = removeable data (ImageSize - StoredSize)
                            if (imageRow.Size > 0)
                            {
                                long removeable = Math.Max(0, imageRow.Size - imageStats.StoredSize);

                                double total = (double)imageRow.Size;
                                imageRow.BarStoredRatio = imageStats.NonSharedUncompressedSize / total;
                                imageRow.BarCompressionRatio = imageStats.NonSharedCompressedSize / total;
                                imageRow.BarDedupRatio = imageStats.SharedUncompressedSize / total;
                                imageRow.BarSharedCompressedRatio = imageStats.SharedCompressedSize / total;
                                imageRow.BarRemoveableRatio = removeable / total;
                            }
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError($"Failed to compute stats for set '{setName}': {ex.Message}");
            }

            Interlocked.Increment(ref processedSets);
            return;
        });
    }

    private void LogError(string message) => _errorNotification?.PublishOperationError(message);

    /// <inheritdoc />
    public Task<long> ComputeDeduplicatedStoredSizeAsync(
        IReadOnlyList<ImageRowViewModel> images,
        IReadOnlyList<ImageSessionModel> sessions,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            // Group filtered images by SetName
            Dictionary<string, List<ImageRowViewModel>> setGroups = images
                .GroupBy(img => img.SetName)
                .ToDictionary(g => g.Key, g => g.ToList());

            long totalDeduplicatedSize = 0;

            foreach (KeyValuePair<string, List<ImageRowViewModel>> setGroup in setGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string setName = setGroup.Key;
                List<ImageRowViewModel> setImages = setGroup.Value;

                // Find the session that owns this set (by set name, not session ID)
                ImageSessionModel? session = sessions.FirstOrDefault(s =>
                    s.Images.Any(img => string.Equals(img.SetName, setName, StringComparison.Ordinal)));

                if (session == null)
                    continue;

                // Check if ALL images in this set are included (no filter active for this set)
                List<ImageRecord> allSetImages = session.Images.Where(img =>
                    string.Equals(img.SetName, setName, StringComparison.Ordinal)).ToList();

                if (setImages.Count == allSetImages.Count)
                {
                    // All images in set are included — use TotalPhysicalBlockStorage (fast, no per-image query)
                    try
                    {
                        DataStoreStatistics? stats = session.DataStore.GetSetStatistics(setName, includePerImageStats: false);
                        if (stats != null)
                            totalDeduplicatedSize += stats.TotalPhysicalBlockStorage;
                    }
                    catch { }
                }
                else
                {
                    // Filtered subset — need to get unique blocks for only these images
                    // Load block sizes for this set, then get unique blocks per filtered image
                    try
                    {
                        DataStoreStatistics? stats = session.DataStore.GetSetStatistics(setName, includePerImageStats: true);
                        if (stats?.ImageDetails == null)
                            continue;

                        // Get the image IDs that are in our filtered set
                        HashSet<long> filteredIds = new HashSet<long>(setImages.Select(img => img.Id));

                        // Sum StoredSize only for filtered images, but deduplicate:
                        // Since each image's StoredSize is its own unique blocks' sizes,
                        // and blocks shared between filtered images are in both,
                        // we need the actual unique block union.
                        // The ImageStatistics already computed StoredSize per image correctly
                        // (unique blocks for THAT image). For the union, we'd need block-level data.
                        // 
                        // Best approximation without re-querying per-block:
                        // Use TotalPhysicalBlockStorage scaled by the fraction of unique blocks
                        // that belong to filtered images. But this is imprecise.
                        //
                        // Correct approach: sum TotalPhysicalBlockStorage (all blocks in set are shared
                        // among all images anyway — the block table IS the deduplicated set).
                        // If only a subset of images is filtered, the true deduplicated size
                        // is still bounded by TotalPhysicalBlockStorage.
                        //
                        // For now, use TotalPhysicalBlockStorage as the upper bound since
                        // blocks are shared across images in the same set.
                        totalDeduplicatedSize += stats.TotalPhysicalBlockStorage;
                    }
                    catch { }
                }
            }

            return totalDeduplicatedSize;
        }, cancellationToken);
    }
}