using FsCheck;
using FsCheck.Xunit;
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
/// Property-based tests for toolbar item completeness and validity.
///
/// Feature: nkds-ui-layout-rework, Property 2: Toolbar Item Completeness and Validity
/// **Validates: Requirements 5.2, 5.4**
/// </summary>
public class ToolbarItemCompletenessPropertyTests : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ToolbarItemCompletenessPropertyTests()
    {
        EnsureReactiveUIInitialized();
        // Override the scheduler so ToolbarViewModel can be constructed without Avalonia dispatcher
        NkdsUi.RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => NkdsUi.RxSchedulers.SetSchedulerForTest(null);

    /// <summary>
    /// Ensures ReactiveUI is initialized once for the test assembly.
    /// </summary>
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
    /// **Validates: Requirements 5.2, 5.4**
    ///
    /// Property 2: Toolbar Item Completeness and Validity.
    /// For any combination of toolbar state flags (HasDataStoreOpen, HasActiveSet,
    /// HasSelectedImages, HasRemovedImages), every ToolbarItemViewModel in the
    /// ToolbarViewModel.Items collection SHALL have:
    /// - A non-null, non-empty Tooltip string
    /// - A non-null, non-empty IconName string
    /// - A non-null Command
    /// - IsEnabled matching Command.CanExecute(null)
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AllToolbarItems_HaveValidTooltip_IconName_Command_And_IsEnabledMatchesCanExecute(
        bool hasDataStoreOpen,
        bool hasActiveSet,
        bool hasSelectedImages,
        bool hasRemovedImages)
    {
        using SessionManager sessionManager = new SessionManager(new StubDataStoreService());
        using ToolbarViewModel vm = new ToolbarViewModel(sessionManager, new SharedObservableState());

        // Apply the generated state to the ViewModel
        vm.HasDataStoreOpen = hasDataStoreOpen;
        if (hasActiveSet)
        {
            vm.UpdateAvailableSetNames(new[] { "TestSet" });
            vm.SelectedSetName = "TestSet";
        }
        else
        {
            vm.SelectedSetName = null;
        }
        vm.HasSelectedImages = hasSelectedImages;
        vm.HasRemovedImages = hasRemovedImages;

        // Verify every item in the collection satisfies the completeness property
        foreach (ToolbarItemViewModel item in vm.Items)
        {
            // Tooltip must be non-null and non-empty
            if (string.IsNullOrEmpty(item.Tooltip))
                return false;

            // IconName must be non-null and non-empty
            if (string.IsNullOrEmpty(item.IconName))
                return false;

            // Command must be non-null
            if (item.Command == null)
                return false;

            // IsEnabled must match Command.CanExecute
            bool canExecute = item.Command.CanExecute(null);
            if (item.IsEnabled != canExecute)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Minimal stub IDataStoreService for constructing SessionManager in tests.
    /// </summary>
    private sealed class StubDataStoreService : IDataStoreService
    {
        private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessions = new(1);

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessions.AsObservable();
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });
        public int SessionCount => 0;

        public Task<bool> OpenDirectoryAsync(string directoryPath) => Task.FromResult(true);
        public Task<bool> OpenFileAsync(string filePath) => Task.FromResult(true);
        public void CloseSession(string sessionId) { }
        public bool IsAlreadyOpen(string path) => false;
        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }
}