namespace NKDS.Models;

/// <summary>
/// Options controlling which views are exposed when mounting a DataStore set as a virtual filesystem.
/// </summary>
public sealed class MountOptions
{
    /// <summary>
    /// Whether to show the image view in the mounted filesystem.
    /// </summary>
    public bool ShowImage { get; init; } = true;

    /// <summary>
    /// Whether to show the filesystem view in the mounted filesystem.
    /// </summary>
    public bool ShowFileSystem { get; init; } = true;

    /// <summary>
    /// Whether to show the system view in the mounted filesystem.
    /// </summary>
    public bool ShowSystem { get; init; }

    /// <summary>
    /// Whether to mount in update mode. Mutually exclusive with ShowImage/ShowFileSystem/ShowSystem.
    /// In update mode, the mounted filesystem allows writing update partition data.
    /// </summary>
    public bool UpdateMode { get; init; }
}