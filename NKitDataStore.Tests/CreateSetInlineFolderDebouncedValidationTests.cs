using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives;
using Microsoft.Reactive.Testing;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Builder;
using Xunit;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for CreateSetDialogViewModel debounced validation timing.
/// Validates: Requirement 5.3
/// Must run sequentially with other tests that modify the global RxSchedulers state.
/// - Typed input triggers validation after 300ms debounce
/// - Browse/dropdown selection triggers immediate validation (no debounce)
/// </summary>
[Collection("RxScheduler Sequential Tests")]
public class CreateSetInlineFolderDebouncedValidationTests : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public CreateSetInlineFolderDebouncedValidationTests()
    {
        EnsureReactiveUIInitialized();
    }

    public void Dispose()
    {
        NkdsUi.RxSchedulers.SetSchedulerForTest(null);
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
    /// Typed input (setting FolderPath directly) triggers validation only after 300ms debounce.
    /// Before 300ms elapses, the FolderPathError should not have been updated by the debounced subscription.
    /// </summary>
    [Fact]
    public void TypedInput_TriggersValidation_After300msDebounce()
    {
        // Arrange — use TestScheduler to control time
        var testScheduler = new TestScheduler();
        NkdsUi.RxSchedulers.SetSchedulerForTest(testScheduler);

        var configService = new StubConfigService(dataStoreHistory: []);
        var vm = new CreateSetDialogViewModel(null, configService);

        // Advance past initial subscription setup
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        // Act — simulate typing a non-existent path
        vm.FolderPath = @"Z:\NonExistent\Path\For\Testing";

        // Assert — before 300ms, the debounced validation has NOT fired
        // FolderPathError should still be null (initial state)
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(299).Ticks);
        Assert.Null(vm.FolderPathError);

        // Act — advance past the 300ms threshold
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(2).Ticks);

        // Assert — now validation has fired and set the error for the non-existent path
        Assert.NotNull(vm.FolderPathError);
        Assert.Equal("Path must be an existing directory", vm.FolderPathError);
    }

    /// <summary>
    /// BrowseCommand triggers immediate validation without waiting for the 300ms debounce.
    /// When a folder is selected via the browse picker, FolderPathError is updated right away.
    /// </summary>
    [Fact]
    public async Task BrowseCommand_TriggersImmediateValidation_NoDebounce()
    {
        // Arrange — use TestScheduler so the debounced path does NOT fire
        var testScheduler = new TestScheduler();
        NkdsUi.RxSchedulers.SetSchedulerForTest(testScheduler);

        var configService = new StubConfigService(dataStoreHistory: []);
        var vm = new CreateSetDialogViewModel(null, configService);

        // Advance past initial setup
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        // Set up the folder picker interaction to return a non-existent path
        const string nonExistentPath = @"Z:\BrowseSelected\NonExistent";
        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            interaction.SetOutput(nonExistentPath);
        });

        // Act — execute BrowseCommand (which calls ValidateFolderPath directly)
        await vm.BrowseCommand.Execute();

        // Assert — validation error is set IMMEDIATELY, without advancing the scheduler
        // (no debounce wait needed for browse selection)
        Assert.Equal(nonExistentPath, vm.FolderPath);
        Assert.NotNull(vm.FolderPathError);
        Assert.Equal("Path must be an existing directory", vm.FolderPathError);
    }

    /// <summary>
    /// Browse selecting a valid directory triggers immediate validation and clears errors.
    /// </summary>
    [Fact]
    public async Task BrowseCommand_ValidDirectory_ClearsErrorImmediately()
    {
        // Arrange — use TestScheduler so debounced path doesn't fire
        var testScheduler = new TestScheduler();
        NkdsUi.RxSchedulers.SetSchedulerForTest(testScheduler);

        var configService = new StubConfigService(dataStoreHistory: []);
        var vm = new CreateSetDialogViewModel(null, configService);

        // Advance past initial setup
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        // First, set a non-existent path and advance past debounce to get an error
        vm.FolderPath = @"Z:\NonExistent";
        testScheduler.AdvanceBy(TimeSpan.FromMilliseconds(301).Ticks);
        Assert.NotNull(vm.FolderPathError); // Pre-condition: error is set

        // Set up folder picker to return a valid existing directory
        var validDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            interaction.SetOutput(validDir);
        });

        // Act — browse selects a valid directory
        await vm.BrowseCommand.Execute();

        // Assert — error is cleared immediately (no scheduler advance needed)
        Assert.Null(vm.FolderPathError);
    }

    /// <summary>
    /// Stub IConfigService for testing.
    /// </summary>
    private sealed class StubConfigService : IConfigService
    {
        private readonly IReadOnlyList<string> _createSetHistory;

        public StubConfigService(IReadOnlyList<string> dataStoreHistory)
        {
            _createSetHistory = dataStoreHistory;
        }

        public IReadOnlyList<string> DataStoreHistory => [];
        public IReadOnlyList<string> CreateSetHistory => _createSetHistory;
        public IReadOnlyList<string> MountPathHistory => [];
        public IReadOnlyList<string> ExportPathHistory => [];
        public IReadOnlyList<string> OneGmrYamlHistory => [];
        public IReadOnlyList<string> OneGmrOutputHistory => [];
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
        public void AddOneGmrYamlPath(string path) { }
        public void AddOneGmrOutputPath(string path) { }
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
        public void SetWindowDecorationMode(WindowDecorationMode mode) { }
        public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
        public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
        public void ResetKeysAndFixPathsToDefaults() { }
    }
}

