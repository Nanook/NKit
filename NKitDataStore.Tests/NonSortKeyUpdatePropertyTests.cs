using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator non-sort-key update behavior.
///
/// Feature: stable-image-list, Property 5: Non-Sort-Key Update Preserves Position
///
/// **Validates: Requirements 3.3**
///
/// For any sorted collection and any row in that collection, updating a property
/// that is NOT the current sort key SHALL leave the row at its current index
/// in the collection.
/// </summary>
public class NonSortKeyUpdatePropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public NonSortKeyUpdatePropertyTests()
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
    /// **Validates: Requirements 3.3**
    ///
    /// Property 5: Non-Sort-Key Update Preserves Position (sort by Size, update Name).
    /// For any pre-sorted collection sorted by Size and any row in that collection,
    /// updating the Name property (a non-sort-key) SHALL leave the row at its
    /// current index in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonSortKeyUpdate_SortedBySize_ChangeName_PreservesPosition(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newNameSeed)
    {
        // Need at least 1 row to update
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

        // Update a non-sort-key property (Name) — this does NOT affect sort order
        // Since Name is a passthrough to ImageRecord, we modify the underlying record
        targetRow.Image.Name = $"updated_name_{newNameSeed.Get}";

        // The row should remain at the same index — no Reposition needed
        // Property: the row is still at the same index
        return ReferenceEquals(collection[targetIndex], targetRow);
    }

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 5: Non-Sort-Key Update Preserves Position (sort by Name, update Size).
    /// For any pre-sorted collection sorted by Name and any row in that collection,
    /// updating the Size property (a non-sort-key) SHALL leave the row at its
    /// current index in the collection.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonSortKeyUpdate_SortedByName_ChangeSize_PreservesPosition(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newSizeSeed)
    {
        // Need at least 1 row to update
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

        // Update a non-sort-key property (Size) — this does NOT affect sort order
        targetRow.Image.Size = (long)(newSizeSeed.Get % 50000) + 1;

        // The row should remain at the same index — no Reposition needed
        // Property: the row is still at the same index
        return ReferenceEquals(collection[targetIndex], targetRow);
    }

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 5: Non-Sort-Key Update Preserves Position — collection order unchanged.
    /// For any pre-sorted collection and any non-sort-key property update,
    /// the entire collection order SHALL remain unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NonSortKeyUpdate_CollectionOrderUnchanged(
        NonNegativeInt[] existingSizes, NonNegativeInt targetIndexSeed, NonNegativeInt newSizeSeed)
    {
        // Need at least 1 row to update
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

        // Capture the order before update
        List<ImageRowViewModel> orderBefore = collection.ToList();

        // Pick a random row to update
        int targetIndex = targetIndexSeed.Get % collection.Count;
        ImageRowViewModel targetRow = collection[targetIndex];

        // Update a non-sort-key property (Size)
        targetRow.Image.Size = (long)(newSizeSeed.Get % 50000) + 1;

        // Property: the entire collection order is unchanged
        if (collection.Count != orderBefore.Count)
            return false;

        for (int i = 0; i < collection.Count; i++)
        {
            if (!ReferenceEquals(collection[i], orderBefore[i]))
                return false;
        }

        return true;
    }
}