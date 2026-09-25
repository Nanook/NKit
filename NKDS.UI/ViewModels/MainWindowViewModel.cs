using NKDS.DatVerification;
using Avalonia.Threading;
using NKDS;
using NKDS.Mount;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels.Commands;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NkdsUi.ViewModels;

/// <summary>
/// Top-level orchestrator ViewModel. Owns child ViewModels and coordinates
/// interactions between panels (image selection → comparison/common files).
/// Delegates all toolbar operations to dedicated Command Handler classes.
/// </summary>
public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDataStoreService _dataStoreService;
    private readonly IConfigService _configService;
    private readonly IHashCacheService _hashCacheService;
    private readonly IImageListSyncService _syncService;
    private readonly MountOrchestrator _mountOrchestrator;
    private readonly ServiceRegistry? _serviceRegistry;
    private readonly SharedObservableState _sharedState;
    private readonly MultipleDisposable _disposables = new();
    private readonly OperationContext _operationContext;
    private bool _suppressSessionUpdates;

    public string NKitVersion { get; } = $"NKit v{Nanook.NKit.AppSettings.GetVersion()}";

    // ─── Command Handlers (one per toolbar operation) ────────────────────────────
    private readonly AddCommandHandler _addHandler = new();
    private readonly ExportCommandHandler _exportHandler = new();
    private readonly CompactCommandHandler _compactHandler = new();
    private readonly RollbackCommandHandler _rollbackHandler = new();
    private readonly RemoveCommandHandler _removeHandler = new();
    private readonly RestoreCommandHandler _restoreHandler = new();
    private readonly MountCommandHandler _mountHandler = new();
    private readonly VerifyCommandHandler _verifyHandler = new();
    private readonly StatsCommandHandler _statsHandler;
    private readonly GraphsCommandHandler _graphsHandler;
    private readonly YamlExportCommandHandler _yamlExportHandler = new();
    private readonly RefreshCommandHandler _refreshHandler = new();
    private readonly CreateSetCommandHandler _createSetHandler = new();
    private readonly Add1GmrCommandHandler _add1GmrHandler = new();
    private readonly AddDirCommandHandler _addDirHandler = new();
    private readonly GroupCommandHandler _groupHandler;
    private readonly DatCommandHandler _datHandler;
    private readonly CloseSetCommandHandler _closeSetHandler = new();
    private readonly ProcessAddCancelledCommandHandler _processAddCancelledHandler = new();
    private readonly ProcessAllAddCancelledCommandHandler _processAllAddCancelledHandler = new();
    private readonly ClearAddCancelledCommandHandler _clearAddCancelledHandler = new();

    // ─── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Orchestrates single-session semantics at the UI level.
    /// Exposes IsLoading, OpenState, and filter bar state for the Title Bar.
    /// </summary>
    public SessionManager SessionManager { get; }

    /// <summary>
    /// Provides access to the configuration service for child view models
    /// that need export format preferences or other persisted settings.
    /// </summary>
    internal IConfigService ConfigService => _configService;

    // Interactions for folder/file picker dialogs (handled by the View layer)
    public Interaction<RxVoid, string?> ShowFolderPicker { get; } = new();
    public Interaction<RxVoid, string?> ShowFilePicker { get; } = new();
    public Interaction<RxVoid, string?> ShowSaveFilePicker { get; } = new();

    // Commands
    public ReactiveCommand<RxVoid, RxVoid> OpenDirectoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenFileCommand { get; }

    /// <summary>
    /// Command to re-process selected AddCancelled rows via the context menu.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ProcessAddCancelledCommand { get; }

    /// <summary>
    /// Command to clear (remove) selected AddCancelled rows via the context menu.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ClearAddCancelledCommand { get; }

    // Child ViewModels
    public ImageListViewModel ImageList { get; }
    public ComparisonPanelViewModel ComparisonPanel { get; }
    public CommonFilesPanelViewModel CommonFilesPanel { get; }
    public ComparisonModeViewModel ComparisonMode { get; }
    public StatsCalculationViewModel StatsCalculation { get; }
    public ToolbarViewModel Toolbar { get; }

    // Dialog interactions
    public Interaction<CreateSetDialogViewModel, bool> ShowCreateSetDialog { get; } = new();
    public Interaction<AddImagesDialogViewModel, bool> ShowAddImagesDialog { get; } = new();
    public Interaction<ExportDialogViewModel, bool> ShowExportDialog { get; } = new();
    public Interaction<CompactDialogViewModel, bool> ShowCompactDialog { get; } = new();
    public Interaction<RollbackDialogViewModel, bool> ShowRollbackDialog { get; } = new();
    public Interaction<MountDialogViewModel, bool> ShowMountDialog { get; } = new();
    public Interaction<Add1GmrDialogViewModel, bool> ShowAdd1GmrDialog { get; } = new();

    /// <summary>
    /// Interaction to show/manage the Grouping Window.
    /// Input: GroupingWindowViewModel, Output: Unit (window closed).
    /// </summary>
    public Interaction<GroupingWindowViewModel, RxVoid> ShowGroupingWindow { get; } = new();

    /// <summary>
    /// Whether the Grouping Window is currently open.
    /// Drives the Group toolbar button's IsChecked state.
    /// </summary>
    public bool IsGroupWindowOpen
    {
        get => _isGroupWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isGroupWindowOpen, value);
    }
    private bool _isGroupWindowOpen;

    /// <summary>
    /// Interaction to show the Settings dialog as a modal window.
    /// Input: Unit, Output: Unit (dialog closed).
    /// </summary>
    public Interaction<RxVoid, RxVoid> ShowSettingsDialog { get; } = new();

    /// <summary>
    /// Interaction to show/manage the Dat Verification Window.
    /// Input: DatVerificationWindowViewModel, Output: Unit (window closed).
    /// </summary>
    public Interaction<DatVerificationWindowViewModel, RxVoid> ShowDatVerificationWindow { get; } = new();

    /// <summary>
    /// Whether the Dat Verification Window is currently open.
    /// Drives the Dat toolbar button's IsChecked state.
    /// </summary>
    public bool IsDatWindowOpen
    {
        get => _isDatWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isDatWindowOpen, value);
    }
    private bool _isDatWindowOpen;

    /// <summary>
    /// Collection of active operation progress panels displayed below the toolbar.
    /// </summary>
    public ObservableCollection<OperationProgressViewModel> ActiveOperations { get; } = new();

    private bool _hasActiveOperations;

    public bool HasActiveOperations
    {
        get => _hasActiveOperations;
        private set => this.RaiseAndSetIfChanged(ref _hasActiveOperations, value);
    }

    private OperationProgressViewModel? _currentOperation;

    /// <summary>
    /// The first active operation (for title bar progress display).
    /// Null when no operations are active.
    /// </summary>
    public OperationProgressViewModel? CurrentOperation
    {
        get => _currentOperation;
        private set => this.RaiseAndSetIfChanged(ref _currentOperation, value);
    }

    // ─── Constructor ────────────────────────────────────────────────────────────

    public MainWindowViewModel(
        IDataStoreService dataStoreService,
        IBlockComparisonService blockComparisonService,
        IHashCacheService hashCacheService,
        ISimilarityGroupService similarityGroupService,
        IThresholdGroupingService thresholdGroupingService,
        IStatsCalculationService statsCalculationService,
        IMountService? mountService = null,
        IPlatformFsHostFactory? platformFsHostFactory = null,
        IConfigService? configService = null,
        ServiceRegistry? serviceRegistry = null)
    {
        _dataStoreService = dataStoreService;
        _configService = configService!;
        _hashCacheService = hashCacheService;
        _serviceRegistry = serviceRegistry;
        _sharedState = serviceRegistry?.SharedState ?? new SharedObservableState();
        IPlatformFsHostFactory factory = platformFsHostFactory ?? PlatformFsHostFactory.CreateForCurrentPlatform();
        _mountOrchestrator = new MountOrchestrator(factory);
        _mountOrchestrator.StateChanged += onMountStateChanged;

        // Create SessionManager for single-session enforcement
        SessionManager = new SessionManager(dataStoreService);

        // Guarantee unmount-before-close ordering for EVERY session-close path (explicit Close,
        // open-replaces-open, and WithExclusiveAccessAsync). Unmount is synchronous here and joins
        // each mount's host thread, so by the time Close() disposes the DataStore no mount is still
        // reading. App enforces single-session semantics, so every active mount belongs to the
        // session being closed.
        SessionManager.BeforeClose = unmountAllActiveMounts;

        // Create child ViewModels
        ImageList = new ImageListViewModel(dataStoreService, serviceRegistry?.ErrorNotificationService);
        ComparisonPanel = new ComparisonPanelViewModel(blockComparisonService, dataStoreService, serviceRegistry?.ErrorNotificationService);
        CommonFilesPanel = new CommonFilesPanelViewModel(blockComparisonService, serviceRegistry?.ErrorNotificationService);
        ComparisonMode = new ComparisonModeViewModel(hashCacheService, similarityGroupService, thresholdGroupingService, ImageList, ComparisonPanel);
        StatsCalculation = new StatsCalculationViewModel(statsCalculationService, dataStoreService, ImageList);
        Toolbar = new ToolbarViewModel(SessionManager, _sharedState);

        // Create the sync service for live incremental image list updates
        _syncService = new ImageListSyncService(ImageList, dataStoreService, _serviceRegistry?.SharedState ?? new SharedObservableState(), serviceRegistry?.ErrorNotificationService ?? new ErrorNotificationService());

        // Instantiate command handlers that require constructor parameters
        _statsHandler = new StatsCommandHandler(StatsCalculation);
        _graphsHandler = new GraphsCommandHandler(StatsCalculation);
        _groupHandler = new GroupCommandHandler(
            _serviceRegistry?.HashCacheService ?? hashCacheService,
            _serviceRegistry?.ThresholdGroupingService ?? thresholdGroupingService);
        _datHandler = new DatCommandHandler(
            _serviceRegistry?.DatVerificationService ?? (IDatVerificationService)new DatVerificationService(),
            ShowDatVerificationWindow,
            this);

        // Build OperationContext — bundles all dependencies for command handlers
        _operationContext = new OperationContext(
            DataStoreService: dataStoreService,
            SessionManager: SessionManager,
            ExclusiveAccessManager: serviceRegistry?.ExclusiveAccessManager ?? new ExclusiveAccessManager(SessionManager, new FileHandleReleaseStrategy()),
            ConfigService: _configService,
            ImageList: ImageList,
            Toolbar: Toolbar,
            SyncService: _syncService,
            ErrorNotification: serviceRegistry?.ErrorNotificationService ?? new ErrorNotificationService(),
            SharedState: _sharedState,
            MountOrchestrator: _mountOrchestrator,
            ActiveOperations: ActiveOperations,
            ShowAddImagesDialog: ShowAddImagesDialog,
            ShowExportDialog: ShowExportDialog,
            ShowCompactDialog: ShowCompactDialog,
            ShowRollbackDialog: ShowRollbackDialog,
            ShowMountDialog: ShowMountDialog,
            ShowAdd1GmrDialog: ShowAdd1GmrDialog,
            ShowCreateSetDialog: ShowCreateSetDialog,
            ShowGroupingWindow: ShowGroupingWindow,
            ShowFolderPicker: ShowFolderPicker,
            ShowFilePicker: ShowFilePicker,
            ShowSaveFilePicker: ShowSaveFilePicker
        );

        // Commands
        OpenDirectoryCommand = ReactiveCommand.CreateFromTask(executeOpenDirectoryAsync);
        OpenDirectoryCommand.DisposeWith(_disposables);
        OpenFileCommand = ReactiveCommand.CreateFromTask(executeOpenFileAsync);
        OpenFileCommand.DisposeWith(_disposables);

        // Context menu commands for AddCancelled rows
        ProcessAddCancelledCommand = ReactiveCommand.Create(() =>
        {
            _processAddCancelledHandler.Execute(_operationContext).Subscribe();
        });
        ProcessAddCancelledCommand.DisposeWith(_disposables);
        ClearAddCancelledCommand = ReactiveCommand.Create(() =>
        {
            _clearAddCancelledHandler.Execute(_operationContext).Subscribe();
        });
        ClearAddCancelledCommand.DisposeWith(_disposables);

        // Wire ImageListViewModel action delegates for context menu commands
        ImageList.ProcessAddCancelledAction = () =>
            _processAddCancelledHandler.Execute(_operationContext).Subscribe();
        ImageList.ProcessAllAddCancelledAction = () =>
            _processAllAddCancelledHandler.Execute(_operationContext).Subscribe();

        wireToolbarCommands();

        // Wire selection changes to shared state HasSelectedImages.
        // Throttle absorbs the transient count=0 that occurs when DataGrid clears selection
        // before selecting a new row (prevents toolbar button blink during row switching).
        ImageList.WhenAnyValue(x => x.SelectedImageCount)
            .Select(count => count > 0)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(15), RxSchedulers.MainThreadScheduler)
            .Subscribe(has => _sharedState.HasSelectedImages = has)
            .DisposeWith(_disposables);

        // Wire SharedState.SelectedSetName to ImageList.SelectedSetFilter for set filtering
        _sharedState.WhenAnyValue(x => x.SelectedSetName)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(setName => ImageList.SelectedSetFilter = setName)
            .DisposeWith(_disposables);

        // Propagate user-initiated SelectedSetName changes from Toolbar (UI dropdown) back to SharedState.
        // Skip(1) avoids propagating the initial value (which came FROM SharedState via ToolbarVM init).
        // Filter out null — transient null occurs when the ComboBox clears during collection rebuild.
        Toolbar.WhenAnyValue(x => x.SelectedSetName)
            .Skip(1)
            .Where(name => name != null)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(setName => _sharedState.SelectedSetName = setName)
            .DisposeWith(_disposables);

        // Propagate user-initiated ShowFilterBar changes from Toolbar (filter toggle) back to SharedState.
        // Skip(1) avoids propagating the initial value (which came FROM SharedState via ToolbarVM init).
        Toolbar.WhenAnyValue(x => x.ShowFilterBar)
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(show => _sharedState.ShowFilterBar = show)
            .DisposeWith(_disposables);

        // Wire StatsCalculation.HasComputedStats to ImageList.ShowStatsColumns
        StatsCalculation.WhenAnyValue(x => x.HasComputedStats)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(has => ImageList.ShowStatsColumns = has)
            .DisposeWith(_disposables);

        // Subscribe to session changes to update toolbar state
        _dataStoreService.Sessions
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(sessions => onSessionsUpdated(sessions))
            .DisposeWith(_disposables);

        // Wire selection changes from ImageList to ComparisonPanel and CommonFilesPanel
        ImageList.SelectedImages.CollectionChanged += onSelectedImagesChanged;

        // Update HasActiveOperations when the collection changes
        ActiveOperations.CollectionChanged += (_, _) =>
        {
            HasActiveOperations = ActiveOperations.Count > 0;
            CurrentOperation = ActiveOperations.Count > 0 ? ActiveOperations[0] : null;
            _sharedState.IsOperationActive = HasActiveOperations;
        };

        // Cancel comparison/common-files when their reference images are removed
        _dataStoreService.AllImages
            .Skip(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(allImages => onAllImagesChanged(allImages))
            .DisposeWith(_disposables);

        // Wire IsGroupWindowOpen to SharedState.IsGroupWindowOpen
        this.WhenAnyValue(x => x.IsGroupWindowOpen)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(isOpen => _sharedState.IsGroupWindowOpen = isOpen)
            .DisposeWith(_disposables);

        // Wire IsDatWindowOpen to SharedState.IsDatWindowOpen
        this.WhenAnyValue(x => x.IsDatWindowOpen)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(isOpen => _sharedState.IsDatWindowOpen = isOpen)
            .DisposeWith(_disposables);

        // Wire SharedState.FilteredImageCount from ImageList.FilteredImages.Count
        // FilteredImages is a stable collection (never reassigned), so subscribe to its CollectionChanged directly
        _sharedState.FilteredImageCount = ImageList.FilteredImages.Count;
        ImageList.FilteredImages.CollectionChanged += (_, _) => _sharedState.FilteredImageCount = ImageList.FilteredImages.Count;

        // Wire per-image errors from ErrorNotificationService to ImageRowViewModels
        _operationContext.ErrorNotification.ImageErrors
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(error =>
            {
                ImageRowViewModel? row = ImageList.FindRow(error.SetName, error.ImageId);
                if (row == null) return;

                if (row.ProcessingStatus == ImageProcessingStatus.Failed)
                {
                    // Already failed — append the new error to OutputText
                    row.AppendOutput(error.Message);
                }
                else
                {
                    // First failure — set status and reason
                    row.ProcessingStatus = ImageProcessingStatus.Failed;
                    row.StatusReason = error.Message;
                }
            })
            .DisposeWith(_disposables);

        // Detect session closures during Comparison Mode
        _dataStoreService.Sessions
            .Buffer(2, 1)
            .Where(buffer => buffer.Count == 2)
            .Select(buffer => new { Previous = buffer[0], Current = buffer[1] })
            .Where(change => change.Previous.Count > change.Current.Count)
            .Select(change =>
            {
                HashSet<string> currentIds = change.Current.Select(s => s.SessionId).ToHashSet();
                return change.Previous
                    .Where(s => !currentIds.Contains(s.SessionId))
                    .Select(s => s.SessionId)
                    .ToList();
            })
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(removedSessionIds => onSessionsClosed(removedSessionIds))
            .DisposeWith(_disposables);

        // Wire ErrorNotificationService operation-level errors to SessionManager.ShowError
        _operationContext.ErrorNotification.OperationErrors
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(error => SessionManager.ShowError(error.Message))
            .DisposeWith(_disposables);
    }

    // ─── Toolbar Command Wiring ─────────────────────────────────────────────────

    private void wireToolbarCommands()
    {
        Toolbar.OpenDataStoreCommand
            .Subscribe(_ => OpenDirectoryCommand.Execute().Subscribe())
            .DisposeWith(_disposables);

        Toolbar.OpenFileCommand
            .Subscribe(_ => OpenFileCommand.Execute().Subscribe())
            .DisposeWith(_disposables);

        Toolbar.CloseSetCommand
            .Subscribe(_ => subscribeHandler(_closeSetHandler))
            .DisposeWith(_disposables);

        Toolbar.CreateSetCommand
            .Subscribe(_ => subscribeHandler(_createSetHandler))
            .DisposeWith(_disposables);

        Toolbar.AddCommand
            .Subscribe(_ => subscribeHandler(_addHandler))
            .DisposeWith(_disposables);

        Toolbar.AddDirCommand
            .Subscribe(_ => subscribeHandler(_addDirHandler))
            .DisposeWith(_disposables);

        Toolbar.Add1GmrCommand
            .Subscribe(_ => subscribeHandler(_add1GmrHandler))
            .DisposeWith(_disposables);

        Toolbar.ExportCommand
            .Subscribe(_ => subscribeHandler(_exportHandler))
            .DisposeWith(_disposables);

        Toolbar.RemoveCommand
            .Subscribe(_ => subscribeHandler(_removeHandler))
            .DisposeWith(_disposables);

        Toolbar.RestoreCommand
            .Subscribe(_ => subscribeHandler(_restoreHandler))
            .DisposeWith(_disposables);

        Toolbar.CompactCommand
            .Subscribe(_ => subscribeHandler(_compactHandler))
            .DisposeWith(_disposables);

        Toolbar.RollbackCommand
            .Subscribe(_ => subscribeHandler(_rollbackHandler))
            .DisposeWith(_disposables);

        Toolbar.StatsCommand
            .Subscribe(_ => subscribeHandler(_statsHandler))
            .DisposeWith(_disposables);

        Toolbar.MountCommand
            .Subscribe(_ => subscribeHandler(_mountHandler))
            .DisposeWith(_disposables);

        Toolbar.GraphsCommand
            .Subscribe(_ => subscribeHandler(_graphsHandler))
            .DisposeWith(_disposables);

        Toolbar.VerifyCommand
            .Subscribe(_ => subscribeHandler(_verifyHandler))
            .DisposeWith(_disposables);

        Toolbar.RefreshCommand
            .Subscribe(_ => subscribeHandler(_refreshHandler))
            .DisposeWith(_disposables);

        Toolbar.GroupCommand
            .Subscribe(_ => subscribeHandler(_groupHandler))
            .DisposeWith(_disposables);

        Toolbar.YamlCommand
            .Subscribe(_ => subscribeHandler(_yamlExportHandler))
            .DisposeWith(_disposables);

        Toolbar.DatCommand
            .Subscribe(_ => subscribeHandler(_datHandler))
            .DisposeWith(_disposables);

        Toolbar.SettingsCommand
            .Subscribe(_ => ShowSettingsDialog.Handle(RxVoid.Default).Subscribe())
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Subscribes to a command handler's observable with error routing to the ErrorNotificationService.
    /// Prevents unobserved exceptions from being silently swallowed.
    /// </summary>
    private void subscribeHandler(ICommandHandler handler)
    {
        handler.Execute(_operationContext).Subscribe(
            onNext: _ => { },
            onError: ex => _operationContext.ErrorNotification.PublishOperationError(
                $"Unhandled error: {ex.Message}"));
    }

    // ─── Open Commands ──────────────────────────────────────────────────────────

    private async Task executeOpenDirectoryAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (string.IsNullOrEmpty(path))
            return;

        // Reset UI state before opening new session
        resetUiStateForSessionChange();

        // Suppress intermediate reactive updates during the close→open transition
        // to prevent the grid and toolbar from flashing to an empty/disabled state.
        _suppressSessionUpdates = true;
        ImageList.SuppressFilters();
        try
        {
            await SessionManager.OpenDirectoryAsync(path);

            // Allow any queued reactive emissions to drain (they'll be skipped by the flag)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            _suppressSessionUpdates = false;
            ImageList.ResumeFilters();
            // Manually process the final session state for the toolbar
            refreshToolbarFromCurrentSessions();
        }

        _configService.AddDataStorePath(path);
    }

    private async Task executeOpenFileAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (string.IsNullOrEmpty(path))
            return;

        // Reset UI state before opening new session
        resetUiStateForSessionChange();

        // Suppress intermediate reactive updates during the close→open transition
        _suppressSessionUpdates = true;
        ImageList.SuppressFilters();
        try
        {
            await SessionManager.OpenFileAsync(path);

            // Allow any queued reactive emissions to drain (they'll be skipped by the flag)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        finally
        {
            _suppressSessionUpdates = false;
            ImageList.ResumeFilters();
            refreshToolbarFromCurrentSessions();
        }

        _configService.AddDataStorePath(path);
    }

    /// <summary>
    /// Resets all transient UI state in preparation for a session change (open/close).
    /// Centralizes the cleanup to avoid inconsistencies between different code paths.
    /// </summary>
    private void resetUiStateForSessionChange()
    {
        _sharedState.ShowFilterBar = false;
        _sharedState.SelectedSetName = null;
        ImageList.SelectedImages.Clear();
        ImageList.NameFilter = "";
        ImageList.SelectedSystemFilter = null;
    }

    /// <summary>
    /// Manually triggers the toolbar/session state update from the current sessions.
    /// Called after suppressed transitions complete to apply the final state in one pass.
    /// </summary>
    private void refreshToolbarFromCurrentSessions()
    {
        IReadOnlyList<ImageSessionModel> sessions = _dataStoreService.Sessions.FirstAsync().GetAwaiter().GetResult();
        onSessionsUpdated(sessions);
    }

    // ─── Grouping / Dat Window Callbacks ────────────────────────────────────────

    /// <summary>
    /// Called when the Grouping Window is closed. Resets state so the window can be reopened fresh.
    /// </summary>
    public void OnGroupWindowClosed()
    {
        IsGroupWindowOpen = false;
        _groupHandler.OnGroupWindowClosed(_sharedState);
    }

    /// <summary>
    /// Called when the Dat Verification Window is closed. Resets state so the window can be reopened fresh.
    /// The ViewModel is disposed by the window's own Closed handler.
    /// </summary>
    public void OnDatWindowClosed()
    {
        IsDatWindowOpen = false;
        _datHandler.OnDatWindowClosed(_sharedState);
    }

    // ─── Session Event Handlers ─────────────────────────────────────────────────

    private void onSelectedImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ComparisonMode.IsActive)
        {
            CommonFilesPanel.CancelAndClear();
            return;
        }

        List<ImageRecord> selectedList = ImageList.SelectedImages.Select(row => row.Image).ToList();
        CommonFilesPanel.UpdateSelection(selectedList);
    }

    private void onAllImagesChanged(IReadOnlyList<ImageRecord> allImages)
    {
        // Skip during exclusive access operations — images will be restored after reopen.
        if (_suppressSessionUpdates || SessionManager.IsExclusiveAccessActive)
            return;

        ImageRecord? referenceImage = ComparisonPanel.ReferenceImage;
        if (referenceImage != null && !allImages.Contains(referenceImage))
            ComparisonPanel.CancelAndClear();

        if (CommonFilesPanel.IsVisible)
        {
            List<ImageRecord> selectedImages = ImageList.SelectedImages.Select(row => row.Image).ToList();
            if (selectedImages.Any(img => !allImages.Contains(img)))
                CommonFilesPanel.CancelAndClear();
        }
    }

    private void onSessionsClosed(List<string> removedSessionIds)
    {
        // Skip during managed transitions — the full state will be rebuilt after.
        if (_suppressSessionUpdates || SessionManager.IsExclusiveAccessActive)
            return;

        foreach (string sessionId in removedSessionIds)
            ImageList.ClearStatsForSession(sessionId);

        // If no images have stats remaining, hide the stats columns
        if (!ImageList.GetAllImages().Any(img => img.HasStats))
            StatsCalculation.HasComputedStats = false;

        if (!ComparisonMode.IsActive)
            return;

        foreach (string sessionId in removedSessionIds)
            _hashCacheService.RemoveSession(sessionId);

        if (_hashCacheService.Cache.Count == 0)
        {
            ComparisonMode.ExitComparisonModeCommand.Execute().Subscribe();
        }
        else
        {
            ImageList.SetWorkingSet(_hashCacheService.Cache.Keys.ToList());
            ComparisonMode.StatusText =
                $"Comparison Mode: {ImageList.TotalImageCount} images loaded ({ImageList.SelectedImageCount} selected)";
        }
    }

    private void onSessionsUpdated(IReadOnlyList<ImageSessionModel> sessions)
    {
        // Skip intermediate emissions during managed transitions (open/close/create)
        // or exclusive access operations (Add/Export/Remove etc.).
        // The final state will be processed when the suppress flag is cleared.
        if (_suppressSessionUpdates || SessionManager.IsExclusiveAccessActive)
            return;

        bool hasDataStore = sessions.Count > 0;
        _sharedState.HasDataStoreOpen = hasDataStore;

        if (!hasDataStore)
        {
            Toolbar.UpdateAvailableSetNames(Array.Empty<string>());
            _sharedState.HasRemovedImages = false;
            return;
        }

        // Collect all set names across all sessions
        List<string> setNames = sessions
            .SelectMany(s =>
            {
                IEnumerable<string> imageSetNames = s.Images.Select(img => img.SetName);
                IEnumerable<string> dsSetNames = Enumerable.Empty<string>();
                try { dsSetNames = s.DataStore.ListSetNames(); }
                catch { /* ignore */ }
                return imageSetNames.Concat(dsSetNames);
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Toolbar.UpdateAvailableSetNames(setNames);

        // Only reset to "All" if the current selection is null (not yet set).
        if (_sharedState.SelectedSetName == null)
            _sharedState.SelectedSetName = "All";

        updateHasRemovedImages(sessions);
    }

    private void updateHasRemovedImages(IReadOnlyList<ImageSessionModel> sessions)
    {
        if (_sharedState.SelectedSetName == null)
        {
            _sharedState.HasRemovedImages = false;
            return;
        }

        foreach (ImageSessionModel session in sessions)
        {
            if (_sharedState.SelectedSetName == "All")
            {
                if (session.Images.Any(img => img.Removed))
                {
                    _sharedState.HasRemovedImages = true;
                    return;
                }
            }
            else if (session.Images.Any(img => img.SetName == _sharedState.SelectedSetName && img.Removed))
            {
                _sharedState.HasRemovedImages = true;
                return;
            }
        }
        _sharedState.HasRemovedImages = false;
    }

    // ─── Mount State Event Handler ──────────────────────────────────────────────

    /// <summary>
    /// Handles <see cref="MountOrchestrator.StateChanged"/> events. Detects errors and
    /// unexpected terminations, then marshals recovery to the UI thread.
    /// </summary>
    private void onMountStateChanged(object? sender, MountStateChangedEventArgs e)
    {
        if (e.State == MountState.Error || e.State == MountState.Unmounted)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Clear operation state — the MountCommandHandler manages its own _activeMounts
                _sharedState.OperationInProgressOnSet = null;

                // Update toolbar mount state
                ToolbarItemViewModel? mountItem = Toolbar.Items.FirstOrDefault(i => i.Kind == ToolbarOperationKind.Mount);
                if (mountItem != null)
                {
                    _sharedState.IsMountActive = false;
                    mountItem.IsChecked = false;
                    mountItem.Label = "Mount";
                    mountItem.Tooltip = "Mount - Mount the listed images as a virtual filesystem";
                }
            });
        }
    }

    /// <summary>
    /// Unmounts every active mount and waits for each host thread to exit. Wired to
    /// <see cref="SessionManager.BeforeClose"/> so it runs before the session's DataStore is disposed.
    /// <see cref="MountOrchestrator.Unmount"/> is synchronous and joins the host thread, so this
    /// returns only once the mounts have fully released their DataStores.
    /// </summary>
    private void unmountAllActiveMounts()
    {
        foreach (NKDS.Models.ActiveMount mount in _mountOrchestrator.GetActiveMounts())
            _mountOrchestrator.Unmount(mount.MountPoint);
    }

    // ─── Application Exit Cleanup ─────────────────────────────────────────────

    /// <summary>
    /// Disposes all active mounts during application exit via <see cref="MountOrchestrator"/>.
    /// Called from the window closing handler.
    /// </summary>
    public Task CleanupMountsAsync()
    {
        // Dispose the orchestrator — it unmounts all active mounts internally
        _mountOrchestrator.Dispose();

        return Task.CompletedTask;
    }

    // ─── Command-Line Add Operations ────────────────────────────────────────────

    /// <summary>
    /// Performs an Add Directory operation with the specified input path.
    /// Uses the full infrastructure (progress, exclusive access, sync).
    /// Called from App.axaml.cs for command-line --action adddir.
    /// </summary>
    internal async Task ExecuteAddDirWithInputAsync(IReadOnlyList<string> inputPaths)
    {
        IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();
        if (sessions.Count == 0) return;

        string? targetSet = Toolbar.SelectedSetName;
        if (targetSet == null || string.Equals(targetSet, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
            targetSet = Toolbar.AvailableSetNames.FirstOrDefault(n => !string.Equals(n, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase));
        if (targetSet == null) return;

        ImageSessionModel? session = SessionResolver.FindSessionForSet(sessions, targetSet);
        if (session == null) return;
        string dataStorePath = SessionResolver.ResolveDataStorePath(session).dataStorePath;

        OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Add Directory" };
        progressVm.TotalItems = inputPaths.Count;
        _operationContext.ActiveOperations.Add(progressVm);
        _sharedState.OperationInProgressOnSet = targetSet;

        try
        {
            ImageList.SuppressFilters();
            await SessionManager.WithExclusiveAccessAsync(async () =>
            {
                using NkdsOperations ops = new NKDS.NkdsOperations();

                for (int i = 0; i < inputPaths.Count; i++)
                {
                    progressVm.ItemsProcessed = i;
                    progressVm.CurrentItem = Path.GetFileName(inputPaths[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                    await ops.AddDirAsync(dataStorePath, targetSet, new[] { inputPaths[i] },
                        progressVm.CreateProgressReporter(), progressVm.CancellationToken);
                }
                progressVm.ItemsProcessed = inputPaths.Count;
            });
            ImageList.ResumeFilters();

            await _syncService.ReconcileAsync(targetSet);
            await _syncService.RefreshAvailableSetNamesAsync();
        }
        catch (OperationCanceledException)
        {
            ImageList.ResumeFilters(applyNow: false);
            _operationContext.ErrorNotification.PublishCancellation("Add Directory");
        }
        catch (Exception ex)
        {
            ImageList.ResumeFilters(applyNow: false);
            _operationContext.ErrorNotification.PublishOperationError($"Add Directory failed: {ex.Message}");
        }
        finally
        {
            await progressVm.WaitForMinimumDisplayTimeAsync();
            _operationContext.ActiveOperations.Remove(progressVm);
            _sharedState.OperationInProgressOnSet = null;
            progressVm.Dispose();
        }
    }

    /// <summary>
    /// Opens the Add Images dialog pre-populated with the specified file paths.
    /// Called from the MainWindow code-behind when files are dragged onto the window.
    /// Requires a DataStore to be open — the caller should gate on HasDataStoreOpen.
    /// </summary>
    internal void ExecuteAddWithDroppedFiles(IReadOnlyList<string> droppedFiles) =>
        // Pre-populate the dialog VM with the dropped files, then execute the normal Add flow
        _addHandler.ExecuteWithFiles(_operationContext, droppedFiles).Subscribe();

    /// <summary>
    /// Performs an Add Images operation with the specified input file path.
    /// Uses the full infrastructure (progress, exclusive access, sync).
    /// Called from App.axaml.cs for command-line --action add.
    /// </summary>
    internal async Task ExecuteAddWithInputAsync(IReadOnlyList<string> inputPaths)
    {
        IReadOnlyList<ImageSessionModel> sessions = await _dataStoreService.Sessions.FirstAsync();
        if (sessions.Count == 0) return;

        string? targetSet = Toolbar.SelectedSetName;
        if (targetSet == null || string.Equals(targetSet, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase))
            targetSet = Toolbar.AvailableSetNames.FirstOrDefault(n => !string.Equals(n, SessionResolver.AllSetsName, StringComparison.OrdinalIgnoreCase));
        if (targetSet == null) return;

        string? dataStorePath = SessionResolver.ResolveDataStorePathForSet(sessions, targetSet);
        if (dataStorePath == null) return;

        OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Add Images" };
        progressVm.TotalItems = inputPaths.Count;
        _operationContext.ActiveOperations.Add(progressVm);
        _sharedState.OperationInProgressOnSet = targetSet;

        try
        {
            ImageList.SuppressFilters();
            await SessionManager.WithExclusiveAccessAsync(async () =>
            {
                using NkdsOperations ops = new NKDS.NkdsOperations();

                for (int i = 0; i < inputPaths.Count; i++)
                {
                    progressVm.ItemsProcessed = i;

                    await ops.AddAsync(dataStorePath, targetSet, new[] { inputPaths[i] }, null,
                        onImageCommitted: evt => { _ = _syncService.InsertImageAsync(evt); },
                        onImageProgress: evt =>
                        {
                            _ = _syncService.UpdateImageProgressAsync(evt);
                            string imageName = ImageList.FindRow(evt.SetName, evt.ImageId)?.Name ?? $"Image {evt.ImageId}";
                            progressVm.CurrentItem = $"{imageName} \u2014 {evt.StepName}";
                            progressVm.Percentage = (int)(evt.OverallProgress * 100);
                        },
                        onImageOutput: (setName, imageId, text) => { _ = _syncService.AppendImageOutputAsync(setName, imageId, text); },
                        cancellationToken: progressVm.CancellationToken);
                }
                progressVm.ItemsProcessed = inputPaths.Count;
            });
            ImageList.ResumeFilters();

            await _syncService.ReconcileAsync(targetSet);
            await _syncService.RefreshAvailableSetNamesAsync();
        }
        catch (OperationCanceledException)
        {
            ImageList.ResumeFilters(applyNow: false);
            _operationContext.ErrorNotification.PublishCancellation("Add Images");
        }
        catch (Exception ex)
        {
            ImageList.ResumeFilters(applyNow: false);
            _operationContext.ErrorNotification.PublishOperationError($"Add failed: {ex.Message}");
        }
        finally
        {
            await progressVm.WaitForMinimumDisplayTimeAsync();
            _operationContext.ActiveOperations.Remove(progressVm);
            _sharedState.OperationInProgressOnSet = null;
            progressVm.Dispose();
        }
    }

    // ─── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        ImageList.SelectedImages.CollectionChanged -= onSelectedImagesChanged;
        _mountOrchestrator.StateChanged -= onMountStateChanged;
        _mountOrchestrator.Dispose();
        _disposables.Dispose();
        Toolbar.Dispose();
        StatsCalculation.Dispose();
        ComparisonMode.Dispose();
        ImageList.Dispose();
        ComparisonPanel.Dispose();
        CommonFilesPanel.Dispose();
    }
}