namespace NkdsUi.Models;

/// <summary>
/// Result of a register or unregister operation on an association entry.
/// </summary>
public sealed class AssociationOperationResult
{
    /// <summary>Whether the operation completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Error message if the operation failed, otherwise null.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the failure was due to insufficient permissions.</summary>
    public bool RequiresElevation { get; init; }
}