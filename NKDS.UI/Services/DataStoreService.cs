using NkdsUi.Models;
using NKitDataStore;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.Services;

/// <summary>
/// Manages the lifecycle of opened DataStore instances (Database Sessions).
/// Provides reactive notifications when sessions are added or removed.
/// All DataStore operations are wrapped in try/catch - exceptions are logged
/// and surfaced as error messages without terminating the process.
/// </summary>
public class DataStoreService : IDataStoreService, IDisposable
{
    private const int MaxSessions = 16;

    private readonly object _lock = new();
    private readonly List<ImageSessionModel> _sessions = new();
    private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessionsSubject;
    private readonly ReplaySignal<IReadOnlyList<ImageRecord>> _allImagesSubject;
    private readonly ReplaySignal<string> _errorMessagesSubject;
    private ErrorNotificationService? _errorNotification;

    public DataStoreService()
    {
        _sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        _allImagesSubject = new ReplaySignal<IReadOnlyList<ImageRecord>>(1);
        _errorMessagesSubject = new ReplaySignal<string>();
        // Emit initial values (replaces BehaviorSubject initial value constructor)
        _sessionsSubject.OnNext(Array.Empty<ImageSessionModel>());
        _allImagesSubject.OnNext(Array.Empty<ImageRecord>());
    }

    /// <summary>
    /// Sets the ErrorNotificationService for centralized error reporting.
    /// Called after construction since DataStoreService is created before ErrorNotificationService in the registry.
    /// </summary>
    public void SetErrorNotification(ErrorNotificationService errorNotification) => _errorNotification = errorNotification;

    /// <inheritdoc />
    public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessionsSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<IReadOnlyList<ImageRecord>> AllImages => _allImagesSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<string> ErrorMessages => _errorMessagesSubject.AsObservable();

    /// <inheritdoc />
    public int SessionCount
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> OpenDirectoryAsync(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        string normalizedPath = NormalizePath(directoryPath);

        // Validate directory exists
        if (!Directory.Exists(directoryPath))
        {
            LogError($"Directory does not exist: {directoryPath}");
            PublishError($"Directory does not exist: {directoryPath}");
            return false;
        }

        // Validate directory contains .nkds files
        string[] nkdsFiles = Directory.GetFiles(directoryPath, $"*{DataStore.DatabaseFileExtension}");
        if (nkdsFiles.Length == 0)
        {
            LogError($"No .nkds files found in directory: {directoryPath}");
            PublishError($"No valid sets were found in directory: {directoryPath}");
            return false;
        }

        lock (_lock)
        {
            // Check for duplicate session
            if (IsAlreadyOpenInternal(normalizedPath))
                return false;

            // Enforce 16-session max
            if (_sessions.Count >= MaxSessions)
                return false;
        }

        // Create DataStore and load images on a background thread
        DataStore? dataStore = null;
        List<ImageRecord> images;
        try
        {
            (dataStore, images) = await Task.Run(() =>
            {
                DataStore ds = new DataStore(directoryPath);
                ds.RepairWiiUImageFormats();
                List<ImageRecord> imgs = ds.ListAllImagesIncludingRemoved().ToList();
                return (ds, imgs);
            });
        }
        catch (Exception ex)
        {
            LogError($"Failed to open DataStore directory '{directoryPath}': {ex.Message}");
            PublishError($"Failed to open directory: {ex.Message}");
            dataStore?.Dispose();
            return false;
        }

        ImageSessionModel session = new ImageSessionModel(
            sessionId: Guid.NewGuid().ToString(),
            path: normalizedPath,
            scopedSetName: null,
            dataStore: dataStore,
            images: images);

        lock (_lock)
        {
            // Double-check after async gap
            if (IsAlreadyOpenInternal(normalizedPath) || _sessions.Count >= MaxSessions)
            {
                session.Dispose();
                return false;
            }

            _sessions.Add(session);
        }

        PublishUpdates();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> OpenFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        string normalizedPath = NormalizePath(filePath);

        // Validate .nkds file exists
        if (!File.Exists(filePath))
        {
            LogError($"File does not exist: {filePath}");
            PublishError($"File does not exist: {filePath}");
            return false;
        }

        if (!filePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        lock (_lock)
        {
            // Check for duplicate session
            if (IsAlreadyOpenInternal(normalizedPath))
                return false;

            // Enforce 16-session max
            if (_sessions.Count >= MaxSessions)
                return false;
        }

        // Extract set name from filename (without extension)
        string setName = Path.GetFileNameWithoutExtension(filePath);

        // Create DataStore and load images on a background thread
        DataStore? dataStore = null;
        List<ImageRecord> images;
        try
        {
            (dataStore, images) = await Task.Run(() =>
            {
                DataStore ds = new DataStore(filePath);
                ds.RepairWiiUImageFormats();
                List<ImageRecord> imgs = ds.ListImagesInSetIncludingRemoved(setName).ToList();
                return (ds, imgs);
            });
        }
        catch (Exception ex)
        {
            LogError($"Failed to open .nkds file '{filePath}': {ex.Message}");
            PublishError($"Failed to open file: {ex.Message}");
            dataStore?.Dispose();
            return false;
        }

        ImageSessionModel session = new ImageSessionModel(
            sessionId: Guid.NewGuid().ToString(),
            path: normalizedPath,
            scopedSetName: setName,
            dataStore: dataStore,
            images: images);

        lock (_lock)
        {
            // Double-check after async gap
            if (IsAlreadyOpenInternal(normalizedPath) || _sessions.Count >= MaxSessions)
            {
                session.Dispose();
                return false;
            }

            _sessions.Add(session);
        }

        PublishUpdates();
        return true;
    }

    /// <inheritdoc />
    public void CloseSession(string sessionId)
    {
        ImageSessionModel? sessionToDispose = null;

        lock (_lock)
        {
            ImageSessionModel? session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null)
                return;

            _sessions.Remove(session);
            sessionToDispose = session;
        }

        sessionToDispose?.Dispose();
        PublishUpdates();
    }

    /// <inheritdoc />
    public bool IsAlreadyOpen(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalizedPath = NormalizePath(path);

        lock (_lock)
        {
            return IsAlreadyOpenInternal(normalizedPath);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string? setName = null)
    {
        ImageSessionModel? session;

        lock (_lock)
        {
            session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
        }

        if (session == null)
            return Array.Empty<ImageRecord>();

        try
        {
            List<ImageRecord> images = await Task.Run(() =>
            {
                if (setName != null)
                {
                    return session.DataStore.ListImagesInSetIncludingRemoved(setName).ToList();
                }
                else if (session.ScopedSetName != null)
                {
                    return session.DataStore.ListImagesInSetIncludingRemoved(session.ScopedSetName).ToList();
                }
                else
                {
                    return session.DataStore.ListAllImagesIncludingRemoved().ToList();
                }
            });

            // Update the session's Images list in-place
            lock (_lock)
            {
                session.Images.Clear();
                session.Images.AddRange(images);
            }

            PublishUpdates();
            return images.AsReadOnly();
        }
        catch (Exception ex)
        {
            LogError($"Failed to refresh session images for session '{sessionId}': {ex.Message}");
            PublishError($"Failed to refresh images: {ex.Message}");
            return Array.Empty<ImageRecord>();
        }
    }

    /// <inheritdoc />
    public DataStore? GetActiveDataStore()
    {
        lock (_lock)
        {
            return _sessions.FirstOrDefault()?.DataStore;
        }
    }

    /// <inheritdoc />
    public string? GetActiveDataStorePath()
    {
        lock (_lock)
        {
            return _sessions.FirstOrDefault()?.Path;
        }
    }

    /// <summary>
    /// Normalizes a path for comparison: resolves to full path, trims trailing separators,
    /// and converts to uppercase for case-insensitive comparison on Windows.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Case-insensitive on Windows
        if (OperatingSystem.IsWindows())
            fullPath = fullPath.ToUpperInvariant();

        return fullPath;
    }

    private bool IsAlreadyOpenInternal(string normalizedPath) => _sessions.Any(s => s.Path == normalizedPath);

    private void PublishUpdates()
    {
        IReadOnlyList<ImageSessionModel> sessionsCopy;
        IReadOnlyList<ImageRecord> allImages;

        lock (_lock)
        {
            sessionsCopy = _sessions.ToList().AsReadOnly();
            allImages = _sessions.SelectMany(s => s.Images).ToList().AsReadOnly();
        }

        _sessionsSubject.OnNext(sessionsCopy);
        _allImagesSubject.OnNext(allImages);
    }

    private void LogError(string message) => _errorNotification?.PublishOperationError(message);

    private void PublishError(string message) => _errorMessagesSubject.OnNext(message);

    public void Dispose()
    {
        List<ImageSessionModel> sessionsToDispose;

        lock (_lock)
        {
            sessionsToDispose = new List<ImageSessionModel>(_sessions);
            _sessions.Clear();
        }

        foreach (ImageSessionModel session in sessionsToDispose)
            session.Dispose();

        _sessionsSubject.OnCompleted();
        _sessionsSubject.Dispose();
        _allImagesSubject.OnCompleted();
        _allImagesSubject.Dispose();
        _errorMessagesSubject.OnCompleted();
        _errorMessagesSubject.Dispose();
    }
}