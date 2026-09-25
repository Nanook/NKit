using Avalonia.Threading;
using NKDS.Models;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Coordinates live, incremental updates to the image list UI.
/// Receives events from NkdsOperations (via callbacks) and applies them
/// to the ImageListViewModel's collection on the UI thread.
/// All collection mutations are marshalled to the UI thread via Dispatcher.UIThread.InvokeAsync.
/// </summary>
public class ImageListSyncService : IImageListSyncService
{
    private readonly ImageListViewModel _imageList;
    private readonly IDataStoreService _dataStoreService;
    private readonly SharedObservableState _sharedState;
    private readonly ErrorNotificationService _errorNotification;

    public ImageListSyncService(
        ImageListViewModel imageList,
        IDataStoreService dataStoreService,
        SharedObservableState sharedState,
        ErrorNotificationService errorNotification)
    {
        _imageList = imageList;
        _dataStoreService = dataStoreService;
        _sharedState = sharedState;
        _errorNotification = errorNotification;
    }

    /// <inheritdoc />
    public async Task InsertImageAsync(ImageCommittedEvent committedEvent)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Active set filter: only insert if the image belongs to the currently displayed set
            // or if "All" is selected (showing all sets).
            if (_sharedState.SelectedSetName != "All" && committedEvent.SetName != _sharedState.SelectedSetName)
                return;

            if (committedEvent.IsProcessing)
            {
                // Insert a placeholder row with Processing status.
                // The image has a temporary negative ID that will be used to target
                // progress/output updates during processing.
                ImageRowViewModel row = new ImageRowViewModel(committedEvent.Image, committedEvent.SessionId);
                row.ProcessingStatus = ImageProcessingStatus.Processing;
                _imageList.InsertRow(row);
                return;
            }

            if (committedEvent.WasFailed || committedEvent.WasCancelled || committedEvent.WasSkipped || committedEvent.WasAlreadyExists)
            {
                // Locate the placeholder row. It was inserted with the temporary (negative) id, so
                // prefer TempId when present. A commit that FAILED verification carries the REAL
                // committed image (correct id/CRC/size) alongside WasFailed=true — update the
                // placeholder in place to that record so the CRC/size columns populate and the row
                // keeps its position, then set the Failed status. Falling back to Image.Id covers
                // the stub-record failure events (no real commit) that carry only the temp id there.
                ImageRowViewModel? existingRow = null;
                if (committedEvent.TempId != 0)
                    existingRow = _imageList.UpdateRowImageInPlace(committedEvent.SetName, committedEvent.TempId, committedEvent.Image);
                existingRow ??= _imageList.FindRow(committedEvent.SetName, committedEvent.Image.Id);
                if (existingRow != null)
                {
                    if (committedEvent.WasFailed)
                        existingRow.ProcessingStatus = ImageProcessingStatus.Failed;
                    else if (committedEvent.WasCancelled)
                    {
                        // Distinguish: if the row was Processing (mid-pipeline), mark as Cancelled.
                        // If it was still Pending (never started), mark as AddCancelled.
                        existingRow.ProcessingStatus = existingRow.ProcessingStatus == ImageProcessingStatus.Processing
                            ? ImageProcessingStatus.Cancelled
                            : ImageProcessingStatus.AddCancelled;
                    }
                    else if (committedEvent.WasAlreadyExists)
                        existingRow.ProcessingStatus = ImageProcessingStatus.AlreadyExists;
                    else
                        existingRow.ProcessingStatus = ImageProcessingStatus.Skipped;

                    if (committedEvent.Reason != null)
                        existingRow.StatusReason = committedEvent.Reason;
                }
                else if (committedEvent.WasFailed && committedEvent.Image.Id > 0)
                {
                    // No placeholder to update, but the image WAS committed (real id) and failed
                    // verification — insert a real row with the Failed status so it is never left
                    // absent or blank. (Skip/cancel/dup stubs carry no real record, so nothing to add.)
                    ImageRowViewModel failedRow = new ImageRowViewModel(committedEvent.Image, committedEvent.SessionId)
                    {
                        ProcessingStatus = ImageProcessingStatus.Failed,
                        StatusReason = committedEvent.Reason
                    };
                    _imageList.InsertRow(failedRow);
                }
                return;
            }

            // TmdAppFolder images are internal aggregation containers — don't show them
            // in the list since the children (individual [tmd.X] App images) are shown instead.
            if (committedEvent.Image.Format == NKitDataStore.ImageFormat.TmdAppFolder)
            {
                // Still need to clean up placeholder if one exists
                if (committedEvent.TempId != 0)
                {
                    _imageList.RemoveRows(row =>
                        row.Image.SetName == committedEvent.SetName && row.Image.Id == committedEvent.TempId);
                }
                return;
            }

            // Normal commit: try to update a pre-populated row in-place (by TempId)
            // to preserve its position in the list. Falls back to remove+insert for
            // non-pre-populated rows.
            if (committedEvent.TempId != 0)
            {
                ImageRowViewModel? updatedRow = _imageList.UpdateRowImageInPlace(
                    committedEvent.SetName, committedEvent.TempId, committedEvent.Image);

                if (updatedRow != null)
                {
                    // Successfully updated pre-populated row in-place
                    updatedRow.ProcessingStatus = ImageProcessingStatus.Completed;
                    if (committedEvent.IsVerified)
                        updatedRow.VerifyResult = VerifyResultStatus.VerifySuccess;
                    return;
                }

                // No pre-populated row found — remove old placeholder (legacy flow)
                _imageList.RemoveRows(row =>
                    row.Image.SetName == committedEvent.SetName && row.Image.Id == committedEvent.TempId);
            }

            // Insert the real committed row (non-pre-populated path or fallback)
            ImageRowViewModel newRow = new ImageRowViewModel(committedEvent.Image, committedEvent.SessionId);
            newRow.ProcessingStatus = ImageProcessingStatus.Completed;

            // Propagate verification status from the pipeline
            if (committedEvent.IsVerified)
                newRow.VerifyResult = VerifyResultStatus.VerifySuccess;

            _imageList.InsertRow(newRow);
        });
    }

    /// <inheritdoc />
    public async Task RemoveImagesAsync(string setName, IReadOnlyList<long> imageIds)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            HashSet<long> idSet = imageIds.ToHashSet();
            _imageList.RemoveRowsBatch(row =>
                row.Image.SetName == setName && idSet.Contains(row.Image.Id));
        });
    }

    /// <inheritdoc />
    public async Task InsertRestoredImagesAsync(string setName, IReadOnlyList<ImageRecord> restoredImages)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_sharedState.SelectedSetName != "All" && setName != _sharedState.SelectedSetName)
                return;

            // Get session ID from existing rows in the image list
            ImageRowViewModel? existingRow = _imageList.FindRow(setName, restoredImages.FirstOrDefault()?.Id ?? 0)
                ?? _imageList.GetAllImages().FirstOrDefault();
            string sessionId = existingRow?.SessionId ?? "";

            List<ImageRowViewModel> rows = new List<ImageRowViewModel>(restoredImages.Count);
            foreach (ImageRecord image in restoredImages)
            {
                rows.Add(new ImageRowViewModel(image, sessionId));
            }

            _imageList.InsertRowsBatch(rows);
        });
    }

    /// <inheritdoc />
    public async Task ReconcileAsync(string setName)
    {
        IReadOnlyList<ImageRecord> currentImages;

        try
        {
            // Re-query the DataStore on a background thread
            // Include ALL images (both active and removed) since the UI displays both
            // but exclude TmdAppFolder images (children are shown instead)
            currentImages = await Task.Run(() =>
            {
                DataStore? ds = _dataStoreService.GetActiveDataStore();
                if (ds == null) return Array.Empty<ImageRecord>() as IReadOnlyList<ImageRecord>;
                return ds.ListImagesInSet(setName)
                    .Where(img => img.Format != NKitDataStore.ImageFormat.TmdAppFolder)
                    .ToList() as IReadOnlyList<ImageRecord>;
            });
        }
        catch (Exception ex)
        {
            // Task 11.2: Catch database errors without crashing or losing session state.
            // Leave current collection unchanged and log the error.
            _errorNotification.PublishOperationError($"ReconcileAsync failed for set '{setName}': {ex.Message}");
            return;
        }

        // Apply reconciliation on the UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _imageList.Reconcile(setName, currentImages);
            }
            catch (Exception ex)
            {
                _errorNotification.PublishOperationError($"Reconcile UI update failed for set '{setName}': {ex.Message}");
            }
        });
    }

    /// <inheritdoc />
    public async Task UpdateImageProgressAsync(ImageProgressEvent progressEvent)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ImageRowViewModel? row = _imageList.FindRow(progressEvent.SetName, progressEvent.ImageId);
            if (row == null) return;

            row.CurrentStepName = progressEvent.StepName;
            row.CurrentStepProgress = progressEvent.StepProgress;
            row.OverallProgress = progressEvent.OverallProgress;
            row.ProcessingStatus = ImageProcessingStatus.Processing;
        });
    }

    /// <inheritdoc />
    public async Task AppendImageOutputAsync(string setName, long imageId, string text)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ImageRowViewModel? row = _imageList.FindRow(setName, imageId);
            row?.AppendOutput(text);
        });
    }

    /// <inheritdoc />
    public async Task SetImageStatusAsync(string setName, long imageId,
        ImageProcessingStatus status, string? reason = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ImageRowViewModel? row = _imageList.FindRow(setName, imageId);
            if (row == null) return;
            row.ProcessingStatus = status;
            if (reason != null) row.StatusReason = reason;
        });
    }

    /// <inheritdoc />
    public async Task SetVerifyResultAsync(string setName, long imageId,
        VerifyResultStatus result, string? method = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ImageRowViewModel? row = _imageList.FindRow(setName, imageId);
            if (row == null) return;
            row.VerifyResult = result;
            if (method != null) row.VerifyMethod = method;
            // Clear processing status so StatusDisplay shows the verify result
            row.ProcessingStatus = ImageProcessingStatus.None;
        });
    }

    /// <inheritdoc />
    public async Task PrePopulateAsync(string setName, string sessionId, IReadOnlyList<CandidateImage> candidates)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            List<ImageRowViewModel> rows = new List<ImageRowViewModel>(candidates.Count);
            foreach (CandidateImage candidate in candidates)
            {
                // Skip synthetic folder entries (TmdAppFolder grouping containers) —
                // they're processed by the pipeline but shouldn't appear in the UI.
                if (candidate.Source.IsSyntheticFolder)
                    continue;

                // Create a placeholder ImageRecord with the candidate's metadata.
                // The real ImageRecord will replace this after pipeline commit.
                ImageRecord placeholderImage = new ImageRecord
                {
                    Id = candidate.TempId,
                    Name = candidate.DisambiguatedName,
                    Size = candidate.Size,
                    SetName = setName,
                    Format = NKitDataStore.ImageFormat.Unknown
                };

                ImageRowViewModel row = new ImageRowViewModel(placeholderImage, sessionId)
                {
                    CandidateSource = candidate
                };
                row.ProcessingStatus = ImageProcessingStatus.Pending;
                rows.Add(row);
            }

            _imageList.InsertRowsBatch(rows, preserveInsertionOrder: true);
        });
    }

    /// <inheritdoc />
    public async Task SetBulkAddCancelledAsync(string setName)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Transition all non-terminal rows to AddCancelled.
            // This includes Pending (never started), Processing (mid-pipeline),
            // Cancelled (pipeline was interrupted), and Failed (pipeline error during cancel).
            // From the user's perspective, the whole batch was cancelled.
            List<ImageRowViewModel> rows = _imageList.GetAllImages()
                .Where(row => row.Image.SetName == setName
                    && (row.ProcessingStatus == ImageProcessingStatus.Pending
                        || row.ProcessingStatus == ImageProcessingStatus.Processing
                        || row.ProcessingStatus == ImageProcessingStatus.Cancelled
                        || row.ProcessingStatus == ImageProcessingStatus.Failed))
                .ToList();

            foreach (ImageRowViewModel row in rows)
            {
                row.ProcessingStatus = ImageProcessingStatus.AddCancelled;
            }
        });
    }

    /// <inheritdoc />
    public async Task RefreshAvailableSetNamesAsync()
    {
        string[] setNames;

        try
        {
            setNames = await Task.Run(() =>
            {
                DataStore? ds = _dataStoreService.GetActiveDataStore();
                if (ds == null) return Array.Empty<string>();
                return ds.ListSetNames().ToArray();
            });
        }
        catch (Exception ex)
        {
            // Task 11.2: Catch database errors without crashing or losing session state
            _errorNotification.PublishOperationError($"RefreshAvailableSetNamesAsync failed: {ex.Message}");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _sharedState.AvailableSetNames = setNames;
        });
    }
}