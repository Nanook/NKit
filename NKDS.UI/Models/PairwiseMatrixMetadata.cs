namespace NkdsUi.Models;

/// <summary>
/// Metadata header for an exported pairwise comparison matrix.
/// </summary>
public class PairwiseMatrixMetadata
{
    /// <summary>
    /// The scope restriction used during computation ("None", "SameSet", "SameSystem", "Both").
    /// </summary>
    public string Scope { get; init; } = "None";

    /// <summary>
    /// Total number of images in the computation.
    /// </summary>
    public int ImageCount { get; init; }

    /// <summary>
    /// ISO 8601 date/time when the computation was performed.
    /// </summary>
    public string ComputedAt { get; init; } = string.Empty;

    /// <summary>
    /// Complete list of image names that participated in the computation.
    /// </summary>
    public List<string> Images { get; init; } = new();
}