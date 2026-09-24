using NkdsUi.Models;
using NkdsUi.ViewModels;

namespace NkdsUi.Services;

/// <summary>
/// Computes per-image statistics across all open sessions using background threads.
/// </summary>
public interface IStatsCalculationService
{
    /// <summary>
    /// Computes stats for images that don't already have stats populated.
    /// Groups images by set, calls GetSetStatistics per distinct set in parallel,
    /// and dispatches results to the UI thread progressively.
    /// </summary>
    Task ComputeStatsAsync(
        IReadOnlyList<ImageRowViewModel> images,
        IReadOnlyList<ImageSessionModel> sessions,
        int maxDegreeOfParallelism,
        Action<int, int> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Computes the deduplicated stored size for a set of filtered images.
    /// For each set, collects all unique block keys from the specified images,
    /// deduplicates them, and sums their physical sizes from the block table.
    /// </summary>
    /// <param name="images">The filtered images to compute stored size for.</param>
    /// <param name="sessions">Open sessions providing DataStore access.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Total deduplicated stored size in bytes.</returns>
    Task<long> ComputeDeduplicatedStoredSizeAsync(
        IReadOnlyList<ImageRowViewModel> images,
        IReadOnlyList<ImageSessionModel> sessions,
        CancellationToken cancellationToken);
}