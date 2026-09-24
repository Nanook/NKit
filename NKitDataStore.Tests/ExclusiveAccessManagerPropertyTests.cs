using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for ExclusiveAccessManager lifecycle ordering.
///
/// Property 8: Exclusive Access Lifecycle Ordering
///
/// **Validates: Requirements 5.1, 5.4, 5.5, 5.6, 6.4**
///
/// Generate random session states (directory/file mode, various paths).
/// Execute WithExclusiveAccessAsync with a mock delegate.
/// Verify the call sequence: close → release → execute → reopen,
/// with IsExclusiveAccessActive correctly toggled.
/// </summary>
public class ExclusiveAccessManagerPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ExclusiveAccessManagerPropertyTests()
    {
        EnsureReactiveUIInitialized();
    }

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _initialized = true;
        }
    }

    /// <summary>
    /// Records lifecycle events in order for verification.
    /// </summary>
    private enum LifecycleEvent
    {
        ExclusiveAccessActivated,
        SessionClosed,
        DelegateExecuted,
        ExclusiveAccessDeactivated,
        SessionReopened
    }

    /// <summary>
    /// Fake IDataStoreService that tracks session operations and records lifecycle events.
    /// </summary>
    private class RecordingDataStoreService : IDataStoreService
    {
        private readonly List<ImageSessionModel> _sessions = new();
        private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessionsSubject;
        private readonly List<LifecycleEvent> _events;

        public RecordingDataStoreService(List<LifecycleEvent> events)
        {
            _events = events;
            _sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        }

        public int SessionCount => _sessions.Count;

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessionsSubject.AsObservable();
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });

        public Task<bool> OpenDirectoryAsync(string directoryPath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, directoryPath, null, null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
            _events.Add(LifecycleEvent.SessionReopened);
            return Task.FromResult(true);
        }

        public Task<bool> OpenFileAsync(string filePath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, filePath, Path.GetFileNameWithoutExtension(filePath), null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
            _events.Add(LifecycleEvent.SessionReopened);
            return Task.FromResult(true);
        }

        public void CloseSession(string sessionId)
        {
            ImageSessionModel session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session != null)
            {
                _sessions.Remove(session);
                _sessionsSubject.OnNext(_sessions.AsReadOnly());
            }
            _events.Add(LifecycleEvent.SessionClosed);
        }

        public bool IsAlreadyOpen(string path) => _sessions.Any(s => s.Path == path);

        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());

        public DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.4, 5.5, 5.6, 6.4**
    ///
    /// Property 8: Exclusive Access Lifecycle Ordering.
    /// For any random session state (directory or file mode, various paths),
    /// when WithExclusiveAccessAsync is called with a session open:
    /// 1. IsExclusiveAccessActive is set to true
    /// 2. Session is closed
    /// 3. File handles are released (ReleaseHandlesAsync called)
    /// 4. Operation delegate is executed
    /// 5. IsExclusiveAccessActive is set to false
    /// 6. Session is reopened in original mode
    ///
    /// Uses NonNegativeInt seeds to generate random scenarios deterministically.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool LifecycleOrdering_IsCorrect_ForAnySessionState(
        NonNegativeInt modeSeed, NonNegativeInt pathSeed, NonNegativeInt delegateSeed)
    {
        // Generate random scenario from seeds
        bool isDirectoryMode = modeSeed.Get % 2 == 0;
        bool delegateSucceeds = delegateSeed.Get % 2 == 0;

        return RunLifecycleOrderingScenario(isDirectoryMode, pathSeed.Get, delegateSucceeds)
            .GetAwaiter().GetResult();
    }

    private async Task<bool> RunLifecycleOrderingScenario(
        bool isDirectoryMode, int pathSeed, bool delegateSucceeds)
    {
        // Track lifecycle events in order
        List<LifecycleEvent> events = new List<LifecycleEvent>();

        RecordingDataStoreService fakeService = new RecordingDataStoreService(events);
        SessionManager sessionManager = new SessionManager(fakeService);

        // Create a temp .nkds file so ResolveDatabaseFilePath can find it
        string tempDir = Path.Combine(Path.GetTempPath(), $"eam_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempNkdsFile = Path.Combine(tempDir, "test.nkds");
        await File.WriteAllBytesAsync(tempNkdsFile, new byte[] { 0x00 });

        try
        {
            // Open a session in the specified mode
            if (isDirectoryMode)
            {
                await sessionManager.OpenDirectoryAsync(tempDir);
            }
            else
            {
                await sessionManager.OpenFileAsync(tempNkdsFile);
            }

            // Verify session is open
            if (!sessionManager.HasSession)
                return false;

            // Clear events from the initial open
            events.Clear();

            // Subscribe to IsExclusiveAccessActive changes to track ordering
            sessionManager.WhenAnyValue(x => x.IsExclusiveAccessActive)
                .Skip(1) // Skip initial value
                .Subscribe(value =>
                {
                    if (value)
                        events.Add(LifecycleEvent.ExclusiveAccessActivated);
                    else
                        events.Add(LifecycleEvent.ExclusiveAccessDeactivated);
                });

            // Create the ExclusiveAccessManager with a real FileHandleReleaseStrategy
            // (the temp file has no locks, so it will succeed immediately)
            FileHandleReleaseStrategy releaseStrategy = new FileHandleReleaseStrategy();
            ExclusiveAccessManager manager = new ExclusiveAccessManager(sessionManager, releaseStrategy);

            // Track when the delegate executes relative to other events
            bool delegateWasExecuted = false;
            Exception thrownException = null;

            try
            {
                await manager.WithExclusiveAccessAsync(tempDir, async path =>
                {
                    events.Add(LifecycleEvent.DelegateExecuted);
                    delegateWasExecuted = true;

                    if (!delegateSucceeds)
                        throw new InvalidOperationException("Simulated delegate failure");
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == "Simulated delegate failure")
            {
                thrownException = ex;
            }

            // Verify delegate behavior
            if (delegateSucceeds && !delegateWasExecuted)
                return false;
            if (!delegateSucceeds && thrownException == null)
                return false;

            // Verify the lifecycle ordering.
            // The ReleaseHandlesAsync call happens between SessionClosed and DelegateExecuted
            // but since it's not virtual we can't directly observe it. We verify the
            // observable ordering:
            // ExclusiveAccessActivated -> SessionClosed -> DelegateExecuted -> ExclusiveAccessDeactivated -> SessionReopened

            // Find indices of key events
            int activatedIdx = events.IndexOf(LifecycleEvent.ExclusiveAccessActivated);
            int closedIdx = events.IndexOf(LifecycleEvent.SessionClosed);
            int delegateIdx = events.IndexOf(LifecycleEvent.DelegateExecuted);
            int deactivatedIdx = events.IndexOf(LifecycleEvent.ExclusiveAccessDeactivated);
            int reopenedIdx = events.IndexOf(LifecycleEvent.SessionReopened);

            // All events must be present
            if (activatedIdx < 0 || closedIdx < 0 || delegateIdx < 0 ||
                deactivatedIdx < 0 || reopenedIdx < 0)
                return false;

            // Verify strict ordering: activated < closed < delegate < deactivated < reopened
            if (!(activatedIdx < closedIdx))
                return false;
            if (!(closedIdx < delegateIdx))
                return false;
            if (!(delegateIdx < deactivatedIdx))
                return false;
            if (!(deactivatedIdx < reopenedIdx))
                return false;

            // Verify IsExclusiveAccessActive is false after completion
            if (sessionManager.IsExclusiveAccessActive)
                return false;

            // Verify session was reopened (HasSession is true)
            if (!sessionManager.HasSession)
                return false;

            // Verify session was reopened in the correct mode
            if (isDirectoryMode && !sessionManager.IsDirectoryMode)
                return false;
            if (!isDirectoryMode && sessionManager.IsDirectoryMode)
                return false;

            return true;
        }
        finally
        {
            sessionManager.Dispose();
            // Cleanup temp files
            try { File.Delete(tempNkdsFile); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}