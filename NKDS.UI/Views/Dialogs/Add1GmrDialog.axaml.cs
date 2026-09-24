using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views.Dialogs;

public partial class Add1GmrDialog : Window
{
    private MultipleDisposable? _interactionDisposables;

    public Add1GmrDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        // Wire up drag-and-drop handlers on window level
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Also handle on the DataGrid with handledEventsToo=true so we get events
        // even if the DataGrid's internal logic marks them as handled
        ImagesDataGrid.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        ImagesDataGrid.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);

        // YAML history popup toggle
        YamlHistoryButton.Click += (_, _) =>
        {
            YamlHistoryPopup.IsOpen = !YamlHistoryPopup.IsOpen;
        };

        // YAML history selection → fill path and close popup
        YamlHistoryList.SelectionChanged += (_, _) =>
        {
            if (YamlHistoryList.SelectedItem is string selectedPath && DataContext is Add1GmrDialogViewModel vm)
            {
                vm.OgmrYamlPath = selectedPath;
                YamlHistoryPopup.IsOpen = false;
                YamlHistoryList.SelectedItem = null;
            }
        };

        // Output history popup toggle
        OutputHistoryButton.Click += (_, _) =>
        {
            OutputHistoryPopup.IsOpen = !OutputHistoryPopup.IsOpen;
        };

        // Output history selection → fill path and close popup
        OutputHistoryList.SelectionChanged += (_, _) =>
        {
            if (OutputHistoryList.SelectedItem is string selectedPath && DataContext is Add1GmrDialogViewModel vm)
            {
                vm.OutputFolder = selectedPath;
                OutputHistoryPopup.IsOpen = false;
                OutputHistoryList.SelectedItem = null;
            }
        };

        // DataGrid row styling for unmatched items
        ImagesDataGrid.LoadingRow += OnDataGridLoadingRow;

        // DataGrid sorting via pointer events on headers
        ImagesDataGrid.AddHandler(InputElement.PointerPressedEvent, OnDataGridPointerPressed, RoutingStrategies.Tunnel);
        ImagesDataGrid.AddHandler(InputElement.PointerReleasedEvent, OnDataGridPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        if (DataContext is not Add1GmrDialogViewModel viewModel)
            return;

        _interactionDisposables = new MultipleDisposable();

        // Register YAML file picker interaction handler
        viewModel.ShowYamlPicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.OgmrYamlHistory.Count > 0)
                {
                    string lastPath = viewModel.OgmrYamlHistory[0];
                    string? dir = Path.GetDirectoryName(lastPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(dir);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select 1GMR YAML File",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("YAML Files")
                    {
                        Patterns = new[] { "*.yaml", "*.yml" }
                    },
                    FilePickerFileTypes.All
                }
            });

            string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);

        // Register disc image file picker interaction handler
        viewModel.ShowImagePicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.ImageBrowseHistory.Count > 0)
                {
                    string lastDir = viewModel.ImageBrowseHistory[0];
                    if (Directory.Exists(lastDir))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastDir);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Disc Image Files",
                AllowMultiple = true,
                SuggestedStartLocation = suggestedStart,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Disc Image Files")
                    {
                        Patterns = AddImagesDialogViewModel.SupportedExtensions.ToArray()
                    },
                    FilePickerFileTypes.All
                }
            });

            List<string> paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => p != null)
                .Cast<string>()
                .ToList();

            interaction.SetOutput(paths);
        }).DisposeWith(_interactionDisposables);

        // Register image directory picker interaction handler
        viewModel.ShowImageDirectoryPicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.ImageBrowseHistory.Count > 0)
                {
                    string lastDir = viewModel.ImageBrowseHistory[0];
                    if (Directory.Exists(lastDir))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastDir);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Directory Containing Images",
                AllowMultiple = true,
                SuggestedStartLocation = suggestedStart
            });

            List<string> folderPaths = folders
                .Select(f => f.TryGetLocalPath())
                .Where(p => p != null)
                .Cast<string>()
                .ToList();

            interaction.SetOutput(folderPaths);
        }).DisposeWith(_interactionDisposables);

        // Register folder picker interaction handler
        viewModel.ShowFolderPicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.OutputFolderHistory.Count > 0)
                {
                    string lastPath = viewModel.OutputFolderHistory[0];
                    if (Directory.Exists(lastPath))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastPath);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Directory",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart
            });

            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);

        // When ConfirmCommand returns true (valid), close with true; null means validation failed (don't close)
        viewModel.ConfirmCommand
            .Subscribe(result => { if (result == true) Close(true); })
            .DisposeWith(_interactionDisposables);

        // When CancelCommand executes, close with false
        viewModel.CancelCommand
            .Subscribe(result => Close(result))
            .DisposeWith(_interactionDisposables);
    }

    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is MatchedFileItem item)
        {
            if (!item.IsMatched)
                e.Row.Classes.Add("unmatched");
            else
                e.Row.Classes.Remove("unmatched");
        }
    }

    private Dictionary<string, bool> _columnSortDirections = new();
    private string? _currentSortColumn;
    private bool _pointerPressedOnHeader;
    private Point _pointerPressPosition;

    private void OnDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerPressedOnHeader = false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Control? source = e.Source as Avalonia.Controls.Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        Thumb? thumb = source?.FindAncestorOfType<Thumb>();
        if (thumb != null)
            return;

        _pointerPressedOnHeader = true;
        _pointerPressPosition = e.GetPosition(this);
    }

    private void OnDataGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerPressedOnHeader)
            return;
        _pointerPressedOnHeader = false;

        if (DataContext is not Add1GmrDialogViewModel vm)
            return;

        Point releasePosition = e.GetPosition(this);
        Point delta = _pointerPressPosition - releasePosition;
        if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
            return;

        Control? source = e.Source as Avalonia.Controls.Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        // Find which column was clicked
        int columnIndex = -1;
        for (int i = 0; i < ImagesDataGrid.Columns.Count; i++)
        {
            if (ImagesDataGrid.Columns[i].Header?.ToString()?.TrimEnd(' ', '▲', '▼').Trim() ==
                columnHeader.Content?.ToString()?.TrimEnd(' ', '▲', '▼').Trim())
            {
                columnIndex = i;
                break;
            }
        }
        if (columnIndex < 0) return;

        string columnName = columnIndex == 0 ? "Name" : "1GMR Set";

        // Toggle sort direction
        if (_currentSortColumn == columnName)
            _columnSortDirections[columnName] = !_columnSortDirections.GetValueOrDefault(columnName, true);
        else
        {
            _currentSortColumn = columnName;
            _columnSortDirections[columnName] = true;
        }

        bool ascending = _columnSortDirections[columnName];

        List<MatchedFileItem> sorted = columnName switch
        {
            "Name" => ascending
                ? vm.MatchedFiles.OrderBy(f => f.FileName).ToList()
                : vm.MatchedFiles.OrderByDescending(f => f.FileName).ToList(),
            _ => ascending
                ? vm.MatchedFiles.OrderBy(f => f.MatchedSetName).ToList()
                : vm.MatchedFiles.OrderByDescending(f => f.MatchedSetName).ToList()
        };

        vm.MatchedFiles.Clear();
        foreach (MatchedFileItem? item in sorted)
            vm.MatchedFiles.Add(item);

        // Update headers with arrows
        ImagesDataGrid.Columns[0].Header = columnName == "Name" ? (ascending ? "Name ▲" : "Name ▼") : "Name";
        ImagesDataGrid.Columns[1].Header = columnName == "1GMR Set" ? (ascending ? "1GMR Set ▲" : "1GMR Set ▼") : "1GMR Set";
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
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

    private bool _processingDrop;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_processingDrop)
            return;

        if (DataContext is not Add1GmrDialogViewModel vm)
            return;

        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
            return;

        e.Handled = true;
        _processingDrop = true;
        try
        {
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

            // Add dropped files through the ViewModel's matching logic
            vm.AddFiles(paths);
        }
        finally
        {
            _processingDrop = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;
        ImagesDataGrid.LoadingRow -= OnDataGridLoadingRow;
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}