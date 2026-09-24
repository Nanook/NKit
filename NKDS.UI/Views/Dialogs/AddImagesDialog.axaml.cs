using Avalonia.Input;
using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Primitives;

namespace NkdsUi.Views.Dialogs;

public partial class AddImagesDialog : ReactiveWindow<AddImagesDialogViewModel>
{
    public AddImagesDialog()
    {
        InitializeComponent();

        // Wire up drag-and-drop handlers
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        this.WhenActivated(disposables =>
        {
            if (ViewModel is not { } vm)
                return;

            // Register file picker interaction handler
            vm.ShowFilePicker.RegisterHandler(async interaction =>
            {
                IStorageFolder? suggestedStart = null;
                try
                {
                    if (vm.ImageBrowseHistory.Count > 0)
                    {
                        string lastDir = vm.ImageBrowseHistory[0];
                        if (Directory.Exists(lastDir))
                            suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastDir);
                    }
                }
                catch { /* Ignore — open picker without suggested location */ }

                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Image Files",
                    AllowMultiple = true,
                    SuggestedStartLocation = suggestedStart,
                    FileTypeFilter = new[]
                    {
                        FilePickerFileTypes.All,
                        new FilePickerFileType("Supported Types")
                        {
                            Patterns = AddImagesDialogViewModel.SupportedExtensions.ToArray()
                        },
                        new FilePickerFileType("Images")
                        {
                            Patterns = AddImagesDialogViewModel.ImageExtensions.ToArray()
                        },
                        new FilePickerFileType("Archives")
                        {
                            Patterns = AddImagesDialogViewModel.ArchiveExtensions.ToArray()
                        }
                    }
                });

                List<string> paths = files
                    .Select(f => f.TryGetLocalPath())
                    .Where(p => p != null)
                    .Cast<string>()
                    .ToList();

                interaction.SetOutput(paths);
            }).DisposeWith(disposables);

            // Register folder picker interaction handler
            vm.ShowFolderPicker.RegisterHandler(async interaction =>
            {
                IStorageFolder? suggestedStart = null;
                try
                {
                    if (vm.ImageBrowseHistory.Count > 0)
                    {
                        string lastDir = vm.ImageBrowseHistory[0];
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

                List<string> paths = folders
                    .Select(f => f.TryGetLocalPath())
                    .Where(p => p != null)
                    .Cast<string>()
                    .ToList();

                interaction.SetOutput(paths);
            }).DisposeWith(disposables);

            // Register close dialog interaction handler
            vm.CloseDialog.RegisterHandler(interaction =>
            {
                Close(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects &= DragDropEffects.Copy;

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

        if (!hasFileOrFolder)
            e.DragEffects = DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        IReadOnlyList<IDataTransferItem>? items = e.DataTransfer?.Items;
        if (items == null || !items.Any())
            return;

        // Extract paths from dropped items
        List<string> paths = await Task.Run(() =>
        {
            List<string> list = new List<string>();
            foreach (IDataTransferItem it in items)
            {
                try
                {
                    IStorageItem? val = it.TryGetFile();
                    if (val is IStorageFile sf)
                    {
                        string? p = sf.Path?.LocalPath;
                        if (!string.IsNullOrEmpty(p))
                            list.Add(p);
                    }
                    else if (val is IStorageFolder folder)
                    {
                        string? p = folder.Path?.LocalPath;
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                        {
                            foreach (string file in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                                list.Add(file);
                        }
                    }
                }
                catch { }
            }
            return list.Distinct().ToList();
        });

        if (paths.Count == 0)
            return;

        // Add dropped files to the ViewModel's SelectedFiles (avoiding duplicates)
        foreach (string? file in paths)
        {
            if (!vm.SelectedFiles.Contains(file))
                vm.SelectedFiles.Add(file);
        }
    }
}