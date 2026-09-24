using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Manages the in-memory BlockKey cache for Comparison Mode.
/// Loads BlockKeys from the database into memory, manages the cache lifecycle,
/// and provides cache-only computation for fast comparison operations.
/// </summary>
public interface IHashCacheService
{
    /// <summary>
    /// The current Hash_Cache. Empty when not in Comparison Mode.
    /// </summary>
    IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> Cache { get; }

    /// <summary>
    /// Observable that emits whenever the cache changes (load complete, session removed, cleared).
    /// </summary>
    IObservable<IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>>> CacheChanged { get; }

    /// <summary>
    /// Whether the cache is currently populated (Comparison Mode is active).
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads BlockKeys for the specified images into the cache.
    /// Reports progress as (imagesLoaded, totalImages).
    /// Skips images whose sessions are closed or that throw exceptions during loading.
    /// </summary>
    /// <param name="images">The images to load BlockKeys for.</param>
    /// <param name="cancellationToken">Token to cancel the loading operation.</param>
    /// <param name="progress">Optional progress reporter as (loaded, total).</param>
    /// <returns>A result indicating how many images loaded successfully and how many failed.</returns>
    Task<HashCacheLoadResult> LoadAsync(
        IReadOnlyList<ImageRecord> images,
        CancellationToken cancellationToken,
        IProgress<(int loaded, int total)>? progress = null);

    /// <summary>
    /// Clears all cached data, releasing memory.
    /// </summary>
    void Clear();

    /// <summary>
    /// Removes all entries for images belonging to the specified session.
    /// </summary>
    /// <param name="sessionId">The session identifier whose images should be removed.</param>
    void RemoveSession(string sessionId);

    /// <summary>
    /// Gets the BlockKey set for a specific image, or null if not cached.
    /// </summary>
    /// <param name="image">The image to look up.</param>
    /// <returns>The set of BlockKeys for the image, or null if not in the cache.</returns>
    HashSet<BlockKey>? GetBlockKeys(ImageRecord image);

    /// <summary>
    /// Computes Match_Percentage between a reference image and candidates using only cached data.
    /// Returns up to maxResults results ordered by match percentage descending (alphabetical tiebreak),
    /// excluding 0% matches.
    /// </summary>
    /// <param name="referenceImage">The image to compare against all candidates.</param>
    /// <param name="candidates">The set of candidate images to compare with.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Token to cancel the computation.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <returns>Up to maxResults comparison results ordered by match percentage descending.</returns>
    Task<List<ComparisonResultModel>> ComputeTopMatchesAsync(
        ImageRecord referenceImage,
        IEnumerable<ImageRecord> candidates,
        int maxResults,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null);
}