using NKitDataStore;

namespace NKDS.Models;

/// <summary>
/// Fired by NkdsOperations after each image is successfully committed to the DataStore during Add.
/// </summary>
public sealed class ImageCommittedEvent
{
    /// <summary>The committed ImageRecord from the DataStore.</summary>
    public required ImageRecord Image { get; init; }

    /// <summary>The set name the image was committed to.</summary>
    public required string SetName { get; init; }

    /// <summary>The session ID of the owning DataStore session.</summary>
    public required string SessionId { get; init; }

    /// <summary>Whether the image was skipped (e.g. system filter / no-op — nothing to add).</summary>
    public bool WasSkipped { get; init; }

    /// <summary>Whether the image already existed in the set (identical content) — a no-op duplicate add.</summary>
    public bool WasAlreadyExists { get; init; }

    /// <summary>Whether the image processing failed (exception during pipeline).</summary>
    public bool WasFailed { get; init; }

    /// <summary>Whether the image processing was cancelled by the user.</summary>
    public bool WasCancelled { get; init; }

    /// <summary>Whether this event represents a processing-started notification (placeholder row).</summary>
    public bool IsProcessing { get; init; }

    /// <summary>
    /// The temporary ID assigned before processing. Used to update the placeholder row
    /// with the real committed image data after processing completes.
    /// </summary>
    public long TempId { get; init; }

    /// <summary>Skip/failure/cancellation reason if applicable.</summary>
    public string Reason { get; init; }

    /// <summary>Whether the NKit pipeline verified the image successfully during Add.</summary>
    public bool IsVerified { get; init; }
}