using NkdsUi.Models;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Services;

/// <summary>
/// Enforces single-session semantics at the UI level.
/// Closes any existing session before opening a new one.
/// Does not affect mount operations which bypass this layer.
/// </summary>
public class SessionManager : ReactiveObject, IDisposable
{
    private readonly IDataStoreService _dataStoreService;
    private readonly MultipleDisposable _disposables = new();

    private SessionInfo? _currentSession;
    private OpenState _currentState = OpenState.None;
    private bool _isLoading;
    private bool _isExclusiveAccessActive;
    private string _openStateText = "";
    private string _setDisplayText = "";
    private string _errorMessage = "";
    private bool _isFilterBarVisible;
    private bool _wasFilterBarActive;

    /// <summary>
    /// Optional hook invoked at the START of <see cref="Close"/>, before the session's DataStore is
    /// disposed. Wired by the owner (MainWindowViewModel) to unmount any active virtual-filesystem
    /// mounts first. A mount holds its OWN DataStore open on a background host thread; disposing the
    /// session (and the process-wide caches its readers share) while that host is still serving reads
    /// crashes. Running this first — and it blocks until each mount's host thread has fully exited —
    /// guarantees the correct teardown ORDER for EVERY close path: explicit Close, the implicit close
    /// when opening a new session, and WithExclusiveAccessAsync. Defaults to a no-op so SessionManager
    /// has no hard dependency on the mount layer and stays unit-testable.
    /// </summary>
    public Action? BeforeClose { get; set; }

    public SessionManager(IDataStoreService dataStoreService)
    {
        _dataStoreService = dataStoreService;
    }

    /// <summary>
    /// The current session state (None, Directory, or File).
    /// </summary>
    public OpenState CurrentState
    {
        get => _currentState;
        private set => this.RaiseAndSetIfChanged(ref _currentState, value);
    }

    /// <summary>
    /// Whether a load operation is in progress.
    /// Drives the mutual exclusivity: when true, the Progress Indicator is shown
    /// and the Filter Bar is hidden in the Title Bar row.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        internal set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// Whether an exclusive access operation is in progress (session temporarily closed
    /// for file access). When true, the UI should not react to intermediate session
    /// changes since the session will be restored when the operation completes.
    /// </summary>
    public bool IsExclusiveAccessActive
    {
        get => _isExclusiveAccessActive;
        internal set => this.RaiseAndSetIfChanged(ref _isExclusiveAccessActive, value);
    }

    /// <summary>
    /// Whether the Filter Bar is currently visible in the Title Bar row.
    /// False when IsLoading is true (Progress Indicator takes precedence).
    /// Also false when the user has not activated the filter bar.
    /// </summary>
    public bool IsFilterBarVisible
    {
        get => _isFilterBarVisible;
        set => this.RaiseAndSetIfChanged(ref _isFilterBarVisible, value);
    }

    /// <summary>
    /// Formatted text for the Title Bar display.
    /// Empty when no session is open.
    /// The folder path for directory mode.
    /// The folder path for file mode.
    /// </summary>
    public string OpenStateText
    {
        get => _openStateText;
        internal set => this.RaiseAndSetIfChanged(ref _openStateText, value);
    }

    /// <summary>
    /// Formatted set display text for the Title Bar.
    /// Empty when no session is open.
    /// "All" for directory mode.
    /// The set name for file mode.
    /// </summary>
    public string SetDisplayText
    {
        get => _setDisplayText;
        set => this.RaiseAndSetIfChanged(ref _setDisplayText, value);
    }

    /// <summary>
    /// Whether a session is currently open (directory or file mode).
    /// </summary>
    public bool HasSession => CurrentState != OpenState.None;

    /// <summary>
    /// Error message to display in the title bar. Empty when no error.
    /// Auto-clears after 5 seconds.
    /// </summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Whether an error message is currently being displayed.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Whether the current session is in directory mode.
    /// </summary>
    public bool IsDirectoryMode => CurrentState == OpenState.Directory;

    /// <summary>
    /// Opens a directory session, closing any existing session first.
    /// No confirmation dialog is shown.
    /// Saves and restores filter bar visibility across the loading period.
    /// </summary>
    public async Task<bool> OpenDirectoryAsync(string path)
    {
        if (HasSession)
        {
            Close();
        }

        beginLoading();
        try
        {
            bool success = await _dataStoreService.OpenDirectoryAsync(path);
            if (success)
            {
                // After successful open in single-session mode, get the session info
                IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();
                ImageSessionModel? session = sessions.LastOrDefault();

                string sessionId = session?.SessionId ?? "";
                _currentSession = new SessionInfo(sessionId, path, null, OpenState.Directory, path);
                CurrentState = OpenState.Directory;
                OpenStateText = path;
                SetDisplayText = "All";
                return true;
            }
            else
            {
                // Open failed (e.g., no sets in folder) — still show the path
                _currentSession = new SessionInfo("", path, null, OpenState.Directory, path);
                CurrentState = OpenState.Directory;
                OpenStateText = path;
                SetDisplayText = "All";
                ShowError("No sets found in folder");
                return false;
            }
        }
        catch (Exception ex)
        {
            _currentSession = null;
            CurrentState = OpenState.None;
            OpenStateText = "";
            SetDisplayText = "";
            ShowError($"Failed to open: {ex.Message}");
            return false;
        }
        finally
        {
            endLoading();
        }
    }

    /// <summary>
    /// Opens a file session, closing any existing session first.
    /// No confirmation dialog is shown.
    /// Saves and restores filter bar visibility across the loading period.
    /// </summary>
    public async Task<bool> OpenFileAsync(string filePath)
    {
        if (HasSession)
        {
            Close();
        }

        beginLoading();
        try
        {
            bool success = await _dataStoreService.OpenFileAsync(filePath);
            if (success)
            {
                // After successful open in single-session mode, get the session info
                IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();
                ImageSessionModel? session = sessions.LastOrDefault();

                string sessionId = session?.SessionId ?? "";
                string folderPath = Path.GetDirectoryName(filePath) ?? filePath;
                string setName = Path.GetFileNameWithoutExtension(filePath);
                _currentSession = new SessionInfo(sessionId, folderPath, setName, OpenState.File, filePath);
                CurrentState = OpenState.File;
                OpenStateText = folderPath;
                SetDisplayText = setName;
                return true;
            }
            else
            {
                _currentSession = null;
                CurrentState = OpenState.None;
                OpenStateText = "";
                SetDisplayText = "";
                ShowError("Failed to open set file");
                return false;
            }
        }
        catch (Exception ex)
        {
            _currentSession = null;
            CurrentState = OpenState.None;
            OpenStateText = "";
            SetDisplayText = "";
            ShowError($"Failed to open: {ex.Message}");
            return false;
        }
        finally
        {
            endLoading();
        }
    }

    /// <summary>
    /// Closes the current session without confirmation.
    /// Sets state to None and clears OpenStateText.
    /// </summary>
    public void Close()
    {
        if (!HasSession)
            return;

        // Tear down any active mounts BEFORE disposing the DataStore. This is the single point that
        // enforces "unmount before close" for every close path. The hook blocks until each mount's
        // host thread has exited, so nothing is still reading when CloseSession disposes the store.
        try
        {
            BeforeClose?.Invoke();
        }
        catch
        {
            // A mount teardown failure must never block the session close — the DataStore still needs
            // to be released. The mount layer logs its own errors.
        }

        // Close the tracked session by ID
        if (_currentSession != null && !string.IsNullOrEmpty(_currentSession.SessionId))
        {
            _dataStoreService.CloseSession(_currentSession.SessionId);
        }
        else
        {
            // Fallback: close all sessions (handles cases where session ID wasn't captured)
            IReadOnlyList<ImageSessionModel> sessions = _dataStoreService.Sessions.FirstAsync().GetAwaiter().GetResult();
            foreach (ImageSessionModel? session in sessions)
                _dataStoreService.CloseSession(session.SessionId);
        }

        CurrentState = OpenState.None;
        OpenStateText = "";
        SetDisplayText = "";
        _currentSession = null;
    }

    /// <summary>
    /// Returns the normalized path of the currently open folder,
    /// or null if no session is open.
    /// Used by 1GMR to determine if session switch is needed.
    /// </summary>
    public string? GetCurrentFolderPath()
    {
        if (_currentSession is null)
            return null;

        string fullPath = Path.GetFullPath(_currentSession.FolderPath);
        // Ensure consistent trailing directory separator
        if (!fullPath.EndsWith(Path.DirectorySeparatorChar))
            fullPath += Path.DirectorySeparatorChar;
        return fullPath;
    }

    /// <summary>
    /// Returns the original path that was used to open the current session.
    /// For directory mode: the folder path. For file mode: the .nkds file path.
    /// Returns null if no session is open.
    /// </summary>
    public string? GetOriginalOpenPath() => _currentSession?.OriginalOpenPath;

    /// <summary>
    /// Closes the current session, performs an action, then reopens the same session.
    /// Ensures file handles are released during the action and the session is restored after.
    /// This is the canonical way for operations to get exclusive file access.
    /// Preserves OpenStateText and SetDisplayText during the operation so the title bar
    /// doesn't blank out while the operation is in progress.
    /// </summary>
    public async Task WithExclusiveAccessAsync(Func<Task> action)
    {
        if (!HasSession)
        {
            await action();
            return;
        }

        SessionInfo savedSession = _currentSession!;
        string savedOpenStateText = OpenStateText;
        string savedSetDisplay = SetDisplayText;
        OpenState savedState = CurrentState;

        IsExclusiveAccessActive = true;
        Close();

        // Restore title bar text immediately so it doesn't blank during the operation.
        // CurrentState stays None (buttons correctly disabled) but labels remain visible.
        OpenStateText = savedOpenStateText;
        SetDisplayText = savedSetDisplay;

        try
        {
            await action();
        }
        finally
        {
            // Clear the exclusive access flag BEFORE reopening so that the reopen's
            // PublishUpdates emission is processed normally by onSessionsUpdated.
            IsExclusiveAccessActive = false;

            // Reopen in the same mode
            if (savedSession.Mode == OpenState.File)
                await OpenFileAsync(savedSession.OriginalOpenPath);
            else
                await OpenDirectoryAsync(savedSession.OriginalOpenPath);

            // Restore set display if it was customized (OpenFileAsync/OpenDirectoryAsync
            // may have set it to a default value)
            if (savedSetDisplay != "All" && !string.IsNullOrEmpty(savedSetDisplay))
                SetDisplayText = savedSetDisplay;
        }
    }

    /// <summary>
    /// Called at the start of a loading operation.
    /// Saves the current filter bar state and hides it,
    /// then shows the progress indicator.
    /// </summary>
    private void beginLoading()
    {
        _wasFilterBarActive = IsFilterBarVisible;
        IsFilterBarVisible = false;
        IsLoading = true;
    }

    /// <summary>
    /// Called when a loading operation completes (success or failure).
    /// Hides the progress indicator and restores the filter bar
    /// if it was previously active.
    /// </summary>
    private void endLoading()
    {
        IsLoading = false;
        IsFilterBarVisible = _wasFilterBarActive;
    }

    public void Dispose() => _disposables.Dispose();

    /// <summary>
    /// Shows an error message in the title bar that auto-clears after the specified duration.
    /// </summary>
    public void ShowError(string message, TimeSpan? duration = null)
    {
        ErrorMessage = message;
        this.RaisePropertyChanged(nameof(HasError));
        TimeSpan clearAfter = duration ?? TimeSpan.FromSeconds(5);
        Task.Delay(clearAfter).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ErrorMessage = "";
                this.RaisePropertyChanged(nameof(HasError));
            });
        });
    }
}