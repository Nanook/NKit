using NkdsUi.Models;
using NKitDataStore;
using NKitDataStore.Interfaces;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.Services;

/// <summary>
/// Manages the in-memory BlockKey cache for Comparison Mode.
/// Loads BlockKeys from the database into memory, manages the cache lifecycle,
/// and provides cache-only computation for fast comparison operations.
/// </summary>
public class HashCacheService : IHashCacheService
{
    private readonly IDataStoreService _dataStoreService;
    private ErrorNotificationService? _errorNotification;
    private readonly Dictionary<ImageRecord, HashSet<BlockKey>> _cache = new();
    private readonly ReplaySignal<IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>>> _cacheChanged = new();

    public HashCacheService(IDataStoreService dataStoreService, ErrorNotificationService? errorNotification = null)
    {
        _dataStoreService = dataStoreService;
        _errorNotification = errorNotification;
    }

    /// <summary>
    /// Sets the ErrorNotificationService for centralized error reporting.
    /// Called after construction since HashCacheService may be created before ErrorNotificationService in the registry.
    /// </summary>
    public void SetErrorNotification(ErrorNotificationService errorNotification) => _errorNotification = errorNotification;

    /// <inheritdoc />
    public IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> Cache => _cache;

    /// <inheritdoc />
    public IObservable<IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>>> CacheChanged => _cacheChanged.AsObservable();

    /// <inheritdoc />
    public bool IsLoaded => _cache.Count > 0;

    /// <inheritdoc />
    public async Task<HashCacheLoadResult> LoadAsync(
        IReadOnlyList<ImageRecord> images,
        CancellationToken cancellationToken,
        IProgress<(int loaded, int total)>? progress = null)
    {
        Dictionary<ImageRecord, HashSet<BlockKey>> tempCache = new Dictionary<ImageRecord, HashSet<BlockKey>>();
        List<string> failedImages = new List<string>();

        await Task.Run(() =>
        {
            for (int i = 0; i < images.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                ImageRecord image = images[i];

                IDataStoreDataAccess? dataAccess;
                try
                {
                    dataAccess = GetDataAccessForImage(image);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to get data access for image '{image.Name}': {ex.Message}");
                    failedImages.Add(image.Name);
                    progress?.Report((i + 1, images.Count));
                    continue;
                }

                if (dataAccess == null)
                {
                    // Session is closed or image not found in any session
                    failedImages.Add(image.Name);
                    progress?.Report((i + 1, images.Count));
                    continue;
                }

                try
                {
                    HashSet<BlockKey> blockKeys = LoadBlockKeysForImage(dataAccess, image);
                    tempCache[image] = blockKeys;
                }
                catch (Exception ex)
                {
                    LogError($"Failed to load BlockKeys for image '{image.Name}': {ex.Message}");
                    failedImages.Add(image.Name);
                }

                progress?.Report((i + 1, images.Count));
            }
        }, CancellationToken.None);

        // If cancelled, discard partial cache
        if (cancellationToken.IsCancellationRequested)
        {
            return new HashCacheLoadResult
            {
                SuccessCount = 0,
                FailedCount = images.Count,
                FailedImageNames = images.Select(i => i.Name).ToList()
            };
        }

        // Commit the loaded data to the real cache
        _cache.Clear();
        foreach (KeyValuePair<ImageRecord, HashSet<BlockKey>> kvp in tempCache)
        {
            _cache[kvp.Key] = kvp.Value;
        }

        _cacheChanged.OnNext(_cache);

        return new HashCacheLoadResult
        {
            SuccessCount = tempCache.Count,
            FailedCount = failedImages.Count,
            FailedImageNames = failedImages
        };
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        _cacheChanged.OnNext(_cache);
    }

    /// <inheritdoc />
    public void RemoveSession(string sessionId)
    {
        // Get the current sessions to find which images belong to this session
        IReadOnlyList<ImageSessionModel> sessions = _dataStoreService.Sessions
            .FirstAsync()
            .GetAwaiter()
            .GetResult();

        ImageSessionModel? session = sessions.FirstOrDefault(s => s.SessionId == sessionId);
        if (session == null)
        {
            // Session already removed from DataStoreService - find images by matching
            // against the cache entries. We need to remove entries whose images
            // are no longer in any active session.
            // Since the session is already gone, we can't look it up.
            // Instead, remove cache entries whose images are not in any current session.
            HashSet<ImageRecord> allSessionImages = sessions.SelectMany(s => s.Images).ToHashSet();
            List<ImageRecord> keysToRemove = _cache.Keys
                .Where(img => !allSessionImages.Contains(img))
                .ToList();

            if (keysToRemove.Count > 0)
            {
                foreach (ImageRecord key in keysToRemove)
                    _cache.Remove(key);

                _cacheChanged.OnNext(_cache);
            }
            return;
        }

        // Remove all cache entries for images in this session
        HashSet<ImageRecord> sessionImages = session.Images.ToHashSet();
        List<ImageRecord> toRemove = _cache.Keys
            .Where(img => sessionImages.Contains(img))
            .ToList();

        if (toRemove.Count > 0)
        {
            foreach (ImageRecord key in toRemove)
                _cache.Remove(key);

            _cacheChanged.OnNext(_cache);
        }
    }

    /// <inheritdoc />
    public HashSet<BlockKey>? GetBlockKeys(ImageRecord image) => _cache.TryGetValue(image, out HashSet<BlockKey>? blockKeys) ? blockKeys : null;

    /// <inheritdoc />
    public Task<List<ComparisonResultModel>> ComputeTopMatchesAsync(
        ImageRecord referenceImage,
        IEnumerable<ImageRecord> candidates,
        int maxResults,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_cache.TryGetValue(referenceImage, out HashSet<BlockKey>? referenceBlockKeys) || referenceBlockKeys.Count == 0)
                return new List<ComparisonResultModel>();

            int referenceBlockCount = referenceBlockKeys.Count;
            IList<ImageRecord> candidateList = candidates as IList<ImageRecord> ?? candidates.ToList();
            List<ComparisonResultModel> results = new List<ComparisonResultModel>();

            for (int i = 0; i < candidateList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ImageRecord candidate = candidateList[i];

                // Skip comparing image to itself
                if (candidate.SetName == referenceImage.SetName && candidate.Id == referenceImage.Id)
                {
                    progress?.Report((double)(i + 1) / candidateList.Count);
                    continue;
                }

                if (!_cache.TryGetValue(candidate, out HashSet<BlockKey>? candidateBlockKeys))
                {
                    progress?.Report((double)(i + 1) / candidateList.Count);
                    continue;
                }

                int sharedCount = 0;
                foreach (BlockKey key in candidateBlockKeys)
                {
                    if (referenceBlockKeys.Contains(key))
                        sharedCount++;
                }

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

                progress?.Report((double)(i + 1) / candidateList.Count);
            }

            // Sort by match percentage descending, then alphabetically by name
            return results
                .OrderByDescending(r => r.MatchPercentage)
                .ThenBy(r => r.MatchedImage.Name, StringComparer.Ordinal)
                .Take(maxResults)
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
    /// Finds the IDataStoreDataAccess for a given image by looking up the session that contains it.
    /// </summary>
    private IDataStoreDataAccess? GetDataAccessForImage(ImageRecord image)
    {
        IReadOnlyList<ImageSessionModel> sessions = _dataStoreService.Sessions
            .FirstAsync()
            .GetAwaiter()
            .GetResult();

        foreach (ImageSessionModel? session in sessions)
        {
            if (session.Images.Any(img => img.SetName == image.SetName && img.Id == image.Id))
            {
                return session.DataStore.DataAccess;
            }
        }

        return null;
    }

    private void LogError(string message) => _errorNotification?.PublishOperationError(message);
}