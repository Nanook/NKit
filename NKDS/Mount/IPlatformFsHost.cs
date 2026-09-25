using NKitDataStore;

namespace NKDS.Mount;

/// <summary>
/// Abstraction for platform-specific virtual filesystem hosts (Dokan on Windows, FUSE on Linux).
/// Implementations start the filesystem event loop via <see cref="Run"/> (blocking) and
/// provide <see cref="IDisposable.Dispose"/> for cleanup/unmount.
/// </summary>
public interface IPlatformFsHost : IDisposable
{
    /// <summary>
    /// Start the filesystem host. This call blocks until the filesystem is unmounted
    /// (either via Dispose or external unmount).
    /// </summary>
    void Run(string dataStorePath, string mountPoint, string setName = null,
        bool showImage = true, bool showFileSystem = true, bool showSystem = false,
        bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null,
        int maxFileSystemYamlSizeKiB = DataStore.DefaultMaxFileSystemSizeKiB);

    /// <summary>
    /// Create a platform-specific error/help message for exceptions thrown during Run.
    /// </summary>
    string GetErrorMessage(Exception ex);

    /// <summary>
    /// Finalise any open database resources for the given datastore (checkpoint WAL/SHM, clear pools).
    /// </summary>
    void FinalizeDatabases(string dataStorePath, string setName = null);
}