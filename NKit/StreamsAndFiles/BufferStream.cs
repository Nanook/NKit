using System;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// A seekable view over a shared <see cref="BufferStreamManager"/>.
    ///
    /// <para>
    /// The public constructor wraps a raw source stream and creates a private manager for it.
    /// <see cref="Clone"/> produces another view over the SAME manager, positioned at 0, so the
    /// setup phase (system detection, IAsIso construction, header parsing) and the decoders can
    /// each hold an independently-positioned handle without being aware of the shared cache
    /// underneath. Consuming classes only ever see a normal <see cref="Stream"/>.
    /// </para>
    ///
    /// <para>
    /// Reads/seeks (including the classic <c>Read(buf, off, -count)</c> peek-and-rewind idiom and
    /// backward seeks) are served from the manager's cache when the source is forward-only (e.g. a
    /// disc inside a zip/7z), or straight from the source when it is genuinely seekable. Views
    /// never seek the underlying source backwards.
    /// </para>
    ///
    /// <para>
    /// Memory is bounded by <see cref="ReleaseTo"/>, which the consuming Image calls at the end of
    /// each read with its image-space read position (the end of the data it has returned). The
    /// manager frees whole cache blocks below that mark. Each view is reference counted against the
    /// manager; disposing the last view disposes the underlying source.
    /// </para>
    /// </summary>
    internal sealed class BufferStream : Stream
    {
        // Monotonic per-view id so nested/cloned streams are distinguishable in the log (each view
        // has its own position over a possibly-shared manager, so identity is per view not per
        // manager). Process-wide counter; the exact number only needs to be unique+ordered.
        private static int _nextId;

        private readonly int _id;
        private readonly BufferStreamManager _manager;
        private long _position;
        private bool _disposed;

        /// <summary>Stable id of this view for logging/diagnostics (see <see cref="_nextId"/>).</summary>
        public int Id => _id;

        /// <summary>
        /// Create a view over <paramref name="baseStream"/>, creating a new shared cache manager
        /// for it. Use <see cref="Clone"/> to obtain additional views sharing this manager.
        /// </summary>
        /// <param name="baseStream">The source stream (may be forward-only, e.g. an archive entry).</param>
        /// <param name="log">Optional log for large cache-fill reporting; null disables it.</param>
        public BufferStream(Stream baseStream, ILogScope log = null)
            : this(new BufferStreamManager(baseStream ?? throw new ArgumentNullException(nameof(baseStream)), log))
        {
        }

        private BufferStream(BufferStreamManager manager)
        {
            _id = System.Threading.Interlocked.Increment(ref _nextId);
            _manager = manager;
            _manager.Attach();
            _position = 0;
        }

        /// <summary>
        /// Create another view sharing this view's cache manager, positioned at 0. All views read
        /// through the one manager; the underlying source is only ever read forward once. The log
        /// lives on the shared manager, so every view reports large cache fills.
        /// </summary>
        public BufferStream Clone() => new BufferStream(_manager);

        public override bool CanRead => true;

        // CanSeek reflects the underlying source's true random-access capability:
        //   true  — a genuinely seekable source (local file). Nothing is cached; Seek works
        //           directly and a consumer may freely seek anywhere at no cost.
        //   false — a forward-only source (archive entry). The manager buffers it, so arbitrary
        //           seeks are not "free": a consumer deciding whether to jump to a far offset must
        //           instead ask CanSeekTo(offset), which allows near seeks (served from cache) but
        //           refuses ones that would force the cache to hold an unreasonable span.
        // Reads/seeks still work either way (the cache serves backward peeks and short forward
        // seeks for a forward-only source); CanSeek is only the "is this natively random-access"
        // signal that gates the far-seek optimisations.
        public override bool CanSeek => _manager.Seekable;

        /// <summary>
        /// Whether the consumer should seek to <paramref name="offset"/>. Always true for a
        /// seekable source (<see cref="CanSeek"/> is true — the seek is free). For a forward-only
        /// (cached) source it is true only when reaching the offset would not force the shared
        /// cache to hold an unreasonable span (see <see cref="BufferStreamManager.CanSeekTo"/>).
        /// When false the consumer should fall back to a calculated value rather than triggering a
        /// huge cache fill (e.g. a PS3 far-forward jump of tens of GiB inside an archive).
        /// </summary>
        public bool CanSeekTo(long offset) => _manager.CanSeekTo(offset);

        public override bool CanWrite => false;
        public override long Length => _manager.Length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            // Negative count is the classic peek idiom: read |count| bytes, leave Position unchanged.
            bool peek = count < 0;
            if (peek)
                count = -count;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset + count > buffer.Length)
                count = Math.Max(0, buffer.Length - offset);
            if (count == 0)
                return 0;

            int n = _manager.ReadAt(_position, buffer, offset, count);
            if (!peek)
                _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _manager.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target > _manager.Length)
                target = _manager.Length;
            if (target < 0)
                target = 0;
            // Detail: report every reposition under [In] [BufferStream]. No-op (and no string built)
            // unless Detail logging is enabled, so the hot path is unaffected in Info mode. The
            // per-view _id disambiguates nested/cloned streams.
            _manager.LogSeek(_id, _position, target, origin);
            _position = target;
            return _position;
        }

        /// <summary>
        /// Mark cached data below <paramref name="offset"/> (image/consumer offset) as no longer
        /// needed. The consuming Image calls this at the end of each read with its read position so
        /// the shared cache can free whole blocks below it. No-op for a seekable source.
        /// </summary>
        public void ReleaseTo(long offset) => _manager.ReleaseTo(offset);

        /// <summary>
        /// Mark the lowest offset that may still be seeked back to; the shared cache will not
        /// release below it on subsequent <see cref="ReleaseTo"/> calls. Pass <see cref="long.MaxValue"/>
        /// to clear the constraint. Used by consumers that seek backwards (e.g. XBox FST re-read)
        /// so a forward-only archive source keeps the region they may revisit. No-op for a seekable
        /// source.
        /// </summary>
        public void Retain(long offset) => _manager.Retain(offset);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _manager.Detach();
            }
            base.Dispose(disposing);
        }
    }
}