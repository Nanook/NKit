using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Computes threshold-based similarity groups from the Hash_Cache.
/// Uses symmetric match percentage: |A ∩ B| / max(|A|, |B|) * 100.
/// </summary>
public interface IThresholdGroupingService
{
    /// <summary>
    /// Computes threshold groups from the Hash_Cache.
    /// 1. Computes Match_Percentage for every unique pair of images.
    /// 2. Constructs an undirected graph with edges where Match_Percentage >= threshold.
    /// 3. Extracts connected components as groups (excluding singletons).
    /// 4. Computes summary statistics (min/max/avg) for each group.
    /// </summary>
    /// <param name="cache">The in-memory Hash_Cache.</param>
    /// <param name="threshold">Minimum match percentage (0–100) for an edge.</param>
    /// <param name="scopeRestriction">Scope restriction for partitioning.</param>
    /// <param name="cancellationToken">Token to cancel the computation.</param>
    /// <param name="progress">Reports progress as fraction of pairs computed (0.0–1.0).</param>
    /// <param name="maxDegreeOfParallelism">Max threads for pairwise computation. Default = 1 (single-threaded).</param>
    /// <returns>Threshold groups sorted by image count desc, then avg match desc.</returns>
    Task<ThresholdGroupingResult> ComputeGroupsAsync(
        IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache,
        double threshold,
        ScopeRestriction scopeRestriction,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null,
        int maxDegreeOfParallelism = 1);
}