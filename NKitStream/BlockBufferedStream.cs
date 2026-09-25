using System.Buffers;

namespace NKitStream
{
    /// <summary>
    /// Abstract base class that provides block-based buffering with peek, seek and rewind
    /// over a sequential source. Subclasses implement <see cref="ReadSourceAsync"/> to supply
    /// the raw forward-only data; all caching, spanning reads and memory management are handled
    /// by this class.
    /// <para>
    /// By default reads pass straight through to the source with zero overhead. Call
    /// <see cref="BeginBuffering"/> when you know a rewind is coming; this starts caching
    /// reads into blocks. Call <see cref="EndBuffering"/> when done; any remaining cached
    /// data ahead of <see cref="Position"/> is drained automatically, then the stream
    /// returns to pass-through mode.
    /// </para>
    /// </summary>
    public abstract class BlockBufferedStream : IDisposable
    {
        private readonly int _blockSize;

        // Blocks are kept in insertion order. _blocks[0] corresponds to the block that
        // contains _firstBlockOffset.  When blocks are released the list is trimmed from
        // the front and _firstBlockOffset advances.
        private readonly List<Block> _blocks = new();
        private long _firstBlockOffset; // absolute stream offset of _blocks[0]

        private long _position;   // current read cursor (absolute)
        private long _floor;      // earliest retained offset — Seek/Rewind below this throws
        private long _buffered;   // one past the highest byte fetched from source
        private bool _buffering;  // true while between BeginBuffering/EndBuffering
        private bool _sourceExhausted;
        private bool _disposed;

        /// <summary>
        /// Creates a new <see cref="BlockBufferedStream"/> with the specified block size.
        /// </summary>
        /// <param name="blockSize">
        /// Size of each internal block in bytes. Must be a positive integer. Default is 65536 (64 KB).
        /// </param>
        protected BlockBufferedStream(int blockSize = 65536)
        {
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize), "Block size must be a positive integer.");

            _blockSize = blockSize;
        }

        /// <summary>Current logical read position (absolute byte offset).</summary>
        public long Position => _position;

        /// <summary>Earliest retained offset. Seeking before this throws.</summary>
        public long Floor => _floor;

        /// <summary>One past the highest byte fetched from the source into cache.</summary>
        public long Buffered => _buffered;

        /// <summary>True once the source returned 0 from a read (end of data).</summary>
        public bool IsSourceExhausted => _sourceExhausted;

        /// <summary>True while between <see cref="BeginBuffering"/> and <see cref="EndBuffering"/>.</summary>
        public bool IsBuffering => _buffering;

        // ── Source contract ────────────────────────────────────────────

        /// <summary>
        /// Read the next bytes from the underlying sequential source.
        /// Implementations must not seek — the base class calls this strictly in order.
        /// Return 0 to signal end of source data.
        /// </summary>
        protected abstract ValueTask<int> ReadSourceAsync(Memory<byte> buffer, CancellationToken ct);

        /// <summary>
        /// Synchronous version of <see cref="ReadSourceAsync"/>.
        /// Override this to enable the synchronous <see cref="Read(Span{byte})"/> and
        /// <see cref="Peek(Span{byte})"/> methods. The default implementation throws
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        protected virtual int ReadSource(Span<byte> buffer)
        {
            throw new NotSupportedException(
                "Synchronous reads are not supported. Override ReadSource(Span<byte>) to enable them.");
        }

        // ── Core reads ─────────────────────────────────────────────────

        /// <summary>
        /// Read up to <paramref name="buffer"/>.Length bytes starting at <see cref="Position"/>
        /// and advance <see cref="Position"/> by the number of bytes read.
        /// Returns 0 only when the source is exhausted and no cached data remains.
        /// </summary>
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            throwIfDisposed();

            if (buffer.Length == 0)
                return 0;

            // Fast path: pass-through when not buffering, no cached blocks, and at source frontier.
            if (!_buffering && _blocks.Count == 0 && _position == _buffered)
            {
                int n = await ReadSourceAsync(buffer, ct).ConfigureAwait(false);
                if (n <= 0) { _sourceExhausted = true; return 0; }
                _position += n;
                _buffered += n;
                _floor = _position;
                return n;
            }

            // Block-based path (buffering active or draining cached data).
            int read = await readInternalAsync(_position, buffer, ct).ConfigureAwait(false);
            _position += read;

            // When not actively buffering, auto-release blocks behind Position so
            // we transition back to pass-through once cached data is fully drained.
            if (!_buffering)
                Release(_position);

            return read;
        }

        /// <summary>
        /// Read up to <paramref name="buffer"/>.Length bytes starting at <see cref="Position"/>
        /// without advancing the position. If no cached blocks exist the data is temporarily
        /// cached and will be auto-released on the next <see cref="ReadAsync"/> call.
        /// </summary>
        public async ValueTask<int> PeekAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            throwIfDisposed();

            return await readInternalAsync(_position, buffer, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Synchronous version of <see cref="ReadAsync"/>.
        /// Requires the subclass to override <see cref="ReadSource(Span{byte})"/>.
        /// </summary>
        public int Read(Span<byte> buffer)
        {
            throwIfDisposed();

            if (buffer.Length == 0)
                return 0;

            // Fast path: pass-through when not buffering, no cached blocks, and at source frontier.
            if (!_buffering && _blocks.Count == 0 && _position == _buffered)
            {
                int n = ReadSource(buffer);
                if (n <= 0) { _sourceExhausted = true; return 0; }
                _position += n;
                _buffered += n;
                _floor = _position;
                return n;
            }

            // Block-based path (buffering active or draining cached data).
            int read = readInternal(_position, buffer);
            _position += read;

            if (!_buffering)
                Release(_position);

            return read;
        }

        /// <summary>
        /// Synchronous version of <see cref="PeekAsync"/>.
        /// Requires the subclass to override <see cref="ReadSource(Span{byte})"/>.
        /// </summary>
        public int Peek(Span<byte> buffer)
        {
            throwIfDisposed();

            return readInternal(_position, buffer);
        }

        // ── Navigation ─────────────────────────────────────────────────

        /// <summary>
        /// Move the read cursor to an absolute offset.
        /// Must be &gt;= <see cref="Floor"/>. If <paramref name="offset"/> is beyond
        /// <see cref="Buffered"/>, data will be fetched from the source on the next read.
        /// </summary>
        public void Seek(long offset)
        {
            throwIfDisposed();

            if (offset < _floor)
                throw new InvalidOperationException(
                    $"Cannot seek to {offset}; floor is {_floor}. Data before the floor has been released.");

            _position = offset;
        }

        // ── Mark / Rewind ──────────────────────────────────────────────

        /// <summary>
        /// Snapshot the current <see cref="Position"/> so it can be restored later
        /// with <see cref="Rewind"/>. Returns the position value as a lightweight token.
        /// </summary>
        public long Mark() => _position;

        /// <summary>
        /// Restore <see cref="Position"/> to a previously marked offset.
        /// The mark must be &gt;= <see cref="Floor"/>.
        /// </summary>
        public void Rewind(long mark) => Seek(mark);

        // ── Buffering control ──────────────────────────────────────────

        /// <summary>
        /// Begin caching reads into blocks so that backward <see cref="Seek"/>,
        /// <see cref="Rewind"/> and <see cref="PeekAsync"/> have cached data to serve.
        /// Reads that occur while buffering is active are retained until
        /// <see cref="Release"/> or <see cref="EndBuffering"/> frees them.
        /// </summary>
        public void BeginBuffering()
        {
            throwIfDisposed();

            if (_buffering)
                return;

            _buffering = true;
        }

        /// <summary>
        /// Stop caching reads. Blocks behind <see cref="Position"/> are released immediately.
        /// Any cached data ahead of <see cref="Position"/> (from a prior seek/read) is retained
        /// and drained automatically by subsequent <see cref="ReadAsync"/> calls before the
        /// stream transitions back to pass-through mode.
        /// </summary>
        public void EndBuffering()
        {
            throwIfDisposed();

            if (!_buffering)
                return;

            _buffering = false;

            // Release blocks behind the current position; blocks ahead (if any)
            // will be drained by ReadAsync which auto-releases when !_buffering.
            Release(_position);
        }

        // ── Memory management ──────────────────────────────────────────

        /// <summary>
        /// Release all cached blocks whose data lies entirely before <paramref name="offset"/>.
        /// After this call, <see cref="Floor"/> is at least <paramref name="offset"/> and any
        /// attempt to seek or rewind before it will throw.
        /// </summary>
        public void Release(long offset)
        {
            throwIfDisposed();

            if (offset <= _floor)
                return;

            _floor = offset;
            trimBlocks();
        }

        // ── Dispose ────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            returnAllBlocks();
            GC.SuppressFinalize(this);
        }

        // ── Internal implementation ────────────────────────────────────

        /// <summary>
        /// Async: read from cache and/or source starting at <paramref name="offset"/>,
        /// filling as much of <paramref name="buffer"/> as possible.
        /// </summary>
        private async ValueTask<int> readInternalAsync(long offset, Memory<byte> buffer, CancellationToken ct)
        {
            if (buffer.Length == 0)
                return 0;

            await ensureBufferedAsync(offset, buffer.Length, ct).ConfigureAwait(false);
            return copyFromBlocks(offset, buffer.Span);
        }

        /// <summary>
        /// Sync: read from cache and/or source starting at <paramref name="offset"/>,
        /// filling as much of <paramref name="buffer"/> as possible.
        /// </summary>
        private int readInternal(long offset, Span<byte> buffer)
        {
            if (buffer.Length == 0)
                return 0;

            ensureBuffered(offset, buffer.Length);
            return copyFromBlocks(offset, buffer);
        }

        /// <summary>
        /// Async: fetch from the source until we have cached data up to at least
        /// <paramref name="offset"/> + <paramref name="desired"/> bytes, or the source is exhausted.
        /// </summary>
        private async ValueTask ensureBufferedAsync(long offset, int desired, CancellationToken ct)
        {
            long need = offset + desired;

            // When no blocks exist, anchor _firstBlockOffset to the current
            // _buffered position so we don't allocate thousands of gap blocks
            // after a long stretch of pass-through reads.
            if (_blocks.Count == 0)
                _firstBlockOffset = _buffered - (_buffered % _blockSize);

            while (_buffered < need && !_sourceExhausted)
            {
                // Allocate (or reuse) a block for the current _buffered position.
                Block block = getOrAllocateBlockAt(_buffered);
                int offsetInBlock = (int)(_buffered % _blockSize);
                int space = _blockSize - offsetInBlock;

                int read = await ReadSourceAsync(block.Data.AsMemory(offsetInBlock, space), ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    _sourceExhausted = true;
                    break;
                }

                int newValid = offsetInBlock + read;
                if (newValid > block.ValidBytes)
                    block.ValidBytes = newValid;

                _buffered += read;
            }
        }

        /// <summary>
        /// Sync: fetch from the source until we have cached data up to at least
        /// <paramref name="offset"/> + <paramref name="desired"/> bytes, or the source is exhausted.
        /// </summary>
        private void ensureBuffered(long offset, int desired)
        {
            long need = offset + desired;

            if (_blocks.Count == 0)
                _firstBlockOffset = _buffered - (_buffered % _blockSize);

            while (_buffered < need && !_sourceExhausted)
            {
                Block block = getOrAllocateBlockAt(_buffered);
                int offsetInBlock = (int)(_buffered % _blockSize);
                int space = _blockSize - offsetInBlock;

                int read = ReadSource(block.Data.AsSpan(offsetInBlock, space));
                if (read <= 0)
                {
                    _sourceExhausted = true;
                    break;
                }

                int newValid = offsetInBlock + read;
                if (newValid > block.ValidBytes)
                    block.ValidBytes = newValid;

                _buffered += read;
            }
        }

        /// <summary>
        /// Copy cached data starting at <paramref name="offset"/> into <paramref name="destination"/>.
        /// Returns the number of bytes actually copied (limited by what is buffered).
        /// </summary>
        private int copyFromBlocks(long offset, Span<byte> destination)
        {
            int totalCopied = 0;

            while (totalCopied < destination.Length)
            {
                long currentOffset = offset + totalCopied;

                if (currentOffset >= _buffered)
                    break; // nothing more cached

                int blockIndex = blockIndexFor(currentOffset);
                if (blockIndex < 0 || blockIndex >= _blocks.Count)
                    break;

                Block block = _blocks[blockIndex];
                int offsetInBlock = (int)(currentOffset % _blockSize);
                int available = block.ValidBytes - offsetInBlock;
                if (available <= 0)
                    break;

                int toCopy = Math.Min(available, destination.Length - totalCopied);
                block.Data.AsSpan(offsetInBlock, toCopy).CopyTo(destination.Slice(totalCopied));
                totalCopied += toCopy;
            }

            return totalCopied;
        }

        // ── Block management ───────────────────────────────────────────

        private int blockIndexFor(long absoluteOffset) => (int)((absoluteOffset - _firstBlockOffset) / _blockSize);

        /// <summary>
        /// Return the block covering <paramref name="absoluteOffset"/>, allocating a new one
        /// from <see cref="ArrayPool{T}"/> if needed.
        /// </summary>
        private Block getOrAllocateBlockAt(long absoluteOffset)
        {
            long blockStart = absoluteOffset - (absoluteOffset % _blockSize);
            int index = blockIndexFor(blockStart);

            // Fast path: block already exists.
            if (index >= 0 && index < _blocks.Count)
                return _blocks[index];

            // Allocate new blocks up to and including the one we need.
            while (index < 0 || index >= _blocks.Count)
            {
                long newBlockStart = _firstBlockOffset + ((long)_blocks.Count * _blockSize);
                byte[] data = ArrayPool<byte>.Shared.Rent(_blockSize);
                _blocks.Add(new Block(newBlockStart, data));

                // Recalculate index — needed when _blocks was empty.
                index = blockIndexFor(blockStart);
            }

            return _blocks[index];
        }

        /// <summary>
        /// Free blocks whose data is entirely before <see cref="_floor"/>.
        /// </summary>
        private void trimBlocks()
        {
            int removeCount = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                long blockEnd = _blocks[i].StartOffset + _blockSize;
                if (blockEnd <= _floor)
                    removeCount++;
                else
                    break;
            }

            for (int i = 0; i < removeCount; i++)
            {
                ArrayPool<byte>.Shared.Return(_blocks[i].Data);
            }

            if (removeCount > 0)
            {
                _firstBlockOffset += (long)removeCount * _blockSize;
                _blocks.RemoveRange(0, removeCount);
            }
        }

        private void returnAllBlocks()
        {
            for (int i = 0; i < _blocks.Count; i++)
                ArrayPool<byte>.Shared.Return(_blocks[i].Data);
            _blocks.Clear();
        }

        private void throwIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        // ── Nested types ───────────────────────────────────────────────

        private sealed class Block
        {
            public readonly long StartOffset;
            public readonly byte[] Data;
            public int ValidBytes;

            public Block(long startOffset, byte[] data)
            {
                StartOffset = startOffset;
                Data = data;
                ValidBytes = 0;
            }
        }

    }
}