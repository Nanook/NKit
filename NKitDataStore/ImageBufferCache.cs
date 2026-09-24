using System.Diagnostics;

namespace NKitDataStore
{
    // Represents a cached buffer with metadata; matches previous internal structure used by ImageBuilder
    internal class BufferCacheEntry
    {
        private static int _NextId = 1;
        public int Id { get; }

        public long AreaId { get; set; }
        public long BufferIndex { get; set; }
        public byte[] Data { get; set; }
        public int ValidDataSize { get; set; }
        public DataStride Stride { get; set; }

        // Track written ranges to detect overlaps (start inclusive, end exclusive)
        public List<(int Start, int End)> WrittenRanges { get; } = new List<(int Start, int End)>();
        public int MaxWrittenOffset { get; private set; } = 0;
        // Thread that created this entry. Used to avoid deadlock when the creating thread
        // attempts to obtain the same entry during population.
        public int CreatorThreadId { get; }

        // Signal that the buffer has been fully populated and is ready for readers.
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        public bool IsReady => _ready.IsSet;

        // The total clean capacity of this buffer (number of clean bytes that correspond to the strided Data length)
        public int CleanCapacity
        {
            get
            {
                if (Stride == null)
                    return Data.Length;
                try
                {
                    long clean = Stride.GetCleanSize(0, Data.Length);
                    return (int)Math.Min(clean, int.MaxValue);
                }
                catch { return Data.Length; }
            }
        }

        // The valid clean data size corresponding to the current ValidDataSize (strided bytes)
        public int CleanValidDataSize
        {
            get
            {
                if (Stride == null)
                    return ValidDataSize;
                try
                {
                    long clean = Stride.GetCleanSize(0, ValidDataSize);
                    return (int)Math.Min(clean, int.MaxValue);
                }
                catch { return ValidDataSize; }
            }
        }

        public BufferCacheEntry(int bufferSize, DataStride stride)
        {
            Id = Interlocked.Increment(ref _NextId);
            CreatorThreadId = Thread.CurrentThread.ManagedThreadId;
            Data = new byte[bufferSize];
            Stride = stride;
            ValidDataSize = 0;
        }

        public bool IsFailed { get; private set; }

        public void SetReady()
        {
            try
            {
                _ready.Set();
            }
            catch { }
        }

        /// <summary>
        /// Marks this entry as failed and signals the ready event so any waiting threads unblock.
        /// Callers should check IsFailed after WaitReady() returns and retry if true.
        /// </summary>
        public void SetFailed()
        {
            IsFailed = true;
            try
            {
                _ready.Set();
            }
            catch { }
        }

        /// <summary>
        /// Blocks the calling thread until the buffer is fully populated or a timeout occurs.
        /// Returns true if the buffer is ready, false if the wait timed out.
        /// The creator thread is never blocked (to avoid deadlock from re-entrancy).
        /// </summary>
        public bool WaitReady()
        {
            // If the current thread created the entry, don't wait to avoid deadlock caused by re-entrancy.
            if (Thread.CurrentThread.ManagedThreadId == CreatorThreadId)
                return true;

            bool signaled = _ready.Wait(TimeSpan.FromSeconds(30));
            if (!signaled)
            {
                Trace.WriteLine($"BufferCacheEntry.WaitReady timed out for entry Id={Id} (AreaId=0x{AreaId:X}, BufferIndex=0x{BufferIndex:X})");
            }
            return signaled;
        }

        public void MarkWritten(int start, int length, string source)
        {
            if (length <= 0)
                return;
            int end = start + length;
            WrittenRanges.Add((start, end));
            if (end > MaxWrittenOffset)
                MaxWrittenOffset = end;
        }
    }

    /// <summary>
    /// Per-image buffer cache with LRU eviction. Thread-safe for GetOrCreate operations.
    /// </summary>
    public class ImageBufferCache : IDisposable
    {
        private readonly LinkedList<BufferCacheEntry> _list = new LinkedList<BufferCacheEntry>();
        private readonly Dictionary<long, LinkedListNode<BufferCacheEntry>> _map = new Dictionary<long, LinkedListNode<BufferCacheEntry>>();
        private readonly object _lock = new object();
        private readonly int _maxSize;
        private readonly int _bufferSize;

        public long ImageId { get; }

        /// <summary>
        /// Gets the current number of cached buffers.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _map.Count;
                }
            }
        }

        public ImageBufferCache(long imageId, int bufferSize, int maxSize)
        {
            ImageId = imageId;
            _bufferSize = bufferSize;
            _maxSize = maxSize;
        }

        internal BufferCacheEntry GetOrCreate(long key, Func<BufferCacheEntry> createFactory)
        {
            const int maxRetries = 3;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                BufferCacheEntry? existingEntry = null;

                lock (_lock)
                {
                    if (_map.TryGetValue(key, out LinkedListNode<BufferCacheEntry>? node) && node != null)
                    {
                        // Promote to MRU
                        if (!ReferenceEquals(_list.First, node))
                        {
                            _list.Remove(node);
                            _list.AddFirst(node);
                        }

                        if (node.Value.IsReady && !node.Value.IsFailed)
                        {
                            // Ready and not failed — return immediately
                            return node.Value;
                        }

                        // Entry exists but is not ready — we need to wait outside the lock
                        existingEntry = node.Value;
                    }
                }

                // If we found a not-ready entry, wait for it outside the lock
                if (existingEntry != null)
                {
                    bool ready = existingEntry.WaitReady();

                    if (ready && !existingEntry.IsFailed)
                    {
                        // Successfully populated by another thread
                        return existingEntry;
                    }

                    // Timed out or failed — remove from cache and retry
                    lock (_lock)
                    {
                        if (_map.TryGetValue(key, out LinkedListNode<BufferCacheEntry>? staleNode) && staleNode != null
                            && ReferenceEquals(staleNode.Value, existingEntry))
                        {
                            _list.Remove(staleNode);
                            _map.Remove(key);
                        }
                    }
                    continue; // retry
                }

                // Cache miss — create new entry under lock
                lock (_lock)
                {
                    // Double-check: another thread may have inserted while we were outside the lock
                    if (_map.TryGetValue(key, out LinkedListNode<BufferCacheEntry>? raceNode) && raceNode != null)
                    {
                        if (!ReferenceEquals(_list.First, raceNode))
                        {
                            _list.Remove(raceNode);
                            _list.AddFirst(raceNode);
                        }

                        if (raceNode.Value.IsReady && !raceNode.Value.IsFailed)
                        {
                            return raceNode.Value;
                        }

                        // Not ready — loop back to wait
                        continue;
                    }

                    // Create new entry via factory and insert into cache.
                    // If the factory throws, no entry is inserted and the exception propagates
                    // so the next caller can retry cleanly.
                    BufferCacheEntry entry = createFactory();

                    LinkedListNode<BufferCacheEntry> newNode = new LinkedListNode<BufferCacheEntry>(entry);
                    _list.AddFirst(newNode);
                    _map[key] = newNode;

                    // Evict if needed
                    while (_list.Count > _maxSize)
                    {
                        LinkedListNode<BufferCacheEntry>? last = _list.Last;
                        if (last == null)
                            break;
                        long oldKey = (last.Value.AreaId << 32) | (uint)last.Value.BufferIndex;
                        _map.Remove(oldKey);
                        _list.RemoveLast();
                    }

                    return entry;
                }
            }

            // Exhausted retries — fall through with one final attempt (no retry protection)
            lock (_lock)
            {
                BufferCacheEntry entry = createFactory();
                LinkedListNode<BufferCacheEntry> newNode = new LinkedListNode<BufferCacheEntry>(entry);
                _list.AddFirst(newNode);
                _map[key] = newNode;

                while (_list.Count > _maxSize)
                {
                    LinkedListNode<BufferCacheEntry>? last = _list.Last;
                    if (last == null)
                        break;
                    long oldKey = (last.Value.AreaId << 32) | (uint)last.Value.BufferIndex;
                    _map.Remove(oldKey);
                    _list.RemoveLast();
                }

                return entry;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                // Signal all entries so any threads blocked in WaitReady() unblock immediately
                foreach (BufferCacheEntry node in _list)
                {
                    node.SetFailed();
                }
                _list.Clear();
                _map.Clear();
            }
        }
    }

    internal static class ImageBufferCacheManager
    {
        private static readonly object _MgrLock = new object();
        private static readonly Dictionary<long, (ImageBufferCache Cache, int RefCount)> _Caches = new Dictionary<long, (ImageBufferCache, int)>();
        private const int _CacheTtlMs = 30000; // keep cache alive for 30s after last release

        public static ImageBufferCache Acquire(long imageId, int bufferSize, int maxCachedBuffers)
        {
            lock (_MgrLock)
            {
                if (_Caches.TryGetValue(imageId, out (ImageBufferCache Cache, int RefCount) v))
                {
                    _Caches[imageId] = (v.Cache, v.RefCount + 1);
                    // try { Debug.WriteLine($"ImageBufferCacheManager.Acquire: reusing cache for image {imageId}, refcount={v.RefCount + 1}"); } catch { }
                    return v.Cache;
                }
                ImageBufferCache cache = new ImageBufferCache(imageId, bufferSize, maxCachedBuffers);
                _Caches[imageId] = (cache, 1);
                // try { Debug.WriteLine($"ImageBufferCacheManager.Acquire: created cache for image {imageId}"); } catch { }
                return cache;
            }
        }

        public static void Release(long imageId)
        {
            // schedule delayed dispose to avoid churn when streams are opened/closed rapidly
            Task.Run(async () =>
            {
                lock (_MgrLock)
                {
                    if (!_Caches.TryGetValue(imageId, out (ImageBufferCache Cache, int RefCount) v)) return;

                    int rc = v.RefCount - 1;
                    if (rc > 0)
                    {
                        _Caches[imageId] = (v.Cache, rc);
                        // try { Trace.WriteLine($"ImageBufferCacheManager.Release: decreased refcount for image {imageId} to {rc}"); } catch { }
                        return;
                    }

                    // decremented to zero, schedule TTL wait outside lock
                    _Caches[imageId] = (v.Cache, 0);
                    // try { Trace.WriteLine($"ImageBufferCacheManager.Release: refcount zero for image {imageId}, scheduling dispose in {_CacheTtlMs}ms"); } catch { }
                }

                await Task.Delay(_CacheTtlMs).ConfigureAwait(false);

                lock (_MgrLock)
                {
                    if (!_Caches.TryGetValue(imageId, out (ImageBufferCache Cache, int RefCount) v2)) return;
                    if (v2.RefCount == 0)
                    {
                        try { v2.Cache.Dispose(); } catch { }
                        _Caches.Remove(imageId);
                        // try { Trace.WriteLine($"ImageBufferCacheManager: disposed cache for image {imageId}"); } catch { }
                    }
                    else
                    {
                        // try { Trace.WriteLine($"ImageBufferCacheManager: cancel dispose for image {imageId}, refcount now {v2.RefCount}"); } catch { }
                    }
                }
            });
        }
    }
}