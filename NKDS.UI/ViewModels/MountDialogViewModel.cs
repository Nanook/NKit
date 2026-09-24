using NKDS.Mount;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Mount dialog. Collects mount point path and mount options
/// (show image, show filesystem, show system views). Checks platform support
/// and disables confirmation when the platform is unsupported or mount point is empty.
/// </summary>
public class MountDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Maximum allowed length for the mount point path (Windows MAX_PATH).
    /// </summary>
    public const int MaxMountPointLength = 260;

    private readonly IMountService _mountService;
    private readonly IConfigService _configService;
    private readonly IReadOnlyCollection<string> _activeMountPoints;

    private string _mountPoint = "";
    private bool _showImage = true;
    private bool _showFileSystem = true;
    private bool _showSystem;
    private bool _updateMode;
    private bool _allowOther;
    private string _uidText = "";
    private string _gidText = "";
    private bool _isPlatformSupported = true;
    private string? _platformUnsupportedReason;
    private string? _errorMessage;

    /// <summary>
    /// The directory path where the set will be mounted as a virtual filesystem.
    /// Enforces a 260-character maximum length.
    /// </summary>
    public string MountPoint
    {
        get => _mountPoint;
        set
        {
            string truncated = value.Length > MaxMountPointLength
                ? value[..MaxMountPointLength]
                : value;
            this.RaiseAndSetIfChanged(ref _mountPoint, truncated);
        }
    }

    /// <summary>
    /// Whether to show the image view in the mounted filesystem.
    /// </summary>
    public bool ShowImage
    {
        get => _showImage;
        set
        {
            this.RaiseAndSetIfChanged(ref _showImage, value);
            this.RaisePropertyChanged(nameof(HasAnyViewOption));
        }
    }

    /// <summary>
    /// Whether to show the filesystem view in the mounted filesystem.
    /// </summary>
    public bool ShowFileSystem
    {
        get => _showFileSystem;
        set
        {
            this.RaiseAndSetIfChanged(ref _showFileSystem, value);
            this.RaisePropertyChanged(nameof(HasAnyViewOption));
        }
    }

    /// <summary>
    /// Whether to show the system view in the mounted filesystem.
    /// </summary>
    public bool ShowSystem
    {
        get => _showSystem;
        set
        {
            this.RaiseAndSetIfChanged(ref _showSystem, value);
            this.RaisePropertyChanged(nameof(HasAnyViewOption));
        }
    }

    /// <summary>
    /// Whether to mount in update mode. Mutually exclusive with ShowImage/ShowFileSystem/ShowSystem.
    /// When enabled, disables the other view options.
    /// </summary>
    public bool UpdateMode
    {
        get => _updateMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _updateMode, value);
            this.RaisePropertyChanged(nameof(ViewOptionsEnabled));
            this.RaisePropertyChanged(nameof(HasAnyViewOption));
            if (value)
            {
                // Update mode is mutually exclusive with view options
                ShowImage = false;
                ShowFileSystem = false;
                ShowSystem = false;
            }
        }
    }

    /// <summary>
    /// Whether to allow other users to access the mount (Linux FUSE allow_other option).
    /// Automatically enabled when UID or GID is set.
    /// </summary>
    public bool AllowOther
    {
        get => _allowOther;
        set => this.RaiseAndSetIfChanged(ref _allowOther, value);
    }

    /// <summary>
    /// Override file owner UID in the mount (Linux only). Accepts a numeric ID or username.
    /// When set, AllowOther is automatically enabled.
    /// </summary>
    public string UidText
    {
        get => _uidText;
        set
        {
            this.RaiseAndSetIfChanged(ref _uidText, value);
            if (!string.IsNullOrWhiteSpace(value))
                AllowOther = true;
        }
    }

    /// <summary>
    /// Override group owner GID in the mount (Linux only). Accepts a numeric ID or group name.
    /// When set, AllowOther is automatically enabled.
    /// </summary>
    public string GidText
    {
        get => _gidText;
        set
        {
            this.RaiseAndSetIfChanged(ref _gidText, value);
            if (!string.IsNullOrWhiteSpace(value))
                AllowOther = true;
        }
    }

    /// <summary>
    /// Whether the current platform is Linux (controls visibility of FUSE-specific options).
    /// </summary>
    public bool IsLinux { get; } = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Linux);

    /// <summary>
    /// Parsed UID value, or null if not set or invalid.
    /// </summary>
    public uint? ParsedUid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_uidText)) return null;
            return uint.TryParse(_uidText, out uint uid) ? uid : null;
        }
    }

    /// <summary>
    /// Parsed GID value, or null if not set or invalid.
    /// </summary>
    public uint? ParsedGid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_gidText)) return null;
            return uint.TryParse(_gidText, out uint gid) ? gid : null;
        }
    }

    /// <summary>
    /// Whether the view options (ShowImage, ShowFileSystem, ShowSystem) are enabled.
    /// Disabled when UpdateMode is active or when the platform is unsupported.
    /// </summary>
    public bool ViewOptionsEnabled => !UpdateMode && IsPlatformSupported;

    /// <summary>
    /// Whether at least one view option or UpdateMode is selected.
    /// Required for the ConfirmCommand to be enabled.
    /// </summary>
    public bool HasAnyViewOption => ShowImage || ShowFileSystem || ShowSystem || UpdateMode;

    /// <summary>
    /// Whether the current platform supports virtual filesystem mounting.
    /// When false, the Mount button is disabled and a warning is shown.
    /// </summary>
    public bool IsPlatformSupported
    {
        get => _isPlatformSupported;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPlatformSupported, value);
            this.RaisePropertyChanged(nameof(ViewOptionsEnabled));
            this.RaisePropertyChanged(nameof(IsMountConfigurationEnabled));
        }
    }

    /// <summary>
    /// Whether mount configuration controls (mount point, browse, view options) are enabled.
    /// Disabled when the platform is unsupported.
    /// </summary>
    public bool IsMountConfigurationEnabled => IsPlatformSupported;

    /// <summary>
    /// Human-readable reason why the platform is unsupported (e.g., "Dokan driver not installed").
    /// Null when the platform is supported.
    /// </summary>
    public string? PlatformUnsupportedReason
    {
        get => _platformUnsupportedReason;
        set => this.RaiseAndSetIfChanged(ref _platformUnsupportedReason, value);
    }

    /// <summary>
    /// Error message displayed in the dialog (e.g., mount point validation errors).
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// The name of the set being mounted (display only).
    /// </summary>
    public string SetName { get; init; } = "";

    /// <summary>
    /// Command to browse for a mount point directory via folder picker.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseCommand { get; }

    /// <summary>
    /// Command to confirm the mount operation. Enabled when platform is supported
    /// and mount point is not empty.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }

    /// <summary>
    /// Command to cancel and close the dialog without mounting.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>
    /// Interaction to show a folder picker dialog for selecting the mount point.
    /// The View registers a handler that returns the selected folder path, or null if cancelled.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFolderPicker { get; } = new();

    /// <summary>
    /// Raised when the dialog should close. Output is true if confirmed, false if cancelled.
    /// </summary>
    public Interaction<bool, RxVoid> CloseDialog { get; } = new();

    /// <summary>
    /// The most-recently-used mount path history for autocomplete suggestions.
    /// </summary>
    public IReadOnlyList<string> MountPathHistory => _configService.MountPathHistory;

    /// <summary>
    /// Creates a new MountDialogViewModel that queries the given mount service for platform support.
    /// </summary>
    /// <param name="mountService">The mount service used to detect platform support and validate mount points.</param>
    /// <param name="configService">The config service used to retrieve mount path history.</param>
    /// <param name="activeMountPoints">Collection of currently active mount point paths for conflict detection.</param>
    public MountDialogViewModel(IMountService mountService, IConfigService configService, IReadOnlyCollection<string>? activeMountPoints = null)
    {
        _mountService = mountService;
        _configService = configService;
        _activeMountPoints = activeMountPoints ?? Array.Empty<string>();

        // Pre-fill mount point with the most recent history entry
        if (_configService.MountPathHistory.Count > 0)
            _mountPoint = _configService.MountPathHistory[0];

        // Pre-fill Linux options from last-used config
        _allowOther = _configService.MountAllowOther;
        _uidText = _configService.MountUid;
        _gidText = _configService.MountGid;

        // Query platform support on initialization before the dialog becomes interactive
        IsPlatformSupported = mountService.IsPlatformSupported;
        PlatformUnsupportedReason = mountService.PlatformUnsupportedReason;

        // BrowseCommand: always enabled, opens folder picker
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseForMountPointAsync);

        // ConfirmCommand: enabled when platform is supported AND mount point is not empty AND at least one view option is checked
        IObservable<bool> canConfirm = this.WhenAnyValue(
                x => x.IsPlatformSupported,
                x => x.MountPoint,
                x => x.HasAnyViewOption,
                (supported, path, hasOption) => supported && !string.IsNullOrWhiteSpace(path) && hasOption)
            .DistinctUntilChanged();

        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, canConfirm);

        // CancelCommand: always enabled
        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
    }

    private async Task BrowseForMountPointAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
        {
            MountPoint = path;
        }
    }

    private async Task ConfirmAsync()
    {
        ErrorMessage = null;

        // Validate mount point before closing
        try
        {
            (bool isValid, string? error) = _mountService.ValidateMountPoint(MountPoint, _activeMountPoints);
            if (!isValid)
            {
                ErrorMessage = error;
                return;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Validation failed: {ex.Message}";
            return;
        }

        await CloseDialog.Handle(true);
    }

    private async Task CancelAsync() => await CloseDialog.Handle(false);
}