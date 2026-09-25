using ReactiveUI;

namespace NkdsUi.ViewModels;

/// <summary>
/// Shared reactive state that mediates cross-ViewModel communication.
/// Any ViewModel can write to properties; any ViewModel can subscribe to changes.
/// Eliminates direct property-setting between ViewModels.
/// </summary>
public class SharedObservableState : ReactiveObject
{
    // --- Properties written by MainWindowViewModel / ImageListViewModel ---

    private bool _hasSelectedImages;

    /// <summary>
    /// Whether images are currently selected in the Image_List.
    /// </summary>
    public bool HasSelectedImages
    {
        get => _hasSelectedImages;
        set => this.RaiseAndSetIfChanged(ref _hasSelectedImages, value);
    }

    private bool _hasRemovedImages;

    /// <summary>
    /// Whether the Active Set contains removed images available for restore.
    /// </summary>
    public bool HasRemovedImages
    {
        get => _hasRemovedImages;
        set => this.RaiseAndSetIfChanged(ref _hasRemovedImages, value);
    }

    private bool _hasDataStoreOpen;

    /// <summary>
    /// Whether a DataStore is currently open.
    /// </summary>
    public bool HasDataStoreOpen
    {
        get => _hasDataStoreOpen;
        set => this.RaiseAndSetIfChanged(ref _hasDataStoreOpen, value);
    }

    private string? _operationInProgressOnSet;

    /// <summary>
    /// The set name that currently has a mutating operation in progress, or null if none.
    /// </summary>
    public string? OperationInProgressOnSet
    {
        get => _operationInProgressOnSet;
        set => this.RaiseAndSetIfChanged(ref _operationInProgressOnSet, value);
    }

    private bool _isOperationActive;

    /// <summary>
    /// Whether any long-running operation is active.
    /// </summary>
    public bool IsOperationActive
    {
        get => _isOperationActive;
        set => this.RaiseAndSetIfChanged(ref _isOperationActive, value);
    }

    private int _filteredImageCount;

    /// <summary>
    /// The number of images currently visible after filtering.
    /// </summary>
    public int FilteredImageCount
    {
        get => _filteredImageCount;
        set => this.RaiseAndSetIfChanged(ref _filteredImageCount, value);
    }

    private bool _isGroupWindowOpen;

    /// <summary>
    /// Whether the Group window is currently open.
    /// </summary>
    public bool IsGroupWindowOpen
    {
        get => _isGroupWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isGroupWindowOpen, value);
    }

    private bool _isDatWindowOpen;

    /// <summary>
    /// Whether the Dat Verification window is currently open.
    /// </summary>
    public bool IsDatWindowOpen
    {
        get => _isDatWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isDatWindowOpen, value);
    }

    private bool _isMountActive;

    /// <summary>
    /// Whether the currently selected set has an active mount.
    /// </summary>
    public bool IsMountActive
    {
        get => _isMountActive;
        set => this.RaiseAndSetIfChanged(ref _isMountActive, value);
    }

    private string? _selectedSetName = "All";

    /// <summary>
    /// The currently selected (active) set name.
    /// </summary>
    public string? SelectedSetName
    {
        get => _selectedSetName;
        set => this.RaiseAndSetIfChanged(ref _selectedSetName, value);
    }

    private bool _showFilterBar;

    /// <summary>
    /// Whether the filter bar is visible.
    /// </summary>
    public bool ShowFilterBar
    {
        get => _showFilterBar;
        set => this.RaiseAndSetIfChanged(ref _showFilterBar, value);
    }

    // --- Properties written by ImageListSyncService ---

    private IReadOnlyList<string> _availableSetNames = Array.Empty<string>();

    /// <summary>
    /// Available set names in the current DataStore.
    /// Written by ImageListSyncService, consumed by ToolbarViewModel.
    /// </summary>
    public IReadOnlyList<string> AvailableSetNames
    {
        get => _availableSetNames;
        set => this.RaiseAndSetIfChanged(ref _availableSetNames, value);
    }
}