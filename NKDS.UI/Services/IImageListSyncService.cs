using NKDS.Models;
using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Coordinates live, incremental updates to the image list UI.
/// Receives events from NkdsOperations (via callbacks) and applies them
/// to the ImageListViewModel's collection on the UI thread.
/// </summary>
public interface IImageListSyncService
{
    /// <summary>
    /// Inserts a single image row into the FilteredImages collection.
    /// Called when an image is successfully committed during Add.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task InsertImageAsync(ImageCommittedEvent committedEvent);

    /// <summary>
    /// Removes rows for the specified image IDs from FilteredImages.
    /// Called after Remove completes.
    /// </summary>
    Task RemoveImagesAsync(string setName, IReadOnlyList<long> imageIds);

    /// <summary>
    /// Inserts rows for restored images into FilteredImages.
    /// Called after Restore completes.
    /// </summary>
    Task InsertRestoredImagesAsync(string setName, IReadOnlyList<ImageRecord> restoredImages);

    /// <summary>
    /// Reconciles FilteredImages with the current DataStore state.
    /// Adds missing rows, removes stale rows. Used after Compact/Rollback.
    /// </summary>
    Task ReconcileAsync(string setName);

    /// <summary>
    /// Updates per-image progress (step name, percentage) on the corresponding row.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task UpdateImageProgressAsync(ImageProgressEvent progressEvent);

    /// <summary>
    /// Appends text output to the specified image's output buffer.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task AppendImageOutputAsync(string setName, long imageId, string text);

    /// <summary>
    /// Updates the processing status of an image row.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task SetImageStatusAsync(string setName, long imageId, ImageProcessingStatus status, string? reason = null);

    /// <summary>
    /// Sets the verify result for an image row.
    /// </summary>
    Task SetVerifyResultAsync(string setName, long imageId, VerifyResultStatus result, string? method = null);

    /// <summary>
    /// Inserts all pre-scanned candidates as Pending rows in batch.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task PrePopulateAsync(string setName, string sessionId, IReadOnlyList<CandidateImage> candidates);

    /// <summary>
    /// Transitions all Pending rows to AddCancelled status in batch.
    /// Called when the user cancels the Add operation.
    /// Thread-safe: marshals to UI thread internally.
    /// </summary>
    Task SetBulkAddCancelledAsync(string setName);

    /// <summary>
    /// Updates the toolbar's available set names from the DataStore.
    /// Called when new sets are created during Add/1GMR.
    /// </summary>
    Task RefreshAvailableSetNamesAsync();
}