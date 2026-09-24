using NKitDataStore;

namespace NkdsUi.Models;

/// <summary>
/// Represents a cluster of images that share BlockKeys (a connected component
/// in the shared-BlockKey graph).
/// </summary>
public class SimilarityGroupModel
{
    /// <summary>
    /// Images in this group, sorted alphabetically by Name.
    /// </summary>
    public List<ImageRecord> Images { get; init; } = new();

    /// <summary>
    /// Count of BlockKeys that appear in at least two images within this group.
    /// </summary>
    public int SharedBlockKeyCount { get; init; }

    /// <summary>
    /// Pairwise match percentages between all images in the group.
    /// Pairs are ordered so that ImageA &lt; ImageB alphabetically.
    /// </summary>
    public List<PairwiseMatch> PairwiseMatches { get; init; } = new();
}

/// <summary>
/// A single pairwise comparison result within a similarity group.
/// </summary>
public class PairwiseMatch
{
    /// <summary>
    /// The first image name (alphabetically earlier).
    /// </summary>
    public string ImageA { get; init; } = string.Empty;

    /// <summary>
    /// The second image name (alphabetically later).
    /// </summary>
    public string ImageB { get; init; } = string.Empty;

    /// <summary>
    /// The match percentage between the two images (0.00 to 100.00).
    /// </summary>
    public double MatchPercentage { get; init; }
}