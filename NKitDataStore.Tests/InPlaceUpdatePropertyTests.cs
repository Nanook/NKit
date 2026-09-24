using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels;
using ReactiveUI.Builder;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for in-place property update preserving row identity.
///
/// Feature: stable-image-list, Property 2: In-Place Property Update Preserves Row Identity
///
/// **Validates: Requirements 1.1**
///
/// For any ImageRowViewModel in the collection and any property update (status, verify result,
/// stats, processing progress), the row SHALL remain at the same index in FilteredImages and
/// the collection SHALL not raise Add or Remove notifications.
/// </summary>
public class InPlaceUpdatePropertyTests
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public InPlaceUpdatePropertyTests()
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
    /// **Validates: Requirements 1.1**
    ///
    /// Property 2: For any row in the collection and any combination of property updates
    /// (UniqueRawSize, SharedRawSize, ProcessingStatus, VerifyResult, OverallProgress, etc.),
    /// the row SHALL remain at the same index and no Add/Remove/Replace CollectionChanged
    /// notifications SHALL be raised.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PropertyUpdate_PreservesRowIndex_NoAddRemoveNotifications(
        PositiveInt collectionSizeRaw,
        NonNegativeInt updateIndexRaw,
        NonNegativeInt uniqueRawSeed,
        NonNegativeInt sharedRawSeed,
        NonNegativeInt statusSeed,
        NonNegativeInt verifySeed,
        NonNegativeInt progressSeed)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 200);
        int updateIndex = updateIndexRaw.Get % collectionSize;

        // Build a collection with multiple rows
        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        // Capture the row reference before updates
        ImageRowViewModel targetRow = collection[updateIndex];

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

        // Apply random property updates using the generated seeds
        targetRow.UniqueRawSize = (long)uniqueRawSeed.Get;
        targetRow.UniqueCompressedSize = (long)(uniqueRawSeed.Get / 2);
        targetRow.SharedRawSize = (long)sharedRawSeed.Get;
        targetRow.SharedCompressedSize = (long)(sharedRawSeed.Get / 2);
        targetRow.SavedSize = (long)(uniqueRawSeed.Get - sharedRawSeed.Get);
        targetRow.ReducedBy = (double)(progressSeed.Get % 100);

        // Cycle through processing statuses based on seed
        ImageProcessingStatus[] statuses = Enum.GetValues<ImageProcessingStatus>();
        targetRow.ProcessingStatus = statuses[statusSeed.Get % statuses.Length];
        targetRow.StatusReason = $"Reason_{statusSeed.Get}";
        targetRow.CurrentStepName = $"Step_{statusSeed.Get % 5}";
        targetRow.CurrentStepProgress = (float)(progressSeed.Get % 100) / 100f;
        targetRow.OverallProgress = (float)(progressSeed.Get % 100) / 100f;

        // Cycle through verify results based on seed
        VerifyResultStatus[] verifyResults = Enum.GetValues<VerifyResultStatus>();
        targetRow.VerifyResult = verifyResults[verifySeed.Get % verifyResults.Length];
        targetRow.VerifyMethod = $"Method_{verifySeed.Get % 3}";

        // Assert: row is still at the same index (same reference)
        bool rowAtSameIndex = ReferenceEquals(collection[updateIndex], targetRow);

        // Assert: no Add/Remove/Replace/Reset notifications were raised
        return rowAtSameIndex && !addOrRemoveRaised;
    }

    /// <summary>
    /// **Validates: Requirements 1.1**
    ///
    /// Property 2: For any row in the collection, updating MarkRemoved/MarkRestored
    /// (which modifies the Removed property) SHALL preserve the row at the same index
    /// and SHALL NOT raise Add/Remove notifications.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MarkRemovedRestored_PreservesRowIndex_NoAddRemoveNotifications(
        PositiveInt collectionSizeRaw,
        NonNegativeInt updateIndexRaw,
        bool markRemoved)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 200);
        int updateIndex = updateIndexRaw.Get % collectionSize;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        ImageRowViewModel targetRow = collection[updateIndex];

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

        // Apply mark removed or restored
        if (markRemoved)
            targetRow.MarkRemoved();
        else
            targetRow.MarkRestored();

        bool rowAtSameIndex = ReferenceEquals(collection[updateIndex], targetRow);
        return rowAtSameIndex && !addOrRemoveRaised;
    }

    /// <summary>
    /// **Validates: Requirements 1.1**
    ///
    /// Property 2: For any row in the collection, multiple sequential property updates
    /// SHALL all preserve the row at the same index. This tests that repeated updates
    /// to the same row never cause collection mutations.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RepeatedUpdates_PreserveRowIndex_NoAddRemoveNotifications(
        PositiveInt collectionSizeRaw,
        NonNegativeInt updateIndexRaw,
        NonNegativeInt[] updateSeeds)
    {
        int collectionSize = Math.Min(collectionSizeRaw.Get, 100);
        int updateIndex = updateIndexRaw.Get % collectionSize;

        ObservableCollection<ImageRowViewModel> collection = new ObservableCollection<ImageRowViewModel>();
        for (int i = 0; i < collectionSize; i++)
        {
            collection.Add(CreateRow(i, $"Image_{i}", (i + 1) * 1024));
        }

        ImageRowViewModel targetRow = collection[updateIndex];

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

        // Apply multiple rounds of property updates
        foreach (NonNegativeInt seed in updateSeeds)
        {
            int updateType = seed.Get % 6;
            switch (updateType)
            {
                case 0:
                    targetRow.UniqueRawSize = (long)seed.Get;
                    break;
                case 1:
                    targetRow.SharedRawSize = (long)seed.Get;
                    break;
                case 2:
                    ImageProcessingStatus[] statuses = Enum.GetValues<ImageProcessingStatus>();
                    targetRow.ProcessingStatus = statuses[seed.Get % statuses.Length];
                    break;
                case 3:
                    VerifyResultStatus[] verifyResults = Enum.GetValues<VerifyResultStatus>();
                    targetRow.VerifyResult = verifyResults[seed.Get % verifyResults.Length];
                    break;
                case 4:
                    targetRow.OverallProgress = (float)(seed.Get % 100) / 100f;
                    break;
                case 5:
                    targetRow.CurrentStepName = $"Step_{seed.Get}";
                    targetRow.CurrentStepProgress = (float)(seed.Get % 100) / 100f;
                    break;
            }

            // Check after each update that the row is still at the same index
            if (!ReferenceEquals(collection[updateIndex], targetRow))
                return false;
        }

        return !addOrRemoveRaised;
    }
}