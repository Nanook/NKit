using NKitDataStore.Interfaces;
using System.Collections.Concurrent;

namespace NKitDataStore
{
    /// <summary>
    /// Manages shared mount-time resources (ImageReaders and buffer caches) with
    /// reference counting, TTL-based disposal, and deterministic shutdown.
    /// Thread-safe for concurrent acquire/release from multiple FUSE/Dokan threads.
    /// </summary>
    internal class MountResourceManager : IMountResourceManager
    {
        private readonly IDataStore _dataStore;
        private readonly int _readerCapacity;
        private readonly int _ttlMs;
        private readonly ConcurrentDictionary<string, ReaderEntry> _readerCache = new();
        private readonly ConcurrentDictionary<long, BufferCachePoolEntry> _bufferCaches = new();
        private readonly ConcurrentDictionary<string, OffsetsManagerEntry> _offsetsCache = new();
        private readonly LinkedList<string> _lruOrder = new();
        private readonly object _lruLock = new();
        private volatile bool _isShutdown = false;

        public MountResourceManager(IDataStore dataStore, int readerCapacity = 32, int ttlMs = 30000)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _readerCapacity = readerCapacity;
            _ttlMs = ttlMs;
        }

        public IImageReader AcquireReader(string setName, long imageId)
        {
            if (_isShutdown)
                throw new ObjectDisposedException(nameof(MountResourceManager));

            string key = $"{setName}:{imageId}";

            // Get or add the entry to the concurrent dictionary
            ReaderEntry entry = _readerCache.GetOrAdd(key, _ => new ReaderEntry());

            lock (entry.Lock)
            {
                if (_isShutdown)
                    throw new ObjectDisposedException(nameof(MountResourceManager));

                if (entry.Reader == null)
                {
                    // Cache miss — create a new reader
                    entry.Reader = _dataStore.OpenImageReader(new GlobalImageKey(setName, imageId));
                }

                // Cancel any pending TTL timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                entry.RefCount++;
            }

            // Update LRU tracking
            promoteToMru(key);

            // Evict if over capacity
            evictIfNeeded();

            return entry.Reader;
        }

        public void ReleaseReader(string setName, long imageId)
        {
            string key = $"{setName}:{imageId}";

            if (!_readerCache.TryGetValue(key, out ReaderEntry? entry))
                return;

            lock (entry.Lock)
            {
                if (entry.RefCount <= 0)
                    return;

                entry.RefCount--;

                if (entry.RefCount == 0 && !_isShutdown)
                {
                    // Start TTL timer — dispose reader after TTL if not re-acquired
                    entry.TtlTimer = new Timer(
                        _ => onTtlExpired(key),
                        null,
                        _ttlMs,
                        Timeout.Infinite);
                }
            }
        }

        public ImageBufferCache AcquireBufferCache(long imageId, int bufferSize, int maxCachedBuffers)
        {
            if (_isShutdown)
                throw new ObjectDisposedException(nameof(MountResourceManager));

            // Get or add the entry to the concurrent dictionary
            BufferCachePoolEntry entry = _bufferCaches.GetOrAdd(imageId, _ => new BufferCachePoolEntry());

            lock (entry.Lock)
            {
                if (_isShutdown)
                    throw new ObjectDisposedException(nameof(MountResourceManager));

                if (entry.Cache == null)
                {
                    // Cache miss — create a new ImageBufferCache
                    entry.Cache = new ImageBufferCache(imageId, bufferSize, maxCachedBuffers);
                }

                // Cancel any pending TTL timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                entry.RefCount++;
            }

            return entry.Cache;
        }

        public void ReleaseBufferCache(long imageId)
        {
            if (!_bufferCaches.TryGetValue(imageId, out BufferCachePoolEntry? entry))
                return;

            lock (entry.Lock)
            {
                if (entry.RefCount <= 0)
                    return;

                entry.RefCount--;

                if (entry.RefCount == 0 && !_isShutdown)
                {
                    // Start TTL timer — dispose cache after TTL if not re-acquired
                    entry.TtlTimer = new Timer(
                        _ => onBufferCacheTtlExpired(imageId),
                        null,
                        _ttlMs,
                        Timeout.Infinite);
                }
            }
        }

        public OffsetsManagerCacheResult AcquireOffsetsManager(string setName, long imageId, int sectionSize, int storeBlockSize)
        {
            if (_isShutdown)
                throw new ObjectDisposedException(nameof(MountResourceManager));

            string key = $"{setName}:{imageId}";
            OffsetsManagerEntry entry = _offsetsCache.GetOrAdd(key, _ => new OffsetsManagerEntry());

            lock (entry.Lock)
            {
                if (_isShutdown)
                    throw new ObjectDisposedException(nameof(MountResourceManager));

                if (entry.Result == null)
                {
                    // Cache miss — build the OffsetsManager
                    IImageReader reader = AcquireReader(setName, imageId);
                    try
                    {
                        List<AreaRecord> areas = reader.GetAreas().OrderBy(a => a.Offset).ToList();
                        Dictionary<long, OffsetRecord> offsetsByPosition = new Dictionary<long, OffsetRecord>();
                        foreach (OffsetRecord? offset in reader.GetOffsets().OrderBy(o => o.Offset))
                            offsetsByPosition[offset.Offset] = offset;
                        OffsetsManager offsetsManager = new OffsetsManager(reader, sectionSize: sectionSize, storeBlockSize: storeBlockSize);
                        entry.Result = new OffsetsManagerCacheResult(offsetsManager, areas, offsetsByPosition);
                    }
                    finally
                    {
                        ReleaseReader(setName, imageId);
                    }
                }

                // Cancel any pending TTL timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                entry.RefCount++;
                return entry.Result;
            }
        }

        public void ReleaseOffsetsManager(string setName, long imageId)
        {
            string key = $"{setName}:{imageId}";

            if (!_offsetsCache.TryGetValue(key, out OffsetsManagerEntry? entry))
                return;

            lock (entry.Lock)
            {
                if (entry.RefCount <= 0)
                    return;

                entry.RefCount--;

                if (entry.RefCount == 0 && !_isShutdown)
                {
                    // Start TTL timer — remove entry after TTL if not re-acquired
                    entry.TtlTimer = new Timer(
                        _ => onOffsetsManagerTtlExpired(key),
                        null,
                        _ttlMs,
                        Timeout.Infinite);
                }
            }
        }

        public void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            // 1. Cancel all OffsetsManager TTL timers and clear offsets cache (before readers)
            foreach (KeyValuePair<string, OffsetsManagerEntry> kvp in _offsetsCache)
            {
                OffsetsManagerEntry entry = kvp.Value;
                lock (entry.Lock)
                {
                    if (entry.TtlTimer != null)
                    {
                        entry.TtlTimer.Dispose();
                        entry.TtlTimer = null;
                    }

                    entry.Result = null;
                }
            }
            _offsetsCache.Clear();

            // 2. Cancel all buffer cache TTL timers and dispose all buffer caches
            foreach (KeyValuePair<long, BufferCachePoolEntry> kvp in _bufferCaches)
            {
                BufferCachePoolEntry entry = kvp.Value;
                lock (entry.Lock)
                {
                    if (entry.TtlTimer != null)
                    {
                        entry.TtlTimer.Dispose();
                        entry.TtlTimer = null;
                    }

                    if (entry.Cache != null)
                    {
                        try { entry.Cache.Dispose(); } catch { }
                        entry.Cache = null;
                    }
                }
            }
            _bufferCaches.Clear();

            // 3. Cancel all reader TTL timers and dispose all readers
            foreach (KeyValuePair<string, ReaderEntry> kvp in _readerCache)
            {
                ReaderEntry entry = kvp.Value;
                lock (entry.Lock)
                {
                    if (entry.TtlTimer != null)
                    {
                        entry.TtlTimer.Dispose();
                        entry.TtlTimer = null;
                    }

                    if (entry.Reader != null)
                    {
                        try { entry.Reader.Dispose(); } catch { }
                        entry.Reader = null;
                    }
                }
            }
            _readerCache.Clear();

            // 4. Clear LRU tracking
            lock (_lruLock)
            {
                _lruOrder.Clear();
            }
        }

        public void Dispose()
        {
            if (!_isShutdown)
            {
                Shutdown();
            }
        }

        private void onTtlExpired(string key)
        {
            if (!_readerCache.TryGetValue(key, out ReaderEntry? entry))
                return;

            lock (entry.Lock)
            {
                // Only dispose if still at zero refs (wasn't re-acquired during TTL)
                if (entry.RefCount > 0)
                    return;

                // Dispose the timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                // Dispose the reader
                try { entry.Reader?.Dispose(); } catch { }
                entry.Reader = null;
            }

            // Remove from cache and LRU
            _readerCache.TryRemove(key, out _);
            removeFromLru(key);
        }

        private void onBufferCacheTtlExpired(long imageId)
        {
            if (!_bufferCaches.TryGetValue(imageId, out BufferCachePoolEntry? entry))
                return;

            lock (entry.Lock)
            {
                // Only dispose if still at zero refs (wasn't re-acquired during TTL)
                if (entry.RefCount > 0)
                    return;

                // Dispose the timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                // Dispose the cache
                try { entry.Cache?.Dispose(); } catch { }
                entry.Cache = null;
            }

            // Remove from dictionary
            _bufferCaches.TryRemove(imageId, out _);
        }

        /// <summary>
        /// Synchronously forces TTL expiry for the offsets manager entry with the given key.
        /// Intended for unit tests only — avoids relying on wall-clock timer scheduling.
        /// </summary>
        internal void ForceOffsetsManagerExpiry(string key) => onOffsetsManagerTtlExpired(key);

        private void onOffsetsManagerTtlExpired(string key)
        {
            if (_isShutdown)
                return;

            if (!_offsetsCache.TryGetValue(key, out OffsetsManagerEntry? entry))
                return;

            lock (entry.Lock)
            {
                // Only remove if still at zero refs (wasn't re-acquired during TTL)
                if (entry.RefCount > 0)
                    return;

                // Dispose the timer
                if (entry.TtlTimer != null)
                {
                    entry.TtlTimer.Dispose();
                    entry.TtlTimer = null;
                }

                // Null out the result (OffsetsManager doesn't implement IDisposable)
                entry.Result = null;
            }

            // Remove from dictionary
            _offsetsCache.TryRemove(key, out _);
        }

        private void promoteToMru(string key)
        {
            lock (_lruLock)
            {
                // Remove existing position if present
                _lruOrder.Remove(key);
                // Add to front (most recently used)
                _lruOrder.AddFirst(key);
            }
        }

        private void removeFromLru(string key)
        {
            lock (_lruLock)
            {
                _lruOrder.Remove(key);
            }
        }

        private void evictIfNeeded()
        {
            while (_readerCache.Count > _readerCapacity)
            {
                string? keyToEvict = null;

                lock (_lruLock)
                {
                    // Walk from the tail (least recently used) to find an evictable entry
                    LinkedListNode<string>? node = _lruOrder.Last;
                    while (node != null)
                    {
                        if (_readerCache.TryGetValue(node.Value, out ReaderEntry? candidate))
                        {
                            // Only evict entries with ref count 0
                            lock (candidate.Lock)
                            {
                                if (candidate.RefCount == 0)
                                {
                                    keyToEvict = node.Value;
                                    _lruOrder.Remove(node);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Entry no longer in cache, clean up LRU
                            LinkedListNode<string>? prev = node.Previous;
                            _lruOrder.Remove(node);
                            node = prev;
                            continue;
                        }
                        node = node.Previous;
                    }
                }

                if (keyToEvict == null)
                    break; // No evictable entries found

                // Evict the entry
                if (_readerCache.TryRemove(keyToEvict, out ReaderEntry? evicted))
                {
                    lock (evicted.Lock)
                    {
                        if (evicted.TtlTimer != null)
                        {
                            evicted.TtlTimer.Dispose();
                            evicted.TtlTimer = null;
                        }
                        try { evicted.Reader?.Dispose(); } catch { }
                        evicted.Reader = null;
                    }
                }
            }
        }

        private class ReaderEntry
        {
            public IImageReader? Reader;
            public int RefCount;
            public Timer? TtlTimer;
            public readonly object Lock = new object();
        }

        private class BufferCachePoolEntry
        {
            public ImageBufferCache? Cache;
            public int RefCount;
            public Timer? TtlTimer;
            public readonly object Lock = new object();
        }

        private class OffsetsManagerEntry
        {
            public OffsetsManagerCacheResult? Result;
            public int RefCount;
            public Timer? TtlTimer;
            public readonly object Lock = new object();
        }
    }
}