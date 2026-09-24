using NKDS.DatVerification;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NkdsUi.Models;
using NkdsUi.Services;
using NkdsUi.ViewModels;
using NkdsUi.Views.Dialogs;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Primitives;

namespace NkdsUi.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private readonly IConfigService _configService = null!;
    private GroupingWindow? _groupingWindow;
    private DatVerificationWindow? _datVerificationWindow;
    private bool _isWindowReady;
    private bool _useNativeTitleBar;

    /// <summary>
    /// Parameterless constructor required by Avalonia XAML loader / designer.
    /// Not used at runtime — the ServiceRegistry overload is called from App.axaml.cs.
    /// </summary>
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
    public MainWindow() : this(null)
    {
    }
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

    public MainWindow(ServiceRegistry services)
    {
        InitializeComponent();

        // Designer/XAML loader path — no services available
        if (services == null) return;

        // On Linux, set window decorations based on persisted preference
        if (OperatingSystem.IsLinux())
        {
            WindowDecorationMode mode = services.ConfigService.GetWindowDecorationMode();
            if (mode == WindowDecorationMode.NativeTitleBar)
            {
                WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
                ExtendClientAreaToDecorationsHint = false;
                ExtendClientAreaTitleBarHeightHint = -1;
                _useNativeTitleBar = true;
                Title = $"NKDS v{Nanook.NKit.AppSettings.GetVersion()}";

                // Hide client-drawn caption buttons (native WM provides its own)
                StackPanel? captionButtons = this.FindControl<Avalonia.Controls.StackPanel>("LinuxCaptionButtons");
                if (captionButtons != null)
                    captionButtons.IsVisible = false;
            }
            else
            {
                // CSD mode: no WM decorations, app draws its own chrome.
                // Disable ExtendClientAreaToDecorationsHint to prevent Avalonia from
                // rendering a managed title bar with the Title text over our logo.
                WindowDecorations = Avalonia.Controls.WindowDecorations.None;
                ExtendClientAreaToDecorationsHint = false;
                // Title is blank in CSD mode — the custom chrome provides the visual identity.
                // The OS taskbar still shows a title, so we set it here even though it is not
                // visible inside the window chrome itself.
                Title = $"NKDS v{Nanook.NKit.AppSettings.GetVersion()}";
            }
        }
        else
        {
            // Windows / macOS: ExtendClientAreaToDecorationsHint suppresses the OS title bar
            // visually, but the taskbar and alt-tab still use Title. Set it so the OS has a
            // meaningful label even though the custom chrome draws the visual identity.
            Title = $"NKDS v{Nanook.NKit.AppSettings.GetVersion()}";
        }

        // Obtain services from ServiceRegistry
        _configService = services.ConfigService;

        // Restore window state if previously saved
        WindowStateConfig? windowState = services.ConfigService.WindowState;
        if (windowState != null)
        {
            Width = windowState.Width;
            Height = windowState.Height;
            Position = new Avalonia.PixelPoint(windowState.X, windowState.Y);
        }

        // Create and set the ViewModel using services from the registry
        MainWindowViewModel viewModel = new MainWindowViewModel(
            services.DataStoreService,
            services.BlockComparisonService,
            services.HashCacheService,
            services.SimilarityGroupService,
            services.ThresholdGroupingService,
            services.StatsCalculationService,
            platformFsHostFactory: services.PlatformFsHostFactory,
            configService: services.ConfigService,
            serviceRegistry: services);
        DataContext = viewModel;

        // Subscribe to window state change events for persistence
        Opened += (_, _) => _isWindowReady = true;
        PositionChanged += (_, _) => saveWindowState();
        this.GetObservable(ClientSizeProperty).Subscribe(_ => saveWindowState());

        // Wire up drag-and-drop handlers for the main window.
        // Dropping files opens the Add Images dialog pre-populated with the dropped files.
        AddHandler(DragDrop.DragOverEvent, OnMainWindowDragOver, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnMainWindowDrop, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // Register interaction handlers for folder/file picker dialogs
        this.WhenActivated(disposables =>
        {
            viewModel.ShowFolderPicker.RegisterHandler(async interaction =>
            {
                // Use the most recent DataStore path as the suggested start location
                IStorageFolder? suggestedStart = null;
                try
                {
                    if (_configService?.DataStoreHistory.Count > 0)
                    {
                        string lastPath = _configService.DataStoreHistory[0];
                        if (Directory.Exists(lastPath))
                            suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastPath);
                        else if (Directory.Exists(Path.GetDirectoryName(lastPath)))
                            suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(lastPath)!);
                    }
                }
                catch { /* Ignore — open picker without suggested location */ }

                IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select DataStore Directory",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedStart
                });

                string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                interaction.SetOutput(path);
            }).DisposeWith(disposables);

            viewModel.ShowFilePicker.RegisterHandler(async interaction =>
            {
                // Use the most recent DataStore path's directory as the suggested start location
                IStorageFolder? suggestedStart = null;
                try
                {
                    if (_configService?.DataStoreHistory.Count > 0)
                    {
                        string lastPath = _configService.DataStoreHistory[0];
                        string? dir = Directory.Exists(lastPath) ? lastPath : Path.GetDirectoryName(lastPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(dir);
                    }
                }
                catch { /* Ignore — open picker without suggested location */ }

                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open .nkds File",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedStart,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("NKit DataStore Files")
                        {
                            Patterns = new[] { "*.nkds" }
                        }
                    }
                });

                string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                interaction.SetOutput(path);
            }).DisposeWith(disposables);

            viewModel.ShowSaveFilePicker.RegisterHandler(async interaction =>
            {
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Image List to YAML",
                    DefaultExtension = "yaml",
                    SuggestedFileName = "image-list",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("YAML Files")
                        {
                            Patterns = new[] { "*.yaml", "*.yml" }
                        }
                    }
                });

                string? path = file?.TryGetLocalPath();
                interaction.SetOutput(path);
            }).DisposeWith(disposables);

            viewModel.ComparisonMode.ShowSaveFileDialog.RegisterHandler(async interaction =>
            {
                IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Similarity Groups",
                    DefaultExtension = "yaml",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("YAML Files")
                        {
                            Patterns = new[] { "*.yaml", "*.yml" }
                        }
                    }
                });

                string? path = file?.TryGetLocalPath();
                interaction.SetOutput(path);
            }).DisposeWith(disposables);

            viewModel.ComparisonMode.ShowGroupsDialog.RegisterHandler(async interaction =>
            {
                ThresholdGroupsDialogViewModel dialogVm = interaction.Input;
                ThresholdGroupsDialog dialog = new ThresholdGroupsDialog { DataContext = dialogVm };

                // Wire the CloseDialog interaction to close the window
                dialogVm.CloseDialog.RegisterHandler(closeInteraction =>
                {
                    dialog.Close();
                    closeInteraction.SetOutput(RxVoid.Default);
                });

                await dialog.ShowDialog(this);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            viewModel.StatsCalculation.ShowGraphsDialog.RegisterHandler(async interaction =>
            {
                StatsGraphsViewModel dialogVm = interaction.Input;
                StatsGraphsDialog dialog = new StatsGraphsDialog { DataContext = dialogVm };
                await dialog.ShowDialog(this);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            // --- Operation dialog interaction handlers ---

            viewModel.ShowCreateSetDialog.RegisterHandler(async interaction =>
            {
                CreateSetDialogViewModel dialogVm = interaction.Input;
                CreateSetDialog dialog = new Dialogs.CreateSetDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowAddImagesDialog.RegisterHandler(async interaction =>
            {
                AddImagesDialogViewModel dialogVm = interaction.Input;
                AddImagesDialog dialog = new Dialogs.AddImagesDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowExportDialog.RegisterHandler(async interaction =>
            {
                ExportDialogViewModel dialogVm = interaction.Input;
                ExportDialog dialog = new Dialogs.ExportDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowCompactDialog.RegisterHandler(async interaction =>
            {
                CompactDialogViewModel dialogVm = interaction.Input;
                CompactDialog dialog = new Dialogs.CompactDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowRollbackDialog.RegisterHandler(async interaction =>
            {
                RollbackDialogViewModel dialogVm = interaction.Input;
                RollbackDialog dialog = new Dialogs.RollbackDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowMountDialog.RegisterHandler(async interaction =>
            {
                MountDialogViewModel dialogVm = interaction.Input;
                MountDialog dialog = new Dialogs.MountDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            viewModel.ShowAdd1GmrDialog.RegisterHandler(async interaction =>
            {
                Add1GmrDialogViewModel dialogVm = interaction.Input;
                Add1GmrDialog dialog = new Dialogs.Add1GmrDialog { DataContext = dialogVm };
                bool result = await dialog.ShowDialog<bool>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);


            viewModel.ShowGroupingWindow.RegisterHandler(interaction =>
            {
                GroupingWindowViewModel vm = interaction.Input;

                if (_groupingWindow != null && _groupingWindow.DataContext == vm)
                {
                    // Window already open with same VM — bring to front
                    _groupingWindow.Activate();
                }
                else
                {
                    // Create and show new GroupingWindow (non-modal)
                    _groupingWindow = new GroupingWindow { DataContext = vm };
                    _groupingWindow.WindowClosed = () =>
                    {
                        viewModel.OnGroupWindowClosed();
                        _groupingWindow = null;
                    };
                    _groupingWindow.Show();
                }

                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            viewModel.ShowDatVerificationWindow.RegisterHandler(interaction =>
            {
                DatVerificationWindowViewModel vm = interaction.Input;

                if (_datVerificationWindow != null && _datVerificationWindow.DataContext == vm)
                {
                    // Window already open with same VM — bring to front
                    _datVerificationWindow.Activate();
                }
                else
                {
                    // Create and show new DatVerificationWindow (non-modal)
                    _datVerificationWindow = new DatVerificationWindow { DataContext = vm };
                    _datVerificationWindow.WindowClosed = () =>
                    {
                        viewModel.OnDatWindowClosed();
                        _datVerificationWindow = null;
                    };
                    _datVerificationWindow.Show();
                }

                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            viewModel.ShowSettingsDialog.RegisterHandler(async interaction =>
            {
                SettingsDialogViewModel settingsVm = new SettingsDialogViewModel(
                    services.FileAssociationService,
                    services.ConfigService);
                SettingsDialog dialog = new Dialogs.SettingsDialog { DataContext = settingsVm };
                await dialog.ShowDialog(this);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }

    // ─── Drag-and-Drop Handlers ─────────────────────────────────────────────────

    private void OnMainWindowDragOver(object? sender, DragEventArgs e)
    {
        // Only allow drop when a DataStore is open
        if (ViewModel is not { } vm || !vm.Toolbar.HasDataStoreOpen)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // Require at least one file or folder item
        bool hasFileOrFolder = items.Any(it =>
        {
            try
            {
                IStorageItem? v = it.TryGetFile();
                return v is IStorageFile || v is IStorageFolder;
            }
            catch { return false; }
        });

        if (hasFileOrFolder)
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;

        e.Handled = true;
    }

    private async void OnMainWindowDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || !vm.Toolbar.HasDataStoreOpen)
            return;

        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
            return;

        e.Handled = true;

        // Extract storage item paths on the UI thread (COM objects may not be thread-safe)
        List<string> filePaths = new List<string>();
        List<string> folderPaths = new List<string>();
        foreach (IDataTransferItem it in items)
        {
            try
            {
                IStorageItem? val = it.TryGetFile();
                if (val is IStorageFile sf)
                {
                    string? p = sf.Path?.LocalPath;
                    if (!string.IsNullOrEmpty(p))
                        filePaths.Add(p);
                }
                else if (val is IStorageFolder folder)
                {
                    string? p = folder.Path?.LocalPath;
                    if (!string.IsNullOrEmpty(p))
                        folderPaths.Add(p);
                }
            }
            catch { }
        }

        // Recursively enumerate folder contents on a background thread
        List<string> paths = await Task.Run(() =>
        {
            List<string> list = new List<string>(filePaths);
            foreach (string folderPath in folderPaths)
            {
                if (Directory.Exists(folderPath))
                {
                    foreach (string file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                        list.Add(file);
                }
            }
            return list.Distinct().ToList();
        });

        if (paths.Count == 0)
            return;

        // Open the Add Images dialog pre-populated with the dropped files
        vm.ExecuteAddWithDroppedFiles(paths);
    }

    private void saveWindowState()
    {
        if (!_isWindowReady) return;
        _configService?.SetWindowState((int)ClientSize.Width, (int)ClientSize.Height, Position.X, Position.Y);
    }

    private void titleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_useNativeTitleBar) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void minimizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void maximizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void closeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void resizeN_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.North, e);
    private void resizeS_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.South, e);
    private void resizeW_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.West, e);
    private void resizeE_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.East, e);
    private void resizeSE_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthEast, e);
    private void resizeSW_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthWest, e);
    private void resizeNE_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            Close();
        else
            BeginResizeDrag(WindowEdge.NorthEast, e);
    }
    private void resizeNW_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthWest, e);
}