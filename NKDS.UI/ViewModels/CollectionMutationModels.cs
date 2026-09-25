namespace NkdsUi.ViewModels;

/// <summary>
/// Describes the outcome of a reconcile operation for scroll/selection adjustment.
/// </summary>
public record ReconcileResult
{
    /// <summary>Rows that were removed, with their former indices (descending order).</summary>
    public IReadOnlyList<(ImageRowViewModel Row, int Index)> Removed { get; init; } =
        [];

    /// <summary>Rows that were added, with their new indices.</summary>
    public IReadOnlyList<(ImageRowViewModel Row, int Index)> Added { get; init; } =
        [];

    /// <summary>Rows that were updated in-place (data changed but row preserved).</summary>
    public IReadOnlyList<ImageRowViewModel> Updated { get; init; } =
        [];
}

/// <summary>
/// Describes the outcome of a filter reapplication.
/// </summary>
public record FilterChangeResult
{
    /// <summary>Rows removed from view (no longer pass filter).</summary>
    public IReadOnlyList<(ImageRowViewModel Row, int Index)> Removed { get; init; } =
        [];

    /// <summary>Rows added to view (now pass filter), with their insertion indices.</summary>
    public IReadOnlyList<(ImageRowViewModel Row, int Index)> Added { get; init; } =
        [];
}

/// <summary>
/// Describes a collection mutation for scroll position adjustment.
/// </summary>
public record MutationEvent(MutationType Type, int Index, int Count);

/// <summary>
/// The type of mutation applied to the collection.
/// </summary>
public enum MutationType
{
    Insert,
    Remove
}