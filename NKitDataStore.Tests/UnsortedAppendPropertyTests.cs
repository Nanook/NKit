using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator unsorted insertion behavior.
///
/// Feature: stable-image-list, Property 4: Unsorted Insertion Appends
///
/// **Validates: Requirements 3.5**
///
/// For any collection with no active sort and any new row that passes the current filter,
/// inserting the row SHALL place it at the end of the collection and all previously
/// existing rows SHALL remain at their original indices.
/// </summary>
public class UnsortedAppendPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public UnsortedAppendPropertyTests()
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
    /// Helper to create an ImageRowViewModel with the given id and name.
    /// </summary>
    private static ImageRowViewModel CreateRow(long id, string name = null)
    {
        ImageRecord record = new ImageRecord
        {
            Id = id,
            Name = name ?? $"image_{id}",
            System = "Wii",
            Format = ImageFormat.Iso,
            SetName = "TestSet",
            Size = 1024 * id
        };
        return new ImageRowViewModel(record, "test-session");
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Property 4: Unsorted Insertion Appends.
    /// For any collection with no active sort (sortComparer = null) and any new row
    /// that passes the filter, Insert SHALL place the new row at the end of the
    /// collection and all previously existing rows SHALL remain at their original indices.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_WithNoSort_AppendsToEnd_And_PreservesExistingOrder(
        NonNegativeInt existingCountSeed, PositiveInt newIdSeed)
    {
        // Generate a random collection size (0 to 20 rows)
        int existingCount = existingCountSeed.Get % 21;
        long newId = newIdSeed.Get + 1000; // Ensure distinct from existing

        // Build the existing collection with no sort comparer
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> existingRows = new List<ImageRowViewModel>();
        for (int i = 0; i < existingCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1);
            existingRows.Add(row);
            collection.Add(row);
        }

        // Create mutator with no sort (null comparer) and an accept-all filter
        StableCollectionMutator mutator = new StableCollectionMutator(
            collection,
            filterPredicate: _ => true,
            sortComparer: null);

        // Insert a new row
        ImageRowViewModel newRow = CreateRow(newId);
        int insertIndex = mutator.Insert(newRow);

        // Property: new row is placed at the end (index == previous count)
        if (insertIndex != existingCount)
            return false;

        // Property: collection count increased by 1
        if (collection.Count != existingCount + 1)
            return false;

        // Property: the last element is the new row
        if (!ReferenceEquals(collection[existingCount], newRow))
            return false;

        // Property: all previously existing rows remain at their original indices
        for (int i = 0; i < existingCount; i++)
        {
            if (!ReferenceEquals(collection[i], existingRows[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Property 4 (supplementary): When no sort is active and the new row does NOT
    /// pass the filter, Insert SHALL return -1 and the collection SHALL remain unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_WithNoSort_FilteredOut_ReturnsNegativeOne_And_CollectionUnchanged(
        NonNegativeInt existingCountSeed, PositiveInt newIdSeed)
    {
        int existingCount = existingCountSeed.Get % 21;
        long newId = newIdSeed.Get + 1000;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> existingRows = new List<ImageRowViewModel>();
        for (int i = 0; i < existingCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1);
            existingRows.Add(row);
            collection.Add(row);
        }

        // Create mutator with no sort and a reject-all filter
        StableCollectionMutator mutator = new StableCollectionMutator(
            collection,
            filterPredicate: _ => false,
            sortComparer: null);

        ImageRowViewModel newRow = CreateRow(newId);
        int insertIndex = mutator.Insert(newRow);

        // Property: returns -1 when filtered out
        if (insertIndex != -1)
            return false;

        // Property: collection size unchanged
        if (collection.Count != existingCount)
            return false;

        // Property: all existing rows unchanged
        for (int i = 0; i < existingCount; i++)
        {
            if (!ReferenceEquals(collection[i], existingRows[i]))
                return false;
        }

        return true;
    }
}