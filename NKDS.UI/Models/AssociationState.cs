namespace NkdsUi.Models;

/// <summary>
/// The detected registration state of an association entry.
/// </summary>
public enum AssociationState
{
    /// <summary>Not yet queried.</summary>
    Unknown,

    /// <summary>Entry exists and points to the current executable.</summary>
    Registered,

    /// <summary>Entry exists but points to a different executable path.</summary>
    Stale,

    /// <summary>No entry exists in the OS.</summary>
    NotRegistered
}