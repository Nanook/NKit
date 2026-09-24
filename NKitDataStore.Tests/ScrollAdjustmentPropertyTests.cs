using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Views.Controls;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ScrollPositionAdjuster static computation methods.
///
/// Feature: stable-image-list, Property 8: Scroll Adjustment on Insertion
/// Feature: stable-image-list, Property 9: Scroll Adjustment on Removal
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public class ScrollAdjustmentPropertyTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Property 8: Scroll Adjustment on Insertion
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 2.1, 2.2**
    ///
    /// Property 8: ComputeScrollAfterInsertion SHALL return currentIndex + count
    /// when insertionIndex &lt;= currentIndex, and currentIndex otherwise.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insertion_AtOrAboveViewport_ShiftsDownByCount(
        NonNegativeInt currentIndexRaw, NonNegativeInt insertionIndexRaw, PositiveInt countRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int insertionIndex = insertionIndexRaw.Get % (currentIndex + 1); // ensure insertionIndex <= currentIndex
        int count = countRaw.Get;

        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentIndex, insertionIndex, count);

        return result == currentIndex + count;
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.2**
    ///
    /// Property 8: ComputeScrollAfterInsertion SHALL return currentIndex unchanged
    /// when insertionIndex > currentIndex.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insertion_BelowViewport_LeavesScrollUnchanged(
        NonNegativeInt currentIndexRaw, PositiveInt offsetRaw, PositiveInt countRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int insertionIndex = currentIndex + offsetRaw.Get; // ensure insertionIndex > currentIndex
        int count = countRaw.Get;

        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentIndex, insertionIndex, count);

        return result == currentIndex;
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.2**
    ///
    /// Property 8: ComputeScrollAfterInsertion with insertionIndex exactly equal to
    /// currentIndex SHALL shift down (insertion at viewport boundary counts as "above").
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insertion_AtExactViewportPosition_ShiftsDown(
        NonNegativeInt currentIndexRaw, PositiveInt countRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int insertionIndex = currentIndex; // exactly at viewport
        int count = countRaw.Get;

        int result = ScrollPositionAdjuster.ComputeScrollAfterInsertion(
            currentIndex, insertionIndex, count);

        return result == currentIndex + count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 9: Scroll Adjustment on Removal
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 2.3, 2.4**
    ///
    /// Property 9: ComputeScrollAfterRemoval SHALL return
    /// max(0, min(currentIndex - removedAbove, newTotal - 1))
    /// where removedAbove = count of removed items with index &lt; currentIndex.
    /// Tests removal entirely above the viewport.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Removal_EntirelyAboveViewport_ShiftsUpByCount(
        PositiveInt currentIndexRaw, PositiveInt countRaw, PositiveInt extraRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int removedCount = Math.Min(countRaw.Get, currentIndex); // ensure all removed are above
        int removalIndex = currentIndex - removedCount; // removal range ends exactly at currentIndex
        // newTotalCount must be valid: original total >= currentIndex + 1 + some extra
        int originalTotal = currentIndex + 1 + extraRaw.Get;
        int newTotalCount = originalTotal - removedCount;

        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentIndex, removalIndex, removedCount, newTotalCount);

        // All removed items are above viewport, so removedAbove = removedCount
        int expected = Math.Max(0, Math.Min(currentIndex - removedCount, newTotalCount - 1));

        return result == expected;
    }

    /// <summary>
    /// **Validates: Requirements 2.3, 2.4**
    ///
    /// Property 9: ComputeScrollAfterRemoval SHALL return currentIndex unchanged
    /// (clamped to valid range) when removal is entirely below the viewport.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Removal_EntirelyBelowViewport_LeavesScrollUnchanged(
        NonNegativeInt currentIndexRaw, PositiveInt offsetRaw, PositiveInt countRaw, PositiveInt extraRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int removalIndex = currentIndex + offsetRaw.Get; // removal starts below viewport
        int removedCount = countRaw.Get;
        // Original total must accommodate the removal range
        int originalTotal = removalIndex + removedCount + extraRaw.Get;
        int newTotalCount = originalTotal - removedCount;

        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentIndex, removalIndex, removedCount, newTotalCount);

        // No items removed above viewport, so removedAbove = 0
        int expected = Math.Max(0, Math.Min(currentIndex, newTotalCount - 1));

        return result == expected;
    }

    /// <summary>
    /// **Validates: Requirements 2.3, 2.4**
    ///
    /// Property 9: ComputeScrollAfterRemoval with empty collection (newTotalCount = 0)
    /// SHALL always return 0.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Removal_ResultingInEmptyCollection_ReturnsZero(
        NonNegativeInt currentIndexRaw, NonNegativeInt removalIndexRaw, PositiveInt countRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int removalIndex = removalIndexRaw.Get;
        int removedCount = countRaw.Get;
        int newTotalCount = 0;

        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentIndex, removalIndex, removedCount, newTotalCount);

        return result == 0;
    }

    /// <summary>
    /// **Validates: Requirements 2.3, 2.4**
    ///
    /// Property 9: ComputeScrollAfterRemoval with removal spanning across the viewport
    /// (some items above, some at/below) SHALL correctly compute removedAbove as
    /// the count of removed items with index &lt; currentIndex.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Removal_SpanningViewport_CorrectlyComputesRemovedAbove(
        PositiveInt currentIndexRaw, PositiveInt belowCountRaw, PositiveInt extraRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        // Removal starts somewhere above viewport and extends past it
        int removalIndex = currentIndex / 2; // starts in the middle below viewport start
        int removedCount = currentIndex - removalIndex + belowCountRaw.Get; // extends past currentIndex
        int originalTotal = currentIndex + belowCountRaw.Get + extraRaw.Get + 1;
        int newTotalCount = originalTotal - removedCount;

        if (newTotalCount <= 0)
            return true; // skip degenerate cases handled by empty-collection test

        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentIndex, removalIndex, removedCount, newTotalCount);

        // removedAbove = min(removalEnd, currentIndex) - removalIndex
        int removalEnd = removalIndex + removedCount;
        int removedAbove = Math.Max(0, Math.Min(removalEnd, currentIndex) - removalIndex);
        int expected = Math.Max(0, Math.Min(currentIndex - removedAbove, newTotalCount - 1));

        return result == expected;
    }

    /// <summary>
    /// **Validates: Requirements 2.3, 2.4**
    ///
    /// Property 9: ComputeScrollAfterRemoval result is always clamped to [0, newTotal - 1].
    /// For any valid inputs, the result SHALL never be negative and never exceed newTotal - 1.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Removal_ResultAlwaysInValidRange(
        NonNegativeInt currentIndexRaw, NonNegativeInt removalIndexRaw,
        PositiveInt countRaw, PositiveInt newTotalRaw)
    {
        int currentIndex = currentIndexRaw.Get;
        int removalIndex = removalIndexRaw.Get;
        int removedCount = countRaw.Get;
        int newTotalCount = newTotalRaw.Get;

        int result = ScrollPositionAdjuster.ComputeScrollAfterRemoval(
            currentIndex, removalIndex, removedCount, newTotalCount);

        return result >= 0 && result <= newTotalCount - 1;
    }
}