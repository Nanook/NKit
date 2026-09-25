using NKDS.DatVerification;
using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Dat Verification Window.
/// Manages dat file loading, CRC verification against the active set,
/// result filtering/sorting, and batch rename of mismatched images.
/// </summary>
public sealed class DatVerificationWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MultipleDisposable _disposables = new();
    private readonly IDatVerificationService _datVerificationService;
    private readonly IConfigService _configService;
    private readonly IDataStoreService _dataStoreService;
    private readonly MainWindowViewModel _mainWindowViewModel;

    /// <summary>
    /// Loaded dat entries stored for re-verification when Active_Set changes.
    /// </summary>
    private IReadOnlyList<DatEntryInfo>? _loadedDatEntries;

    /// <summary>
    /// The full unfiltered result set from the last verification run.
    /// Summary totals are computed from this list and remain invariant to filtering.
    /// </summary>
    private IReadOnlyList<DatResultModel> _allResults = Array.Empty<DatResultModel>();

    // --- Dat file selection ---

    private string _datPathText = string.Empty;
    public string DatPathText
    {
        get => _datPathText;
        set => this.RaiseAndSetIfChanged(ref _datPathText, value);
    }

    public ObservableCollection<string> DatPathHistory { get; }

    private string? _datName;
    public string? DatName
    {
        get => _datName;
        private set => this.RaiseAndSetIfChanged(ref _datName, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    // --- Summary totals (invariant to filtering) ---

    private int _correctCount;
    public int CorrectCount
    {
        get => _correctCount;
        private set => this.RaiseAndSetIfChanged(ref _correctCount, value);
    }

    private int _missingCount;
    public int MissingCount
    {
        get => _missingCount;
        private set => this.RaiseAndSetIfChanged(ref _missingCount, value);
    }

    private int _badlyNamedCount;
    public int BadlyNamedCount
    {
        get => _badlyNamedCount;
        private set => this.RaiseAndSetIfChanged(ref _badlyNamedCount, value);
    }

    private int _wrongCrcCount;
    public int WrongCrcCount
    {
        get => _wrongCrcCount;
        private set => this.RaiseAndSetIfChanged(ref _wrongCrcCount, value);
    }

    private int _unmatchedCount;
    public int UnmatchedCount
    {
        get => _unmatchedCount;
        private set => this.RaiseAndSetIfChanged(ref _unmatchedCount, value);
    }

    // --- Filter toggles (all default true) ---

    private bool _showCorrect = true;
    public bool ShowCorrect
    {
        get => _showCorrect;
        set => this.RaiseAndSetIfChanged(ref _showCorrect, value);
    }

    private bool _showMissing = true;
    public bool ShowMissing
    {
        get => _showMissing;
        set => this.RaiseAndSetIfChanged(ref _showMissing, value);
    }

    private bool _showBadlyNamed = true;
    public bool ShowBadlyNamed
    {
        get => _showBadlyNamed;
        set => this.RaiseAndSetIfChanged(ref _showBadlyNamed, value);
    }

    private bool _showWrongCrc = true;
    public bool ShowWrongCrc
    {
        get => _showWrongCrc;
        set => this.RaiseAndSetIfChanged(ref _showWrongCrc, value);
    }

    private bool _showUnmatched = true;
    public bool ShowUnmatched
    {
        get => _showUnmatched;
        set => this.RaiseAndSetIfChanged(ref _showUnmatched, value);
    }

    // --- Results grid (filtered view) ---

    private IReadOnlyList<DatResultModel> _filteredResults = Array.Empty<DatResultModel>();
    public IReadOnlyList<DatResultModel> FilteredResults
    {
        get => _filteredResults;
        private set => this.RaiseAndSetIfChanged(ref _filteredResults, value);
    }

    // --- Sort state ---

    private string _sortColumn = "Status";
    public string SortColumn
    {
        get => _sortColumn;
        set => this.RaiseAndSetIfChanged(ref _sortColumn, value);
    }

    private SortDirection _sortDirection = SortDirection.Ascending;
    public SortDirection SortDirection
    {
        get => _sortDirection;
        set => this.RaiseAndSetIfChanged(ref _sortDirection, value);
    }

    // --- Rename enabled state ---

    private bool _hasBadlyNamed;
    public bool HasBadlyNamed
    {
        get => _hasBadlyNamed;
        private set => this.RaiseAndSetIfChanged(ref _hasBadlyNamed, value);
    }

    // --- Interactions ---

    /// <summary>
    /// Interaction to show a file picker for selecting a dat file.
    /// The View registers a handler that returns the selected file path, or null if cancelled.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFilePicker { get; } = new();

    // --- Commands ---

    public ReactiveCommand<RxVoid, RxVoid> BrowseCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RenameCommand { get; }

    public DatVerificationWindowViewModel(
        IDatVerificationService datVerificationService,
        IConfigService configService,
        IDataStoreService dataStoreService,
        MainWindowViewModel mainWindowViewModel)
    {
        _datVerificationService = datVerificationService;
        _configService = configService;
        _dataStoreService = dataStoreService;
        _mainWindowViewModel = mainWindowViewModel;

        // Initialize history from persisted config
        DatPathHistory = new ObservableCollection<string>(_configService.DatPathHistory);

        // --- Commands ---

        BrowseCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseAsync);
        BrowseCommand.DisposeWith(_disposables);

        IObservable<bool> canLoad = this.WhenAnyValue(x => x.DatPathText)
            .Select(path => !string.IsNullOrWhiteSpace(path));
        LoadCommand = ReactiveCommand.Create(ExecuteLoad, canLoad);
        LoadCommand.DisposeWith(_disposables);

        IObservable<bool> canRename = this.WhenAnyValue(x => x.HasBadlyNamed);
        RenameCommand = ReactiveCommand.Create(ExecuteRename, canRename);
        RenameCommand.DisposeWith(_disposables);

        // Subscribe to filter toggle changes — reapply filters and sort when any toggle changes
        this.WhenAnyValue(
                x => x.ShowCorrect,
                x => x.ShowMissing,
                x => x.ShowBadlyNamed,
                x => x.ShowWrongCrc,
                x => x.ShowUnmatched)
            .Skip(1) // Skip the initial value emission
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => ApplyFiltersAndSort())
            .DisposeWith(_disposables);

        // Subscribe to sort state changes — reapply sort when column or direction changes
        this.WhenAnyValue(
                x => x.SortColumn,
                x => x.SortDirection)
            .Skip(1) // Skip the initial value emission
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => ApplyFiltersAndSort())
            .DisposeWith(_disposables);

        // Subscribe to Active_Set changes — re-run verification when the selected set changes
        _mainWindowViewModel.Toolbar.WhenAnyValue(x => x.SelectedSetName)
            .Skip(1) // Skip the initial value emission
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (_loadedDatEntries != null)
                    RunVerification();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Opens a file picker filtered to .dat files. On selection, sets DatPathText.
    /// </summary>
    private async Task ExecuteBrowseAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            DatPathText = path;
    }

    /// <summary>
    /// Loads the dat file at DatPathText. On success: sets DatName, clears ErrorMessage,
    /// adds path to history, stores entries, and runs verification.
    /// On failure: sets ErrorMessage.
    /// </summary>
    private void ExecuteLoad()
    {
        string path = DatPathText;
        if (string.IsNullOrWhiteSpace(path))
            return;

        DatLoadResult result = _datVerificationService.LoadDat(path);

        if (result.Success)
        {
            DatName = result.DatName;
            ErrorMessage = null;
            _loadedDatEntries = result.Entries;

            // Add to MRU history and update the observable collection
            _configService.AddDatPath(path);
            DatPathHistory.Clear();
            foreach (string historyPath in _configService.DatPathHistory)
                DatPathHistory.Add(historyPath);

            // Run verification against the active set
            RunVerification();
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    /// <summary>
    /// Renames all BadlyNamed images to match their corresponding dat entry names.
    /// Preserves the image's current file extension. Skips renames where the target
    /// name already exists (conflict). Reports failures via ErrorMessage.
    /// </summary>
    private void ExecuteRename()
    {
        // Collect all BadlyNamed results (scoped to Active_Set since _allResults only contains Active_Set images)
        List<DatResultModel> badlyNamed = _allResults
            .Where(r => r.Status == DatVerificationStatus.BadlyNamed && r.ImageId.HasValue && r.SetName != null)
            .ToList();

        if (badlyNamed.Count == 0)
            return;

        // Build a set of all current image names in the active set for conflict detection
        IReadOnlyList<ImageRowViewModel> allImageRows = _mainWindowViewModel.ImageList.GetAllImages();
        string? activeSetName = _mainWindowViewModel.Toolbar.SelectedSetName;

        IEnumerable<ImageRowViewModel> scopedRows;
        if (activeSetName == null || string.Equals(activeSetName, "All", StringComparison.OrdinalIgnoreCase))
            scopedRows = allImageRows;
        else
            scopedRows = allImageRows.Where(row => string.Equals(row.SetName, activeSetName, StringComparison.Ordinal));

        // Materialise for repeated lookups
        List<ImageRowViewModel> scopedRowsList = scopedRows.ToList();

        HashSet<string> existingNames = new HashSet<string>(
            scopedRowsList.Select(r => r.Name),
            StringComparer.OrdinalIgnoreCase);

        // Name → row lookup for conflict CRC check (Bug 2: same-CRC duplicate detection)
        Dictionary<string, ImageRowViewModel> nameToRow = scopedRowsList
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Get current sessions for DataStore access
        List<ImageSessionModel> sessions = new List<ImageSessionModel>();
        using (IDisposable sub = _dataStoreService.Sessions.Subscribe(s => sessions = s.ToList())) { }

        List<string> failures = new List<string>();
        Dictionary<int, string> renamedIndices = new Dictionary<int, string>(); // index in _allResults → new target name

        // Titles already renamed this pass (set + new base). A WiiU multi-TMD title produces several
        // BadlyNamed results (one per [tmd.N] child); the FIRST renames the whole title group via
        // RenameImageTitle, so siblings must be skipped here (and reclassified below) rather than
        // re-renamed — which would otherwise trip the conflict check against the just-added base name.
        HashSet<(string Set, string Base)> renamedTitles = new HashSet<(string, string)>();

        for (int i = 0; i < _allResults.Count; i++)
        {
            DatResultModel result = _allResults[i];
            if (result.Status != DatVerificationStatus.BadlyNamed || !result.ImageId.HasValue || result.SetName == null)
                continue;

            // Target name is the dat entry name directly — dat names are plain game titles with
            // no file extensions, and stored image names also have no extension, so no stripping needed.
            // Using Path.GetFileNameWithoutExtension would incorrectly truncate at dots in the title
            // (e.g. "Super Mario Bros. Wii" → "Super Mario Bros", "(v1.06B)" → "(v1").
            string targetName = result.DatEntryName ?? "";
            string datEntryStem = targetName; // same thing — kept for clarity in the title-group logic

            // If this result is a sibling child of a title already renamed this pass, don't rename
            // again — just reclassify it (its record was already renamed by RenameImageTitle).
            if (renamedTitles.Contains((result.SetName, datEntryStem)))
            {
                renamedIndices[i] = targetName;
                continue;
            }

            // Check for conflict: target name already exists as another image.
            // If the conflicting entry has the SAME CRC as the badly-named image it is a redundant
            // duplicate (add → rename → re-add scenario). Delete the duplicate by ID rather than
            // failing — the correctly-named entry already exists, so the duplicate is just noise.
            if (existingNames.Contains(targetName) &&
                !string.Equals(targetName, result.ImageName, StringComparison.OrdinalIgnoreCase))
            {
                // Look up the conflicting image's CRC to decide whether it's a same-CRC duplicate
                nameToRow.TryGetValue(targetName, out ImageRowViewModel? conflicting);
                bool isSameCrcDuplicate = conflicting != null && conflicting.Crc32 == result.Crc32;

                if (!isSameCrcDuplicate)
                {
                    failures.Add($"'{result.ImageName}': Target name '{targetName}' already exists with a different CRC");
                    continue;
                }

                // Same CRC: the badly-named entry is a redundant duplicate. Delete it by ID and
                // mark the result Correct (the correctly-named entry already has the right data).
                ImageSessionModel? delSession = FindSessionForSet(sessions, result.SetName);
                if (delSession != null)
                {
                    try { delSession.DataStore.DeleteImage(new GlobalImageKey(result.SetName, result.ImageId.Value)); }
                    catch (Exception ex)
                    {
                        failures.Add($"'{result.ImageName}': Could not remove duplicate: {ex.Message}");
                        continue;
                    }
                }

                // Remove from UI rows and tracking set
                existingNames.Remove(result.ImageName ?? "");
                renamedIndices[i] = targetName; // reclassify as Correct with the existing target name
                continue;
            }

            // Find the session that owns this set
            ImageSessionModel? session = FindSessionForSet(sessions, result.SetName);
            if (session == null)
            {
                failures.Add($"'{result.ImageName}': No open session found for set '{result.SetName}'");
                continue;
            }

            try
            {
                GlobalImageKey key = new GlobalImageKey(result.SetName, result.ImageId.Value);

                // Rename the whole TITLE, not just this one record. For a WiiU multi-TMD title the
                // datastore has a TmdAppFolder umbrella + several "[tmd.N]" children; RenameImageTitle
                // renames the umbrella to the new base AND every child to "{base} [tmd.N]" (preserving
                // each index) in one transaction, so the mount recombine stays intact. For a plain
                // image it renames just that one record. The dat entry stem is the clean BASE title
                // (dat has one entry per title, no [tmd.N]); RenameImageTitle re-applies the suffixes.
                string targetBase = datEntryStem;
                session.DataStore.RenameImageTitle(key, targetBase);
                renamedTitles.Add((result.SetName, targetBase));

                // Update the existing name set: remove old, add new
                if (result.ImageName != null)
                    existingNames.Remove(result.ImageName);
                existingNames.Add(targetName);

                renamedIndices[i] = targetName;

                // Refresh the ImageRowViewModel(s) from the store for EVERY record the title rename
                // touched (umbrella + all [tmd.N] children), not just this one — otherwise siblings
                // keep their stale names in the UI and would be re-processed as still-badly-named.
                foreach (ImageRecord renamed in session.DataStore.ListImagesInSet(result.SetName)
                             .Where(r => string.Equals(NKitDataStore.DataStore.ExtractBaseName(r.Name) ?? r.Name, targetBase, StringComparison.OrdinalIgnoreCase)))
                {
                    ImageRowViewModel? row = allImageRows.FirstOrDefault(r => r.Id == renamed.Id && r.SetName == result.SetName);
                    if (row != null)
                        row.Image.Name = renamed.Name;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"'{result.ImageName}': {ex.Message}");
            }
        }

        // Reclassify successfully renamed results as Correct with the new name
        if (renamedIndices.Count > 0)
        {
            List<DatResultModel> updatedResults = _allResults.ToList();
            foreach ((int idx, string? targetName) in renamedIndices)
            {
                DatResultModel original = updatedResults[idx];
                updatedResults[idx] = new DatResultModel
                {
                    Status = DatVerificationStatus.Correct,
                    DatEntryName = original.DatEntryName,
                    ImageName = targetName,
                    Crc32 = original.Crc32,
                    ImageId = original.ImageId,
                    SetName = original.SetName
                };
            }

            _allResults = updatedResults;

            // Recompute summary totals
            CorrectCount = _allResults.Count(r => r.Status == DatVerificationStatus.Correct);
            MissingCount = _allResults.Count(r => r.Status == DatVerificationStatus.Missing);
            BadlyNamedCount = _allResults.Count(r => r.Status == DatVerificationStatus.BadlyNamed);
            WrongCrcCount = _allResults.Count(r => r.Status == DatVerificationStatus.WrongCrc);
            UnmatchedCount = _allResults.Count(r => r.Status == DatVerificationStatus.Unmatched);
            HasBadlyNamed = BadlyNamedCount > 0;

            // Reapply filters and sort
            ApplyFiltersAndSort();
        }

        // Set or clear error message
        if (failures.Count > 0)
        {
            ErrorMessage = $"Rename failed for {failures.Count} image(s):\n" +
                           string.Join("\n", failures);
        }
        else
        {
            ErrorMessage = null;
        }
    }

    /// <summary>
    /// Finds the session that owns the given set name.
    /// </summary>
    private static ImageSessionModel? FindSessionForSet(IReadOnlyList<ImageSessionModel> sessions, string setName)
    {
        foreach (ImageSessionModel session in sessions)
        {
            if (session.Images.Any(img => img.SetName == setName))
                return session;

            try
            {
                if (session.DataStore.ListSetNames().Contains(setName))
                    return session;
            }
            catch { /* ignore */ }
        }

        return sessions.Count > 0 ? sessions[0] : null;
    }

    /// <summary>
    /// Runs verification of the loaded dat entries against the current Active_Set images.
    /// Updates summary totals, filtered results, and HasBadlyNamed state.
    /// </summary>
    internal void RunVerification()
    {
        if (_loadedDatEntries == null)
            return;

        // Build ImageInfo list from Active_Set images via MainWindowViewModel
        string? activeSetName = _mainWindowViewModel.Toolbar.SelectedSetName;
        IReadOnlyList<ImageRowViewModel> allImageRows = _mainWindowViewModel.ImageList.GetAllImages();

        IEnumerable<ImageRowViewModel> scopedRows;
        if (activeSetName == null || string.Equals(activeSetName, "All", StringComparison.OrdinalIgnoreCase))
        {
            // "All" — include all images
            scopedRows = allImageRows;
        }
        else
        {
            // Specific set — include only images from the active set
            scopedRows = allImageRows
                .Where(row => string.Equals(row.SetName, activeSetName, StringComparison.Ordinal));
        }

        List<ImageInfo> images = scopedRows.Select(row => new ImageInfo
        {
            Id = row.Id,
            Name = row.Name,
            // Stored image names have no file extension — use the full name as the stem so that
            // matching against dat entry names (also extension-free) works correctly.
            NameStem = row.Name,
            Crc32 = row.Crc32,
            SetName = row.SetName
        }).ToList();

        // Run verification
        _allResults = _datVerificationService.Verify(_loadedDatEntries, images);

        // Compute summary totals from the full (unfiltered) result set
        CorrectCount = _allResults.Count(r => r.Status == DatVerificationStatus.Correct);
        MissingCount = _allResults.Count(r => r.Status == DatVerificationStatus.Missing);
        BadlyNamedCount = _allResults.Count(r => r.Status == DatVerificationStatus.BadlyNamed);
        WrongCrcCount = _allResults.Count(r => r.Status == DatVerificationStatus.WrongCrc);
        UnmatchedCount = _allResults.Count(r => r.Status == DatVerificationStatus.Unmatched);
        HasBadlyNamed = BadlyNamedCount > 0;

        // Apply current filters and sort to produce FilteredResults
        ApplyFiltersAndSort();
    }

    /// <summary>
    /// Sorts the results grid by the specified column.
    /// If the column is already the current sort column, toggles direction.
    /// Otherwise sets the new column with ascending direction.
    /// Called from the view when column headers are clicked.
    /// </summary>
    public void SortByColumn(string columnName)
    {
        if (string.IsNullOrEmpty(columnName))
            return;

        if (string.Equals(_sortColumn, columnName, StringComparison.Ordinal))
        {
            // Toggle direction on repeated click
            SortDirection = _sortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        else
        {
            // New column: default to ascending
            SortColumn = columnName;
            SortDirection = SortDirection.Ascending;
        }
    }

    /// <summary>
    /// Applies the current filter toggles and sort state to _allResults,
    /// producing the FilteredResults list. Summary totals are NOT recomputed
    /// here — they remain invariant to filter changes.
    /// </summary>
    internal void ApplyFiltersAndSort()
    {
        // Phase 1: Filter — include results whose status matches any enabled filter toggle
        IEnumerable<DatResultModel> filtered = _allResults.Where(r => r.Status switch
        {
            DatVerificationStatus.Correct => _showCorrect,
            DatVerificationStatus.Missing => _showMissing,
            DatVerificationStatus.BadlyNamed => _showBadlyNamed,
            DatVerificationStatus.WrongCrc => _showWrongCrc,
            DatVerificationStatus.Unmatched => _showUnmatched,
            _ => true
        });

        // Phase 2: Sort by SortColumn in SortDirection order
        bool descending = _sortDirection == SortDirection.Descending;

        IReadOnlyList<DatResultModel> sorted = _sortColumn switch
        {
            "Status" => OrderBy(filtered, r => (int)r.Status, descending),
            "DatEntryName" => OrderByString(filtered, r => r.DatEntryName, descending),
            "ImageName" => OrderByString(filtered, r => r.ImageName, descending),
            "Crc32" => OrderBy(filtered, r => r.Crc32, descending),
            _ => filtered.ToList()
        };

        FilteredResults = sorted;
    }

    private static IReadOnlyList<DatResultModel> OrderBy<TKey>(
        IEnumerable<DatResultModel> source,
        Func<DatResultModel, TKey> keySelector,
        bool descending)
    {
        return descending
            ? source.OrderByDescending(keySelector).ToList()
            : source.OrderBy(keySelector).ToList();
    }

    /// <summary>
    /// Null-safe string sort: nulls sort first (before any non-null value) in ascending order.
    /// </summary>
    private static IReadOnlyList<DatResultModel> OrderByString(
        IEnumerable<DatResultModel> source,
        Func<DatResultModel, string?> keySelector,
        bool descending)
    {
        return source.OrderBy(r => r, Comparer<DatResultModel>.Create((a, b) =>
        {
            string? x = keySelector(a);
            string? y = keySelector(b);
            if (x == null && y == null) return 0;
            if (x == null) return descending ? 1 : -1;  // nulls first in ascending
            if (y == null) return descending ? -1 : 1;
            int result = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            return descending ? -result : result;
        })).ToList();
    }

    public void Dispose() => _disposables.Dispose();
}