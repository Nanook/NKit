using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Manages the toolbar button collection, enabled states, Active Set selection,
/// and concurrent operation conflict detection.
/// Commands are placeholder (no-op) — actual execution logic is wired in task 10.2.
/// </summary>
public class ToolbarViewModel : ViewModelBase, IDisposable
{
    private readonly MultipleDisposable _disposables = new();
    private readonly SessionManager _sessionManager;

    private bool _hasDataStoreOpen;
    private bool _hasActiveSet;
    private bool _hasSelectedImages;
    private bool _hasRemovedImages;
    private string? _operationInProgressOnSet;
    private string? _selectedSetName;
    private bool _showFilterBar;
    private bool _isGroupWindowOpen;
    private bool _isDatWindowOpen;
    private int _filteredImageCount;

    /// <summary>
    /// Whether a DataStore is currently open.
    /// </summary>
    public bool HasDataStoreOpen
    {
        get => _hasDataStoreOpen;
        set => this.RaiseAndSetIfChanged(ref _hasDataStoreOpen, value);
    }

    /// <summary>
    /// Whether an Active Set is designated (SelectedSetName is not null).
    /// Derived from SelectedSetName changes.
    /// </summary>
    public bool HasActiveSet
    {
        get => _hasActiveSet;
        private set => this.RaiseAndSetIfChanged(ref _hasActiveSet, value);
    }

    /// <summary>
    /// Whether images are currently selected in the Image_List.
    /// </summary>
    public bool HasSelectedImages
    {
        get => _hasSelectedImages;
        set => this.RaiseAndSetIfChanged(ref _hasSelectedImages, value);
    }

    /// <summary>
    /// Whether the Active Set contains removed images available for restore.
    /// </summary>
    public bool HasRemovedImages
    {
        get => _hasRemovedImages;
        set => this.RaiseAndSetIfChanged(ref _hasRemovedImages, value);
    }

    /// <summary>
    /// The set name that currently has a mutating operation in progress, or null if none.
    /// When set, mutating operations targeting the same set are disabled.
    /// </summary>
    public string? OperationInProgressOnSet
    {
        get => _operationInProgressOnSet;
        set => this.RaiseAndSetIfChanged(ref _operationInProgressOnSet, value);
    }

    /// <summary>
    /// The currently selected (active) set name. Toolbar operations target this set by default.
    /// </summary>
    public string? SelectedSetName
    {
        get => _selectedSetName;
        set
        {
            // Ignore transient null caused by ComboBox clearing during collection rebuild
            if (value == null && _selectedSetName != null)
                return;
            this.RaiseAndSetIfChanged(ref _selectedSetName, value);
        }
    }

    /// <summary>
    /// Whether the filter bar is visible. Toggled by the Filter button on the toolbar.
    /// </summary>
    public bool ShowFilterBar
    {
        get => _showFilterBar;
        set => this.RaiseAndSetIfChanged(ref _showFilterBar, value);
    }

    private bool _isOperationActive;

    /// <summary>
    /// Whether any long-running operation is active. When true, the filter toggle is disabled
    /// and the filter bar is hidden (progress bar takes its place).
    /// Set by MainWindowViewModel when ActiveOperations changes.
    /// </summary>
    public bool IsOperationActive
    {
        get => _isOperationActive;
        set => this.RaiseAndSetIfChanged(ref _isOperationActive, value);
    }

    /// <summary>
    /// Whether the Group window is currently open (drives IsChecked on the Group button).
    /// </summary>
    public bool IsGroupWindowOpen
    {
        get => _isGroupWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isGroupWindowOpen, value);
    }

    /// <summary>
    /// Whether the Dat Verification window is currently open (drives IsChecked on the Dat button).
    /// </summary>
    public bool IsDatWindowOpen
    {
        get => _isDatWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isDatWindowOpen, value);
    }

    /// <summary>
    /// The number of images currently visible after filtering.
    /// Fed from ImageListViewModel.FilteredImages.Count via MainWindowViewModel.
    /// </summary>
    public int FilteredImageCount
    {
        get => _filteredImageCount;
        set => this.RaiseAndSetIfChanged(ref _filteredImageCount, value);
    }

    private bool _isMountActive;

    /// <summary>
    /// Whether the currently selected set has an active mount.
    /// Set by MainWindowViewModel when the selected set's mount state changes.
    /// When true, the Mount button remains enabled to allow unmount toggle.
    /// </summary>
    public bool IsMountActive
    {
        get => _isMountActive;
        set => this.RaiseAndSetIfChanged(ref _isMountActive, value);
    }

    /// <summary>
    /// Command to refresh all open sessions (reload images from disk).
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }

    /// <summary>
    /// Available set names in the current DataStore, sorted case-insensitive alphabetically.
    /// </summary>
    public ObservableCollection<string> AvailableSetNames { get; } = new();

    /// <summary>
    /// Whether there are multiple sets available (controls visibility of the Set filter).
    /// </summary>
    public bool HasMultipleSets => AvailableSetNames.Count > 1;

    /// <summary>
    /// Toolbar items bound to the ListBox. One per operation in display order.
    /// </summary>
    public IReadOnlyList<ToolbarItemViewModel> Items { get; }

    // Commands for each operation
    public ReactiveCommand<RxVoid, RxVoid> OpenDataStoreCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenFileCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseSetCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CreateSetCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddDirCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> Add1GmrCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> VerifyCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ExportCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RestoreCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CompactCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RollbackCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> StatsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> GraphsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> MountCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> GroupCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> YamlCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DatCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SettingsCommand { get; }

    public ToolbarViewModel(SessionManager sessionManager, SharedObservableState sharedState)
    {
        _sessionManager = sessionManager;

        // Initialize local properties synchronously from shared state's current values.
        // This ensures canExecute observables evaluate correctly during construction
        // without waiting for ObserveOn to deliver the initial value.
        HasSelectedImages = sharedState.HasSelectedImages;
        HasRemovedImages = sharedState.HasRemovedImages;
        HasDataStoreOpen = sharedState.HasDataStoreOpen;
        OperationInProgressOnSet = sharedState.OperationInProgressOnSet;
        IsOperationActive = sharedState.IsOperationActive;
        FilteredImageCount = sharedState.FilteredImageCount;
        IsGroupWindowOpen = sharedState.IsGroupWindowOpen;
        IsDatWindowOpen = sharedState.IsDatWindowOpen;
        IsMountActive = sharedState.IsMountActive;
        SelectedSetName = sharedState.SelectedSetName;
        ShowFilterBar = sharedState.ShowFilterBar;
        UpdateAvailableSetNames(sharedState.AvailableSetNames);

        // Subscribe to shared state for subsequent changes (Requirement 4.6).
        // Skip(1) avoids queuing the initial WhenAnyValue emission (already read directly above).
        // ObserveOn ensures updates from background threads marshal to the UI thread.
        sharedState.WhenAnyValue(x => x.HasSelectedImages)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => HasSelectedImages = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.HasRemovedImages)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => HasRemovedImages = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.HasDataStoreOpen)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => HasDataStoreOpen = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.OperationInProgressOnSet)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => OperationInProgressOnSet = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.IsOperationActive)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => IsOperationActive = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.FilteredImageCount)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => FilteredImageCount = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.IsGroupWindowOpen)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => IsGroupWindowOpen = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.IsDatWindowOpen)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => IsDatWindowOpen = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.IsMountActive)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => IsMountActive = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.AvailableSetNames)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(names => UpdateAvailableSetNames(names))
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.SelectedSetName)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => SelectedSetName = v)
            .DisposeWith(_disposables);

        sharedState.WhenAnyValue(x => x.ShowFilterBar)
            .Skip(1).ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(v => ShowFilterBar = v)
            .DisposeWith(_disposables);
        // Create toolbar items from the static operation definitions
        int lastGroup = 0;
        Items = ToolbarOperations.All.Select(op =>
        {
            bool showDivider = lastGroup != 0 && op.Group != lastGroup;
            lastGroup = op.Group;
            return new ToolbarItemViewModel
            {
                Kind = op.Kind,
                Label = op.Label,
                IconName = op.IconName,
                Tooltip = op.Tooltip,
                Group = op.Group,
                ShowDividerBefore = showDivider,
                IsEnabled = op.Kind is ToolbarOperationKind.OpenDataStore or ToolbarOperationKind.OpenFile or ToolbarOperationKind.CreateSet or ToolbarOperationKind.Add1Gmr
            };
        }).ToList();

        // Update HasActiveSet when SelectedSetName changes
        this.WhenAnyValue(x => x.SelectedSetName)
            .Subscribe(name => HasActiveSet = name != null)
            .DisposeWith(_disposables);

        // --- canExecute observables ---
        // 
        // Principle: When ANY mutating operation is active (IsOperationActive == true),
        // ALL mutating commands are disabled globally to prevent file contention and
        // exclusive-access deadlocks. Read-only commands remain enabled.
        //
        // Categories:
        //   ALWAYS enabled: Open, Open Set, Create Set, Add 1GMR (these show pickers/dialogs first)
        //   MUTATING (disabled globally during any operation):
        //     Add, AddDir, Export, Remove, Restore, Compact, Rollback, Mount
        //   READ-ONLY (only need DataStore open, unaffected by operations):
        //     Verify, Stats, Graphs, Dat, Refresh, Group, YAML, Filter

        // --- Mutating commands: disabled when any operation is active ---

        // Add, AddDir: session open + active set + no operation active
        IObservable<bool> canExecuteAdd = _sessionManager.WhenAnyValue(x => x.CurrentState)
            .Select(state => state != OpenState.None)
            .CombineLatest(
                this.WhenAnyValue(x => x.HasActiveSet, x => x.IsOperationActive,
                    (hasSet, opActive) => hasSet && !opActive),
                (hasSession, ready) => hasSession && ready)
            .DistinctUntilChanged();

        // Export, Remove: DataStore open + active set + selection + no operation active
        IObservable<bool> canExecuteSelectionMutating = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.HasActiveSet,
                x => x.HasSelectedImages,
                x => x.IsOperationActive,
                (hasDs, hasSet, hasSel, opActive) => hasDs && hasSet && hasSel && !opActive)
            .DistinctUntilChanged();

        // Restore: DataStore open + active set + has removed images selected + no operation active
        IObservable<bool> canExecuteRestore = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.HasActiveSet,
                x => x.HasSelectedImages,
                x => x.IsOperationActive,
                (hasDs, hasSet, hasSel, opActive) => hasDs && hasSet && hasSel && !opActive)
            .DistinctUntilChanged();

        // Compact, Rollback: DataStore open + active set + no operation active
        IObservable<bool> canExecuteSetMutating = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.HasActiveSet,
                x => x.IsOperationActive,
                (hasDs, hasSet, opActive) => hasDs && hasSet && !opActive)
            .DistinctUntilChanged();

        // Mount: DataStore open + active set + (already mounted for unmount OR no operation active)
        IObservable<bool> canExecuteMount = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.HasActiveSet,
                x => x.IsOperationActive,
                x => x.IsMountActive,
                (hasDs, hasSet, opActive, isMounted) =>
                    hasDs && hasSet && (isMounted || !opActive))
            .DistinctUntilChanged();

        // Open/OpenSet/Close: disabled during operations (prevents session changes mid-operation)
        IObservable<bool> canExecuteSessionChange = this.WhenAnyValue(x => x.IsOperationActive)
            .Select(opActive => !opActive)
            .DistinctUntilChanged();

        // --- Read-only commands: unaffected by operations ---

        // Verify, Stats, Graphs, Dat: DataStore open + active set (read-only, safe during operations)
        IObservable<bool> canExecuteReadOnly = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.HasActiveSet,
                (hasDs, hasSet) => hasDs && hasSet)
            .DistinctUntilChanged();

        // Refresh: DataStore open (read-only reconciliation)
        IObservable<bool> canExecuteRefresh = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.IsOperationActive,
                (hasDs, opActive) => hasDs && !opActive)
            .DistinctUntilChanged();

        // --- Create commands ---

        OpenDataStoreCommand = ReactiveCommand.Create(() => { }, canExecuteSessionChange);
        OpenDataStoreCommand.DisposeWith(_disposables);

        OpenFileCommand = ReactiveCommand.Create(() => { }, canExecuteSessionChange);
        OpenFileCommand.DisposeWith(_disposables);

        CloseSetCommand = ReactiveCommand.Create(() => { },
            _sessionManager.WhenAnyValue(x => x.CurrentState)
                .CombineLatest(this.WhenAnyValue(x => x.IsOperationActive),
                    (state, opActive) => state != OpenState.None && !opActive)
                .DistinctUntilChanged());
        CloseSetCommand.DisposeWith(_disposables);

        CreateSetCommand = ReactiveCommand.Create(() => { }, canExecuteSessionChange);
        CreateSetCommand.DisposeWith(_disposables);

        AddCommand = ReactiveCommand.Create(() => { }, canExecuteAdd);
        AddCommand.DisposeWith(_disposables);

        AddDirCommand = ReactiveCommand.Create(() => { }, canExecuteAdd);
        AddDirCommand.DisposeWith(_disposables);

        Add1GmrCommand = ReactiveCommand.Create(() => { }, canExecuteSessionChange);
        Add1GmrCommand.DisposeWith(_disposables);

        ExportCommand = ReactiveCommand.Create(() => { }, canExecuteSelectionMutating);
        ExportCommand.DisposeWith(_disposables);

        RemoveCommand = ReactiveCommand.Create(() => { }, canExecuteSelectionMutating);
        RemoveCommand.DisposeWith(_disposables);

        RestoreCommand = ReactiveCommand.Create(() => { }, canExecuteRestore);
        RestoreCommand.DisposeWith(_disposables);

        CompactCommand = ReactiveCommand.Create(() => { }, canExecuteSetMutating);
        CompactCommand.DisposeWith(_disposables);

        RollbackCommand = ReactiveCommand.Create(() => { }, canExecuteSetMutating);
        RollbackCommand.DisposeWith(_disposables);

        MountCommand = ReactiveCommand.Create(() => { }, canExecuteMount);
        MountCommand.DisposeWith(_disposables);

        // Read-only commands
        VerifyCommand = ReactiveCommand.Create(() => { }, canExecuteReadOnly);
        VerifyCommand.DisposeWith(_disposables);

        StatsCommand = ReactiveCommand.Create(() => { }, canExecuteReadOnly);
        StatsCommand.DisposeWith(_disposables);

        GraphsCommand = ReactiveCommand.Create(() => { }, canExecuteReadOnly);
        GraphsCommand.DisposeWith(_disposables);

        DatCommand = ReactiveCommand.Create(() => { }, canExecuteReadOnly);
        DatCommand.DisposeWith(_disposables);

        RefreshCommand = ReactiveCommand.Create(() => { }, canExecuteRefresh);
        RefreshCommand.DisposeWith(_disposables);

        // GroupCommand: enabled when DataStore is open AND at least 2 filtered images (read-only)
        IObservable<bool> canExecuteGroup = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.FilteredImageCount,
                (hasDs, count) => hasDs && count >= 2)
            .DistinctUntilChanged();

        GroupCommand = ReactiveCommand.Create(() => { }, canExecuteGroup);
        GroupCommand.DisposeWith(_disposables);

        // YamlCommand: enabled when DataStore is open AND at least 1 filtered image (read-only)
        IObservable<bool> canExecuteYaml = this.WhenAnyValue(
                x => x.HasDataStoreOpen,
                x => x.FilteredImageCount,
                (hasDs, count) => hasDs && count > 0)
            .DistinctUntilChanged();

        YamlCommand = ReactiveCommand.Create(() => { }, canExecuteYaml);
        YamlCommand.DisposeWith(_disposables);

        // SettingsCommand: always enabled, no preconditions (Requirement 1.1)
        SettingsCommand = ReactiveCommand.Create(() => { });
        SettingsCommand.DisposeWith(_disposables);

        // Subscribe to each command's CanExecute to update the corresponding ToolbarItemViewModel.IsEnabled
        SubscribeCommandToItem(OpenDataStoreCommand, ToolbarOperationKind.OpenDataStore);
        SubscribeCommandToItem(OpenFileCommand, ToolbarOperationKind.OpenFile);
        SubscribeCommandToItem(CloseSetCommand, ToolbarOperationKind.CloseSet);
        SubscribeCommandToItem(CreateSetCommand, ToolbarOperationKind.CreateSet);
        SubscribeCommandToItem(AddCommand, ToolbarOperationKind.Add);
        SubscribeCommandToItem(AddDirCommand, ToolbarOperationKind.AddDir);
        SubscribeCommandToItem(Add1GmrCommand, ToolbarOperationKind.Add1Gmr);
        SubscribeCommandToItem(VerifyCommand, ToolbarOperationKind.Verify);
        SubscribeCommandToItem(ExportCommand, ToolbarOperationKind.Export);
        SubscribeCommandToItem(RemoveCommand, ToolbarOperationKind.Remove);
        SubscribeCommandToItem(RestoreCommand, ToolbarOperationKind.Restore);
        SubscribeCommandToItem(CompactCommand, ToolbarOperationKind.Compact);
        SubscribeCommandToItem(RollbackCommand, ToolbarOperationKind.Rollback);
        SubscribeCommandToItem(StatsCommand, ToolbarOperationKind.Stats);
        SubscribeCommandToItem(GraphsCommand, ToolbarOperationKind.Graphs);
        SubscribeCommandToItem(MountCommand, ToolbarOperationKind.Mount);
        SubscribeCommandToItem(GroupCommand, ToolbarOperationKind.Group);
        SubscribeCommandToItem(DatCommand, ToolbarOperationKind.Dat);

        // Bind IsGroupWindowOpen to the Group toolbar item's IsChecked state
        this.WhenAnyValue(x => x.IsGroupWindowOpen)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(isOpen => Items.First(i => i.Kind == ToolbarOperationKind.Group).IsChecked = isOpen)
            .DisposeWith(_disposables);

        // Bind IsDatWindowOpen to the Dat toolbar item's IsChecked state
        this.WhenAnyValue(x => x.IsDatWindowOpen)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(isOpen => Items.First(i => i.Kind == ToolbarOperationKind.Dat).IsChecked = isOpen)
            .DisposeWith(_disposables);
    }

    /// <summary>
    /// Updates the AvailableSetNames collection with a new set of names, sorted case-insensitive.
    /// If the currently selected set is no longer in the list, selects the first available set.
    /// </summary>
    public void UpdateAvailableSetNames(IEnumerable<string> setNames)
    {
        List<string> sorted = setNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        // Determine what SelectedSetName should be AFTER the update
        string? targetSelection = SelectedSetName;
        if (targetSelection == null || (!sorted.Contains(targetSelection, StringComparer.Ordinal) && targetSelection != "All"))
        {
            targetSelection = "All";
        }

        // Rebuild the collection. The ComboBox two-way binding may set SelectedSetName to null
        // during Clear() — we'll restore it after.
        AvailableSetNames.Clear();
        AvailableSetNames.Add("All");
        foreach (string name in sorted)
            AvailableSetNames.Add(name);

        // Restore/set the correct selection (handles the null that ComboBox may have injected)
        SelectedSetName = targetSelection;

        this.RaisePropertyChanged(nameof(HasMultipleSets));
    }

    /// <summary>
    /// Determines whether there is a conflict between the operation in progress and the target set.
    /// <summary>
    /// Subscribes to a command's CanExecute observable and updates the matching ToolbarItemViewModel.IsEnabled.
    /// Also assigns the command to the item so it can be invoked from the view.
    /// </summary>
    private void SubscribeCommandToItem(ReactiveCommand<RxVoid, RxVoid> command, ToolbarOperationKind kind)
    {
        ToolbarItemViewModel item = Items.First(i => i.Kind == kind);
        item.Command = command;
        command.CanExecute
            .DistinctUntilChanged()
            .Subscribe(canExec => item.IsEnabled = canExec)
            .DisposeWith(_disposables);
    }

    public void Dispose() => _disposables.Dispose();
}