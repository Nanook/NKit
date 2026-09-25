using NkdsUi.Models;
using NkdsUi.Services;

namespace NKitDataStore.Tests;

public class PairwiseMatrixYamlWriterTests
{
    private static string WriteToString(PairwiseMatrixMetadata metadata,
        IReadOnlyList<(string ImageA, string ImageB, double MatchPercent)> pairs)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            PairwiseMatrixYamlWriter.WritePairwiseMatrix(writer, metadata, pairs);
        }
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return reader.ReadToEnd();
    }

    [Fact]
    public void WritePairwiseMatrix_WritesCorrectFormat()
    {
        // Arrange
        var metadata = new PairwiseMatrixMetadata
        {
            Scope = "None",
            ImageCount = 5,
            ComputedAt = "2024-01-15T14:30:00Z",
            Images = new List<string>
            {
                "Game A (Europe)",
                "Game A (Japan)",
                "Game A (USA)",
                "Game B (Europe)",
                "Game B (Japan)"
            }
        };

        var pairs = new List<(string ImageA, string ImageB, double MatchPercent)>
        {
            ("Game A (Europe)", "Game A (Japan)", 81.23),
            ("Game A (Europe)", "Game A (USA)", 88.50),
            ("Game A (Japan)", "Game A (USA)", 83.40),
            ("Game B (Europe)", "Game B (Japan)", 92.10)
        };

        // Act
        var output = WriteToString(metadata, pairs);

        // Assert
        Assert.Contains("scope: None", output);
        Assert.Contains("imageCount: 5", output);
        Assert.Contains("computedAt: \"2024-01-15T14:30:00Z\"", output);
        Assert.Contains("images:", output);
        Assert.Contains("  - \"Game A (Europe)\"", output);
        Assert.Contains("  - \"Game A (Japan)\"", output);
        Assert.Contains("  - \"Game A (USA)\"", output);
        Assert.Contains("  - \"Game B (Europe)\"", output);
        Assert.Contains("  - \"Game B (Japan)\"", output);
        Assert.Contains("pairs:", output);
        Assert.Contains("  - a: \"Game A (Europe)\", b: \"Game A (Japan)\", pct: 81.23", output);
        Assert.Contains("  - a: \"Game A (Europe)\", b: \"Game A (USA)\", pct: 88.50", output);
        Assert.Contains("  - a: \"Game A (Japan)\", b: \"Game A (USA)\", pct: 83.40", output);
        Assert.Contains("  - a: \"Game B (Europe)\", b: \"Game B (Japan)\", pct: 92.10", output);
    }

    [Fact]
    public void WritePairwiseMatrix_FormatsMatchPercentageWith2DecimalPlaces()
    {
        // Arrange
        var metadata = new PairwiseMatrixMetadata
        {
            Scope = "SameSet",
            ImageCount = 2,
            ComputedAt = "2024-06-01T10:00:00Z",
            Images = new List<string> { "Image A", "Image B" }
        };

        var pairs = new List<(string ImageA, string ImageB, double MatchPercent)>
        {
            ("Image A", "Image B", 87.4)
        };

        // Act
        var output = WriteToString(metadata, pairs);

        // Assert - should be formatted to 2 decimal places
        Assert.Contains("pct: 87.40", output);
    }

    [Fact]
    public void WritePairwiseMatrix_WritesImageNamesUnmodified()
    {
        // Arrange - image names with special characters
        var metadata = new PairwiseMatrixMetadata
        {
            Scope = "Both",
            ImageCount = 2,
            ComputedAt = "2024-03-20T08:15:30Z",
            Images = new List<string>
            {
                "Game (Special Edition) [Rev 1]",
                "Game: The Sequel - Part 2"
            }
        };

        var pairs = new List<(string ImageA, string ImageB, double MatchPercent)>
        {
            ("Game (Special Edition) [Rev 1]", "Game: The Sequel - Part 2", 55.00)
        };

        // Act
        var output = WriteToString(metadata, pairs);

        // Assert - names should appear exactly as provided
        Assert.Contains("  - \"Game (Special Edition) [Rev 1]\"", output);
        Assert.Contains("  - \"Game: The Sequel - Part 2\"", output);
        Assert.Contains("  - a: \"Game (Special Edition) [Rev 1]\", b: \"Game: The Sequel - Part 2\", pct: 55.00", output);
    }

    [Fact]
    public void WritePairwiseMatrix_EmptyPairs_WritesMetadataAndEmptyPairsSection()
    {
        // Arrange
        var metadata = new PairwiseMatrixMetadata
        {
            Scope = "None",
            ImageCount = 1,
            ComputedAt = "2024-01-01T00:00:00Z",
            Images = new List<string> { "Single Image" }
        };

        var pairs = new List<(string ImageA, string ImageB, double MatchPercent)>();

        // Act
        var output = WriteToString(metadata, pairs);

        // Assert
        Assert.Contains("scope: None", output);
        Assert.Contains("imageCount: 1", output);
        Assert.Contains("images:", output);
        Assert.Contains("  - \"Single Image\"", output);
        Assert.Contains("pairs:", output);
        // No pair entries after "pairs:"
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var pairsLineIndex = Array.FindIndex(lines, l => l.Trim() == "pairs:");
        Assert.Equal(lines.Length - 1, pairsLineIndex); // pairs: is the last line
    }

    [Fact]
    public void WritePairwiseMatrix_ComputedAtIsQuoted()
    {
        // Arrange
        var metadata = new PairwiseMatrixMetadata
        {
            Scope = "None",
            ImageCount = 0,
            ComputedAt = "2024-12-31T23:59:59Z",
            Images = new List<string>()
        };

        var pairs = new List<(string ImageA, string ImageB, double MatchPercent)>();

        // Act
        var output = WriteToString(metadata, pairs);

        // Assert - computedAt value should be in quotes
        Assert.Contains("computedAt: \"2024-12-31T23:59:59Z\"", output);
    }
}
