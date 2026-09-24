using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the standalone Grouping Window.
/// Manages the full computation lifecycle: configure → start → progress → results → re-filter → export/import.
/// Owns its own HashCacheService instance to operate independently of Comparison Mode.
/// </summary>
public class GroupingWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MultipleDisposable _disposables = new();
    private readonly IHashCacheService _hashCacheService;
    private readonly IThresholdGroupingService _thresholdGroupingService;
    private readonly Func<IReadOnlyList<ImageRecord>> _getFilteredImages;

    private CancellationTokenSource? _computeCts;

    // --- Computation state ---

    private bool _isComputing;
    public bool IsComputing
    {
        get => _isComputing;
        set => this.RaiseAndSetIfChanged(ref _isComputing, value);
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private string? _warningMessage;
    public string? WarningMessage
    {
        get => _warningMessage;
        set => this.RaiseAndSetIfChanged(ref _warningMessage, value);
    }

    // --- Configuration ---

    private double _threshold = 60;
    public double Threshold
    {
        get => _threshold;
        set => this.RaiseAndSetIfChanged(ref _threshold, value);
    }

    private int _threads = 8;
    public int Threads
    {
        get => _threads;
        set => this.RaiseAndSetIfChanged(ref _threads, value);
    }

    private int _filteredImageCount;
    public int FilteredImageCount
    {
        get => _filteredImageCount;
        set => this.RaiseAndSetIfChanged(ref _filteredImageCount, value);
    }

    public int MaxThreads { get; } = Environment.ProcessorCount * 2;

    // --- Results ---

    private bool _hasResults;
    public bool HasResults
    {
        get => _hasResults;
        set => this.RaiseAndSetIfChanged(ref _hasResults, value);
    }

    private int _totalGroups;
    public int TotalGroups
    {
        get => _totalGroups;
        set => this.RaiseAndSetIfChanged(ref _totalGroups, value);
    }

    private int _totalGroupedImages;
    public int TotalGroupedImages
    {
        get => _totalGroupedImages;
        set => this.RaiseAndSetIfChanged(ref _totalGroupedImages, value);
    }

    private string _headerSummary = string.Empty;
    public string HeaderSummary
    {
        get => _headerSummary;
        set => this.RaiseAndSetIfChanged(ref _headerSummary, value);
    }

    private bool _hasNoGroups;
    public bool HasNoGroups
    {
        get => _hasNoGroups;
        set => this.RaiseAndSetIfChanged(ref _hasNoGroups, value);
    }

    private IReadOnlyList<ThresholdGroupModel> _groups = Array.Empty<ThresholdGroupModel>();
    public IReadOnlyList<ThresholdGroupModel> Groups
    {
        get => _groups;
        set => this.RaiseAndSetIfChanged(ref _groups, value);
    }

    private IReadOnlyList<NumberedGroupViewModel> _numberedGroups = Array.Empty<NumberedGroupViewModel>();
    public IReadOnlyList<NumberedGroupViewModel> NumberedGroups
    {
        get => _numberedGroups;
        set => this.RaiseAndSetIfChanged(ref _numberedGroups, value);
    }

    private List<(string ImageA, string ImageB, double MatchPercent)>? _pairResults;

    // Pre-computed index-based data for fast re-filtering
    private string[]? _indexToName;
    private string?[]? _indexToSystem;
    private bool _hasMultipleSystems;
    private (int A, int B, double Pct)[]? _sortedIndexPairs; // sorted by Pct descending

    public List<(string ImageA, string ImageB, double MatchPercent)>? PairResults
    {
        get => _pairResults;
        set => this.RaiseAndSetIfChanged(ref _pairResults, value);
    }

    private List<string>? _imageNames;
    public List<string>? ImageNames
    {
        get => _imageNames;
        set => this.RaiseAndSetIfChanged(ref _imageNames, value);
    }

    // --- Commands ---

    public ReactiveCommand<RxVoid, RxVoid> StartCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ExportGroupsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ExportPairsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportPairsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ThresholdUpCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ThresholdDownCommand { get; }

    // --- Interactions (file dialogs) ---

    public Interaction<string, string?> ShowSaveFileDialog { get; } = new();
    public Interaction<string, string?> ShowOpenFileDialog { get; } = new();

    public GroupingWindowViewModel(
        IHashCacheService hashCacheService,
        IThresholdGroupingService thresholdGroupingService,
        Func<IReadOnlyList<ImageRecord>> getFilteredImages)
    {
        _hashCacheService = hashCacheService;
        _thresholdGroupingService = thresholdGroupingService;
        _getFilteredImages = getFilteredImages;

        // Initialize filtered image count from current state
        FilteredImageCount = _getFilteredImages().Count;

        // --- canExecute observables ---

        IObservable<bool> canStart = this.WhenAnyValue(x => x.IsComputing)
            .Select(computing => !computing);

        IObservable<bool> canCancel = this.WhenAnyValue(x => x.IsComputing);

        IObservable<bool> canExport = this.WhenAnyValue(
                x => x.HasResults,
                x => x.IsComputing,
                (hasResults, computing) => hasResults && !computing)
            .DistinctUntilChanged();

        IObservable<bool> canImport = this.WhenAnyValue(x => x.IsComputing)
            .Select(computing => !computing);

        // --- Create commands (bodies implemented in tasks 2.2, 2.3, 2.4) ---

        StartCommand = ReactiveCommand.CreateFromTask(ExecuteComputeAsync, canStart);
        StartCommand.DisposeWith(_disposables);

        CancelCommand = ReactiveCommand.Create(ExecuteCancel, canCancel);
        CancelCommand.DisposeWith(_disposables);

        ExportGroupsCommand = ReactiveCommand.CreateFromTask(ExportGroupsAsync, canExport);
        ExportGroupsCommand.DisposeWith(_disposables);

        ExportPairsCommand = ReactiveCommand.CreateFromTask(ExportPairsAsync, canExport);
        ExportPairsCommand.DisposeWith(_disposables);

        ImportPairsCommand = ReactiveCommand.CreateFromTask(ImportPairsAsync, canImport);
        ImportPairsCommand.DisposeWith(_disposables);

        ThresholdUpCommand = ReactiveCommand.Create(() =>
        {
            Threshold = Math.Min(100, Threshold + 5);
        });
        ThresholdUpCommand.DisposeWith(_disposables);

        ThresholdDownCommand = ReactiveCommand.Create(() =>
        {
            Threshold = Math.Max(20, Threshold - 5);
        });
        ThresholdDownCommand.DisposeWith(_disposables);

        // Debounced re-filtering: when Threshold changes, wait 300ms of inactivity
        // then re-filter groups from stored PairResults at the new threshold.
        // Only re-filter when we have results and are not currently computing.
        this.WhenAnyValue(x => x.Threshold)
            .Throttle(TimeSpan.FromMilliseconds(300), RxSchedulers.MainThreadScheduler)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Where(_ => HasResults && !IsComputing)
            .Subscribe(ReFilterGroups)
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Executes the two-phase computation pipeline.
    /// Phase 1: Load BlockKeys (0-50% progress).
    /// Phase 2: Compute groups (50-100% progress).
    /// </summary>
    private async Task ExecuteComputeAsync()
    {
        // Clear any previous error
        ErrorMessage = null;

        // Capture snapshot of filtered images as working set
        IReadOnlyList<ImageRecord> snapshot = _getFilteredImages();

        // Validate working set has >= 2 images
        if (snapshot.Count < 2)
        {
            ErrorMessage = "At least 2 images are required for grouping.";
            return;
        }

        // Transition to computing state
        IsComputing = true;
        ProgressPercent = 0;
        StatusText = string.Empty;

        // Create new CancellationTokenSource
        _computeCts?.Dispose();
        _computeCts = new CancellationTokenSource();
        CancellationToken ct = _computeCts.Token;

        try
        {
            // Phase 1: Load BlockKeys (0-50% progress)
            StatusText = "Loading BlockKeys...";
            Progress<(int loaded, int total)> phase1Progress = new Progress<(int loaded, int total)>(p =>
            {
                if (p.total > 0)
                    ProgressPercent = p.loaded / (double)p.total * 50;
            });

            await _hashCacheService.LoadAsync(snapshot, ct, phase1Progress);

            ct.ThrowIfCancellationRequested();

            // Phase 2: Compute groups (50-100% progress)
            StatusText = "Computing groups...";
            Progress<double> phase2Progress = new Progress<double>(p =>
            {
                ProgressPercent = 50 + (p * 50);
            });

            ThresholdGroupingResult result = await _thresholdGroupingService.ComputeGroupsAsync(
                _hashCacheService.Cache,
                20, // Always compute at 20% minimum so user can adjust threshold freely
                ScopeRestriction.None,
                ct,
                phase2Progress,
                Threads);

            ct.ThrowIfCancellationRequested();

            // On success: store results
            PairResults = result.AllPairResults;
            ImageNames = result.ImageNames;
            PreComputeIndexedPairs();
            HasResults = true;

            // Re-filter at the user's display threshold (computation was at 20% minimum)
            ReFilterGroups(Threshold);
        }
        catch (OperationCanceledException)
        {
            // Discard partial results, return to idle silently
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            // Return to idle state
            IsComputing = false;
            ProgressPercent = 0;
            StatusText = string.Empty;
        }
    }

    /// <summary>
    /// Cancels the current computation.
    /// Implementation in task 2.3.
    /// </summary>
    private void ExecuteCancel() => _computeCts?.Cancel();

    /// <summary>
    /// Re-filters groups from stored pair results at the given threshold.
    /// Called by the debounced Threshold subscription and after import.
    /// Rebuilds groups using the static BuildThresholdGroups method, then updates all result properties.
    /// <summary>
    /// Pre-computes index-based pair data sorted by match % descending.
    /// Called once after computation or import. Makes ReFilterGroups O(pairs-above-threshold).
    /// </summary>
    private void PreComputeIndexedPairs()
    {
        if (_pairResults == null || _pairResults.Count == 0)
        {
            _indexToName = null;
            _indexToSystem = null;
            _hasMultipleSystems = false;
            _sortedIndexPairs = null;
            return;
        }

        Dictionary<string, int> nameToIndex = new Dictionary<string, int>();
        foreach ((string? a, string? b, double _) in _pairResults)
        {
            if (!nameToIndex.ContainsKey(a)) nameToIndex[a] = nameToIndex.Count;
            if (!nameToIndex.ContainsKey(b)) nameToIndex[b] = nameToIndex.Count;
        }

        _indexToName = new string[nameToIndex.Count];
        foreach ((string? name, int idx) in nameToIndex)
            _indexToName[idx] = name;

        // Build name→system lookup from the hash cache (which is keyed by ImageRecord)
        _indexToSystem = new string?[nameToIndex.Count];
        HashSet<string> distinctSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ImageRecord imageRecord in _hashCacheService.Cache.Keys)
        {
            if (nameToIndex.TryGetValue(imageRecord.Name, out int idx))
            {
                _indexToSystem[idx] = imageRecord.System;
                if (!string.IsNullOrEmpty(imageRecord.System))
                    distinctSystems.Add(imageRecord.System);
            }
        }
        _hasMultipleSystems = distinctSystems.Count > 1;

        // Sort pairs descending by match % — allows early exit in ReFilterGroups
        _sortedIndexPairs = _pairResults
            .Select(p => (nameToIndex[p.ImageA], nameToIndex[p.ImageB], p.MatchPercent))
            .OrderByDescending(p => p.Item3)
            .ToArray();
    }

    /// <summary>
    /// Fast re-filter using pre-computed sorted index pairs and union-find.
    /// Single pass through pairs (stops at threshold), then one pass to build groups.
    /// </summary>
    private void ReFilterGroups(double threshold)
    {
        if (_sortedIndexPairs == null || _indexToName == null || _sortedIndexPairs.Length == 0)
            return;

        int n = _indexToName.Length;
        int[] parent = new int[n];
        int[] rank = new int[n];
        double[] bestPct = new double[n]; // best match % per image
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }

        // Single pass: union pairs >= threshold (sorted desc, so we can break early)
        for (int i = 0; i < _sortedIndexPairs.Length; i++)
        {
            (int a, int b, double pct) = _sortedIndexPairs[i];
            if (pct < threshold) break; // sorted desc — everything after is below threshold

            // Union
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                if (rank[ra] < rank[rb]) parent[ra] = rb;
                else if (rank[ra] > rank[rb]) parent[rb] = ra;
                else { parent[rb] = ra; rank[ra]++; }
            }

            // Track best match per image
            if (pct > bestPct[a]) bestPct[a] = pct;
            if (pct > bestPct[b]) bestPct[b] = pct;
        }

        // Group by root, exclude singletons
        Dictionary<int, List<int>> groupMembers = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groupMembers.TryGetValue(root, out List<int>? list))
            {
                list = new List<int>();
                groupMembers[root] = list;
            }
            list.Add(i);
        }

        // Build view models
        List<NumberedGroupViewModel> numberedGroups = new List<NumberedGroupViewModel>();
        int groupNum = 0;

        foreach ((int _, List<int>? members) in groupMembers
            .Where(g => g.Value.Count > 1)
            .OrderByDescending(g => g.Value.Count))
        {
            groupNum++;

            // Find the image with the highest best-match as group name
            int topIdx = members[0];
            double topPct = bestPct[topIdx];
            for (int i = 1; i < members.Count; i++)
            {
                if (bestPct[members[i]] > topPct)
                {
                    topPct = bestPct[members[i]];
                    topIdx = members[i];
                }
            }

            // Compute summary from intra-group pairs (use the sorted array, stop at threshold)
            double gMin = double.MaxValue, gMax = 0, gSum = 0;
            int gCount = 0;
            HashSet<int> memberSet = new HashSet<int>(members);

            for (int i = 0; i < _sortedIndexPairs.Length; i++)
            {
                (int a, int b, double pct) = _sortedIndexPairs[i];
                if (pct < threshold) break;
                if (memberSet.Contains(a) && memberSet.Contains(b))
                {
                    if (pct < gMin) gMin = pct;
                    if (pct > gMax) gMax = pct;
                    gSum += pct;
                    gCount++;
                }
            }

            List<GroupedImageItem> imageItems = members
                .Select(idx => new GroupedImageItem { Name = FormatDisplayName(idx), BestMatchPercent = bestPct[idx] })
                .OrderByDescending(x => x.BestMatchPercent)
                .ToList();

            ThresholdGroupSummary summary = new ThresholdGroupSummary
            {
                MinMatchPercentage = gCount > 0 ? gMin : 0,
                MaxMatchPercentage = gCount > 0 ? gMax : 0,
                AvgMatchPercentage = gCount > 0 ? gSum / gCount : 0
            };

            ThresholdGroupModel group = new ThresholdGroupModel
            {
                Images = members.Select(idx => new ImageRecord { Name = _indexToName[idx] }).OrderBy(i => i.Name, StringComparer.Ordinal).ToList(),
                PairwiseMatches = new List<PairwiseMatch>(),
                Summary = summary
            };

            numberedGroups.Add(new NumberedGroupViewModel
            {
                GroupNumber = groupNum,
                Group = group,
                SortedPairwiseMatches = Array.Empty<PairwiseMatch>(),
                GroupName = FormatDisplayName(topIdx),
                ImageItems = imageItems
            });
        }

        // Sort groups by member count descending, then avg match descending for ties
        numberedGroups.Sort((a, b) =>
        {
            int countCmp = b.Group.Images.Count.CompareTo(a.Group.Images.Count);
            if (countCmp != 0) return countCmp;
            return b.Group.Summary.AvgMatchPercentage.CompareTo(a.Group.Summary.AvgMatchPercentage);
        });

        // Re-number groups after sorting
        for (int i = 0; i < numberedGroups.Count; i++)
            numberedGroups[i].GroupNumber = i + 1;

        Groups = numberedGroups.Select(g => g.Group).ToList();
        NumberedGroups = numberedGroups;
        TotalGroups = numberedGroups.Count;
        TotalGroupedImages = numberedGroups.Sum(g => g.Group.Images.Count);
        HeaderSummary = $"{TotalGroups} group(s), {TotalGroupedImages} image(s) at {threshold:F0}% threshold";
        HasNoGroups = HasResults && TotalGroups == 0;
    }

    /// <summary>
    /// Formats a display name for an image at the given index.
    /// Appends the system type in brackets when multiple systems are loaded.
    /// </summary>
    private string FormatDisplayName(int idx)
    {
        string name = _indexToName![idx];
        if (_hasMultipleSystems && _indexToSystem != null && !string.IsNullOrEmpty(_indexToSystem[idx]))
            return $"{name} [{_indexToSystem[idx]}]";
        return name;
    }

    /// <summary>
    /// Exports grouped results to YAML via save file dialog.
    /// </summary>
    private async Task ExportGroupsAsync()
    {
        ErrorMessage = null;
        WarningMessage = null;

        string filePath = await ShowSaveFileDialog.Handle("yaml");
        if (filePath == null) return;

        try
        {
            using StreamWriter writer = new StreamWriter(filePath);
            writer.WriteLine("# Threshold Groups");
            writer.WriteLine($"# Threshold: {Threshold:F0}%");
            writer.WriteLine($"# Groups: {TotalGroups}");
            writer.WriteLine($"# Images: {TotalGroupedImages}");

            foreach (NumberedGroupViewModel group in NumberedGroups)
            {
                writer.WriteLine();
                writer.WriteLine($"- name: {YamlEscape(group.GroupName)}");
                writer.WriteLine("  matches:");
                foreach (GroupedImageItem item in group.ImageItems)
                {
                    writer.WriteLine($"    - {YamlEscape(item.Name)}");
                }
            }
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports full pairwise matrix to YAML via save file dialog.
    /// </summary>
    private async Task ExportPairsAsync()
    {
        ErrorMessage = null;
        WarningMessage = null;

        string filePath = await ShowSaveFileDialog.Handle("yaml");
        if (filePath == null) return;

        try
        {
            PairwiseMatrixMetadata metadata = new PairwiseMatrixMetadata
            {
                Scope = "None",
                ImageCount = ImageNames!.Count,
                ComputedAt = DateTime.UtcNow.ToString("o"),
                Images = ImageNames
            };
            using StreamWriter writer = new StreamWriter(filePath);
            PairwiseMatrixYamlWriter.WritePairwiseMatrix(writer, metadata, PairResults!);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports pairwise matrix from YAML, validates image names, re-filters.
    /// </summary>
    private async Task ImportPairsAsync()
    {
        ErrorMessage = null;
        WarningMessage = null;

        string filePath = await ShowOpenFileDialog.Handle("yaml");
        if (filePath == null) return;

        try
        {
            using StreamReader reader = new StreamReader(filePath);
            PairwiseMatrixParseResult result = PairwiseMatrixYamlWriter.ParsePairwiseMatrix(reader);

            // Validate it's actually a pairs file
            if (result.Pairs.Count == 0 && result.Metadata.Images.Count == 0)
            {
                ErrorMessage = "Invalid file: not a pairwise matrix export. Use a file created by the Export button.";
                return;
            }

            if (result.Pairs.Count == 0)
            {
                ErrorMessage = "Invalid file: no pair results found. The file may be a groups save rather than a pairwise export.";
                return;
            }

            // Validate image names against current filtered images
            HashSet<string> importedSet = new HashSet<string>(result.Metadata.Images);
            HashSet<string> currentNames = _getFilteredImages().Select(img => img.Name).ToHashSet();

            if (!importedSet.SetEquals(currentNames))
            {
                int missing = importedSet.Except(currentNames).Count();
                int extra = currentNames.Except(importedSet).Count();
                WarningMessage = $"Import mismatch: {missing} images missing, {extra} extra images in current session";
            }

            // Replace pair results and image names with imported data
            PairResults = result.Pairs;
            ImageNames = result.Metadata.Images;
            PreComputeIndexedPairs();

            // Enable results display first so ReFilterGroups output is visible
            HasResults = true;

            // Re-filter groups at current threshold
            ReFilterGroups(Threshold);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        catch (FormatException ex)
        {
            ErrorMessage = $"Invalid pairwise matrix format: {ex.Message}";
        }
    }

    private static string YamlEscape(string value)
    {
        if (value.AsSpan().IndexOfAny(":#{[]},%&*!|>'\"`@") >= 0)
            return $"'{value.Replace("'", "''")}'";
        return value;
    }

    public void Dispose()
    {
        _computeCts?.Cancel();
        _computeCts?.Dispose();
        _hashCacheService.Clear();
        _pairResults = null;
        _imageNames = null;
        _groups = Array.Empty<ThresholdGroupModel>();
        _numberedGroups = Array.Empty<NumberedGroupViewModel>();
        _disposables.Dispose();
    }
}