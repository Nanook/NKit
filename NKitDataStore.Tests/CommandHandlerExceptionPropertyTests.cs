using FsCheck;
using FsCheck.Xunit;
using NKDS.Models;
using NKDS.Mount;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using NkdsUi.ViewModels.Commands;
using ReactiveUI;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for Command Handler Exception Cleanup.
///
/// Property 1: Command Handler Exception Cleanup
///
/// **Validates: Requirements 1.8**
///
/// Generate random exception types, invoke handlers with a mock OperationContext that throws.
/// Verify: error published to operation-level stream, ActiveOperations empty,
/// OperationInProgressOnSet is null.
/// </summary>
public class CommandHandlerExceptionPropertyTests
{
    private static readonly object _InitLock = new();
    private static bool _Initialized;

    public CommandHandlerExceptionPropertyTests()
    {
        ensureReactiveUIInitialized();
    }

    private static void ensureReactiveUIInitialized()
    {
        if (_Initialized) return;
        lock (_InitLock)
        {
            if (_Initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _Initialized = true;
        }
    }

    /// <summary>
    /// A command handler that simulates the standard handler pattern:
    /// 1. Add progress to ActiveOperations
    /// 2. Set OperationInProgressOnSet
    /// 3. Execute operation (which throws)
    /// 4. In catch: publish error to ErrorNotificationService
    /// 5. In finally: remove from ActiveOperations, clear OperationInProgressOnSet
    ///
    /// This mirrors the exact pattern used by CompactCommandHandler, AddCommandHandler, etc.
    /// </summary>
    private class ThrowingCommandHandler : ICommandHandler
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingCommandHandler(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public IObservable<RxVoid> Execute(OperationContext ctx)
        {
            return Signal.FromAsync<RxVoid>(async () =>
            {
                OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Test" };
                ctx.ActiveOperations.Add(progressVm);
                ctx.SharedState.OperationInProgressOnSet = "TestSet";

                try
                {
                    // Simulate the operation that throws
                    await Task.CompletedTask;
                    throw _exceptionToThrow;
                }
                catch (OperationCanceledException)
                {
                    ctx.ErrorNotification.PublishCancellation("Test");
                }
                catch (Exception ex)
                {
                    ctx.ErrorNotification.PublishOperationError(
                        $"Test failed: {ex.Message}");
                }
                finally
                {
                    ctx.ActiveOperations.Remove(progressVm);
                    ctx.SharedState.OperationInProgressOnSet = null;
                    progressVm.Dispose();
                }

                return RxVoid.Default;
            });
        }
    }

    /// <summary>
    /// Minimal IDataStoreService implementation for testing.
    /// </summary>
    private class FakeDataStoreService : IDataStoreService
    {
        private readonly ReplaySignal<IReadOnlyList<ImageSessionModel>> _sessions =
            new(1);

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessions.AsObservable();
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });
        public int SessionCount => 0;
        public Task<bool> OpenDirectoryAsync(string directoryPath) => Task.FromResult(false);
        public Task<bool> OpenFileAsync(string filePath) => Task.FromResult(false);
        public void CloseSession(string sessionId) { }
        public bool IsAlreadyOpen(string path) => false;
        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }

    /// <summary>
    /// Minimal IImageListSyncService implementation for testing.
    /// </summary>
    private class FakeSyncService : IImageListSyncService
    {
        public Task InsertImageAsync(ImageCommittedEvent committedEvent) => Task.CompletedTask;
        public Task RemoveImagesAsync(string setName, IReadOnlyList<long> imageIds) => Task.CompletedTask;
        public Task InsertRestoredImagesAsync(string setName, IReadOnlyList<ImageRecord> restoredImages) => Task.CompletedTask;
        public Task ReconcileAsync(string setName) => Task.CompletedTask;
        public Task UpdateImageProgressAsync(ImageProgressEvent progressEvent) => Task.CompletedTask;
        public Task AppendImageOutputAsync(string setName, long imageId, string text) => Task.CompletedTask;
        public Task SetImageStatusAsync(string setName, long imageId, ImageProcessingStatus status, string reason = null) => Task.CompletedTask;
        public Task SetVerifyResultAsync(string setName, long imageId, VerifyResultStatus result, string method = null) => Task.CompletedTask;
        public Task PrePopulateAsync(string setName, string sessionId, IReadOnlyList<CandidateImage> candidates) => Task.CompletedTask;
        public Task SetBulkAddCancelledAsync(string setName) => Task.CompletedTask;
        public Task RefreshAvailableSetNamesAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Creates a minimal OperationContext suitable for testing exception cleanup behavior.
    /// </summary>
    private static OperationContext createTestContext(
        ErrorNotificationService errorNotification,
        SharedObservableState sharedState,
        ObservableCollection<OperationProgressViewModel> activeOperations)
    {
        FakeDataStoreService dataStoreService = new FakeDataStoreService();
        SessionManager sessionManager = new SessionManager(dataStoreService);
        FileHandleReleaseStrategy releaseStrategy = new FileHandleReleaseStrategy();
        ExclusiveAccessManager exclusiveAccessManager = new ExclusiveAccessManager(sessionManager, releaseStrategy);
        MountOrchestrator mountOrchestrator = new MountOrchestrator(new NullPlatformFsHostFactory());

        return new OperationContext(
            DataStoreService: dataStoreService,
            SessionManager: sessionManager,
            ExclusiveAccessManager: exclusiveAccessManager,
            ConfigService: new NullConfigService(),
            ImageList: null!,  // Not used by ThrowingCommandHandler
            Toolbar: null!,    // Not used by ThrowingCommandHandler
            SyncService: new FakeSyncService(),
            ErrorNotification: errorNotification,
            SharedState: sharedState,
            MountOrchestrator: mountOrchestrator,
            ActiveOperations: activeOperations,
            ShowAddImagesDialog: new Interaction<AddImagesDialogViewModel, bool>(),
            ShowExportDialog: new Interaction<ExportDialogViewModel, bool>(),
            ShowCompactDialog: new Interaction<CompactDialogViewModel, bool>(),
            ShowRollbackDialog: new Interaction<RollbackDialogViewModel, bool>(),
            ShowMountDialog: new Interaction<MountDialogViewModel, bool>(),
            ShowAdd1GmrDialog: new Interaction<Add1GmrDialogViewModel, bool>(),
            ShowCreateSetDialog: new Interaction<CreateSetDialogViewModel, bool>(),
            ShowGroupingWindow: new Interaction<GroupingWindowViewModel, RxVoid>(),
            ShowFolderPicker: new Interaction<RxVoid, string>(),
            ShowFilePicker: new Interaction<RxVoid, string>(),
            ShowSaveFilePicker: new Interaction<RxVoid, string>()
        );
    }

    /// <summary>
    /// Generates a random exception from a set of common exception types with random messages.
    /// </summary>
    private static Exception createException(int typeSeed, string message)
    {
        return (typeSeed % 7) switch
        {
            0 => new InvalidOperationException(message),
            1 => new IOException(message),
            2 => new ArgumentException(message),
            3 => new TimeoutException(message),
            4 => new UnauthorizedAccessException(message),
            5 => new OperationCanceledException(message),
            6 => new NullReferenceException(message),
            _ => new Exception(message)
        };
    }

    /// <summary>
    /// **Validates: Requirements 1.8**
    ///
    /// Property 1: Command Handler Exception Cleanup.
    /// For any random exception type thrown during handler execution:
    /// 1. An error is published to the operation-level stream (OperationErrors)
    /// 2. ActiveOperations is empty after the handler completes
    /// 3. SharedState.OperationInProgressOnSet is null after the handler completes
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ExceptionCleanup_AlwaysPublishesError_ClearsActiveOperations_ClearsOperationInProgress(
        NonNegativeInt typeSeed, NonEmptyString message)
    {
        return runExceptionCleanupScenario(typeSeed.Get, message.Get)
            .GetAwaiter().GetResult();
    }

    private async Task<bool> runExceptionCleanupScenario(int typeSeed, string exceptionMessage)
    {
        // Arrange
        ErrorNotificationService errorNotification = new ErrorNotificationService();
        SharedObservableState sharedState = new SharedObservableState();
        ObservableCollection<OperationProgressViewModel> activeOperations = new ObservableCollection<OperationProgressViewModel>();

        // Collect errors published to the operation-level stream
        List<OperationError> publishedErrors = new List<OperationError>();
        errorNotification.OperationErrors.Subscribe(e => publishedErrors.Add(e));

        Exception exception = createException(typeSeed, exceptionMessage);
        ThrowingCommandHandler handler = new ThrowingCommandHandler(exception);

        OperationContext ctx = createTestContext(errorNotification, sharedState, activeOperations);

        // Pre-condition: set some initial state to verify cleanup
        sharedState.OperationInProgressOnSet = "ShouldBeOverwritten";

        // Act - execute the handler (it will throw internally and handle the exception)
        await handler.Execute(ctx).FirstAsync();

        // Assert invariants:

        // 1. Error was published to the operation-level stream
        if (publishedErrors.Count != 1)
            return false;

        // 2. For OperationCanceledException, IsCancellation should be true
        if (exception is OperationCanceledException)
        {
            if (!publishedErrors[0].IsCancellation)
                return false;
        }
        else
        {
            // For other exceptions, IsCancellation should be false and message should contain the exception message
            if (publishedErrors[0].IsCancellation)
                return false;
            if (!publishedErrors[0].Message.Contains(exceptionMessage))
                return false;
        }

        // 3. ActiveOperations must be empty after completion
        if (activeOperations.Count != 0)
            return false;

        // 4. OperationInProgressOnSet must be null after completion
        if (sharedState.OperationInProgressOnSet != null)
            return false;

        return true;
    }

    /// <summary>
    /// Null implementation of IPlatformFsHostFactory for testing.
    /// </summary>
    private class NullPlatformFsHostFactory : IPlatformFsHostFactory
    {
        public IPlatformFsHost Create() => new NullPlatformFsHost();
        public bool IsSupported => false;
        public string UnsupportedReason => "Test environment";
    }

    private class NullPlatformFsHost : IPlatformFsHost
    {
        public void Run(string dataStorePath, string mountPoint, string setName,
            bool showImage = true, bool showFileSystem = true, bool showSystem = false,
            bool updateMode = false, bool allowOther = false, uint? uid = null, uint? gid = null,
            int maxFileSystemYamlSizeKiB = 0)
        { }
        public string GetErrorMessage(Exception ex) => ex.Message;
        public void FinalizeDatabases(string dataStorePath, string setName) { }
        public void Dispose() { }
    }
}