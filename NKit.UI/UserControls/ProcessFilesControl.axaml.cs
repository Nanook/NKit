using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
// tracing removed
using NKit.Ui.Helpers;
using NKit.Ui.Models;
using NKit.Ui.Services;
using NKit.Ui.ViewModels;
using ReactiveUI.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace NKit.Ui.UserControls
{
    // Tracing removed from this control. No-op logging previously present has been deleted.
    public partial class ProcessFilesControl : UserControl
    {
        private Dictionary<string, bool> _columnSortDirections = new Dictionary<string, bool>();
        private DockPanel _dropState;
        private DataGrid _processFilesDataGrid;
        private TextBox _tbConsoleOutput;
        private ScrollViewer _consoleOutputScrollViewer;
        private bool _isDataGridRowClickRunning = false;
        private Flyout _currentFlyout;
        // Current flyout button references for live updates
        private Button _flyoutRemoveBtn;
        private Button _flyoutRemoveSelectedBtn;
        private Button _flyoutProcessBtn;

        private double _gridRowDetailsColumnWidthLeft = 0;

        private Expander _exConsole;
        private Expander _exSettings;
        // Legacy flags retained as no-ops after collapsing/expanding logic was removed
#pragma warning disable CS0414 // Field is assigned but its value is never used
        private bool _suppressRowDetailsDuringMultiSelect = false;
        private bool _suppressRowDetailsDuringRightClick = false;
        // Guards previously used for collapse/toggle sequencing - retained but unused
        private bool _isCollapsingRowDetails = false;
        private bool _suppressSelectionChangedDuringToggle = false;
        private bool _isEnforcingSingleDetails = false;
#pragma warning restore CS0414
        private int _toggleSequence = 0;
        // no longer using requested toggle; keep state driven directly from selection click
        //private Grid _gridProcessFilesConsole;

        //private const double _consoleCollapsedWidth = 55;
        //private double _consoleExpandedWidth = 0;

        public ProcessFilesControl()
        {
            DataContext = new ProcessFilesViewModel();

            initializeComponent();

            // Details visibility logic removed - handled exclusively by DataGrid default behavior.

            AddHandler(DragDrop.DropEvent, drop);
            AddHandler(DragDrop.DragOverEvent, dragOver);
            // Key handlers retained for delete + other global keys but no longer affect details
            AddHandler(KeyDownEvent, onGlobalKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, onGlobalKeyUp, RoutingStrategies.Tunnel);

            // Subscribe to task changes to expand settings panel
            MainWindowViewModel.SystemOrTaskUpdatedEvent += onSystemOrTaskUpdated;

            // Initialize UI state based on current settings (no timer needed)
            initializeUiState();

        }

        /// <summary>
        /// Centralized selection handler for rows. All pointer and keyboard paths should call this
        /// to ensure consistent selection behavior.
        /// </summary>
        private void handleRowSelection(SourceFileRecord record, DataGridRow row, KeyModifiers modifiers, bool isRightClick)
        {
            try
            {
                if (record == null)
                    return;

                List<SourceFileRecord> selected = _processFilesDataGrid?.SelectedItems?.OfType<SourceFileRecord>()?.ToList() ?? new List<SourceFileRecord>();
                // tracing removed

                // If right-click, ensure the clicked item is selected (common UX expectation)
                if (isRightClick)
                {
                    if (!selected.Contains(record))
                    {
                        // tracing removed
                        try { _processFilesDataGrid.SelectedItems?.Clear(); } catch { }
                        try { _processFilesDataGrid.SelectedItems?.Add(record); } catch { }
                        // tracing removed
                    }

                    return; // do not alter expansion state here
                }

                // If multi-select modifier is active either globally (key down) or on this pointer event,
                // collapse all details and do not expand items.
                if (_suppressRowDetailsDuringMultiSelect || modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift))
                {
                    collapseExpandedItems(null);
                    return;
                }

                // Single-click selects the item and toggles its details visibility.
                // tracing removed
                try
                {
                    // Use a sequence to avoid races with selection/other posted actions.
                    int mySeq = System.Threading.Interlocked.Increment(ref _toggleSequence);
                    // Suppress selection-changed while we perform toggle to avoid re-entrant updates
                    _suppressSelectionChangedDuringToggle = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        // If multi-select modifier became active after the click but before this posted action
                        // run, abort any expand actions to honor the modifier requirement.
                        if (_suppressRowDetailsDuringMultiSelect)
                        {
                            _suppressSelectionChangedDuringToggle = false;
                            return;
                        }
                        try
                        {
                            if (mySeq != _toggleSequence) return; // stale

                            ProcessFilesViewModel vm = DataContext as ProcessFilesViewModel;

                            if (record.IsDetailsVisible)
                            {
                                // collapse
                                try { _isCollapsingRowDetails = true; } catch { }
                                record.IsDetailsVisible = false;
                                record.DisplayDetailsIcon = "ChevronRight";
                                try { updateRowDetailsVisibility(); } catch { }
                                try { if (row != null) row.AreDetailsVisible = false; } catch { }
                                // tracing removed
                                try { _isCollapsingRowDetails = false; } catch { }
                            }
                            else
                            {
                                // expand exclusively
                                if (vm != null)
                                {
                                    // Snapshot the queue before iterating: NKitService.Run mutates
                                    // FileQueue on a background thread (item replacement during
                                    // processing), which would otherwise throw "Collection was
                                    // modified" mid-enumeration.
                                    foreach (SourceFileRecord f in vm.FileQueue.ToList())
                                    {
                                        if (!object.ReferenceEquals(f, record))
                                            f.IsDetailsVisible = false;
                                    }
                                }

                                record.IsDetailsVisible = true;
                                record.DisplayDetailsIcon = "ChevronDown";
                                try { updateRowDetailsVisibility(); } catch { }
                                try { if (row != null) { row.IsSelected = true; row.AreDetailsVisible = true; } } catch { }
                                // tracing removed
                            }

                            // Do not force item materialization/selection here to avoid racing with DataGrid.
                            // tracing removed
                        }
                        catch { }
                        finally
                        {
                            _suppressSelectionChangedDuringToggle = false;
                        }
                    }, DispatcherPriority.Input);
                }
                catch { }
            }
            catch { }
        }

        private void onGlobalKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
                removeSelectedFiles(e);

            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                // Mark suppression so selection-changed doesn't fight our collapse
                _suppressRowDetailsDuringMultiSelect = true;
                // Collapse all expanded details immediately
                try { collapseExpandedItems(null); } catch { }
            }
        }

        private void onGlobalKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _suppressRowDetailsDuringMultiSelect = false;
                // Refresh visuals according to current selection state
                try { updateRowDetailsVisibility(); } catch { }
            }
        }

        private void removeSelectedFiles(KeyEventArgs e)
        {
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;
            List<SourceFileRecord> selectedFiles = getSelectedFiles().Where(a => a.ProcessingStatus != ProcessingStatus.Processing).ToList();
            if (selectedFiles.Count == 0)
                return;

            foreach (SourceFileRecord file in selectedFiles)
                dataContext.RemoveFile(file);

            e.Handled = true;
        }

        private IReadOnlyList<SourceFileRecord> getSelectedFiles(SourceFileRecord fallback = null)
        {
            List<SourceFileRecord> selectedFiles = _processFilesDataGrid.SelectedItems?
                .OfType<SourceFileRecord>()
                .Distinct()
                .ToList() ?? new List<SourceFileRecord>();

            if (selectedFiles.Count == 0 && fallback != null)
                selectedFiles.Add(fallback);
            else if (fallback != null && !selectedFiles.Contains(fallback))
                selectedFiles = new List<SourceFileRecord> { fallback };

            return selectedFiles;
        }

        private void initializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _dropState = this.Find<DockPanel>("DropState");
            _processFilesDataGrid = this.Find<DataGrid>("ProcessFilesDataGrid");
            // Use model-bound AreDetailsVisible; keep RowDetailsVisibilityMode visible-when-selected so
            // details can show when the model requests it.
            _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
            _processFilesDataGrid.SelectionChanged += onDataGridSelectionChanged;

            _consoleOutputScrollViewer = this.Find<ScrollViewer>("ConsoleOutputScrollViewer");

            ConsoleOutput.ConsoleTextChangedEvent += scrollToEndOfConsoleOutput;

            _exConsole = this.Find<Expander>("exConsole");
            _exSettings = this.Find<Expander>("exSettings");
            _tbConsoleOutput = this.Find<TextBox>("tbConsoleOutput");
            //_gridProcessFilesConsole = this.Find<Grid>("gridProcessFilesConsole");

            // Flyout buttons are initialized once when opened; no live subscription needed.
        }

        // Buttons are initialized when the flyout is created and do not change while open.

        private async void processSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            ProcessFilesViewModel vm = DataContext as ProcessFilesViewModel;
            Button button = sender as Button;
            SourceFileRecord file = button?.Tag as SourceFileRecord;

            if (vm == null)
                return;

            if (vm.IsBusy)
            {
                // When busy, the process button acts as Cancel
                vm.CancelTask();
            }
            else
            {
                List<SourceFileRecord> selected = getSelectedFiles(file).ToList();
                if (!selected.Any())
                    return;

                // Force process regardless of reprocess settings
                await vm.StartProcessingSubset(selected, true);
            }

            _currentFlyout?.Hide();
        }

        private void onExpanderTapped(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;

            double exConsoleWidth = _exConsole.Width;

            if (expander == _exConsole)
            {
                if (_exSettings.IsExpanded) { _exSettings.IsExpanded = false; }
            }

            if (expander == _exSettings)
            {
                if (_exConsole.IsExpanded) { _exConsole.IsExpanded = false; }

                // Let user control tab selection - don't force it
            }
        }

        private void scrollToEndOfConsoleOutput(object sender, EventArgs e) => Dispatcher.UIThread.Post(() => _consoleOutputScrollViewer.ScrollToEnd());

        private void dragOver(object sender, DragEventArgs e)
        {
            // Only allow Copy or Link as Drop Operations.
            e.DragEffects &= DragDropEffects.Copy;

            IReadOnlyList<IDataTransferItem> items = e.DataTransfer?.Items;
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
                    IStorageItem v = it.TryGetFile();
                    return v is IStorageFile || v is IStorageFolder;
                }
                catch
                {
                    return false;
                }
            });

            if (!hasFileOrFolder)
                e.DragEffects = DragDropEffects.None;
        }

        private async void drop(object sender, DragEventArgs e)
        {
            IReadOnlyList<IDataTransferItem> items = e.DataTransfer?.Items;
            if (items == null || !items.Any())
                return;

            // Extract paths on background thread to avoid UI work
            string[] paths = await System.Threading.Tasks.Task.Run(() =>
            {
                List<string> list = new List<string>();
                foreach (IDataTransferItem it in items)
                {
                    try
                    {
                        IStorageItem val = it.TryGetFile();
                        if (val is IStorageFile sf)
                        {
                            string p = sf.Path?.LocalPath;
                            if (!string.IsNullOrEmpty(p))
                                list.Add(p);
                        }
                        else if (val is IStorageFolder folder)
                        {
                            string p = folder.Path?.LocalPath;
                            if (!string.IsNullOrEmpty(p))
                                list.Add(p);
                        }
                    }
                    catch { }
                }

                return list.Distinct().ToArray();
            }).ConfigureAwait(false);

            if (paths == null || paths.Length == 0)
                return;

            // Switch back to UI thread for UI work
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Cursor = new Cursor(StandardCursorType.Wait);
                ((ProcessFilesViewModel)DataContext).AddFiles(paths);
                Cursor = Cursor.Default;
            });
        }

        public async void OnSelectFilesClicked(object sender, RoutedEventArgs args)
        {
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;

            IStorageProvider storageProvider = getWindow().StorageProvider;

            IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select file(s)",
                AllowMultiple = true,
                FileTypeFilter = FileExtensionsHelper.GetFileDialogFilters(),
                SuggestedStartLocation = !string.IsNullOrEmpty(dataContext.SettingsStore.LastFolderBrowsed)
                    ? await storageProvider.TryGetFolderFromPathAsync(dataContext.SettingsStore.LastFolderBrowsed)
                    : null
            });

            if (files?.FirstOrDefault() != null)
            {
                dataContext.SettingsStore.LastFolderBrowsed = Path.GetDirectoryName(files.First().Path.LocalPath);
            }
            else
            {
                return;
            }

            Cursor = new Cursor(StandardCursorType.Wait);
            ((ProcessFilesViewModel)DataContext).AddFiles(files.Select(f => f.Path.LocalPath).ToArray());
            Cursor = Cursor.Default;
        }

        public async void OnSelectFolderClicked(object sender, RoutedEventArgs args)
        {
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;

            IStorageProvider storageProvider = getWindow().StorageProvider;

            IReadOnlyList<IStorageFolder> folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder",
                AllowMultiple = false,
                SuggestedStartLocation = !string.IsNullOrEmpty(dataContext.SettingsStore.LastFolderBrowsed)
                    ? await storageProvider.TryGetFolderFromPathAsync(dataContext.SettingsStore.LastFolderBrowsed)
                    : null
            });

            Cursor = new Cursor(StandardCursorType.Wait);

            if (folder?.FirstOrDefault() != null)
            {
                string folderPath = folder.First().Path.LocalPath;
                dataContext.SettingsStore.LastFolderBrowsed = folderPath;
                ((ProcessFilesViewModel)DataContext).AddFiles(new string[] { folderPath });
            }

            Cursor = Cursor.Default;
        }

        public void OnCellPointerPressed(object sender, DataGridCellPointerPressedEventArgs e)
        {
            // Ignore clicks while a previous row-click handler is still processing to avoid
            // queued Dispatcher posts racing and re-opening rows.
            if (_isDataGridRowClickRunning)
            {
                return;
            }
            _isDataGridRowClickRunning = true;
            KeyModifiers modifiers = e.PointerPressedEventArgs.KeyModifiers;
            bool isExtendedSelection = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift);
            // If multi-select mode is active, just show flyout on right-click; no expand/collapse logic
            bool isRightClick = e.PointerPressedEventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed;
            if (isExtendedSelection)
            {
                // Delegate multi-select/right-click selection behavior to central handler
                handleRowSelection(e.Row.DataContext as SourceFileRecord, e.Row, modifiers, isRightClick);
                _isDataGridRowClickRunning = false;
                return;
            }

            if (e.PointerPressedEventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !isExtendedSelection)
            {
                // Defer toggle until after selection processing so we can check final SelectedItems
                DataGridRow row = e.Row;
                SourceFileRecord src = row.DataContext as SourceFileRecord;

                try
                {
                    handleRowSelection(src, row, modifiers, false);
                }
                catch { }

                _isDataGridRowClickRunning = false;
            }

            if (e.PointerPressedEventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                // Ensure selection reflects right-click target as needed, then show flyout
                try { handleRowSelection(e.Row.DataContext as SourceFileRecord, e.Row, modifiers, true); } catch { }
                Flyout flyout = createFlyout(e.Row.DataContext as SourceFileRecord);
                flyout.ShowAt(e.Row, true);
                _currentFlyout = flyout;
                refreshOpenFlyout();

                // Mark click processing finished for this pointer event
                _isDataGridRowClickRunning = false;
            }
        }

        private Flyout createFlyout(SourceFileRecord file)
        {
            // Simple, deterministic flyout: compute enablement once and set styles.
            ProcessFilesViewModel vm = DataContext as ProcessFilesViewModel;
            bool isBusy = vm?.IsBusy ?? false;
            IReadOnlyList<SourceFileRecord> selected = getSelectedFiles(file);

            bool canRemoveSingle = !isBusy && file?.ProcessingStatus != ProcessingStatus.Processing;
            bool canRemoveSelected = !isBusy && selected.Any(s => s.ProcessingStatus != ProcessingStatus.Processing);
            bool canProcessSelected = !isBusy && selected.Any();

            Button removeBtn = new Button
            {
                Content = "Remove File",
                Tag = file,
                IsEnabled = canRemoveSingle,
                Background = canRemoveSingle ? Brushes.DarkRed : new SolidColorBrush(Color.Parse("#555555")),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };

            Button removeSelectedBtn = new Button
            {
                Content = "Remove Selected",
                Tag = file,
                IsEnabled = canRemoveSelected,
                Background = canRemoveSelected ? Brushes.DarkRed : new SolidColorBrush(Color.Parse("#555555")),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // If system is busy, show Cancel (red) and allow cancelling; otherwise show Process (green).
            Button processBtn = new Button
            {
                Tag = file,
                Foreground = Brushes.White
            };

            if (isBusy)
            {
                processBtn.Content = "Cancel";
                processBtn.IsEnabled = true;
                processBtn.Background = Brushes.DarkRed;
            }
            else
            {
                processBtn.Content = "Process Selected";
                processBtn.IsEnabled = canProcessSelected;
                processBtn.Background = canProcessSelected ? Brushes.Green : new SolidColorBrush(Color.Parse("#555555"));
            }

            // Store references for live updates
            _flyoutRemoveBtn = removeBtn;
            _flyoutRemoveSelectedBtn = removeSelectedBtn;
            _flyoutProcessBtn = processBtn;

            removeBtn.Click += (s, e) =>
            {
                _currentFlyout?.Hide();
                if (!canRemoveSingle) return;
                vm?.RemoveFile(file);
            };

            removeSelectedBtn.Click += (s, e) =>
            {
                _currentFlyout?.Hide();
                if (!canRemoveSelected) return;
                List<SourceFileRecord> toRemove = selected.Where(x => x.ProcessingStatus != ProcessingStatus.Processing).ToList();
                foreach (SourceFileRecord f in toRemove)
                    vm?.RemoveFile(f);
            };

            processBtn.Click += async (s, e) =>
            {
                _currentFlyout?.Hide();
                // Re-evaluate busy state at click time
                ProcessFilesViewModel vmLocal = DataContext as ProcessFilesViewModel;
                if (vmLocal?.IsBusy ?? false)
                {
                    vmLocal.CancelTask();
                    return;
                }

                if (!selected.Any())
                    return;

                await vmLocal.StartProcessingSubset(selected.ToList(), true);
            };

            StackPanel stack = new StackPanel
            {
                Background = new SolidColorBrush(Color.Parse("#252525"))
            };

            stack.Children.Add(removeBtn);
            stack.Children.Add(removeSelectedBtn);
            stack.Children.Add(processBtn);

            return new Flyout { Content = stack, ShowMode = FlyoutShowMode.Standard };
        }

        // Update open flyout buttons when selection or busy state changes
        private void refreshOpenFlyout()
        {
            try
            {
                if (_currentFlyout == null) return;
                ProcessFilesViewModel vm = DataContext as ProcessFilesViewModel;
                SourceFileRecord file = _flyoutProcessBtn?.Tag as SourceFileRecord;
                IReadOnlyList<SourceFileRecord> selected = getSelectedFiles(file);

                bool isBusy = vm?.IsBusy ?? false;

                if (_flyoutRemoveBtn != null)
                {
                    bool canRemoveSingle = !isBusy && file?.ProcessingStatus != ProcessingStatus.Processing;
                    _flyoutRemoveBtn.IsEnabled = canRemoveSingle;
                    _flyoutRemoveBtn.Background = canRemoveSingle ? Brushes.DarkRed : new SolidColorBrush(Color.Parse("#555555"));
                }

                if (_flyoutRemoveSelectedBtn != null)
                {
                    bool canRemoveSelected = !isBusy && selected.Any(s => s.ProcessingStatus != ProcessingStatus.Processing);
                    _flyoutRemoveSelectedBtn.IsEnabled = canRemoveSelected;
                    _flyoutRemoveSelectedBtn.Background = canRemoveSelected ? Brushes.DarkRed : new SolidColorBrush(Color.Parse("#555555"));
                }

                if (_flyoutProcessBtn != null)
                {
                    bool canProcess = !isBusy && selected.Any();
                    if (isBusy)
                    {
                        _flyoutProcessBtn.IsEnabled = true;
                        _flyoutProcessBtn.Background = Brushes.DarkRed;
                        _flyoutProcessBtn.Content = "Cancel";
                    }
                    else
                    {
                        _flyoutProcessBtn.IsEnabled = canProcess;
                        _flyoutProcessBtn.Background = canProcess ? Brushes.Green : new SolidColorBrush(Color.Parse("#555555"));
                        _flyoutProcessBtn.Content = "Process Selected";
                    }
                }
            }
            catch { }
        }

        private void removeFile(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            SourceFileRecord fileToRemove = button.Tag as SourceFileRecord;
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;

            // Hide the flyout immediately when the action is selected
            _currentFlyout?.Hide();
            _currentFlyout = null;

            // If processing is active or the file is processing, do nothing
            try
            {
                if (fileToRemove != null && fileToRemove.ProcessingStatus == ProcessingStatus.Processing)
                    return;
            }
            catch { }

            dataContext.RemoveFile(fileToRemove);
        }

        private void tabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;

            if (e?.AddedItems.Count > 0 && e?.RemovedItems.Count > 0)
            {
                TabItem gotFocus = e.AddedItems[0] as TabItem;

                if (gotFocus?.Name == "ConsoleOutput")
                {
                    dataContext.ConsoleOutput.HasFocus = true;
                    dataContext.ConsoleOutput.HasUnviewed = false;
                }
                else
                {
                    dataContext.ConsoleOutput.HasFocus = false;
                }
            }

            GC.Collect();
        }

        private void dataGrid_OnSortChanged(object sender, DataGridColumnEventArgs e)
        {
            //// TODO - may not be very robust / 
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;

            string columnHeader = e.Column.Header.ToString();

            if (!_columnSortDirections.ContainsKey(columnHeader))
            {
                _columnSortDirections.Add(columnHeader, true);
            }
            else
            {
                _columnSortDirections[columnHeader] = !_columnSortDirections[columnHeader];
            }

            IOrderedEnumerable<SourceFileRecord> sorted = columnHeader switch
            {
                "File Type" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.IsImageOrArchiveType) :
                                dataContext.FileQueue.OrderByDescending(x => x.IsImageOrArchiveType),
                "Status" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.CurrentTask) :
                                dataContext.FileQueue.OrderByDescending(x => x.CurrentTask),
                "Name" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.Name) :
                                dataContext.FileQueue.OrderByDescending(x => x.Name),
                "Format" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.GetImageOrArchiveType) :
                                dataContext.FileQueue.OrderByDescending(x => x.GetImageOrArchiveType),
                "Size" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.GetFileSize) :
                                dataContext.FileQueue.OrderByDescending(x => x.GetFileSize),
                "Progress" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.Progress) :
                                dataContext.FileQueue.OrderByDescending(x => x.Progress),
                "Verify Status" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.VerifyResultMessage) :
                                dataContext.FileQueue.OrderByDescending(x => x.VerifyResultMessage),
                "Output File" => _columnSortDirections[columnHeader] ?
                                dataContext.FileQueue.OrderBy(x => x.OutFileName) :
                                dataContext.FileQueue.OrderByDescending(x => x.OutFileName),
                _ => dataContext.FileQueue.OrderBy(x => x)
            };

            dataContext.FileQueueFiltered = new ObservableCollection<SourceFileRecord>(sorted);
        }

        TopLevel getWindow() => TopLevel.GetTopLevel(this);

        private void onGridRowDetailsDragCompleted(object sender, VectorEventArgs e)
        {
            Grid parentGrid = (sender as GridSplitter)?.Parent as Grid;
            ColumnDefinitions columnDefinitions = parentGrid?.ColumnDefinitions;

            _gridRowDetailsColumnWidthLeft = columnDefinitions[0].ActualWidth;
        }

        private void onRowDetailsVisibilityChanged(object sender, DataGridRowDetailsEventArgs e)
        {
            Grid grid = e.DetailsElement.GetLogicalChildren().FirstOrDefault(x => typeof(Grid) == x.GetType()) as Grid;

            if (grid != null)
            {
                grid.ColumnDefinitions.FirstOrDefault().Width = new GridLength(_gridRowDetailsColumnWidthLeft);
            }
        }

        public void OnLoadingRow(object sender, DataGridRowEventArgs e) => setRowDetailsVisibility(e.Row);

        public void OnUnloadingRow(object sender, DataGridRowEventArgs e) => setRowDetailsVisibility(e.Row);

        public void OnLoadingRowDetails(object sender, DataGridRowDetailsEventArgs e) => setRowDetailsVisibility(e.Row);

        public void OnUnloadingRowDetails(object sender, DataGridRowDetailsEventArgs e) => setRowDetailsVisibility(e.Row);

        private void setRowDetailsVisibility(DataGridRow row)
        {
            // No custom behavior - rely on DataGrid defaults
        }

        private void collapseExpandedItems(SourceFileRecord exclude = null)
        {
            try
            {
                ProcessFilesViewModel vm = DataContext as ProcessFilesViewModel;
                if (vm == null)
                    return;

                // Snapshot: FileQueue can be mutated by the background processing thread while this
                // UI-thread loop runs (item replacement during processing), which would throw
                // "Collection was modified; enumeration operation may not execute".
                foreach (SourceFileRecord f in vm.FileQueue.ToList())
                {
                    try
                    {
                        if (exclude != null && object.ReferenceEquals(f, exclude))
                            continue;

                        f.IsDetailsVisible = false;
                        f.DisplayDetailsIcon = "ChevronRight";
                    }
                    catch { }
                }

                try { updateRowDetailsVisibility(); } catch { }
            }
            catch { }
        }

        private void onDataGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // When multiple items are selected via Ctrl/Shift, collapse any expanded row details
            try
            {
                if (_suppressSelectionChangedDuringToggle)
                {
                    return;
                }
                int selectedCount = _processFilesDataGrid?.SelectedItems?.Count ?? 0;

                // Dump state of all items for debugging
                try
                {
                    ProcessFilesViewModel vmDbg = DataContext as ProcessFilesViewModel;
                    if (vmDbg != null)
                    {
                        // tracing removed
                        // Snapshot to avoid "Collection was modified" if the background processing
                        // thread replaces items in FileQueue during this enumeration.
                        foreach (SourceFileRecord f in vmDbg.FileQueue.ToList())
                        {
                            try
                            {
                                bool isSelected = _processFilesDataGrid?.SelectedItems?.OfType<SourceFileRecord>()?.Contains(f) ?? false;
                                // tracing removed
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                // Update row details visibility according to current selection state
                updateRowDetailsVisibility();
                // Refresh any open flyout so buttons reflect new selection
                refreshOpenFlyout();
            }
            catch { }
        }

        private void updateRowDetailsVisibility()
        {
            try
            {
                if (_processFilesDataGrid == null)
                    return;

                int selectedCount = _processFilesDataGrid.SelectedItems?.Count ?? 0;

                // If multi-select active (either suppression flag or multiple selected), collapse all
                // Rely on DataGrid defaults for row details visibility
                if (selectedCount > 1)
                {
                    _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
                    return;
                }

                // If multi-select modifier is active globally, ensure details are collapsed regardless of selection
                if (_suppressRowDetailsDuringMultiSelect)
                {
                    List<DataGridRow> rowsSupp = _processFilesDataGrid.GetLogicalChildren().OfType<DataGridRow>().ToList();
                    foreach (DataGridRow r in rowsSupp)
                    {
                        try { if (r.DataContext is SourceFileRecord src) src.IsDetailsVisible = false; } catch { }
                        try { r.AreDetailsVisible = false; } catch { }
                    }

                    _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
                    return;
                }

                // Single selection - show details only for the single selected row
                if (selectedCount == 1)
                {
                    // Ensure RowDetailsVisibilityMode allows visible-when-selected so the selected row can show details
                    _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;

                    // Determine which SourceFileRecord is selected
                    SourceFileRecord selectedRecord = _processFilesDataGrid.SelectedItems?.OfType<SourceFileRecord>().FirstOrDefault();

                    List<DataGridRow> rows = _processFilesDataGrid.GetLogicalChildren().OfType<DataGridRow>().ToList();
                    foreach (DataGridRow r in rows)
                    {
                        SourceFileRecord rec = r.DataContext as SourceFileRecord;
                        if (rec == null)
                        {
                            r.AreDetailsVisible = false;
                            continue;
                        }

                        if (rec == selectedRecord)
                        {
                            // Respect the model's IsDetailsVisible value for the selected record so
                            // explicit toggles (collapse) are preserved. Do not force it open here.
                            r.AreDetailsVisible = rec.IsDetailsVisible;
                        }
                        else
                        {
                            rec.IsDetailsVisible = false;
                            r.AreDetailsVisible = false;
                        }
                    }

                    // Ensure RowDetailsVisibilityMode allows showing details for our single row
                    _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.VisibleWhenSelected;
                }
                else
                {
                    // no selection
                    List<DataGridRow> rows = _processFilesDataGrid.GetLogicalChildren().OfType<DataGridRow>().ToList();
                    foreach (DataGridRow r in rows)
                    {
                        if (r.DataContext is SourceFileRecord src)
                            src.IsDetailsVisible = false;
                        r.AreDetailsVisible = false;
                    }
                    _processFilesDataGrid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
                }
            }
            catch { }
        }

        /// <summary>
        /// Handles system or task changes to expand settings panel when task is selected
        /// </summary>
        private void onSystemOrTaskUpdated(object sender, SystemOrTaskChangedEventArgs e)
        {
            // Expand settings panel when a task is changed to show user the available options
            // But don't force tab switching - let user control that
            if (e.IsTaskChange && _exSettings != null)
            {
                // Close console if it's open to make room for settings
                if (_exConsole?.IsExpanded == true)
                {
                    _exConsole.IsExpanded = false;
                }

                // Expand settings panel
                _exSettings.IsExpanded = true;
            }
        }

        /// <summary>
        /// Initialize UI state based on current settings without forcing changes
        /// </summary>
        private void initializeUiState()
        {
            ProcessFilesViewModel dataContext = DataContext as ProcessFilesViewModel;
            if (dataContext?.Settings != null)
            {
                // Set initial panel state based on settings without forcing
                if (_exSettings != null && dataContext.Settings.Task == Nanook.NKit.TaskType.Convert)
                {
                    _exSettings.IsExpanded = true;
                }
            }
        }

        /// <summary>
        /// Forces the Convert Options tab to be selected
        /// </summary>
        private void forceConvertOptionsTab()
        {
            SettingsControl settingsControl = this.FindLogicalDescendantOfType<SettingsControl>();
            if (settingsControl?.DataContext is SettingsControlViewModel settingsViewModel)
            {
                if (settingsViewModel.IsConvertSelected)
                {
                    settingsViewModel.SelectedTabIndex = 3; // Convert tab
                }
            }
        }
    }
}