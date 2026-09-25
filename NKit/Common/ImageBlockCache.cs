using System.Collections.Generic;

namespace Nanook.NKit
{
    internal class ImageBlockCached { public int Count; public byte[] Data; public object Tag; public int Size; }
    internal class ImageBlockCache
    {

        private Dictionary<ulong, ImageBlockCached> _cache;
        private ulong _maxOffset;
        private object _lock;

        public ImageBlockCache(bool readMode)
        {
            ReadMode = readMode;
            _lock = new object();
            _cache = new Dictionary<ulong, ImageBlockCached>();
            _maxOffset = 0;
        }

        public bool ReadMode { get; }

        public void Register(ulong offset, int size)
        {
            if (offset > _maxOffset)
                _maxOffset = offset;
            else
            {
                ImageBlockCached b;
                if (_cache.TryGetValue(offset, out b))
                    b.Count++;
                else
                    _cache.Add(offset, new ImageBlockCached() { Count = 1, Size = size });
            }
        }

        public ImageBlockCached IsRegistered(ulong offset, out bool toSet)
        {
            ImageBlockCached b;
            if (_cache.TryGetValue(offset, out b))
            {
                toSet = b.Data == null;
                if (toSet)
                    b.Data = new byte[b.Size];
                else if ((--b.Count) == 0)
                    _cache.Remove(offset);
                return b;
            }
            toSet = false;
            return null;
        }

        internal ImageBlockCached Register(ulong xxHash, out bool added)
        {
            lock (_lock)
            {
                ImageBlockCached b;
                added = false;
                if (_cache.TryGetValue(xxHash, out b))
                    return b;
                else
                {
                    b = new ImageBlockCached() { Tag = null };
                    added = true;
                    _cache.Add(xxHash, b);
                    return b;
                }
            }
        }
    }
}