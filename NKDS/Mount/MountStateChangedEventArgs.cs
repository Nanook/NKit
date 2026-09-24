namespace NKDS.Mount;

/// <summary>
/// Provides data for the <see cref="MountOrchestrator.StateChanged"/> event,
/// indicating a transition in the mount lifecycle.
/// </summary>
public sealed class MountStateChangedEventArgs : EventArgs
{
    /// <summary>The mount point path associated with this state change.</summary>
    public required string MountPoint { get; init; }

    /// <summary>The new state of the mount operation.</summary>
    public required MountState State { get; init; }

    /// <summary>Optional error message when <see cref="State"/> is <see cref="MountState.Error"/>.</summary>
    public string ErrorMessage { get; init; }
}

/// <summary>
/// Represents the lifecycle states of a mount operation.
/// </summary>
public enum MountState
{
    /// <summary>Mount operation is starting.</summary>
    Mounting,

    /// <summary>Mount is active and serving filesystem requests.</summary>
    Mounted,

    /// <summary>Unmount operation is in progress.</summary>
    Unmounting,

    /// <summary>Mount has been cleanly unmounted.</summary>
    Unmounted,

    /// <summary>An error occurred during the mount lifecycle.</summary>
    Error
}