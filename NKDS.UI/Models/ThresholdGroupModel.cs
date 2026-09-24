using NKitDataStore;

namespace NkdsUi.Models;

/// <summary>
/// A single threshold group: a connected component in the threshold-filtered graph.
/// </summary>
public class ThresholdGroupModel
{
    /// <summary>
    /// Images in this group, sorted alphabetically by Name.
    /// </summary>
    public List<ImageRecord> Images { get; init; } = new();

    /// <summary>
    /// All pairwise match percentages within this group.
    /// Pairs ordered so ImageA.Name &lt; ImageB.Name alphabetically.
    /// </summary>
    public List<PairwiseMatch> PairwiseMatches { get; init; } = new();

    /// <summary>
    /// Summary statistics for this group.
    /// </summary>
    public ThresholdGroupSummary Summary { get; init; } = new();
}