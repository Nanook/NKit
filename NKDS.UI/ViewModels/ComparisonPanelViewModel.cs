using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Comparison Panel. Manages block-level similarity comparison
/// of a reference image against candidate images, with scope restriction support.
/// </summary>
public class ComparisonPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IBlockComparisonService _comparisonService;
    private readonly IDataStoreService _dataStoreService;
    private readonly ErrorNotificationService? _errorNotification;
    private readonly MultipleDisposable _disposables = new();

    private CancellationTokenSource? _currentCts;
    private IReadOnlyList<ImageRecord> _cachedAllImages = Array.Empty<ImageRecord>();

    private ImageRecord? _referenceImage;
    private bool _isComputing;
    private double _progress;
    private string? _errorMessage;
    private string _scopeDescription = string.Empty;
    private bool _restrictToSameSet;
    private bool _restrictToSameSystem;
    private bool _isInComparisonMode;
    private IReadOnlyList<ComparisonResultModel> _results = Array.Empty<ComparisonResultModel>();

    /// <summary>
    /// The image currently being used as the comparison reference.
    /// </summary>
    public ImageRecord? ReferenceImage
    {
        get => _referenceImage;
        private set => this.RaiseAndSetIfChanged(ref _referenceImage, value);
    }

    /// <summary>
    /// Whether a comparison is currently in progress.
    /// </summary>
    public bool IsComputing
    {
        get => _isComputing;
        private set => this.RaiseAndSetIfChanged(ref _isComputing, value);
    }

    /// <summary>
    /// Progress of the current comparison (0.0 to 1.0).
    /// </summary>
    public double Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    /// <summary>
    /// Error message displayed when comparison fails or is not possible.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Description of active scope restrictions (e.g., "Same Set", "Same System", "Same Set + Same System").
    /// </summary>
    public string ScopeDescription
    {
        get => _scopeDescription;
        private set => this.RaiseAndSetIfChanged(ref _scopeDescription, value);
    }

    /// <summary>
    /// When true, only compare against images with the same SetName as the reference.
    /// </summary>
    public bool RestrictToSameSet
    {
        get => _restrictToSameSet;
        set => this.RaiseAndSetIfChanged(ref _restrictToSameSet, value);
    }

    /// <summary>
    /// When true, only compare against images with the same System as the reference.
    /// </summary>
    public bool RestrictToSameSystem
    {
        get => _restrictToSameSystem;
        set => this.RaiseAndSetIfChanged(ref _restrictToSameSystem, value);
    }

    /// <summary>
    /// When true, the panel is operating in Comparison Mode (in-memory hash cache).
    /// The auto-recompute subscription is suppressed because ComparisonModeViewModel
    /// handles scope changes by clearing results instead of re-querying the database.
    /// </summary>
    public bool IsInComparisonMode
    {
        get => _isInComparisonMode;
        set => this.RaiseAndSetIfChanged(ref _isInComparisonMode, value);
    }

    /// <summary>
    /// The comparison results (top 10 most similar images).
    /// </summary>
    public IReadOnlyList<ComparisonResultModel> Results
    {
        get => _results;
        private set => this.RaiseAndSetIfChanged(ref _results, value);
    }

    /// <summary>
    /// Command to trigger a comparison for the given reference image.
    /// </summary>
    public ReactiveCommand<ImageRecord, RxVoid> CompareCommand { get; }

    /// <summary>
    /// Command to cancel an in-progress comparison.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    public ComparisonPanelViewModel(
        IBlockComparisonService comparisonService,
        IDataStoreService dataStoreService,
        ErrorNotificationService? errorNotification = null)
    {
        _comparisonService = comparisonService;
        _dataStoreService = dataStoreService;
        _errorNotification = errorNotification;

        // Cache the latest AllImages list for synchronous filtering
        _dataStoreService.AllImages
            .Subscribe(images => _cachedAllImages = images)
            .DisposeWith(_disposables);

        // CompareCommand can always execute
        CompareCommand = ReactiveCommand.CreateFromTask<ImageRecord>(ExecuteCompareAsync);
        CompareCommand.DisposeWith(_disposables);

        // CancelCommand can execute only when a comparison is in progress
        IObservable<bool> canCancel = this.WhenAnyValue(x => x.IsComputing);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel, canCancel);
        CancelCommand.DisposeWith(_disposables);

        // Auto-recompute when scope restrictions change while a reference image is set
        // (skipped during Comparison Mode — ComparisonModeViewModel handles scope changes)
        this.WhenAnyValue(x => x.RestrictToSameSet, x => x.RestrictToSameSystem)
            .Skip(1) // Skip the initial value emission
            .Where(_ => ReferenceImage != null && !IsInComparisonMode)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ =>
            {
                UpdateScopeDescription();
                if (ReferenceImage != null)
                {
                    CompareCommand.Execute(ReferenceImage).Subscribe();
                }
            })
            .DisposeWith(_disposables);

        // Update scope description when restrictions change (even without reference)
        this.WhenAnyValue(x => x.RestrictToSameSet, x => x.RestrictToSameSystem)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => UpdateScopeDescription())
            .DisposeWith(_disposables);
    }

    private async Task ExecuteCompareAsync(ImageRecord referenceImage)
    {
        // Cancel any in-progress comparison
        CancelCurrentComparison();

        // Set reference and reset state
        ReferenceImage = referenceImage;
        ErrorMessage = null;
        Results = Array.Empty<ComparisonResultModel>();
        Progress = 0.0;
        IsComputing = true;

        CancellationTokenSource cts = new CancellationTokenSource();
        _currentCts = cts;

        try
        {
            // Apply scope restrictions to filter candidates
            IEnumerable<ImageRecord> candidates = FilterCandidates(referenceImage, _cachedAllImages);

            if (!candidates.Any())
            {
                ErrorMessage = "No candidate images available for comparison";
                return;
            }

            // Create progress reporter that updates the Progress property
            Progress<double> progress = new Progress<double>(p =>
            {
                Progress = p;
            });

            List<ComparisonResultModel> results = await _comparisonService.CompareImageAsync(
                referenceImage,
                candidates,
                cts.Token,
                progress);

            // Check if cancelled during execution
            if (cts.Token.IsCancellationRequested)
                return;

            Results = results;
            Progress = 1.0;

            if (results.Count == 0)
            {
                ErrorMessage = "No block data available for comparison";
            }
        }
        catch (OperationCanceledException)
        {
            // Comparison was cancelled, no error to display
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Comparison failed: {ex.Message}";
            Results = Array.Empty<ComparisonResultModel>();
            _errorNotification?.PublishOperationError($"Comparison failed: {ex.Message}");
        }
        finally
        {
            if (_currentCts == cts)
            {
                IsComputing = false;
                _currentCts = null;
            }
            cts.Dispose();
        }
    }

    private void ExecuteCancel() => CancelCurrentComparison();

    private void CancelCurrentComparison()
    {
        if (_currentCts != null)
        {
            _currentCts.Cancel();
            _currentCts = null;
            IsComputing = false;
        }
    }

    /// <summary>
    /// Sets comparison results from Comparison Mode's fast top-10 computation.
    /// This bypasses the normal CompareCommand flow since the computation is done
    /// by HashCacheService using in-memory data.
    /// </summary>
    /// <param name="referenceImage">The reference image used for comparison.</param>
    /// <param name="results">The computed top-match results.</param>
    public void SetComparisonModeResults(ImageRecord referenceImage, IReadOnlyList<ComparisonResultModel> results)
    {
        CancelCurrentComparison();
        ReferenceImage = referenceImage;
        ErrorMessage = null;
        Results = results;
        Progress = 1.0;
    }

    /// <summary>
    /// Cancels any in-progress comparison and clears all results and reference image.
    /// Called when the session containing the reference image is closed.
    /// </summary>
    public void CancelAndClear()
    {
        CancelCurrentComparison();
        ReferenceImage = null;
        Results = Array.Empty<ComparisonResultModel>();
        ErrorMessage = null;
        Progress = 0.0;
        ScopeDescription = string.Empty;
    }

    /// <summary>
    /// Filters candidate images based on active scope restrictions.
    /// Excludes the reference image itself from candidates.
    /// </summary>
    internal IEnumerable<ImageRecord> FilterCandidates(
        ImageRecord referenceImage,
        IReadOnlyList<ImageRecord> allImages)
    {
        IEnumerable<ImageRecord> candidates = allImages;

        // Exclude the reference image itself
        candidates = candidates.Where(img => img != referenceImage);

        // Apply SameSet restriction
        if (RestrictToSameSet)
        {
            candidates = candidates.Where(img =>
                string.Equals(img.SetName, referenceImage.SetName, StringComparison.Ordinal));
        }

        // Apply SameSystem restriction
        if (RestrictToSameSystem)
        {
            candidates = candidates.Where(img =>
                string.Equals(img.System, referenceImage.System, StringComparison.Ordinal));
        }

        return candidates;
    }

    /// <summary>
    /// Updates the ScopeDescription based on active restrictions.
    /// </summary>
    private void UpdateScopeDescription()
    {
        if (RestrictToSameSet && RestrictToSameSystem)
        {
            ScopeDescription = "Same Set + Same System";
        }
        else if (RestrictToSameSet)
        {
            ScopeDescription = "Same Set";
        }
        else if (RestrictToSameSystem)
        {
            ScopeDescription = "Same System";
        }
        else
        {
            ScopeDescription = string.Empty;
        }
    }

    public void Dispose()
    {
        CancelCurrentComparison();
        _disposables.Dispose();
    }
}