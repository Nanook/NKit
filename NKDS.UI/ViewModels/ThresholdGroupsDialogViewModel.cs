using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Threshold Groups results dialog.
/// </summary>
public class ThresholdGroupsDialogViewModel : ViewModelBase
{
    /// <summary>
    /// The threshold value used for computation (displayed in header).
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Total number of groups found.
    /// </summary>
    public int TotalGroups { get; private set; }

    /// <summary>
    /// Total number of images across all groups.
    /// </summary>
    public int TotalGroupedImages { get; private set; }

    /// <summary>
    /// The computed groups to display.
    /// </summary>
    public IReadOnlyList<ThresholdGroupModel> Groups { get; private set; }
        = Array.Empty<ThresholdGroupModel>();

    /// <summary>
    /// Whether there are no groups (shows informational message).
    /// </summary>
    public bool HasNoGroups => TotalGroups == 0;

    /// <summary>
    /// Header summary text (e.g., "3 groups, 12 images at ≥80.00% threshold").
    /// Uses DialogThreshold so it updates when the user adjusts the threshold.
    /// </summary>
    public string HeaderSummary => HasNoGroups
        ? $"No groups found at \u2265{DialogThreshold:F2}% threshold"
        : $"{TotalGroups} group(s), {TotalGroupedImages} images at \u2265{DialogThreshold:F2}% threshold";

    /// <summary>
    /// Groups wrapped with 1-based numbering and sorted pairwise matches for display.
    /// </summary>
    public IReadOnlyList<NumberedGroupViewModel> NumberedGroups { get; private set; }
        = Array.Empty<NumberedGroupViewModel>();

    // --- New properties for threshold re-filtering and export/import ---

    /// <summary>
    /// The raw pair results held for re-filtering. Not recomputed on threshold change.
    /// </summary>
    public List<(string ImageA, string ImageB, double MatchPercent)> PairResults { get; private set; }
        = new();

    /// <summary>
    /// Image names from the computation (for export metadata and import validation).
    /// </summary>
    public List<string> ImageNames { get; private set; } = new();

    /// <summary>
    /// Scope restriction from the computation (for export metadata).
    /// </summary>
    public ScopeRestriction ScopeRestriction { get; private set; }

    /// <summary>
    /// The adjustable threshold in the dialog. Changes trigger debounced re-filtering.
    /// Clamped to [0, 100] and rounded to 2 decimal places.
    /// </summary>
    private double _dialogThreshold;
    public double DialogThreshold
    {
        get => _dialogThreshold;
        set => this.RaiseAndSetIfChanged(ref _dialogThreshold, ClampThreshold(value));
    }

    /// <summary>
    /// Whether pair results are loaded (enables Export/Export Pairs buttons).
    /// </summary>
    public bool HasPairResults => PairResults?.Count > 0;

    /// <summary>
    /// Warning/error message displayed in the dialog.
    /// </summary>
    private string? _dialogMessage;
    public string? DialogMessage
    {
        get => _dialogMessage;
        set => this.RaiseAndSetIfChanged(ref _dialogMessage, value);
    }

    /// <summary>
    /// Clamps a threshold value to [0, 100] and rounds to 2 decimal places.
    /// </summary>
    internal static double ClampThreshold(double value)
        => Math.Round(Math.Clamp(value, 0, 100), 2);

    // --- End new properties ---

    /// <summary>
    /// Command to export grouped results to a YAML file.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportGroupsCommand { get; }

    /// <summary>
    /// Command to export the full pairwise comparison matrix to a YAML file.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ExportPairsCommand { get; }

    /// <summary>
    /// Command to import a previously exported pairwise matrix from a YAML file.
    /// Always enabled (no CanExecute constraint).
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ImportPairsCommand { get; }

    /// <summary>
    /// Interaction to show a save file dialog. Input is the file extension filter, output is the selected path or null.
    /// </summary>
    public Interaction<string, string?> ShowSaveFileDialog { get; } = new();

    /// <summary>
    /// Interaction to show an open file dialog. Input is the file extension filter, output is the selected path or null.
    /// </summary>
    public Interaction<string, string?> ShowOpenFileDialog { get; } = new();

    /// <summary>
    /// Command to close the dialog window.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    /// <summary>
    /// Raised when the dialog should close.
    /// </summary>
    public Interaction<RxVoid, RxVoid> CloseDialog { get; } = new();

    public ThresholdGroupsDialogViewModel()
    {
        IObservable<bool> canExport = this.WhenAnyValue(x => x.HasPairResults);
        ExportGroupsCommand = ReactiveCommand.CreateFromTask(ExportGroupsAsync, canExport);
        ExportPairsCommand = ReactiveCommand.CreateFromTask(ExportPairsAsync, canExport);
        ImportPairsCommand = ReactiveCommand.CreateFromTask(ImportPairsAsync);

        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await CloseDialog.Handle(RxVoid.Default);
        });

        // Debounced re-filtering: when DialogThreshold changes, wait 300ms of inactivity
        // then re-filter groups from stored PairResults at the new threshold.
        this.WhenAnyValue(x => x.DialogThreshold)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(ReFilterGroups);
    }

    /// <summary>
    /// Exports the current grouped results to a YAML file via save file dialog.
    /// </summary>
    private async Task ExportGroupsAsync()
    {
        string filePath = await ShowSaveFileDialog.Handle(".yaml");
        if (filePath == null) return;

        try
        {
            using StreamWriter writer = new StreamWriter(filePath);
            PairwiseMatrixYamlWriter.WriteGroupedResults(writer, Groups, DialogThreshold);
        }
        catch (IOException ex)
        {
            DialogMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports the full pairwise comparison matrix to a YAML file via save file dialog.
    /// </summary>
    private async Task ExportPairsAsync()
    {
        string filePath = await ShowSaveFileDialog.Handle(".yaml");
        if (filePath == null) return;

        try
        {
            PairwiseMatrixMetadata metadata = new PairwiseMatrixMetadata
            {
                Scope = ScopeRestriction.ToString(),
                ImageCount = ImageNames.Count,
                ComputedAt = DateTime.UtcNow.ToString("o"),
                Images = ImageNames
            };
            using StreamWriter writer = new StreamWriter(filePath);
            PairwiseMatrixYamlWriter.WritePairwiseMatrix(writer, metadata, PairResults);
        }
        catch (IOException ex)
        {
            DialogMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports a pairwise matrix from a YAML file, validates image names, and re-filters groups.
    /// </summary>
    private async Task ImportPairsAsync()
    {
        string filePath = await ShowOpenFileDialog.Handle(".yaml");
        if (filePath == null) return;

        try
        {
            using StreamReader reader = new StreamReader(filePath);
            PairwiseMatrixParseResult result = PairwiseMatrixYamlWriter.ParsePairwiseMatrix(reader);

            // Validate image names
            HashSet<string> importedSet = new HashSet<string>(result.Metadata.Images);
            HashSet<string> currentSet = new HashSet<string>(ImageNames);
            if (!importedSet.SetEquals(currentSet))
            {
                int missing = importedSet.Except(currentSet).Count();
                int extra = currentSet.Except(importedSet).Count();
                DialogMessage = $"Import mismatch: {missing} images missing, {extra} extra images in current session";
            }
            else
            {
                DialogMessage = null;
            }

            PairResults = result.Pairs;
            ImageNames = result.Metadata.Images;
            this.RaisePropertyChanged(nameof(HasPairResults));
            ReFilterGroups(DialogThreshold);
        }
        catch (IOException ex)
        {
            DialogMessage = $"Import failed: {ex.Message}";
        }
        catch (FormatException ex)
        {
            DialogMessage = $"Invalid pairwise matrix format: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-filters groups from stored PairResults at the given threshold.
    /// Called by the debounced DialogThreshold subscription and after import.
    /// </summary>
    private void ReFilterGroups(double threshold)
    {
        if (PairResults == null || PairResults.Count == 0)
            return;

        List<ThresholdGroupModel> groups = ThresholdGroupingService.BuildThresholdGroups(PairResults, threshold);

        List<NumberedGroupViewModel> numberedGroups = groups
            .Select((g, i) => new NumberedGroupViewModel
            {
                GroupNumber = i + 1,
                Group = g,
                SortedPairwiseMatches = g.PairwiseMatches
                    .OrderByDescending(p => p.MatchPercentage)
                    .ToList()
            })
            .ToList();

        Groups = groups;
        NumberedGroups = numberedGroups;
        TotalGroups = groups.Count;
        TotalGroupedImages = groups.Sum(g => g.Images.Count);

        this.RaisePropertyChanged(nameof(Groups));
        this.RaisePropertyChanged(nameof(NumberedGroups));
        this.RaisePropertyChanged(nameof(TotalGroups));
        this.RaisePropertyChanged(nameof(TotalGroupedImages));
        this.RaisePropertyChanged(nameof(HasNoGroups));
        this.RaisePropertyChanged(nameof(HeaderSummary));
    }

    /// <summary>
    /// Creates a ThresholdGroupsDialogViewModel from a ThresholdGroupingResult.
    /// </summary>
    public static ThresholdGroupsDialogViewModel FromResult(ThresholdGroupingResult result)
    {
        List<NumberedGroupViewModel> numberedGroups = result.Groups
            .Select((g, i) => new NumberedGroupViewModel
            {
                GroupNumber = i + 1,
                Group = g,
                SortedPairwiseMatches = g.PairwiseMatches
                    .OrderByDescending(p => p.MatchPercentage)
                    .ToList()
            })
            .ToList();

        ThresholdGroupsDialogViewModel vm = new ThresholdGroupsDialogViewModel
        {
            Threshold = result.Threshold,
            TotalGroups = result.Groups.Count,
            TotalGroupedImages = result.TotalGroupedImages,
            Groups = result.Groups,
            NumberedGroups = numberedGroups,
            PairResults = result.AllPairResults,
            ImageNames = result.ImageNames,
            ScopeRestriction = result.ScopeRestriction
        };
        vm.DialogThreshold = result.Threshold;
        return vm;
    }
}