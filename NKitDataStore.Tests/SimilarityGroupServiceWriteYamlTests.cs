using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;

namespace NKitDataStore.Tests;

public class SimilarityGroupServiceWriteYamlTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SimilarityGroupService _service;

    public SimilarityGroupServiceWriteYamlTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SimilarityYamlTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new SimilarityGroupService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteYamlAsync_WritesCorrectFormat_ForSingleGroup()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Game A (Europe)" },
                    new() { Name = "Game A (USA)" }
                },
                SharedBlockKeyCount = 4521,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Game A (Europe)", ImageB = "Game A (USA)", MatchPercentage = 95.23 }
                }
            }
        };

        var filePath = Path.Combine(_tempDir, "output.yaml");

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("similarity_groups:", content);
        Assert.Contains("  - images:", content);
        Assert.Contains("      - \"Game A (Europe)\"", content);
        Assert.Contains("      - \"Game A (USA)\"", content);
        Assert.Contains("    shared_block_count: 4521", content);
        Assert.Contains("    pairwise_matches:", content);
        Assert.Contains("      - image_a: \"Game A (Europe)\"", content);
        Assert.Contains("        image_b: \"Game A (USA)\"", content);
        Assert.Contains("        match_percentage: 95.23", content);
    }

    [Fact]
    public async Task WriteYamlAsync_FormatsMatchPercentageTo2DecimalPlaces()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Image A" },
                    new() { Name = "Image B" }
                },
                SharedBlockKeyCount = 100,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Image A", ImageB = "Image B", MatchPercentage = 87.4 }
                }
            }
        };

        var filePath = Path.Combine(_tempDir, "format_test.yaml");

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("match_percentage: 87.40", content);
    }

    [Fact]
    public async Task WriteYamlAsync_WritesMultipleGroups()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Game A (Europe)" },
                    new() { Name = "Game A (Japan)" },
                    new() { Name = "Game A (USA)" }
                },
                SharedBlockKeyCount = 4521,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Game A (Europe)", ImageB = "Game A (USA)", MatchPercentage = 95.23 },
                    new() { ImageA = "Game A (Japan)", ImageB = "Game A (USA)", MatchPercentage = 87.41 },
                    new() { ImageA = "Game A (Europe)", ImageB = "Game A (Japan)", MatchPercentage = 82.10 }
                }
            },
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Game B (Europe)" },
                    new() { Name = "Game B (USA)" }
                },
                SharedBlockKeyCount = 3200,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Game B (Europe)", ImageB = "Game B (USA)", MatchPercentage = 91.55 }
                }
            }
        };

        var filePath = Path.Combine(_tempDir, "multi_group.yaml");

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("    shared_block_count: 4521", content);
        Assert.Contains("    shared_block_count: 3200", content);
        Assert.Contains("        match_percentage: 95.23", content);
        Assert.Contains("        match_percentage: 91.55", content);
    }

    [Fact]
    public async Task WriteYamlAsync_DoesNotLeavePartialFileOnCancellation()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Image A" },
                    new() { Name = "Image B" }
                },
                SharedBlockKeyCount = 100,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Image A", ImageB = "Image B", MatchPercentage = 50.00 }
                }
            }
        };

        var filePath = Path.Combine(_tempDir, "cancelled.yaml");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.WriteYamlAsync(groups, filePath, cts.Token));

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task WriteYamlAsync_ThrowsDescriptiveMessageOnPermissionError()
    {
        // Arrange - use a path that should fail (directory as file path)
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord> { new() { Name = "Image A" } },
                SharedBlockKeyCount = 0,
                PairwiseMatches = new List<PairwiseMatch>()
            }
        };

        // Use a non-existent directory to trigger IOException
        var filePath = Path.Combine(_tempDir, "nonexistent_dir", "subdir", "output.yaml");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<IOException>(
            () => _service.WriteYamlAsync(groups, filePath, CancellationToken.None));

        Assert.Contains("Cannot write similarity groups", ex.Message);
        Assert.Contains(filePath, ex.Message);
    }

    [Fact]
    public async Task WriteYamlAsync_EscapesQuotesInImageNames()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord>
                {
                    new() { Name = "Game \"Special\" Edition" },
                    new() { Name = "Normal Game" }
                },
                SharedBlockKeyCount = 50,
                PairwiseMatches = new List<PairwiseMatch>
                {
                    new() { ImageA = "Game \"Special\" Edition", ImageB = "Normal Game", MatchPercentage = 75.00 }
                }
            }
        };

        var filePath = Path.Combine(_tempDir, "escape_test.yaml");

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\\\"Special\\\"", content);
    }

    [Fact]
    public async Task WriteYamlAsync_EmptyGroupsList_WritesHeaderOnly()
    {
        // Arrange
        var groups = new List<SimilarityGroupModel>();
        var filePath = Path.Combine(_tempDir, "empty.yaml");

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Equal("similarity_groups:" + Environment.NewLine, content);
    }

    [Fact]
    public async Task WriteYamlAsync_OverwritesExistingFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "overwrite.yaml");
        await File.WriteAllTextAsync(filePath, "old content");

        var groups = new List<SimilarityGroupModel>
        {
            new()
            {
                Images = new List<ImageRecord> { new() { Name = "New Image" } },
                SharedBlockKeyCount = 10,
                PairwiseMatches = new List<PairwiseMatch>()
            }
        };

        // Act
        await _service.WriteYamlAsync(groups, filePath, CancellationToken.None);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        Assert.DoesNotContain("old content", content);
        Assert.Contains("New Image", content);
    }
}
