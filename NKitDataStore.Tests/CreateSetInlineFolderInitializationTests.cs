using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for CreateSetDialogViewModel initialization, folder pre-population,
/// and BrowseCommand / folder picker interaction.
/// Validates: Requirements 1.3, 1.4, 2.2, 2.3, 2.4, 2.5
/// </summary>
[Collection("RxScheduler Sequential Tests")]
public class CreateSetInlineFolderInitializationTests : IDisposable
{
    private static readonly object _InitLock = new();
    private static bool _Initialized;

    public CreateSetInlineFolderInitializationTests()
    {
        ensureReactiveUIInitializee();
        NkdsUi.RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => NkdsUi.RxSchedulers.SetSchedulerForTest(null);

    private static void ensureReactiveUIInitializee()
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

    [Fact]
    public void Constructor_WithSessionPath_PrePopulatesFolderPath()
    {
        // Arrange
        StubConfigService configService = new StubConfigService(dataStoreHistory: ["C:\\OldPath"]);
        const string sessionPath = @"D:\MyDataStore";

        // Act
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(sessionPath, configService);

        // Assert — session path takes priority over MRU history
        Assert.Equal(sessionPath, vm.FolderPath);
    }

    [Fact]
    public void Constructor_WithNullPathAndNonEmptyMru_UsesFirstMruEntry()
    {
        // Arrange
        List<string> mruEntries = new List<string> { @"C:\First", @"C:\Second", @"C:\Third" };
        StubConfigService configService = new StubConfigService(dataStoreHistory: mruEntries);

        // Act
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);

        // Assert — first MRU entry is used when no session path is provided
        Assert.Equal(@"C:\First", vm.FolderPath);
    }

    [Fact]
    public void Constructor_WithNullPathAndEmptyMru_LeavesFolderPathEmpty()
    {
        // Arrange
        StubConfigService configService = new StubConfigService(dataStoreHistory: []);

        // Act
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);

        // Assert — FolderPath is empty when no session and no MRU history
        Assert.Equal("", vm.FolderPath);
    }

    // --- BrowseCommand and folder picker interaction tests ---
    // Validates: Requirements 2.2, 2.3, 2.4, 2.5

    [Fact]
    public async Task BrowseCommand_OpensInteraction_WithCurrentFolderPathAsStartLocation()
    {
        // Arrange — set FolderPath to a real existing directory
        StubConfigService configService = new StubConfigService(dataStoreHistory: []);
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);
        string existingDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        vm.FolderPath = existingDir;

        string receivedSuggestedPath = "__not_called__";
        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            receivedSuggestedPath = interaction.Input;
            interaction.SetOutput(null); // cancel
        });

        // Act
        await vm.BrowseCommand.Execute();

        // Assert — the interaction was called with the current FolderPath as suggested path
        Assert.Equal(existingDir, receivedSuggestedPath);
    }

    [Fact]
    public async Task BrowseCommand_SelectingFolder_UpdatesFolderPath()
    {
        // Arrange
        StubConfigService configService = new StubConfigService(dataStoreHistory: []);
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);
        vm.FolderPath = "";
        const string selectedFolder = @"C:\SelectedFolder";

        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            interaction.SetOutput(selectedFolder);
        });

        // Act
        await vm.BrowseCommand.Execute();

        // Assert — FolderPath is updated to the selected folder
        Assert.Equal(selectedFolder, vm.FolderPath);
    }

    [Fact]
    public async Task BrowseCommand_CancellingPicker_RetainsPreviousFolderPath()
    {
        // Arrange
        StubConfigService configService = new StubConfigService(dataStoreHistory: []);
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);
        string existingDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        vm.FolderPath = existingDir;

        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            interaction.SetOutput(null); // user cancelled
        });

        // Act
        await vm.BrowseCommand.Execute();

        // Assert — FolderPath retains its previous value
        Assert.Equal(existingDir, vm.FolderPath);
    }

    [Fact]
    public async Task BrowseCommand_WithNonExistentPath_OpensPickerWithoutSuggestedLocation()
    {
        // Arrange — set FolderPath to a path that doesn't exist
        StubConfigService configService = new StubConfigService(dataStoreHistory: []);
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);
        vm.FolderPath = @"Z:\NonExistent\Path\That\Does\Not\Exist";

        string receivedSuggestedPath = "__not_called__";
        vm.ShowFolderPicker.RegisterHandler(interaction =>
        {
            receivedSuggestedPath = interaction.Input;
            interaction.SetOutput(null); // cancel
        });

        // Act
        await vm.BrowseCommand.Execute();

        // Assert — suggested path is null when current FolderPath doesn't exist
        Assert.Null(receivedSuggestedPath);
    }

    /// <summary>
    /// Stub IConfigService with configurable DataStoreHistory for testing pre-population logic.
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

        // --- No-op implementations for other IConfigService members ---
        public IReadOnlyList<string> MountPathHistory => [];
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
        public void SetWindowDecorationMode(WindowDecorationMode mode) { }
        public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
        public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
        public void ResetKeysAndFixPathsToDefaults() { }
    }
}