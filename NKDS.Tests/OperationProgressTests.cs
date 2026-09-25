using NKDS.Models;

namespace NKDS.Tests;

public class OperationProgressTests
{
    [Fact]
    public void Percentage_ClampsToZero_WhenNegative()
    {
        OperationProgress progress = new OperationProgress { Percentage = -5 };
        Assert.Equal(0, progress.Percentage);
    }

    [Fact]
    public void Percentage_ClampsTo100_WhenAbove()
    {
        OperationProgress progress = new OperationProgress { Percentage = 150 };
        Assert.Equal(100, progress.Percentage);
    }

    [Fact]
    public void Percentage_PreservesValidValue()
    {
        OperationProgress progress = new OperationProgress { Percentage = 50 };
        Assert.Equal(50, progress.Percentage);
    }

    [Fact]
    public void Percentage_AllowsBoundaryZero()
    {
        OperationProgress progress = new OperationProgress { Percentage = 0 };
        Assert.Equal(0, progress.Percentage);
    }

    [Fact]
    public void Percentage_AllowsBoundary100()
    {
        OperationProgress progress = new OperationProgress { Percentage = 100 };
        Assert.Equal(100, progress.Percentage);
    }

    [Fact]
    public void CurrentItem_TruncatesAt260Chars()
    {
        string longString = new string('x', 500);
        OperationProgress progress = new OperationProgress { CurrentItem = longString };
        Assert.Equal(260, progress.CurrentItem.Length);
    }

    [Fact]
    public void CurrentItem_PreservesShortString()
    {
        OperationProgress progress = new OperationProgress { CurrentItem = "hello.iso" };
        Assert.Equal("hello.iso", progress.CurrentItem);
    }

    [Fact]
    public void CurrentItem_HandlesNull()
    {
        OperationProgress progress = new OperationProgress { CurrentItem = null! };
        Assert.Equal("", progress.CurrentItem);
    }

    [Fact]
    public void CurrentItem_Exactly260_NotTruncated()
    {
        string exact = new string('a', 260);
        OperationProgress progress = new OperationProgress { CurrentItem = exact };
        Assert.Equal(260, progress.CurrentItem.Length);
        Assert.Equal(exact, progress.CurrentItem);
    }
}