using NKDS.Models;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace NkdsUi.ViewModels;

/// <summary>
/// Tracks a single running operation's progress and cancellation.
/// Each long-running operation gets its own instance displayed in a progress panel below the toolbar.
/// </summary>
public class OperationProgressViewModel : ViewModelBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    private readonly Avalonia.Threading.DispatcherTimer _elapsedTimer;

    private string _operationName = "";
    private int _percentage;
    private double _percentComplete;
    private string _currentItem = "";
    private string? _currentFileName;
    private int _itemsProcessed;
    private int _totalItems;
    private TimeSpan _elapsed;
    private bool _isCancelling;

    /// <summary>
    /// Display name of the operation (e.g. "Add Images", "Verify", "Export").
    /// </summary>
    public string OperationName
    {
        get => _operationName;
        set => this.RaiseAndSetIfChanged(ref _operationName, value);
    }

    /// <summary>
    /// Percentage complete (0–100) as an integer.
    /// </summary>
    public int Percentage
    {
        get => _percentage;
        set => this.RaiseAndSetIfChanged(ref _percentage, value);
    }

    /// <summary>
    /// Percentage complete (0.0–100.0) for the current file being processed.
    /// Provides finer-grained progress than the integer Percentage property.
    /// </summary>
    public double PercentComplete
    {
        get => _percentComplete;
        set => this.RaiseAndSetIfChanged(ref _percentComplete, Math.Clamp(value, 0.0, 100.0));
    }

    /// <summary>
    /// Current item being processed (full path or identifier).
    /// </summary>
    public string CurrentItem
    {
        get => _currentItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentItem, value);
            // Extract just the file name from the path for display
            CurrentFileName = string.IsNullOrEmpty(value) ? null : Path.GetFileName(value);
        }
    }

    /// <summary>
    /// The file name (without directory path) of the current file being processed.
    /// Automatically derived from CurrentItem.
    /// </summary>
    public string? CurrentFileName
    {
        get => _currentFileName;
        private set => this.RaiseAndSetIfChanged(ref _currentFileName, value);
    }

    /// <summary>
    /// Number of items processed so far.
    /// </summary>
    public int ItemsProcessed
    {
        get => _itemsProcessed;
        set
        {
            this.RaiseAndSetIfChanged(ref _itemsProcessed, value);
            this.RaisePropertyChanged(nameof(ItemsProgressDisplay));
            this.RaisePropertyChanged(nameof(CounterDisplay));
        }
    }

    /// <summary>
    /// Total number of items to process.
    /// </summary>
    public int TotalItems
    {
        get => _totalItems;
        set
        {
            this.RaiseAndSetIfChanged(ref _totalItems, value);
            this.RaisePropertyChanged(nameof(ItemsProgressDisplay));
            this.RaisePropertyChanged(nameof(CounterDisplay));
        }
    }

    /// <summary>
    /// Formatted display of items progress (e.g. "3 / 10").
    /// </summary>
    public string ItemsProgressDisplay => $"{ItemsProcessed} / {TotalItems}";

    /// <summary>
    /// Counter display showing the current item being processed in "n/total" format
    /// (e.g. "1/65", "2/65"). Uses ItemsProcessed + 1 to indicate the item currently
    /// being worked on (1-based).
    /// </summary>
    public string CounterDisplay => TotalItems > 0
        ? $"{ItemsProcessed + 1}/{TotalItems}"
        : "";

    /// <summary>
    /// Elapsed time since the operation started.
    /// </summary>
    public TimeSpan Elapsed
    {
        get => _elapsed;
        set
        {
            this.RaiseAndSetIfChanged(ref _elapsed, value);
            this.RaisePropertyChanged(nameof(ElapsedDisplay));
        }
    }

    /// <summary>
    /// Formatted elapsed time display (e.g. "00:01:23").
    /// </summary>
    public string ElapsedDisplay => Elapsed.ToString(@"hh\:mm\:ss");

    /// <summary>
    /// True after the user has requested cancellation but before the operation has acknowledged it.
    /// </summary>
    public bool IsCancelling
    {
        get => _isCancelling;
        private set => this.RaiseAndSetIfChanged(ref _isCancelling, value);
    }

    /// <summary>
    /// Command to request cancellation of the operation.
    /// Signals the CancellationTokenSource and sets IsCancelling to true.
    /// Disabled once cancellation has been requested.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>
    /// CancellationToken that the operation should observe for cooperative cancellation.
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    public OperationProgressViewModel()
    {
        IObservable<bool> canCancel = this.WhenAnyValue(x => x.IsCancelling, isCancelling => !isCancelling);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel, canCancel);

        // Tick elapsed time every second
        _elapsedTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => Elapsed = _stopwatch.Elapsed;
        _elapsedTimer.Start();
    }

    /// <summary>
    /// Creates an IProgress&lt;OperationProgress&gt; reporter that updates this ViewModel's properties.
    /// The returned Progress&lt;T&gt; captures the current SynchronizationContext at creation time,
    /// so it automatically marshals callbacks to the UI thread.
    /// </summary>
    public IProgress<OperationProgress> CreateProgressReporter()
    {
        return new Progress<OperationProgress>(p =>
        {
            Percentage = p.Percentage;
            PercentComplete = p.Percentage; // Update double version as well
            CurrentItem = p.CurrentItem;
            ItemsProcessed = p.ItemsProcessed;
            TotalItems = p.TotalItems;
            Elapsed = p.Elapsed;
        });
    }

    /// <summary>
    /// Minimum time the progress panel should be visible (prevents sub-second flashes).
    /// </summary>
    private static readonly TimeSpan MinimumDisplayTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Waits until at least <see cref="MinimumDisplayTime"/> has elapsed since this instance
    /// was created. Call this before removing from ActiveOperations to prevent sub-second
    /// flashes that make the UI look glitchy. If the operation took longer than the minimum,
    /// this returns immediately.
    /// </summary>
    public async Task WaitForMinimumDisplayTimeAsync()
    {
        TimeSpan remaining = MinimumDisplayTime - _stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);
    }

    /// <summary>
    /// Recalculates <see cref="Percentage"/> and <see cref="PercentComplete"/> based on
    /// <see cref="ItemsProcessed"/> and <see cref="TotalItems"/>.
    /// Formula: (ItemsProcessed / TotalItems) * 100.
    /// </summary>
    private void RecalculatePercentage()
    {
        if (TotalItems > 0)
        {
            double pct = (double)ItemsProcessed / TotalItems * 100.0;
            Percentage = (int)pct;
            PercentComplete = pct;
        }
    }

    private void ExecuteCancel()
    {
        _cts.Cancel();
        IsCancelling = true;
    }

    public void Dispose()
    {
        _elapsedTimer.Stop();
        _cts.Dispose();
    }
}