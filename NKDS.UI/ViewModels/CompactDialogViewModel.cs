using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Result returned by the Compact Sets dialog.
/// </summary>
public enum CompactDialogResult
{
    /// <summary>User dismissed the dialog without choosing an operation.</summary>
    Cancelled = 0,
    /// <summary>Full compact: rewrites shards and removes orphaned blocks. Irreversible.</summary>
    FullCompact,
    /// <summary>Fast compact: rebuilds the index only. Shard files are never touched.</summary>
    FastCompact
}

/// <summary>
/// ViewModel for the Compact Sets dialog. Displays a checklist of all available sets
/// and offers Fast Compact (index-only, safe) and Full Compact (reclaims shard space,
/// irreversible) as distinct actions.
/// </summary>
public class CompactDialogViewModel : ViewModelBase
{
    private string? _warningMessage;

    /// <summary>Available sets with their checked state.</summary>
    public ObservableCollection<CompactSetItem> Sets { get; }

    /// <summary>The set names that were selected (checked) when the user confirms.</summary>
    public IReadOnlyList<string> SelectedSetNames =>
        this.Sets.Where(s => s.IsChecked).Select(s => s.Name).ToList();

    /// <summary>Warning or informational message displayed in the dialog.</summary>
    public string? WarningMessage
    {
        get => _warningMessage;
        set => this.RaiseAndSetIfChanged(ref _warningMessage, value);
    }

    /// <summary>Perform a full compact (irreversible). Enabled when at least one set is checked.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }

    /// <summary>Perform a fast compact (index-only, safe). Enabled when at least one set is checked.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmFastCommand { get; }

    /// <summary>Cancel and close the dialog without performing any operation.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>Select all sets.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectAllCommand { get; }

    /// <summary>Deselect all sets.</summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectNoneCommand { get; }

    /// <summary>
    /// Creates a new CompactDialogViewModel.
    /// </summary>
    /// <param name="availableSetNames">The set names available in the current DataStore.</param>
    /// <param name="activeSetName">The currently active set name (checked by default).</param>
    public CompactDialogViewModel(IReadOnlyList<string> availableSetNames, string? activeSetName)
    {
        List<string> realSets = availableSetNames
            .Where(n => !string.Equals(n, "All", StringComparison.OrdinalIgnoreCase))
            .ToList();

        this.Sets = new ObservableCollection<CompactSetItem>(
            realSets.Select(name => new CompactSetItem
            {
                Name = name,
                IsChecked = name == activeSetName
            }));

        IObservable<bool> canConfirm = Signal.Merge(
            this.Sets.Select(s => s.WhenAnyValue(x => x.IsChecked)).Merge().Select(_ => RxVoid.Default),
            Signal.Emit(RxVoid.Default)
        ).Select(_ => this.Sets.Any(s => s.IsChecked));

        this.ConfirmCommand = ReactiveCommand.Create(() => { }, canConfirm);
        this.ConfirmFastCommand = ReactiveCommand.Create(() => { }, canConfirm);
        this.CancelCommand = ReactiveCommand.Create(() => { });
        this.SelectAllCommand = ReactiveCommand.Create(() => { foreach (CompactSetItem s in this.Sets) s.IsChecked = true; });
        this.SelectNoneCommand = ReactiveCommand.Create(() => { foreach (CompactSetItem s in this.Sets) s.IsChecked = false; });
    }
}

/// <summary>Represents a single set in the compact checklist.</summary>
public class CompactSetItem : ReactiveObject
{
    private bool _isChecked;

    public string Name { get; init; } = "";

    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }
}
