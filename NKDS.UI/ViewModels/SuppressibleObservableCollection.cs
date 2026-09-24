using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NkdsUi.ViewModels;

/// <summary>
/// An ObservableCollection that supports suppressing CollectionChanged notifications.
/// Used by <see cref="BatchCoalescer{T}"/> to coalesce multiple mutations into a single
/// Reset notification. This approach is AOT-safe (no reflection required).
/// </summary>
public class SuppressibleObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// When true, CollectionChanged notifications are suppressed.
    /// Set by <see cref="BatchCoalescer{T}"/> during batch operations.
    /// </summary>
    public bool SuppressNotifications { get; set; }

    /// <summary>
    /// Creates a new empty SuppressibleObservableCollection.
    /// </summary>
    public SuppressibleObservableCollection()
    {
    }

    /// <summary>
    /// Creates a new SuppressibleObservableCollection populated with the specified items.
    /// </summary>
    public SuppressibleObservableCollection(IEnumerable<T> collection) : base(collection)
    {
    }

    /// <summary>
    /// Overrides the notification mechanism to suppress events when
    /// <see cref="SuppressNotifications"/> is true.
    /// </summary>
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!SuppressNotifications)
        {
            base.OnCollectionChanged(e);
        }
    }

    /// <summary>
    /// Raises a Reset notification to inform subscribers that the collection
    /// has been substantially changed. Called by <see cref="BatchCoalescer{T}.EndBatch"/>.
    /// </summary>
    public void RaiseReset() => OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
}