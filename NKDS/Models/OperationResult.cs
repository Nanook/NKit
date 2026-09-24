namespace NKDS.Models;

/// <summary>
/// Result of any NKDS operation. Success is false if any items failed or operation was cancelled.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public bool WasCancelled { get; init; }
    public IReadOnlyList<OperationErrorEntry> Errors { get; init; } = [];
    public int ItemsProcessed { get; init; }
    public int ItemsFailed { get; init; }
    public TimeSpan Duration { get; init; }
    public long? BytesReclaimed { get; init; }
    public long? TotalOutputSize { get; init; }
    public IReadOnlyList<string> DuplicateItems { get; init; }
}