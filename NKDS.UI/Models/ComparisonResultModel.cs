using NKitDataStore;

namespace NkdsUi.Models;

/// <summary>
/// A single comparison result entry showing how similar a candidate image is to the reference.
/// </summary>
public class ComparisonResultModel
{
    /// <summary>
    /// The candidate image that was compared against the reference.
    /// </summary>
    public ImageRecord MatchedImage { get; init; } = null!;

    /// <summary>
    /// The similarity percentage (0.00 to 100.00) based on shared BlockKeys.
    /// </summary>
    public double MatchPercentage { get; init; }

    /// <summary>
    /// The number of BlockKeys shared between the reference and this candidate.
    /// </summary>
    public int SharedBlockCount { get; init; }

    /// <summary>
    /// The total number of BlockKeys in the reference image.
    /// </summary>
    public int ReferenceBlockCount { get; init; }
}