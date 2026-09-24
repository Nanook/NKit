namespace NKDS.Models;

/// <summary>
/// Per-image progress update from the NKit pipeline.
/// </summary>
public sealed class ImageProgressEvent
{
    /// <summary>The set name containing the image.</summary>
    public required string SetName { get; init; }

    /// <summary>The image ID being processed.</summary>
    public required long ImageId { get; init; }

    /// <summary>Current processing step name (e.g. "Scan", "Dedupe", "Verify").</summary>
    public required string StepName { get; init; }

    /// <summary>Progress within the current step (0.0 to 1.0).</summary>
    public required float StepProgress { get; init; }

    /// <summary>Overall progress across all steps (0.0 to 1.0).</summary>
    public required float OverallProgress { get; init; }

    /// <summary>Current step index (0-based).</summary>
    public int StepIndex { get; init; }

    /// <summary>Total number of steps.</summary>
    public int StepTotal { get; init; }

    /// <summary>All step names in order.</summary>
    public string[] Steps { get; init; }
}