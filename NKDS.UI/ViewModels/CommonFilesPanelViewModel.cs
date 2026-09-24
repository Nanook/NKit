using NkdsUi.Models;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Common Files Panel. Displays files shared between multiple selected images
/// based on common BlockKeys. Auto-triggers computation when selection changes and supports
/// cancellation of in-progress computations.
/// </summary>
public class CommonFilesPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IBlockComparisonService _blockComparisonService;
    private readonly ErrorNotificationService? _errorNotification;
    private readonly MultipleDisposable _disposables = new();

    private CancellationTokenSource? _computationCts;

    private bool _isVisible;
    private bool _isComputing;
    private string? _message;
    private IReadOnlyList<CommonFileModel> _commonFiles = Array.Empty<CommonFileModel>();

    /// <summary>
    /// Whether the Common Files Panel is visible. True when 2 or more images are selected.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    /// <summary>
    /// Whether a common files computation is currently in progress.
    /// </summary>
    public bool IsComputing
    {
        get => _isComputing;
        set => this.RaiseAndSetIfChanged(ref _isComputing, value);
    }

    /// <summary>
    /// Status message displayed when no shared content is found or an error occurs.
    /// </summary>
    public string? Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    /// <summary>
    /// The list of common files found between the selected images.
    /// </summary>
    public IReadOnlyList<CommonFileModel> CommonFiles
    {
        get => _commonFiles;
        private set => this.RaiseAndSetIfChanged(ref _commonFiles, value);
    }

    public CommonFilesPanelViewModel(IBlockComparisonService blockComparisonService, ErrorNotificationService? errorNotification = null)
    {
        _blockComparisonService = blockComparisonService;
        _errorNotification = errorNotification;
    }

    /// <summary>
    /// Called when the image list selection changes. Triggers common files computation
    /// when 2+ images are selected, or hides the panel when fewer are selected.
    /// Cancels any in-progress computation before starting a new one.
    /// </summary>
    /// <param name="selectedImages">The currently selected images.</param>
    public void UpdateSelection(IReadOnlyList<ImageRecord> selectedImages)
    {
        // Cancel any in-progress computation
        CancelCurrentComputation();

        if (selectedImages.Count < 2)
        {
            // Hide panel and clear results when fewer than 2 images selected
            IsVisible = false;
            IsComputing = false;
            Message = null;
            CommonFiles = Array.Empty<CommonFileModel>();
            return;
        }

        // Show panel and start computation
        IsVisible = true;
        StartComputation(selectedImages);
    }

    /// <summary>
    /// Starts the common files computation on a background thread.
    /// </summary>
    private async void StartComputation(IReadOnlyList<ImageRecord> selectedImages)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        _computationCts = cts;

        IsComputing = true;
        Message = null;
        CommonFiles = Array.Empty<CommonFileModel>();

        try
        {
            List<CommonFileModel> results = await _blockComparisonService.FindCommonFilesAsync(
                selectedImages,
                cts.Token);

            // Check if this computation was cancelled while awaiting
            if (cts.Token.IsCancellationRequested)
                return;

            if (results.Count == 0)
            {
                Message = "No shared content found between selected images";
                CommonFiles = Array.Empty<CommonFileModel>();
            }
            else
            {
                Message = null;
                CommonFiles = results;
            }
        }
        catch (OperationCanceledException)
        {
            // Computation was cancelled, do nothing (a new one may be starting)
        }
        catch (Exception ex)
        {
            // Display error and clear results
            Message = $"Error computing common files: {ex.Message}";
            CommonFiles = Array.Empty<CommonFileModel>();
            _errorNotification?.PublishOperationError($"Common files computation failed: {ex.Message}");
        }
        finally
        {
            // Only clear IsComputing if this is still the active computation
            if (_computationCts == cts)
            {
                IsComputing = false;
            }
        }
    }

    /// <summary>
    /// Cancels the current in-progress computation, if any.
    /// </summary>
    private void CancelCurrentComputation()
    {
        if (_computationCts != null)
        {
            _computationCts.Cancel();
            _computationCts.Dispose();
            _computationCts = null;
        }
    }

    /// <summary>
    /// Cancels any in-progress computation and clears all results.
    /// Called when a session is closed and the selected images may no longer be valid.
    /// </summary>
    public void CancelAndClear()
    {
        CancelCurrentComputation();
        IsVisible = false;
        IsComputing = false;
        Message = null;
        CommonFiles = Array.Empty<CommonFileModel>();
    }

    public void Dispose()
    {
        CancelCurrentComputation();
        _disposables.Dispose();
    }
}