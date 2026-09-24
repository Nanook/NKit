using NkdsUi.Models;
using NKitDataStore;

namespace NkdsUi.Services;

/// <summary>
/// Manages the lifecycle of opened DataStore instances (Database Sessions).
/// Provides reactive notifications when sessions are added or removed.
/// </summary>
public interface IDataStoreService
{
    /// <summary>
    /// Observable stream of all currently open sessions.
    /// Emits a new list whenever sessions are added or removed.
    /// </summary>
    IObservable<IReadOnlyList<ImageSessionModel>> Sessions { get; }

    /// <summary>
    /// Observable stream of all images across all open sessions.
    /// Emits a new combined list whenever sessions change.
    /// </summary>
    IObservable<IReadOnlyList<ImageRecord>> AllImages { get; }

    /// <summary>
    /// Observable stream of error messages from DataStore operations.
    /// Emits a message whenever an operation fails, allowing the UI to display notifications.
    /// </summary>
    IObservable<string> ErrorMessages { get; }

    /// <summary>
    /// Opens a DataStore directory, loading all non-removed images from every .nkds file.
    /// </summary>
    /// <param name="directoryPath">Path to the DataStore directory.</param>
    /// <returns>True if the directory was successfully opened; false if validation failed or it was a duplicate.</returns>
    Task<bool> OpenDirectoryAsync(string directoryPath);

    /// <summary>
    /// Opens a specific .nkds file, loading images scoped to that single set.
    /// </summary>
    /// <param name="filePath">Path to the .nkds file.</param>
    /// <returns>True if the file was successfully opened; false if validation failed or it was a duplicate.</returns>
    Task<bool> OpenFileAsync(string filePath);

    /// <summary>
    /// Closes a session by its identifier, disposing the associated DataStore and removing its images.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    void CloseSession(string sessionId);

    /// <summary>
    /// Checks whether the given path is already open as an existing session.
    /// Normalizes paths before comparison.
    /// </summary>
    /// <param name="path">The directory or file path to check.</param>
    /// <returns>True if the path is already open.</returns>
    bool IsAlreadyOpen(string path);

    /// <summary>
    /// Gets the number of currently open sessions.
    /// </summary>
    int SessionCount { get; }

    /// <summary>
    /// Re-queries the DataStore for the current set's images and updates
    /// the ImageSessionModel.Images list without closing the session.
    /// Returns the updated image list.
    /// </summary>
    /// <param name="sessionId">The session to refresh.</param>
    /// <param name="setName">Optional set name to scope the query. If null, loads all non-removed images.</param>
    /// <returns>The updated image list, or an empty list if the session was not found.</returns>
    Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string? setName = null);

    /// <summary>
    /// Gets the current DataStore instance from the active session.
    /// Returns null if no session is open.
    /// </summary>
    DataStore? GetActiveDataStore();

    /// <summary>
    /// Gets the active session's DataStore directory path.
    /// Returns null if no session is open.
    /// </summary>
    string? GetActiveDataStorePath();
}