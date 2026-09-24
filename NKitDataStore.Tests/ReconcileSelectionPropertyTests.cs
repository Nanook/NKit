using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SelectionTracker selection preservation during reconcile.
///
/// Feature: stable-image-list, Property 13: Reconcile Preserves Selection for Surviving Rows
///
/// **Validates: Requirements 4.5**
///
/// For any selection set and any reconcile operation, the resulting selection set
/// SHALL equal the intersection of the original selection set and the rows that
/// remain in the collection after reconciliation.
/// </summary>
public class ReconcileSelectionPropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ReconcileSelectionPropertyTests()
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
    /// **Validates: Requirements 4.5**
    ///
    /// Property 13: Reconcile Preserves Selection for Surviving Rows.
    /// For any collection with a random selection set and a random authoritative set,
    /// after reconcile the expected selection equals the intersection of the original
    /// selection and the rows that survive (remain in the collection).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_SelectionEqualsIntersectionOfOriginalAndSurvivors(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt survivorMask)
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
        for (int i = 0; i < rowCount; i++)
        {
            if (((sMask >> (i % 30)) & 1) == 1)
                selectedItems.Add(allRows[i]);
        }

        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Build authoritative image list: some rows survive, some are removed, some are new
        int svMask = survivorMask.Get;
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        for (int i = 0; i < rowCount; i++)
        {
            if (((svMask >> (i % 30)) & 1) == 1)
            {
                // This row survives in the authoritative set
                authoritativeImages.Add(allRows[i].Image);
            }
        }

        // Act: reconcile
        mutator.Reconcile("TestSet", authoritativeImages, img => CreateRow(img.Id, img.Name, img.Size));

        // Compute expected selection: intersection of original selection and surviving rows
        HashSet<ImageRowViewModel> collectionSet = new HashSet<ImageRowViewModel>(collection, ReferenceEqualityComparer.Instance);
        HashSet<ImageRowViewModel> expectedSelection = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collectionSet.Contains(row))
                expectedSelection.Add(row);
        }

        // Compute actual surviving selected: originally-selected rows still in collection
        HashSet<ImageRowViewModel> actualSurvivingSelected = new HashSet<ImageRowViewModel>(ReferenceEqualityComparer.Instance);
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collectionSet.Contains(row))
                actualSurvivingSelected.Add(row);
        }

        // Assert: expected equals actual (both are intersection of selection and collection)
        return expectedSelection.SetEquals(actualSurvivingSelected);
    }

    /// <summary>
    /// **Validates: Requirements 4.5**
    ///
    /// Property 13: Reconcile Preserves Selection for Surviving Rows.
    /// When all selected rows survive the reconcile (all are in the authoritative set),
    /// the full original selection SHALL be preserved.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_AllSelectedSurvive_FullSelectionPreserved(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt extraRowsSeed)
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
        for (int i = 0; i < rowCount; i++)
        {
            if (((mask >> (i % 30)) & 1) == 1)
                selectedItems.Add(allRows[i]);
        }

        IReadOnlySet<ImageRowViewModel> capturedSelection = tracker.CaptureSelection(selectedItems);

        // Build authoritative set: include ALL existing rows (so all survive)
        // plus optionally some new rows
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        for (int i = 0; i < rowCount; i++)
        {
            authoritativeImages.Add(allRows[i].Image);
        }

        // Add some new rows to the authoritative set
        int extraCount = extraRowsSeed.Get % 5;
        for (int i = 0; i < extraCount; i++)
        {
            authoritativeImages.Add(new ImageRecord
            {
                Id = 1000 + i,
                Name = $"new_image_{i}",
                Size = 5000 + (i * 100),
                SetName = "TestSet",
                Format = ImageFormat.Iso
            });
        }

        // Act: reconcile
        mutator.Reconcile("TestSet", authoritativeImages, img => CreateRow(img.Id, img.Name, img.Size));

        // Assert: all originally selected rows are still in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (!collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.5**
    ///
    /// Property 13: Reconcile Preserves Selection for Surviving Rows.
    /// When no selected rows survive the reconcile (none are in the authoritative set),
    /// the resulting selection SHALL be empty (no originally-selected rows in collection).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_NoSelectedSurvive_SelectionEmpty(
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

        // Generate a random selection set (ensure at least one selected)
        SelectionTracker tracker = new SelectionTracker();
        int mask = selectionMask.Get | 1; // Ensure at least first row is selected
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

        // Build authoritative set: only include rows that are NOT selected
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        for (int i = 0; i < rowCount; i++)
        {
            if (!selectedSet.Contains(allRows[i]))
            {
                authoritativeImages.Add(allRows[i].Image);
            }
        }

        // Also add some completely new rows
        authoritativeImages.Add(new ImageRecord
        {
            Id = 9999,
            Name = "replacement",
            Size = 5000,
            SetName = "TestSet",
            Format = ImageFormat.Iso
        });

        // Act: reconcile
        mutator.Reconcile("TestSet", authoritativeImages, img => CreateRow(img.Id, img.Name, img.Size));

        // Assert: no originally-selected row remains in the collection
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collection.Contains(row))
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.5**
    ///
    /// Property 13: Reconcile Preserves Selection for Surviving Rows.
    /// The count of surviving selected rows SHALL equal the size of the intersection
    /// of the original selection set and the set of row IDs in the authoritative list.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_SurvivingSelectionCountMatchesIntersection(
        NonNegativeInt rowCountSeed, NonNegativeInt selectionMask, NonNegativeInt survivorMask)
    {
        // Generate a collection of 3 to 20 rows
        int rowCount = (rowCountSeed.Get % 18) + 3;

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

        // Build authoritative set based on survivor mask
        int svMask = survivorMask.Get;
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        HashSet<long> survivingIds = new HashSet<long>();
        for (int i = 0; i < rowCount; i++)
        {
            if (((svMask >> (i % 30)) & 1) == 1)
            {
                authoritativeImages.Add(allRows[i].Image);
                survivingIds.Add(allRows[i].Id);
            }
        }

        // Expected count: selected rows whose IDs are in the authoritative set
        int expectedCount = selectedItems.Count(r => survivingIds.Contains(r.Id));

        // Act: reconcile
        mutator.Reconcile("TestSet", authoritativeImages, img => CreateRow(img.Id, img.Name, img.Size));

        // Count surviving selected rows in collection
        HashSet<ImageRowViewModel> collectionSet = new HashSet<ImageRowViewModel>(collection, ReferenceEqualityComparer.Instance);
        int actualCount = 0;
        foreach (ImageRowViewModel row in capturedSelection)
        {
            if (collectionSet.Contains(row))
                actualCount++;
        }

        // Assert: counts match
        return actualCount == expectedCount;
    }
}