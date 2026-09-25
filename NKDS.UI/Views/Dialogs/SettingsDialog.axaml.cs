using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views.Dialogs;

public partial class SettingsDialog : Window
{
    private MultipleDisposable? _interactionDisposables;

    public SettingsDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        Opened += OnOpened;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        if (DataContext is not SettingsDialogViewModel viewModel)
            return;

        _interactionDisposables = new MultipleDisposable();

        // Register close dialog interaction handler
        viewModel.CloseDialog.RegisterHandler(interaction =>
        {
            Close();
            interaction.SetOutput(RxVoid.Default);
        }).DisposeWith(_interactionDisposables);

        // Register folder picker interaction for Keys & Fix Files tab
        viewModel.KeysFixFilesTab.ShowFolderPicker.RegisterHandler(async interaction =>
        {
            IReadOnlyList<IStorageFolder> folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Folder",
                AllowMultiple = false
            });

            string? path = folder.Count > 0 ? folder[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);

        // Register file picker interaction for Keys & Fix Files tab
        viewModel.KeysFixFilesTab.ShowFilePicker.RegisterHandler(async interaction =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select File",
                AllowMultiple = false,
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

        // Register keys archive picker interaction for Keys & Fix Files tab
        viewModel.KeysFixFilesTab.ShowKeysArchivePicker.RegisterHandler(async interaction =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Keys Archive",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Archives")
                    {
                        Patterns = new[] { "*.zip", "*.7z", "*.rar" }
                    },
                    FilePickerFileTypes.All
                }
            });

            string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SettingsDialogViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
        Opened -= OnOpened;

        if (DataContext is SettingsDialogViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}