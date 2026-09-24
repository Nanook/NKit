using NKDS.Models;
using NKitDataStore;

namespace NKDS.Mount;

/// <summary>
/// Configuration object for mount operations. Encapsulates all parameters needed
/// to initiate a virtual filesystem mount via <see cref="MountOrchestrator"/>.
/// </summary>
public sealed class MountRequest
{
    /// <summary>
    /// One or more DataStore paths to mount. Images from all paths appear in a unified hierarchy.
    /// </summary>
    public required string[] DataStorePaths { get; init; }

    /// <summary>
    /// The directory or drive letter where the virtual filesystem will be mounted.
    /// </summary>
    public required string MountPoint { get; init; }

    /// <summary>
    /// The set name to filter images by. Null or "All" (case-insensitive) means all sets are included.
    /// </summary>
    public string SetName { get; init; }

    /// <summary>
    /// Options controlling which views (Image, FileSystem, System) are exposed in the mount.
    /// </summary>
    public MountOptions Options { get; init; } = new();

    /// <summary>
    /// Maximum size in KiB for eagerly-loaded filesystem YAML. Larger files are lazy-loaded.
    /// </summary>
    public int MaxFileSystemYamlSizeKiB { get; init; } = DataStore.DefaultMaxFileSystemSizeKiB;

    /// <summary>
    /// Whether to pass the FUSE allow_other option (Linux only).
    /// </summary>
    public bool AllowOther { get; init; }

    /// <summary>
    /// Override UID for mounted files (Linux only). Null means use the calling user's UID.
    /// </summary>
    public uint? Uid { get; init; }

    /// <summary>
    /// Override GID for mounted files (Linux only). Null means use the calling user's GID.
    /// </summary>
    public uint? Gid { get; init; }
}