using System;
using System.IO;

namespace NKitDataStore.Binary
{
    /// <summary>
    /// A logically contiguous read-only byte buffer backed by lazily-loaded 1 GiB chunks,
    /// supporting buffers larger than <c>Int32.MaxValue</c> (2 GiB) without unsafe code.
    ///
    /// Chunks are loaded on demand from the file and released once the sequential reader has
    /// advanced past them. For the typical forward-only access pattern in
    /// <c>BlockIndexSerializer.Deserialize</c> only one 1 GiB chunk is live in memory at a
    /// time, minimising peak allocation.
    ///
    /// Cross-chunk <see cref="Slice"/> calls (rare — only for section headers that straddle a
    /// chunk boundary) copy to a small temporary array.
    /// </summary>
    internal sealed class LargeByteArray
    {
        private const int ChunkBits = 30;              // 1 GiB per chunk
        private const int ChunkSize = 1 << ChunkBits;  // 0x40000000
        private const int ChunkMask = ChunkSize - 1;

        private readonly byte[][] _chunks;
        private readonly FileStream _stream;
        private readonly object _readLock;
        private readonly long _fileOffset;
        private int _lastChunkIdx = -1;

        /// <summary>Total logical length in bytes.</summary>
        public long Length { get; }

        /// <summary>
        /// Creates a lazy-loading buffer backed by <paramref name="stream"/> at
        /// <paramref name="fileOffset"/>. No data is read until <see cref="Slice"/> is first called.
        /// <paramref name="readLock"/> is used to serialise stream seeks on platforms that lack
        /// position-independent I/O (<c>RandomAccess</c>).
        /// </summary>
        public LargeByteArray(long length, FileStream stream, object readLock, long fileOffset)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length     = length;
            _stream    = stream    ?? throw new ArgumentNullException(nameof(stream));
            _readLock  = readLock  ?? throw new ArgumentNullException(nameof(readLock));
            _fileOffset = fileOffset;

            int chunkCount = (int)((length + ChunkSize - 1) / ChunkSize);
            _chunks = new byte[chunkCount][];
        }

        private byte[] loadChunk(int chunkIdx)
        {
            // Release the previous chunk if we've moved forward — sequential access pattern
            // means we will never need it again.
            if (_lastChunkIdx >= 0 && chunkIdx > _lastChunkIdx)
                _chunks[_lastChunkIdx] = null!; // eligible for GC

            _lastChunkIdx = chunkIdx;

            if (_chunks[chunkIdx] != null)
                return _chunks[chunkIdx];

            long start = (long)chunkIdx * ChunkSize;
            int  size  = (int)Math.Min(ChunkSize, Length - start);
            byte[] chunk = new byte[size];

#if NET6_0_OR_GREATER
            int totalRead = 0;
            while (totalRead < size)
            {
                int read = RandomAccess.Read(
                    _stream.SafeFileHandle,
                    chunk.AsSpan(totalRead),
                    _fileOffset + start + totalRead);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream reading block index chunk {chunkIdx}.");
                totalRead += read;
            }
#else
            lock (_readLock)
            {
                _stream.Position = _fileOffset + start;
                int totalRead = 0;
                while (totalRead < size)
                {
                    int read = _stream.Read(chunk, totalRead, size - totalRead);
                    if (read == 0)
                        throw new EndOfStreamException(
                            $"Unexpected end of stream reading block index chunk {chunkIdx}.");
                    totalRead += read;
                }
            }
#endif
            return _chunks[chunkIdx] = chunk;
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySpan{Byte}"/> over the requested range.
        /// In-chunk slices are returned as a direct view (zero copy).
        /// Cross-chunk slices (uncommon) copy to a temporary array.
        /// Accessing a new chunk automatically releases the previous one.
        /// </summary>
        public ReadOnlySpan<byte> Slice(long offset, int length)
        {
            if (length == 0) return ReadOnlySpan<byte>.Empty;
            if (offset < 0 || offset + length > Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            int chunkIdx = (int)(offset >> ChunkBits);
            int chunkOff = (int)(offset & ChunkMask);

            // Fast path: entirely within one chunk
            if (chunkOff + length <= ChunkSize)
                return loadChunk(chunkIdx).AsSpan(chunkOff, length);

            // Slow path: spans a chunk boundary (section header ~8 bytes max)
            byte[] temp = new byte[length];
            int copied = 0;
            while (copied < length)
            {
                long absPos = offset + copied;
                int ci  = (int)(absPos >> ChunkBits);
                int co  = (int)(absPos & ChunkMask);
                int avail = Math.Min(ChunkSize - co, length - copied);
                loadChunk(ci).AsSpan(co, avail).CopyTo(temp.AsSpan(copied));
                copied += avail;
            }
            return temp;
        }

        /// <summary>Byte indexer (for completeness; prefer <see cref="Slice"/> for bulk access).</summary>
        public byte this[long index]
        {
            get
            {
                int ci = (int)(index >> ChunkBits);
                int co = (int)(index & ChunkMask);
                return loadChunk(ci)[co];
            }
        }
    }
}
