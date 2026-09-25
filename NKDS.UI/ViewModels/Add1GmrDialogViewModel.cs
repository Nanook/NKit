using Avalonia.Collections;
using Nanook.NKit.Configuration;
using Nanook.NKit.Ogmr;
using NkdsUi.Services;
using NKitDataStore;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Add 1GMR dialog. Provides YAML loading, file matching,
/// output configuration, and validation before confirming a 1GMR batch import.
/// </summary>
public class Add1GmrDialogViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private string? _ogmrYamlPath;
    private string? _ogmrYamlStatus;
    private bool _isYamlValid;
    private FileRouter? _fileRouter;
    private string _summaryText = "";
    private string? _outputFolder;
    private string _selectedShardSize = "Single File";
    private string _selectedBlockSize = "Default (64 KiB)";
    private bool _isAuxMode;
    private string _auxSetName = "";
    private string? _existingAuxName;
    private int _matchedFileCount;
    private string? _validationMessage;

    public Add1GmrDialogViewModel(IConfigService configService)
    {
        _configService = configService;

        // Pre-fill YAML path with the most recent history entry
        if (_configService.OgmrYamlHistory.Count > 0)
            _ogmrYamlPath = _configService.OgmrYamlHistory[0];

        BrowseYamlCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseYamlAsync);

        // BrowseImagesCommand: enabled only when YAML is valid
        IObservable<bool> canBrowseImages = this.WhenAnyValue(x => x.IsYamlValid);
        BrowseImagesCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseImagesAsync, canBrowseImages);

        // BrowseDirectoryCommand: enabled only when YAML is valid, opens folder picker
        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseDirectoryAsync, canBrowseImages);

        // RemoveFileCommand: always enabled, removes a specific file from the list
        RemoveFileCommand = ReactiveCommand.Create<MatchedFileItem>(file => MatchedFiles.Remove(file));

        // BrowseOutputFolderCommand: always enabled, opens folder picker
        BrowseOutputFolderCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseOutputFolderAsync);

        // ConfirmCommand: always enabled, validates on click, returns true if valid
        ConfirmCommand = ReactiveCommand.Create(ExecuteConfirm);

        // CancelCommand: always enabled, returns false
        CancelCommand = ReactiveCommand.Create(() => false);

        // Update validation message reactively
        this.WhenAnyValue(
                x => x.IsYamlValid,
                x => x.MatchedFileCount,
                x => x.OutputFolder)
            .Subscribe(_ => UpdateValidationMessage());
        // Pre-fill output folder with the most recent history entry
        if (_configService.OgmrOutputHistory.Count > 0)
            _outputFolder = _configService.OgmrOutputHistory[0];

        // When YAML path changes, parse and update status
        this.WhenAnyValue(x => x.OgmrYamlPath)
            .Subscribe(path => LoadYaml(path));

        // Initialize the sortable view
        MatchedFilesView = new DataGridCollectionView(MatchedFiles);

        // Recompute summary and re-evaluate canConfirm whenever the matched files collection changes
        MatchedFiles.CollectionChanged += (_, _) =>
        {
            UpdateSummary();
            MatchedFileCount = MatchedFiles.Count;
            MatchedFilesView.Refresh();
        };

        // Detect existing aux store when output folder changes
        this.WhenAnyValue(x => x.OutputFolder)
            .Subscribe(folder => DetectExistingAux(folder));
    }

    /// <summary>
    /// Path to the loaded 1GMR YAML file.
    /// </summary>
    public string? OgmrYamlPath
    {
        get => _ogmrYamlPath;
        set => this.RaiseAndSetIfChanged(ref _ogmrYamlPath, value);
    }

    /// <summary>
    /// The most-recently-used YAML path history for the combo box dropdown.
    /// </summary>
    public IReadOnlyList<string> OgmrYamlHistory => _configService.OgmrYamlHistory;

    /// <summary>
    /// The most-recently-used image browse directory history for the image file picker.
    /// </summary>
    public IReadOnlyList<string> ImageBrowseHistory => _configService.ImageBrowseHistory;

    /// <summary>
    /// Status message: "Loaded N games" on success, or error message on failure.
    /// </summary>
    public string? OgmrYamlStatus
    {
        get => _ogmrYamlStatus;
        private set => this.RaiseAndSetIfChanged(ref _ogmrYamlStatus, value);
    }

    /// <summary>
    /// True when the YAML file has been successfully parsed.
    /// </summary>
    public bool IsYamlValid
    {
        get => _isYamlValid;
        private set => this.RaiseAndSetIfChanged(ref _isYamlValid, value);
    }

    /// <summary>
    /// The matched files collection. Populated by task 4.3 (file management).
    /// Exposed here so YAML re-loading can re-match existing files.
    /// </summary>
    public ObservableCollection<MatchedFileItem> MatchedFiles { get; } = new();

    /// <summary>
    /// Sortable view of MatchedFiles for the DataGrid.
    /// </summary>
    public DataGridCollectionView MatchedFilesView { get; }

    /// <summary>
    /// The number of files in MatchedFiles. Used by canConfirm observable since
    /// WhenAnyValue cannot observe ObservableCollection.Count directly.
    /// </summary>
    public int MatchedFileCount
    {
        get => _matchedFileCount;
        private set => this.RaiseAndSetIfChanged(ref _matchedFileCount, value);
    }

    /// <summary>
    /// Validation message shown when the user clicks OK but conditions aren't met.
    /// Null when everything is valid.
    /// </summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => this.RaiseAndSetIfChanged(ref _validationMessage, value);
    }

    /// <summary>
    /// Summary text showing "N images, N sets, N not matched".
    /// Recomputes automatically when MatchedFiles changes.
    /// </summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => this.RaiseAndSetIfChanged(ref _summaryText, value);
    }

    /// <summary>
    /// DataStore output directory for the 1GMR import.
    /// </summary>
    public string? OutputFolder
    {
        get => _outputFolder;
        set => this.RaiseAndSetIfChanged(ref _outputFolder, value);
    }

    /// <summary>
    /// The most-recently-used output folder history for the combo box dropdown.
    /// </summary>
    public IReadOnlyList<string> OutputFolderHistory => _configService.OgmrOutputHistory;

    /// <summary>
    /// Available shard size options for the dropdown (same as CreateSetDialog).
    /// </summary>
    public string[] ShardSizeOptions { get; } = ["Default (50 GiB)", "10 GiB", "25 GiB", "50 GiB", "100 GiB", "200 GiB", "Single File"];

    /// <summary>
    /// Available block size options for the dropdown (same as CreateSetDialog).
    /// </summary>
    public string[] BlockSizeOptions { get; } = ["Default (64 KiB)", "4 KiB", "8 KiB", "16 KiB", "32 KiB", "64 KiB", "128 KiB", "256 KiB", "512 KiB", "1 MiB"];

    /// <summary>
    /// The selected shard size from the dropdown. Defaults to "Single File".
    /// </summary>
    public string SelectedShardSize
    {
        get => _selectedShardSize;
        set => this.RaiseAndSetIfChanged(ref _selectedShardSize, value);
    }

    /// <summary>
    /// The selected block size from the dropdown. Defaults to "Default (64 KiB)".
    /// </summary>
    public string SelectedBlockSize
    {
        get => _selectedBlockSize;
        set => this.RaiseAndSetIfChanged(ref _selectedBlockSize, value);
    }

    /// <summary>
    /// Whether to automatically create an aux store (.aux.nkds) for update partitions.
    /// Disabled when an existing aux store is found in the output folder.
    /// </summary>
    public bool IsAuxMode
    {
        get => _isAuxMode;
        set => this.RaiseAndSetIfChanged(ref _isAuxMode, value);
    }

    /// <summary>
    /// The filename for the aux store (e.g. "wii.aux.nkds").
    /// Auto-populated when an existing aux is found, or defaults based on context.
    /// </summary>
    public string AuxSetName
    {
        get => _auxSetName;
        set => this.RaiseAndSetIfChanged(ref _auxSetName, value);
    }

    /// <summary>
    /// The name of an existing aux store found in the output folder, or null if none exists.
    /// When non-null, the aux checkbox is dimmed and the text box shows this name.
    /// </summary>
    public string? ExistingAuxName
    {
        get => _existingAuxName;
        private set => this.RaiseAndSetIfChanged(ref _existingAuxName, value);
    }

    /// <summary>
    /// True when an aux store already exists in the output folder.
    /// </summary>
    public bool IsAuxExisting => _existingAuxName != null;

    /// <summary>
    /// True when the aux controls (checkbox + text box) should be editable.
    /// False when an existing aux store was found.
    /// </summary>
    public bool IsAuxEditable => _existingAuxName == null;

    /// <summary>
    /// Interaction to show a folder picker for the output directory. The View registers
    /// a handler that returns the selected folder path, or null if cancelled.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFolderPicker { get; } = new();

    /// <summary>
    /// Interaction to show a YAML file picker. The View registers a handler that
    /// returns the selected file path, or null if cancelled.
    /// </summary>
    public Interaction<RxVoid, string?> ShowYamlPicker { get; } = new();

    /// <summary>
    /// Command to browse for a YAML file. Opens the YAML picker interaction.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseYamlCommand { get; }

    /// <summary>
    /// Interaction to show a file picker for disc image files. The View registers a handler
    /// that returns the selected file paths, or an empty list if cancelled.
    /// </summary>
    public Interaction<RxVoid, IReadOnlyList<string>> ShowImagePicker { get; } = new();

    /// <summary>
    /// Command to browse for disc image files. Enabled only when a valid YAML is loaded.
    /// Opens the image picker and adds matched results to MatchedFiles.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseImagesCommand { get; }

    /// <summary>
    /// Interaction to show a folder picker for selecting directories containing disc images.
    /// The View registers a handler that returns selected folder paths, or empty if cancelled.
    /// </summary>
    public Interaction<RxVoid, IReadOnlyList<string>> ShowImageDirectoryPicker { get; } = new();

    /// <summary>
    /// Command to browse for a directory containing disc images. Enabled only when a valid YAML is loaded.
    /// Opens the folder picker and adds all child image files to MatchedFiles.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseDirectoryCommand { get; }

    /// <summary>
    /// Command to remove a file from the matched files list.
    /// </summary>
    public ReactiveCommand<MatchedFileItem, RxVoid> RemoveFileCommand { get; }

    /// <summary>
    /// Command to browse for an output folder. Opens the folder picker interaction.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseOutputFolderCommand { get; }

    /// <summary>
    /// Command to confirm the dialog (OK). Always enabled — validates on click
    /// and shows a message if conditions aren't met. Returns true if valid, null if not.
    /// </summary>
    public ReactiveCommand<RxVoid, bool?> ConfirmCommand { get; }

    /// <summary>
    /// Command to cancel the dialog. Always enabled. Returns false.
    /// </summary>
    public ReactiveCommand<RxVoid, bool> CancelCommand { get; }

    /// <summary>
    /// Interaction to close the dialog with a result (true = confirmed, false = cancelled).
    /// The View registers a handler that closes the window with the appropriate result.
    /// </summary>
    public Interaction<bool, RxVoid> CloseDialog { get; } = new();

    private bool? ExecuteConfirm()
    {
        UpdateValidationMessage();
        if (ValidationMessage != null)
            return null; // Don't close — validation failed

        return true;
    }

    private void UpdateValidationMessage()
    {
        if (!IsYamlValid)
            ValidationMessage = "Please load a valid 1GMR YAML file";
        else if (MatchedFileCount == 0)
            ValidationMessage = "Please add at least one image file";
        else if (string.IsNullOrWhiteSpace(OutputFolder))
            ValidationMessage = "Please specify an output folder";
        else
            ValidationMessage = null;
    }

    private async Task ExecuteBrowseYamlAsync()
    {
        string path = await ShowYamlPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            OgmrYamlPath = path;
    }

    private async Task ExecuteBrowseImagesAsync()
    {
        IReadOnlyList<string> files = await ShowImagePicker.Handle(RxVoid.Default);
        if (files.Count > 0)
        {
            // Persist the directory of the first selected file for next time
            string? dir = Path.GetDirectoryName(files[0]);
            if (!string.IsNullOrEmpty(dir))
                _configService.AddImageBrowsePath(dir);

            AddFiles(files);
        }
    }

    private async Task ExecuteBrowseDirectoryAsync()
    {
        IReadOnlyList<string> folders = await ShowImageDirectoryPicker.Handle(RxVoid.Default);
        if (folders.Count > 0)
        {
            // Persist the first selected folder for next time
            _configService.AddImageBrowsePath(folders[0]);

            List<string> allFiles = new List<string>();
            foreach (string folder in folders)
            {
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                        allFiles.Add(file);
                }
            }

            if (allFiles.Count > 0)
                AddFiles(allFiles);
        }
    }

    private async Task ExecuteBrowseOutputFolderAsync()
    {
        string folder = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(folder))
            OutputFolder = folder;
    }

    /// <summary>
    /// Matches each file against the loaded FileRouter and adds to MatchedFiles.
    /// Skips duplicates (files already in the list by full path).
    /// </summary>
    internal void AddFiles(IReadOnlyList<string> filePaths)
    {
        foreach (string filePath in filePaths)
        {
            // Avoid duplicates
            if (MatchedFiles.Any(f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            string fileName = Path.GetFileName(filePath);
            GameEntry? entry = _fileRouter?.Match(fileName);
            MatchedFiles.Add(new MatchedFileItem
            {
                FileName = fileName,
                FilePath = filePath,
                MatchedSetName = entry?.SanitizedName ?? "No Match",
                IsMatched = entry != null
            });
        }
    }

    private void LoadYaml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _fileRouter = null;
            IsYamlValid = false;
            OgmrYamlStatus = null;
            return;
        }

        try
        {
            List<GameEntry> games = OgmrYamlParser.Parse(path);
            _fileRouter = new FileRouter(games);
            IsYamlValid = true;
            OgmrYamlStatus = $"Loaded {games.Count} games";

            // Re-match any existing files against the new YAML
            RematchFiles();
        }
        catch (OgmrException ex)
        {
            _fileRouter = null;
            IsYamlValid = false;
            OgmrYamlStatus = ex.Message;
        }
        catch (Exception ex)
        {
            _fileRouter = null;
            IsYamlValid = false;
            OgmrYamlStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-matches all files in MatchedFiles against the current FileRouter.
    /// Called when YAML is reloaded to update match results.
    /// </summary>
    private void RematchFiles()
    {
        if (MatchedFiles.Count == 0)
            return;

        // Capture file paths, rebuild the collection with updated match results
        List<string> filePaths = MatchedFiles.Select(f => f.FilePath).ToList();
        MatchedFiles.Clear();

        foreach (string? filePath in filePaths)
        {
            string fileName = Path.GetFileName(filePath);
            GameEntry? entry = _fileRouter?.Match(fileName);
            MatchedFiles.Add(new MatchedFileItem
            {
                FileName = fileName,
                FilePath = filePath,
                MatchedSetName = entry?.SanitizedName ?? "No Match",
                IsMatched = entry != null
            });
        }
    }

    /// <summary>
    /// Recomputes the SummaryText based on the current MatchedFiles collection.
    /// Format: "N images, N sets, N not matched"
    /// </summary>
    private void UpdateSummary()
    {
        int totalImages = MatchedFiles.Count;
        int setCount = MatchedFiles
            .Where(f => f.IsMatched)
            .Select(f => f.MatchedSetName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int notMatched = MatchedFiles.Count(f => !f.IsMatched);

        SummaryText = $"{totalImages} images, {setCount} sets, {notMatched} not matched";
    }

    /// <summary>
    /// Scans the output folder for an existing .aux.nkds file.
    /// If found, populates ExistingAuxName and dims the aux controls.
    /// If not found, resets to editable state and defaults AuxSetName to folder name.
    /// </summary>
    private void DetectExistingAux(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ExistingAuxName = null;
            if (!_isAuxMode)
                AuxSetName = "";
            this.RaisePropertyChanged(nameof(IsAuxExisting));
            this.RaisePropertyChanged(nameof(IsAuxEditable));
            return;
        }

        string auxSuffix = DataStore.AuxSetSuffix; // ".aux.nkds"
        foreach (string file in Directory.EnumerateFiles(folderPath, $"*{auxSuffix}"))
        {
            string fileName = Path.GetFileName(file);
            ExistingAuxName = fileName;
            AuxSetName = fileName;
            IsAuxMode = true;
            this.RaisePropertyChanged(nameof(IsAuxExisting));
            this.RaisePropertyChanged(nameof(IsAuxEditable));
            return;
        }

        // No aux found — default name to folder name + .aux.nkds
        ExistingAuxName = null;
        string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(folderName))
            AuxSetName = folderName.ToLowerInvariant() + DataStore.AuxSetSuffix;
        this.RaisePropertyChanged(nameof(IsAuxExisting));
        this.RaisePropertyChanged(nameof(IsAuxEditable));
    }

    /// <summary>
    /// Gets the current FileRouter for use by file addition logic (task 4.3).
    /// Returns null if no valid YAML is loaded.
    /// </summary>
    internal FileRouter? GetFileRouter() => _fileRouter;

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
}