using Avalonia.Controls;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Preserves DataGrid selection state across collection mutations.
/// Tracks selection by row identity (reference) rather than index.
/// </summary>
public class SelectionTracker
{
    /// <summary>
    /// Captures the current selection set before a mutation.
    /// Uses reference equality so that identity (not value equality) determines membership.
    /// </summary>
    public IReadOnlySet<ImageRowViewModel> CaptureSelection(
        IEnumerable<ImageRowViewModel> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        return new HashSet<ImageRowViewModel>(selectedItems, ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Restores selection after a mutation, selecting only rows that
    /// are still present in the collection.
    /// </summary>
    public void RestoreSelection(
        DataGrid dataGrid,
        IReadOnlySet<ImageRowViewModel> previousSelection,
        ObservableCollection<ImageRowViewModel> currentCollection)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(previousSelection);
        ArgumentNullException.ThrowIfNull(currentCollection);

        dataGrid.SelectedItems.Clear();

        foreach (ImageRowViewModel row in currentCollection)
        {
            if (previousSelection.Contains(row))
            {
                dataGrid.SelectedItems.Add(row);
            }
        }
    }
}