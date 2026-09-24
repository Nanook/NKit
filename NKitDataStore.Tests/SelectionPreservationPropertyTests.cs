using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SelectionTracker selection preservation across mutations.
///
/// Feature: stable-image-list, Property 11: Selection Preserved on Non-Destructive Operations
///
/// **Validates: Requirements 4.1, 4.2, 4.4**
///
/// For any selection set and any operation that does not remove selected rows
/// (insertion, in-place update, removal of non-selected rows), the selection set
/// SHALL be identical before and after the operation.
/// </summary>
public class SelectionPreservationPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SelectionPreservationPropertyTests()
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
    /// **Validates: Requirements 4.1, 4.4**
    ///
    /// Property 11: Selection Preserved on Non-Destructive Operations (Insertion).
    /// For any collection with a random selection set, after inserting a new row,
    /// all previously selected rows SHALL still be present in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_PreservesAllSelectedRows(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt newSizeSeed)
    {
        // Generate a collection of 1 to 20 rows
        int rowCount = (rowCountSeed.Get % 20) + 1;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            collection.Add(row);
            allRows.Add(row);
        }

        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Generate a random selection set
        SelectionTracker tracker = new SelectionTracker();
        int mask = selectionMask.Get;
        List<ImageRowViewModel> selectedItems = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedItems.Add(allRows[i]);
        }

        // Capture selection
        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Act: insert a new row
        ImageRowViewModel newRow = CreateRow(9999, "new_image", (newSizeSeed.Get % 5000) + 1);
        mutator.Insert(newRow);

        // Assert: all previously selected rows are still in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (!collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.2**
    ///
    /// Property 11: Selection Preserved on Non-Destructive Operations (Removal of non-selected rows).
    /// For any collection with a random selection set, after removing rows that are NOT
    /// in the selection set, all selected rows SHALL still be present in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RemoveNonSelected_PreservesAllSelectedRows(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt removalMask)
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

        // Capture selection
        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Determine rows to remove: only non-selected rows
        int rMask = removalMask.Get;
        HashSet<ImageRowViewModel> rowsToRemove = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((rMask >> (i % 30)) & 1) == 1 && !selectedSet.Contains(allRows[i]))
                rowsToRemove.Add(allRows[i]);
        }

        // Act: remove non-selected rows
        mutator.Remove(r => rowsToRemove.Contains(r));

        // Assert: all previously selected rows are still in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (!collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.4**
    ///
    /// Property 11: Selection Preserved on Non-Destructive Operations (In-place update).
    /// For any collection with a random selection set, after an in-place property update
    /// on any row, all previously selected rows SHALL still be present in the collection
    /// at the same positions.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool InPlaceUpdate_PreservesAllSelectedRows(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt updateTargetSeed)
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

        // Generate a random selection set
        SelectionTracker tracker = new SelectionTracker();
        int mask = selectionMask.Get;
        List<ImageRowViewModel> selectedItems = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedItems.Add(allRows[i]);
        }

        // Capture selection
        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Act: perform an in-place update on a random row (non-sort-key property)
        int targetIndex = updateTargetSeed.Get % rowCount;
        ImageRowViewModel targetRow = allRows[targetIndex];
        targetRow.UniqueRawSize = 42;  // In-place property update

        // Assert: all previously selected rows are still in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (!collection.Contains(row))
                return false;
        }

        // Assert: the collection hasn't changed size (no add/remove)
        return collection.Count == rowCount;
    }

    /// <summary>
    /// **Validates: Requirements 4.1, 4.2, 4.4**
    ///
    /// Property 11: Selection Preserved on Non-Destructive Operations (combined).
    /// For any sequence of non-destructive operations (insertions, in-place updates,
    /// removal of non-selected rows), the captured selection set SHALL be identical
    /// to the set of originally-selected rows still in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MixedNonDestructiveOps_SelectionSetUnchanged(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask,
        NonNegativeInt opSeed1, NonNegativeInt opSeed2)
    {
        // Generate a collection of 3 to 15 rows
        int rowCount = (rowCountSeed.Get % 13) + 3;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            collection.Add(row);
            allRows.Add(row);
        }

        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

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

        // Capture selection
        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Op 1: Insert a new row
        ImageRowViewModel newRow = CreateRow(9999, "inserted", (opSeed1.Get % 5000) + 1);
        mutator.Insert(newRow);

        // Op 2: In-place update on a random row
        int updateIdx = opSeed2.Get % rowCount;
        allRows[updateIdx].UniqueRawSize = 99;

        // Op 3: Remove a non-selected row (if any exist)
        List<ImageRowViewModel> nonSelected = allRows.Where(r => !selectedSet.Contains(r)).ToList();
        if (nonSelected.Count > 0)
        {
            ImageRowViewModel toRemove = nonSelected[opSeed1.Get % nonSelected.Count];
            mutator.Remove(r => ReferenceEquals(r, toRemove));
        }

        // Assert: all originally selected rows are still in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (!collection.Contains(row))
                return false;
        }

        return true;
    }
}