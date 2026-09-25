using NKDS.Models;
using NKDS.Mount;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using NkdsUi.ViewModels.Commands;
using ReactiveUI;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for CreateSetCommandHandler modified flow.
/// Validates: Requirements 6.1, 6.2, 6.3, 6.4
/// </summary>
[Collection("RxScheduler Sequential Tests")]
public class CreateSetInlineFolderCommandHandlerTests : IDisposable
{
    private static readonly object _InitLock = new();
    private static bool _Initialized;

    public CreateSetInlineFolderCommandHandlerTests()
    {
        ensureReactiveUIInitialized();
        NkdsUi.RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => NkdsUi.RxSchedulers.SetSchedulerForTest(null);

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
    /// Requirement 6.1: When the user invokes Create Set with no DataStore session active,
    /// the handler SHALL open the Create_Set_Dialog directly without invoking ShowFolderPicker,
    /// passing null dataStorePath to CreateSetDialogViewModel.
    /// </summary>
    [Fact]
    public async Task Execute_NoSession_OpensDialogWithNullPath()
    {
        // Arrange
        ReplaySignal<IReadOnlyList<ImageSessionModel>> sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        sessionsSubject.OnNext(Array.Empty<ImageSessionModel>());
        FakeDataStoreService fakeDataStoreService = new FakeDataStoreService(sessionsSubject);
        ErrorNotificationService errorNotification = new ErrorNotificationService();
        Interaction<CreateSetDialogViewModel, bool> showCreateSetDialog = new Interaction<CreateSetDialogViewModel, bool>();

        CreateSetDialogViewModel capturedVm = null;
        showCreateSetDialog.RegisterHandler(interaction =>
        {
            capturedVm = interaction.Input;
            interaction.SetOutput(false); // Cancel the dialog
        });

        OperationContext ctx = createTestContext(
            fakeDataStoreService,
            errorNotification,
            showCreateSetDialog);

        CreateSetCommandHandler handler = new CreateSetCommandHandler();

        // Act
        await handler.Execute(ctx).FirstAsync();

        // Assert — dialog was opened with null path (empty FolderPath from null dataStorePath)
        Assert.NotNull(capturedVm);
        // When dataStorePath is null and MRU is empty, FolderPath should be empty
        Assert.Equal("", capturedVm.FolderPath);
    }

    /// <summary>
    /// Requirement 6.2: When the user invokes Create Set with one or more active sessions,
    /// the handler SHALL resolve the DataStore_Folder path from the first session and
    /// open the dialog with that path pre-populated.
    /// </summary>
    [Fact]
    public async Task Execute_ActiveSession_ResolvesPathFromFirstSessionAndPrePopulates()
    {
        // Arrange
        string testDir = Path.Combine(Path.GetTempPath(), "NKitTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            // Create a session with a directory path
            ImageSessionModel session = new ImageSessionModel(
                sessionId: Guid.NewGuid().ToString(),
                path: testDir,
                scopedSetName: null,
                dataStore: null!,
                images: []);

            ReplaySignal<IReadOnlyList<ImageSessionModel>> sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
            sessionsSubject.OnNext(new[] { session });
            FakeDataStoreService fakeDataStoreService = new FakeDataStoreService(sessionsSubject);
            ErrorNotificationService errorNotification = new ErrorNotificationService();
            Interaction<CreateSetDialogViewModel, bool> showCreateSetDialog = new Interaction<CreateSetDialogViewModel, bool>();

            CreateSetDialogViewModel capturedVm = null;
            showCreateSetDialog.RegisterHandler(interaction =>
            {
                capturedVm = interaction.Input;
                interaction.SetOutput(false); // Cancel the dialog
            });

            OperationContext ctx = createTestContext(
                fakeDataStoreService,
                errorNotification,
                showCreateSetDialog);

            CreateSetCommandHandler handler = new CreateSetCommandHandler();

            // Act
            await handler.Execute(ctx).FirstAsync();

            // Assert — dialog was opened with the session's resolved path
            Assert.NotNull(capturedVm);
            Assert.Equal(testDir, capturedVm.FolderPath);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    /// <summary>
    /// Requirement 6.4: If DataStoreService.Sessions emits an error, the handler SHALL
    /// publish an operation error via ErrorNotification and abort the Create Set flow.
    /// </summary>
    [Fact]
    public async Task Execute_SessionsObservableError_PublishesErrorAndAborts()
    {
        // Arrange — Sessions observable throws an exception
        IObservable<IReadOnlyList<ImageSessionModel>> errorObservable = Signal.Create<IReadOnlyList<ImageSessionModel>>(observer =>
        {
            observer.OnError(new InvalidOperationException("Sessions unavailable"));
            return EmptyDisposable.Instance;
        });
        FakeDataStoreService fakeDataStoreService = new FakeDataStoreService(errorObservable);
        ErrorNotificationService errorNotification = new ErrorNotificationService();
        Interaction<CreateSetDialogViewModel, bool> showCreateSetDialog = new Interaction<CreateSetDialogViewModel, bool>();

        List<OperationError> publishedErrors = new List<OperationError>();
        errorNotification.OperationErrors.Subscribe(e => publishedErrors.Add(e));

        bool dialogWasShown = false;
        showCreateSetDialog.RegisterHandler(interaction =>
        {
            dialogWasShown = true;
            interaction.SetOutput(false);
        });

        OperationContext ctx = createTestContext(
            fakeDataStoreService,
            errorNotification,
            showCreateSetDialog);

        CreateSetCommandHandler handler = new CreateSetCommandHandler();

        // Act
        await handler.Execute(ctx).FirstAsync();

        // Assert — error was published and dialog was NOT shown
        Assert.Single(publishedErrors);
        Assert.Contains("Sessions unavailable", publishedErrors[0].Message);
        Assert.False(publishedErrors[0].IsCancellation);
        Assert.False(dialogWasShown);
    }

    /// <summary>
    /// Requirement 6.4: If the dialog interaction fails (throws), the handler SHALL
    /// publish an error via ErrorNotification and abort without showing a partial dialog.
    /// </summary>
    [Fact]
    public async Task Execute_DialogInteractionFailure_PublishesError()
    {
        // Arrange — Sessions works fine, but dialog interaction throws
        ReplaySignal<IReadOnlyList<ImageSessionModel>> sessionsSubject = new ReplaySignal<IReadOnlyList<ImageSessionModel>>(1);
        sessionsSubject.OnNext(Array.Empty<ImageSessionModel>());
        FakeDataStoreService fakeDataStoreService = new FakeDataStoreService(sessionsSubject);
        ErrorNotificationService errorNotification = new ErrorNotificationService();
        Interaction<CreateSetDialogViewModel, bool> showCreateSetDialog = new Interaction<CreateSetDialogViewModel, bool>();

        List<OperationError> publishedErrors = new List<OperationError>();
        errorNotification.OperationErrors.Subscribe(e => publishedErrors.Add(e));

        // Register a handler that throws to simulate dialog interaction failure
        showCreateSetDialog.RegisterHandler(interaction =>
        {
            throw new InvalidOperationException("Dialog rendering failed");
        });

        OperationContext ctx = createTestContext(
            fakeDataStoreService,
            errorNotification,
            showCreateSetDialog);

        CreateSetCommandHandler handler = new CreateSetCommandHandler();

        // Act
        await handler.Execute(ctx).FirstAsync();

        // Assert — error was published
        Assert.Single(publishedErrors);
        Assert.Contains("Dialog rendering failed", publishedErrors[0].Message);
        Assert.False(publishedErrors[0].IsCancellation);
    }

    #region Test Infrastructure

    private static OperationContext createTestContext(
        IDataStoreService dataStoreService,
        ErrorNotificationService errorNotification,
        Interaction<CreateSetDialogViewModel, bool> showCreateSetDialog)
    {
        SessionManager sessionManager = new SessionManager(dataStoreService);
        FileHandleReleaseStrategy releaseStrategy = new FileHandleReleaseStrategy();
        ExclusiveAccessManager exclusiveAccessManager = new ExclusiveAccessManager(sessionManager, releaseStrategy);
        MountOrchestrator mountOrchestrator = new MountOrchestrator(new NullPlatformFsHostFactory());

        return new OperationContext(
            DataStoreService: dataStoreService,
            SessionManager: sessionManager,
            ExclusiveAccessManager: exclusiveAccessManager,
            ConfigService: new NullConfigService(),
            ImageList: null!,
            Toolbar: null!,
            SyncService: new FakeSyncService(),
            ErrorNotification: errorNotification,
            SharedState: new SharedObservableState(),
            MountOrchestrator: mountOrchestrator,
            ActiveOperations: new ObservableCollection<OperationProgressViewModel>(),
            ShowAddImagesDialog: new Interaction<AddImagesDialogViewModel, bool>(),
            ShowExportDialog: new Interaction<ExportDialogViewModel, bool>(),
            ShowCompactDialog: new Interaction<CompactDialogViewModel, bool>(),
            ShowRollbackDialog: new Interaction<RollbackDialogViewModel, bool>(),
            ShowMountDialog: new Interaction<MountDialogViewModel, bool>(),
            ShowAdd1GmrDialog: new Interaction<Add1GmrDialogViewModel, bool>(),
            ShowCreateSetDialog: showCreateSetDialog,
            ShowGroupingWindow: new Interaction<GroupingWindowViewModel, RxVoid>(),
            ShowFolderPicker: new Interaction<RxVoid, string>(),
            ShowFilePicker: new Interaction<RxVoid, string>(),
            ShowSaveFilePicker: new Interaction<RxVoid, string>()
        );
    }

    /// <summary>
    /// Fake IDataStoreService that allows controlling Sessions observable behavior.
    /// </summary>
    private sealed class FakeDataStoreService : IDataStoreService
    {
        private readonly IObservable<IReadOnlyList<ImageSessionModel>> _sessions;

        public FakeDataStoreService(IObservable<IReadOnlyList<ImageSessionModel>> sessions)
        {
            _sessions = sessions;
        }

        public IObservable<IReadOnlyList<ImageSessionModel>> Sessions => _sessions;
        public IObservable<IReadOnlyList<ImageRecord>> AllImages => Signal.Emit<IReadOnlyList<ImageRecord>>([]);
        public IObservable<string> ErrorMessages => Signal.Create<string>(observer => { observer.OnCompleted(); return EmptyDisposable.Instance; });
        public int SessionCount => 0;
        public Task<bool> OpenDirectoryAsync(string directoryPath) => Task.FromResult(false);
        public Task<bool> OpenFileAsync(string filePath) => Task.FromResult(false);
        public void CloseSession(string sessionId) { }
        public bool IsAlreadyOpen(string path) => false;
        public Task<IReadOnlyList<ImageRecord>> RefreshSessionImagesAsync(string sessionId, string setName = null)
            => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
        public NKitDataStore.DataStore GetActiveDataStore() => null;
        public string GetActiveDataStorePath() => null;
    }

    /// <summary>
    /// Minimal IImageListSyncService implementation for testing.
    /// </summary>
    private sealed class FakeSyncService : IImageListSyncService
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

    private sealed class NullPlatformFsHostFactory : IPlatformFsHostFactory
    {
        public IPlatformFsHost Create() => new NullPlatformFsHost();
        public bool IsSupported => false;
        public string UnsupportedReason => "Test environment";
    }

    private sealed class NullPlatformFsHost : IPlatformFsHost
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

    #endregion
}