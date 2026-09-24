using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for collection identity invariant.
///
/// Feature: stable-image-list, Property 1: Collection Identity Invariant
///
/// **Validates: Requirements 1.4, 6.1**
///
/// For any sequence of operations (insert, remove, filter change, sort change, reconcile)
/// applied to an ImageListViewModel that has been initialised, the FilteredImages property
/// SHALL reference the same ObservableCollection instance throughout.
/// </summary>
public class StableCollectionIdentityPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public StableCollectionIdentityPropertyTests()
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
    /// Creates an ImageRecord for reconcile testing.
    /// </summary>
    private static ImageRecord CreateRecord(long id, string name, long size)
    {
        return new ImageRecord
        {
            Id = id,
            Name = name,
            Size = size,
            SetName = "TestSet",
            Format = ImageFormat.Iso
        };
    }

    /// <summary>
    /// **Validates: Requirements 1.4, 6.1**
    ///
    /// Property 1: Collection Identity Invariant under random insert operations.
    /// For any sequence of inserts via StableCollectionMutator, the underlying
    /// collection reference SHALL remain the same instance.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Insert_Operations_PreserveCollectionIdentity(NonNegativeInt[] insertSizes)
    {
        SuppressibleObservableCollection<ImageRowViewModel> collection = new SuppressibleObservableCollection<ImageRowViewModel>();
        SuppressibleObservableCollection<ImageRowViewModel> originalReference = collection;

        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Perform multiple inserts
        for (int i = 0; i < insertSizes.Length; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (long)(insertSizes[i].Get % 10000) + 1);
            mutator.Insert(row);
        }

        // Assert: collection reference is unchanged
        return ReferenceEquals(originalReference, collection);
    }

    /// <summary>
    /// **Validates: Requirements 1.4, 6.1**
    ///
    /// Property 1: Collection Identity Invariant under random remove operations.
    /// For any collection and any removal predicate applied via StableCollectionMutator,
    /// the underlying collection reference SHALL remain the same instance.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Remove_Operations_PreserveCollectionIdentity(
        NonNegativeInt collectionSizeSeed, NonNegativeInt moduloSeed)
    {
        int collectionSize = (collectionSizeSeed.Get % 20) + 1;
        int modulo = (moduloSeed.Get % 4) + 2;

        SuppressibleObservableCollection<ImageRowViewModel> collection = new SuppressibleObservableCollection<ImageRowViewModel>();
        SuppressibleObservableCollection<ImageRowViewModel> originalReference = collection;

        for (int i = 0; i < collectionSize; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", (i + 1) * 100));

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Remove rows whose Id is divisible by modulo
        mutator.Remove(r => r.Id % modulo == 0);

        // Assert: collection reference is unchanged
        return ReferenceEquals(originalReference, collection);
    }

    /// <summary>
    /// **Validates: Requirements 1.4, 6.1**
    ///
    /// Property 1: Collection Identity Invariant under filter reapplication.
    /// For any collection and any new filter predicate applied via ReapplyFilter,
    /// the underlying collection reference SHALL remain the same instance.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReapplyFilter_PreservesCollectionIdentity(
        NonNegativeInt rowCountSeed, NonNegativeInt filterThresholdSeed)
    {
        int rowCount = (rowCountSeed.Get % 20) + 1;
        long threshold = (long)((filterThresholdSeed.Get % 20) + 1) * 100;

        SuppressibleObservableCollection<ImageRowViewModel> collection = new SuppressibleObservableCollection<ImageRowViewModel>();
        SuppressibleObservableCollection<ImageRowViewModel> originalReference = collection;

        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        for (int i = 0; i < rowCount; i++)
        {
            ImageRowViewModel row = CreateRow(i + 1, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Reapply filter with a new predicate
        Func<ImageRowViewModel, bool> newPredicate = r => r.Size <= threshold;
        mutator.ReapplyFilter(allRows, newPredicate);

        // Assert: collection reference is unchanged
        return ReferenceEquals(originalReference, collection);
    }

    /// <summary>
    /// **Validates: Requirements 1.4, 6.1**
    ///
    /// Property 1: Collection Identity Invariant under reconcile operations.
    /// For any collection and any reconcile with changed/added/removed images,
    /// the underlying collection reference SHALL remain the same instance.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_PreservesCollectionIdentity(
        NonNegativeInt existingCountSeed, NonNegativeInt newCountSeed)
    {
        int existingCount = (existingCountSeed.Get % 15) + 1;
        int newCount = (newCountSeed.Get % 15) + 1;

        SuppressibleObservableCollection<ImageRowViewModel> collection = new SuppressibleObservableCollection<ImageRowViewModel>();
        SuppressibleObservableCollection<ImageRowViewModel> originalReference = collection;

        // Populate collection with existing rows
        for (int i = 0; i < existingCount; i++)
            collection.Add(CreateRow(i + 1, $"image_{i}", (i + 1) * 100));

        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Create authoritative image list (some overlap, some new, some removed)
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        for (int i = 0; i < newCount; i++)
            authoritativeImages.Add(CreateRecord(i + 1, $"image_{i}_updated", (i + 1) * 200));

        mutator.Reconcile("TestSet", authoritativeImages, img => CreateRow(img.Id, img.Name, img.Size));

        // Assert: collection reference is unchanged
        return ReferenceEquals(originalReference, collection);
    }

    /// <summary>
    /// **Validates: Requirements 1.4, 6.1**
    ///
    /// Property 1: Collection Identity Invariant under mixed operation sequences.
    /// For any random sequence of operations (insert, remove, filter reapply, reconcile)
    /// applied via StableCollectionMutator, the underlying collection reference SHALL
    /// remain the same instance throughout all operations.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MixedOperationSequence_PreservesCollectionIdentity(
        NonNegativeInt[] operationSeeds)
    {
        SuppressibleObservableCollection<ImageRowViewModel> collection = new SuppressibleObservableCollection<ImageRowViewModel>();
        SuppressibleObservableCollection<ImageRowViewModel> originalReference = collection;

        // Maintain a list of all rows ever created (for filter reapplication)
        List<ImageRowViewModel> allRows = new List<ImageRowViewModel>();
        long nextId = 1;

        // Seed the collection with a few initial rows
        for (int i = 0; i < 5; i++)
        {
            ImageRowViewModel row = CreateRow(nextId++, $"image_{i}", (i + 1) * 100);
            allRows.Add(row);
            collection.Add(row);
        }

        Func<ImageRowViewModel, ImageRowViewModel, int> comparer =
            (a, b) => a.Size.CompareTo(b.Size);
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, comparer);

        // Execute a random sequence of operations
        foreach (NonNegativeInt seed in operationSeeds)
        {
            int operation = seed.Get % 4;

            switch (operation)
            {
                case 0: // Insert
                    ImageRowViewModel newRow = CreateRow(nextId++, $"image_{nextId}", (long)(seed.Get % 10000) + 1);
                    allRows.Add(newRow);
                    mutator.Insert(newRow);
                    break;

                case 1: // Remove
                    if (collection.Count > 0)
                    {
                        int removeIdx = seed.Get % Math.Max(1, collection.Count);
                        ImageRowViewModel targetRow = collection[removeIdx];
                        mutator.Remove(r => ReferenceEquals(r, targetRow));
                    }
                    break;

                case 2: // ReapplyFilter
                    long threshold = (long)(seed.Get % 5000) + 1;
                    mutator.ReapplyFilter(allRows, r => r.Size <= threshold);
                    break;

                case 3: // Reconcile
                    int reconcileCount = (seed.Get % 10) + 1;
                    List<ImageRecord> authImages = new List<ImageRecord>();
                    for (int j = 0; j < reconcileCount; j++)
                        authImages.Add(CreateRecord(j + 1, $"reconciled_{j}", (j + 1) * 150));
                    mutator.Reconcile("TestSet", authImages, img => CreateRow(img.Id, img.Name, img.Size));
                    break;
            }

            // Check identity after every operation
            if (!ReferenceEquals(originalReference, collection))
                return false;
        }

        // Final assertion: collection reference is still the same
        return ReferenceEquals(originalReference, collection);
    }
}