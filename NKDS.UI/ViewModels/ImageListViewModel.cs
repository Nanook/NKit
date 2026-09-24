using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the image list DataGrid. Manages filtering, sorting, selection, and status counts
/// for all images across open Database Sessions.
/// </summary>
public class ImageListViewModel : ViewModelBase, IDisposable
{
    private readonly IDataStoreService _dataStoreService;
    private readonly ErrorNotificationService? _errorNotification;
    private readonly MultipleDisposable _disposables = new();

    private Dictionary<(string SetName, long ImageId), ImageRowViewModel> _allImages = new();

    // Backing list of all rows for filter source (updated when image source changes)
    private List<ImageRowViewModel> _allRows = new();

    // Last received sessions for deferred refresh
    private IReadOnlyList<ImageSessionModel>? _lastSessions;

    // Comparison Mode / Working Set
    private bool _isComparisonMode;
    private IReadOnlyList<ImageRowViewModel> _workingSet = Array.Empty<ImageRowViewModel>();

    // Filtering
    private string _nameFilter = string.Empty;
    private string? _selectedSystemFilter;
    private string? _selectedSetFilter;
    private bool _showStatsColumns;
    private ObservableCollection<string> _availableSystems = new();
    private readonly SuppressibleObservableCollection<ImageRowViewModel> _filteredImages = new();

    // Stable collection mutation support
    private StableCollectionMutator _mutator;
    private readonly BatchCoalescer<ImageRowViewModel> _batchCoalescer;
    private readonly SelectionTracker _selectionTracker = new();

    // Suppresses ApplyFilters during exclusive access operations to prevent flicker
    private bool _suppressApplyFilters;

    // Selection
    private ObservableCollection<ImageRowViewModel> _selectedImages = new();

    // Sorting
    private string? _sortColumn;
    private SortDirection _sortDirection = SortDirection.None;

    // Status
    private int _totalImageCount;
    private int _selectedImageCount;

    /// <summary>
    /// Text filter for case-insensitive substring match on image Name.
    /// </summary>
    public string NameFilter
    {
        get => _nameFilter;
        set => this.RaiseAndSetIfChanged(ref _nameFilter, value);
    }

    /// <summary>
    /// Selected system filter value. Null or empty means no system filter applied.
    /// </summary>
    public string? SelectedSystemFilter
    {
        get => _selectedSystemFilter;
        set => this.RaiseAndSetIfChanged(ref _selectedSystemFilter, value);
    }

    /// <summary>
    /// Set name filter. When set to a specific set name, only images from that set are shown.
    /// When null or "All", all sets are shown.
    /// </summary>
    public string? SelectedSetFilter
    {
        get => _selectedSetFilter;
        set => this.RaiseAndSetIfChanged(ref _selectedSetFilter, value);
    }

    /// <summary>
    /// Whether to show the stats columns (Analysis, Stored, Compressed, Dedupe, etc.).
    /// Set to true after stats are computed.
    /// </summary>
    public bool ShowStatsColumns
    {
        get => _showStatsColumns;
        set => this.RaiseAndSetIfChanged(ref _showStatsColumns, value);
    }

    /// <summary>
    /// List of distinct System values from the current image set, for populating the filter dropdown.
    /// </summary>
    public ObservableCollection<string> AvailableSystems
    {
        get => _availableSystems;
        private set => this.RaiseAndSetIfChanged(ref _availableSystems, value);
    }

    /// <summary>
    /// Observable collection of images after applying name and system filters, with current sort applied.
    /// This instance is created once and never reassigned; all mutations are performed in-place.
    /// </summary>
    public SuppressibleObservableCollection<ImageRowViewModel> FilteredImages => _filteredImages;

    /// <summary>
    /// Observable collection of currently selected images (multi-select support).
    /// </summary>
    public ObservableCollection<ImageRowViewModel> SelectedImages
    {
        get => _selectedImages;
        private set => this.RaiseAndSetIfChanged(ref _selectedImages, value);
    }

    /// <summary>
    /// Total number of images after filtering.
    /// </summary>
    public int TotalImageCount
    {
        get => _totalImageCount;
        private set => this.RaiseAndSetIfChanged(ref _totalImageCount, value);
    }

    /// <summary>
    /// Number of currently selected images.
    /// </summary>
    public int SelectedImageCount
    {
        get => _selectedImageCount;
        private set => this.RaiseAndSetIfChanged(ref _selectedImageCount, value);
    }

    /// <summary>
    /// Whether Comparison Mode is active. When true, the image source switches
    /// from AllImages to the Working_Set.
    /// </summary>
    public bool IsComparisonMode
    {
        get => _isComparisonMode;
        set => this.RaiseAndSetIfChanged(ref _isComparisonMode, value);
    }

    /// <summary>
    /// The currently sorted column name, or null if no sort is active.
    /// </summary>
    public string? SortColumn
    {
        get => _sortColumn;
        private set => this.RaiseAndSetIfChanged(ref _sortColumn, value);
    }

    /// <summary>
    /// The current sort direction (None, Ascending, or Descending).
    /// </summary>
    public SortDirection SortDirection
    {
        get => _sortDirection;
        private set => this.RaiseAndSetIfChanged(ref _sortDirection, value);
    }

    /// <summary>
    /// Clears the current sort, reverting to insertion order.
    /// Call before pre-populating to ensure rows appear in scan order.
    /// </summary>
    public void ClearSort()
    {
        SortColumn = null;
        SortDirection = SortDirection.None;
    }

    /// <summary>
    /// Command that accepts a column name and applies the three-state sort cycle:
    /// unsorted → ascending → descending → unsorted (default order).
    /// </summary>
    public ReactiveCommand<string, RxVoid> SortCommand { get; }

    /// <summary>
    /// Whether one or more selected rows have AddCancelled processing status.
    /// Used to control visibility of the "Add Cancelled" context menu submenu.
    /// </summary>
    private bool _hasAddCancelledSelection;
    public bool HasAddCancelledSelection
    {
        get => _hasAddCancelledSelection;
        private set => this.RaiseAndSetIfChanged(ref _hasAddCancelledSelection, value);
    }

    /// <summary>
    /// Command to re-process selected AddCancelled rows through the pipeline.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ProcessAddCancelledCommand { get; }

    /// <summary>
    /// Command to remove selected AddCancelled rows from the image list.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ClearAddCancelledCommand { get; }

    /// <summary>
    /// Command to re-process ALL AddCancelled rows through the pipeline.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ProcessAllAddCancelledCommand { get; }

    /// <summary>
    /// Command to remove ALL AddCancelled rows from the image list.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ClearAllAddCancelledCommand { get; }

    /// <summary>Action delegate set by MainWindowViewModel to wire Process Selected to the handler.</summary>
    public Action? ProcessAddCancelledAction { get; set; }

    /// <summary>Action delegate set by MainWindowViewModel to wire Process All to the handler.</summary>
    public Action? ProcessAllAddCancelledAction { get; set; }

    public ImageListViewModel(IDataStoreService dataStoreService, ErrorNotificationService? errorNotification = null)
    {
        _dataStoreService = dataStoreService;
        _errorNotification = errorNotification;

        // Initialize stable collection mutation support
        // Accept-all filter and no sort initially; will be updated when filters/sort change
        _mutator = new StableCollectionMutator(_filteredImages, _ => true, null);
        _batchCoalescer = new BatchCoalescer<ImageRowViewModel>(_filteredImages);

        // Default sort: Name ascending
        _sortColumn = "Name";
        _sortDirection = SortDirection.Ascending;

        // Create the SortCommand
        SortCommand = ReactiveCommand.Create<string>(ExecuteSort);
        SortCommand.DisposeWith(_disposables);

        // Create AddCancelled context menu commands (canExecute tied to HasAddCancelledSelection)
        IObservable<bool> canExecuteAddCancelled = this.WhenAnyValue(x => x.HasAddCancelledSelection);
        ProcessAddCancelledCommand = ReactiveCommand.Create(() => ProcessAddCancelledAction?.Invoke(), canExecuteAddCancelled);
        ProcessAddCancelledCommand.DisposeWith(_disposables);
        ClearAddCancelledCommand = ReactiveCommand.Create(ExecuteClearAddCancelled, canExecuteAddCancelled);
        ClearAddCancelledCommand.DisposeWith(_disposables);
        ProcessAllAddCancelledCommand = ReactiveCommand.Create(() => ProcessAllAddCancelledAction?.Invoke(), canExecuteAddCancelled);
        ProcessAllAddCancelledCommand.DisposeWith(_disposables);
        ClearAllAddCancelledCommand = ReactiveCommand.Create(ExecuteClearAllAddCancelled, canExecuteAddCancelled);
        ClearAllAddCancelledCommand.DisposeWith(_disposables);

        // Subscribe to Sessions to get images with their owning session context
        _dataStoreService.Sessions
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(sessions =>
            {
                _lastSessions = sessions;
                if (_suppressApplyFilters)
                    return;

                try
                {
                    RebuildAllImagesFromSessions(sessions);
                    UpdateAvailableSystems();
                    ApplyFilters();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Error] Sessions update failed: {ex.Message}");
                    _errorNotification?.PublishOperationError($"Sessions update failed: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);

        // React to filter changes and reapply filters
        this.WhenAnyValue(x => x.NameFilter, x => x.SelectedSystemFilter, x => x.SelectedSetFilter)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                try
                {
                    ApplyFilters();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Error] Filter change failed: {ex.Message}");
                    _errorNotification?.PublishOperationError($"Filter change failed: {ex.Message}");
                }
            })
            .DisposeWith(_disposables);

        // Track selection count changes and update HasAddCancelledSelection
        SelectedImages.CollectionChanged += (_, _) =>
        {
            SelectedImageCount = SelectedImages.Count;
            HasAddCancelledSelection = SelectedImages.Any(
                row => row.ProcessingStatus == ImageProcessingStatus.AddCancelled);
        };
    }

    /// <summary>
    /// Rebuilds the _allImages dictionary from the current sessions.
    /// Preserves existing ImageRowViewModel instances (and their stats) for images that still exist.
    /// Also preserves rows with meaningful UI state (VerifyResult, ProcessingStatus, stats) even if
    /// their session is temporarily closed (e.g., during an Add operation that requires exclusive access).
    /// Assigns the correct SessionId from the owning session.
    /// Includes all images (both active and removed) since session.Images now contains both.
    /// </summary>
    private void RebuildAllImagesFromSessions(IReadOnlyList<ImageSessionModel> sessions)
    {
        Dictionary<(string SetName, long ImageId), ImageRowViewModel> newDict = new Dictionary<(string SetName, long ImageId), ImageRowViewModel>();

        foreach (ImageSessionModel session in sessions)
        {
            foreach (ImageRecord image in session.Images)
            {
                // TmdAppFolder images are internal aggregation containers — the children
                // (individual [tmd.X] App images) are shown instead.
                if (image.Format == ImageFormat.TmdAppFolder)
                    continue;

                (string SetName, long Id) key = (image.SetName, image.Id);
                if (_allImages.TryGetValue(key, out ImageRowViewModel? existingRow))
                {
                    // Preserve existing row (keeps stats intact)
                    newDict[key] = existingRow;
                }
                else
                {
                    newDict[key] = new ImageRowViewModel(image, session.SessionId);
                }
            }
        }

        // Preserve rows with meaningful UI state that aren't in the current sessions.
        // This handles the case where a session is temporarily closed (e.g., for exclusive
        // file access during Add) — we don't want to lose VerifyResult, ProcessingStatus,
        // or stats that were set by previous operations or live insertion.
        foreach (KeyValuePair<(string SetName, long ImageId), ImageRowViewModel> kvp in _allImages)
        {
            if (!newDict.ContainsKey(kvp.Key) && kvp.Value.HasMeaningfulState)
            {
                newDict[kvp.Key] = kvp.Value;
            }
        }

        _allImages = newDict;
    }

    /// <summary>
    /// Executes the three-state sort cycle for the given column name.
    /// </summary>
    private void ExecuteSort(string columnName)
    {
        if (string.IsNullOrEmpty(columnName))
            return;

        if (_sortColumn != columnName)
        {
            // Clicking a different column: sort ascending
            SortColumn = columnName;
            SortDirection = SortDirection.Ascending;
        }
        else if (_sortDirection == SortDirection.Ascending)
        {
            // Clicking the same column that's ascending: sort descending
            SortDirection = SortDirection.Descending;
        }
        else
        {
            // Clicking the same column that's descending: clear sort
            SortColumn = null;
            SortDirection = SortDirection.None;
        }

        ApplyFilters();
    }

    /// <summary>
    /// Removes selected AddCancelled rows from the image list.
    /// Only removes rows that are both selected and have ProcessingStatus == AddCancelled.
    /// </summary>
    private void ExecuteClearAddCancelled()
    {
        // Snapshot the selected AddCancelled rows before mutation
        List<ImageRowViewModel> rowsToClear = SelectedImages
            .Where(row => row.ProcessingStatus == ImageProcessingStatus.AddCancelled)
            .ToList();

        if (rowsToClear.Count == 0)
            return;

        // Use a HashSet for O(1) lookup in the predicate
        HashSet<ImageRowViewModel> rowSet = new HashSet<ImageRowViewModel>(rowsToClear, ReferenceEqualityComparer.Instance);

        RemoveRowsBatch(row => rowSet.Contains(row));
    }

    /// <summary>
    /// Removes ALL AddCancelled rows from the image list (regardless of selection).
    /// </summary>
    private void ExecuteClearAllAddCancelled() => RemoveRowsBatch(row => row.ProcessingStatus == ImageProcessingStatus.AddCancelled);

    /// <summary>
    /// Updates the AvailableSystems list from distinct System values in the current image source.
    /// When in Comparison Mode, scopes to the Working_Set; otherwise uses all images.
    /// </summary>
    private void UpdateAvailableSystems()
    {
        IEnumerable<ImageRowViewModel> source = IsComparisonMode
            ? _workingSet
            : (IEnumerable<ImageRowViewModel>)_allImages.Values;

        List<string?> systems = source
            .Select(row => row.System)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AvailableSystems = new ObservableCollection<string>(systems!);
    }

    /// <summary>
    /// Applies name and system filters to the current image source, sorts the result,
    /// and updates FilteredImages in-place. Uses batch notification suppression to
    /// avoid per-item CollectionChanged events during the rebuild.
    /// </summary>
    internal void ApplyFilters()
    {
        // Skip if suppressed (during exclusive access operations that will trigger a full refresh later)
        if (_suppressApplyFilters)
            return;

        // Update the backing list of all rows from the current source
        _allRows = IsComparisonMode
            ? _workingSet.ToList()
            : _allImages.Values.ToList();

        // Filter and sort
        IReadOnlyList<ImageRowViewModel> filtered = ApplyFilters(_allRows, _nameFilter, _selectedSystemFilter, _selectedSetFilter);
        IReadOnlyList<ImageRowViewModel> sorted = ApplySort(filtered, _sortColumn, _sortDirection);

        // Rebuild the collection in-place with suppressed notifications (single Reset at end)
        _filteredImages.SuppressNotifications = true;
        _filteredImages.Clear();
        foreach (ImageRowViewModel row in sorted)
            _filteredImages.Add(row);
        _filteredImages.SuppressNotifications = false;
        _filteredImages.RaiseReset();

        // Build the new filter predicate for the mutator (used by InsertRow, Remove, Reconcile)
        string nameFilter = _nameFilter;
        string? systemFilter = _selectedSystemFilter;
        string? setFilter = _selectedSetFilter;

        Func<ImageRowViewModel, bool> newPredicate = row =>
        {
            if (!string.IsNullOrEmpty(setFilter) && setFilter != "All")
            {
                if (!string.Equals(row.SetName, setFilter, StringComparison.Ordinal))
                    return false;
            }

            if (!string.IsNullOrEmpty(nameFilter))
            {
                if (!row.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrEmpty(systemFilter))
            {
                if (!string.Equals(row.System, systemFilter, StringComparison.Ordinal))
                    return false;
            }

            return true;
        };

        // Get the current sort comparer (null if no sort active)
        Func<ImageRowViewModel, ImageRowViewModel, int>? sortComparer =
            (string.IsNullOrEmpty(_sortColumn) || _sortDirection == SortDirection.None)
                ? null
                : GetCurrentSortComparer();

        // Update the mutator with current filter/sort state for subsequent InsertRow/Remove calls
        _mutator = new StableCollectionMutator(_filteredImages, newPredicate, sortComparer);

        TotalImageCount = _filteredImages.Count;
    }

    /// <summary>
    /// Pure filtering logic: applies name filter (case-insensitive substring) and system filter (exact match).
    /// Exposed as static for testability.
    /// </summary>
    internal static IReadOnlyList<ImageRowViewModel> ApplyFilters(
        IReadOnlyList<ImageRowViewModel> images,
        string nameFilter,
        string? systemFilter,
        string? setFilter = null)
    {
        IEnumerable<ImageRowViewModel> result = images;

        // Apply set filter: exact match on SetName (skip if "All" or null)
        if (!string.IsNullOrEmpty(setFilter) && setFilter != "All")
        {
            result = result.Where(row =>
                string.Equals(row.SetName, setFilter, StringComparison.Ordinal));
        }

        // Apply name filter: case-insensitive substring match on Name
        if (!string.IsNullOrEmpty(nameFilter))
        {
            result = result.Where(row =>
                row.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply system filter: exact match on System
        if (!string.IsNullOrEmpty(systemFilter))
        {
            result = result.Where(row =>
                string.Equals(row.System, systemFilter, StringComparison.Ordinal));
        }

        return result.ToList();
    }

    /// <summary>
    /// Applies the current sort to a filtered list of images.
    /// Uses NullsLastComparer for nullable stats columns.
    /// </summary>
    internal static IReadOnlyList<ImageRowViewModel> ApplySort(
        IReadOnlyList<ImageRowViewModel> images,
        string? sortColumn,
        SortDirection sortDirection)
    {
        if (string.IsNullOrEmpty(sortColumn) || sortDirection == SortDirection.None)
            return images;

        bool descending = sortDirection == SortDirection.Descending;

        return sortColumn switch
        {
            "Name" => OrderBy(images, r => r.Name, descending),
            "System" => OrderBy(images, r => r.System ?? string.Empty, descending),
            "SetName" => OrderBy(images, r => r.SetName, descending),
            "Size" => OrderBy(images, r => r.Size, descending),
            "Format" => OrderBy(images, r => r.Format.ToString(), descending),
            "Crc32" => OrderBy(images, r => r.Crc32, descending),
            "XxHash64" => OrderBy(images, r => r.XxHash64, descending),
            "UniqueRawSize" => OrderByNullable(images, r => r.UniqueRawSize, descending),
            "UniqueCompressedSize" => OrderByNullable(images, r => r.UniqueCompressedSize, descending),
            "SharedRawSize" => OrderByNullable(images, r => r.SharedRawSize, descending),
            "SharedCompressedSize" => OrderByNullable(images, r => r.SharedCompressedSize, descending),
            "SavedSize" => OrderByNullable(images, r => r.SavedSize, descending),
            "ReducedBy" => OrderByNullable(images, r => r.ReducedBy, descending),
            "Removed" => OrderBy(images, r => r.Removed ? 1 : 0, descending),
            _ => images
        };
    }

    /// <summary>
    /// Orders a list by a non-nullable key using standard comparison.
    /// </summary>
    private static IReadOnlyList<ImageRowViewModel> OrderBy<TKey>(
        IReadOnlyList<ImageRowViewModel> images,
        Func<ImageRowViewModel, TKey> keySelector,
        bool descending)
    {
        return descending
            ? images.OrderByDescending(keySelector).ToList()
            : images.OrderBy(keySelector).ToList();
    }

    /// <summary>
    /// Orders a list by a nullable value-type key with nulls always sorted last.
    /// </summary>
    private static IReadOnlyList<ImageRowViewModel> OrderByNullable<TKey>(
        IReadOnlyList<ImageRowViewModel> images,
        Func<ImageRowViewModel, TKey?> keySelector,
        bool descending) where TKey : struct, IComparable<TKey>
    {
        return images.OrderBy(row => row, Comparer<ImageRowViewModel>.Create((a, b) =>
        {
            TKey? x = keySelector(a);
            TKey? y = keySelector(b);
            if (x == null && y == null) return 0;
            if (x == null) return 1;  // nulls last regardless of direction
            if (y == null) return -1;
            int result = x.Value.CompareTo(y.Value);
            return descending ? -result : result;
        })).ToList();
    }

    /// <summary>
    /// Sets the Working_Set for Comparison Mode filtering.
    /// Enables Comparison Mode, scopes AvailableSystems to the Working_Set, and reapplies filters.
    /// Accepts ImageRecord list for compatibility with existing callers (e.g., ComparisonModeViewModel).
    /// </summary>
    public void SetWorkingSet(IReadOnlyList<ImageRecord> workingSet)
    {
        // Convert ImageRecords to their corresponding ImageRowViewModels
        List<ImageRowViewModel> rows = new List<ImageRowViewModel>(workingSet.Count);
        foreach (ImageRecord image in workingSet)
        {
            (string SetName, long Id) key = (image.SetName, image.Id);
            if (_allImages.TryGetValue(key, out ImageRowViewModel? row))
            {
                rows.Add(row);
            }
        }

        _workingSet = rows;
        IsComparisonMode = true;
        UpdateAvailableSystems();
        ApplyFilters();
    }

    /// <summary>
    /// Clears the Working_Set and exits Comparison Mode filtering.
    /// Restores AvailableSystems to all images and reapplies filters against the full image set.
    /// </summary>
    public void ClearWorkingSet()
    {
        _workingSet = Array.Empty<ImageRowViewModel>();
        IsComparisonMode = false;
        UpdateAvailableSystems();
        ApplyFilters();
    }

    /// <summary>
    /// Removes all rows from the image list, clearing _allImages, FilteredImages, and SelectedImages.
    /// Called by the Close toolbar button to ensure a clean slate (including any residual AddCancelled rows).
    /// </summary>
    public void ClearAllRows()
    {
        _batchCoalescer.BeginBatch();
        try
        {
            _allImages.Clear();
            _allRows.Clear();
            SelectedImages.Clear();
            _filteredImages.Clear();
        }
        finally
        {
            _batchCoalescer.EndBatch();
        }

        TotalImageCount = 0;
        SelectedImageCount = 0;
        HasAddCancelledSelection = false;
    }

    /// <summary>
    /// Gets all ImageRowViewModels across all sessions (including those currently filtered out).
    /// Used by StatsCalculationViewModel to compute stats for all images.
    /// </summary>
    public IReadOnlyList<ImageRowViewModel> GetAllImages() => _allImages.Values.ToList();

    /// <summary>
    /// Inserts a single row into FilteredImages, respecting current sort order and filters.
    /// Delegates to <see cref="StableCollectionMutator.Insert"/> for sorted, filter-aware insertion.
    /// If a row with the same SetName+Id already exists in the collection, it is removed first
    /// to prevent duplicates.
    /// </summary>
    public void InsertRow(ImageRowViewModel row)
    {
        (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);

        // Remove any existing row with the same identity to prevent duplicates
        if (_allImages.TryGetValue(key, out ImageRowViewModel? existingRow) && existingRow != row)
        {
            _mutator.Remove(r => ReferenceEquals(r, existingRow));
        }

        _allImages[key] = row;
        _mutator.Insert(row);
        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Updates a pre-populated row's ImageRecord in-place and re-keys it in _allImages.
    /// Used when a commit event arrives for a row that was pre-populated with a temporary ID.
    /// The row's position in FilteredImages is preserved (no remove+insert).
    /// </summary>
    /// <param name="setName">The set the row belongs to.</param>
    /// <param name="oldId">The previous ID (temporary negative ID) used to find the row.</param>
    /// <param name="committedImage">The real committed ImageRecord to apply.</param>
    /// <returns>The updated row if found, or null if no row with oldId exists.</returns>
    public ImageRowViewModel? UpdateRowImageInPlace(string setName, long oldId, ImageRecord committedImage)
    {
        (string setName, long oldId) oldKey = (setName, oldId);
        if (!_allImages.TryGetValue(oldKey, out ImageRowViewModel? row))
            return null;

        // Re-key in _allImages: remove old key, add new key
        _allImages.Remove(oldKey);
        (string SetName, long Id) newKey = (committedImage.SetName, committedImage.Id);
        _allImages[newKey] = row;

        // Update the Image data in-place (ImageRecord is mutable)
        ImageRecord existing = row.Image;
        existing.Id = committedImage.Id;
        existing.Name = committedImage.Name;
        existing.Size = committedImage.Size;
        existing.Crc32 = committedImage.Crc32;
        existing.XxHash64 = committedImage.XxHash64;
        existing.SetName = committedImage.SetName;
        existing.System = committedImage.System;
        existing.Format = committedImage.Format;
        existing.RollbackFileId = committedImage.RollbackFileId;
        existing.RollbackOffset = committedImage.RollbackOffset;
        existing.Removed = committedImage.Removed;

        // Raise property changed for all passthrough properties on the row
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Id));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Name));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Size));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.SizeDisplay));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Crc32));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Crc32Hex));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.XxHash64));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.XxHash64Hex));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.System));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Format));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.FormatDisplay));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Removed));
        ((ReactiveUI.ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.RemovedDisplay));

        return row;
    }

    /// <summary>
    /// Removes rows matching the predicate from FilteredImages.
    /// Delegates to <see cref="StableCollectionMutator.Remove"/> for index-safe removal.
    /// Also removes from _allImages and SelectedImages for consistency.
    /// </summary>
    public void RemoveRows(Func<ImageRowViewModel, bool> predicate)
    {
        IReadOnlyList<(ImageRowViewModel Row, int Index)> removed = _mutator.Remove(predicate);
        foreach ((ImageRowViewModel? row, int _) in removed)
        {
            (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);
            _allImages.Remove(key);
            SelectedImages.Remove(row);
        }
        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Reconciles the displayed rows with the provided image list.
    /// Delegates to <see cref="StableCollectionMutator.Reconcile"/> for minimal in-place mutations.
    /// Wraps the operation in a batch to coalesce UI notifications.
    /// Preserves existing rows and their state (selection, stats).
    /// </summary>
    public void Reconcile(string setName, IReadOnlyList<ImageRecord> currentImages)
    {
        // Determine session ID for new rows from existing rows in the set
        ImageRowViewModel? existingRow = _allImages.Values.FirstOrDefault(r =>
            string.Equals(r.SetName, setName, StringComparison.Ordinal));
        string sessionId = existingRow?.SessionId
            ?? _allImages.Values.FirstOrDefault()?.SessionId ?? "";

        // Capture selection before mutation
        IReadOnlySet<ImageRowViewModel> previousSelection = _selectionTracker.CaptureSelection(SelectedImages);

        _batchCoalescer.BeginBatch();
        try
        {
            ReconcileResult result = _mutator.Reconcile(setName, currentImages,
                image => new ImageRowViewModel(image, sessionId));

            // Update _allImages to reflect removals and additions
            foreach ((ImageRowViewModel? row, int _) in result.Removed)
            {
                (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);
                _allImages.Remove(key);
            }

            foreach ((ImageRowViewModel? row, int _) in result.Added)
            {
                (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);
                _allImages[key] = row;
            }
        }
        finally
        {
            _batchCoalescer.EndBatch();
        }

        // Restore selection for surviving rows (safely — don't crash if collection changed)
        try
        {
            List<ImageRowViewModel> survivingSelection = SelectedImages
                .Where(row => previousSelection.Contains(row) && FilteredImages.Contains(row))
                .ToList();
            SelectedImages.Clear();
            foreach (ImageRowViewModel? row in survivingSelection)
            {
                SelectedImages.Add(row);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Warning] Selection restore failed: {ex.Message}");
        }

        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Finds a row by set name and image ID.
    /// </summary>
    public ImageRowViewModel? FindRow(string setName, long imageId)
    {
        _allImages.TryGetValue((setName, imageId), out ImageRowViewModel? row);
        return row;
    }

    /// <summary>
    /// Suppresses ApplyFilters calls triggered by Sessions changes.
    /// Use during exclusive access operations (Remove, Restore, Rollback) where the session
    /// closes and reopens but the image list shouldn't visually change.
    /// Must be paired with <see cref="ResumeFilters"/>.
    /// </summary>
    public void SuppressFilters() => _suppressApplyFilters = true;

    /// <summary>
    /// Resumes ApplyFilters and optionally triggers a refresh.
    /// Call after the exclusive access operation completes and the session has reopened.
    /// </summary>
    public void ResumeFilters(bool applyNow = true)
    {
        _suppressApplyFilters = false;
        if (applyNow)
        {
            try
            {
                RebuildAllImagesFromSessions(_lastSessions ?? Array.Empty<ImageSessionModel>());
                UpdateAvailableSystems();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Error] ResumeFilters failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resumes ApplyFilters using the provided sessions, bypassing <see cref="_lastSessions"/>.
    /// Use after an exclusive-access operation that reopens the session, where the Sessions
    /// observable update may not have been processed yet (deferred via ObserveOn scheduler).
    /// </summary>
    public void ResumeFilters(IReadOnlyList<ImageSessionModel> currentSessions)
    {
        _suppressApplyFilters = false;
        _lastSessions = currentSessions;
        try
        {
            RebuildAllImagesFromSessions(currentSessions);
            UpdateAvailableSystems();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Error] ResumeFilters failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resumes filter processing without rebuilding _allImages from sessions.
    /// Use after operations where rows were inserted/updated in-place during the operation
    /// (e.g., Add Images with pre-population) and a full rebuild is unnecessary and could
    /// race with deferred Sessions emissions from the session close/reopen cycle.
    /// Only re-applies the current filters to show/hide rows based on current state.
    /// Suppresses the next deferred Sessions rebuild to prevent wiping transient rows.
    /// </summary>
    public void ResumeFiltersWithoutRebuild()
    {
        _suppressApplyFilters = false;
        // Update available systems from current _allImages (rows were inserted in-place during operation)
        UpdateAvailableSystems();
        // Do NOT call ApplyFilters() here — it would re-sort pre-populated rows.
        // The rows are already in FilteredImages in scan order from InsertRowsBatch.
    }

    /// <summary>
    /// Begins a batch operation, suppressing individual CollectionChanged notifications.
    /// Must be paired with <see cref="EndBatch"/> in a try/finally block.
    /// Use for bulk operations that insert or remove multiple rows.
    /// </summary>
    public void BeginBatch() => _batchCoalescer.BeginBatch();

    /// <summary>
    /// Ends a batch operation and raises a single Reset notification.
    /// Always call in a finally block after <see cref="BeginBatch"/>.
    /// </summary>
    public void EndBatch()
    {
        _batchCoalescer.EndBatch();
        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Inserts multiple rows in a batch, coalescing UI notifications.
    /// Wraps the operation with <see cref="BeginBatch"/>/<see cref="EndBatch"/>.
    /// Each row is inserted at the correct sorted position if it passes current filters.
    /// Removes any existing rows with the same SetName+Id to prevent duplicates.
    /// </summary>
    public void InsertRowsBatch(IReadOnlyList<ImageRowViewModel> rows, bool preserveInsertionOrder = false)
    {
        _batchCoalescer.BeginBatch();
        try
        {
            foreach (ImageRowViewModel row in rows)
            {
                (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);

                // Remove any existing row with the same identity to prevent duplicates
                if (_allImages.TryGetValue(key, out ImageRowViewModel? existingRow) && existingRow != row)
                {
                    _mutator.Remove(r => ReferenceEquals(r, existingRow));
                }

                _allImages[key] = row;

                if (preserveInsertionOrder)
                {
                    // Append at end regardless of sort — used during pre-population
                    // to maintain scan order for sequential processing.
                    _filteredImages.Add(row);
                }
                else
                {
                    _mutator.Insert(row);
                }
            }
        }
        finally
        {
            _batchCoalescer.EndBatch();
        }
        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Removes rows matching the predicate in a batch, coalescing UI notifications.
    /// Wraps the operation with <see cref="BeginBatch"/>/<see cref="EndBatch"/>.
    /// Also removes from _allImages and SelectedImages for consistency.
    /// </summary>
    public void RemoveRowsBatch(Func<ImageRowViewModel, bool> predicate)
    {
        _batchCoalescer.BeginBatch();
        try
        {
            IReadOnlyList<(ImageRowViewModel Row, int Index)> removed = _mutator.Remove(predicate);
            foreach ((ImageRowViewModel? row, int _) in removed)
            {
                (string SetName, long Id) key = (row.Image.SetName, row.Image.Id);
                _allImages.Remove(key);
                SelectedImages.Remove(row);
            }
        }
        finally
        {
            _batchCoalescer.EndBatch();
        }
        TotalImageCount = FilteredImages.Count;
    }

    /// <summary>
    /// Returns a comparison function based on the current sort column and direction.
    /// </summary>
    private Func<ImageRowViewModel, ImageRowViewModel, int> GetCurrentSortComparer()
    {
        bool descending = _sortDirection == SortDirection.Descending;

        Func<ImageRowViewModel, ImageRowViewModel, int> baseComparer = _sortColumn switch
        {
            "Name" => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            "System" => (a, b) => string.Compare(a.System ?? "", b.System ?? "", StringComparison.OrdinalIgnoreCase),
            "SetName" => (a, b) => string.Compare(a.SetName, b.SetName, StringComparison.OrdinalIgnoreCase),
            "Size" => (a, b) => a.Size.CompareTo(b.Size),
            "Format" => (a, b) => string.Compare(a.Format.ToString(), b.Format.ToString(), StringComparison.Ordinal),
            "Crc32" => (a, b) => a.Crc32.CompareTo(b.Crc32),
            "XxHash64" => (a, b) => a.XxHash64.CompareTo(b.XxHash64),
            "UniqueRawSize" => NullableCompare<long>(a => a.UniqueRawSize),
            "UniqueCompressedSize" => NullableCompare<long>(a => a.UniqueCompressedSize),
            "SharedRawSize" => NullableCompare<long>(a => a.SharedRawSize),
            "SharedCompressedSize" => NullableCompare<long>(a => a.SharedCompressedSize),
            "SavedSize" => NullableCompare<long>(a => a.SavedSize),
            "ReducedBy" => NullableCompare<double>(a => a.ReducedBy),
            "Removed" => (a, b) => (a.Removed ? 1 : 0).CompareTo(b.Removed ? 1 : 0),
            _ => (_, _) => 0
        };

        return descending
            ? (a, b) => -baseComparer(a, b)
            : baseComparer;
    }

    /// <summary>
    /// Creates a comparison function for nullable value-type properties with nulls sorted last.
    /// </summary>
    private static Func<ImageRowViewModel, ImageRowViewModel, int> NullableCompare<T>(
        Func<ImageRowViewModel, T?> selector) where T : struct, IComparable<T>
    {
        return (a, b) =>
        {
            T? x = selector(a);
            T? y = selector(b);
            if (x == null && y == null) return 0;
            if (x == null) return 1;  // nulls last
            if (y == null) return -1;
            return x.Value.CompareTo(y.Value);
        };
    }

    /// <summary>
    /// Clears stats for all images belonging to the specified session.
    /// Called when a Database_Session is closed to reset stats columns to blank.
    /// </summary>
    public void ClearStatsForSession(string sessionId)
    {
        foreach (ImageRowViewModel row in _allImages.Values)
        {
            if (string.Equals(row.SessionId, sessionId, StringComparison.Ordinal))
            {
                row.UniqueRawSize = null;
                row.UniqueCompressedSize = null;
                row.SharedRawSize = null;
                row.SharedCompressedSize = null;
                row.SavedSize = null;
                row.ReducedBy = null;
                row.BarStoredRatio = 0;
                row.BarCompressionRatio = 0;
                row.BarDedupRatio = 0;
                row.BarSharedCompressedRatio = 0;
                row.BarRemoveableRatio = 0;
            }
        }
    }

    public void Dispose()
    {
        _batchCoalescer.Dispose();
        _disposables.Dispose();
    }
}