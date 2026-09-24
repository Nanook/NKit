namespace NkdsUi.Models;

/// <summary>
/// Result of a threshold grouping computation.
/// </summary>
public class ThresholdGroupingResult
{
    /// <summary>
    /// The computed threshold groups, sorted by image count desc, then avg match desc.
    /// </summary>
    public List<ThresholdGroupModel> Groups { get; init; } = new();

    /// <summary>
    /// The threshold value used for this computation.
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Total number of images that were grouped (excludes singletons).
    /// </summary>
    public int TotalGroupedImages { get; init; }

    /// <summary>
    /// Total number of unique pairs computed.
    /// </summary>
    public int TotalPairsComputed { get; init; }

    /// <summary>
    /// The raw pairwise results for all computed pairs.
    /// Stored so the dialog can re-filter at different thresholds without recomputing.
    /// </summary>
    public List<(string ImageA, string ImageB, double MatchPercent)> AllPairResults { get; init; } = new();

    /// <summary>
    /// The scope restriction used for this computation (for export metadata).
    /// </summary>
    public ScopeRestriction ScopeRestriction { get; init; }

    /// <summary>
    /// The image names that participated in this computation (for export metadata).
    /// </summary>
    public List<string> ImageNames { get; init; } = new();
}