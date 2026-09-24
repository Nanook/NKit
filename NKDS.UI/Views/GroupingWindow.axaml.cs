using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views;

public partial class GroupingWindow : Window
{
    private MultipleDisposable? _interactionDisposables;

    /// <summary>
    /// Callback invoked when the window is closed, so MainWindowViewModel can be notified.
    /// Set by MainWindow.axaml.cs when it creates the GroupingWindow.
    /// </summary>
    public Action? WindowClosed { get; set; }

    public GroupingWindow()
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

        if (DataContext is not GroupingWindowViewModel viewModel)
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

        // Dispose the ViewModel to trigger cleanup (cancel computation, clear cache, etc.)
        (DataContext as GroupingWindowViewModel)?.Dispose();

        // Notify MainWindowViewModel that the window has been closed
        WindowClosed?.Invoke();

        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}