using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NKitCore
{
    /// <summary>
    /// Represents a logical contiguous region within the stream. Implementations must be thread-safe.
    /// </summary>
    public interface IArea
    {
        int Index { get; }

        /// <summary>Absolute byte offset in the image/stream where this area starts.</summary>
        long ImageOffset { get; }

        /// <summary>Size of the area in bytes. May be null if unknown at creation time.</summary>
        long? Size { get; }

        IReadOnlyDictionary<string, object> Metadata { get; }
        void SetMetadata(string key, object value);
        bool TryGetMetadata<T>(string key, out T value);
        bool TryRemoveMetadata(string key);
        void UpdateSize(long size);
    }

    public sealed class Area : IArea
    {
        private readonly ConcurrentDictionary<string, object> _metadata;
        private long? _size;
        private readonly object _sizeLock = new object();

        public Area(int index, long imageOffset, long? size = null, IDictionary<string, object> initialMetadata = null)
        {
            Index = index;
            ImageOffset = imageOffset;
            _size = size;
            _metadata = initialMetadata != null
                ? new ConcurrentDictionary<string, object>(initialMetadata)
                : new ConcurrentDictionary<string, object>();
        }

        public int Index { get; }
        public long ImageOffset { get; }
        public long? Size { get { lock (_sizeLock) { return _size; } } }
        public IReadOnlyDictionary<string, object> Metadata => _metadata;
        public void SetMetadata(string key, object value) => _metadata[key] = value;

        public bool TryGetMetadata<T>(string key, out T value)
        {
            value = default(T);
            if (_metadata.TryGetValue(key, out object obj) && obj is T)
            {
                value = (T)obj;
                return true;
            }
            return false;
        }

        public bool TryRemoveMetadata(string key) => _metadata.TryRemove(key, out _);

        public void UpdateSize(long size)
        {
            lock (_sizeLock) { _size = size; }
        }
    }
}