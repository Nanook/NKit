using NKitDataStore;

namespace NKDS.Models;

/// <summary>
/// Result of a statistics calculation operation, containing per-set statistics and any errors.
/// </summary>
public sealed class StatsResult
{
    /// <summary>
    /// Whether the statistics calculation completed successfully for all requested sets.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Statistics for each set that was successfully processed.
    /// </summary>
    public IReadOnlyList<DataStoreStatistics> SetStatistics { get; init; } = [];

    /// <summary>
    /// Total duration of the statistics calculation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Errors encountered during the statistics calculation (one per failed set).
    /// </summary>
    public IReadOnlyList<OperationErrorEntry> Errors { get; init; } = [];
}