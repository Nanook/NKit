using NKDS.Validation;

namespace NKDS.Tests;

public class ValidationTests
{
    // --- Set Name Validation ---

    [Fact]
    public void ValidateSetName_ValidName_ReturnsValid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName("MySet");
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateSetName_Null_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName(null);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateSetName_Empty_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName("");
        Assert.False(isValid);
        Assert.Contains("empty", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSetName_TooLong_ReturnsInvalid()
    {
        string name = new string('a', 129);
        (bool isValid, string? error) = NkdsValidation.ValidateSetName(name);
        Assert.False(isValid);
        Assert.Contains("128", error!);
    }

    [Fact]
    public void ValidateSetName_MaxLength_ReturnsValid()
    {
        string name = new string('a', 128);
        (bool isValid, string _) = NkdsValidation.ValidateSetName(name);
        Assert.True(isValid);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    public void ValidateSetName_PathSeparator_ReturnsInvalid(string sep)
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName($"my{sep}set");
        Assert.False(isValid);
        Assert.Contains("path separator", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData(":")]
    [InlineData("\"")]
    [InlineData("|")]
    [InlineData("?")]
    [InlineData("*")]
    public void ValidateSetName_ReservedChar_ReturnsInvalid(string ch)
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName($"my{ch}set");
        Assert.False(isValid);
        Assert.Contains("reserved", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSetName_ControlChar_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName("my\x01set");
        Assert.False(isValid);
        Assert.Contains("control", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData(" both ")]
    public void ValidateSetName_LeadingTrailingWhitespace_ReturnsInvalid(string name)
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName(name);
        Assert.False(isValid);
        Assert.Contains("whitespace", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSetName_InternalSpaces_ReturnsValid()
    {
        (bool isValid, string _) = NkdsValidation.ValidateSetName("my set name");
        Assert.True(isValid);
    }

    // --- Shard Size Validation ---

    [Fact]
    public void ValidateShardSize_MinBoundary_ReturnsValid()
    {
        (bool isValid, string _) = NkdsValidation.ValidateShardSize(NkdsValidation.ShardSizeMin);
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateShardSize_MaxBoundary_ReturnsValid()
    {
        (bool isValid, string _) = NkdsValidation.ValidateShardSize(NkdsValidation.ShardSizeMax);
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateShardSize_BelowMin_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateShardSize(NkdsValidation.ShardSizeMin - 1);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateShardSize_AboveMax_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateShardSize(NkdsValidation.ShardSizeMax + 1);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // --- Block Size Validation ---

    [Theory]
    [InlineData(4096)]       // 4 KiB
    [InlineData(8192)]       // 8 KiB
    [InlineData(65536)]      // 64 KiB
    [InlineData(1048576)]    // 1 MiB
    public void ValidateBlockSize_ValidPowerOfTwo_ReturnsValid(int size)
    {
        (bool isValid, string _) = NkdsValidation.ValidateBlockSize(size);
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateBlockSize_NotPowerOfTwo_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateBlockSize(5000);
        Assert.False(isValid);
        Assert.Contains("power of 2", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBlockSize_BelowMin_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateBlockSize(2048); // 2 KiB, below 4 KiB min
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateBlockSize_AboveMax_ReturnsInvalid()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateBlockSize(2 * 1024 * 1024); // 2 MiB, above 1 MiB max
        Assert.False(isValid);
        Assert.NotNull(error);
    }
}