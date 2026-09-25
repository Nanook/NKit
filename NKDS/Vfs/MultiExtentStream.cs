using NKitDataStore.Interfaces;

namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// A read-only seekable stream that presents a multi-extent file as a single contiguous stream.
    /// Each extent maps a logical byte range to a disc image offset; reads spanning extent boundaries
    /// are handled seamlessly by transitioning between extents.
    /// Uses IImageReader.OpenStream() to read block data for each extent.
    /// </summary>
    internal class MultiExtentStream : Stream
    {
        private readonly IImageReader _reader;
        private readonly ExtentInfo[] _extents;
        private readonly long _totalSize;
        private long _position;
        private bool _disposed;

        // Cached streams per extent — opened on first access to each extent
        private readonly Stream[] _extentStreams;

        /// <summary>
        /// Describes a single extent: its logical offset in the file, its image offset, and its size.
        /// </summary>
        private readonly struct ExtentInfo
        {
            public readonly long OffsetInFile;   // Logical byte offset from start of file
            public readonly long ImageOffset;    // Disc image offset where this extent's data is stored
            public readonly long Size;           // Size of this extent in bytes

            public ExtentInfo(long offsetInFile, long imageOffset, long size)
            {
                OffsetInFile = offsetInFile;
                ImageOffset = imageOffset;
                Size = size;
            }
        }

        /// <summary>
        /// Creates a MultiExtentStream that reads from multiple non-contiguous disc regions
        /// via the given IImageReader.
        /// </summary>
        /// <param name="reader">The image reader to read block data from (not owned/disposed by this stream).</param>
        /// <param name="parts">The ordered list of file parts (extents) making up this file.</param>
        public MultiExtentStream(IImageReader reader, IReadOnlyList<IFsFilePart> parts)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("Parts list must contain at least one extent.", nameof(parts));

            _extents = new ExtentInfo[parts.Count];
            _extentStreams = new Stream[parts.Count];
            long totalSize = 0;

            for (int i = 0; i < parts.Count; i++)
            {
                IFsFilePart part = parts[i];
                long partSize = part.FsFile.FsSize;
                long imageOffset = part.FsFile.FsOffset;

                _extents[i] = new ExtentInfo(totalSize, imageOffset, partSize);
                totalSize += partSize;
            }

            _totalSize = totalSize;
            _position = 0;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => _totalSize;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _totalSize)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MultiExtentStream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            if (_position >= _totalSize)
                return 0;

            int totalRead = 0;
            int remaining = (int)Math.Min(count, _totalSize - _position);

            while (remaining > 0)
            {
                // Find which extent contains the current logical position
                int extentIndex = findExtentIndex(_position);
                if (extentIndex < 0)
                    break;

                ExtentInfo extent = _extents[extentIndex];
                long positionInExtent = _position - extent.OffsetInFile;
                long availableInExtent = extent.Size - positionInExtent;
                int toRead = (int)Math.Min(remaining, availableInExtent);

                // Get or open the stream for this extent
                Stream extentStream = getOrOpenExtentStream(extentIndex);

                // Seek within the extent stream to the correct position
                extentStream.Position = positionInExtent;
                int bytesRead = readFully(extentStream, buffer, offset + totalRead, toRead);

                totalRead += bytesRead;
                remaining -= bytesRead;
                _position += bytesRead;

                if (bytesRead < toRead)
                    break; // Unexpected short read
            }

            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MultiExtentStream));

            long newPos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _totalSize + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPos < 0 || newPos > _totalSize)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = newPos;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// Gets or lazily opens the stream for the given extent index.
        /// </summary>
        private Stream getOrOpenExtentStream(int index)
        {
            if (_extentStreams[index] == null)
                _extentStreams[index] = _reader.OpenStream(_extents[index].ImageOffset);
            return _extentStreams[index];
        }

        /// <summary>
        /// Finds the extent index containing the given logical file position using binary search.
        /// </summary>
        private int findExtentIndex(long logicalPosition)
        {
            int lo = 0, hi = _extents.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                ExtentInfo ext = _extents[mid];
                if (logicalPosition < ext.OffsetInFile)
                    hi = mid - 1;
                else if (logicalPosition >= ext.OffsetInFile + ext.Size)
                    lo = mid + 1;
                else
                    return mid;
            }
            return -1; // Position beyond all extents
        }

        /// <summary>
        /// Reads exactly the requested number of bytes from the stream, handling partial reads.
        /// </summary>
        private static int readFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }
            return totalRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    // Dispose cached extent streams
                    for (int i = 0; i < _extentStreams.Length; i++)
                    {
                        try { _extentStreams[i]?.Dispose(); } catch { }
                        _extentStreams[i] = null;
                    }
                }
                // Do not dispose _reader — it is shared/pooled and managed externally
            }
            base.Dispose(disposing);
        }
    }
}