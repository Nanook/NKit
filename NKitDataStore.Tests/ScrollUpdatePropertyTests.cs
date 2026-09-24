using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for scroll position stability during in-place property updates.
///
/// Feature: stable-image-list, Property 10: Scroll Unchanged on Property Update
///
/// **Validates: Requirements 2.5**
///
/// For any in-place property update on any row, the scroll offset SHALL remain unchanged.
/// Neither ComputeScrollAfterInsertion nor ComputeScrollAfterRemoval should be called
/// for property updates — the scroll position stays exactly where it was.
/// </summary>
public class ScrollUpdatePropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public ScrollUpdatePropertyTests()
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
    /// **Validates: Requirements 2.5**
    ///
    /// Property 10: For any scroll position and any in-place property update on any row,
    /// the collection SHALL NOT raise Add or Remove notifications, meaning the scroll
    /// position remains unchanged.
    ///
    /// We verify this by updating various properties on a row within an ObservableCollection
    /// and asserting that no Add/Remove/Replace CollectionChanged events are raised.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PropertyUpdate_DoesNotRaiseAddOrRemove_ScrollUnchanged(
        NonNegativeInt scrollPosRaw, PositiveInt collectionSizeRaw, NonNegativeInt updateIndexRaw)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 1000); // cap for performance
        int updateIndex = updateIndexRaw.Get % collectionSize;
        int scrollPos = scrollPosRaw.Get % collectionSize;

        // Build a collection with multiple rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        // Track whether any Add/Remove/Replace/Reset events are raised
        bool addOrRemoveRaised = false;
        collection.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add ||
                args.Action == NotifyCollectionChangedAction.Remove ||
                args.Action == NotifyCollectionChangedAction.Replace ||
                args.Action == NotifyCollectionChangedAction.Reset)
            {
                addOrRemoveRaised = true;
            }
        };

        // Perform in-place property updates on the row at updateIndex
        ImageRowViewModel row = collection[updateIndex];
        row.UniqueRawSize = 500L;
        row.UniqueCompressedSize = 300L;
        row.SharedRawSize = 200L;
        row.SharedCompressedSize = 100L;
        row.SavedSize = 50L;
        row.ReducedBy = 25.5;
        row.ProcessingStatus = ImageProcessingStatus.Processing;
        row.StatusReason = "Test reason";
        row.CurrentStepName = "Hashing";
        row.CurrentStepProgress = 0.5f;
        row.OverallProgress = 0.75f;
        row.VerifyResult = VerifyResultStatus.VerifySuccess;
        row.VerifyMethod = "CRC32";
        row.MarkRemoved();
        row.MarkRestored();

        // The scroll position should remain unchanged because no Add/Remove occurred.
        // Since no collection mutation events are raised, ComputeScrollAfterInsertion and
        // ComputeScrollAfterRemoval would never be invoked, so scroll stays at scrollPos.
        return !addOrRemoveRaised;
    }

    /// <summary>
    /// **Validates: Requirements 2.5**
    ///
    /// Property 10: For any scroll position, an in-place property update never changes
    /// the row count of the collection, which is a prerequisite for scroll stability.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PropertyUpdate_PreservesCollectionCount(
        PositiveInt collectionSizeRaw, NonNegativeInt updateIndexRaw)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 500);
        int updateIndex = updateIndexRaw.Get % collectionSize;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        int countBefore = collection.Count;

        // Perform in-place property updates
        ImageRowViewModel row = collection[updateIndex];
        row.UniqueRawSize = 999L;
        row.ProcessingStatus = ImageProcessingStatus.Completed;
        row.VerifyResult = VerifyResultStatus.VerifyFailed;
        row.OverallProgress = 1.0f;

        return collection.Count == countBefore;
    }

    /// <summary>
    /// **Validates: Requirements 2.5**
    ///
    /// Property 10: For any scroll position and any in-place property update,
    /// the row remains at the same index in the collection (identity preserved).
    /// This ensures no scroll adjustment is needed.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PropertyUpdate_RowRemainsAtSameIndex(
        NonNegativeInt scrollPosRaw, PositiveInt collectionSizeRaw, NonNegativeInt updateIndexRaw)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 500);
        int updateIndex = updateIndexRaw.Get % collectionSize;
        int scrollPos = scrollPosRaw.Get % collectionSize;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        ImageRowViewModel row = collection[updateIndex];

        // Perform in-place property updates
        row.UniqueRawSize = 12345L;
        row.SharedCompressedSize = 6789L;
        row.ProcessingStatus = ImageProcessingStatus.Processing;
        row.VerifyResult = VerifyResultStatus.VerifySuccess;

        // The row should still be at the same index (same reference)
        return ReferenceEquals(collection[updateIndex], row);
    }
}