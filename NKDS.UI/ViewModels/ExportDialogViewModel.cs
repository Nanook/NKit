using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Export dialog. Displays a per-system format grid
/// and resolves per-image target formats on confirm.
/// </summary>
public class ExportDialogViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private string _outputDirectory = "";
    private int _selectedImageCount;

    /// <summary>
    /// The output directory where exported images will be written.
    /// </summary>
    public string OutputDirectory
    {
        get => _outputDirectory;
        set => this.RaiseAndSetIfChanged(ref _outputDirectory, value);
    }

    /// <summary>
    /// Number of images selected for export. Displayed as informational text in the dialog.
    /// </summary>
    public int SelectedImageCount
    {
        get => _selectedImageCount;
        set => this.RaiseAndSetIfChanged(ref _selectedImageCount, value);
    }

    /// <summary>
    /// The format rows computed from the selected images.
    /// One row per unique (System, SourceFormat) combination.
    /// </summary>
    public IReadOnlyList<FormatRowViewModel> FormatRows { get; private set; } = [];

    /// <summary>
    /// Interaction to show a folder picker for selecting the output directory.
    /// The View registers a handler that returns the selected folder path, or null if cancelled.
    /// </summary>
    public Interaction<RxVoid, string?> ShowFolderPicker { get; } = new();

    /// <summary>
    /// Interaction to close the dialog. The View registers a handler that closes the window
    /// with the specified boolean result (true = confirmed, false = cancelled).
    /// </summary>
    public Interaction<bool, RxVoid> CloseDialog { get; } = new();

    /// <summary>
    /// The most-recently-used export output path history for the combo box dropdown.
    /// </summary>
    public IReadOnlyList<string> ExportPathHistory => _configService.ExportPathHistory;

    /// <summary>
    /// Command to browse for an output directory. Opens the folder picker interaction.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseCommand { get; }

    /// <summary>
    /// Command to confirm the export. Enabled only when OutputDirectory is non-empty.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }

    /// <summary>
    /// Command to cancel the dialog without exporting.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    public ExportDialogViewModel(IConfigService configService)
    {
        _configService = configService;

        // Pre-fill output directory with the most recent export path
        if (_configService.ExportPathHistory.Count > 0)
            _outputDirectory = _configService.ExportPathHistory[0];

        BrowseCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseAsync);

        IObservable<bool> canConfirm = this.WhenAnyValue(x => x.OutputDirectory)
            .Select(dir => !string.IsNullOrWhiteSpace(dir));
        ConfirmCommand = ReactiveCommand.CreateFromTask(ExecuteConfirmAsync, canConfirm);
        CancelCommand = ReactiveCommand.CreateFromTask(ExecuteCancelAsync);
    }

    /// <summary>
    /// Initializes FormatRows from the selected images. Called by MainWindowViewModel before showing the dialog.
    /// Computes distinct (System, SourceFormat) pairs, loads persisted preferences, and creates
    /// a FormatRowViewModel per pair with the persisted default (if valid).
    /// Subscribes to target format changes to reload persisted options when the user switches formats.
    /// </summary>
    public void InitializeFromImages(IReadOnlyList<ImageRowViewModel> selectedImages)
    {
        List<(string System, string SourceFormat)> distinctPairs = selectedImages
            .Select(img => (System: img.System ?? "Unknown", SourceFormat: img.Format.ToString().ToLowerInvariant()))
            .Distinct()
            .ToList();

        FormatRows = distinctPairs.Select(pair =>
        {
            IReadOnlyList<string> targetFormats = FormatMappings.GetTargetFormats(pair.System, pair.SourceFormat);
            string? persisted = _configService.GetExportFormat(pair.System, pair.SourceFormat);
            FormatRowViewModel row = new FormatRowViewModel(pair.System, pair.SourceFormat, targetFormats, persisted);

            // Load persisted format-specific options for the row's current format combination
            FormatOptionsEntry? options = _configService.GetFormatOptions(row.System, row.SourceFormat, row.SelectedTargetFormat);
            row.LoadOptions(options);

            // Subscribe to target format changes to load persisted options for the new format.
            // Skip(1) avoids triggering on the initial value which is already loaded above.
            row.WhenAnyValue(r => r.SelectedTargetFormat)
                .Skip(1)
                .Subscribe(newTargetFormat =>
                {
                    FormatOptionsEntry? newOptions = _configService.GetFormatOptions(row.System, row.SourceFormat, newTargetFormat);
                    row.LoadOptions(newOptions);
                });

            return row;
        }).ToList();

        this.RaisePropertyChanged(nameof(FormatRows));
    }

    /// <summary>
    /// Returns the resolved TargetFormat for a given image based on its System + SourceFormat.
    /// Looks up the matching FormatRow and returns its SelectedTargetFormat.
    /// Returns null if no matching row exists (should not happen if InitializeFromImages was called correctly).
    /// </summary>
    public string? ResolveFormatForImage(ImageRowViewModel image)
    {
        string system = image.System ?? "Unknown";
        string sourceFormat = image.Format.ToString().ToLowerInvariant();
        FormatRowViewModel? row = FormatRows.FirstOrDefault(r =>
            string.Equals(r.System, system, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.SourceFormat, sourceFormat, StringComparison.OrdinalIgnoreCase));
        return row?.SelectedTargetFormat;
    }

    /// <summary>
    /// Returns a mapping of image ID → full format string for all provided images.
    /// The format string includes conversion options (e.g., "rvz:zstd:19:128kb:16" instead of just "rvz").
    /// </summary>
    public Dictionary<long, string> GetFormatMapping(IReadOnlyList<ImageRowViewModel> selectedImages)
    {
        Dictionary<long, string> result = new Dictionary<long, string>();
        foreach (ImageRowViewModel image in selectedImages)
        {
            string system = image.System ?? "Unknown";
            string sourceFormat = image.Format.ToString().ToLowerInvariant();
            FormatRowViewModel? row = FormatRows.FirstOrDefault(r =>
                string.Equals(r.System, system, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.SourceFormat, sourceFormat, StringComparison.OrdinalIgnoreCase));
            if (row != null)
                result[image.Id] = row.GetConvertFormatString();
        }
        return result;
    }

    /// <summary>
    /// Persists the current format selections via the config service.
    /// Each row's selection is saved individually so other preferences are preserved.
    /// Also persists format-specific options (encoding, level, block size for RVZ; lossless for CISO/WBFS).
    /// </summary>
    private void PersistPreferences()
    {
        foreach (FormatRowViewModel row in FormatRows)
        {
            if (row.SelectedTargetFormat is null)
                continue;

            _configService.SetExportFormat(row.System, row.SourceFormat, row.SelectedTargetFormat);

            // Persist format-specific options for formats that have them
            string format = row.SelectedTargetFormat.ToLowerInvariant();
            FormatOptionsEntry? entry = format switch
            {
                "rvz" => new FormatOptionsEntry
                {
                    Encoding = row.SelectedEncoding,
                    Level = IsEncodingWithLevel(row.SelectedEncoding) ? row.SelectedLevel : null,
                    BlockSize = row.SelectedBlockSize
                },
                "ciso" or "wbfs" => new FormatOptionsEntry
                {
                    Lossless = row.Lossless
                },
                _ => null
            };

            if (entry is not null)
                _configService.SetFormatOptions(row.System, row.SourceFormat, row.SelectedTargetFormat!, entry);
        }
        // Persist the export output directory to MRU history
        if (!string.IsNullOrWhiteSpace(OutputDirectory))
            _configService.AddExportPath(OutputDirectory);
    }

    private static bool IsEncodingWithLevel(string? encoding) =>
        string.Equals(encoding, "zstd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(encoding, "lzma", StringComparison.OrdinalIgnoreCase);

    private async Task ExecuteBrowseAsync()
    {
        string path = await ShowFolderPicker.Handle(RxVoid.Default);
        if (!string.IsNullOrEmpty(path))
            OutputDirectory = path;
    }

    private async Task ExecuteConfirmAsync()
    {
        PersistPreferences();
        await CloseDialog.Handle(true);
    }

    private async Task ExecuteCancelAsync() => await CloseDialog.Handle(false);
}