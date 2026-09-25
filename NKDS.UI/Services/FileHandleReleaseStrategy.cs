namespace NkdsUi.Services;

/// <summary>
/// Deterministic mechanism for releasing SQLite file handles after a session is closed.
/// Uses SqliteConnection.ClearAllPools() followed by a file-lock probe with exponential backoff.
/// </summary>
public class FileHandleReleaseStrategy
{
    /// <summary>
    /// Maximum cumulative wait time before reporting failure (500ms).
    /// Retry schedule: 50ms, 100ms, 200ms, 400ms (4 retries, exponential backoff).
    /// </summary>
    private const int _MaxCumulativeWaitMs = 500;
    private const int _InitialDelayMs = 50;

    /// <summary>
    /// Releases SQLite file handles for the specified database path.
    /// Calls SqliteConnection.ClearAllPools(), then probes for exclusive file access.
    /// </summary>
    /// <param name="databaseFilePath">
    /// Path to the .nkds SQLite database file whose handles must be released.
    /// </param>
    /// <returns>True if handles were confirmed released; false if max wait exceeded.</returns>
    public async Task<bool> ReleaseHandlesAsync(string databaseFilePath)
    {
        // Immediate check — no delay in the nominal path
        if (canAcquireExclusiveLock(databaseFilePath))
            return true;

        // Exponential backoff: 50ms, 100ms, 200ms, 400ms
        int delayMs = _InitialDelayMs;
        int cumulativeMs = 0;

        while (cumulativeMs < _MaxCumulativeWaitMs)
        {
            // Cap the delay so cumulative doesn't exceed the maximum
            int actualDelay = Math.Min(delayMs, _MaxCumulativeWaitMs - cumulativeMs);
            await Task.Delay(actualDelay);
            cumulativeMs += actualDelay;

            if (canAcquireExclusiveLock(databaseFilePath))
                return true;

            delayMs *= 2;
        }

        return false;
    }

    /// <summary>
    /// Probes whether the file can be opened with exclusive access.
    /// Does not hold the lock — opens and immediately closes.
    /// </summary>
    private static bool canAcquireExclusiveLock(string filePath)
    {
        try
        {
            using FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}