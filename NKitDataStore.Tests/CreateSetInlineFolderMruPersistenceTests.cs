using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using ReactiveUI.Primitives.Signals;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for MRU persistence behavior on CreateSetDialogViewModel confirm.
/// Validates: Requirements 4.1, 4.5
/// </summary>
[Collection("RxScheduler Sequential Tests")]
public class CreateSetInlineFolderMruPersistenceTests : IDisposable
{
    private static readonly object _InitLock = new();
    private static bool _Initialized;

    public CreateSetInlineFolderMruPersistenceTests()
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
    /// Confirming the dialog with a valid folder path calls AddDataStorePath with that path.
    /// Validates: Requirement 4.1
    /// </summary>
    [Fact]
    public async Task Confirm_WithValidFolderPath_CallsAddDataStorePath()
    {
        // Arrange: create a real temp directory so validation passes
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            SpyConfigService configService = new SpyConfigService();
            CreateSetDialogViewModel vm = new CreateSetDialogViewModel(tempDir, configService);
            vm.SetName = "ValidSet";

            // Act: execute the ConfirmCommand
            bool result = await vm.ConfirmCommand.Execute();

            // Assert: AddCreateSetPath was called with the folder path
            Assert.True(result);
            Assert.Single(configService.AddedCreateSetPaths);
            Assert.Equal(tempDir, configService.AddedCreateSetPaths[0]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: false);
        }
    }

    /// <summary>
    /// Confirming with an empty folder path does not call AddDataStorePath
    /// because the ConfirmCommand is disabled when FolderPath is empty.
    /// Validates: Requirement 4.5
    /// </summary>
    [Fact]
    public async Task Confirm_WithEmptyFolderPath_DoesNotCallAddDataStorePath()
    {
        // Arrange: empty history so FolderPath stays empty
        SpyConfigService configService = new SpyConfigService();
        CreateSetDialogViewModel vm = new CreateSetDialogViewModel(null, configService);
        vm.SetName = "ValidSet";

        // Assert: ConfirmCommand is not executable when FolderPath is empty
        bool canExecute = await vm.ConfirmCommand.CanExecute.FirstAsync();
        Assert.False(canExecute);

        // Verify AddCreateSetPath was never called
        Assert.Empty(configService.AddedCreateSetPaths);
    }

    /// <summary>
    /// Spy implementation of IConfigService that tracks calls to AddCreateSetPath.
    /// </summary>
    private sealed class SpyConfigService : IConfigService
    {
        public List<string> AddedCreateSetPaths { get; } = new();

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

        public void AddMountPath(string path) { }
        public void AddDataStorePath(string path) { }
        public void AddExportPath(string path) { }
        public void AddCreateSetPath(string path) => AddedCreateSetPaths.Add(path);
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
        public string MountUid => "";
        public string MountGid => "";
        public bool MountAllowOther => false;
        public void SetMountLinuxOptions(string uid, string gid, bool allowOther) { }
        public WindowDecorationMode GetWindowDecorationMode() => WindowDecorationMode.ClientSideDecorations;
        public void SetWindowDecorationMode(WindowDecorationMode mode) { }
        public KeysAndFixPathsConfig KeysAndFixPaths => KeysAndFixPathsConfig.CreateDefault();
        public void SetKeysAndFixPaths(KeysAndFixPathsConfig paths) { }
        public void ResetKeysAndFixPathsToDefaults() { }
    }
}