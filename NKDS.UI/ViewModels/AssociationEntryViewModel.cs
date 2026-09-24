using NkdsUi.Models;
using ReactiveUI;

namespace NkdsUi.ViewModels;

/// <summary>
/// ViewModel for a single association entry row in the Settings dialog.
/// Exposes observable state and derived display properties for binding.
/// </summary>
public class AssociationEntryViewModel : ViewModelBase
{
    private AssociationState _state;
    private bool _isLoading;
    private bool _isOperationInProgress;
    private string? _errorMessage;
    private bool _isChecked;

    public AssociationEntryViewModel(AssociationEntry entry)
    {
        Entry = entry;
    }

    /// <summary>The underlying association entry definition.</summary>
    public AssociationEntry Entry { get; }

    /// <summary>Display label for this entry.</summary>
    public string Label => Entry.Label;

    /// <summary>The current registration state of this entry.</summary>
    public AssociationState State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(IsRegistered));
            this.RaisePropertyChanged(nameof(IsStale));
            this.RaisePropertyChanged(nameof(CanToggle));
            this.RaisePropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>True while the initial state query is in progress.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>True while a register/unregister operation is in progress.</summary>
    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOperationInProgress, value);
            this.RaisePropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>Error message from the last failed query or operation, or null if no error.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>
    /// The desired checked state (bound to the checkbox).
    /// After initialization, this reflects the current OS state.
    /// The user toggles it, and Apply processes the difference.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            this.RaiseAndSetIfChanged(ref _isChecked, value);
            this.RaisePropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>True when the checkbox state differs from the current OS registration state.</summary>
    public bool IsDirty => IsChecked != (State == AssociationState.Registered || State == AssociationState.Stale);

    // --- Derived display properties ---

    /// <summary>True when the entry is registered and points to the current executable.</summary>
    public bool IsRegistered => State == AssociationState.Registered;

    /// <summary>True when the entry is registered but points to a different executable path.</summary>
    public bool IsStale => State == AssociationState.Stale;

    /// <summary>True when the toggle control should be enabled (no loading, no operation, no error).</summary>
    public bool CanToggle => !IsLoading && !IsOperationInProgress && ErrorMessage == null;
}