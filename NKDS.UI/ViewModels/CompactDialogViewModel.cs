using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for the Compact dialog. Displays a checklist of all available sets,
/// allowing the user to select one or more sets to compact.
/// </summary>
public class CompactDialogViewModel : ViewModelBase
{
    private string? _warningMessage;

    /// <summary>
    /// Available sets with their checked state.
    /// </summary>
    public ObservableCollection<CompactSetItem> Sets { get; }

    /// <summary>
    /// The set names that were selected (checked) when the user confirms.
    /// </summary>
    public IReadOnlyList<string> SelectedSetNames =>
        Sets.Where(s => s.IsChecked).Select(s => s.Name).ToList();

    /// <summary>
    /// Warning or informational message displayed in the dialog.
    /// </summary>
    public string? WarningMessage
    {
        get => _warningMessage;
        set => this.RaiseAndSetIfChanged(ref _warningMessage, value);
    }

    /// <summary>
    /// Command to confirm the compact operation. Enabled when at least one set is checked.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> ConfirmCommand { get; }

    /// <summary>
    /// Command to cancel and close the dialog without performing any operation.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    /// <summary>
    /// Command to select all sets.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectAllCommand { get; }

    /// <summary>
    /// Command to deselect all sets.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> SelectNoneCommand { get; }

    /// <summary>
    /// Creates a new CompactDialogViewModel.
    /// </summary>
    /// <param name="availableSetNames">The set names available in the current DataStore.</param>
    /// <param name="activeSetName">The currently active set name (checked by default).</param>
    public CompactDialogViewModel(IReadOnlyList<string> availableSetNames, string? activeSetName)
    {
        // Filter out the "All" pseudo-entry from the set list
        List<string> realSets = availableSetNames.Where(n => !string.Equals(n, "All", StringComparison.OrdinalIgnoreCase)).ToList();

        Sets = new ObservableCollection<CompactSetItem>(
            realSets.Select(name => new CompactSetItem
            {
                Name = name,
                IsChecked = name == activeSetName
            }));

        IObservable<bool> canConfirm = Sets.ToObservable()
            .Select(_ => Sets.Any(s => s.IsChecked))
            .StartWith(Sets.Any(s => s.IsChecked));

        // Re-evaluate canConfirm when any item's IsChecked changes
        canConfirm = Signal.Merge(
            Sets.Select(s => s.WhenAnyValue(x => x.IsChecked)).Merge().Select(_ => RxVoid.Default),
            Signal.Emit(RxVoid.Default)
        ).Select(_ => Sets.Any(s => s.IsChecked));

        ConfirmCommand = ReactiveCommand.Create(() => { }, canConfirm);
        CancelCommand = ReactiveCommand.Create(() => { });
        SelectAllCommand = ReactiveCommand.Create(() => { foreach (CompactSetItem s in Sets) s.IsChecked = true; });
        SelectNoneCommand = ReactiveCommand.Create(() => { foreach (CompactSetItem s in Sets) s.IsChecked = false; });
    }
}

/// <summary>
/// Represents a single set in the compact checklist.
/// </summary>
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