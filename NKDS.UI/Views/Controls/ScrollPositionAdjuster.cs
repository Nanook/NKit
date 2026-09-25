using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace NkdsUi.Views.Controls;

/// <summary>
/// Tracks the DataGrid scroll position and adjusts it when items are
/// inserted or removed above the current viewport.
/// Static computation methods are pure functions for testability.
/// Instance methods interact with the DataGrid to capture and adjust scroll state.
/// </summary>
public class ScrollPositionAdjuster
{
    private readonly DataGrid _dataGrid;
    private int _capturedFirstVisibleIndex;
    private readonly List<Action> _pendingAdjustments = new();
    private bool _isLoaded;

    /// <summary>
    /// Creates a new ScrollPositionAdjuster bound to the specified DataGrid.
    /// If the DataGrid is not yet loaded, adjustments are queued until it is.
    /// </summary>
    public ScrollPositionAdjuster(DataGrid dataGrid)
    {
        _dataGrid = dataGrid ?? throw new ArgumentNullException(nameof(dataGrid));
        _isLoaded = dataGrid.IsLoaded;

        if (!_isLoaded)
        {
            _dataGrid.Loaded += OnDataGridLoaded;
        }
    }

    private void OnDataGridLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isLoaded = true;
        _dataGrid.Loaded -= OnDataGridLoaded;

        // Apply any queued adjustments
        foreach (Action adjustment in _pendingAdjustments)
        {
            adjustment();
        }
        _pendingAdjustments.Clear();
    }

    /// <summary>
    /// Gets the row height used for scroll calculations.
    /// Uses the DataGrid's RowHeight if set, otherwise defaults to 24.
    /// </summary>
    private double GetRowHeight() => _dataGrid.RowHeight > 0 ? _dataGrid.RowHeight : 24.0;

    /// <summary>
    /// Finds the ScrollViewer within the DataGrid's visual tree.
    /// </summary>
    private ScrollViewer? FindScrollViewer() => _dataGrid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>
    /// Call before a mutation to capture the current scroll state.
    /// Stores the index of the first fully visible row.
    /// </summary>
    public void CaptureScrollState()
    {
        if (!_isLoaded)
        {
            _capturedFirstVisibleIndex = 0;
            return;
        }

        ScrollViewer? scrollViewer = FindScrollViewer();
        if (scrollViewer == null)
        {
            _capturedFirstVisibleIndex = 0;
            return;
        }

        double rowHeight = GetRowHeight();
        _capturedFirstVisibleIndex = (int)(scrollViewer.Offset.Y / rowHeight);
    }

    /// <summary>
    /// Call after an insertion to adjust scroll if the insertion was above viewport.
    /// </summary>
    /// <param name="insertionIndex">The index at which rows were inserted.</param>
    /// <param name="count">The number of rows inserted.</param>
    public void AdjustForInsertion(int insertionIndex, int count = 1)
    {
        if (!_isLoaded)
        {
            _pendingAdjustments.Add(() => AdjustForInsertion(insertionIndex, count));
            return;
        }

        int newFirstVisible = ComputeScrollAfterInsertion(_capturedFirstVisibleIndex, insertionIndex, count);
        ApplyScrollIndex(newFirstVisible);
    }

    /// <summary>
    /// Call after a removal to adjust scroll if the removal was above viewport.
    /// </summary>
    /// <param name="removalIndex">The starting index of the removed range.</param>
    /// <param name="count">The number of rows removed.</param>
    public void AdjustForRemoval(int removalIndex, int count = 1)
    {
        if (!_isLoaded)
        {
            _pendingAdjustments.Add(() => AdjustForRemoval(removalIndex, count));
            return;
        }

        int totalCount = _dataGrid.ItemsSource is System.Collections.ICollection collection
            ? collection.Count
            : 0;

        int newFirstVisible = ComputeScrollAfterRemoval(_capturedFirstVisibleIndex, removalIndex, count, totalCount);
        ApplyScrollIndex(newFirstVisible);
    }

    /// <summary>
    /// Applies the computed first-visible-row index to the DataGrid's ScrollViewer.
    /// </summary>
    private void ApplyScrollIndex(int firstVisibleIndex)
    {
        ScrollViewer? scrollViewer = FindScrollViewer();
        if (scrollViewer == null)
            return;

        double rowHeight = GetRowHeight();
        double newOffsetY = firstVisibleIndex * rowHeight;
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newOffsetY);
    }

    /// <summary>
    /// Computes the adjusted first-visible-row index after an insertion.
    /// If the insertion is at or above the current viewport, the viewport shifts down
    /// by the inserted count. If the insertion is below the viewport, no change occurs.
    /// </summary>
    /// <param name="currentFirstVisibleIndex">The index of the first fully visible row before insertion.</param>
    /// <param name="insertionIndex">The index at which new rows were inserted.</param>
    /// <param name="insertedCount">The number of rows inserted.</param>
    /// <returns>The adjusted first-visible-row index.</returns>
    public static int ComputeScrollAfterInsertion(
        int currentFirstVisibleIndex, int insertionIndex, int insertedCount)
    {
        if (insertionIndex <= currentFirstVisibleIndex)
        {
            return currentFirstVisibleIndex + insertedCount;
        }

        return currentFirstVisibleIndex;
    }

    /// <summary>
    /// Computes the adjusted first-visible-row index after a removal.
    /// If removals occur above the current viewport, the viewport shifts up
    /// by the number of removed items that were above it (clamped to 0).
    /// The result is also clamped to the new valid range [0, newTotalCount - 1].
    /// </summary>
    /// <param name="currentFirstVisibleIndex">The index of the first fully visible row before removal.</param>
    /// <param name="removalIndex">The starting index of the removed range.</param>
    /// <param name="removedCount">The number of rows removed.</param>
    /// <param name="newTotalCount">The total number of rows after removal.</param>
    /// <returns>The adjusted first-visible-row index.</returns>
    public static int ComputeScrollAfterRemoval(
        int currentFirstVisibleIndex, int removalIndex, int removedCount, int newTotalCount)
    {
        if (newTotalCount <= 0)
        {
            return 0;
        }

        // Calculate how many of the removed items were above the current viewport.
        // The removed range is [removalIndex, removalIndex + removedCount - 1].
        // Items above the viewport are those with index < currentFirstVisibleIndex.
        int removalEnd = removalIndex + removedCount; // exclusive end
        int removedAbove = Math.Max(0, Math.Min(removalEnd, currentFirstVisibleIndex) - removalIndex);

        int adjusted = currentFirstVisibleIndex - removedAbove;

        // Clamp to valid range
        return Math.Max(0, Math.Min(adjusted, newTotalCount - 1));
    }
}