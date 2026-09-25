using FsCheck;
using FsCheck.Xunit;
using NkdsUi.ViewModels;
using ReactiveUI;
using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for SharedObservableState Reactive Propagation.
///
/// Property 7: SharedObservableState Reactive Propagation
///
/// **Validates: Requirements 4.3**
///
/// For any random property values written to SharedObservableState, all subscribers
/// SHALL receive the updated value through their reactive subscription without the
/// producer holding a reference to the consumer.
/// </summary>
public class SharedObservableStatePropertyTests : IDisposable
{
    private static readonly object _InitLock = new();
    private static bool _Initialized;

    public SharedObservableStatePropertyTests()
    {
        ensureReactiveUIInitialized();
        NkdsUi.RxSchedulers.SetSchedulerForTest(ImmediateSequencer.Instance);
    }

    public void Dispose() => NkdsUi.RxSchedulers.SetSchedulerForTest(null);

    private static void ensureReactiveUIInitialized()
    {
        if (_Initialized) return;
        lock (_InitLock)
        {
            if (_Initialized) return;
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
            _Initialized = true;
        }
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — Boolean properties.
    /// For any random boolean values written to HasSelectedImages, HasRemovedImages,
    /// HasDataStoreOpen, IsOperationActive, IsGroupWindowOpen, IsDatWindowOpen,
    /// IsMountActive, and ShowFilterBar, all WhenAnyValue subscribers receive the value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BooleanProperties_PropagateToAllSubscribers(
        bool hasSelectedImages,
        bool hasRemovedImages,
        bool hasDataStoreOpen,
        bool isOperationActive,
        bool isGroupWindowOpen,
        bool isDatWindowOpen,
        bool isMountActive,
        bool showFilterBar)
    {
        SharedObservableState state = new SharedObservableState();

        // Track received values from subscribers
        bool? receivedHasSelectedImages = null;
        bool? receivedHasRemovedImages = null;
        bool? receivedHasDataStoreOpen = null;
        bool? receivedIsOperationActive = null;
        bool? receivedIsGroupWindowOpen = null;
        bool? receivedIsDatWindowOpen = null;
        bool? receivedIsMountActive = null;
        bool? receivedShowFilterBar = null;

        // Subscribe to each boolean property
        state.WhenAnyValue(x => x.HasSelectedImages)
            .Subscribe(v => receivedHasSelectedImages = v);
        state.WhenAnyValue(x => x.HasRemovedImages)
            .Subscribe(v => receivedHasRemovedImages = v);
        state.WhenAnyValue(x => x.HasDataStoreOpen)
            .Subscribe(v => receivedHasDataStoreOpen = v);
        state.WhenAnyValue(x => x.IsOperationActive)
            .Subscribe(v => receivedIsOperationActive = v);
        state.WhenAnyValue(x => x.IsGroupWindowOpen)
            .Subscribe(v => receivedIsGroupWindowOpen = v);
        state.WhenAnyValue(x => x.IsDatWindowOpen)
            .Subscribe(v => receivedIsDatWindowOpen = v);
        state.WhenAnyValue(x => x.IsMountActive)
            .Subscribe(v => receivedIsMountActive = v);
        state.WhenAnyValue(x => x.ShowFilterBar)
            .Subscribe(v => receivedShowFilterBar = v);

        // Write values to SharedObservableState
        state.HasSelectedImages = hasSelectedImages;
        state.HasRemovedImages = hasRemovedImages;
        state.HasDataStoreOpen = hasDataStoreOpen;
        state.IsOperationActive = isOperationActive;
        state.IsGroupWindowOpen = isGroupWindowOpen;
        state.IsDatWindowOpen = isDatWindowOpen;
        state.IsMountActive = isMountActive;
        state.ShowFilterBar = showFilterBar;

        // Verify all subscribers received the correct values
        return receivedHasSelectedImages == hasSelectedImages
            && receivedHasRemovedImages == hasRemovedImages
            && receivedHasDataStoreOpen == hasDataStoreOpen
            && receivedIsOperationActive == isOperationActive
            && receivedIsGroupWindowOpen == isGroupWindowOpen
            && receivedIsDatWindowOpen == isDatWindowOpen
            && receivedIsMountActive == isMountActive
            && receivedShowFilterBar == showFilterBar;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — Int property.
    /// For any random int value written to FilteredImageCount, all WhenAnyValue
    /// subscribers receive the value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FilteredImageCount_PropagatesToAllSubscribers(int filteredImageCount)
    {
        SharedObservableState state = new SharedObservableState();

        int? receivedValue = null;
        state.WhenAnyValue(x => x.FilteredImageCount)
            .Subscribe(v => receivedValue = v);

        state.FilteredImageCount = filteredImageCount;

        return receivedValue == filteredImageCount;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — Nullable string property.
    /// For any random string value written to OperationInProgressOnSet, all WhenAnyValue
    /// subscribers receive the value (including null).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OperationInProgressOnSet_PropagatesToAllSubscribers(string operationInProgressOnSet)
    {
        SharedObservableState state = new SharedObservableState();

        bool received = false;
        string receivedValue = "sentinel";
        state.WhenAnyValue(x => x.OperationInProgressOnSet)
            .Subscribe(v => { receivedValue = v; received = true; });

        state.OperationInProgressOnSet = operationInProgressOnSet;

        return received && receivedValue == operationInProgressOnSet;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — SelectedSetName property.
    /// For any random non-null string value written to SelectedSetName, all WhenAnyValue
    /// subscribers receive the value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SelectedSetName_PropagatesToAllSubscribers(NonNull<string> selectedSetName)
    {
        SharedObservableState state = new SharedObservableState();

        string receivedValue = null;
        state.WhenAnyValue(x => x.SelectedSetName)
            .Subscribe(v => receivedValue = v);

        state.SelectedSetName = selectedSetName.Get;

        return receivedValue == selectedSetName.Get;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — AvailableSetNames property.
    /// For any random list of strings written to AvailableSetNames, all WhenAnyValue
    /// subscribers receive the value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AvailableSetNames_PropagatesToAllSubscribers(string[] setNames)
    {
        SharedObservableState state = new SharedObservableState();
        IReadOnlyList<string> inputList = (IReadOnlyList<string>)(setNames ?? Array.Empty<string>());

        IReadOnlyList<string> receivedValue = null;
        state.WhenAnyValue(x => x.AvailableSetNames)
            .Subscribe(v => receivedValue = v);

        state.AvailableSetNames = inputList;

        return receivedValue != null && receivedValue.SequenceEqual(inputList);
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — Multiple subscribers.
    /// For any random property value, multiple independent subscribers all receive
    /// the same updated value, confirming broadcast semantics.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MultipleSubscribers_AllReceiveSameValue(int filteredImageCount, NonNegativeInt subscriberCountSeed)
    {
        int subscriberCount = (subscriberCountSeed.Get % 5) + 2; // 2 to 6 subscribers
        SharedObservableState state = new SharedObservableState();

        int?[] receivedValues = new int?[subscriberCount];
        for (int i = 0; i < subscriberCount; i++)
        {
            int index = i;
            state.WhenAnyValue(x => x.FilteredImageCount)
                .Subscribe(v => receivedValues[index] = v);
        }

        state.FilteredImageCount = filteredImageCount;

        // All subscribers must have received the same value
        return receivedValues.All(v => v == filteredImageCount);
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    ///
    /// Property 7: SharedObservableState Reactive Propagation — All properties together.
    /// For any random combination of all 12 property values written simultaneously,
    /// all subscribers receive the correct values for their respective properties.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool AllProperties_PropagateCorrectlyWhenSetTogether(
        bool hasSelectedImages,
        bool hasRemovedImages,
        bool hasDataStoreOpen,
        bool isOperationActive,
        bool isGroupWindowOpen,
        bool isDatWindowOpen,
        bool isMountActive,
        bool showFilterBar,
        int filteredImageCount,
        NonNegativeInt setNameSeed)
    {
        SharedObservableState state = new SharedObservableState();

        // Generate string values from seeds
        string[] setNames = new[] { "All", "GameCube", "Wii", "WiiU", "Custom Set", null };
        string operationInProgress = setNames[setNameSeed.Get % setNames.Length];
        string selectedSetName = setNames[setNameSeed.Get % (setNames.Length - 1)] ?? "All"; // non-null
        List<string> availableSetNames = new List<string> { "All", "Set1" };

        // Track all received values
        bool? rHasSelectedImages = null;
        bool? rHasRemovedImages = null;
        bool? rHasDataStoreOpen = null;
        bool? rIsOperationActive = null;
        bool? rIsGroupWindowOpen = null;
        bool? rIsDatWindowOpen = null;
        bool? rIsMountActive = null;
        bool? rShowFilterBar = null;
        int? rFilteredImageCount = null;
        string rOperationInProgressOnSet = "sentinel";
        string rSelectedSetName = null;
        IReadOnlyList<string> rAvailableSetNames = null;

        // Subscribe to all properties
        state.WhenAnyValue(x => x.HasSelectedImages).Subscribe(v => rHasSelectedImages = v);
        state.WhenAnyValue(x => x.HasRemovedImages).Subscribe(v => rHasRemovedImages = v);
        state.WhenAnyValue(x => x.HasDataStoreOpen).Subscribe(v => rHasDataStoreOpen = v);
        state.WhenAnyValue(x => x.IsOperationActive).Subscribe(v => rIsOperationActive = v);
        state.WhenAnyValue(x => x.IsGroupWindowOpen).Subscribe(v => rIsGroupWindowOpen = v);
        state.WhenAnyValue(x => x.IsDatWindowOpen).Subscribe(v => rIsDatWindowOpen = v);
        state.WhenAnyValue(x => x.IsMountActive).Subscribe(v => rIsMountActive = v);
        state.WhenAnyValue(x => x.ShowFilterBar).Subscribe(v => rShowFilterBar = v);
        state.WhenAnyValue(x => x.FilteredImageCount).Subscribe(v => rFilteredImageCount = v);
        state.WhenAnyValue(x => x.OperationInProgressOnSet).Subscribe(v => rOperationInProgressOnSet = v);
        state.WhenAnyValue(x => x.SelectedSetName).Subscribe(v => rSelectedSetName = v);
        state.WhenAnyValue(x => x.AvailableSetNames).Subscribe(v => rAvailableSetNames = v);

        // Write all values
        state.HasSelectedImages = hasSelectedImages;
        state.HasRemovedImages = hasRemovedImages;
        state.HasDataStoreOpen = hasDataStoreOpen;
        state.IsOperationActive = isOperationActive;
        state.IsGroupWindowOpen = isGroupWindowOpen;
        state.IsDatWindowOpen = isDatWindowOpen;
        state.IsMountActive = isMountActive;
        state.ShowFilterBar = showFilterBar;
        state.FilteredImageCount = filteredImageCount;
        state.OperationInProgressOnSet = operationInProgress;
        state.SelectedSetName = selectedSetName;
        state.AvailableSetNames = availableSetNames;

        // Verify all subscribers received correct values
        return rHasSelectedImages == hasSelectedImages
            && rHasRemovedImages == hasRemovedImages
            && rHasDataStoreOpen == hasDataStoreOpen
            && rIsOperationActive == isOperationActive
            && rIsGroupWindowOpen == isGroupWindowOpen
            && rIsDatWindowOpen == isDatWindowOpen
            && rIsMountActive == isMountActive
            && rShowFilterBar == showFilterBar
            && rFilteredImageCount == filteredImageCount
            && rOperationInProgressOnSet == operationInProgress
            && rSelectedSetName == selectedSetName
            && rAvailableSetNames != null && rAvailableSetNames.SequenceEqual(availableSetNames);
    }
}