using NKDS.Models;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NKDS.Mount;

/// <summary>
/// Default implementation of <see cref="IMountService"/> that provides platform detection,
/// mount point validation, and active mount tracking. The actual filesystem mounting
/// will be wired to the existing Dokan/FUSE infrastructure during integration.
/// </summary>
public sealed class MountService : IMountService, IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveMount> _activeMounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _isPlatformSupported;
    private readonly string _platformUnsupportedReason;

    public MountService()
    {
        (_isPlatformSupported, _platformUnsupportedReason) = DetectPlatformSupport();
    }

    /// <inheritdoc />
    public bool IsPlatformSupported => _isPlatformSupported;

    /// <inheritdoc />
    public string PlatformUnsupportedReason => _platformUnsupportedReason;

    /// <inheritdoc />
    public async Task<OperationResult> MountAsync(string dataStorePath, string setName, string mountPoint,
        MountOptions options, CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();

        // Check platform support
        if (!_isPlatformSupported)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = _platformUnsupportedReason ?? "Platform not supported for mounting." }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        // Validate mount point
        ReadOnlyCollection<string> activeMountPointsList = _activeMounts.Keys.ToList().AsReadOnly();
        (bool valid, string validationError) = ValidateMountPoint(mountPoint, activeMountPointsList);
        if (!valid)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = validationError! }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        // Check if already mounted at this point
        if (_activeMounts.ContainsKey(mountPoint))
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = "Mount point is already in use." }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        // Check cancellation before proceeding
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationResult
            {
                Success = false,
                WasCancelled = true,
                ItemsProcessed = 0,
                ItemsFailed = 0,
                Duration = sw.Elapsed
            };
        }

        try
        {
            // TODO: Wire to actual Dokan/FUSE mount infrastructure during integration.
            // The platform-specific filesystem code (WindowsFs/LinuxFs) lives in the NKDS CLI project.
            // For now, track the mount and return success — actual mounting will be delegated
            // to the platform host when integration is complete.
            await Task.CompletedTask.ConfigureAwait(false);

            ActiveMount activeMount = new ActiveMount
            {
                MountPoint = mountPoint,
                SetName = setName,
                DataStorePath = dataStorePath
            };

            if (!_activeMounts.TryAdd(mountPoint, activeMount))
            {
                return new OperationResult
                {
                    Success = false,
                    Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = "Mount point is already in use." }],
                    ItemsProcessed = 0,
                    ItemsFailed = 1,
                    Duration = sw.Elapsed
                };
            }

            return new OperationResult
            {
                Success = true,
                ItemsProcessed = 1,
                ItemsFailed = 0,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = ex.Message }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public OperationResult Unmount(string mountPoint)
    {
        Stopwatch sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint ?? "", Reason = "Mount point cannot be empty." }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        if (!_activeMounts.TryRemove(mountPoint, out _))
        {
            return new OperationResult
            {
                Success = false,
                Errors = [new OperationErrorEntry { ItemName = mountPoint, Reason = "No active mount found at the specified mount point." }],
                ItemsProcessed = 0,
                ItemsFailed = 1,
                Duration = sw.Elapsed
            };
        }

        // TODO: Wire to actual Dokan/FUSE unmount during integration.
        // The platform host will handle releasing file handles and cache resources.

        return new OperationResult
        {
            Success = true,
            ItemsProcessed = 1,
            ItemsFailed = 0,
            Duration = sw.Elapsed
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ActiveMount> GetActiveMounts() => _activeMounts.Values.ToList().AsReadOnly();

    /// <summary>
    /// Validates that the mount point path is suitable for mounting.
    /// Checks are performed in order: existence → emptiness (Windows only) → active mount conflict.
    /// Stops at the first failure and returns the appropriate error message.
    /// </summary>
    /// <param name="mountPoint">The directory path to validate as a mount point.</param>
    /// <param name="activeMountPoints">Collection of mount point paths currently in use.</param>
    /// <returns>A tuple indicating whether the mount point is valid and an error message if not.</returns>
    public (bool IsValid, string Error) ValidateMountPoint(string mountPoint, IReadOnlyCollection<string> activeMountPoints)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return (false, "Mount point path cannot be empty.");

        // On Linux, FUSE requires the mount point directory to exist
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!Directory.Exists(mountPoint))
                return (false, $"Mount point directory does not exist: {mountPoint}");
        }

        // On Windows, Dokan handles mount point creation itself (supports drive letters
        // like N:\, non-existent directories, and existing empty directories) — no
        // existence or emptiness check needed.

        // Check active mount conflicts
        if (activeMountPoints.Contains(mountPoint, StringComparer.OrdinalIgnoreCase))
            return (false, $"Mount point is already in use: {mountPoint}");

        return (true, null);
    }

    /// <summary>
    /// Detects whether the current platform supports virtual filesystem mounting.
    /// Windows requires Dokan driver; Linux requires FUSE.
    /// </summary>
    internal static (bool IsSupported, string UnsupportedReason) DetectPlatformSupport()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return detectDokanSupport();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return detectFuseSupport();
        }

        return (false, $"Mounting is not supported on {RuntimeInformation.OSDescription}. Only Windows (Dokan) and Linux (FUSE) are supported.");
    }

    private static (bool IsSupported, string UnsupportedReason) detectDokanSupport()
    {
        // Check for Dokan driver DLL in standard installation paths
        string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string dokanDriverPath = Path.Combine(systemRoot, "System32", "drivers", "dokan2.sys");

        if (File.Exists(dokanDriverPath))
            return (true, null);

        // Also check for the Dokan library DLL which is more commonly present
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string dokanLibPath = Path.Combine(programFiles, "Dokan", "DokanLibrary");

        if (Directory.Exists(dokanLibPath))
            return (true, null);

        // Check PATH for dokanctl.exe as a fallback indicator
        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, "dokanctl.exe")))
                        return (true, null);
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }
        }

        return (false, "Dokan driver is not installed. Install Dokan v2.3.1.1000 or later from https://github.com/dokan-dev/dokany/releases and reboot.");
    }

    private static (bool IsSupported, string UnsupportedReason) detectFuseSupport()
    {
        // Check for libfuse shared library
        string[] fusePaths =
        [
            "/usr/lib/libfuse.so",
            "/usr/lib/libfuse.so.2",
            "/usr/lib/libfuse3.so",
            "/usr/lib/libfuse3.so.3",
            "/usr/lib64/libfuse.so",
            "/usr/lib64/libfuse.so.2",
            "/usr/lib64/libfuse3.so",
            "/usr/lib64/libfuse3.so.3",
            "/lib/x86_64-linux-gnu/libfuse.so.2",
            "/lib/x86_64-linux-gnu/libfuse3.so.3",
            "/lib/aarch64-linux-gnu/libfuse.so.2",
            "/lib/aarch64-linux-gnu/libfuse3.so.3",
        ];

        foreach (string path in fusePaths)
        {
            if (File.Exists(path))
                return (true, null);
        }

        // Check if fusermount is available (indicates FUSE is installed)
        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, "fusermount")) ||
                        File.Exists(Path.Combine(dir, "fusermount3")))
                        return (true, null);
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }
        }

        return (false, "FUSE is not installed. Install FUSE 3 for your distribution:\n  Debian/Ubuntu: sudo apt install fuse3 libfuse3-dev\n  Fedora/RHEL:   sudo dnf install fuse3 fuse3-devel\n  Arch:          sudo pacman -S fuse3");
    }

    public void Dispose()
    {
        // Unmount all active mounts on disposal
        foreach (string mountPoint in _activeMounts.Keys.ToList())
        {
            _activeMounts.TryRemove(mountPoint, out _);
            // TODO: Wire actual unmount calls during integration
        }
    }
}