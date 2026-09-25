namespace NkdsUi.Models;

/// <summary>
/// Result of querying the registration state of an association entry.
/// </summary>
public sealed class AssociationQueryResult
{
    /// <summary>The detected state of the association.</summary>
    public required AssociationState State { get; init; }

    /// <summary>Error message if the query failed, otherwise null.</summary>
    public string? ErrorMessage { get; init; }
}