using NKDS.DatVerification;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace NkdsUi.Views;

public partial class DatVerificationWindow : Window
{
    private MultipleDisposable? _interactionDisposables;
    private bool _pointerPressedOnHeader;
    private Point _pointerPressPosition;

    private static readonly string[] ColumnNames = ["Status", "Dat Entry Name", "Image Name", "CRC32"];
    private static readonly string[] ColumnKeys = ["Status", "DatEntryName", "ImageName", "Crc32"];

    /// <summary>
    /// Callback invoked when the window is closed, so MainWindowViewModel can be notified.
    /// Set by MainWindow.axaml.cs when it creates the DatVerificationWindow.
    /// </summary>
    public Action? WindowClosed { get; set; }

    public DatVerificationWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        // History popup toggle
        DatHistoryButton.Click += (_, _) =>
        {
            DatHistoryPopup.IsOpen = !DatHistoryPopup.IsOpen;
        };

        // History selection → fill path and close popup
        DatHistoryList.SelectionChanged += (_, _) =>
        {
            if (DatHistoryList.SelectedItem is string selectedPath && DataContext is DatVerificationWindowViewModel vm)
            {
                vm.DatPathText = selectedPath;
                DatHistoryPopup.IsOpen = false;
                DatHistoryList.SelectedItem = null;
            }
        };

        // DataGrid sorting via pointer events on headers (same pattern as Add1GmrDialog)
        ResultsDataGrid.AddHandler(InputElement.PointerPressedEvent, OnDataGridPointerPressed, RoutingStrategies.Tunnel);
        ResultsDataGrid.AddHandler(InputElement.PointerReleasedEvent, OnDataGridPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Dispose previous registrations if DataContext changes
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        if (DataContext is not DatVerificationWindowViewModel viewModel)
            return;

        _interactionDisposables = new MultipleDisposable();

        viewModel.ShowFilePicker.RegisterHandler(async interaction =>
        {
            IStorageFolder? suggestedStart = null;
            try
            {
                if (viewModel.DatPathHistory.Count > 0)
                {
                    string lastPath = viewModel.DatPathHistory[0];
                    string? dir = Path.GetDirectoryName(lastPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        suggestedStart = await StorageProvider.TryGetFolderFromPathAsync(dir);
                }
            }
            catch { /* Ignore — open picker without suggested location */ }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Dat File",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Dat Files")
                    {
                        Patterns = new[] { "*.dat" }
                    }
                }
            });

            string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            interaction.SetOutput(path);
        }).DisposeWith(_interactionDisposables);
    }

    /// <summary>
    /// Handles Enter key press in the dat path text box to trigger LoadCommand.
    /// </summary>
    private void DatPathTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DatVerificationWindowViewModel viewModel)
        {
            viewModel.LoadCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void OnDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerPressedOnHeader = false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Control? source = e.Source as Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        // Don't sort if clicking the resize gripper
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

        if (DataContext is not DatVerificationWindowViewModel vm)
            return;

        // Ignore if pointer moved too far (was a drag, not a click)
        Point releasePosition = e.GetPosition(this);
        Point delta = _pointerPressPosition - releasePosition;
        if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
            return;

        Control? source = e.Source as Control;
        DataGridColumnHeader? columnHeader = source?.FindAncestorOfType<DataGridColumnHeader>();
        if (columnHeader == null)
            return;

        // Find which column was clicked by matching header text (strip arrows)
        int columnIndex = -1;
        for (int i = 0; i < ResultsDataGrid.Columns.Count; i++)
        {
            string? headerText = ResultsDataGrid.Columns[i].Header?.ToString()?.TrimEnd(' ', '▲', '▼').Trim();
            string? clickedText = columnHeader.Content?.ToString()?.TrimEnd(' ', '▲', '▼').Trim();
            if (headerText == clickedText)
            {
                columnIndex = i;
                break;
            }
        }
        if (columnIndex < 0 || columnIndex >= ColumnKeys.Length)
            return;

        string columnKey = ColumnKeys[columnIndex];

        // Delegate sorting to the ViewModel
        vm.SortByColumn(columnKey);

        // Update column headers with sort direction arrows
        bool ascending = vm.SortDirection == SortDirection.Ascending;
        for (int i = 0; i < ResultsDataGrid.Columns.Count && i < ColumnNames.Length; i++)
        {
            if (ColumnKeys[i] == columnKey)
                ResultsDataGrid.Columns[i].Header = ColumnNames[i] + (ascending ? " ▲" : " ▼");
            else
                ResultsDataGrid.Columns[i].Header = ColumnNames[i];
        }
    }

    /// <summary>
    /// Applies CSS classes to DataGrid rows based on the DatVerificationStatus of each result,
    /// enabling colour-coded row backgrounds via styles defined in the AXAML.
    /// </summary>
    private void ResultsDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        // Remove all status classes first
        e.Row.Classes.Remove("status-correct");
        e.Row.Classes.Remove("status-missing");
        e.Row.Classes.Remove("status-badlynamed");
        e.Row.Classes.Remove("status-wrongcrc");

        if (e.Row.DataContext is DatResultModel result)
        {
            switch (result.Status)
            {
                case DatVerificationStatus.Correct:
                    e.Row.Classes.Add("status-correct");
                    break;
                case DatVerificationStatus.Missing:
                    e.Row.Classes.Add("status-missing");
                    break;
                case DatVerificationStatus.BadlyNamed:
                    e.Row.Classes.Add("status-badlynamed");
                    break;
                case DatVerificationStatus.WrongCrc:
                    e.Row.Classes.Add("status-wrongcrc");
                    break;
                    // Unmatched: no class added, uses default background
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _interactionDisposables?.Dispose();
        _interactionDisposables = null;

        // Dispose the ViewModel to trigger cleanup
        (DataContext as DatVerificationWindowViewModel)?.Dispose();

        // Notify MainWindowViewModel that the window has been closed
        WindowClosed?.Invoke();

        DataContextChanged -= OnDataContextChanged;
        Closed -= OnClosed;
    }
}