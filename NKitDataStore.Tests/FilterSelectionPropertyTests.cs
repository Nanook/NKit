using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator filter reapplication selection.
///
/// Feature: stable-image-list, Property 15: Filter Reapplication Preserves Selection for Visible Rows
///
/// **Validates: Requirements 6.2**
///
/// For any selection set and any filter change, the resulting selection set SHALL equal
/// the intersection of the original selection set and the rows that pass the new filter.
/// Since SelectionTracker isn't implemented yet, this tests at the collection level —
/// verifying that selected rows which pass the new filter remain in the collection
/// (and thus can be re-selected), while selected rows that don't pass the filter are removed.
/// </summary>
public class FilterSelectionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public FilterSelectionPropertyTests()
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
    /// **Validates: Requirements 6.2**
    ///
    /// Property 15: Filter Reapplication Preserves Selection for Visible Rows.
    /// For any collection with a random selection set and a new filter predicate,
    /// after ReapplyFilter the rows that were selected AND pass the new filter
    /// SHALL remain in the collection (preserving selection eligibility),
    /// while selected rows that don't pass the new filter SHALL be removed.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReapplyFilter_SelectedRowsPassingNewFilter_RemainInCollection(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt filterThresholdSeed)
    {
        // Generate a collection of 2 to 20 rows
        int rowCount = (rowCountSeed.Get % 19) + 2;

        // Create all rows with varying sizes
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
        }

        // Initial filter: accept all rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>(allRows);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Generate a random selection set using the mask bits
        int mask = selectionMask.Get;
        HashSet<ImageRowViewModel> selectedRows = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedRows.Add(allRows[i]);
        }

        // New filter: only rows with Size <= threshold pass
        long threshold = ((long)(filterThresholdSeed.Get % rowCount) + 1) * 100;
        Func<ImageRowViewModel, bool> newFilter = r => r.Size <= threshold;

        // Act: reapply filter
        mutator.ReapplyFilter(allRows, newFilter);

        // Expected: selected rows that pass the new filter should remain in collection
        HashSet<object> expectedVisibleSelected = selectedRows.Where(r => newFilter(r)).ToHashSet(ReferenceEqualityComparer.Instance);

        // Assert: all selected rows that pass the filter are still in the collection
        foreach (object row in expectedVisibleSelected)
        {
            if (!collection.Contains(row))
                return false;
        }

        // Assert: selected rows that DON'T pass the filter are NOT in the collection
        HashSet<object> expectedRemovedSelected = selectedRows.Where(r => !newFilter(r)).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (object row in expectedRemovedSelected)
        {
            if (collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 6.2**
    ///
    /// Property 15: Filter Reapplication Preserves Selection for Visible Rows.
    /// The set of originally-selected rows remaining in the collection after filter
    /// reapplication SHALL equal exactly the intersection of the original selection
    /// and the set of all rows passing the new filter.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReapplyFilter_ResultingSelectionEqualsIntersectionOfSelectionAndFilter(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt moduloSeed)
    {
        // Generate a collection of 2 to 25 rows
        int rowCount = (rowCountSeed.Get % 24) + 2;

        // Create all rows with varying sizes
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
        }

        // Initial filter: accept all rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>(allRows);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Generate a random selection set using the mask bits
        int mask = selectionMask.Get;
        HashSet<ImageRowViewModel> selectedRows = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedRows.Add(allRows[i]);
        }

        // New filter: rows whose Id is NOT divisible by modulo (varied filter)
        int modulo = (moduloSeed.Get % 4) + 2; // 2..5
        Func<ImageRowViewModel, bool> newFilter = r => r.Id % modulo != 0;

        // Act: reapply filter
        mutator.ReapplyFilter(allRows, newFilter);

        // Compute expected intersection: selected rows that pass the new filter
        HashSet<object> expectedIntersection = selectedRows
            .Where(r => newFilter(r))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        // Compute actual: originally-selected rows that are still in the collection
        HashSet<ImageRowViewModel> collectionSet = new HashSet<ImageRowViewModel>(collection, ReferenceEqualityComparer.Instance);
        HashSet<object> actualSelectedInCollection = selectedRows
            .Where(r => collectionSet.Contains(r))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        // Assert: the sets are equal
        return expectedIntersection.SetEquals(actualSelectedInCollection);
    }

    /// <summary>
    /// **Validates: Requirements 6.2**
    ///
    /// Property 15: Filter Reapplication Preserves Selection for Visible Rows.
    /// When the filter changes from restrictive to permissive (more rows pass),
    /// previously selected rows that were visible before SHALL remain in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReapplyFilter_FromRestrictiveToPermissive_PreservesVisibleSelectedRows(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt thresholdSeed)
    {
        // Generate a collection of 3 to 20 rows
        int rowCount = (rowCountSeed.Get % 18) + 3;

        // Create all rows
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
        }

        // Initial restrictive filter: only rows with Size <= initial threshold
        long initialThreshold = ((long)((thresholdSeed.Get % (rowCount / 2)) + 1)) * 100;
        Func<ImageRowViewModel, bool> initialFilter = r => r.Size <= initialThreshold;

        // Build collection with initial filter applied
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        foreach (ImageRowViewModel row in allRows)
        {
            if (initialFilter(row))
                collection.Add(row);
        }

        StableCollectionMutator mutator = new StableCollectionMutator(collection, initialFilter, null);

        // Generate selection from rows currently in the collection
        int mask = selectionMask.Get;
        HashSet<ImageRowViewModel> selectedRows = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < collection.Count; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedRows.Add(collection[i]);
        }

        // New permissive filter: accept all rows (broader than initial)
        Func<ImageRowViewModel, bool> newFilter = _ => true;

        // Act: reapply filter (should add rows, not remove any)
        mutator.ReapplyFilter(allRows, newFilter);

        // Assert: all previously selected rows (which were visible) remain in collection
        foreach (ImageRowViewModel row in selectedRows)
        {
            if (!collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 6.2**
    ///
    /// Property 15: Filter Reapplication Preserves Selection for Visible Rows.
    /// With a sorted collection, after filter reapplication the intersection of
    /// selection and visible rows is preserved, and the collection remains sorted.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReapplyFilter_WithSortedCollection_PreservesSelectionAndSortOrder(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt filterThresholdSeed)
    {
        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        // Generate a collection of 2 to 20 rows
        int rowCount = (rowCountSeed.Get % 19) + 2;

        // Create all rows (already sorted by size since size = (i+1)*100)
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
        }

        // Initial filter: accept all
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>(allRows);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Generate a random selection set
        int mask = selectionMask.Get;
        HashSet<ImageRowViewModel> selectedRows = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedRows.Add(allRows[i]);
        }

        // New filter: only rows with Size <= threshold
        long threshold = ((long)(filterThresholdSeed.Get % rowCount) + 1) * 100;
        Func<ImageRowViewModel, bool> newFilter = r => r.Size <= threshold;

        // Act: reapply filter
        mutator.ReapplyFilter(allRows, newFilter);

        // Assert: selected rows passing new filter remain in collection
        HashSet<object> expectedVisibleSelected = selectedRows
            .Where(r => newFilter(r))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        foreach (object row in expectedVisibleSelected)
        {
            if (!collection.Contains(row))
                return false;
        }

        // Assert: collection remains sorted
        for (int i = 1; i < collection.Count; i++)
        {
            if (comparer(collection[i - 1], collection[i]) > 0)
                return false;
        }

        return true;
    }
}