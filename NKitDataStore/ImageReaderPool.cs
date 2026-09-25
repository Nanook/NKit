using NKitDataStore.Interfaces;
using System.Collections.Concurrent;

namespace NKitDataStore
{
    // Simple reference-counted pool for IImageReader instances keyed by image global key string.
    // Keeps readers alive for a short TTL after last release to avoid expensive reopen churn from Dokan/FUSE.
    public static class ImageReaderPool
    {
        private class Entry
        {
            public IImageReader Reader;
            public int RefCount;
            public Timer? DisposeTimer;
            public readonly object Lock = new object();

            public Entry(IImageReader reader)
            {
                Reader = reader;
                RefCount = 0;
            }
        }

        private static readonly ConcurrentDictionary<string, Entry> _Entries = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Soft-close TTL in milliseconds (default 30s)
        private static readonly int _SoftCloseMs = 30_000;

        // Acquire or create a shared reader for the given key. 'create' will be invoked only when needed.
        public static IImageReader Acquire(string key, Func<IImageReader> create)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (create == null) throw new ArgumentNullException(nameof(create));

            Entry entry = _Entries.GetOrAdd(key, _ => new Entry(create()));

            lock (entry.Lock)
            {
                try { entry.DisposeTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                entry.DisposeTimer = null;
                entry.RefCount++;
                return entry.Reader;
            }
        }

        // Release a previously acquired reader. If no references remain, start a soft-close timer.
        public static void Release(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (!_Entries.TryGetValue(key, out Entry? entry))
                return;

            lock (entry.Lock)
            {
                if (entry.RefCount > 0)
                    entry.RefCount--;

                if (entry.RefCount == 0)
                {
                    try
                    {
                        entry.DisposeTimer = new Timer(_ => disposeIfUnused(key, entry), null, _SoftCloseMs, Timeout.Infinite);
                    }
                    catch { }
                }
            }
        }

        private static void disposeIfUnused(string key, Entry entry)
        {
            lock (entry.Lock)
            {
                if (entry.RefCount != 0)
                    return; // someone re-acquired

                try { entry.Reader.Dispose(); } catch { }
                try { entry.DisposeTimer?.Dispose(); } catch { }
                _Entries.TryRemove(key, out _);
            }
        }

        // Force immediate dispose regardless of refcount (use for real shutdown/unmount)
        public static void ForceDispose(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_Entries.TryRemove(key, out Entry? entry))
                return;

            lock (entry.Lock)
            {
                try { entry.DisposeTimer?.Dispose(); } catch { }
                try { entry.Reader.Dispose(); } catch { }
            }
        }

        // Dispose all pooled readers immediately
        public static void Shutdown()
        {
            foreach (KeyValuePair<string, Entry> kv in _Entries.ToArray())
            {
                ForceDispose(kv.Key);
            }
        }
    }
}