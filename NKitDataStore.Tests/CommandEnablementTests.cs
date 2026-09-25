using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for toolbar command enablement states.
/// Validates: Requirements 4.1, 4.2, 4.3, 5.4
/// </summary>
public class CommandEnablementTests : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public CommandEnablementTests()
    {
        EnsureReactiveUIInitialized();
        NkdsUi.RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => NkdsUi.RxSchedulers.SetSchedulerForTest(null);

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

    // --- CreateSetCommand Tests (Requirement 4.1, 4.2, 4.3) ---

    [Fact]
    public async Task CreateSetCommand_IsEnabled_WhenCurrentState_IsDirectory()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenDirectoryAsync(@"C:\TestFolder");

        Assert.True(await vm.CreateSetCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task CreateSetCommand_IsEnabled_WhenCurrentState_IsNone()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        // CreateSet is always enabled — it shows a folder picker if no session is open
        Assert.True(await vm.CreateSetCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task CreateSetCommand_IsEnabled_WhenCurrentState_IsFile()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenFileAsync(@"C:\TestFolder\set.nkds");

        // CreateSet is always enabled regardless of session mode
        Assert.True(await vm.CreateSetCommand.CanExecute.FirstAsync());
    }

    // --- CloseSetCommand Tests (Requirement 5.4) ---

    [Fact]
    public async Task CloseSetCommand_IsEnabled_WhenCurrentState_IsDirectory()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenDirectoryAsync(@"C:\TestFolder");

        Assert.True(await vm.CloseSetCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task CloseSetCommand_IsEnabled_WhenCurrentState_IsFile()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenFileAsync(@"C:\TestFolder\set.nkds");

        Assert.True(await vm.CloseSetCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task CloseSetCommand_IsDisabled_WhenCurrentState_IsNone()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        // Default state is None
        Assert.False(await vm.CloseSetCommand.CanExecute.FirstAsync());
    }

    // --- AddCommand Tests (Requirements 4.1, 5.4 — requires session AND active set) ---

    [Fact]
    public async Task AddCommand_IsEnabled_WhenSessionOpen_AndHasActiveSet()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenDirectoryAsync(@"C:\TestFolder");
        vm.UpdateAvailableSetNames(new[] { "TestSet" });
        vm.SelectedSetName = "TestSet";

        Assert.True(await vm.AddCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task AddCommand_IsDisabled_WhenNoSession_EvenIfHasActiveSet()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        // No session (state is None), but set HasActiveSet
        vm.UpdateAvailableSetNames(new[] { "TestSet" });
        vm.SelectedSetName = "TestSet";

        Assert.False(await vm.AddCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task AddCommand_IsDisabled_WhenSessionOpen_ButOperationActive()
    {
        using SessionManager sessionManager = new SessionManager(new FakeDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        await sessionManager.OpenDirectoryAsync(@"C:\TestFolder");
        vm.UpdateAvailableSetNames(new[] { "TestSet" });
        vm.SelectedSetName = "TestSet";

        // Simulate an active operation — disables all mutating commands
        vm.IsOperationActive = true;

        Assert.False(await vm.AddCommand.CanExecute.FirstAsync());
    }

    /// <summary>
    /// Fake IDataStoreService that tracks sessions for constructing SessionManager in tests.
    /// </summary>
    private sealed class FakeDataStoreService : IDataStoreService
    {
        private readonly List<ImageSessionModel> _sessions = new();
        private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessionsSubject;

        public FakeDataStoreService()
        {
            _sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        }

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessionsSubject.AsObservable();
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });
        public int SessionCount => _sessions.Count;

        public Task<bool> OpenDirectoryAsync(string directoryPath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, directoryPath, null, null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
            return Task.FromResult(true);
        }

        public Task<bool> OpenFileAsync(string filePath)
        {
            string sessionId = Guid.NewGuid().ToString();
            ImageSessionModel session = new ImageSessionModel(sessionId, filePath, Path.GetFileNameWithoutExtension(filePath), null!, new List<ImageRecord>());
            _sessions.Add(session);
            _sessionsSubject.OnNext(_sessions.AsReadOnly());
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
        }

        public bool IsAlreadyOpen(string path) => false;
        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }
}