using NkdsUi.Views.Controls;

namespace NKitDataStore.Tests;

/// <summary>
/// Integration tests for scroll position preservation using the static
/// computation methods of <see cref="ScrollPositionAdjuster"/>.
/// These tests verify specific, concrete scenarios with known inputs and outputs.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public class ScrollPositionIntegrationTests
{
    // ── 2.1: Insert above viewport adjusts scroll ────────────────────────

    /// <summary>
    /// Validates Requirement 2.1: Insert at index 5 when viewport starts at index 10
    /// adjusts scroll to 11 (shifts down by 1).
    /// </summary>
    [Fact]
    public void InsertAboveViewport_AdjustsScrollDown()
    {
        // Arrange
        int currentFirstVisible = 10;
        int insertionIndex = 5;
        int insertedCount = 1;

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentFirstVisible, insertionIndex, insertedCount);

        // Assert
        Assert.Equal(11, result);
    }

    /// <summary>
    /// Validates Requirement 2.1: Insert at index 0 (top) when viewport starts at index 0
    /// adjusts scroll to 1 (shifts down by 1).
    /// </summary>
    [Fact]
    public void InsertAtTop_WhenViewportAtTop_AdjustsScrollDown()
    {
        // Arrange
        int currentFirstVisible = 0;
        int insertionIndex = 0;
        int insertedCount = 1;

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentFirstVisible, insertionIndex, insertedCount);

        // Assert
        Assert.Equal(1, result);
    }

    // ── 2.2: Insert below viewport leaves scroll unchanged ───────────────

    /// <summary>
    /// Validates Requirement 2.2: Insert at index 15 when viewport starts at index 10
    /// leaves scroll at 10 (no change).
    /// </summary>
    [Fact]
    public void InsertBelowViewport_LeavesScrollUnchanged()
    {
        // Arrange
        int currentFirstVisible = 10;
        int insertionIndex = 15;
        int insertedCount = 1;

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentFirstVisible, insertionIndex, insertedCount);

        // Assert
        Assert.Equal(10, result);
    }

    // ── 2.3: Removal above viewport adjusts scroll ───────────────────────

    /// <summary>
    /// Validates Requirement 2.3: Remove at index 3 when viewport starts at index 10
    /// adjusts scroll to 9 (shifts up by 1).
    /// </summary>
    [Fact]
    public void RemoveAboveViewport_AdjustsScrollUp()
    {
        // Arrange
        int currentFirstVisible = 10;
        int removalIndex = 3;
        int removedCount = 1;
        int newTotalCount = 99; // original was 100, removed 1

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentFirstVisible, removalIndex, removedCount, newTotalCount);

        // Assert
        Assert.Equal(9, result);
    }

    /// <summary>
    /// Validates Requirement 2.3: Remove 3 items starting at index 8 when viewport
    /// starts at index 10 adjusts scroll to 8 (2 items above viewport removed).
    /// The removal range [8, 9, 10] has 2 items below currentFirstVisible (indices 8, 9).
    /// </summary>
    [Fact]
    public void RemoveMultipleSpanningViewport_AdjustsScrollByRemovedAboveCount()
    {
        // Arrange
        int currentFirstVisible = 10;
        int removalIndex = 8;
        int removedCount = 3; // removes indices 8, 9, 10
        int newTotalCount = 97; // original was 100, removed 3

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentFirstVisible, removalIndex, removedCount, newTotalCount);

        // Assert — only 2 items (indices 8, 9) are above viewport (< 10)
        Assert.Equal(8, result);
    }

    // ── 2.4: Removal below viewport leaves scroll unchanged ──────────────

    /// <summary>
    /// Validates Requirement 2.4: Remove at index 15 when viewport starts at index 10
    /// leaves scroll at 10 (no change).
    /// </summary>
    [Fact]
    public void RemoveBelowViewport_LeavesScrollUnchanged()
    {
        // Arrange
        int currentFirstVisible = 10;
        int removalIndex = 15;
        int removedCount = 1;
        int newTotalCount = 99; // original was 100, removed 1

        // Act
        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentFirstVisible, removalIndex, removedCount, newTotalCount);

        // Assert
        Assert.Equal(10, result);
    }
}