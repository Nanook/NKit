using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog. Manages association entries, state queries,
/// and the window decorations toggle. Uses OK/Cancel semantics — changes are only
/// persisted when the user clicks OK.
/// </summary>
public class SettingsDialogViewModel : ViewModelBase, IDisposable
{
    private readonly IFileAssociationService _fileAssociationService;
    private readonly IConfigService _configService;

    private WindowDecorationMode _selectedDecorationMode;
    private WindowDecorationMode _originalDecorationMode;
    private bool _showRestartNotice;
    private bool _disposed;

    public SettingsDialogViewModel(IFileAssociationService fileAssociationService, IConfigService configService)
    {
        _fileAssociationService = fileAssociationService;
        _configService = configService;

        // --- Keys & Fix Files Tab ---
        KeysFixFilesTab = new KeysFixFilesTabViewModel(configService);

        // --- File Associations ---
        IsFileAssociationsSupported = fileAssociationService.IsSupported;
        DirectoryContextMenuLabel = fileAssociationService.DirectoryContextMenuLabel;

        DirectoryEntries = new ObservableCollection<AssociationEntryViewModel>(
            GetEntriesForCategory(AssociationCategory.DirectoryContextMenu));
        FileEntries = new ObservableCollection<AssociationEntryViewModel>(
            GetEntriesForCategory(AssociationCategory.FileContextMenu));
        DoubleClickEntries = new ObservableCollection<AssociationEntryViewModel>(
            GetEntriesForCategory(AssociationCategory.DoubleClickHandler));

        // --- Window Decorations ---
        ShowWindowDecorations = OperatingSystem.IsLinux();
        _selectedDecorationMode = configService.GetWindowDecorationMode();
        _originalDecorationMode = _selectedDecorationMode;

        // --- Commands ---
        ToggleAssociationCommand = ReactiveCommand.CreateFromTask<AssociationEntryViewModel>(ToggleAssociationAsync);
        OkCommand = ReactiveCommand.CreateFromTask(OkAsync);
        SelectAllCommand = ReactiveCommand.Create(SelectAll);
        SelectNoneCommand = ReactiveCommand.Create(SelectNone);
        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
    }

    // --- Keys & Fix Files ---

    /// <summary>ViewModel for the Keys &amp; Fix Files settings tab.</summary>
    public KeysFixFilesTabViewModel KeysFixFilesTab { get; }

    // --- File Associations ---

    /// <summary>Whether the current platform supports file associations.</summary>
    public bool IsFileAssociationsSupported { get; }

    /// <summary>Platform-specific label for the directory context menu group.</summary>
    public string DirectoryContextMenuLabel { get; }

    /// <summary>Directory context menu association entries.</summary>
    public ObservableCollection<AssociationEntryViewModel> DirectoryEntries { get; }

    /// <summary>.nkds file context menu association entries.</summary>
    public ObservableCollection<AssociationEntryViewModel> FileEntries { get; }

    /// <summary>.nkds double-click handler association entries.</summary>
    public ObservableCollection<AssociationEntryViewModel> DoubleClickEntries { get; }

    // --- Window Decorations (Linux only) ---

    /// <summary>Whether to show the window decorations section (Linux only).</summary>
    public bool ShowWindowDecorations { get; }

    /// <summary>The currently selected window decoration mode.</summary>
    public WindowDecorationMode SelectedDecorationMode
    {
        get => _selectedDecorationMode;
        set
        {
            if (_selectedDecorationMode == value) return;
            this.RaiseAndSetIfChanged(ref _selectedDecorationMode, value);
            ShowRestartNotice = value != _originalDecorationMode;
            _configService.SetWindowDecorationMode(value);
        }
    }

    /// <summary>Whether to show the restart notice after a decoration mode change.</summary>
    public bool ShowRestartNotice
    {
        get => _showRestartNotice;
        private set => this.RaiseAndSetIfChanged(ref _showRestartNotice, value);
    }

    // --- Commands ---

    /// <summary>Toggles the registration state of an association entry.</summary>
    public ReactiveCommand<AssociationEntryViewModel, RxVoid> ToggleAssociationCommand { get; }

    /// <summary>Applies all changes and closes the dialog.</summary>
    public ReactiveCommand<RxVoid, RxVoid> OkCommand { get; }

    /// <summary>Checks all association entry checkboxes.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectAllCommand { get; }

    /// <summary>Unchecks all association entry checkboxes.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectNoneCommand { get; }

    /// <summary>Discards changes and closes the dialog.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>Interaction to close the dialog. The View registers a handler.</summary>
    public Interaction<RxVoid, RxVoid> CloseDialog { get; } = new();

    // --- Lifecycle ---

    /// <summary>
    /// Called after the dialog opens to query the current state of all association entries.
    /// Each entry is queried with a 5-second timeout.
    /// </summary>
    public async Task InitializeAsync()
    {
        IEnumerable<AssociationEntryViewModel> allEntries = DirectoryEntries.Concat(FileEntries).Concat(DoubleClickEntries);

        foreach (AssociationEntryViewModel? entryVm in allEntries)
        {
            entryVm.IsLoading = true;
            entryVm.ErrorMessage = null;

            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                AssociationQueryResult result = await _fileAssociationService.QueryStateAsync(entryVm.Entry, cts.Token);

                if (result.ErrorMessage != null)
                {
                    entryVm.ErrorMessage = result.ErrorMessage;
                }
                else
                {
                    entryVm.State = result.State;
                    // Set checkbox to reflect current OS state
                    entryVm.IsChecked = result.State == AssociationState.Registered
                                     || result.State == AssociationState.Stale;
                }
            }
            catch (OperationCanceledException)
            {
                entryVm.ErrorMessage = "Query timed out";
            }
            catch (Exception ex)
            {
                entryVm.ErrorMessage = ex.Message;
            }
            finally
            {
                entryVm.IsLoading = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ToggleAssociationCommand.Dispose();
        OkCommand.Dispose();
        SelectAllCommand.Dispose();
        SelectNoneCommand.Dispose();
        CancelCommand.Dispose();
    }

    // --- Private helpers ---

    private void SelectAll()
    {
        foreach (AssociationEntryViewModel? entry in DirectoryEntries.Concat(FileEntries).Concat(DoubleClickEntries))
        {
            if (entry.CanToggle)
                entry.IsChecked = true;
        }
    }

    private void SelectNone()
    {
        foreach (AssociationEntryViewModel? entry in DirectoryEntries.Concat(FileEntries).Concat(DoubleClickEntries))
        {
            if (entry.CanToggle)
                entry.IsChecked = false;
        }
    }

    private static IEnumerable<AssociationEntryViewModel> GetEntriesForCategory(AssociationCategory category)
    {
        AssociationEntry[] allEntries = new[]
        {
            AssociationEntries.DirectoryOpen,
            AssociationEntries.DirectoryMount,
            AssociationEntries.DirectoryAddDir,
            AssociationEntries.DirectoryAddDirNew,
            AssociationEntries.FileOpenSet,
            AssociationEntries.FileMountSet,
            AssociationEntries.FileAdd,
            AssociationEntries.FileAddNew,
            AssociationEntries.DoubleClickOpen
        };

        return allEntries
            .Where(e => e.Category == category)
            .Select(e => new AssociationEntryViewModel(e));
    }

    private async Task ToggleAssociationAsync(AssociationEntryViewModel entryVm)
    {
        if (!entryVm.CanToggle) return;

        entryVm.IsOperationInProgress = true;
        entryVm.ErrorMessage = null;

        try
        {
            AssociationOperationResult result;

            if (entryVm.State == AssociationState.Registered || entryVm.State == AssociationState.Stale)
            {
                result = await _fileAssociationService.UnregisterAsync(entryVm.Entry);
            }
            else
            {
                result = await _fileAssociationService.RegisterAsync(entryVm.Entry);
            }

            if (result.Success)
            {
                // Re-query state after successful operation
                AssociationQueryResult queryResult = await _fileAssociationService.QueryStateAsync(entryVm.Entry);
                if (queryResult.ErrorMessage != null)
                {
                    entryVm.ErrorMessage = queryResult.ErrorMessage;
                }
                else
                {
                    entryVm.State = queryResult.State;
                }
            }
            else
            {
                if (result.RequiresElevation)
                {
                    entryVm.ErrorMessage = "Elevated permissions are required. Please run the application as administrator.";
                }
                else
                {
                    entryVm.ErrorMessage = result.ErrorMessage ?? "Operation failed";
                }
            }
        }
        catch (Exception ex)
        {
            entryVm.ErrorMessage = ex.Message;
        }
        finally
        {
            entryVm.IsOperationInProgress = false;
        }
    }

    private async Task OkAsync()
    {
        // Apply file association changes
        await ApplyAssociationChangesAsync();

        // Persist keys & fix files paths
        KeysFixFilesTab.CommitChanges();

        await CloseDialog.Handle(RxVoid.Default);
    }

    private async Task CancelAsync() =>
        // Discard all changes — nothing is persisted
        await CloseDialog.Handle(RxVoid.Default);

    private async Task ApplyAssociationChangesAsync()
    {
        IEnumerable<AssociationEntryViewModel> allEntries = DirectoryEntries.Concat(FileEntries).Concat(DoubleClickEntries);
        // Include dirty entries (user changed checkbox) AND stale entries (need re-registering with current path)
        List<AssociationEntryViewModel> entriesToApply = allEntries.Where(e => e.CanToggle && (e.IsDirty || (e.IsChecked && e.State == AssociationState.Stale))).ToList();

        foreach (AssociationEntryViewModel? entryVm in entriesToApply)
        {
            entryVm.IsOperationInProgress = true;
            entryVm.ErrorMessage = null;

            try
            {
                AssociationOperationResult result;

                if (entryVm.IsChecked)
                {
                    result = await _fileAssociationService.RegisterAsync(entryVm.Entry);
                }
                else
                {
                    result = await _fileAssociationService.UnregisterAsync(entryVm.Entry);
                }

                if (result.Success)
                {
                    AssociationQueryResult queryResult = await _fileAssociationService.QueryStateAsync(entryVm.Entry);
                    if (queryResult.ErrorMessage != null)
                    {
                        entryVm.ErrorMessage = queryResult.ErrorMessage;
                    }
                    else
                    {
                        entryVm.State = queryResult.State;
                        entryVm.IsChecked = queryResult.State == AssociationState.Registered
                                         || queryResult.State == AssociationState.Stale;
                    }
                }
                else
                {
                    if (result.RequiresElevation)
                    {
                        entryVm.ErrorMessage = "Elevated permissions are required. Please run the application as administrator.";
                    }
                    else
                    {
                        entryVm.ErrorMessage = result.ErrorMessage ?? "Operation failed";
                    }
                    entryVm.IsChecked = entryVm.State == AssociationState.Registered
                                     || entryVm.State == AssociationState.Stale;
                }
            }
            catch (Exception ex)
            {
                entryVm.ErrorMessage = ex.Message;
                entryVm.IsChecked = entryVm.State == AssociationState.Registered
                                 || entryVm.State == AssociationState.Stale;
            }
            finally
            {
                entryVm.IsOperationInProgress = false;
            }
        }
    }
}