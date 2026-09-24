using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Primitives;

namespace NkdsUi.Views.Dialogs;

public partial class CreateSetDialog : ReactiveWindow<CreateSetDialogViewModel>
{
    public CreateSetDialog()
    {
        InitializeComponent();

        // Once the window has sized to content, lock the height so only horizontal resizing is allowed
        Opened += (_, _) =>
        {
            MinHeight = Height;
            MaxHeight = Height;
        };

        // Toggle history popup when the dropdown button is clicked
        FolderHistoryButton.Click += (_, _) =>
        {
            FolderHistoryPopup.IsOpen = !FolderHistoryPopup.IsOpen;
        };

        // When a history item is selected, fill the TextBox and close the popup
        FolderHistoryList.SelectionChanged += (_, _) =>
        {
            if (FolderHistoryList.SelectedItem is string selectedPath && ViewModel is { } vm)
            {
                vm.FolderPath = selectedPath;
                FolderHistoryPopup.IsOpen = false;
                FolderHistoryList.SelectedItem = null;
            }
        };

        this.WhenActivated(disposables =>
        {
            if (ViewModel is null)
                return;

            // Register folder picker interaction handler
            ViewModel.ShowFolderPicker.RegisterHandler(async interaction =>
            {
                IStorageFolder? suggestedStart = null;
                try
                {
                    string? currentPath = interaction.Input;
                    if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(currentPath);
                }
                catch { /* Ignore — open picker without suggested location */ }

                IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select DataStore Folder",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedStart
                });

                string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                interaction.SetOutput(path);
            }).DisposeWith(disposables);

            // When ConfirmCommand executes (returns true), close the dialog with result true
            ViewModel.ConfirmCommand
                .Subscribe(result => Close(result))
                .DisposeWith(disposables);

            // When CancelCommand executes (returns false), close the dialog with result false
            ViewModel.CancelCommand
                .Subscribe(result => Close(result))
                .DisposeWith(disposables);
        });
    }
}