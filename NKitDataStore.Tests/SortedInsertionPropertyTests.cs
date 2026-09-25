using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator sorted insertion.
///
/// Feature: stable-image-list, Property 3: Sorted Insertion Maintains Order
///
/// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
///
/// For any sorted FilteredImages collection (with any active sort column and direction)
/// and any new row that passes the current filter, inserting the row SHALL result in a
/// collection that remains sorted according to the active sort comparer.
/// </summary>
public class SortedInsertionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public SortedInsertionPropertyTests()
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
    /// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
    ///
    /// Property 3: Sorted Insertion Maintains Order (sort by Name ascending).
    /// For any pre-sorted collection and any new row, inserting via
    /// StableCollectionMutator.Insert SHALL maintain ascending Name order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_IntoNameSortedCollection_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt newNameSeed)
    {
        // Sort comparer: ascending by Name
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal);

        // Build a pre-sorted collection
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        string[] names = existingSizes
            .Select((s, i) => $"image_{i:D4}_{s.Get % 1000}")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        for (int i = 0; i < names.Length; i++)
            collection.Add(CreateRow(i + 1, names[i], (existingSizes[i].Get % 10000) + 1));

        // Create a new row with a random name
        ImageRowViewModel newRow = CreateRow(9999, $"image_{newNameSeed.Get % 10000:D4}", 512);

        // Filter: accept all rows
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Act
        mutator.Insert(newRow);

        // Assert: collection remains sorted
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
    ///
    /// Property 3: Sorted Insertion Maintains Order (sort by Size ascending).
    /// For any pre-sorted collection and any new row, inserting via
    /// StableCollectionMutator.Insert SHALL maintain ascending Size order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_IntoSizeSortedCollection_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt newSize)
    {
        // Sort comparer: ascending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        // Build a pre-sorted collection (sorted by size ascending)
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderBy(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        // Create a new row with a random size
        ImageRowViewModel newRow = CreateRow(9999, "new_image", (long)(newSize.Get % 10000) + 1);

        // Filter: accept all rows
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Act
        mutator.Insert(newRow);

        // Assert: collection remains sorted
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
    ///
    /// Property 3: Sorted Insertion Maintains Order (sort by Size descending).
    /// For any pre-sorted collection and any new row, inserting via
    /// StableCollectionMutator.Insert SHALL maintain descending Size order.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_IntoDescendingSizeSortedCollection_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt newSize)
    {
        // Sort comparer: descending by Size
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => b.Size.CompareTo(a.Size);

        // Build a pre-sorted collection (sorted by size descending)
        long[] sortedSizes = existingSizes
            .Select(s => (long)((s.Get % 10000) + 1))
            .OrderByDescending(s => s)
            .ToArray();

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        // Create a new row with a random size
        ImageRowViewModel newRow = CreateRow(9999, "new_image", (long)(newSize.Get % 10000) + 1);

        // Filter: accept all rows
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Act
        mutator.Insert(newRow);

        // Assert: collection remains sorted
        return IsSorted(collection, comparer);
    }

    /// <summary>
    /// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
    ///
    /// Property 3: Sorted Insertion Maintains Order with multiple insertions.
    /// For any pre-sorted collection and any sequence of new rows, inserting each
    /// via StableCollectionMutator.Insert SHALL maintain sort order after every insertion.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MultipleInserts_IntoSortedCollection_MaintainsOrder(
        NonNegativeInt[] existingSizes, NonNegativeInt[] newSizes)
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
        for (int i = 0; i < sortedSizes.Length; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", sortedSizes[i]));

        // Filter: accept all rows
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Insert multiple new rows
        for (int i = 0; i < newSizes.Length; i++)
        {
            ImageRowViewModel newRow = CreateRow(1000 + i, $"new_{i}", (long)(newSizes[i].Get % 10000) + 1);
            mutator.Insert(newRow);

            // Assert sorted after each insertion
            if (!IsSorted(collection, comparer))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 1.2, 3.2, 6.3, 7.2**
    ///
    /// Property 3: Sorted Insertion into empty collection maintains order.
    /// Inserting into an empty collection with a sort comparer always produces
    /// a single-element collection (trivially sorted).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_IntoEmptyCollection_ProducesSortedSingleElement(
        NonNegativeInt size)
    {
        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        ImageRowViewModel newRow = CreateRow(1, "image_0", (long)(size.Get % 10000) + 1);
        mutator.Insert(newRow);

        return collection.Count == 1 && collection[0] == newRow;
    }
}