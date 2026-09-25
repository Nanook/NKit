using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for SettingsDialogViewModel.
/// Validates: Requirements 2.5, 7.3, 7.4, 9.1, 9.4, 10.1
/// </summary>
public class SettingsDialogViewModelTests : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SettingsDialogViewModelTests()
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

    // --- Entry population tests ---

    [Fact]
    public void DirectoryEntries_ArePopulated_WhenServiceIsSupported()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        Assert.Equal(4, vm.DirectoryEntries.Count);
        Assert.Contains(vm.DirectoryEntries, e => e.Label == "NKDS Open");
        Assert.Contains(vm.DirectoryEntries, e => e.Label == "NKDS Mount");
    }

    [Fact]
    public void FileEntries_ArePopulated_WhenServiceIsSupported()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        Assert.Equal(4, vm.FileEntries.Count);
        Assert.Contains(vm.FileEntries, e => e.Label == "NKDS Open Set");
        Assert.Contains(vm.FileEntries, e => e.Label == "NKDS Mount Set");
    }

    [Fact]
    public void DoubleClickEntries_ArePopulated_WhenServiceIsSupported()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        Assert.Equal(1, vm.DoubleClickEntries.Count);
        Assert.Equal("Open as Set", vm.DoubleClickEntries[0].Label);
    }

    [Fact]
    public void Entries_ArePopulated_WhenServiceIsNotSupported()
    {
        // Entries are still populated even when unsupported (UI hides the section via IsFileAssociationsSupported)
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: false);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        Assert.Equal(4, vm.DirectoryEntries.Count);
        Assert.Equal(4, vm.FileEntries.Count);
        Assert.Equal(1, vm.DoubleClickEntries.Count);
        Assert.False(vm.IsFileAssociationsSupported);
    }

    [Fact]
    public void IsFileAssociationsSupported_ReflectsServiceIsSupported()
    {
        FakeFileAssociationService supported = new FakeFileAssociationService(isSupported: true);
        FakeFileAssociationService unsupported = new FakeFileAssociationService(isSupported: false);

        using SettingsDialogViewModel vmSupported = new SettingsDialogViewModel(supported, new NullConfigService());
        using SettingsDialogViewModel vmUnsupported = new SettingsDialogViewModel(unsupported, new NullConfigService());

        Assert.True(vmSupported.IsFileAssociationsSupported);
        Assert.False(vmUnsupported.IsFileAssociationsSupported);
    }

    // --- Toggle command tests ---

    [Fact]
    public async Task ToggleCommand_CallsRegister_WhenEntryIsNotRegistered()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.NotRegistered;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Contains(entry.Entry, service.RegisteredEntries);
    }

    [Fact]
    public async Task ToggleCommand_CallsUnregister_WhenEntryIsRegistered()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.Registered;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Contains(entry.Entry, service.UnregisteredEntries);
    }

    [Fact]
    public async Task ToggleCommand_CallsUnregister_WhenEntryIsStale()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.Stale;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Contains(entry.Entry, service.UnregisteredEntries);
    }

    [Fact]
    public async Task ToggleCommand_UpdatesState_AfterSuccessfulRegister()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        // After register, QueryStateAsync returns Registered
        service.QueryResultOverride = new AssociationQueryResult { State = AssociationState.Registered };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.NotRegistered;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Equal(AssociationState.Registered, entry.State);
    }

    [Fact]
    public async Task ToggleCommand_SetsErrorMessage_WhenOperationFails()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.OperationResultOverride = new AssociationOperationResult
        {
            Success = false,
            ErrorMessage = "Access denied"
        };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.NotRegistered;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Equal("Access denied", entry.ErrorMessage);
    }

    [Fact]
    public async Task ToggleCommand_SetsElevationMessage_WhenRequiresElevation()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.OperationResultOverride = new AssociationOperationResult
        {
            Success = false,
            RequiresElevation = true,
            ErrorMessage = "Registry access denied"
        };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.State = AssociationState.NotRegistered;

        await vm.ToggleAssociationCommand.Execute(entry);

        Assert.Contains("administrator", entry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // --- InitializeAsync loading state tests ---

    [Fact]
    public async Task InitializeAsync_SetsIsLoadingTrue_ThenFalse()
    {
        List<bool> loadingStates = new List<bool>();
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.QueryResultOverride = new AssociationQueryResult { State = AssociationState.NotRegistered };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        AssociationEntryViewModel entry = vm.DirectoryEntries[0];
        entry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AssociationEntryViewModel.IsLoading))
                loadingStates.Add(entry.IsLoading);
        };

        await vm.InitializeAsync();

        // Should have transitioned to true then back to false
        Assert.Contains(true, loadingStates);
        Assert.False(entry.IsLoading);
    }

    [Fact]
    public async Task InitializeAsync_SetsState_FromQueryResult()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.QueryResultOverride = new AssociationQueryResult { State = AssociationState.Registered };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        await vm.InitializeAsync();

        foreach (AssociationEntryViewModel entry in vm.DirectoryEntries.Concat(vm.FileEntries).Concat(vm.DoubleClickEntries))
        {
            Assert.Equal(AssociationState.Registered, entry.State);
        }
    }

    [Fact]
    public async Task InitializeAsync_PropagatesErrorMessage_FromQueryResult()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.QueryResultOverride = new AssociationQueryResult
        {
            State = AssociationState.Unknown,
            ErrorMessage = "Permission denied"
        };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        await vm.InitializeAsync();

        foreach (AssociationEntryViewModel entry in vm.DirectoryEntries.Concat(vm.FileEntries).Concat(vm.DoubleClickEntries))
        {
            Assert.Equal("Permission denied", entry.ErrorMessage);
        }
    }

    [Fact]
    public async Task InitializeAsync_SetsTimeoutError_WhenQueryTimesOut()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        service.QueryDelay = TimeSpan.FromSeconds(10); // Exceeds the 5-second timeout
        service.QueryResultOverride = new AssociationQueryResult { State = AssociationState.NotRegistered };
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        await vm.InitializeAsync();

        foreach (AssociationEntryViewModel entry in vm.DirectoryEntries.Concat(vm.FileEntries).Concat(vm.DoubleClickEntries))
        {
            Assert.Contains("timed out", entry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(entry.IsLoading);
        }
    }

    // --- Window decoration mode tests ---

    [Fact]
    public void SelectedDecorationMode_PersistsViaConfigService()
    {
        TrackingConfigService configService = new TrackingConfigService();
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, configService);

        vm.SelectedDecorationMode = WindowDecorationMode.NativeTitleBar;

        Assert.Equal(WindowDecorationMode.NativeTitleBar, configService.LastSavedMode);
    }

    [Fact]
    public void SelectedDecorationMode_SetsShowRestartNotice_WhenChanged()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        Assert.False(vm.ShowRestartNotice);

        vm.SelectedDecorationMode = WindowDecorationMode.NativeTitleBar;

        Assert.True(vm.ShowRestartNotice);
    }

    [Fact]
    public void SelectedDecorationMode_DoesNotShowRestartNotice_WhenRevertedToOriginal()
    {
        FakeFileAssociationService service = new FakeFileAssociationService(isSupported: true);
        using SettingsDialogViewModel vm = new SettingsDialogViewModel(service, new NullConfigService());

        vm.SelectedDecorationMode = WindowDecorationMode.NativeTitleBar;
        Assert.True(vm.ShowRestartNotice);

        // Revert back to original
        vm.SelectedDecorationMode = WindowDecorationMode.ClientSideDecorations;
        Assert.False(vm.ShowRestartNotice);
    }

    // --- Fake implementations ---

    private sealed class FakeFileAssociationService : IFileAssociationService
    {
        public FakeFileAssociationService(bool isSupported)
        {
            IsSupported = isSupported;
        }

        public bool IsSupported { get; }
        public string DirectoryContextMenuLabel => IsSupported ? "Explorer Context Menu" : "";

        public List<AssociationEntry> RegisteredEntries { get; } = new();
        public List<AssociationEntry> UnregisteredEntries { get; } = new();

        public AssociationQueryResult QueryResultOverride { get; set; }
        public AssociationOperationResult OperationResultOverride { get; set; }
        public TimeSpan QueryDelay { get; set; } = TimeSpan.Zero;

        public async Task<AssociationQueryResult> QueryStateAsync(AssociationEntry entry, CancellationToken ct = default)
        {
            if (QueryDelay > TimeSpan.Zero)
            {
                await Task.Delay(QueryDelay, ct);
            }

            return QueryResultOverride ?? new AssociationQueryResult { State = AssociationState.NotRegistered };
        }

        public Task<AssociationOperationResult> RegisterAsync(AssociationEntry entry, CancellationToken ct = default)
        {
            RegisteredEntries.Add(entry);
            AssociationOperationResult result = OperationResultOverride ?? new AssociationOperationResult { Success = true };
            return Task.FromResult(result);
        }

        public Task<AssociationOperationResult> UnregisterAsync(AssociationEntry entry, CancellationToken ct = default)
        {
            UnregisteredEntries.Add(entry);
            AssociationOperationResult result = OperationResultOverride ?? new AssociationOperationResult { Success = true };
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Config service that tracks SetWindowDecorationMode calls.
    /// </summary>
    private sealed class TrackingConfigService : IConfigService
    {
        private bool _shouldThrowOnce;

        public WindowDecorationMode? LastSavedMode { get; private set; }
        public bool ShouldThrowOnce
        {
            get => _shouldThrowOnce;
            set => _shouldThrowOnce = value;
        }

        public void SetWindowDecorationMode(WindowDecorationMode mode)
        {
            if (_shouldThrowOnce)
            {
                _shouldThrowOnce = false;
                throw new IOException("Disk full");
            }
            LastSavedMode = mode;
        }

        // --- Null implementations for other IConfigService members ---
        public IReadOnlyList<string> MountPathHistory => [];
        public IReadOnlyList<string> DataStoreHistory => [];
        public IReadOnlyList<string> CreateSetHistory => [];
        public IReadOnlyList<string> ExportPathHistory => [];
        public IReadOnlyList<string> OgmrYamlHistory => [];
        public IReadOnlyList<string> OgmrOutputHistory => [];
        public IReadOnlyList<string> DatPathHistory => [];
        public IReadOnlyList<string> ImageBrowseHistory => [];
        public WindowStateConfig WindowState => null;
        public IReadOnlyDictionary<string, double> ColumnWidths => null;
        public string MountUid => "";
        public string MountGid => "";
        public bool MountAllowOther => false;
        public void AddMountPath(string path) { }
        public void AddDataStorePath(string path) { }
        public void AddCreateSetPath(string path) { }
        public void AddExportPath(string path) { }
        public void AddOgmrYamlPath(string path) { }
        public void AddOgmrOutputPath(string path) { }
        public void AddDatPath(string path) { }
        public void AddImageBrowsePath(string path) { }
        public string GetExportFormat(string system, string sourceFormat) => null;
        public void SetExportFormat(string system, string sourceFormat, string targetFormat) { }
        public void SetWindowState(int width, int height, int x, int y) { }
        public FormatOptionsEntry GetFormatOptions(string system, string sourceFormat, string targetFormat) => null;
        public void SetFormatOptions(string system, string sourceFormat, string targetFormat, FormatOptionsEntry options) { }
        public void SetColumnWidths(Dictionary<string, double> widths) { }
        public void SetMountLinuxOptions(string uid, string gid, bool allowOther) { }
        public WindowDecorationMode GetWindowDecorationMode() => WindowDecorationMode.ClientSideDecorations;
        public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
        public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
        public void ResetKeysAndFixPathsToDefaults() { }
    }

    /// <summary>
    /// Config service that always throws on SetWindowDecorationMode.
    /// </summary>
    private sealed class ThrowingConfigService : IConfigService
    {
        public void SetWindowDecorationMode(WindowDecorationMode mode) => throw new IOException("Failed to write config file");

        // --- Null implementations for other IConfigService members ---
        public IReadOnlyList<string> MountPathHistory => [];
        public IReadOnlyList<string> DataStoreHistory => [];
        public IReadOnlyList<string> CreateSetHistory => [];
        public IReadOnlyList<string> ExportPathHistory => [];
        public IReadOnlyList<string> OgmrYamlHistory => [];
        public IReadOnlyList<string> OgmrOutputHistory => [];
        public IReadOnlyList<string> DatPathHistory => [];
        public IReadOnlyList<string> ImageBrowseHistory => [];
        public WindowStateConfig WindowState => null;
        public IReadOnlyDictionary<string, double> ColumnWidths => null;
        public string MountUid => "";
        public string MountGid => "";
        public bool MountAllowOther => false;
        public void AddMountPath(string path) { }
        public void AddDataStorePath(string path) { }
        public void AddCreateSetPath(string path) { }
        public void AddExportPath(string path) { }
        public void AddOgmrYamlPath(string path) { }
        public void AddOgmrOutputPath(string path) { }
        public void AddDatPath(string path) { }
        public void AddImageBrowsePath(string path) { }
        public string GetExportFormat(string system, string sourceFormat) => null;
        public void SetExportFormat(string system, string sourceFormat, string targetFormat) { }
        public void SetWindowState(int width, int height, int x, int y) { }
        public FormatOptionsEntry GetFormatOptions(string system, string sourceFormat, string targetFormat) => null;
        public void SetFormatOptions(string system, string sourceFormat, string targetFormat, FormatOptionsEntry options) { }
        public void SetColumnWidths(Dictionary<string, double> widths) { }
        public void SetMountLinuxOptions(string uid, string gid, bool allowOther) { }
        public WindowDecorationMode GetWindowDecorationMode() => WindowDecorationMode.ClientSideDecorations;
        public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
        public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
        public void ResetKeysAndFixPathsToDefaults() { }
    }
}