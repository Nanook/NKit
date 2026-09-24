using NKitDataStore;

namespace NkdsUi.Models;

/// <summary>
/// Tracks a single opened DataStore instance and its associated images.
/// Represents one Database Session in the application.
/// </summary>
public class ImageSessionModel : IDisposable
{
    /// <summary>
    /// Unique identifier for this session (GUID string).
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// The directory or .nkds file path that was opened.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The scoped set name when a specific .nkds file was opened; null for directory sessions.
    /// </summary>
    public string? ScopedSetName { get; }

    /// <summary>
    /// The opened DataStore instance for this session.
    /// </summary>
    public DataStore DataStore { get; }

    /// <summary>
    /// The images loaded from this session.
    /// </summary>
    public List<ImageRecord> Images { get; }

    public ImageSessionModel(string sessionId, string path, string? scopedSetName, DataStore dataStore, List<ImageRecord> images)
    {
        SessionId = sessionId;
        Path = path;
        ScopedSetName = scopedSetName;
        DataStore = dataStore;
        Images = images;
    }

    public void Dispose() => DataStore.Dispose();
}