using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NkdsUi.Views.Controls;

public partial class ImageListControl : UserControl
{
    private MultipleDisposable? _sortIndicatorDisposable;

    // Store original header text for each column (keyed by Tag)
    private readonly Dictionary<string, string> _originalHeaders = new();

    // Saved column widths to restore when stats columns become visible
    private IReadOnlyDictionary<string, double>? _savedColumnWidths;

    // Track pointer press to distinguish clicks from resize drags
    private Point _pointerPressPosition;
    private bool _pointerPressedOnHeader;
    private double[]? _columnWidthsAtPress;

    // Scroll position adjuster for stable scroll during collection mutations
    private ScrollPositionAdjuster? _scrollPositionAdjuster;

    // Track the currently subscribed FilteredImages collection to unsubscribe on context change
    private SuppressibleObservableCollection<ImageRowViewModel>? _subscribedCollection;

    public ImageListControl()
    {
        InitializeComponent();

        // Handle SelectionChanged to sync selected items to ViewModel.SelectedImages
        ImageDataGrid.SelectionChanged += OnDataGridSelectionChanged;

        // Suppress context menu when there are no actionable items
        ImageDataGrid.ContextRequested += OnDataGridContextRequested;

        // Track pointer press/release on column headers for custom sorting.
        // We track press position and only trigger sort if the pointer didn't move
        // (distinguishes clicks from column resize drags).
        ImageDataGrid.AddHandler(PointerPressedEvent, OnDataGridPointerPressed, handledEventsToo: true);
        ImageDataGrid.AddHandler(PointerReleasedEvent, OnDataGridPointerReleased, handledEventsToo: true);

        // Forward drag-and-drop events from the DataGrid to the parent window.
        // The DataGrid's CanUserReorderColumns consumes DnD events internally,
        // so we re-raise DragOver/Drop on the window level with handledEventsToo.
        ImageDataGrid.AddHandler(DragDrop.DragOverEvent, OnImageDataGridDragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ImageDataGrid.AddHandler(DragDrop.DropEvent, OnImageDataGridDrop, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Also handle at UserControl level to catch drops on empty background area
        AddHandler(DragDrop.DragOverEvent, OnImageDataGridDragOver);
        AddHandler(DragDrop.DropEvent, OnImageDataGridDrop);

        // Store original headers after initialization
        Dispatcher.UIThread.Post(() => CaptureOriginalHeaders(), DispatcherPriority.Loaded);

        // Subscribe to sort state changes to update column header indicators
        DataContextChanged += OnDataContextChanged;
    }

    private void OnImageDataGridDragOver(object? sender, DragEventArgs e)
    {
        // Check if the drag contains files (not a column reorder)
        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
            return;

        bool hasFileOrFolder = items.Any(it =>
        {
            try
            {
                IStorageItem? v = it.TryGetFile();
                return v is Avalonia.Platform.Storage.IStorageFile || v is Avalonia.Platform.Storage.IStorageFolder;
            }
            catch { return false; }
        });

        if (hasFileOrFolder)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnImageDataGridDrop(object? sender, DragEventArgs e)
    {
        // Check if the drag contains files (not a column reorder)
        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
            return;

        bool hasFileOrFolder = items.Any(it =>
        {
            try
            {
                IStorageItem? v = it.TryGetFile();
                return v is Avalonia.Platform.Storage.IStorageFile || v is Avalonia.Platform.Storage.IStorageFolder;
            }
            catch { return false; }
        });

        if (hasFileOrFolder)
        {
            // Mark as handled to prevent the DataGrid from processing it as a column reorder.
            // The MainWindow's handler uses handledEventsToo=true so it still receives this event.
            e.Handled = true;
        }
    }

    private void CaptureOriginalHeaders()
    {
        foreach (DataGridColumn? column in ImageDataGrid.Columns)
        {
            string? tag = column.Tag as string;
            if (string.IsNullOrEmpty(tag))
                continue;

            if (column.Header is string header)
            {
                _originalHeaders[tag] = header;
            }
            else if (column.Header is Avalonia.Controls.TextBlock tb)
            {
                // Stats columns use TextBlock headers — capture the original text
                _originalHeaders[tag] = tb.Text ?? "";
            }
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _sortIndicatorDisposable?.Dispose();
        _sortIndicatorDisposable = new MultipleDisposable();

        // Unsubscribe from previous collection
        if (_subscribedCollection != null)
        {
            _subscribedCollection.CollectionChanged -= OnFilteredImagesCollectionChanged;
            _subscribedCollection = null;
        }

        if (DataContext is ImageListViewModel viewModel)
        {
            viewModel.WhenAnyValue(x => x.SortColumn, x => x.SortDirection)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateSortIndicators(viewModel))
                .DisposeWith(_sortIndicatorDisposable);

            // Toggle stats columns visibility based on ShowStatsColumns
            viewModel.WhenAnyValue(x => x.ShowStatsColumns)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(show => UpdateStatsColumnsVisibility(show))
                .DisposeWith(_sortIndicatorDisposable);

            // Initial state: hide stats columns
            UpdateStatsColumnsVisibility(viewModel.ShowStatsColumns);

            // Initialize ScrollPositionAdjuster with the DataGrid
            _scrollPositionAdjuster = new ScrollPositionAdjuster(ImageDataGrid);

            // Subscribe to FilteredImages CollectionChanged for scroll adjustment
            _subscribedCollection = viewModel.FilteredImages;
            _subscribedCollection.CollectionChanged += OnFilteredImagesCollectionChanged;
        }
    }

    /// <summary>
    /// Handles CollectionChanged events from FilteredImages to adjust scroll position
    /// when items are inserted or removed above the current viewport.
    /// </summary>
    private void OnFilteredImagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollPositionAdjuster == null)
            return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                // Capture current scroll state before adjusting
                _scrollPositionAdjuster.CaptureScrollState();
                if (e.NewStartingIndex >= 0)
                {
                    _scrollPositionAdjuster.AdjustForInsertion(e.NewStartingIndex, e.NewItems?.Count ?? 1);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                // Capture current scroll state before adjusting
                _scrollPositionAdjuster.CaptureScrollState();
                if (e.OldStartingIndex >= 0)
                {
                    _scrollPositionAdjuster.AdjustForRemoval(e.OldStartingIndex, e.OldItems?.Count ?? 1);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                // Reset events come from batch operations (EndBatch) — no scroll adjustment needed.
                // The batch operation handles scroll state externally if needed.
                break;
        }
    }

    private void OnDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerPressedOnHeader = false;

        // Only track left-button presses on column headers
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Control? source = e.Source as Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        // Check we're not on a resize grip (the thin edge between columns)
        // Thumb controls are used for resize grips in Avalonia DataGrid
        Thumb? thumb = source?.FindAncestorOfType<Thumb>();
        if (thumb != null)
            return;

        _pointerPressedOnHeader = true;
        _pointerPressPosition = e.GetPosition(this);
        _columnWidthsAtPress = ImageDataGrid.Columns.Select(c => c.ActualWidth).ToArray();
    }

    private void OnDataGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerPressedOnHeader)
            return;

        _pointerPressedOnHeader = false;

        if (DataContext is not ImageListViewModel viewModel)
            return;

        // Check the pointer didn't move significantly (threshold: 5px)
        Point releasePosition = e.GetPosition(this);
        Point delta = _pointerPressPosition - releasePosition;
        if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
            return;

        // If any column width changed, this was a resize drag — don't sort
        if (_columnWidthsAtPress != null)
        {
            ObservableCollection<DataGridColumn> columns = ImageDataGrid.Columns;
            for (int i = 0; i < columns.Count && i < _columnWidthsAtPress.Length; i++)
            {
                if (Math.Abs(columns[i].ActualWidth - _columnWidthsAtPress[i]) > 0.5)
                    return;
            }
        }

        // Find the column header under the release point
        Control? source = e.Source as Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        // Match the column header to a DataGrid column.
        // Strategy: try matching by string content first (plain text headers),
        // then fall back to matching by column index (for template headers like stats columns).
        DataGridColumn? targetColumn = null;
        string? contentStr = columnHeader.Content as string;

        if (contentStr != null)
        {
            // Try exact match first
            foreach (DataGridColumn? col in ImageDataGrid.Columns)
            {
                if (col.Header is string headerText && headerText == contentStr)
                {
                    targetColumn = col;
                    break;
                }
            }

            // Fall back: match via original headers (handles arrow suffixes from sort indicators)
            if (targetColumn == null)
            {
                foreach (KeyValuePair<string, string> kvp in _originalHeaders)
                {
                    if (contentStr.StartsWith(kvp.Value))
                    {
                        foreach (DataGridColumn? col in ImageDataGrid.Columns)
                        {
                            if ((col.Tag as string) == kvp.Key)
                            {
                                targetColumn = col;
                                break;
                            }
                        }
                        break;
                    }
                }
            }
        }
        else
        {
            // Header content is not a string (e.g., a TextBlock for stats columns).
            // Match by reference: the columnHeader.Content is the same object as column.Header.
            object? headerContent = columnHeader.Content;
            if (headerContent != null)
            {
                foreach (DataGridColumn? col in ImageDataGrid.Columns)
                {
                    if (ReferenceEquals(col.Header, headerContent))
                    {
                        targetColumn = col;
                        break;
                    }
                }
            }
        }

        if (targetColumn == null)
            return;

        string? columnName = targetColumn.Tag as string;
        if (string.IsNullOrEmpty(columnName))
            return;

        viewModel.SortCommand.Execute(columnName).Subscribe();
    }

    private static readonly HashSet<string> StatsColumnTags = new()
    {
        "Analysis", "UniqueRawSize", "UniqueCompressedSize",
        "SharedRawSize", "SharedCompressedSize", "SavedSize", "ReducedBy"
    };

    private void UpdateStatsColumnsVisibility(bool show)
    {
        foreach (DataGridColumn? column in ImageDataGrid.Columns)
        {
            string? tag = column.Tag as string;
            if (!string.IsNullOrEmpty(tag) && StatsColumnTags.Contains(tag))
            {
                column.IsVisible = show;

                // Restore saved width when making columns visible
                if (show && _savedColumnWidths != null &&
                    _savedColumnWidths.TryGetValue(tag, out double width) && width > 0)
                {
                    column.Width = new DataGridLength(width);
                }
            }
        }
    }

    /// <summary>
    /// Provides saved column widths so they can be restored when stats columns become visible.
    /// </summary>
    public void SetSavedColumnWidths(IReadOnlyDictionary<string, double>? widths) => _savedColumnWidths = widths;

    private void UpdateSortIndicators(ImageListViewModel viewModel)
    {
        foreach (DataGridColumn? column in ImageDataGrid.Columns)
        {
            string? columnTag = column.Tag as string;
            if (string.IsNullOrEmpty(columnTag))
                continue;

            if (!_originalHeaders.TryGetValue(columnTag, out string? originalHeader))
                continue;

            string arrow = string.Empty;
            if (string.Equals(columnTag, viewModel.SortColumn, StringComparison.Ordinal))
            {
                arrow = viewModel.SortDirection switch
                {
                    SortDirection.Ascending => " ▲",
                    SortDirection.Descending => " ▼",
                    _ => string.Empty
                };
            }

            if (column.Header is string)
            {
                column.Header = originalHeader + arrow;
            }
            else if (column.Header is Avalonia.Controls.TextBlock tb)
            {
                tb.Text = originalHeader + arrow;
            }
        }
    }

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ImageListViewModel viewModel)
            return;

        viewModel.SelectedImages.Clear();
        foreach (object? item in ImageDataGrid.SelectedItems)
        {
            if (item is ImageRowViewModel rowViewModel)
            {
                viewModel.SelectedImages.Add(rowViewModel);
            }
        }
    }

    private void OnDataGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ImageListViewModel viewModel || !viewModel.HasAddCancelledSelection)
            e.Handled = true;
    }
}