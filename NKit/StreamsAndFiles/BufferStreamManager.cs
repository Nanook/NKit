using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Shared cache over a single underlying source stream, fronting one or more
    /// <see cref="BufferStream"/> views.
    ///
    /// <para>
    /// The pipeline reads a disc image strictly forward at the consumer (Image) level, but the
    /// setup phase (system detection, IAsIso construction, header/partition-table parsing) and the
    /// decoders (ChdAsIso hunk reads, Wii/GcFixAsIso segment moves, DefaultAsIso repositioning)
    /// need random access into the source. When that source is an archive entry it is forward-only
    /// and cannot be seeked backwards. This manager bridges the two: it reads the forward-only
    /// source once, caches it in large blocks, and serves any offset at or above the released
    /// floor to any number of views — each view keeps its own position but shares this one cache.
    /// </para>
    ///
    /// <para>
    /// A genuinely seekable source (a local file) is NOT cached: the manager seeks it directly and
    /// holds nothing, preserving the low-memory behaviour for local images.
    /// </para>
    ///
    /// <para>
    /// Memory is bounded by <see cref="ReleaseTo"/>: the consuming Image marks how far it has
    /// returned data (its image-space read position) at the end of each read; the manager frees
    /// whole blocks that lie entirely below that mark. Offsets at/above the released floor remain
    /// readable; a read or seek below the floor throws.
    /// </para>
    ///
    /// <para>
    /// The manager is reference counted: each attached view increments the count and each disposed
    /// view decrements it. When the last view detaches the underlying source is disposed. Views
    /// are created for the setup phase and disposed as it completes, so once the Image is built
    /// only the single live reader remains attached.
    /// </para>
    /// </summary>
    internal sealed class BufferStreamManager
    {
        // 10 MiB cache blocks rented from the shared array pool. Small blocks keep memory tracking
        // fine-grained (a ReleaseTo frees promptly) and let the pool recycle buffers across the
        // pipeline instead of re-allocating. Block arrays are NOT cleared on rent or return —
        // stream data overwrites the region actually read, and reads are bounded by Valid, so
        // stale bytes are never observed.
        // Section-size buffer / cache block size. Central value: Engine.Core.NKitCoreConsts.
        private const int BlockSize = Engine.Core.NKitCoreConsts.BufferBlockSize;

        // Seek-reach limit for CanSeekTo. A consumer asking to reach an offset more than this far
        // beyond the retained floor would force the cache to hold that whole span; instead the
        // consumer should fall back to a calculated guess. 8.5 GiB is comfortably larger than a
        // Wii dual-layer (~8.15 GiB) or XBox image, so real backward/forward seeks within a disc
        // are always allowed, while a 47 GiB PS3 far-jump is refused.
        private const long SeekReachLimit = 8704L * 1024 * 1024; // 8.5 GiB

        // A cache fill (ensureCached) that pulls at least this many bytes from the forward-only
        // source in one go is reported. This covers BOTH a large Read and a forward Seek over a
        // span (the fill is the same operation); the Spectre live console renders it as a scrolling
        // "Caching…" line so the pause is visibly a cache fill, not a hang.
        // Central value: Engine.Core.NKitCoreConsts.
        private const long CacheLogThreshold = Engine.Core.NKitCoreConsts.CacheLogThreshold;

        private readonly Stream _source;
        private readonly bool _seekable;
        private readonly long _length;
        private readonly ILogScope _log;
        private readonly object _sync = new object();

        // Cached blocks for a forward-only source, in ascending BlockSize-aligned order.
        // _blocks[i] holds block number (_firstBlockNumber + i), i.e. its Start is
        // (_firstBlockNumber + i) * BlockSize. Blocks below the released floor are trimmed from the
        // front and _firstBlockNumber advances. This lets any offset map to a list index in O(1)
        // (absOffset / BlockSize - _firstBlockNumber) without scanning.
        //
        // Memory is bounded solely by ReleaseTo, driven by the consumer's read position
        // (_position / tmppos). There is no frontier-based auto-release: a reader that never
        // releases (or reads a huge image linearly) is expected to drive ReleaseTo, and far
        // forward jumps are refused up front via CanSeekTo rather than cached.
        private readonly List<Block> _blocks = new List<Block>();
        private long _firstBlockNumber; // block number of _blocks[0] (block number = start / BlockSize)
        private long _frontier;    // absolute count of bytes read from the source so far
        private long _floor;       // released-up-to: reads/seeks below this throw
        private long _retainFloor = long.MaxValue; // lowest offset a consumer may still seek back to; never released
        private int _refCount;
        private bool _disposed;

        private sealed class Block
        {
            public long Start;      // absolute offset of Data[0] (BlockSize-aligned)
            public byte[] Data;     // rented from ArrayPool; length >= Size (may be larger)
            public int Size;        // logical block size = min(BlockSize, source length - Start)
            public int Valid;       // bytes populated in Data
            public long End => Start + Valid;
        }

        public BufferStreamManager(Stream source, ILogScope log = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _log = log;
            _length = source.Length;

            // A source that decodes on the fly and verifies a linear checksum (ChdAsIso with a
            // custom checksum active) must be treated as forward-only even though it reports
            // CanSeek: seeking it backwards abandons the verify pass (ChdAsIso.Seek turns verify
            // off on any non-linear reposition), yielding an empty custom checksum. Cache such a
            // source so backward peeks/seeks are served from the cache and the source only ever
            // advances forward. A plain seekable source (local file) is served directly, uncached.
            bool linearVerify = (source as IAsIso)?.CustomChecksums() != null;
            _seekable = source.CanSeek && !linearVerify;
        }

        public long Length => _length;

        /// <summary>True when the underlying source is seekable and served directly (no caching).</summary>
        public bool Seekable => _seekable;

        /// <summary>
        /// Emit a Detail log line for a view Seek. <paramref name="id"/> is the per-view id (leads
        /// the message as "N: ...") so nested/cloned streams are distinguishable. One line per Seek
        /// call so a log filtered on <c>[BufferStream]</c> shows every reposition (useful when
        /// diagnosing where a reader jumped before a crash). Gated on Detail being enabled so there is no cost — and no
        /// string built — in Info mode. The large forward cache-fill Info line (the ~100MiB
        /// "Caching…" message) is emitted separately by <c>ensureCached</c> and is unaffected.
        /// </summary>
        public void LogSeek(int id, long from, long to, SeekOrigin origin)
        {
            if (to == from)
                return; // no-op reposition (cursor already there) — nothing to diagnose, don't flood
            long delta = to - from;
            long absDelta = delta >= 0 ? delta : -delta;
            // Only NOTABLE repositions are worth a line: a backward seek (unusual for a forward
            // reader) or a large forward jump (>= threshold). The small few-byte forward
            // padding/alignment seeks happen on almost every read and are pure noise — they are NOT
            // logged at all (even at Trace), which was flooding the debug output. Notable seeks log
            // at Detail so they surface without needing debug level.
            bool notable = delta < 0 || absDelta >= Engine.Core.NKitCoreConsts.SeekLogDetailThreshold;
            if (!notable)
                return;
            ILogScope scope = _log?.ScopeFor(LogScopes.In);
            if (scope == null || !scope.IsEnabled(LogLevel.Detail))
                return;
            scope.Log(LogLevel.Detail,
                $"{LogScopes.Tag(LogScopes.BufferStream)}{id}: seek {origin} 0x{from:X} -> 0x{to:X} ({(delta >= 0 ? "+" : "-")}0x{absDelta:X}){(_seekable ? "" : " cached")}");
        }

        /// <summary>
        /// Whether a consumer should seek to <paramref name="absOffset"/>. For a genuinely seekable
        /// source the seek is cheap, so this is always true. For a forward-only (cached) source it
        /// is true only when reaching the offset would not force the cache to hold more than
        /// <see cref="SeekReachLimit"/> beyond the retained floor — i.e. the span from the released
        /// floor up to the target. This measures against the floor (not the frontier) so a consumer
        /// cannot defeat the guard by nudging forward repeatedly without releasing: everything it
        /// keeps counts against the limit. When it returns false the consumer must fall back to a
        /// calculated value rather than triggering a huge cache fill (e.g. a 47 GiB PS3 far-jump).
        /// </summary>
        public bool CanSeekTo(long absOffset)
        {
            if (_seekable)
                return true;
            lock (_sync)
            {
                long floor = _floor;
                if (absOffset <= floor)
                    return true; // backward into (or below) the retained window — served from cache
                return absOffset - floor <= SeekReachLimit;
            }
        }

        /// <summary>Lowest absolute offset still readable. Anything below has been released.</summary>
        public long Floor { get { lock (_sync) return _floor; } }

        public void Attach()
        {
            lock (_sync)
                _refCount++;
        }

        /// <summary>Decrement the view count; dispose the underlying source when it reaches zero.</summary>
        public void Detach()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                if (--_refCount > 0)
                    return;
                _disposed = true;
                foreach (Block b in _blocks)
                {
                    if (b.Data.Length > 0)
                        ArrayPool<byte>.Shared.Return(b.Data);
                }
                _blocks.Clear();
                try { _source.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Read up to <paramref name="count"/> bytes at absolute <paramref name="absOffset"/> into
        /// <paramref name="buffer"/>. Returns the number of bytes read (0 at/after EOF, or a short
        /// read near EOF). Never advances any view's position — the caller owns position.
        /// </summary>
        public int ReadAt(long absOffset, byte[] buffer, int offset, int count)
        {
            if (count <= 0 || absOffset >= _length)
                return 0;
            if (absOffset + count > _length)
                count = (int)(_length - absOffset);

            if (_seekable)
            {
                lock (_sync)
                {
                    if (_source.Position != absOffset)
                        _source.Position = absOffset;
                    return readFull(_source, buffer, offset, count);
                }
            }

            lock (_sync)
            {
                if (absOffset < _floor)
                    throw new InvalidOperationException(
                        $"Cannot read at {absOffset}; floor is {_floor}. Data before the floor has been released.");

                ensureCached(absOffset + count);
                return copyFromBlocks(absOffset, buffer, offset, count);
            }
        }

        /// <summary>
        /// Release cached data below <paramref name="absOffset"/>. Whole blocks that end at or
        /// before the (retain-clamped) offset are freed; the floor advances so later reads below it
        /// throw. Never releases below the retain floor set via <see cref="Retain"/>, so data a
        /// consumer may still seek back to is preserved. No-op for a seekable source (uncached).
        /// </summary>
        public void ReleaseTo(long absOffset)
        {
            if (_seekable)
                return;
            lock (_sync)
                releaseToInternal(absOffset);
        }

        // Advance the released floor and trim whole blocks below it. Clamped to _retainFloor so a
        // consumer's declared seek-back region is never freed. Assumes _sync held.
        private void releaseToInternal(long absOffset)
        {
            // Never release below the retain floor — a consumer (e.g. XBox re-reading the FST)
            // has declared it may seek back to there.
            if (absOffset > _retainFloor)
                absOffset = _retainFloor;
            if (absOffset <= _floor)
                return;
            _floor = absOffset;
            int remove = 0;
            while (remove < _blocks.Count && _blocks[remove].Start + _blocks[remove].Size <= _floor)
                remove++;
            if (remove > 0)
            {
                for (int i = 0; i < remove; i++)
                {
                    if (_blocks[i].Data.Length > 0)
                        ArrayPool<byte>.Shared.Return(_blocks[i].Data);
                }
                _blocks.RemoveRange(0, remove);
                _firstBlockNumber += remove;
            }
        }

        /// <summary>
        /// Mark the lowest absolute offset a consumer may still seek back to. <see cref="ReleaseTo"/>
        /// will never free cached data below this. Pass <see cref="long.MaxValue"/> to clear (allow
        /// releasing up to the plain <see cref="ReleaseTo"/> mark again). No-op for a seekable
        /// source (nothing is cached).
        /// </summary>
        public void Retain(long absOffset)
        {
            if (_seekable)
                return;
            lock (_sync)
                _retainFloor = absOffset;
        }

        // ── internals (all called under _sync) ─────────────────────────────────

        // Read the forward-only source ahead until at least 'need' absolute bytes are cached (or
        // the source is exhausted). The source is only ever read forward from _frontier.
        private void ensureCached(long need)
        {
            if (need > _length)
                need = _length;

            // Report a large forward cache fill (a big Read, or a forward Seek over a span — both
            // pull the source from _frontier up to 'need'). Measured once, before the fill loop.
            long fill = need - _frontier;
            if (fill >= CacheLogThreshold)
                _log?.Info(() => $"Caching {fill / (1024 * 1024)}MiB...");

            while (_frontier < need)
            {
                Block b = blockAt(_frontier); // allocates the frontier block on demand
                int inBlock = (int)(_frontier - b.Start);
                int space = b.Size - inBlock;
                // The frontier is always inside the block we just fetched (block sized to the
                // source), so space > 0 here.

                int toRead = (int)Math.Min(space, need - _frontier);
                int n = readFull(_source, b.Data, inBlock, toRead);
                if (n <= 0)
                    break; // source exhausted
                b.Valid = Math.Max(b.Valid, inBlock + n);
                _frontier += n;
            }
        }

        // O(1) list index for the block covering 'absOffset' (block number - first block number).
        private int listIndex(long absOffset) => (int)((absOffset / BlockSize) - _firstBlockNumber);

        // Return the block covering 'absOffset', allocating trailing blocks up to it if needed.
        // Blocks are contiguous and BlockSize-aligned; the last/partial block (and small whole
        // sources) are sized to the actual remaining source length to avoid over-allocating.
        private Block blockAt(long absOffset)
        {
            long blockNumber = absOffset / BlockSize;

            // Re-anchor when there are no retained blocks. ReleaseTo can advance the floor (and so
            // _firstBlockNumber) PAST the current frontier — a decoder's output position can run
            // ahead of the raw bytes fetched — leaving the list empty with a stale anchor. Anchor
            // to the requested block so its list index is 0.
            if (_blocks.Count == 0)
                _firstBlockNumber = blockNumber;

            int idx = listIndex(absOffset);
            if (idx >= 0 && idx < _blocks.Count)
                return _blocks[idx];

            // Append trailing blocks until the requested block exists. The frontier advances one
            // block at a time, so this loops at most once. idx is >= _blocks.Count here (the empty
            // re-anchor above guarantees idx >= 0).
            while (idx >= _blocks.Count)
            {
                long start = (_firstBlockNumber + _blocks.Count) * BlockSize;
                int size = (int)Math.Min(BlockSize, _length - start);
                if (size <= 0)
                    size = 0; // defensive; frontier never advances past _length
                // Rent from the shared pool. The pool returns an array of at least 'size' bytes
                // (often larger) — Size tracks the logical block extent, Data.Length is the pooled
                // capacity. Not cleared: reads overwrite what they use and Valid bounds copies.
                byte[] data = size > 0 ? ArrayPool<byte>.Shared.Rent(size) : Array.Empty<byte>();
                _blocks.Add(new Block { Start = start, Data = data, Size = size, Valid = 0 });
            }
            return _blocks[idx];
        }

        private int copyFromBlocks(long absOffset, byte[] buffer, int offset, int count)
        {
            int copied = 0;
            while (copied < count)
            {
                long at = absOffset + copied;
                if (at >= _frontier)
                    break; // nothing more cached (EOF short read)

                int idx = listIndex(at);
                if (idx < 0 || idx >= _blocks.Count)
                    break;
                Block b = _blocks[idx];
                int inBlock = (int)(at - b.Start);
                int avail = b.Valid - inBlock;
                if (avail <= 0)
                    break;
                int chunk = Math.Min(avail, count - copied);
                Array.Copy(b.Data, inBlock, buffer, offset + copied, chunk);
                copied += chunk;
            }
            return copied;
        }

        private static int readFull(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, offset + total, count - total);
                if (n == 0)
                    break;
                total += n;
            }
            return total;
        }
    }
}