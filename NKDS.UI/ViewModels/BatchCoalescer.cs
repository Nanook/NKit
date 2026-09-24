using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Provides batch operation support for ObservableCollection.
/// Suppresses individual CollectionChanged events during a batch,
/// raising a single Reset notification on completion.
/// </summary>
/// <remarks>
/// The collection passed to this class must be a <see cref="SuppressibleObservableCollection{T}"/>
/// to enable event suppression. This is an AOT-safe approach that avoids reflection.
/// </remarks>
public class BatchCoalescer<T> : IDisposable
{
    private readonly SuppressibleObservableCollection<T> _collection;
    private bool _disposed;

    /// <summary>
    /// Whether a batch is currently active.
    /// </summary>
    public bool IsBatching { get; private set; }

    /// <summary>
    /// Creates a new BatchCoalescer wrapping the specified collection.
    /// </summary>
    /// <param name="collection">
    /// The collection to coalesce notifications for.
    /// Must be a <see cref="SuppressibleObservableCollection{T}"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when collection is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when collection is not a <see cref="SuppressibleObservableCollection{T}"/>.
    /// </exception>
    public BatchCoalescer(ObservableCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (collection is not SuppressibleObservableCollection<T> suppressible)
        {
            throw new ArgumentException(
                $"Collection must be a {nameof(SuppressibleObservableCollection<T>)} to support batch coalescing.",
                nameof(collection));
        }

        _collection = suppressible;
    }

    /// <summary>
    /// Begins a batch operation. All mutations until EndBatch() are coalesced
    /// into a single Reset notification.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if a batch is already active.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    public void BeginBatch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsBatching)
            throw new InvalidOperationException("A batch operation is already in progress. Nested batches are not supported.");

        IsBatching = true;
        _collection.SuppressNotifications = true;
    }

    /// <summary>
    /// Ends the batch and raises a single CollectionChanged Reset notification.
    /// Safe to call even if no batch is active (no-op in that case).
    /// </summary>
    /// <remarks>
    /// This method should always be called in a finally block to prevent stuck batch state:
    /// <code>
    /// coalescer.BeginBatch();
    /// try
    /// {
    ///     // ... mutations ...
    /// }
    /// finally
    /// {
    ///     coalescer.EndBatch();
    /// }
    /// </code>
    /// </remarks>
    public void EndBatch()
    {
        if (!IsBatching)
            return;

        IsBatching = false;
        _collection.SuppressNotifications = false;
        _collection.RaiseReset();
    }

    /// <summary>
    /// Disposes the coalescer, ending any active batch.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (IsBatching)
        {
            EndBatch();
        }

        _disposed = true;
    }
}