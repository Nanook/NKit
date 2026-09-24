namespace NkdsUi.Models;

public enum ImageProcessingStatus
{
    /// <summary>No processing status — image is at rest (loaded from session, not being processed).</summary>
    None,

    /// <summary>Image is awaiting processing (pre-populated during the Pre-Scan phase).</summary>
    Pending,

    /// <summary>Image is currently being processed by the NKit pipeline.</summary>
    Processing,

    /// <summary>Image processing completed successfully.</summary>
    Completed,

    /// <summary>Image processing failed (exception, verify failure, pipeline error).</summary>
    Failed,

    /// <summary>Image was skipped (e.g. system-filter or no-op skip — nothing to add).</summary>
    Skipped,

    /// <summary>Image already exists in the set (identical content) — the add was a no-op duplicate.</summary>
    AlreadyExists,

    /// <summary>Image processing was cancelled by the user.</summary>
    Cancelled,

    /// <summary>Pre-populated image was not processed because the user cancelled the Add operation before it was reached.</summary>
    AddCancelled
}