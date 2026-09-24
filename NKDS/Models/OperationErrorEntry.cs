namespace NKDS.Models;

/// <summary>
/// Represents a single error that occurred during an operation, identifying the item and reason.
/// </summary>
public sealed class OperationErrorEntry
{
    public string ItemName { get; init; } = "";
    public string Reason { get; init; } = "";
}