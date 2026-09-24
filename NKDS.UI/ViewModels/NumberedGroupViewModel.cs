using NkdsUi.Models;

namespace NkdsUi.ViewModels;

/// <summary>
/// Wraps a ThresholdGroupModel with its display number and sorted pairwise matches.
/// Used by the ThresholdGroupsDialog to show group numbering and descending match order.
/// </summary>
public class NumberedGroupViewModel
{
    /// <summary>
    /// The 1-based group number for display.
    /// </summary>
    public int GroupNumber { get; set; }

    /// <summary>
    /// The underlying threshold group model.
    /// </summary>
    public ThresholdGroupModel Group { get; init; } = new();

    /// <summary>
    /// Pairwise matches sorted by MatchPercentage descending for display.
    /// </summary>
    public IReadOnlyList<PairwiseMatch> SortedPairwiseMatches { get; init; }
        = Array.Empty<PairwiseMatch>();

    /// <summary>
    /// The name of the image from the highest-match pair in this group.
    /// Used as the group's display name (the "most connected" image).
    /// </summary>
    public string GroupName { get; init; } = "";

    /// <summary>
    /// Images in this group with their best match percentage, sorted by match % descending.
    /// </summary>
    public IReadOnlyList<GroupedImageItem> ImageItems { get; init; } = Array.Empty<GroupedImageItem>();
}

/// <summary>
/// An image within a group, showing its best match percentage against other group members.
/// </summary>
public class GroupedImageItem
{
    public string Name { get; init; } = "";
    public double BestMatchPercent { get; init; }
}