namespace NkdsUi.Models;

/// <summary>
/// Tracks a single active mount with UI-specific metadata needed for lifecycle management.
/// Each instance represents a mounted DataStore set exposed as a virtual filesystem.
/// The actual host and thread management is delegated to <see cref="NKDS.Mount.MountOrchestrator"/>.
/// </summary>
internal sealed class MountEntry
{
    /// <summary>Name of the mounted set.</summary>
    public required string SetName { get; init; }

    /// <summary>Directory path where the virtual filesystem is exposed.</summary>
    public required string MountPoint { get; init; }

    /// <summary>Path to the DataStore directory.</summary>
    public required string DataStorePath { get; init; }

    /// <summary>Original open path (for session reopen after unmount).</summary>
    public required string OriginalPath { get; init; }

    /// <summary>All session paths that were open when the mount started (for reopening on unmount).</summary>
    public required IReadOnlyList<string> AllSessionPaths { get; init; }
}