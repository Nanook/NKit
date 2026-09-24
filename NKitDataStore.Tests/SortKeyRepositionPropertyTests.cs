using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator sort-key update repositioning.
///
/// Feature: stable-image-list, Property 6: Sort-Key Update Repositions Correctly
///
/// **Validates: Requirements 3.4**
///
/// For any sorted collection and any row in that collection, updating the property
/// that IS the current sort key SHALL result in the collection remaining correctly
/// sorted (the row moves to its new correct position).
/// </summary>
public class SortKeyRepositionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SortKeyRepositionPropertyTests()
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
    private static ImageRowViewModel CreateRow(long id, string name, long size)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = name,
            Size = size,
            SetName = "TestSet",
            Format = ImageFormat.Iso
        };
        return new ImageRowViewModel(record, "test-session");
    }

    /// <summary>
    /// Checks whether a collection is sorted according to the given comparer.
    /// </summary>
    private static bool IsSorted(
        ObservableCollection<ImageRowViewModel> collection,
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer)
    {
        for (int i = 1; i < collection.Count; i++)
        {
            if (comparer(collection[i - 1], collection[i]) > 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 6: Sort-Key Update Repositions Correctly (sort by Size ascending).
    /// For any pre-sorted collection sorted by Size and any row in that collection,
    /// updating the Size property (the sort key) and calling Reposition SHALL result
    /// in the collection remaining correctly sorted.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortKeyUpdate_SortedBySize_Reposition_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newSizeSeed)
    {
        // Need at least 1 row to reposition
        if (existingSizes.Length == 0)
            return true;

        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        // Build a pre-sorted collection
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderBy(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Pick a random row to update
        int targetIndex = targetIndexSeed.Get % collection.Count;
        ImageRowViewModel targetRow = collection[targetIndex];

        // Update the sort-key property (Size)
        targetRow.Image.Size = (long)(newSizeSeed.Get % 50000) + 1;

        // Call Reposition to move the row to its correct sorted position
        mutator.Reposition(targetRow);

        // Property: collection remains sorted after reposition
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 6: Sort-Key Update Repositions Correctly (sort by Size descending).
    /// For any pre-sorted collection sorted by Size descending and any row in that
    /// collection, updating the Size property and calling Reposition SHALL result
    /// in the collection remaining correctly sorted in descending order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortKeyUpdate_SortedBySizeDescending_Reposition_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newSizeSeed)
    {
        // Need at least 1 row to reposition
        if (existingSizes.Length == 0)
            return true;

        // Sort comparer: descending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => b.Size.CompareTo(a.Size);

        // Build a pre-sorted collection (descending)
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderByDescending(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Pick a random row to update
        int targetIndex = targetIndexSeed.Get % collection.Count;
        ImageRowViewModel targetRow = collection[targetIndex];

        // Update the sort-key property (Size)
        targetRow.Image.Size = (long)(newSizeSeed.Get % 50000) + 1;

        // Call Reposition to move the row to its correct sorted position
        mutator.Reposition(targetRow);

        // Property: collection remains sorted after reposition
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 6: Sort-Key Update Repositions Correctly (sort by Name ascending).
    /// For any pre-sorted collection sorted by Name and any row in that collection,
    /// updating the Name property (the sort key) and calling Reposition SHALL result
    /// in the collection remaining correctly sorted.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortKeyUpdate_SortedByName_Reposition_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newNameSeed)
    {
        // Need at least 1 row to reposition
        if (existingSizes.Length == 0)
            return true;

        // Sort comparer: ascending by Name
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal);

        // Build a pre-sorted collection (sorted by name)
        string[] names = existingSizes
            .Select((_, i) => $"image_{i:D4}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < names.Length; i++)
            collection.Add(CreateRow(i + 1, names[i], (existingSizes[i].Get % 10000) + 1));

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Pick a random row to update
        int targetIndex = targetIndexSeed.Get % collection.Count;
        ImageRowViewModel targetRow = collection[targetIndex];

        // Update the sort-key property (Name)
        targetRow.Image.Name = $"updated_{newNameSeed.Get % 10000:D4}";

        // Call Reposition to move the row to its correct sorted position
        mutator.Reposition(targetRow);

        // Property: collection remains sorted after reposition
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 6: Sort-Key Update Repositions Correctly — row is still in collection.
    /// After Reposition, the updated row SHALL still be present in the collection
    /// (it is moved, not removed).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortKeyUpdate_Reposition_RowStillInCollection(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newSizeSeed)
    {
        // Need at least 1 row to reposition
        if (existingSizes.Length == 0)
            return true;

        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        // Build a pre-sorted collection
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderBy(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        int originalCount = collection.Count;
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Pick a random row to update
        int targetIndex = targetIndexSeed.Get % collection.Count;
        ImageRowViewModel targetRow = collection[targetIndex];

        // Update the sort-key property (Size)
        targetRow.Image.Size = (long)(newSizeSeed.Get % 50000) + 1;

        // Call Reposition
        (int oldIndex, int newIndex) = mutator.Reposition(targetRow);

        // Property: row is still in the collection
        if (!collection.Contains(targetRow))
            return false;

        // Property: collection count is unchanged
        if (collection.Count != originalCount)
            return false;

        // Property: the row is at the new index
        if (!ReferenceEquals(collection[newIndex], targetRow))
            return false;

        return true;
    }
}