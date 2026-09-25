using NkdsUi.Models;
using NKitDataStore;
using System.Globalization;

namespace NkdsUi.Services;

/// <summary>
/// Writes and parses YAML files for grouped results and pairwise matrices.
/// Uses StreamWriter/StreamReader directly (no reflection-based serializers) for AOT compatibility.
/// </summary>
public static class PairwiseMatrixYamlWriter
{
    /// <summary>
    /// Writes grouped results in compact YAML format.
    /// </summary>
    /// <param name="writer">The StreamWriter to write to.</param>
    /// <param name="groups">The threshold groups to export.</param>
    /// <param name="threshold">The threshold percentage used for grouping.</param>
    public static void WriteGroupedResults(
        StreamWriter writer,
        IReadOnlyList<ThresholdGroupModel> groups,
        double threshold)
    {
        int totalImages = groups.Sum(g => g.Images.Count);

        // Write comment header
        writer.WriteLine("# Threshold Groups Export");
        writer.WriteLine($"# Threshold: {threshold:F2}%");
        writer.WriteLine($"# Groups: {groups.Count}");
        writer.WriteLine($"# Images: {totalImages}");

        foreach (ThresholdGroupModel group in groups)
        {
            writer.WriteLine();

            // Use the first image name alphabetically as the group name
            List<ImageRecord> sortedImages = group.Images.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
            string groupName = sortedImages[0].Name;

            writer.WriteLine($"- name: \"{groupName}\"");
            writer.WriteLine("  matches:");

            // Find all pairwise matches involving the named image
            foreach (PairwiseMatch match in group.PairwiseMatches)
            {
                string? matchedName = null;
                double matchPercent = match.MatchPercentage;

                if (string.Equals(match.ImageA, groupName, StringComparison.Ordinal))
                {
                    matchedName = match.ImageB;
                }
                else if (string.Equals(match.ImageB, groupName, StringComparison.Ordinal))
                {
                    matchedName = match.ImageA;
                }

                if (matchedName != null)
                {
                    int roundedPercent = (int)Math.Round(matchPercent);
                    writer.WriteLine($"    - \"{matchedName} [{roundedPercent}%]\"");
                }
            }
        }
    }

    /// <summary>
    /// Writes the full pairwise comparison matrix with metadata header.
    /// </summary>
    /// <param name="writer">The StreamWriter to write to.</param>
    /// <param name="metadata">Metadata header containing scope, image count, computation date, and image names.</param>
    /// <param name="pairs">The list of pairwise comparison results to export.</param>
    public static void WritePairwiseMatrix(
        StreamWriter writer,
        PairwiseMatrixMetadata metadata,
        IReadOnlyList<(string ImageA, string ImageB, double MatchPercent)> pairs)
    {
        // Write metadata header
        writer.WriteLine($"scope: {metadata.Scope}");
        writer.WriteLine($"imageCount: {metadata.ImageCount}");
        writer.WriteLine($"computedAt: \"{metadata.ComputedAt}\"");

        // Write images list
        writer.WriteLine("images:");
        foreach (string image in metadata.Images)
        {
            writer.WriteLine($"  - \"{image}\"");
        }

        // Write pairs section
        writer.WriteLine("pairs:");
        foreach ((string? imageA, string? imageB, double matchPercent) in pairs)
        {
            writer.WriteLine($"  - a: \"{imageA}\", b: \"{imageB}\", pct: {matchPercent:F2}");
        }
    }

    /// <summary>
    /// Parses a pairwise matrix YAML file, returning metadata and pair results.
    /// Uses simple line-by-line StreamReader with string splitting (no YamlDotNet).
    /// </summary>
    /// <param name="reader">The StreamReader to read from.</param>
    /// <returns>A <see cref="PairwiseMatrixParseResult"/> containing parsed metadata and pairs.</returns>
    public static PairwiseMatrixParseResult ParsePairwiseMatrix(StreamReader reader)
    {
        string scope = "None";
        int imageCount = 0;
        string computedAt = string.Empty;
        List<string> images = new List<string>();
        List<(string ImageA, string ImageB, double MatchPercent)> pairs = new List<(string ImageA, string ImageB, double MatchPercent)>();

        bool inImages = false;
        bool inPairs = false;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            if (inPairs)
            {
                // Parse pair entries: "  - a: "X", b: "Y", pct: Z"
                if (line.StartsWith("  - a: \""))
                {
                    (string ImageA, string ImageB, double MatchPercent)? pair = ParsePairLine(line);
                    if (pair.HasValue)
                        pairs.Add(pair.Value);
                }
                continue;
            }

            if (inImages)
            {
                // Image list entries start with "  - "
                if (line.StartsWith("  - "))
                {
                    string imageName = StripListEntryQuotes(line);
                    images.Add(imageName);
                    continue;
                }
                else
                {
                    // Non-indented line ends the images section
                    inImages = false;
                    // Fall through to check if this line is "pairs:" or another key
                }
            }

            // Check for section keys
            if (line.StartsWith("pairs:"))
            {
                inPairs = true;
                inImages = false;
            }
            else if (line.StartsWith("images:"))
            {
                inImages = true;
            }
            else if (line.StartsWith("scope: "))
            {
                scope = line.Substring("scope: ".Length).Trim();
            }
            else if (line.StartsWith("imageCount: "))
            {
                string value = line.Substring("imageCount: ".Length).Trim();
                int.TryParse(value, CultureInfo.InvariantCulture, out imageCount);
            }
            else if (line.StartsWith("computedAt: "))
            {
                computedAt = StripQuotes(line.Substring("computedAt: ".Length).Trim());
            }
        }

        PairwiseMatrixMetadata metadata = new PairwiseMatrixMetadata
        {
            Scope = scope,
            ImageCount = imageCount,
            ComputedAt = computedAt,
            Images = images
        };

        return new PairwiseMatrixParseResult
        {
            Metadata = metadata,
            Pairs = pairs
        };
    }

    /// <summary>
    /// Strips the "  - " prefix and surrounding quotes from a YAML list entry.
    /// </summary>
    private static string StripListEntryQuotes(string line)
    {
        // Remove "  - " prefix
        string value = line.Substring(4); // "  - " is 4 chars
        return StripQuotes(value);
    }

    /// <summary>
    /// Strips surrounding double quotes from a string value.
    /// </summary>
    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    /// <summary>
    /// Parses a pair line in the format: '  - a: "X", b: "Y", pct: Z'
    /// </summary>
    private static (string ImageA, string ImageB, double MatchPercent)? ParsePairLine(string line)
    {
        // Format: '  - a: "X", b: "Y", pct: Z'
        // Find first quoted value after 'a: "'
        const string aPrefix = "  - a: \"";
        const string bMarker = "\", b: \"";
        const string pctMarker = "\", pct: ";

        if (!line.StartsWith(aPrefix))
            return null;

        int aStart = aPrefix.Length;
        int bMarkerIndex = line.IndexOf(bMarker, aStart, StringComparison.Ordinal);
        if (bMarkerIndex < 0)
            return null;

        string imageA = line[aStart..bMarkerIndex];

        int bStart = bMarkerIndex + bMarker.Length;
        int pctMarkerIndex = line.IndexOf(pctMarker, bStart, StringComparison.Ordinal);
        if (pctMarkerIndex < 0)
            return null;

        string imageB = line[bStart..pctMarkerIndex];

        int pctStart = pctMarkerIndex + pctMarker.Length;
        string pctStr = line[pctStart..];

        if (!double.TryParse(pctStr, CultureInfo.InvariantCulture, out double matchPercent))
            return null;

        return (imageA, imageB, matchPercent);
    }
}