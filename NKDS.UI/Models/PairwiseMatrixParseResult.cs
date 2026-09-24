namespace NkdsUi.Models;

/// <summary>
/// Result of parsing a pairwise matrix YAML file.
/// </summary>
public class PairwiseMatrixParseResult
{
    /// <summary>
    /// The parsed metadata header from the YAML file.
    /// </summary>
    public PairwiseMatrixMetadata Metadata { get; init; } = new();

    /// <summary>
    /// The parsed pair results (ImageA, ImageB, MatchPercent).
    /// </summary>
    public List<(string ImageA, string ImageB, double MatchPercent)> Pairs { get; init; } = new();
}