using NKDS.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NKDS.Mount;

/// <summary>
/// High-level entry point for the mount lifecycle. Encapsulates DataStore path resolution,
/// platform host creation, background thread management, active mount tracking, unmount/cleanup,
/// and state change event emission. Used by both CLI and GUI applications.
/// </summary>
public sealed class MountOrchestrator : IDisposable
{
    private readonly IPlatformFsHostFactory _hostFactory;
    private readonly ConcurrentDictionary<string, MountEntry> _activeMounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Raised when a mount operation transitions between lifecycle states.
    /// </summary>
    public event EventHandler<MountStateChangedEventArgs> StateChanged;

    /// <summary>
    /// Creates a new MountOrchestrator with the specified platform host factory.
    /// </summary>
    /// <param name="hostFactory">Factory for creating platform-specific filesystem hosts.</param>
    public MountOrchestrator(IPlatformFsHostFactory hostFactory)
    {
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
    }

    /// <summary>
    /// Start a mount operation. The platform host runs on a background thread so this method
    /// returns immediately after the host thread is started.
    /// </summary>
    /// <param name="request">Configuration for the mount operation.</param>
    /// <param name="ct">Cancellation token (not currently used for host cancellation, reserved for future use).</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    public Task<OperationResult> MountAsync(MountRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.DataStorePaths == null || request.DataStorePaths.Length == 0)
            return Task.FromResult(createErrorResult("At least one DataStore path is required."));

        if (string.IsNullOrWhiteSpace(request.MountPoint))
            return Task.FromResult(createErrorResult("Mount point is required."));

        if (_activeMounts.ContainsKey(request.MountPoint))
            return Task.FromResult(createErrorResult($"Mount point '{request.MountPoint}' is already in use."));

        if (!_hostFactory.IsSupported)
            return Task.FromResult(createErrorResult(
                _hostFactory.UnsupportedReason ?? "Platform filesystem host is not supported."));

        // Normalize "All" set name to null
        string normalizedSetName = NormalizeSetName(request.SetName);

        emitStateChanged(request.MountPoint, MountState.Mounting);

        IPlatformFsHost host;
        try
        {
            host = _hostFactory.Create();
        }
        catch (PlatformNotSupportedException ex)
        {
            emitStateChanged(request.MountPoint, MountState.Error, ex.Message);
            return Task.FromResult(createErrorResult(ex.Message));
        }

        // Set DataStorePaths on the host if it supports multi-DataStore via property
        setDataStorePaths(host, request.DataStorePaths);

        MountEntry entry = new MountEntry(host, request.MountPoint, request.DataStorePaths, normalizedSetName);

        if (!_activeMounts.TryAdd(request.MountPoint, entry))
        {
            host.Dispose();
            return Task.FromResult(createErrorResult($"Mount point '{request.MountPoint}' is already in use."));
        }

        // Run the host on a background thread (Run is blocking)
        entry.HostThread = new Thread(() => runHost(entry, request, normalizedSetName))
        {
            IsBackground = true,
            Name = $"MountHost-{request.MountPoint}"
        };
        entry.HostThread.Start();

        return Task.FromResult(new OperationResult
        {
            Success = true,
            ItemsProcessed = 1
        });
    }

    /// <summary>
    /// Unmount and cleanup the mount at the specified mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point to unmount.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    public OperationResult Unmount(string mountPoint)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(mountPoint))
            return createErrorResult("Mount point is required.");

        if (!_activeMounts.TryRemove(mountPoint, out MountEntry entry))
            return createErrorResult($"No active mount found at '{mountPoint}'.");

        emitStateChanged(mountPoint, MountState.Unmounting);

        try
        {
            // Signal the host to unmount. On Windows this sets the unmount signal; the actual
            // filesystem/model teardown runs on the host thread once its blocking Run loop returns.
            entry.Host.Dispose();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Mount] Error during unmount of '{mountPoint}': {ex.Message}");
        }

        // Wait for the host thread to actually exit before returning. Until Run() returns, the
        // platform layer (Dokan/FUSE) may still hold the DataStore open and deliver callbacks, so
        // disposing the DataStore now (as the caller does right after Unmount) would crash or hang.
        // Joining here enforces the "unmount fully completes before close" ordering the callers rely on.
        joinHostThread(entry);

        // Finalize databases for all DataStore paths
        foreach (string path in entry.DataStorePaths)
        {
            try
            {
                entry.Host.FinalizeDatabases(path, entry.SetName);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] Error finalizing databases for '{path}': {ex.Message}");
            }
        }

        emitStateChanged(mountPoint, MountState.Unmounted);

        return new OperationResult
        {
            Success = true,
            ItemsProcessed = 1
        };
    }

    /// <summary>
    /// Returns a snapshot of all currently active mounts.
    /// </summary>
    public IReadOnlyList<ActiveMount> GetActiveMounts()
    {
        return _activeMounts.Values
            .Select(e => new ActiveMount
            {
                MountPoint = e.MountPoint,
                SetName = e.SetName ?? "All",
                DataStorePath = e.DataStorePaths.Length > 0 ? e.DataStorePaths[0] : ""
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Normalizes the "All" set name to null. Case-insensitive comparison.
    /// This is a clearly testable method for Property 8 verification.
    /// </summary>
    /// <param name="setName">The set name from the mount request.</param>
    /// <returns>Null if the set name is "All" (case-insensitive) or already null; otherwise the original value.</returns>
    public static string NormalizeSetName(string setName)
    {
        if (setName == null)
            return null;

        if (string.Equals(setName, "All", StringComparison.OrdinalIgnoreCase))
            return null;

        return setName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unmount all active mounts
        foreach (KeyValuePair<string, MountEntry> kvp in _activeMounts)
        {
            try
            {
                emitStateChanged(kvp.Key, MountState.Unmounting);
                kvp.Value.Host.Dispose();
                joinHostThread(kvp.Value);
                emitStateChanged(kvp.Key, MountState.Unmounted);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Mount] Error during dispose unmount of '{kvp.Key}': {ex.Message}");
                emitStateChanged(kvp.Key, MountState.Error, ex.Message);
            }
        }

        _activeMounts.Clear();
    }

    // Bounded wait for a mount host thread to exit after being signalled to unmount. Mirrors
    // Nanook.NKit.Vfs.VfsConstants.UnmountTimeoutMs (kept as a local const so this cross-cutting
    // orchestrator has no dependency on the platform Vfs namespace).
    private const int _hostThreadJoinTimeoutMs = 5000;

    // Wait (bounded) for a mount's host thread to finish its blocking Run loop after it has been
    // signalled to unmount. This guarantees the platform filesystem layer has released the DataStore
    // before the caller disposes it. Never joins the current thread (defensive — the host always
    // runs on its own background thread) and never throws.
    private static void joinHostThread(MountEntry entry)
    {
        Thread t = entry.HostThread;
        if (t == null || !t.IsAlive || t == Thread.CurrentThread)
            return;

        try
        {
            if (!t.Join(_hostThreadJoinTimeoutMs))
                Trace.WriteLine($"[Mount] Host thread for '{entry.MountPoint}' did not exit within {_hostThreadJoinTimeoutMs}ms.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Mount] Error joining host thread for '{entry.MountPoint}': {ex.Message}");
        }
    }

    private void runHost(MountEntry entry, MountRequest request, string normalizedSetName)
    {
        try
        {
            // Use the first DataStore path as the primary path for the Run call.
            // Multi-DataStore is handled via the DataStorePaths property set earlier.
            string primaryPath = request.DataStorePaths[0];

            // Emit Mounted before entering the blocking Run loop
            emitStateChanged(request.MountPoint, MountState.Mounted);

            entry.Host.Run(
                primaryPath,
                request.MountPoint,
                normalizedSetName,
                request.Options.ShowImage,
                request.Options.ShowFileSystem,
                request.Options.ShowSystem,
                request.Options.UpdateMode,
                request.AllowOther,
                request.Uid,
                request.Gid,
                request.MaxFileSystemYamlSizeKiB);

            // Run returned normally (host was unmounted externally or via Dispose)
            if (_activeMounts.TryRemove(request.MountPoint, out _))
            {
                emitStateChanged(request.MountPoint, MountState.Unmounted);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Mount] Host error at '{request.MountPoint}': {ex.Message}");
            _activeMounts.TryRemove(request.MountPoint, out _);
            emitStateChanged(request.MountPoint, MountState.Error, entry.Host.GetErrorMessage(ex));
        }
    }

    private void emitStateChanged(string mountPoint, MountState state, string errorMessage = null)
    {
        try
        {
            StateChanged?.Invoke(this, new MountStateChangedEventArgs
            {
                MountPoint = mountPoint,
                State = state,
                ErrorMessage = errorMessage
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Mount] Error in StateChanged handler: {ex.Message}");
        }
    }

    private static void setDataStorePaths(IPlatformFsHost host, string[] dataStorePaths)
    {
        // Set DataStorePaths on hosts that support multi-DataStore mounting.
        // WindowsPlatformFsHost exposes a DataStorePaths property for multi-DataStore support.
#if WINDOWS
        if (host is Nanook.NKit.Vfs.WindowsPlatformFsHost windowsHost)
        {
            windowsHost.DataStorePaths = dataStorePaths;
        }
#endif
    }

    private static OperationResult createErrorResult(string errorMessage)
    {
        return new OperationResult
        {
            Success = false,
            Errors = [new OperationErrorEntry { ItemName = "Mount", Reason = errorMessage }]
        };
    }

    /// <summary>
    /// Internal tracking entry for an active mount.
    /// </summary>
    private sealed class MountEntry
    {
        public IPlatformFsHost Host { get; }
        public string MountPoint { get; }
        public string[] DataStorePaths { get; }
        public string SetName { get; }
        public Thread HostThread { get; set; }

        public MountEntry(IPlatformFsHost host, string mountPoint, string[] dataStorePaths, string setName)
        {
            Host = host;
            MountPoint = mountPoint;
            DataStorePaths = dataStorePaths;
            SetName = setName;
        }
    }
}