using NKDS.Models;

namespace NKDS.Mount;

/// <summary>
/// Abstraction for platform-specific virtual filesystem mount operations.
/// The UI checks <see cref="IsPlatformSupported"/> to enable/disable the Mount toolbar button.
/// Implementations delegate to Dokan (Windows) or FUSE (Linux) for actual filesystem mounting.
/// </summary>
public interface IMountService
{
    /// <summary>
    /// Mounts a DataStore set as a virtual filesystem at the specified mount point.
    /// </summary>
    /// <param name="dataStorePath">Path to the DataStore directory.</param>
    /// <param name="setName">Name of the set to mount.</param>
    /// <param name="mountPoint">Directory path where the virtual filesystem will be exposed.</param>
    /// <param name="options">Mount options controlling which views are exposed.</param>
    /// <param name="cancellationToken">Token to cancel the mount operation.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    Task<OperationResult> MountAsync(string dataStorePath, string setName, string mountPoint,
        MountOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unmounts a previously mounted virtual filesystem.
    /// </summary>
    /// <param name="mountPoint">The mount point to unmount.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    OperationResult Unmount(string mountPoint);

    /// <summary>
    /// Returns all currently active mounts tracked by this service.
    /// </summary>
    IReadOnlyList<ActiveMount> GetActiveMounts();

    /// <summary>
    /// Validates that the mount point path is suitable for mounting.
    /// Checks are performed in order: existence → emptiness (Windows only) → active mount conflict.
    /// Stops at the first failure and returns the appropriate error message.
    /// </summary>
    /// <param name="mountPoint">The directory path to validate as a mount point.</param>
    /// <param name="activeMountPoints">Collection of mount point paths currently in use.</param>
    /// <returns>A tuple indicating whether the mount point is valid and an error message if not.</returns>
    (bool IsValid, string Error) ValidateMountPoint(string mountPoint, IReadOnlyCollection<string> activeMountPoints);

    /// <summary>
    /// Indicates whether the current platform supports virtual filesystem mounting.
    /// Windows requires Dokan; Linux requires FUSE.
    /// </summary>
    bool IsPlatformSupported { get; }

    /// <summary>
    /// If <see cref="IsPlatformSupported"/> is false, provides a human-readable reason
    /// explaining the missing dependency (e.g., "Dokan driver not installed" or "FUSE not available").
    /// Returns null when the platform is supported.
    /// </summary>
    string PlatformUnsupportedReason { get; }
}