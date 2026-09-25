using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for StableCollectionMutator reconcile in-place update.
///
/// Feature: stable-image-list, Property 14: Reconcile In-Place Update for Changed Data
///
/// **Validates: Requirements 7.3**
///
/// For any reconcile operation where an existing row's underlying data has changed
/// (same SetName+Id, different properties), the row's ImageRowViewModel instance SHALL
/// be preserved (same reference) with its properties updated, rather than being removed
/// and re-inserted.
/// </summary>
public class ReconcileInPlacePropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ReconcileInPlacePropertyTests()
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
    /// Creates an ImageRecord with the given properties.
    /// </summary>
    private static ImageRecord CreateRecord(long id, string setName, string name, long size, uint crc32)
    {
        return new ImageRecord
        {
            Id = id,
            Name = name,
            Size = size,
            Crc32 = crc32,
            SetName = setName,
            Format = ImageFormat.Iso
        };
    }

    /// <summary>
    /// Creates an ImageRowViewModel from an ImageRecord.
    /// </summary>
    private static ImageRowViewModel CreateRow(ImageRecord record) => new ImageRowViewModel(record, "test-session");

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 14: Reconcile In-Place Update for Changed Data.
    /// When Reconcile finds a row with the same SetName+Id but different properties
    /// (e.g., different Size, Name, Crc32), it should update the existing
    /// ImageRowViewModel in place (same reference preserved) rather than removing
    /// and re-inserting.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_WithChangedData_PreservesRowReference(
        PositiveInt id,
        NonNegativeInt originalSize,
        NonNegativeInt newSize,
        NonNegativeInt originalCrc,
        NonNegativeInt newCrc)
    {
        const string setName = "TestSet";
        long imageId = id.Get;
        long origSize = (originalSize.Get % 10000) + 1;
        long changedSize = (newSize.Get % 10000) + 1;
        uint origCrc = (uint)(originalCrc.Get % 1000000);
        uint changedCrc = (uint)(newCrc.Get % 1000000);

        // Ensure at least one property is different so HasDataChanged returns true
        if (origSize == changedSize && origCrc == changedCrc)
            changedSize = origSize + 1;

        // Create original record and row
        ImageRecord originalRecord = CreateRecord(imageId, setName, "image_original", origSize, origCrc);
        ImageRowViewModel originalRow = CreateRow(originalRecord);

        // Set up collection with the original row
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel> { originalRow };

        // No sort, accept all filter
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Create authoritative data with same Id but different properties
        ImageRecord changedRecord = CreateRecord(imageId, setName, "image_changed", changedSize, changedCrc);
        List<ImageRecord> authoritativeImages = new List<ImageRecord> { changedRecord };

        // Act
        ReconcileResult result = mutator.Reconcile(setName, authoritativeImages, CreateRow);

        // Assert: same reference preserved in collection
        bool referencePreserved = ReferenceEquals(collection[0], originalRow);

        // Assert: row is in the Updated list
        bool inUpdatedList = result.Updated.Contains(originalRow);

        // Assert: no rows were removed or added (identity matches)
        bool noRemovals = result.Removed.Count == 0;
        bool noAdditions = result.Added.Count == 0;

        // Assert: properties were updated on the existing row
        bool propertiesUpdated = originalRow.Image.Size == changedSize
            && originalRow.Image.Crc32 == changedCrc
            && originalRow.Image.Name == "image_changed";

        return referencePreserved && inUpdatedList && noRemovals && noAdditions && propertiesUpdated;
    }

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 14: Reconcile In-Place Update for Changed Data (multiple rows).
    /// When Reconcile processes multiple rows with changed data, all existing
    /// ImageRowViewModel references are preserved.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_WithMultipleChangedRows_PreservesAllReferences(
        NonNegativeInt rowCount,
        NonNegativeInt sizeDelta)
    {
        const string setName = "TestSet";
        int count = (rowCount.Get % 10) + 2; // 2 to 11 rows
        long delta = (sizeDelta.Get % 1000) + 1;

        // Create original rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> originalRows = new List<ImageRowViewModel>();

        for (int i = 0; i < count; i++)
        {
            ImageRecord record = CreateRecord(i + 1, setName, $"image_{i}", (i + 1) * 100, (uint)(i * 10));
            ImageRowViewModel row = CreateRow(record);
            collection.Add(row);
            originalRows.Add(row);
        }

        // No sort, accept all filter
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Create authoritative data with same Ids but different sizes
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        for (int i = 0; i < count; i++)
        {
            ImageRecord changedRecord = CreateRecord(
                i + 1, setName, $"image_{i}_updated", ((i + 1) * 100) + delta, (uint)((i * 10) + 1));
            authoritativeImages.Add(changedRecord);
        }

        // Act
        ReconcileResult result = mutator.Reconcile(setName, authoritativeImages, CreateRow);

        // Assert: all original references preserved
        bool allReferencesPreserved = true;
        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(collection[i], originalRows[i]))
            {
                allReferencesPreserved = false;
                break;
            }
        }

        // Assert: all rows in Updated list
        bool allInUpdated = result.Updated.Count == count;

        // Assert: no removals or additions
        bool noRemovals = result.Removed.Count == 0;
        bool noAdditions = result.Added.Count == 0;

        return allReferencesPreserved && allInUpdated && noRemovals && noAdditions;
    }

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 14: Reconcile In-Place Update for Changed Data (mixed scenario).
    /// When Reconcile processes a mix of unchanged and changed rows, only changed
    /// rows appear in the Updated list, and all row references are preserved.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Reconcile_WithMixedChangedAndUnchanged_PreservesReferencesAndReportsCorrectly(
        NonNegativeInt rowCount,
        NonNegativeInt changeMask)
    {
        const string setName = "TestSet";
        int count = (rowCount.Get % 8) + 2; // 2 to 9 rows
        int mask = changeMask.Get;

        // Create original rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        List<ImageRowViewModel> originalRows = new List<ImageRowViewModel>();

        for (int i = 0; i < count; i++)
        {
            ImageRecord record = CreateRecord(i + 1, setName, $"image_{i}", (i + 1) * 100, (uint)(i * 10));
            ImageRowViewModel row = CreateRow(record);
            collection.Add(row);
            originalRows.Add(row);
        }

        // No sort, accept all filter
        StableCollectionMutator mutator = new StableCollectionMutator(collection, _ => true, null);

        // Create authoritative data: some rows changed, some unchanged
        List<ImageRecord> authoritativeImages = new List<ImageRecord>();
        int expectedChangedCount = 0;

        for (int i = 0; i < count; i++)
        {
            bool shouldChange = ((mask >> (i % 30)) & 1) == 1;
            if (shouldChange)
            {
                // Changed: different size
                ImageRecord changedRecord = CreateRecord(
                    i + 1, setName, $"image_{i}_changed", ((i + 1) * 100) + 999, (uint)(i * 10));
                authoritativeImages.Add(changedRecord);
                expectedChangedCount++;
            }
            else
            {
                // Unchanged: same properties
                ImageRecord sameRecord = CreateRecord(
                    i + 1, setName, $"image_{i}", (i + 1) * 100, (uint)(i * 10));
                authoritativeImages.Add(sameRecord);
            }
        }

        // Act
        ReconcileResult result = mutator.Reconcile(setName, authoritativeImages, CreateRow);

        // Assert: all original references preserved (changed or not)
        bool allReferencesPreserved = true;
        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(collection[i], originalRows[i]))
            {
                allReferencesPreserved = false;
                break;
            }
        }

        // Assert: Updated list contains exactly the changed rows
        bool correctUpdatedCount = result.Updated.Count == expectedChangedCount;

        // Assert: no removals or additions (all identities match)
        bool noRemovals = result.Removed.Count == 0;
        bool noAdditions = result.Added.Count == 0;

        return allReferencesPreserved && correctUpdatedCount && noRemovals && noAdditions;
    }
}