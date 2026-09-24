using NkdsUi.Models;
using ReactiveUI;
using System.Windows.Input;

namespace NkdsUi.ViewModels;

public class ToolbarItemViewModel : ViewModelBase
{
    private bool _isEnabled = true;
    private bool _isChecked;
    private string _tooltip = "";
    private ICommand? _command;

    private string _label = "";

    public ToolbarOperationKind Kind { get; init; }

    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    public string IconName { get; init; } = "";

    public string Tooltip
    {
        get => _tooltip;
        set => this.RaiseAndSetIfChanged(ref _tooltip, value);
    }

    public int Group { get; init; }

    /// <summary>
    /// Whether to show a divider before this item (first item of a new group).
    /// </summary>
    public bool ShowDividerBefore { get; init; }

    /// <summary>
    /// The command to execute when this toolbar item is clicked.
    /// Assigned by ToolbarViewModel after construction.
    /// </summary>
    public ICommand? Command
    {
        get => _command;
        set => this.RaiseAndSetIfChanged(ref _command, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    /// <summary>
    /// Whether this toolbar item is in its toggled/checked state.
    /// Used for toggle buttons like Group.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }
}