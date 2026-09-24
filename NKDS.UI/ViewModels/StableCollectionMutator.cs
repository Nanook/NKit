using NkdsUi.Models;
using NKitDataStore;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Performs in-place mutations on an ObservableCollection while preserving
/// collection identity, sort order, and selection state.
/// </summary>
public class StableCollectionMutator
{
    private readonly ObservableCollection<ImageRowViewModel> _collection;
    private readonly Func<ImageRowViewModel, bool> _filterPredicate;
    private readonly Func<ImageRowViewModel, ImageRowViewModel, int>? _sortComparer;

    public StableCollectionMutator(
        ObservableCollection<ImageRowViewModel> collection,
        Func<ImageRowViewModel, bool> filterPredicate,
        Func<ImageRowViewModel, ImageRowViewModel, int>? sortComparer)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _filterPredicate = filterPredicate ?? throw new ArgumentNullException(nameof(filterPredicate));
        _sortComparer = sortComparer;
    }

    /// <summary>
    /// Inserts a row at the correct sorted position if it passes filters.
    /// Returns the insertion index, or -1 if filtered out.
    /// </summary>
    public int Insert(ImageRowViewModel row)
    {
        if (!_filterPredicate(row))
            return -1;

        int insertIndex = FindSortedInsertIndex(row);
        _collection.Insert(insertIndex, row);
        return insertIndex;
    }

    /// <summary>
    /// Removes rows matching the predicate. Returns the removed rows and their former indices.
    /// Removes in descending index order to preserve indices of earlier items.
    /// </summary>
    public IReadOnlyList<(ImageRowViewModel Row, int Index)> Remove(
        Func<ImageRowViewModel, bool> predicate)
    {
        List<(ImageRowViewModel Row, int Index)> toRemove = new List<(ImageRowViewModel Row, int Index)>();

        for (int i = 0; i < _collection.Count; i++)
        {
            if (predicate(_collection[i]))
                toRemove.Add((_collection[i], i));
        }

        // Remove in descending index order to preserve indices of earlier items
        for (int i = toRemove.Count - 1; i >= 0; i--)
        {
            _collection.RemoveAt(toRemove[i].Index);
        }

        return toRemove;
    }

    /// <summary>
    /// Repositions a row after its sort-key property changed.
    /// </summary>
    public (int OldIndex, int NewIndex) Reposition(ImageRowViewModel row)
    {
        int oldIndex = _collection.IndexOf(row);
        if (oldIndex < 0)
            throw new ArgumentException("Row not found in collection.", nameof(row));

        _collection.RemoveAt(oldIndex);
        int newIndex = FindSortedInsertIndex(row);
        _collection.Insert(newIndex, row);

        return (oldIndex, newIndex);
    }

    /// <summary>
    /// Reconciles the collection against an authoritative set of images.
    /// Performs minimal add/remove/update operations.
    /// Returns a ReconcileResult describing what changed.
    /// </summary>
    public ReconcileResult Reconcile(
        string setName,
        IReadOnlyList<ImageRecord> currentImages,
        Func<ImageRecord, ImageRowViewModel> rowFactory)
    {
        ArgumentNullException.ThrowIfNull(currentImages);
        ArgumentNullException.ThrowIfNull(rowFactory);

        // Build lookup of authoritative images by Id (all share the same setName)
        Dictionary<long, ImageRecord> authoritativeById = new Dictionary<long, ImageRecord>(currentImages.Count);
        for (int i = 0; i < currentImages.Count; i++)
        {
            authoritativeById[currentImages[i].Id] = currentImages[i];
        }

        // Identify existing rows in the collection that belong to this set
        Dictionary<long, (ImageRowViewModel Row, int Index)> existingRowsByKey = new Dictionary<long, (ImageRowViewModel Row, int Index)>();
        for (int i = 0; i < _collection.Count; i++)
        {
            ImageRowViewModel row = _collection[i];
            if (string.Equals(row.SetName, setName, StringComparison.Ordinal))
            {
                existingRowsByKey[row.Id] = (row, i);
            }
        }

        // Determine removals: rows in collection but not in authoritative set.
        // Preserve rows that are in transient processing states (Pending, Processing, AddCancelled)
        // as they are managed by the Add flow and will be updated/removed separately.
        List<(ImageRowViewModel Row, int Index)> toRemove = new List<(ImageRowViewModel Row, int Index)>();
        foreach (KeyValuePair<long, (ImageRowViewModel Row, int Index)> kvp in existingRowsByKey)
        {
            if (!authoritativeById.ContainsKey(kvp.Key))
            {
                // Don't remove rows that are still being managed by an Add operation
                ImageProcessingStatus status = kvp.Value.Row.ProcessingStatus;
                if (status == NkdsUi.Models.ImageProcessingStatus.Pending ||
                    status == NkdsUi.Models.ImageProcessingStatus.Processing ||
                    status == NkdsUi.Models.ImageProcessingStatus.AddCancelled ||
                    status == NkdsUi.Models.ImageProcessingStatus.Cancelled)
                    continue;

                toRemove.Add(kvp.Value);
            }
        }

        // Sort removals by descending index to preserve indices during removal
        toRemove.Sort((a, b) => b.Index.CompareTo(a.Index));

        // Execute removals
        for (int i = 0; i < toRemove.Count; i++)
        {
            _collection.RemoveAt(toRemove[i].Index);
        }

        // Determine updates: rows with same identity but different properties
        List<ImageRowViewModel> updated = new List<ImageRowViewModel>();
        foreach (KeyValuePair<long, ImageRecord> kvp in authoritativeById)
        {
            if (existingRowsByKey.TryGetValue(kvp.Key, out (ImageRowViewModel Row, int Index) existing))
            {
                // This row exists in both sets — check if data changed
                if (HasDataChanged(existing.Row.Image, kvp.Value))
                {
                    UpdateRowInPlace(existing.Row, kvp.Value);
                    updated.Add(existing.Row);
                }
            }
        }

        // Determine additions: images in authoritative set but not in collection
        List<(ImageRowViewModel Row, int Index)> added = new List<(ImageRowViewModel Row, int Index)>();
        foreach (KeyValuePair<long, ImageRecord> kvp in authoritativeById)
        {
            if (!existingRowsByKey.ContainsKey(kvp.Key))
            {
                ImageRowViewModel newRow = rowFactory(kvp.Value);
                if (_filterPredicate(newRow))
                {
                    int insertIndex = FindSortedInsertIndex(newRow);
                    _collection.Insert(insertIndex, newRow);
                    added.Add((newRow, insertIndex));
                }
            }
        }

        return new ReconcileResult
        {
            Removed = toRemove,
            Added = added,
            Updated = updated
        };
    }

    /// <summary>
    /// Reapplies the filter predicate, adding/removing rows as needed
    /// without replacing the collection.
    /// </summary>
    public FilterChangeResult ReapplyFilter(
        IReadOnlyList<ImageRowViewModel> allRows,
        Func<ImageRowViewModel, bool> newPredicate)
    {
        ArgumentNullException.ThrowIfNull(allRows);
        ArgumentNullException.ThrowIfNull(newPredicate);

        // Step 1: Remove rows currently in the collection that don't pass the new predicate
        List<(ImageRowViewModel Row, int Index)> removed = new List<(ImageRowViewModel Row, int Index)>();
        for (int i = 0; i < _collection.Count; i++)
        {
            if (!newPredicate(_collection[i]))
            {
                removed.Add((_collection[i], i));
            }
        }

        // Remove in descending index order to preserve indices of earlier items
        for (int i = removed.Count - 1; i >= 0; i--)
        {
            _collection.RemoveAt(removed[i].Index);
        }

        // Step 2: Build a set of rows currently in the collection for fast lookup
        // Use both reference equality AND identity (SetName+Id) to prevent duplicates
        HashSet<ImageRowViewModel> currentRows = new HashSet<ImageRowViewModel>(
            ReferenceEqualityComparer.Instance);
        HashSet<(string SetName, long Id)> currentRowIds = new HashSet<(string SetName, long Id)>();
        for (int i = 0; i < _collection.Count; i++)
        {
            currentRows.Add(_collection[i]);
            currentRowIds.Add((_collection[i].SetName, _collection[i].Id));
        }

        // Step 3: Insert rows from allRows that pass the new predicate but aren't in the collection
        List<(ImageRowViewModel Row, int Index)> added = new List<(ImageRowViewModel Row, int Index)>();
        for (int i = 0; i < allRows.Count; i++)
        {
            ImageRowViewModel row = allRows[i];
            if (newPredicate(row) && !currentRows.Contains(row) && !currentRowIds.Contains((row.SetName, row.Id)))
            {
                int insertIndex = FindSortedInsertIndex(row);
                _collection.Insert(insertIndex, row);
                added.Add((row, insertIndex));
                currentRowIds.Add((row.SetName, row.Id));
            }
        }

        return new FilterChangeResult
        {
            Removed = removed,
            Added = added
        };
    }

    /// <summary>
    /// Determines whether the authoritative image data differs from the existing row's data.
    /// </summary>
    private static bool HasDataChanged(ImageRecord existing, ImageRecord authoritative)
    {
        return existing.Name != authoritative.Name
            || existing.Size != authoritative.Size
            || existing.Crc32 != authoritative.Crc32
            || existing.XxHash64 != authoritative.XxHash64
            || existing.System != authoritative.System
            || existing.Format != authoritative.Format
            || existing.Removed != authoritative.Removed
            || existing.RollbackFileId != authoritative.RollbackFileId
            || existing.RollbackOffset != authoritative.RollbackOffset;
    }

    /// <summary>
    /// Updates the existing row's underlying ImageRecord properties in place
    /// and raises property changed notifications for any changed passthrough properties.
    /// </summary>
    private static void UpdateRowInPlace(ImageRowViewModel row, ImageRecord authoritative)
    {
        ImageRecord existing = row.Image;

        if (existing.Name != authoritative.Name)
        {
            existing.Name = authoritative.Name;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Name));
        }

        if (existing.Size != authoritative.Size)
        {
            existing.Size = authoritative.Size;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Size));
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.SizeDisplay));
        }

        if (existing.Crc32 != authoritative.Crc32)
        {
            existing.Crc32 = authoritative.Crc32;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Crc32));
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Crc32Hex));
        }

        if (existing.XxHash64 != authoritative.XxHash64)
        {
            existing.XxHash64 = authoritative.XxHash64;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.XxHash64));
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.XxHash64Hex));
        }

        if (existing.System != authoritative.System)
        {
            existing.System = authoritative.System;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.System));
        }

        if (existing.Format != authoritative.Format)
        {
            existing.Format = authoritative.Format;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Format));
        }

        if (existing.Removed != authoritative.Removed)
        {
            existing.Removed = authoritative.Removed;
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.Removed));
            ((ReactiveObject)row).RaisePropertyChanged(nameof(ImageRowViewModel.RemovedDisplay));
        }

        // RollbackFileId and RollbackOffset are not exposed as passthrough properties
        // on ImageRowViewModel, but update them for data consistency
        existing.RollbackFileId = authoritative.RollbackFileId;
        existing.RollbackOffset = authoritative.RollbackOffset;
    }

    /// <summary>
    /// Finds the correct insertion index using binary search when a sort comparer is active,
    /// or returns the end of the collection when no sort is active.
    /// </summary>
    private int FindSortedInsertIndex(ImageRowViewModel row)
    {
        if (_sortComparer == null)
            return _collection.Count;

        int lo = 0, hi = _collection.Count;

        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_sortComparer(_collection[mid], row) <= 0)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}