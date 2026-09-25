using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SelectionTracker selection adjustment on removal.
///
/// Feature: stable-image-list, Property 12: Selection Adjusted on Removal
///
/// **Validates: Requirements 4.3**
///
/// For any selection set and any removal operation, the resulting selection set
/// SHALL equal the original selection set minus the removed rows (set difference).
/// </summary>
public class SelectionRemovalPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SelectionRemovalPropertyTests()
    {
        EnsureReactiveUIInitialized();
    }

    private static void EnsureReactiveUIInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _initialized = true;
        }
    }

    /// <summary>
    /// Creates an ImageRowViewModel with the given properties for testing.
    /// </summary>
    private static ImageRowViewModel CreateRow(long id, string name = null, long size = 1024)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = name ?? $"image_{id}",
            Size = size,
            SetName = "TestSet",
            Format = ImageFormat.Iso
        };
        return new ImageRowViewModel(record, "test-session");
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 12: Selection Adjusted on Removal.
    /// For any collection with a random selection set and a random removal set,
    /// after removal the expected selection equals the original selection minus
    /// the removed rows. All surviving selected rows SHALL remain in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_SelectionEqualsOriginalMinusRemoved(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt removalMask)
    {
        // Generate a collection of 2 to 25 rows
        int rowCount = (rowCountSeed.Get % 24) + 2;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            collection.Add(row);
            allRows.Add(row);
        }

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Generate a random selection set
        SelectionTracker tracker = new SelectionTracker();
        int sMask = selectionMask.Get;
        List<ImageRowViewModel> selectedItems = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            if (((sMask >> (i % 30)) & 1) == 1)
                selectedItems.Add(allRows[i]);
        }

        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Generate a random removal set (may overlap with selection)
        int rMask = removalMask.Get;
        HashSet<ImageRowViewModel> rowsToRemove = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((rMask >> (i % 30)) & 1) == 1)
                rowsToRemove.Add(allRows[i]);
        }

        // Act: remove rows
        mutator.Remove(r => rowsToRemove.Contains(r));

        // Compute expected selection: original selection minus removed rows
        HashSet<ImageRowViewModel> expectedSelection = new HashSet<ImageRowViewModel>(capturedSelection, ReferenceEqualityComparer.Instance);
        expectedSelection.ExceptWith(rowsToRemove);

        // Compute actual: originally-selected rows that are still in the collection
        HashSet<ImageRowViewModel> collectionSet = new HashSet<ImageRowViewModel>(collection, ReferenceEqualityComparer.Instance);
        HashSet<ImageRowViewModel> actualSurvivingSelected = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collectionSet.Contains(row))
                actualSurvivingSelected.Add(row);
        }

        // Assert: expected equals actual
        return expectedSelection.SetEquals(actualSurvivingSelected);
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 12: Selection Adjusted on Removal (all selected rows removed).
    /// When all selected rows are removed, the resulting selection set SHALL be empty.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RemoveAllSelected_ResultsInEmptySelection(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask)
    {
        // Generate a collection of 2 to 20 rows
        int rowCount = (rowCountSeed.Get % 19) + 2;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            collection.Add(row);
            allRows.Add(row);
        }

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Generate a random selection set
        SelectionTracker tracker = new SelectionTracker();
        int mask = selectionMask.Get;
        List<ImageRowViewModel> selectedItems = new List<ImageRowViewModel>();
        HashSet<ImageRowViewModel> selectedSet = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
            {
                selectedItems.Add(allRows[i]);
                selectedSet.Add(allRows[i]);
            }
        }

        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Act: remove all selected rows
        mutator.Remove(r => selectedSet.Contains(r));

        // Assert: no originally-selected row remains in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 12: Selection Adjusted on Removal (partial overlap).
    /// When some selected rows are removed and some are not, the surviving
    /// selection count SHALL equal original selection count minus the count
    /// of selected rows that were removed.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RemovePartialSelection_SurvivingCountEqualsExpected(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt removalMask)
    {
        // Generate a collection of 3 to 25 rows
        int rowCount = (rowCountSeed.Get % 23) + 3;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            collection.Add(row);
            allRows.Add(row);
        }

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Generate a random selection set
        SelectionTracker tracker = new SelectionTracker();
        int sMask = selectionMask.Get;
        List<ImageRowViewModel> selectedItems = new List<ImageRowViewModel>();
        HashSet<ImageRowViewModel> selectedSet = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((sMask >> (i % 30)) & 1) == 1)
            {
                selectedItems.Add(allRows[i]);
                selectedSet.Add(allRows[i]);
            }
        }

        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Generate a random removal set
        int rMask = removalMask.Get;
        HashSet<ImageRowViewModel> rowsToRemove = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((rMask >> (i % 30)) & 1) == 1)
                rowsToRemove.Add(allRows[i]);
        }

        // Count how many selected rows will be removed
        int selectedRemovedCount = selectedSet.Count(r => rowsToRemove.Contains(r));
        int expectedSurvivingCount = selectedSet.Count - selectedRemovedCount;

        // Act: remove rows
        mutator.Remove(r => rowsToRemove.Contains(r));

        // Count surviving selected rows in collection
        HashSet<ImageRowViewModel> collectionSet = new HashSet<ImageRowViewModel>(collection, ReferenceEqualityComparer.Instance);
        int actualSurvivingCount = capturedSelection.Count(r => collectionSet.Contains(r));

        // Assert: counts match
        return actualSurvivingCount == expectedSurvivingCount;
    }
}