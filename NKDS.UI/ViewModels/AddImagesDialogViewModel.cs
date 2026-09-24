using Nanook.NKit;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Add Images dialog. Collects file paths and target set selection
/// before invoking the Add operation. The actual NKDS.AddAsync invocation is handled
/// by the MainWindowViewModel (task 10.2) after the dialog returns its result.
/// </summary>
public class AddImagesDialogViewModel : ViewModelBase
{
    private readonly IConfigService? _configService;
    private string? _selectedSetName;

    /// <summary>
    /// The files selected by the user for import.
    /// </summary>
    public ObservableCollection<string> SelectedFiles { get; } = new();

    /// <summary>
    /// The target set name chosen for import. Pre-selected to the active set.
    /// </summary>
    public string? SelectedSetName
    {
        get => _selectedSetName;
        set => this.RaiseAndSetIfChanged(ref _selectedSetName, value);
    }

    /// <summary>
    /// Available set names in the current DataStore (sorted alphabetically).
    /// </summary>
    public IReadOnlyList<string> AvailableSetNames { get; }

    /// <summary>
    /// Interaction to open a file picker dialog for disc image files.
    /// The View registers a handler that returns selected file paths, or empty if cancelled.
    /// </summary>
    public Interaction<RxVoid, IReadOnlyList<string>> ShowFilePicker { get; } = new();

    /// <summary>
    /// Interaction to open a folder picker dialog to select directories containing disc images.
    /// The View registers a handler that returns selected folder paths, or empty if cancelled.
    /// </summary>
    public Interaction<RxVoid, IReadOnlyList<string>> ShowFolderPicker { get; } = new();

    /// <summary>
    /// Command to browse for disc image files. Opens the file picker and adds results to SelectedFiles.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseCommand { get; }

    /// <summary>
    /// Command to browse for a directory. Opens the folder picker and adds all image files found recursively.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> BrowseDirectoryCommand { get; }

    /// <summary>
    /// Command to remove a file from the selected files list.
    /// </summary>
    public ReactiveCommand<string, RxVoid> RemoveFileCommand { get; }

    /// <summary>
    /// Command to confirm the dialog (OK). Enabled when files are selected and a target set is chosen.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }

    /// <summary>
    /// Command to cancel the dialog.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>
    /// Interaction to close the dialog with a result (true = confirmed, false = cancelled).
    /// </summary>
    public Interaction<bool, RxVoid> CloseDialog { get; } = new();

    /// <summary>
    /// The most-recently-used image browse path history for the file picker's suggested start location.
    /// </summary>
    public IReadOnlyList<string> ImageBrowseHistory => _configService?.ImageBrowseHistory ?? [];

    /// <summary>
    /// Disc image file extensions as file picker patterns.
    /// </summary>
    public static IReadOnlyList<string> ImageExtensions => SourceFiles.ImageFilePatterns;

    /// <summary>
    /// Archive file extensions as file picker patterns.
    /// </summary>
    public static IReadOnlyList<string> ArchiveExtensions => SourceFiles.ArchiveFilePatterns;

    /// <summary>
    /// All supported file extensions (images + archives) as file picker patterns.
    /// </summary>
    public static IReadOnlyList<string> SupportedExtensions => SourceFiles.SupportedFilePatterns;

    public AddImagesDialogViewModel(IReadOnlyList<string> availableSetNames, string? activeSetName, IConfigService? configService = null)
    {
        _configService = configService;
        AvailableSetNames = availableSetNames;
        SelectedSetName = activeSetName;

        // BrowseCommand: always enabled, opens file picker and adds files
        BrowseCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseAsync);

        // BrowseDirectoryCommand: always enabled, opens folder picker and adds all child image files
        BrowseDirectoryCommand = ReactiveCommand.CreateFromTask(ExecuteBrowseDirectoryAsync);

        // RemoveFileCommand: removes a specific file from the list
        RemoveFileCommand = ReactiveCommand.Create<string>(path => SelectedFiles.Remove(path));

        // ConfirmCommand: enabled when files are selected AND a target set is chosen
        IObservable<bool> canConfirm = this.WhenAnyValue(
                x => x.SelectedSetName,
                x => x.SelectedFiles.Count,
                (setName, fileCount) => !string.IsNullOrEmpty(setName) && fileCount > 0)
            .DistinctUntilChanged();

        ConfirmCommand = ReactiveCommand.CreateFromTask(
            async () => await CloseDialog.Handle(true),
            canConfirm);

        CancelCommand = ReactiveCommand.CreateFromTask(
            async () => await CloseDialog.Handle(false));

        // Re-evaluate canConfirm when the collection changes
        SelectedFiles.CollectionChanged += (_, _) =>
            this.RaisePropertyChanged(nameof(SelectedFiles) + ".Count");
    }

    private async Task ExecuteBrowseAsync()
    {
        IReadOnlyList<string> files = await ShowFilePicker.Handle(RxVoid.Default);
        if (files.Count > 0)
        {
            // Persist the directory of the first selected file for next time
            string? dir = Path.GetDirectoryName(files[0]);
            if (!string.IsNullOrEmpty(dir))
                _configService?.AddImageBrowsePath(dir);

            foreach (string file in files)
            {
                // Avoid duplicates
                if (!SelectedFiles.Contains(file))
                    SelectedFiles.Add(file);
            }
        }
    }

    private async Task ExecuteBrowseDirectoryAsync()
    {
        IReadOnlyList<string> folders = await ShowFolderPicker.Handle(RxVoid.Default);
        if (folders.Count > 0)
        {
            // Persist the first selected folder for next time
            _configService?.AddImageBrowsePath(folders[0]);

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (!SelectedFiles.Contains(file))
                        SelectedFiles.Add(file);
                }
            }
        }
    }
}