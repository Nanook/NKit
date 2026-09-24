using System.Buffers;

namespace NKitDataStore
{
    /// <summary>
    /// A specialized Stream that reads data from an image, automatically handling
    /// block boundaries, offset records, and gaps. This provides a complete file view.
    /// Uses buffer pooling and zero-copy decompression for optimal performance.
    /// 
    /// For multi-offset files (grouped by offsetStart), this stream presents a continuous
    /// view of the file data by concatenating all offset records in order, skipping gaps.
    /// 
    /// Note: BlockPadding offset records are excluded from reading as they are for
    /// stride reconstruction only (used by StridedReadStream).
    /// </summary>
    public class ImageReadStream : Stream
    {
        private readonly ImageReader _reader;
        private long _streamSize;
        private readonly bool _isGroupedStream; // True if this represents a multi-offset file group
        private readonly int _blockSize;
        private readonly List<OffsetRecord> _offsets;

        // Pre-computed prefix sums for O(log N) grouped position lookup
        private readonly long[] _cumulativeSizes;

        private long _position = 0;
        private bool _isDisposed = false;

        // Cache for current block - avoid repeated decompressions
        private byte[]? _currentBlockData = null;
        private int _currentBlockLength = 0;
        private int _currentBlockDataOffset = 0; // Offset within _currentBlockData to start reading
        private BlockKey? _currentBlockKey = null;
        // Per-stream decompression workspace - each stream owns its own buffer so concurrent
        // streams on the same ImageReader do not share or corrupt each other's decompression output.
        private readonly byte[] _decompressionBuffer;

        // Sequential read-ahead cache: one large I/O call reads a run of shard-adjacent blocks.
        // Dedupe writes each image's unique blocks contiguously, so consecutive blocks within
        // the same OffsetRecord are typically adjacent in the shard — one read covers many blocks.
        private byte[]? _rawRunBuffer;           // raw bytes for the current run
        private OffsetRecord? _rawRunOffset;     // OffsetRecord that owns this run
        private int _rawRunStartBlockIdx = -1;   // block index of the first block in the run
        private int _rawRunBlockCount;           // number of blocks in the run
        private int[]? _rawRunLocalOffsets;      // _rawRunLocalOffsets[i] = offset of block[startIdx+i] within _rawRunBuffer
        private int[]? _rawRunLocalSizes;        // _rawRunLocalSizes[i]   = raw size  of block[startIdx+i] within _rawRunBuffer

        internal ImageReadStream(ImageReader reader, long streamSize, int blockSize, List<OffsetRecord>? offsets = null, bool isGroupedStream = false)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _streamSize = streamSize;
            _isGroupedStream = isGroupedStream;
            _blockSize = blockSize;
            _decompressionBuffer = new byte[blockSize];

            // Load and sort offsets - use provided list or get all from reader
            // IMPORTANT: Exclude BlockPadding offsets - they are for stride reconstruction only
            IEnumerable<OffsetRecord> baseOffsets = offsets ?? reader.GetOffsets();

            // Exclude auxiliary files (negative offsets) and padding blocks (stride-only).
            IEnumerable<OffsetRecord> filtered = baseOffsets
                .Where(o => o.Offset >= 0)
                .Where(o => o.Type != BlockType.BlockPadding);

            // Only remove BlockType.Other entries when we constructed the offsets
            // ourselves (offsets == null) for grouped streams. If the caller passed
            // an explicit list of offsets, preserve them (they may refer to real
            // file data such as .tmd/.tik/.cert stored as Other).
            if (offsets == null && _isGroupedStream)
                filtered = filtered.Where(o => o.Type != BlockType.Other);

            _offsets = filtered.OrderBy(o => o.Offset).ToList();

            // If this is a grouped stream and we built the offsets list ourselves,
            // recompute the stream size to reflect any filtering we've applied.
            if (_isGroupedStream && offsets == null)
                _streamSize = _offsets.Sum(o => o.Size);

            // Pre-compute cumulative offset sizes for O(log N) binary search in findOffsetForGroupedPosition
            _cumulativeSizes = new long[_offsets.Count];
            long cumulative = 0;
            for (int i = 0; i < _offsets.Count; i++)
            {
                cumulative += _offsets[i].Size;
                _cumulativeSizes[i] = cumulative;
            }
        }

        public override bool CanRead => !_isDisposed;
        public override bool CanSeek => !_isDisposed;
        public override bool CanWrite => false;
        public override long Length => _streamSize;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        /// <summary>
        /// Reads data from the image, reconstructing it from offset records and blocks.
        /// 
        /// For grouped streams (multi-offset files), stream positions map directly to
        /// concatenated offset data: position 0-99 = first offset, 100-199 = second offset, etc.
        /// 
        /// For non-grouped streams (full image), returns zeros for gaps between offsets.
        /// 
        /// Uses zero-copy decompression and buffer caching for optimal performance.
        /// 
        /// Note: BlockPadding offset records are automatically skipped as they are
        /// intended for stride reconstruction only (StridedReadStream).
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ImageReadStream));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (_position >= _streamSize)
                return 0; // EOF

            int totalBytesRead = 0;
            int bytesRemaining = (int)Math.Min(count, _streamSize - _position);

            while (bytesRemaining > 0)
            {
                if (_isGroupedStream)
                {
                    // Grouped stream: map stream position directly to offset records (no gaps)
                    // Find which offset record contains this stream position
                    (OffsetRecord? offsetRecord, long positionInOffset) = findOffsetForGroupedPosition(_position);

                    if (offsetRecord == null)
                        // Should never happen if streamSize is calculated correctly
                        throw new InvalidOperationException($"Stream position {_position} is beyond all offset records");

                    // Read from this offset record
                    int bytesToRead = (int)Math.Min(offsetRecord.Size - positionInOffset, bytesRemaining);
                    int bytesRead = readFromOffset(offsetRecord, positionInOffset, buffer, offset + totalBytesRead, bytesToRead);

                    _position += bytesRead;
                    totalBytesRead += bytesRead;
                    bytesRemaining -= bytesRead;
                }
                else
                {
                    // Non-grouped stream: use absolute image positions, fill gaps with zeros
                    long absolutePosition = _position;

                    // Find the offset record that contains current position
                    OffsetRecord? offsetRecord = findOffsetAtPosition(absolutePosition);

                    if (offsetRecord == null)
                    {
                        // Gap detected - fill with zeros until next offset or end of request
                        long gapEnd = findNextOffsetStart(absolutePosition);
                        if (gapEnd == -1 || gapEnd > absolutePosition + bytesRemaining)
                            gapEnd = absolutePosition + bytesRemaining;

                        int gapBytes = (int)(gapEnd - absolutePosition);
                        Array.Clear(buffer, offset + totalBytesRead, gapBytes);

                        _position += gapBytes;
                        totalBytesRead += gapBytes;
                        bytesRemaining -= gapBytes;
                    }
                    else
                    {
                        // We have data - read from the offset
                        long offsetWithinRecord = absolutePosition - offsetRecord.Offset;
                        int bytesToRead = (int)Math.Min(offsetRecord.Size - offsetWithinRecord, bytesRemaining);
                        int bytesRead = readFromOffset(offsetRecord, offsetWithinRecord, buffer, offset + totalBytesRead, bytesToRead);

                        _position += bytesRead;
                        totalBytesRead += bytesRead;
                        bytesRemaining -= bytesRead;
                    }
                }
            }

            return totalBytesRead;
        }

        /// <summary>
        /// Finds the offset record and position within it for a grouped stream position.
        /// Grouped streams concatenate all offset records without gaps.
        /// </summary>
        private (OffsetRecord? offsetRecord, long positionInOffset) findOffsetForGroupedPosition(long streamPosition)
        {
            if (_cumulativeSizes.Length == 0)
                return (null, 0);

            // Binary search on prefix sums: find the first entry where cumulative > streamPosition
            int idx = Array.BinarySearch(_cumulativeSizes, streamPosition + 1);
            if (idx < 0) idx = ~idx; // ~idx is the insertion point (first element > streamPosition)
            if (idx >= _offsets.Count) return (null, 0);

            long start = idx > 0 ? _cumulativeSizes[idx - 1] : 0;
            return (_offsets[idx], streamPosition - start);
        }

        /// <summary>
        /// Reads data from a specific offset record at a given position within that record.
        /// On the first access to a block, attempts to coalesce up to MaxReadAheadBlocks
        /// shard-adjacent blocks into a single I/O call and caches the raw result. Subsequent
        /// blocks in the same run are served from the cache without touching the shard file.
        /// </summary>
        private int readFromOffset(OffsetRecord offsetRecord, long positionInOffset, byte[] buffer, int bufferOffset, int count)
        {
            int totalBytesRead = 0;

            while (count > 0 && positionInOffset < offsetRecord.Size)
            {
                int blockIndex = (int)(positionInOffset / _blockSize);
                int positionInBlock = (int)(positionInOffset % _blockSize);

                BlockKey blockKey = offsetRecord.GetBlockAt(blockIndex);

                if (_currentBlockKey == null || !_currentBlockKey.Equals(blockKey))
                {
                    int expectedBlockLength = (int)Math.Min(_blockSize, offsetRecord.Size - ((long)blockIndex * _blockSize));

                    // Step 1 — ensure the run cache covers this block.
                    // If not, attempt to fetch a new run (one syscall for many blocks).
                    bool inRun = object.ReferenceEquals(_rawRunOffset, offsetRecord)
                        && blockIndex >= _rawRunStartBlockIdx
                        && blockIndex < _rawRunStartBlockIdx + _rawRunBlockCount;

                    if (!inRun)
                    {
                        (byte[]? runData, int runLength, int[]? runOffsets, int[]? runSizes) = _reader.ReadBlockRun(offsetRecord, blockIndex);
                        if (runData != null)
                        {
                            _rawRunBuffer = runData;
                            _rawRunOffset = offsetRecord;
                            _rawRunStartBlockIdx = blockIndex;
                            _rawRunBlockCount = runLength;
                            _rawRunLocalOffsets = runOffsets;
                            _rawRunLocalSizes = runSizes;
                            inRun = true;
                        }
                    }

                    // Step 2 — get raw bytes: slice from run buffer or fall back to single-block read.
                    byte[] rawSource;
                    int srcOffset, srcLength;

                    if (inRun)
                    {
                        int i = blockIndex - _rawRunStartBlockIdx;
                        srcOffset = _rawRunLocalOffsets![i];
                        srcLength = _rawRunLocalSizes![i];
                        rawSource = _rawRunBuffer!;
                    }
                    else
                    {
                        byte[]? single = _reader.GetRawBlockData(blockKey);
                        if (single == null)
                            throw new InvalidOperationException($"Block not found: {blockKey}");
                        rawSource = single;
                        srcOffset = 0;
                        srcLength = single.Length;
                    }

                    // Step 3 — decompress into this stream's own buffer (zero-copy when uncompressed).
                    //
                    // Fast path: when reading from the block start with enough room remaining,
                    // decompress directly into the caller's output buffer using the decompressor's
                    // destinationOffset support — no _decompressionBuffer intermediate, no Array.Copy.
                    // buffer.Length - bufferOffset >= count >= expectedBlockLength is guaranteed by
                    // the invariants upheld in Read(), so no explicit bounds check is required.
                    if (srcLength != expectedBlockLength && positionInBlock == 0 && count >= expectedBlockLength)
                    {
                        int decompressedSize = _reader.DecompressBlockDirect(
                            rawSource, srcOffset, srcLength, expectedBlockLength, buffer, bufferOffset);
                        _currentBlockKey = null; // data lives in caller's transient buffer — cannot cache
                        positionInOffset += decompressedSize;
                        bufferOffset += decompressedSize;
                        totalBytesRead += decompressedSize;
                        count -= decompressedSize;
                        continue;
                    }

                    (byte[] decompressedBuffer, int dataOffset, int decompressedLength) =
                        _reader.GetBlockDataInternal(expectedBlockLength, rawSource, srcOffset, srcLength, _decompressionBuffer);

                    _currentBlockData = decompressedBuffer;
                    _currentBlockDataOffset = dataOffset;
                    _currentBlockLength = decompressedLength;
                    _currentBlockKey = blockKey;
                }

                int bytesAvailableInBlock = _currentBlockLength - positionInBlock;
                long bytesAvailableInOffset = offsetRecord.Size - positionInOffset;
                int bytesToRead = (int)Math.Min(Math.Min(bytesAvailableInBlock, bytesAvailableInOffset), count);

                Array.Copy(_currentBlockData!, _currentBlockDataOffset + positionInBlock, buffer, bufferOffset, bytesToRead);

                positionInOffset += bytesToRead;
                bufferOffset += bytesToRead;
                totalBytesRead += bytesToRead;
                count -= bytesToRead;
            }

            return totalBytesRead;
        }

        /// <summary>
        /// Reads a single byte from the stream.
        /// </summary>
        public override int ReadByte()
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
            try
            {
                int bytesRead = Read(buffer, 0, 1);
                return bytesRead == 0 ? -1 : buffer[0];
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Seeks to a specific position in the stream.
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ImageReadStream));

            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _streamSize + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0)
                throw new IOException("Cannot seek before beginning of stream");
            if (newPosition > _streamSize)
                throw new IOException("Cannot seek beyond end of stream");

            _position = newPosition;
            return _position;
        }

        /// <summary>
        /// Finds the offset record that contains the given absolute image position.
        /// Returns null if position is in a gap.
        /// Used for non-grouped streams only.
        /// </summary>
        private OffsetRecord? findOffsetAtPosition(long position)
        {
            foreach (OffsetRecord offset in _offsets)
            {
                if (position >= offset.Offset && position < (offset.Offset + offset.Size))
                    return offset;
            }
            return null;
        }

        /// <summary>
        /// Finds the start position of the next offset after the given position.
        /// Returns -1 if there are no more offsets.
        /// Used for non-grouped streams only.
        /// </summary>
        private long findNextOffsetStart(long position)
        {
            foreach (OffsetRecord offset in _offsets)
            {
                if (offset.Offset > position)
                    return offset.Offset;
            }
            return -1; // No more offsets
        }

        public override void Flush()
        {
            // No-op for read-only stream
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed && disposing)
            {
                _currentBlockData = null;
                _currentBlockKey = null;
                _rawRunBuffer = null;
                _rawRunOffset = null;
                _rawRunLocalOffsets = null;
                _rawRunLocalSizes = null;
                _isDisposed = true;
            }
            base.Dispose(disposing);
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("ImageReadStream does not support writing");

        public override void SetLength(long value) => throw new NotSupportedException("ImageReadStream does not support setting length");
    }
}