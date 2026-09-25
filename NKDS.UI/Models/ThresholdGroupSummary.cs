namespace NkdsUi.Models;

/// <summary>
/// Aggregate statistics for a threshold group.
/// </summary>
public class ThresholdGroupSummary
{
    /// <summary>
    /// Minimum match percentage across all pairs in the group.
    /// </summary>
    public double MinMatchPercentage { get; init; }

    /// <summary>
    /// Maximum match percentage across all pairs in the group.
    /// </summary>
    public double MaxMatchPercentage { get; init; }

    /// <summary>
    /// Average match percentage across all pairs in the group.
    /// </summary>
    public double AvgMatchPercentage { get; init; }
}