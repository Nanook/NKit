namespace NkdsUi.Models;

/// <summary>
/// Result of a Hash_Cache load operation, reporting success and failure counts.
/// </summary>
public class HashCacheLoadResult
{
    /// <summary>
    /// The number of images whose BlockKeys were successfully loaded into the cache.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// The number of images that failed to load (closed sessions, exceptions).
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Names of images that failed to load, for diagnostic/warning display.
    /// </summary>
    public List<string> FailedImageNames { get; init; } = new();
}