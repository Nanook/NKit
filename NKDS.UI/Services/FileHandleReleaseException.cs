namespace NkdsUi.Services;

/// <summary>
/// Thrown when the FileHandleReleaseStrategy cannot confirm handle release
/// within the maximum cumulative wait period (500ms).
/// </summary>
public class FileHandleReleaseException : Exception
{
    public string DatabaseFilePath { get; }
    public TimeSpan ElapsedWait { get; }

    public FileHandleReleaseException(string databaseFilePath, TimeSpan elapsedWait)
        : base($"Could not acquire exclusive access to '{databaseFilePath}' after {elapsedWait.TotalMilliseconds}ms")
    {
        DatabaseFilePath = databaseFilePath;
        ElapsedWait = elapsedWait;
    }
}