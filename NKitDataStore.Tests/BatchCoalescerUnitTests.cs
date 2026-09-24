using NkdsUi.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for <see cref="BatchCoalescer{T}"/>.
/// Validates Requirements 5.1, 5.2: batch coalescing suppresses individual notifications
/// and raises a single Reset on EndBatch.
/// </summary>
public class BatchCoalescerUnitTests
{
    // ── 5.1: Individual notifications are suppressed during batch ─────────

    /// <summary>
    /// Validates Requirement 5.1: During a batch, individual Add/Remove
    /// CollectionChanged events are suppressed.
    /// </summary>
    [Fact]
    public void DuringBatch_IndividualNotifications_AreSuppressed()
    {
        // Arrange
        SuppressibleObservableCollection<string> collection = new SuppressibleObservableCollection<string>();
        using BatchCoalescer<string> coalescer = new BatchCoalescer<string>(collection);
        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        // Act
        coalescer.BeginBatch();
        collection.Add("item1");
        collection.Add("item2");
        collection.Add("item3");
        collection.Remove("item2");

        // Assert — no events fired while batch is active
        Assert.Empty(events);
    }

    // ── 5.2: Single Reset raised on EndBatch ─────────────────────────────

    /// <summary>
    /// Validates Requirement 5.2: EndBatch raises exactly one CollectionChanged
    /// event with Action = Reset.
    /// </summary>
    [Fact]
    public void EndBatch_RaisesExactlyOneResetNotification()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        using BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);
        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        coalescer.BeginBatch();
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Act
        coalescer.EndBatch();

        // Assert
        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, events[0].Action);
    }

    // ── 5.1: After EndBatch, notifications resume normally ───────────────

    /// <summary>
    /// Validates Requirement 5.1: After EndBatch, individual notifications
    /// resume as normal.
    /// </summary>
    [Fact]
    public void AfterEndBatch_NotificationsResumeNormally()
    {
        // Arrange
        SuppressibleObservableCollection<string> collection = new SuppressibleObservableCollection<string>();
        using BatchCoalescer<string> coalescer = new BatchCoalescer<string>(collection);

        coalescer.BeginBatch();
        collection.Add("during-batch");
        coalescer.EndBatch();

        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        // Act — add after batch ended
        collection.Add("after-batch");

        // Assert — normal Add notification fires
        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, events[0].Action);
    }

    // ── 5.1, 5.2: Exception safety — EndBatch in finally ─────────────────

    /// <summary>
    /// Validates Requirements 5.1, 5.2: EndBatch in a finally block ensures
    /// notifications are restored even when an exception occurs mid-batch.
    /// </summary>
    [Fact]
    public void ExceptionDuringBatch_EndBatchInFinally_RestoresNotifications()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        using BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);
        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        // Act — simulate exception during batch with EndBatch in finally
        coalescer.BeginBatch();
        try
        {
            collection.Add(1);
            collection.Add(2);
            throw new InvalidOperationException("Simulated failure");
        }
        catch
        {
            // Expected
        }
        finally
        {
            coalescer.EndBatch();
        }

        // Assert — Reset was raised despite exception
        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, events[0].Action);

        // Verify notifications work normally after recovery
        events.Clear();
        collection.Add(99);
        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, events[0].Action);
    }

    // ── 5.1: Nested BeginBatch throws InvalidOperationException ──────────

    /// <summary>
    /// Validates Requirement 5.1: Nested BeginBatch calls throw
    /// InvalidOperationException to prevent misuse.
    /// </summary>
    [Fact]
    public void NestedBeginBatch_ThrowsInvalidOperationException()
    {
        // Arrange
        SuppressibleObservableCollection<string> collection = new SuppressibleObservableCollection<string>();
        using BatchCoalescer<string> coalescer = new BatchCoalescer<string>(collection);

        coalescer.BeginBatch();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => coalescer.BeginBatch());
    }

    // ── EndBatch when not batching is a no-op ────────────────────────────

    /// <summary>
    /// EndBatch when no batch is active should be a safe no-op (no Reset raised).
    /// </summary>
    [Fact]
    public void EndBatch_WhenNotBatching_IsNoOp()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        using BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);
        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        // Act
        coalescer.EndBatch();

        // Assert — no events raised
        Assert.Empty(events);
    }

    // ── IsBatching property reflects state correctly ─────────────────────

    /// <summary>
    /// IsBatching is false initially, true after BeginBatch, false after EndBatch.
    /// </summary>
    [Fact]
    public void IsBatching_ReflectsCorrectState()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        using BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);

        // Assert initial state
        Assert.False(coalescer.IsBatching);

        // Act & Assert after BeginBatch
        coalescer.BeginBatch();
        Assert.True(coalescer.IsBatching);

        // Act & Assert after EndBatch
        coalescer.EndBatch();
        Assert.False(coalescer.IsBatching);
    }

    // ── Dispose ends active batch ────────────────────────────────────────

    /// <summary>
    /// Disposing the coalescer while a batch is active should end the batch
    /// and raise a Reset notification.
    /// </summary>
    [Fact]
    public void Dispose_WithActiveBatch_EndsBatchAndRaisesReset()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);
        List<NotifyCollectionChangedEventArgs> events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        coalescer.BeginBatch();
        collection.Add(1);
        collection.Add(2);

        // Act
        coalescer.Dispose();

        // Assert — Reset raised on dispose
        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, events[0].Action);
        Assert.False(coalescer.IsBatching);
    }

    // ── Constructor rejects null collection ──────────────────────────────

    /// <summary>
    /// Constructor throws ArgumentNullException when collection is null.
    /// </summary>
    [Fact]
    public void Constructor_NullCollection_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => new BatchCoalescer<int>(null!));

    // ── Constructor rejects non-SuppressibleObservableCollection ─────────

    /// <summary>
    /// Constructor throws ArgumentException when collection is a plain
    /// ObservableCollection (not SuppressibleObservableCollection).
    /// </summary>
    [Fact]
    public void Constructor_PlainObservableCollection_ThrowsArgumentException()
    {
        ObservableCollection<int> plainCollection = new global::System.Collections.ObjectModel.ObservableCollection<int>();
        Assert.Throws<ArgumentException>(() => new BatchCoalescer<int>(plainCollection));
    }

    // ── BeginBatch after Dispose throws ObjectDisposedException ──────────

    /// <summary>
    /// BeginBatch after Dispose throws ObjectDisposedException.
    /// </summary>
    [Fact]
    public void BeginBatch_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        SuppressibleObservableCollection<int> collection = new SuppressibleObservableCollection<int>();
        BatchCoalescer<int> coalescer = new BatchCoalescer<int>(collection);
        coalescer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => coalescer.BeginBatch());
    }
}