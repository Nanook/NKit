using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views;

public partial class ThresholdGroupsDialog : Window
{
    private MultipleDisposable? _interactionDisposables;

    public ThresholdGroupsDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Dispose previous registrations if DataContext changes
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        if (DataContext is not ThresholdGroupsDialogViewModel viewModel)
            return;

        _interactionDisposables = new MultipleDisposable();

        viewModel.ShowSaveFileDialog.RegisterHandler(async interaction =>
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save YAML File",
                DefaultExtension = "yaml",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("YAML Files")
                    {
                        Patterns = new[] { "*.yaml" }
                    }
                }
            });

            string? path = file?.TryGetLocalPath();
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);

        viewModel.ShowOpenFileDialog.RegisterHandler(async interaction =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open YAML File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("YAML Files")
                    {
                        Patterns = new[] { "*.yaml" }
                    }
                }
            });

            string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
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