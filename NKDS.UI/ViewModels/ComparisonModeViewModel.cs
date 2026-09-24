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
/// Orchestrates the Comparison Mode lifecycle: activation, deactivation, toolbar button states,
/// progress/cancellation for loading and export, and coordination between services and child ViewModels.
/// </summary>
public class ComparisonModeViewModel : ViewModelBase, IDisposable
{
    private readonly IHashCacheService _hashCacheService;
    private readonly ISimilarityGroupService _similarityGroupService;
    private readonly IThresholdGroupingService _thresholdGroupingService;
    private readonly ImageListViewModel _imageList;
    private readonly ComparisonPanelViewModel _comparisonPanel;
    private readonly MultipleDisposable _disposables = new();

    private CancellationTokenSource? _loadingCts;
    private CancellationTokenSource? _topMatchesCts;
    private CancellationTokenSource? _groupingCts;

    // State backing fields
    private bool _isActive;
    private bool _isLoading;
    private bool _isExporting;
    private int _loadedCount;
    private int _totalToLoad;
    private int _failedCount;
    private string? _warningMessage;
    private string? _errorMessage;
    private string _statusText = string.Empty;
    private double _thresholdPercent = 80.0;
    private int _maxDegreeOfParallelism = Environment.ProcessorCount;
    private bool _isComputingGroups;
    private double _groupingProgress;

    /// <summary>
    /// Whether Comparison Mode is currently active (hash cache loaded, working set applied).
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    /// <summary>
    /// Whether BlockKey loading is currently in progress.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// Whether a similarity group export is currently in progress.
    /// </summary>
    public bool IsExporting
    {
        get => _isExporting;
        set => this.RaiseAndSetIfChanged(ref _isExporting, value);
    }

    /// <summary>
    /// Number of images loaded so far during the current loading operation.
    /// </summary>
    public int LoadedCount
    {
        get => _loadedCount;
        set => this.RaiseAndSetIfChanged(ref _loadedCount, value);
    }

    /// <summary>
    /// Total number of images to load during the current loading operation.
    /// </summary>
    public int TotalToLoad
    {
        get => _totalToLoad;
        set => this.RaiseAndSetIfChanged(ref _totalToLoad, value);
    }

    /// <summary>
    /// Number of images that failed to load during the last loading operation.
    /// </summary>
    public int FailedCount
    {
        get => _failedCount;
        set => this.RaiseAndSetIfChanged(ref _failedCount, value);
    }

    /// <summary>
    /// Warning message displayed after partial load failures.
    /// </summary>
    public string? WarningMessage
    {
        get => _warningMessage;
        set => this.RaiseAndSetIfChanged(ref _warningMessage, value);
    }

    /// <summary>
    /// Error message displayed when operations fail completely.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Status text for the Comparison Mode indicator (e.g., "Comparison Mode: 50 images loaded (3 selected)").
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    /// <summary>
    /// The threshold percentage for grouping (0–100, default 80).
    /// </summary>
    public double ThresholdPercent
    {
        get => _thresholdPercent;
        set => this.RaiseAndSetIfChanged(ref _thresholdPercent, ClampThreshold(value));
    }

    /// <summary>
    /// Max threads for pairwise computation. Default = Environment.ProcessorCount.
    /// Constrained to [1, Environment.ProcessorCount * 2].
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => this.RaiseAndSetIfChanged(ref _maxDegreeOfParallelism, ClampThreads(value));
    }

    /// <summary>
    /// Whether a threshold grouping computation is in progress.
    /// </summary>
    public bool IsComputingGroups
    {
        get => _isComputingGroups;
        set => this.RaiseAndSetIfChanged(ref _isComputingGroups, value);
    }

    /// <summary>
    /// Progress of the grouping computation (0.0 to 1.0).
    /// </summary>
    public double GroupingProgress
    {
        get => _groupingProgress;
        set => this.RaiseAndSetIfChanged(ref _groupingProgress, value);
    }

    // Commands

    /// <summary>
    /// Activates Comparison Mode by loading BlockKeys for the working set.
    /// CanExecute: not already active, not currently loading, and image list is non-empty.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> EnterComparisonModeCommand { get; }

    /// <summary>
    /// Deactivates Comparison Mode, releasing the hash cache and restoring the full image list.
    /// CanExecute: Comparison Mode is active.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ExitComparisonModeCommand { get; }

    /// <summary>
    /// Exports similarity groups as YAML via a save file dialog.
    /// CanExecute: Comparison Mode is active and no export is in progress.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportSimilarityGroupsCommand { get; }

    /// <summary>
    /// Finds the top 10 closest matches for the single selected image.
    /// CanExecute: Comparison Mode is active, exactly one image is selected, and that image is in the hash cache.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> FindTopMatchesCommand { get; }

    /// <summary>
    /// Cancels the current loading operation.
    /// CanExecute: loading is in progress.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelLoadingCommand { get; }

    /// <summary>
    /// Triggers threshold grouping computation.
    /// CanExecute: IsActive &amp;&amp; !IsComputingGroups &amp;&amp; Cache.Count >= 2
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ComputeGroupsCommand { get; }

    /// <summary>
    /// Cancels the in-progress grouping computation.
    /// CanExecute: IsComputingGroups
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelGroupingCommand { get; }

    // Interactions

    /// <summary>
    /// Interaction to show a save file dialog for YAML export.
    /// Input is the suggested file extension filter, output is the selected path or null if cancelled.
    /// </summary>
    public Interaction<string, string?> ShowSaveFileDialog { get; } = new();

    /// <summary>
    /// Interaction to show the ThresholdGroupsDialog.
    /// </summary>
    public Interaction<ThresholdGroupsDialogViewModel, RxVoid> ShowGroupsDialog { get; } = new();

    /// <summary>
    /// The ImageListViewModel for accessing selection and image counts.
    /// </summary>
    public ImageListViewModel ImageList => _imageList;

    public ComparisonModeViewModel(
        IHashCacheService hashCacheService,
        ISimilarityGroupService similarityGroupService,
        IThresholdGroupingService thresholdGroupingService,
        ImageListViewModel imageList,
        ComparisonPanelViewModel comparisonPanel)
    {
        _hashCacheService = hashCacheService;
        _similarityGroupService = similarityGroupService;
        _thresholdGroupingService = thresholdGroupingService;
        _imageList = imageList;
        _comparisonPanel = comparisonPanel;

        // EnterComparisonModeCommand: !IsActive && !IsLoading && ImageList.TotalImageCount > 0
        IObservable<bool> canEnter = this.WhenAnyValue(
                x => x.IsActive,
                x => x.IsLoading,
                x => x._imageList.TotalImageCount,
                (isActive, isLoading, totalCount) => !isActive && !isLoading && totalCount > 0);

        EnterComparisonModeCommand = ReactiveCommand.CreateFromTask(
            ExecuteEnterComparisonModeAsync, canEnter);
        EnterComparisonModeCommand.DisposeWith(_disposables);

        // ExitComparisonModeCommand: IsActive
        IObservable<bool> canExit = this.WhenAnyValue(x => x.IsActive);

        ExitComparisonModeCommand = ReactiveCommand.CreateFromTask(
            ExecuteExitComparisonModeAsync, canExit);
        ExitComparisonModeCommand.DisposeWith(_disposables);

        // ExportSimilarityGroupsCommand: IsActive && !IsExporting
        IObservable<bool> canExport = this.WhenAnyValue(
            x => x.IsActive,
            x => x.IsExporting,
            (isActive, isExporting) => isActive && !isExporting);

        ExportSimilarityGroupsCommand = ReactiveCommand.CreateFromTask(
            ExecuteExportSimilarityGroupsAsync, canExport);
        ExportSimilarityGroupsCommand.DisposeWith(_disposables);

        // FindTopMatchesCommand: IsActive && SelectedImageCount >= 1 && at least one selected image is in Hash_Cache
        IObservable<bool> canFindTopMatches = this.WhenAnyValue(x => x.IsActive)
            .CombineLatest(
                _imageList.WhenAnyValue(x => x.SelectedImageCount),
                _hashCacheService.CacheChanged.StartWith(_hashCacheService.Cache),
                _imageList.WhenAnyValue(x => x.SelectedImages),
                (isActive, selectedCount, cache, selectedImages) =>
                {
                    if (!isActive || selectedCount < 1)
                        return false;

                    // At least one selected image must be in the cache
                    return selectedImages.Any(row => cache.ContainsKey(row.Image));
                });

        FindTopMatchesCommand = ReactiveCommand.CreateFromTask(
            ExecuteFindTopMatchesAsync, canFindTopMatches);
        FindTopMatchesCommand.DisposeWith(_disposables);

        // CancelLoadingCommand: IsLoading
        IObservable<bool> canCancel = this.WhenAnyValue(x => x.IsLoading);

        CancelLoadingCommand = ReactiveCommand.Create(ExecuteCancelLoading, canCancel);
        CancelLoadingCommand.DisposeWith(_disposables);

        // ComputeGroupsCommand: IsActive && !IsComputingGroups && Cache.Count >= 2
        IObservable<bool> canComputeGroups = this.WhenAnyValue(x => x.IsActive, x => x.IsComputingGroups)
            .CombineLatest(
                _hashCacheService.CacheChanged.StartWith(_hashCacheService.Cache),
                (state, cache) => state.Item1 && !state.Item2 && cache.Count >= 2);

        ComputeGroupsCommand = ReactiveCommand.CreateFromTask(
            ExecuteComputeGroupsAsync, canComputeGroups);
        ComputeGroupsCommand.DisposeWith(_disposables);

        // CancelGroupingCommand: IsComputingGroups
        IObservable<bool> canCancelGrouping = this.WhenAnyValue(x => x.IsComputingGroups);

        CancelGroupingCommand = ReactiveCommand.Create(
            () => { _groupingCts?.Cancel(); }, canCancelGrouping);
        CancelGroupingCommand.DisposeWith(_disposables);

        // Update StatusText reactively based on IsActive, TotalImageCount, and SelectedImageCount
        this.WhenAnyValue(
                x => x.IsActive,
                x => x._imageList.TotalImageCount,
                x => x._imageList.SelectedImageCount)
            .Subscribe(tuple =>
            {
                (bool isActive, int totalCount, int selectedCount) = tuple;
                StatusText = isActive
                    ? $"Comparison Mode: {totalCount} images loaded ({selectedCount} selected)"
                    : string.Empty;
            })
            .DisposeWith(_disposables);

        // When scope restrictions change while in Comparison Mode, clear displayed results
        // and reset to idle state (requirement 8.3)
        _comparisonPanel.WhenAnyValue(x => x.RestrictToSameSet, x => x.RestrictToSameSystem)
            .Skip(1) // Skip the initial value emission
            .Where(_ => IsActive)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                _comparisonPanel.CancelAndClear();
                ErrorMessage = null;
                WarningMessage = null;
            })
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Clamps a threshold value to [0, 100] and rounds to 2 decimal places.
    /// </summary>
    internal static double ClampThreshold(double value)
    {
        if (value < 0.0) return 0.0;
        if (value > 100.0) return 100.0;
        return Math.Round(value, 2);
    }

    /// <summary>
    /// Clamps thread count to [1, Environment.ProcessorCount * 2].
    /// </summary>
    internal static int ClampThreads(int value)
    {
        int max = Environment.ProcessorCount * 2;
        if (value < 1) return 1;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Determines the Working_Set: selected images if selection is non-empty, else all visible images.
    /// Returns the underlying ImageRecord instances for use with the hash cache.
    /// </summary>
    internal IReadOnlyList<ImageRecord> DetermineWorkingSet()
    {
        ObservableCollection<ImageRowViewModel> selectedImages = _imageList.SelectedImages;
        if (selectedImages.Count > 0)
        {
            return selectedImages.Select(row => row.Image).ToList();
        }

        return _imageList.FilteredImages.Select(row => row.Image).ToList();
    }

    // Command Execute stubs — actual logic will be implemented in tasks 7.3-7.6

    private async Task ExecuteEnterComparisonModeAsync()
    {
        // 1. Determine the Working_Set
        IReadOnlyList<ImageRecord> workingSet = DetermineWorkingSet();

        // 2. Create a new CancellationTokenSource
        _loadingCts?.Dispose();
        _loadingCts = new CancellationTokenSource();
        CancellationToken ct = _loadingCts.Token;

        // 3. Set loading state
        IsLoading = true;
        TotalToLoad = workingSet.Count;
        LoadedCount = 0;

        // 4. Clear any previous warning/error messages
        WarningMessage = null;
        ErrorMessage = null;

        try
        {
            // 5. Call LoadAsync with progress handler
            Progress<(int loaded, int total)> progress = new Progress<(int loaded, int total)>(p =>
            {
                LoadedCount = p.loaded;
            });

            HashCacheLoadResult result = await _hashCacheService.LoadAsync(workingSet, ct, progress);

            // 6. On success (at least some images loaded)
            if (result.SuccessCount > 0)
            {
                IsActive = true;
                _comparisonPanel.IsInComparisonMode = true;
                _imageList.SetWorkingSet(_hashCacheService.Cache.Keys.ToList());

                // Partial failure: set warning
                if (result.FailedCount > 0)
                {
                    WarningMessage = $"{result.FailedCount} image(s) failed to load and were skipped.";
                    FailedCount = result.FailedCount;
                }
            }
            else
            {
                // 7. Total failure (no images loaded successfully)
                ErrorMessage = "All images failed to load. Cannot enter Comparison Mode.";
            }
        }
        catch (OperationCanceledException)
        {
            // 8. On cancellation: remain in non-comparison state
            // HashCacheService discards partial data on cancellation internally
        }
        finally
        {
            // 9. Set IsLoading = false, dispose the CTS
            IsLoading = false;
            _loadingCts?.Dispose();
            _loadingCts = null;
        }
    }

    private Task ExecuteExitComparisonModeAsync()
    {
        // 1. Cancel any in-progress loading operation
        _loadingCts?.Cancel();

        // 1b. Cancel any in-progress top-10 computation
        _topMatchesCts?.Cancel();
        _topMatchesCts?.Dispose();
        _topMatchesCts = null;

        // 1c. Cancel any in-progress threshold grouping computation
        _groupingCts?.Cancel();
        _groupingCts?.Dispose();
        _groupingCts = null;

        // 2. Clear the hash cache (releases all BlockKey data from memory)
        _hashCacheService.Clear();

        // 3. Restore the full image list by clearing the working set
        _imageList.ClearWorkingSet();

        // 4. Clear comparison panel results
        _comparisonPanel.CancelAndClear();

        // 5. Deactivate Comparison Mode
        IsActive = false;
        _comparisonPanel.IsInComparisonMode = false;

        // 6. Clear warning and error messages
        WarningMessage = null;
        ErrorMessage = null;

        // 7. Reset status text (will also be updated reactively via the subscription,
        //    but explicitly clearing ensures immediate consistency)
        StatusText = string.Empty;

        return Task.CompletedTask;
    }

    private async Task ExecuteExportSimilarityGroupsAsync()
    {
        // Step 1: Show save file dialog — if user cancels, return
        string? filePath = await ShowSaveFileDialog.Handle(".yaml");
        if (filePath is null)
            return;

        IsExporting = true;
        ErrorMessage = null;

        try
        {
            // Step 2: Determine scope restriction from comparison panel
            ScopeRestriction scopeRestriction = ScopeRestriction.None;
            if (_comparisonPanel.RestrictToSameSet)
                scopeRestriction |= ScopeRestriction.SameSet;
            if (_comparisonPanel.RestrictToSameSystem)
                scopeRestriction |= ScopeRestriction.SameSystem;

            // Step 3: Compute similarity groups using the hash cache
            using CancellationTokenSource cts = new CancellationTokenSource();
            Progress<double> progress = new Progress<double>();

            List<SimilarityGroupModel> groups = await _similarityGroupService.ComputeGroupsAsync(
                _hashCacheService.Cache,
                scopeRestriction,
                null,
                cts.Token,
                progress);

            // Step 4: Handle results
            if (groups.Count == 0)
            {
                // All images are singletons — show informational message, do not write file
                StatusText = "No similarity groups detected — all images are unique.";
            }
            else
            {
                // Write YAML and show success
                await _similarityGroupService.WriteYamlAsync(groups, filePath, cts.Token);
                StatusText = $"Exported {groups.Count} similarity group(s) to {System.IO.Path.GetFileName(filePath)}.";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — return silently
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Failed to write file: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task ExecuteFindTopMatchesAsync()
    {
        List<ImageRowViewModel> selectedRows = _imageList.SelectedImages.ToList();
        List<ImageRecord> selectedImages = selectedRows.Select(row => row.Image).ToList();

        // 1. Union all BlockKeys from selected images into a combined reference set
        HashSet<BlockKey> combinedBlockKeys = new HashSet<BlockKey>();
        List<ImageRecord> selectedInCache = new List<ImageRecord>();

        foreach (ImageRecord? image in selectedImages)
        {
            HashSet<BlockKey>? blockKeys = _hashCacheService.GetBlockKeys(image);
            if (blockKeys != null && blockKeys.Count > 0)
            {
                combinedBlockKeys.UnionWith(blockKeys);
                selectedInCache.Add(image);
            }
        }

        // 2. If no BlockKeys found: show "comparison not possible" message, return
        if (combinedBlockKeys.Count == 0)
        {
            _comparisonPanel.CancelAndClear();
            ErrorMessage = "Comparison not possible: the selected image(s) have no block data.";
            return;
        }

        // 3. Get the current scope restriction from the comparison panel
        ScopeRestriction scopeRestriction = ScopeRestriction.None;
        if (_comparisonPanel.RestrictToSameSet)
            scopeRestriction |= ScopeRestriction.SameSet;
        if (_comparisonPanel.RestrictToSameSystem)
            scopeRestriction |= ScopeRestriction.SameSystem;

        // 4. Filter candidates: all cached images except the selected ones, filtered by scope
        HashSet<ImageRecord> selectedSet = new HashSet<ImageRecord>(selectedImages);
        IEnumerable<ImageRecord> allCachedImages = _hashCacheService.Cache.Keys;
        IEnumerable<ImageRecord> candidates = allCachedImages.Where(img => !selectedSet.Contains(img));

        // For scope restriction with multi-select, use the first selected image as reference for scope
        if (scopeRestriction.HasFlag(ScopeRestriction.SameSet))
        {
            HashSet<string> refSetNames = selectedInCache.Select(img => img.SetName).ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(img => refSetNames.Contains(img.SetName));
        }

        if (scopeRestriction.HasFlag(ScopeRestriction.SameSystem))
        {
            HashSet<string?> refSystems = selectedInCache.Select(img => img.System).ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(img => refSystems.Contains(img.System));
        }

        List<ImageRecord> candidateList = candidates.ToList();

        // 5. If no candidates match scope: show message, return
        if (candidateList.Count == 0)
        {
            _comparisonPanel.CancelAndClear();
            ErrorMessage = "No candidates matched the active scope restriction.";
            return;
        }

        // 6. Cancel any previous top-10 computation
        _topMatchesCts?.Cancel();
        _topMatchesCts?.Dispose();

        // 7. Create new CTS and compute top matches against the combined BlockKey set
        _topMatchesCts = new CancellationTokenSource();
        CancellationToken ct = _topMatchesCts.Token;

        ErrorMessage = null;

        try
        {
            List<ComparisonResultModel> results = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                int referenceBlockCount = combinedBlockKeys.Count;
                List<ComparisonResultModel> matchResults = new List<ComparisonResultModel>();

                for (int i = 0; i < candidateList.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    ImageRecord candidate = candidateList[i];
                    HashSet<BlockKey>? candidateBlockKeys = _hashCacheService.GetBlockKeys(candidate);
                    if (candidateBlockKeys == null)
                        continue;

                    int sharedCount = 0;
                    foreach (BlockKey key in candidateBlockKeys)
                    {
                        if (combinedBlockKeys.Contains(key))
                            sharedCount++;
                    }

                    if (sharedCount > 0)
                    {
                        double matchPercentage = (double)sharedCount / referenceBlockCount * 100.0;
                        matchResults.Add(new ComparisonResultModel
                        {
                            MatchedImage = candidate,
                            MatchPercentage = matchPercentage,
                            SharedBlockCount = sharedCount,
                            ReferenceBlockCount = referenceBlockCount
                        });
                    }
                }

                return matchResults
                    .OrderByDescending(r => r.MatchPercentage)
                    .ThenBy(r => r.MatchedImage.Name, StringComparer.Ordinal)
                    .Take(10)
                    .ToList();
            }, ct);

            // 8. Update comparison panel with results — use first selected image as reference display
            _comparisonPanel.SetComparisonModeResults(selectedInCache.First(), results);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: return silently
        }
    }

    private void ExecuteCancelLoading() => _loadingCts?.Cancel();

    private async Task ExecuteComputeGroupsAsync()
    {
        // 1. Set computing state and reset progress
        IsComputingGroups = true;
        GroupingProgress = 0;
        ErrorMessage = null;

        // 2. Create a new CancellationTokenSource
        _groupingCts?.Cancel();
        _groupingCts?.Dispose();
        _groupingCts = new CancellationTokenSource();
        CancellationToken ct = _groupingCts.Token;

        try
        {
            // 3. Validate cache has >= 2 images
            IReadOnlyDictionary<ImageRecord, HashSet<BlockKey>> cache = _hashCacheService.Cache;
            if (cache.Count < 2)
            {
                ErrorMessage = "At least 2 images are required for threshold grouping.";
                return;
            }

            // 4. Validate at least one image has non-empty BlockKeys
            if (!cache.Values.Any(blockKeys => blockKeys.Count > 0))
            {
                ErrorMessage = "Comparison not possible: no images have block data.";
                return;
            }

            // 5. Determine scope restriction from comparison panel
            ScopeRestriction scopeRestriction = ScopeRestriction.None;
            if (_comparisonPanel.RestrictToSameSet)
                scopeRestriction |= ScopeRestriction.SameSet;
            if (_comparisonPanel.RestrictToSameSystem)
                scopeRestriction |= ScopeRestriction.SameSystem;

            // 6. Create progress reporter that updates GroupingProgress on the UI thread
            Progress<double> progress = new Progress<double>(value =>
            {
                GroupingProgress = value;
            });

            // 7. Call the threshold grouping service on a background thread
            ThresholdGroupingResult result = await Task.Run(
                () => _thresholdGroupingService.ComputeGroupsAsync(
                    cache, ThresholdPercent, scopeRestriction, ct, progress,
                    maxDegreeOfParallelism: MaxDegreeOfParallelism),
                ct);

            // 8. On success: create dialog ViewModel and show dialog
            ThresholdGroupsDialogViewModel dialogVm = ThresholdGroupsDialogViewModel.FromResult(result);

            await ShowGroupsDialog.Handle(dialogVm);
        }
        catch (OperationCanceledException)
        {
            // 9. On cancellation: discard results silently, return to idle
        }
        catch (Exception ex)
        {
            // 10. On exception: display error message
            ErrorMessage = $"Grouping computation failed: {ex.Message}";
        }
        finally
        {
            // 11. Return to idle state
            IsComputingGroups = false;
            GroupingProgress = 0;
        }
    }

    public void Dispose()
    {
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;
        _topMatchesCts?.Cancel();
        _topMatchesCts?.Dispose();
        _topMatchesCts = null;
        _groupingCts?.Cancel();
        _groupingCts?.Dispose();
        _groupingCts = null;
        _disposables.Dispose();
    }
}