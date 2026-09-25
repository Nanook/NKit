using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator targeted removal.
///
/// Feature: stable-image-list, Property 7: Targeted Removal Preserves Other Rows
///
/// **Validates: Requirements 1.3, 7.1**
///
/// For any collection and any subset of rows to remove, after removal the remaining
/// rows SHALL appear in the same relative order and at contiguous indices, and no row
/// outside the removal set SHALL be affected.
/// </summary>
public class TargetedRemovalPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public TargetedRemovalPropertyTests()
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
    /// **Validates: Requirements 1.3, 7.1**
    ///
    /// Property 7: Targeted Removal Preserves Other Rows.
    /// For any collection and any removal predicate (based on even/odd Id),
    /// after removal the remaining rows SHALL appear in the same relative order
    /// and no row outside the removal set SHALL be affected.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_PreservesRelativeOrderOfRemainingRows(
        NonNegativeInt collectionSizeSeed, NonNegativeInt moduloSeed)
    {
        // Generate a collection of 0 to 30 rows
        int collectionSize = collectionSizeSeed.Get % 31;
        // Use modulo to determine which rows to remove (mod 2..5)
        int modulo = (moduloSeed.Get % 4) + 2;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        // Predicate: remove rows whose Id is divisible by modulo
        Func<ImageRowViewModel, bool> removalPredicate = r => r.Id % modulo == 0;

        // Determine expected survivors (rows NOT matching predicate) in original order
        List<ImageRowViewModel> expectedSurvivors = allRows.Where(r => !removalPredicate(r)).ToList();

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Act
        mutator.Remove(removalPredicate);

        // Assert: collection count matches expected survivors
        if (collection.Count != expectedSurvivors.Count)
            return false;

        // Assert: remaining rows are in same relative order (by reference)
        for (int i = 0; i < expectedSurvivors.Count; i++)
        {
            if (!ReferenceEquals(collection[i], expectedSurvivors[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.3, 7.1**
    ///
    /// Property 7: Targeted Removal does not affect non-target rows.
    /// For any collection and any removal predicate, the returned removed set
    /// SHALL contain exactly the rows matching the predicate, and no other rows
    /// SHALL have been removed from the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_OnlyRemovesTargetedRows_And_ReturnsCorrectSet(
        NonNegativeInt collectionSizeSeed, NonNegativeInt thresholdSeed)
    {
        // Generate a collection of 0 to 30 rows
        int collectionSize = collectionSizeSeed.Get % 31;
        // Threshold: remove rows with Size > threshold
        long threshold = ((long)(thresholdSeed.Get % 30) + 1) * 100;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        // Predicate: remove rows with Size > threshold
        Func<ImageRowViewModel, bool> removalPredicate = r => r.Size > threshold;

        List<ImageRowViewModel> expectedRemoved = allRows.Where(r => removalPredicate(r)).ToList();
        List<ImageRowViewModel> expectedSurvivors = allRows.Where(r => !removalPredicate(r)).ToList();

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Act
        IReadOnlyList<(ImageRowViewModel Row, int Index)> removedResult = mutator.Remove(removalPredicate);

        // Assert: returned removed set contains exactly the expected rows
        if (removedResult.Count != expectedRemoved.Count)
            return false;

        // Assert: each removed row matches expected (by reference)
        List<ImageRowViewModel> removedRows = removedResult.Select(r => r.Row).ToList();
        for (int i = 0; i < expectedRemoved.Count; i++)
        {
            if (!removedRows.Contains(expectedRemoved[i]))
                return false;
        }

        // Assert: collection contains exactly the expected survivors
        if (collection.Count != expectedSurvivors.Count)
            return false;

        for (int i = 0; i < expectedSurvivors.Count; i++)
        {
            if (!ReferenceEquals(collection[i], expectedSurvivors[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.3, 7.1**
    ///
    /// Property 7: Targeted Removal with sorted collection preserves relative order.
    /// For any sorted collection and any removal predicate, after removal the
    /// remaining rows SHALL still be in sorted order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_FromSortedCollection_PreservesSortOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt thresholdSeed)
    {
        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        // Build a pre-sorted collection
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderBy(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", sortedSizes[i]);
            allRows.Add(row);
            collection.Add(row);
        }

        // Threshold for removal: remove rows with Size > threshold
        long threshold = (long)((thresholdSeed.Get % 10000) + 1);
        Func<ImageRowViewModel, bool> removalPredicate = r => r.Size > threshold;

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Act
        mutator.Remove(removalPredicate);

        // Assert: remaining collection is still sorted
        for (int i = 1; i < collection.Count; i++)
        {
            if (comparer(collection[i - 1], collection[i]) > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.3, 7.1**
    ///
    /// Property 7: Targeted Removal when predicate matches nothing leaves collection unchanged.
    /// For any collection and a predicate that matches no rows, the collection
    /// SHALL remain completely unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_WithNoMatchingPredicate_LeavesCollectionUnchanged(
        NonNegativeInt collectionSizeSeed)
    {
        int collectionSize = collectionSizeSeed.Get % 31;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        // Predicate that matches nothing
        Func<ImageRowViewModel, bool> removalPredicate = _ => false;

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Act
        IReadOnlyList<(ImageRowViewModel Row, int Index)> removedResult = mutator.Remove(removalPredicate);

        // Assert: nothing removed
        if (removedResult.Count != 0)
            return false;

        // Assert: collection unchanged
        if (collection.Count != allRows.Count)
            return false;

        for (int i = 0; i < allRows.Count; i++)
        {
            if (!ReferenceEquals(collection[i], allRows[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.3, 7.1**
    ///
    /// Property 7: Targeted Removal when predicate matches all rows empties collection.
    /// For any collection and a predicate that matches all rows, the collection
    /// SHALL be empty after removal and all rows SHALL be returned.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_WithAllMatchingPredicate_EmptiesCollection(
        NonNegativeInt collectionSizeSeed)
    {
        int collectionSize = collectionSizeSeed.Get % 31;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        // Predicate that matches everything
        Func<ImageRowViewModel, bool> removalPredicate = _ => true;

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Act
        IReadOnlyList<(ImageRowViewModel Row, int Index)> removedResult = mutator.Remove(removalPredicate);

        // Assert: all rows removed
        if (removedResult.Count != allRows.Count)
            return false;

        // Assert: collection is empty
        if (collection.Count != 0)
            return false;

        return true;
    }
}