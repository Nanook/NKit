using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Provides the single canonical mechanism for performing mutating file operations
/// that require exclusive access to the DataStore files.
/// Closes the session, releases file handles deterministically, executes the operation,
/// and reopens the session in its original mode.
/// </summary>
public class ExclusiveAccessManager
{
    private readonly SessionManager _sessionManager;
    private readonly FileHandleReleaseStrategy _handleRelease;

    public ExclusiveAccessManager(
        SessionManager sessionManager,
        FileHandleReleaseStrategy handleRelease)
    {
        _sessionManager = sessionManager;
        _handleRelease = handleRelease;
    }

    /// <summary>
    /// Executes an operation with exclusive file access.
    /// If a session is open: closes it, releases handles, runs the operation, reopens.
    /// If no session is open: runs the operation directly.
    /// </summary>
    /// <param name="dataStorePath">Path to the DataStore directory.</param>
    /// <param name="operation">The mutating operation to execute.</param>
    /// <exception cref="FileHandleReleaseException">
    /// Thrown if file handles cannot be released within the maximum wait period.
    /// </exception>
    public async Task WithExclusiveAccessAsync(
        string dataStorePath,
        Func<string, Task> operation)
    {
        if (!_sessionManager.HasSession)
        {
            await operation(dataStorePath);
            return;
        }

        // Save state before closing
        bool wasDirectoryMode = _sessionManager.IsDirectoryMode;
        string savedOpenStateText = _sessionManager.OpenStateText;
        string savedSetDisplayText = _sessionManager.SetDisplayText;
        string? originalOpenPath = _sessionManager.GetOriginalOpenPath();

        _sessionManager.IsExclusiveAccessActive = true;
        _sessionManager.Close();

        // Restore title bar text immediately so it doesn't blank during the operation
        _sessionManager.OpenStateText = savedOpenStateText;
        _sessionManager.SetDisplayText = savedSetDisplayText;

        try
        {
            // Release file handles deterministically
            string databaseFilePath = ResolveDatabaseFilePath(dataStorePath);
            bool released = await _handleRelease.ReleaseHandlesAsync(databaseFilePath);
            if (!released)
            {
                throw new FileHandleReleaseException(
                    databaseFilePath,
                    TimeSpan.FromMilliseconds(500));
            }

            await operation(dataStorePath);
        }
        finally
        {
            _sessionManager.IsExclusiveAccessActive = false;

            // Reopen in the same mode
            if (originalOpenPath != null)
            {
                if (wasDirectoryMode)
                    await _sessionManager.OpenDirectoryAsync(originalOpenPath);
                else
                    await _sessionManager.OpenFileAsync(originalOpenPath);
            }

            // Restore set display if it was customized
            if (savedSetDisplayText != "All" && !string.IsNullOrEmpty(savedSetDisplayText))
                _sessionManager.SetDisplayText = savedSetDisplayText;
        }
    }

    /// <summary>
    /// Synchronous overload for operations that don't need async.
    /// </summary>
    /// <param name="dataStorePath">Path to the DataStore directory.</param>
    /// <param name="operation">The mutating operation to execute.</param>
    /// <exception cref="FileHandleReleaseException">
    /// Thrown if file handles cannot be released within the maximum wait period.
    /// </exception>
    public async Task WithExclusiveAccessAsync(
        string dataStorePath,
        Action<string> operation)
    {
        await WithExclusiveAccessAsync(dataStorePath, path =>
        {
            operation(path);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Resolves the database file path to probe for exclusive lock.
    /// If the path is already a .nkds file, uses it directly.
    /// If it's a directory, finds the first .nkds file in it.
    /// </summary>
    private static string ResolveDatabaseFilePath(string dataStorePath)
    {
        if (dataStorePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)
            && File.Exists(dataStorePath))
        {
            return dataStorePath;
        }

        // Find the first .nkds file in the directory for probing
        if (Directory.Exists(dataStorePath))
        {
            string[] files = Directory.GetFiles(dataStorePath, $"*{DataStore.DatabaseFileExtension}");
            if (files.Length > 0)
                return files[0];
        }

        // Fallback: use the path as-is (will fail the lock probe gracefully)
        return dataStorePath;
    }
}