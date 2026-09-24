using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Diagnostics;

namespace NkdsUi.ViewModels;

/// <summary>
/// Manages the Stats toolbar button state, progress, and cancellation.
/// Orchestrates background stats computation via IStatsCalculationService.
/// </summary>
public class StatsCalculationViewModel : ViewModelBase, IDisposable
{
    private readonly IStatsCalculationService _statsCalculationService;
    private readonly IDataStoreService _dataStoreService;
    private readonly ImageListViewModel _imageList;
    private readonly MultipleDisposable _disposables = new();

    private CancellationTokenSource? _cts;

    private bool _isCalculating;
    private bool _canCalculate;
    private int _processedCount;
    private int _totalCount;
    private string _progressText = string.Empty;
    private string _totalSummaryText = string.Empty;
    private string _selectedSummaryText = string.Empty;
    private bool _hasComputedStats;

    /// <summary>
    /// True while stats computation is in progress.
    /// </summary>
    public bool IsCalculating
    {
        get => _isCalculating;
        private set => this.RaiseAndSetIfChanged(ref _isCalculating, value);
    }

    /// <summary>
    /// True when at least one session is open and not currently calculating.
    /// </summary>
    public bool CanCalculate
    {
        get => _canCalculate;
        private set => this.RaiseAndSetIfChanged(ref _canCalculate, value);
    }

    /// <summary>
    /// Number of sets processed so far during the current computation.
    /// </summary>
    public int ProcessedCount
    {
        get => _processedCount;
        private set => this.RaiseAndSetIfChanged(ref _processedCount, value);
    }

    /// <summary>
    /// Total number of sets to process during the current computation.
    /// </summary>
    public int TotalCount
    {
        get => _totalCount;
        private set => this.RaiseAndSetIfChanged(ref _totalCount, value);
    }

    /// <summary>
    /// Progress text displayed during computation (e.g. "42 / 128").
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        private set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    /// <summary>
    /// Total summary: sum of all image sizes vs sum of shard file sizes on disk.
    /// Fast computation — no DB queries, just filesystem + already-loaded data.
    /// Shown always when sessions are open.
    /// </summary>
    public string TotalSummaryText
    {
        get => _totalSummaryText;
        private set => this.RaiseAndSetIfChanged(ref _totalSummaryText, value);
    }

    /// <summary>
    /// Selected/computed summary: based on per-image stats after Calculate Stats is clicked.
    /// Shows the sum of StoredSize for images with computed stats.
    /// </summary>
    public string SelectedSummaryText
    {
        get => _selectedSummaryText;
        private set => this.RaiseAndSetIfChanged(ref _selectedSummaryText, value);
    }

    /// <summary>
    /// True after stats calculation has completed successfully at least once.
    /// Controls visibility of the "Show Graphs" button.
    /// </summary>
    public bool HasComputedStats
    {
        get => _hasComputedStats;
        internal set => this.RaiseAndSetIfChanged(ref _hasComputedStats, value);
    }

    /// <summary>
    /// Command to initiate stats calculation.
    /// CanExecute: at least one session is open and not currently calculating.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CalculateStatsCommand { get; }

    /// <summary>
    /// Command to cancel the in-progress stats calculation.
    /// CanExecute: IsCalculating is true.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelStatsCommand { get; }

    /// <summary>
    /// Command to show the storage statistics graphs dialog.
    /// CanExecute: HasComputedStats is true.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ShowGraphsCommand { get; }

    /// <summary>
    /// Interaction to show the StatsGraphsDialog.
    /// </summary>
    public Interaction<StatsGraphsViewModel, RxVoid> ShowGraphsDialog { get; } = new();

    public StatsCalculationViewModel(
        IStatsCalculationService statsCalculationService,
        IDataStoreService dataStoreService,
        ImageListViewModel imageList)
    {
        _statsCalculationService = statsCalculationService;
        _dataStoreService = dataStoreService;
        _imageList = imageList;

        // CanCalculate: sessions open AND not calculating
        IObservable<bool> canCalculateObs = _dataStoreService.Sessions
            .CombineLatest(
                this.WhenAnyValue(x => x.IsCalculating),
                (sessions, isCalculating) => sessions.Count > 0 && !isCalculating);

        canCalculateObs
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(value => CanCalculate = value)
            .DisposeWith(_disposables);

        // Compute TOTAL summary whenever the image list is rebuilt (sessions loaded/closed).
        // This is FAST: just sums Image.Size (already in memory) + shard file sizes (filesystem).
        // No DB queries needed.
        _imageList.WhenAnyValue(x => x.TotalImageCount)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Subscribe(_ =>
            {
                Task.Run(() =>
                {
                    IReadOnlyList<ImageSessionModel>? sessions = null;
                    try
                    {
                        sessions = _dataStoreService.Sessions.FirstAsync().GetAwaiter().GetResult();
                    }
                    catch { }

                    if (sessions == null || sessions.Count == 0)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            TotalSummaryText = string.Empty;
                            SelectedSummaryText = string.Empty;
                        });
                        return;
                    }

                    ComputeTotalSummary(sessions);
                });
            })
            .DisposeWith(_disposables);

        // CalculateStatsCommand: enabled when CanCalculate is true
        CalculateStatsCommand = ReactiveCommand.CreateFromTask(
            ExecuteCalculateStatsAsync,
            canCalculateObs.ObserveOn(RxSchedulers.MainThreadScheduler));
        CalculateStatsCommand.DisposeWith(_disposables);

        // CancelStatsCommand: enabled when IsCalculating is true
        IObservable<bool> canCancel = this.WhenAnyValue(x => x.IsCalculating);

        CancelStatsCommand = ReactiveCommand.Create(
            ExecuteCancelStats, canCancel);
        CancelStatsCommand.DisposeWith(_disposables);

        // ShowGraphsCommand: enabled when HasComputedStats is true
        IObservable<bool> canShowGraphs = this.WhenAnyValue(x => x.HasComputedStats);
        ShowGraphsCommand = ReactiveCommand.CreateFromTask(
            ExecuteShowGraphsAsync, canShowGraphs);
        ShowGraphsCommand.DisposeWith(_disposables);
    }

    private async Task ExecuteCalculateStatsAsync()
    {
        // Create a new CancellationTokenSource for this computation
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        // Set calculating state
        IsCalculating = true;
        ProcessedCount = 0;
        TotalCount = 0;
        ProgressText = "Reading blocks 0%";

        try
        {
            // Get all images and current sessions
            IReadOnlyList<ImageRowViewModel> allImages = _imageList.GetAllImages();

            // Get the latest sessions snapshot
            IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();

            // Progress callback: update ProgressText with phase name
            // The StatsCalculationService manages the global processedImages counter
            // and calls this per-image. We use it to determine phase and compute percentage.
            int totalImages = allImages.Count;
            bool firstProgressReceived = false;
            void OnProgress(int processedCount, int totalCount)
            {
                if (!firstProgressReceived)
                {
                    firstProgressReceived = true;
                }

                ProcessedCount = processedCount;
                TotalCount = totalCount;

                // Map image progress (0-100%) to bar range 0-95%
                int pct = totalCount > 0 ? (int)((long)processedCount * 95 / totalCount) : 0;
                ProgressText = $"Reading blocks {pct}%";
            }

            // Call the stats calculation service on a background thread.
            // This frees the UI thread to process InvokeAsync progress updates from the service.
            await Task.Run(() => _statsCalculationService.ComputeStatsAsync(
                allImages,
                sessions,
                maxDegreeOfParallelism: 4,
                progress: OnProgress,
                cancellationToken: ct), ct);

            // After per-image stats are done, compute the selected summary from the computed stats.
            ProgressText = "Calculating totals 95%";

            // Animate 95→99% during totals computation
            int totalsPct = 95;
            using Timer totalsTimer = new System.Threading.Timer(_ =>
            {
                if (totalsPct < 99)
                {
                    totalsPct++;
                    ProgressText = $"Calculating totals {totalsPct}%";
                }
            }, null, 300, 300);

            await ComputeSelectedSummaryAsync(ct);

            totalsTimer.Change(Timeout.Infinite, Timeout.Infinite);
            ProgressText = "Calculating totals 100%";

            // Mark that stats have been computed (enables Show Graphs button)
            HasComputedStats = true;
        }
        catch (OperationCanceledException)
        {
            // Cancellation: retain any stats already computed, just stop
        }
        finally
        {
            // Return to idle state
            IsCalculating = false;
            ProcessedCount = 0;
            TotalCount = 0;
            ProgressText = string.Empty;
        }
    }

    /// <summary>
    /// Calculates stats and reports progress to the provided OperationProgressViewModel.
    /// Used by the Graphs toolbar button to calculate stats with a progress bar before showing graphs.
    /// </summary>
    public async Task CalculateStatsWithProgressAsync(OperationProgressViewModel progressVm)
    {
        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        // Link the progress VM's cancellation token to the internal _cts so that
        // clicking the cancel button on the progress panel stops the stats calculation.
        // CancelStatsCommand continues to work independently since it also cancels _cts directly.
        using CancellationTokenRegistration ctReg = progressVm.CancellationToken.Register(() => _cts?.Cancel());

        // Subscribe to internal progress changes and forward to the progressVm
        using IDisposable sub = this.WhenAnyValue(x => x.ProcessedCount, x => x.TotalCount)
            .Subscribe(t =>
            {
                progressVm.ItemsProcessed = t.Item1;
                progressVm.TotalItems = t.Item2;
                progressVm.Elapsed = sw.Elapsed;
            });

        await ExecuteCalculateStatsAsync();
    }

    /// <summary>
    /// Fast total summary: sums Image.Size from sessions (already loaded in memory)
    /// and sums shard file sizes (block data) separately from DB sizes.
    /// In embedded mode (single file), the whole file is counted as stored.
    /// In non-embedded mode, shards ({setName}_NNNN.nkds) are "Stored" and the main
    /// {setName}.nkds is "DB".
    /// </summary>
    private void ComputeTotalSummary(IReadOnlyList<ImageSessionModel> sessions)
    {
        long totalImageSize = 0;
        long totalShardSize = 0;
        long totalDbSize = 0;
        HashSet<string> processedSets = new HashSet<string>(StringComparer.Ordinal);

        foreach (ImageSessionModel session in sessions)
        {
            // Sum image sizes (already in memory)
            foreach (ImageRecord img in session.Images)
                totalImageSize += img.Size;

            // Get distinct set names from this session
            IEnumerable<string> setNames = session.Images
                .Select(img => img.SetName)
                .Distinct(StringComparer.Ordinal);

            foreach (string setName in setNames)
            {
                if (processedSets.Contains(setName))
                    continue;
                processedSets.Add(setName);

                try
                {
                    string baseDir = Path.GetDirectoryName(session.Path) ?? session.Path;
                    if (session.ScopedSetName == null)
                        baseDir = session.Path;

                    if (!Directory.Exists(baseDir))
                        continue;

                    string setBaseName = Path.GetFileName(setName);
                    string mainFile = Path.Combine(baseDir, $"{setBaseName}.nkds");

                    if (File.Exists(mainFile))
                    {
                        // Binary index mode: main file is the index, shards are block data
                        totalDbSize += new FileInfo(mainFile).Length;

                        // Sum shard files: {setName}_NNNN.nkds
                        foreach (string shardFile in Directory.EnumerateFiles(baseDir, $"{setBaseName}_*.nkds"))
                            totalShardSize += new FileInfo(shardFile).Length;
                    }
                }
                catch
                {
                    // Skip if filesystem access fails
                }
            }
        }

        string totalSizeStr = FormatBytes(totalImageSize);
        string storedSizeStr = FormatBytes(totalShardSize);
        string dbSizeStr = FormatBytes(totalDbSize);
        double reductionPct = totalImageSize > 0
            ? (1.0 - ((double)totalShardSize / totalImageSize)) * 100.0
            : 0;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            TotalSummaryText = totalDbSize > 0
                ? $"Total: {totalSizeStr}  |  Stored: {storedSizeStr}  |  DB: {dbSizeStr}  |  Reduced: {reductionPct:F1}%"
                : $"Total: {totalSizeStr}  |  Stored: {storedSizeStr}  |  Reduced: {reductionPct:F1}%";
        });
    }

    /// <summary>
    /// Selected summary: computed from per-image stats after Calculate Stats completes.
    /// Uses the deduplicated stored size — unique blocks across filtered images per set.
    /// </summary>
    private async Task ComputeSelectedSummaryAsync(CancellationToken ct)
    {
        List<ImageRowViewModel> filteredImages = _imageList.FilteredImages.ToList();
        long totalImageSize = 0;

        foreach (ImageRowViewModel? img in filteredImages)
            totalImageSize += img.Size;

        if (totalImageSize == 0)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => SelectedSummaryText = string.Empty);
            return;
        }

        // Get sessions
        IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();

        // Compute deduplicated stored size for the filtered images
        long totalStoredSize = await _statsCalculationService.ComputeDeduplicatedStoredSizeAsync(
            filteredImages.Cast<ImageRowViewModel>().ToList(), sessions, ct);

        string totalSizeStr = FormatBytes(totalImageSize);
        string storedSizeStr = FormatBytes(totalStoredSize);
        double reductionPct = totalImageSize > 0
            ? (1.0 - ((double)totalStoredSize / totalImageSize)) * 100.0
            : 0;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            SelectedSummaryText = $"Computed: {totalSizeStr}  |  Stored: {storedSizeStr}  |  Reduced: {reductionPct:F1}%";
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }

    private async Task ExecuteShowGraphsAsync()
    {
        StatsGraphsViewModel vm = await BuildGraphViewModelAsync();
        await ShowGraphsDialog.Handle(vm);
    }

    /// <summary>
    /// Gathers graph data from the DataStore on a background thread.
    /// Returns the ViewModel ready to be passed to the graph dialog.
    /// Public so that callers can show a progress indicator during this work.
    /// </summary>
    public async Task<StatsGraphsViewModel> BuildGraphViewModelAsync()
    {
        // Get all images and sessions
        IReadOnlyList<ImageRowViewModel> allImages = _imageList.GetAllImages();
        IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();

        // Aggregate stats across all sets on a background thread to avoid blocking the UI.
        return await Task.Run(() =>
        {
            long totalImageSize = 0;
            long totalBlockRefs = 0;
            long uniqueBlocks = 0;
            long compressedBlocks = 0;
            long uncompressedBlocks = 0;
            long totalPhysicalStorage = 0;
            long afterDedupSize = 0;
            long afterJunkRemovalSize = 0;

            HashSet<string> processedSets = new HashSet<string>(StringComparer.Ordinal);

            foreach (ImageSessionModel? session in sessions)
            {
                IEnumerable<string> setNames = session.Images
                    .Select(img => img.SetName)
                    .Distinct(StringComparer.Ordinal);

                foreach (string setName in setNames)
                {
                    if (!processedSets.Add(setName))
                        continue;

                    // Sum image sizes for this set
                    List<ImageRecord> setImages = session.Images.Where(img =>
                        string.Equals(img.SetName, setName, StringComparison.Ordinal)).ToList();
                    foreach (ImageRecord? img in setImages)
                        totalImageSize += img.Size;

                    try
                    {
                        DataStoreStatistics? stats = session.DataStore.GetSetStatistics(setName, includePerImageStats: false);
                        if (stats != null)
                        {
                            totalBlockRefs += stats.TotalBlockReferences;
                            uniqueBlocks += stats.UniqueBlocksStored;
                            compressedBlocks += stats.CompressedBlocks;
                            uncompressedBlocks += stats.UncompressedBlocks;
                            totalPhysicalStorage += stats.TotalPhysicalBlockStorage;
                            afterJunkRemovalSize += stats.TotalBlockReferences * stats.BlockSize;
                            afterDedupSize += stats.UniqueBlocksStored * stats.BlockSize;
                        }
                    }
                    catch
                    {
                        // Skip if DB access fails
                    }
                }
            }

            return new StatsGraphsViewModel
            {
                TotalImageSize = totalImageSize,
                AfterJunkRemovalSize = afterJunkRemovalSize,
                AfterDedupSize = afterDedupSize,
                AfterCompressionSize = totalPhysicalStorage,
                TotalBlockRefs = totalBlockRefs,
                UniqueBlocks = uniqueBlocks,
                CompressedBlocks = compressedBlocks,
                UncompressedBlocks = uncompressedBlocks
            };
        });
    }

    private void ExecuteCancelStats() => _cts?.Cancel();

    /// <summary>
    /// Cancels the in-progress stats calculation directly (no CanExecute guard).
    /// Used by the progress panel's cancel button which fires from a CancellationToken callback.
    /// </summary>
    public void CancelCalculation() => _cts?.Cancel();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _disposables.Dispose();
    }
}