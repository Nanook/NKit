using Nanook.NKit.Configuration;
using NKDS.Validation;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Create Set dialog. Provides inline validation for set name,
/// shard size, and block size fields. ConfirmCommand returns true (dialog result)
/// when all fields pass validation; CancelCommand returns false.
/// </summary>
public class CreateSetDialogViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private string _setName = "";
    private string _folderPath = "";
    private string? _folderPathError;
    private string _selectedShardSize = "Default (50 GiB)";
    private string _selectedBlockSize = "Default (64 KiB)";
    private bool _isAuxMode;
    private string _auxSetName = "";
    private string? _existingAuxName;
    private string? _setNameError;

    /// <summary>
    /// Available shard size options for the dropdown.
    /// </summary>
    public string[] ShardSizeOptions { get; } = ["Default (50 GiB)", "Single File", "10 GiB", "25 GiB", "50 GiB", "100 GiB", "200 GiB"];

    /// <summary>
    /// Available block size options for the dropdown.
    /// </summary>
    public string[] BlockSizeOptions { get; } = ["Default (64 KiB)", "4 KiB", "8 KiB", "16 KiB", "32 KiB", "64 KiB", "128 KiB", "256 KiB", "512 KiB", "1 MiB"];

    /// <summary>
    /// Maximum allowed length for a folder path (Windows MAX_PATH).
    /// </summary>
    internal const int MaxFolderPathLength = 260;

    /// <summary>
    /// The DataStore folder path. Bindable two-way for the inline folder text box.
    /// Values exceeding 260 characters are truncated.
    /// </summary>
    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (value?.Length > MaxFolderPathLength)
                value = value[..MaxFolderPathLength];
            this.RaiseAndSetIfChanged(ref _folderPath, value ?? "");
        }
    }

    /// <summary>
    /// Inline validation error for the folder path, or null if valid/empty.
    /// </summary>
    public string? FolderPathError
    {
        get => _folderPathError;
        set => this.RaiseAndSetIfChanged(ref _folderPathError, value);
    }

    /// <summary>
    /// The most-recently-used DataStore path history for the dropdown.
    /// </summary>
    public IReadOnlyList<string> DataStoreHistory => _configService.CreateSetHistory;

    /// <summary>
    /// Interaction to show a folder picker for selecting the DataStore directory.
    /// Input is the suggested start path (or null). Output is the selected path (or null if cancelled).
    /// </summary>
    public Interaction<string?, string?> ShowFolderPicker { get; } = new();

    /// <summary>
    /// The name for the new set. Validated against NkdsValidation.ValidateSetName.
    /// </summary>
    public string SetName
    {
        get => _setName;
        set
        {
            this.RaiseAndSetIfChanged(ref _setName, value);
            ValidateSetName();
        }
    }

    /// <summary>
    /// The selected shard size from the dropdown.
    /// </summary>
    public string SelectedShardSize
    {
        get => _selectedShardSize;
        set => this.RaiseAndSetIfChanged(ref _selectedShardSize, value);
    }

    /// <summary>
    /// The selected block size from the dropdown.
    /// </summary>
    public string SelectedBlockSize
    {
        get => _selectedBlockSize;
        set => this.RaiseAndSetIfChanged(ref _selectedBlockSize, value);
    }

    /// <summary>
    /// Whether to create an aux store alongside the primary set.
    /// Disabled (dimmed) when an aux store already exists in the folder.
    /// </summary>
    public bool IsAuxMode
    {
        get => _isAuxMode;
        set => this.RaiseAndSetIfChanged(ref _isAuxMode, value);
    }

    /// <summary>
    /// The name for the aux set (without the .nkds extension).
    /// When an existing aux is found, this is populated with its name and read-only.
    /// When creating new, the user can type a name (defaults to SetName + ".aux").
    /// </summary>
    public string AuxSetName
    {
        get => _auxSetName;
        set => this.RaiseAndSetIfChanged(ref _auxSetName, value);
    }

    /// <summary>
    /// The name of an existing aux store found in the folder, or null if none exists.
    /// When non-null, the aux checkbox is dimmed and the text box shows this name.
    /// </summary>
    public string? ExistingAuxName
    {
        get => _existingAuxName;
        private set => this.RaiseAndSetIfChanged(ref _existingAuxName, value);
    }

    /// <summary>
    /// True when an aux store already exists in the folder. Used to dim the checkbox and text box.
    /// </summary>
    public bool IsAuxExisting => _existingAuxName != null;

    /// <summary>
    /// True when the aux controls (checkbox + text box) should be editable.
    /// False when an existing aux store was found.
    /// </summary>
    public bool IsAuxEditable => _existingAuxName == null;

    /// <summary>
    /// Inline error message for the set name field, or null if valid.
    /// </summary>
    public string? SetNameError
    {
        get => _setNameError;
        private set => this.RaiseAndSetIfChanged(ref _setNameError, value);
    }

    /// <summary>
    /// The parsed shard size in bytes from the selected dropdown value.
    /// "Single File" = 0, "Default (50 GiB)" = 50 GiB.
    /// </summary>
    public long ParsedShardSize
    {
        get
        {
            if (_selectedShardSize == "Single File") return 0;
            if (_selectedShardSize.StartsWith("Default")) return ConfigSettingsFormatParser.ParseSizeToBytes("50 GiB");
            return ConfigSettingsFormatParser.ParseSizeToBytes(_selectedShardSize);
        }
    }

    /// <summary>
    /// The parsed block size in bytes from the selected dropdown value.
    /// "Default (64 KiB)" = 64 KiB.
    /// </summary>
    public int ParsedBlockSize
    {
        get
        {
            if (_selectedBlockSize.StartsWith("Default")) return (int)ConfigSettingsFormatParser.ParseSizeToBytes("64 KiB");
            return (int)ConfigSettingsFormatParser.ParseSizeToBytes(_selectedBlockSize);
        }
    }

    /// <summary>
    /// Command that opens the folder picker interaction and sets FolderPath to the selected path.
    /// If the user cancels, the previous FolderPath is retained.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseCommand { get; }

    /// <summary>
    /// Command that confirms the dialog. Only executable when set name is valid.
    /// Returns true as the dialog result.
    /// </summary>
    public ReactiveCommand<RxVoid, bool> ConfirmCommand { get; }

    /// <summary>
    /// Command that cancels the dialog. Always executable. Returns false as the dialog result.
    /// </summary>
    public ReactiveCommand<RxVoid, bool> CancelCommand { get; }

    public CreateSetDialogViewModel(string? dataStorePath, IConfigService configService)
    {
        _configService = configService;

        // Pre-populate FolderPath: from session path, or first MRU entry, or empty
        if (!string.IsNullOrWhiteSpace(dataStorePath))
            _folderPath = dataStorePath;
        else if (_configService.CreateSetHistory.Count > 0)
            _folderPath = _configService.CreateSetHistory[0];

        // canExecute: set name valid AND folder path valid AND folder path non-empty
        IObservable<bool> canConfirm = this.WhenAnyValue(
                x => x.SetNameError,
                x => x.FolderPathError,
                x => x.FolderPath)
            .Select(((string? nameErr, string? folderErr, string folderPath) t) =>
                t.nameErr == null && t.folderErr == null && !string.IsNullOrWhiteSpace(t.folderPath))
            .DistinctUntilChanged();

        ConfirmCommand = ReactiveCommand.Create(() =>
        {
            _configService.AddCreateSetPath(FolderPath);
            return true;
        }, canConfirm);
        CancelCommand = ReactiveCommand.Create(() => false);
        BrowseCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseAsync);

        // Run initial validation
        ValidateSetName();

        // Debounced folder path validation for typed input (300ms)
        this.WhenAnyValue(x => x.FolderPath)
            .Throttle(TimeSpan.FromMilliseconds(300), RxSchedulers.MainThreadScheduler)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(path =>
            {
                ValidateFolderPath();
                if (FolderPathError == null && !string.IsNullOrWhiteSpace(path))
                    DetectExistingAux(path);
            });

        // Check for existing aux store in the initial folder
        DetectExistingAux(_folderPath);
    }

    private void ValidateSetName()
    {
        (bool isValid, string? error) = NkdsValidation.ValidateSetName(_setName);
        SetNameError = isValid ? null : error;
    }

    /// <summary>
    /// Validates the current FolderPath. Sets FolderPathError to null if valid or empty,
    /// or to an error message if the path is non-empty and does not exist as a directory.
    /// Can be called directly for immediate validation (browse/dropdown) or by the debounced subscription.
    /// </summary>
    private void ValidateFolderPath()
    {
        if (string.IsNullOrWhiteSpace(_folderPath))
        {
            FolderPathError = null;
            return;
        }

        if (!Directory.Exists(_folderPath))
        {
            FolderPathError = "Path must be an existing directory";
            return;
        }

        FolderPathError = null;
    }

    /// <summary>
    /// Opens the ShowFolderPicker interaction with the current FolderPath as the suggested start location.
    /// If the user selects a folder, sets FolderPath and triggers immediate validation (no debounce).
    /// If the user cancels, retains the previous FolderPath.
    /// </summary>
    private async Task ExecuteBrowseAsync()
    {
        // Pass the current path as suggested start location, or null if it doesn't exist
        string? suggestedPath = !string.IsNullOrWhiteSpace(_folderPath) && Directory.Exists(_folderPath)
            ? _folderPath
            : null;

        string selectedPath = await ShowFolderPicker.Handle(suggestedPath);

        if (!string.IsNullOrEmpty(selectedPath))
        {
            FolderPath = selectedPath;
            // Immediate validation — bypass debounce for browse selection
            ValidateFolderPath();
            if (FolderPathError == null)
                DetectExistingAux(selectedPath);
        }
    }

    /// <summary>
    /// Scans the dataStorePath for an existing .aux.nkds file.
    /// If found, populates ExistingAuxName and dims the aux controls.
    /// If not found, clears aux detection, enables aux controls, and defaults AuxSetName to the folder name.
    /// If path is empty/invalid, clears aux detection and leaves aux controls in editable state.
    /// </summary>
    private void DetectExistingAux(string? dataStorePath)
    {
        if (string.IsNullOrWhiteSpace(dataStorePath) || !Directory.Exists(dataStorePath))
        {
            // Path is empty/invalid — clear aux detection, leave controls editable
            ClearAuxDetection();
            return;
        }

        // Use the same discovery pattern as DataStore.ResolveAuxSetName
        string auxSuffix = DataStore.AuxSetSuffix; // ".aux.nkds"
        foreach (string file in Directory.EnumerateFiles(dataStorePath, $"*{auxSuffix}"))
        {
            string fileName = Path.GetFileName(file);
            ExistingAuxName = fileName;
            AuxSetName = fileName;
            IsAuxMode = true;
            this.RaisePropertyChanged(nameof(IsAuxExisting));
            this.RaisePropertyChanged(nameof(IsAuxEditable));
            return;
        }

        // No aux found — clear detection, enable controls, default name to folder name + .aux.nkds
        ClearAuxDetection();
        string folderName = Path.GetFileName(dataStorePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(folderName))
            AuxSetName = folderName.ToLowerInvariant() + DataStore.AuxSetSuffix;
    }

    /// <summary>
    /// Clears the aux detection state: sets ExistingAuxName to null and raises
    /// property changed notifications for IsAuxExisting and IsAuxEditable,
    /// leaving the aux controls in their editable state.
    /// </summary>
    private void ClearAuxDetection()
    {
        if (_existingAuxName != null)
        {
            ExistingAuxName = null;
            this.RaisePropertyChanged(nameof(IsAuxExisting));
            this.RaisePropertyChanged(nameof(IsAuxEditable));
        }
    }
}