using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the "Keys &amp; Fix Files" settings tab.
/// Manages bindable path properties, browse commands, and a reset-to-defaults command.
/// Path changes persist immediately via IConfigService.
/// </summary>
public class KeysFixFilesTabViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private bool _suppressPersist;

    private string _wiiUKeysPath = "";
    private string _ps3KeysPath = "";
    private string _gameCubeFixInfoPath = "";
    private string _gameCubeFixFilesPath = "";
    private string _wiiFixInfoPath = "";
    private string _wiiFixFilesPath = "";
    private string _ps3FixInfoPath = "";
    private string _ps3FixFilesPath = "";
    private string _dreamcastFixInfoPath = "";

    public KeysFixFilesTabViewModel(IConfigService configService)
    {
        _configService = configService;

        _suppressPersist = true;
        LoadFromConfig();
        // Keep persistence suppressed — the parent ViewModel commits on OK

        // Browse commands — folder picker for keys and fix files directories, file picker for fix info files
        BrowseWiiUKeysCommand = ReactiveCommand.CreateFromTask(BrowseWiiUKeysAsync);
        BrowseWiiUKeysArchiveCommand = ReactiveCommand.CreateFromTask(BrowseWiiUKeysArchiveAsync);
        BrowsePs3KeysCommand = ReactiveCommand.CreateFromTask(BrowsePs3KeysAsync);
        BrowsePs3KeysArchiveCommand = ReactiveCommand.CreateFromTask(BrowsePs3KeysArchiveAsync);
        BrowseGameCubeFixInfoCommand = ReactiveCommand.CreateFromTask(BrowseGameCubeFixInfoAsync);
        BrowseGameCubeFixFilesCommand = ReactiveCommand.CreateFromTask(BrowseGameCubeFixFilesAsync);
        BrowseWiiFixInfoCommand = ReactiveCommand.CreateFromTask(BrowseWiiFixInfoAsync);
        BrowseWiiFixFilesCommand = ReactiveCommand.CreateFromTask(BrowseWiiFixFilesAsync);
        BrowsePs3FixInfoCommand = ReactiveCommand.CreateFromTask(BrowsePs3FixInfoAsync);
        BrowsePs3FixFilesCommand = ReactiveCommand.CreateFromTask(BrowsePs3FixFilesAsync);
        BrowseDreamcastFixInfoCommand = ReactiveCommand.CreateFromTask(BrowseDreamcastFixInfoAsync);

        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
    }

    // --- Tooltip constants matching requirements ---

    /// <summary>Tooltip for GameCube fix info field (Requirement 4.7).</summary>
    public const string GameCubeFixInfoTooltip = "Used for reading updated junk IDs when adding to the store - not required after that";

    /// <summary>Tooltip for GameCube fix files field (Requirement 4.8).</summary>
    public const string GameCubeFixFilesTooltip = "Not currently used by NKDS";

    /// <summary>Tooltip for Wii fix info field (Requirement 5.7).</summary>
    public const string WiiFixInfoTooltip = "Not currently used by NKDS";

    /// <summary>Tooltip for Wii fix files field (Requirement 5.8).</summary>
    public const string WiiFixFilesTooltip = "Only required for reading nkit.iso and nkit.gcz with partitions removed";

    /// <summary>Tooltip for PS3 fix info field (Requirement 6.7).</summary>
    public const string Ps3FixInfoTooltip = "Used for sanitising overlapping filesystems";

    /// <summary>Tooltip for PS3 fix files field (Requirement 6.8).</summary>
    public const string Ps3FixFilesTooltip = "Not currently used by NKDS";

    /// <summary>Tooltip for Dreamcast fix info field (Requirement 7.5).</summary>
    public const string DreamcastFixInfoTooltip = "Used for correcting audio differences when converting between CUE and GDI formats";

    // --- Bindable path properties ---

    public string WiiUKeysPath
    {
        get => _wiiUKeysPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _wiiUKeysPath, value);
            PersistPaths();
        }
    }

    public string Ps3KeysPath
    {
        get => _ps3KeysPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _ps3KeysPath, value);
            PersistPaths();
        }
    }

    public string GameCubeFixInfoPath
    {
        get => _gameCubeFixInfoPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _gameCubeFixInfoPath, value);
            PersistPaths();
        }
    }

    public string GameCubeFixFilesPath
    {
        get => _gameCubeFixFilesPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _gameCubeFixFilesPath, value);
            PersistPaths();
        }
    }

    public string WiiFixInfoPath
    {
        get => _wiiFixInfoPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _wiiFixInfoPath, value);
            PersistPaths();
        }
    }

    public string WiiFixFilesPath
    {
        get => _wiiFixFilesPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _wiiFixFilesPath, value);
            PersistPaths();
        }
    }

    public string Ps3FixInfoPath
    {
        get => _ps3FixInfoPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _ps3FixInfoPath, value);
            PersistPaths();
        }
    }

    public string Ps3FixFilesPath
    {
        get => _ps3FixFilesPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _ps3FixFilesPath, value);
            PersistPaths();
        }
    }

    public string DreamcastFixInfoPath
    {
        get => _dreamcastFixInfoPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _dreamcastFixInfoPath, value);
            PersistPaths();
        }
    }

    // --- Interactions for file/folder pickers (View registers handlers) ---

    /// <summary>
    /// Interaction to show a folder picker. Returns the selected folder path, or null if cancelled.
    /// Used for keys paths and fix files (directory) paths.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFolderPicker { get; } = new();

    /// <summary>
    /// Interaction to show a file picker. Returns the selected file path, or null if cancelled.
    /// Used for fix info (YAML file) paths.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFilePicker { get; } = new();

    /// <summary>
    /// Interaction to show a file picker for key archives. Returns the selected file path, or null if cancelled.
    /// Used for keys archive paths (zip files containing encryption keys).
    /// </summary>
    public Interaction<RxVoid, string?> ShowKeysArchivePicker { get; } = new();

    // --- Browse commands ---

    /// <summary>Browse for WiiU keys folder.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseWiiUKeysCommand { get; }

    /// <summary>Browse for WiiU keys archive file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseWiiUKeysArchiveCommand { get; }

    /// <summary>Browse for PS3 keys folder.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowsePs3KeysCommand { get; }

    /// <summary>Browse for PS3 keys archive file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowsePs3KeysArchiveCommand { get; }

    /// <summary>Browse for GameCube fix info file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseGameCubeFixInfoCommand { get; }

    /// <summary>Browse for GameCube fix files folder.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseGameCubeFixFilesCommand { get; }

    /// <summary>Browse for Wii fix info file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseWiiFixInfoCommand { get; }

    /// <summary>Browse for Wii fix files folder.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseWiiFixFilesCommand { get; }

    /// <summary>Browse for PS3 fix info file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowsePs3FixInfoCommand { get; }

    /// <summary>Browse for PS3 fix files folder.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowsePs3FixFilesCommand { get; }

    /// <summary>Browse for Dreamcast fix info file.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseDreamcastFixInfoCommand { get; }

    // --- Reset command ---

    /// <summary>Resets all paths to their default values.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ResetToDefaultsCommand { get; }

    // --- Private helpers ---

    private void LoadFromConfig()
    {
        KeysAndFixPathsConfig paths = _configService.KeysAndFixPaths;
        _wiiUKeysPath = paths.WiiUKeysPath;
        _ps3KeysPath = paths.Ps3KeysPath;
        _gameCubeFixInfoPath = paths.GameCubeFixInfoPath;
        _gameCubeFixFilesPath = paths.GameCubeFixFilesPath;
        _wiiFixInfoPath = paths.WiiFixInfoPath;
        _wiiFixFilesPath = paths.WiiFixFilesPath;
        _ps3FixInfoPath = paths.Ps3FixInfoPath;
        _ps3FixFilesPath = paths.Ps3FixFilesPath;
        _dreamcastFixInfoPath = paths.DreamcastFixInfoPath;
    }

    private void PersistPaths()
    {
        if (_suppressPersist) return;

        KeysAndFixPathsConfig config = new KeysAndFixPathsConfig
        {
            WiiUKeysPath = _wiiUKeysPath,
            Ps3KeysPath = _ps3KeysPath,
            GameCubeFixInfoPath = _gameCubeFixInfoPath,
            GameCubeFixFilesPath = _gameCubeFixFilesPath,
            WiiFixInfoPath = _wiiFixInfoPath,
            WiiFixFilesPath = _wiiFixFilesPath,
            Ps3FixInfoPath = _ps3FixInfoPath,
            Ps3FixFilesPath = _ps3FixFilesPath,
            DreamcastFixInfoPath = _dreamcastFixInfoPath
        };
        _configService.SetKeysAndFixPaths(config);
    }

    private void ResetToDefaults()
    {
        _configService.ResetKeysAndFixPathsToDefaults();

        LoadFromConfig();

        // Raise property changed for all path properties so the UI refreshes
        this.RaisePropertyChanged(nameof(WiiUKeysPath));
        this.RaisePropertyChanged(nameof(Ps3KeysPath));
        this.RaisePropertyChanged(nameof(GameCubeFixInfoPath));
        this.RaisePropertyChanged(nameof(GameCubeFixFilesPath));
        this.RaisePropertyChanged(nameof(WiiFixInfoPath));
        this.RaisePropertyChanged(nameof(WiiFixFilesPath));
        this.RaisePropertyChanged(nameof(Ps3FixInfoPath));
        this.RaisePropertyChanged(nameof(Ps3FixFilesPath));
        this.RaisePropertyChanged(nameof(DreamcastFixInfoPath));
    }

    /// <summary>
    /// Persists all current path values to the config service. Called by the parent ViewModel on OK.
    /// </summary>
    internal void CommitChanges()
    {
        KeysAndFixPathsConfig config = new KeysAndFixPathsConfig
        {
            WiiUKeysPath = _wiiUKeysPath,
            Ps3KeysPath = _ps3KeysPath,
            GameCubeFixInfoPath = _gameCubeFixInfoPath,
            GameCubeFixFilesPath = _gameCubeFixFilesPath,
            WiiFixInfoPath = _wiiFixInfoPath,
            WiiFixFilesPath = _wiiFixFilesPath,
            Ps3FixInfoPath = _ps3FixInfoPath,
            Ps3FixFilesPath = _ps3FixFilesPath,
            DreamcastFixInfoPath = _dreamcastFixInfoPath
        };
        _configService.SetKeysAndFixPaths(config);
    }

    // --- Browse handlers ---

    private async Task BrowseWiiUKeysAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            WiiUKeysPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseWiiUKeysArchiveAsync()
    {
        string path = await ShowKeysArchivePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            WiiUKeysPath = PathResolver.ToStorable(path);
    }

    private async Task BrowsePs3KeysAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            Ps3KeysPath = PathResolver.ToStorable(path);
    }

    private async Task BrowsePs3KeysArchiveAsync()
    {
        string path = await ShowKeysArchivePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            Ps3KeysPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseGameCubeFixInfoAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            GameCubeFixInfoPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseGameCubeFixFilesAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            GameCubeFixFilesPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseWiiFixInfoAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            WiiFixInfoPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseWiiFixFilesAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            WiiFixFilesPath = PathResolver.ToStorable(path);
    }

    private async Task BrowsePs3FixInfoAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            Ps3FixInfoPath = PathResolver.ToStorable(path);
    }

    private async Task BrowsePs3FixFilesAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            Ps3FixFilesPath = PathResolver.ToStorable(path);
    }

    private async Task BrowseDreamcastFixInfoAsync()
    {
        string path = await ShowFilePicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            DreamcastFixInfoPath = PathResolver.ToStorable(path);
    }
}