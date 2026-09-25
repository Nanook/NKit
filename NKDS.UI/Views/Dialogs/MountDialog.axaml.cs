using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views.Dialogs;

public partial class MountDialog : Window
{
    private MultipleDisposable? _interactionDisposables;

    public MountDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        // Toggle history popup when the dropdown button is clicked
        MountHistoryButton.Click += (_, _) =>
        {
            MountHistoryPopup.IsOpen = !MountHistoryPopup.IsOpen;
        };

        // When a history item is selected, fill the TextBox and close the popup
        MountHistoryList.SelectionChanged += (_, _) =>
        {
            if (MountHistoryList.SelectedItem is string selectedPath && DataContext is MountDialogViewModel vm)
            {
                vm.MountPoint = selectedPath;
                MountPointTextBox.Text = selectedPath;
                MountHistoryPopup.IsOpen = false;
                MountHistoryList.SelectedItem = null;
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        if (DataContext is not MountDialogViewModel viewModel)
            return;

        _interactionDisposables = new MultipleDisposable();

        // Register folder picker interaction handler
        viewModel.ShowFolderPicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.MountPathHistory.Count > 0)
                {
                    string lastPath = viewModel.MountPathHistory[0];
                    if (Directory.Exists(lastPath))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(lastPath);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Mount Point Directory",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart
            });

            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);

        // Register close dialog interaction handler
        viewModel.CloseDialog.RegisterHandler(interaction =>
        {
            Close(interaction.Input);
            interaction.SetOutput(RxVoid.Default);
        }).DisposeWith(_interactionDisposables);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;
        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}