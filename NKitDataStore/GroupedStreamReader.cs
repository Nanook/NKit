namespace NKitDataStore
{
    /// <summary>
    /// Simplified grouped reader. Opens the grouped stream for the given offsetStart and reads
    /// the requested bytes directly. This implementation intentionally avoids any caching or
    /// sharing of stream state to reduce complexity and risk of cross-area caching bugs.
    /// </summary>
    public class GroupedStreamReader : IDisposable
    {
        private readonly Func<long, Stream> _openGroupedStream;

        public GroupedStreamReader(Func<long, Stream> openGroupedStream, int blockSize = 0x10000, int maxBlocks = 0x400)
        {
            _openGroupedStream = openGroupedStream ?? throw new ArgumentNullException(nameof(openGroupedStream));
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> bytes from the logical grouped stream for <paramref name="offsetStart"/>
        /// starting at <paramref name="groupedPosition"/> into <paramref name="buffer"/> at <paramref name="offset"/>.
        /// This method opens a fresh stream via the factory and reads the requested range directly.
        /// </summary>
        public int ReadFromGroup(long offsetStart, long groupedPosition, byte[] buffer, int offset, int count) => ReadFromGroup(offsetStart, groupedPosition, -1, -1, buffer, offset, count);

        /// <summary>
        /// Area-aware overload. When areaGroupedEnd >= 0 the read will be limited so it does not return
        /// bytes past the supplied grouped-area end. No caching is performed; areaId is accepted for
        /// compatibility but not used for caching in this simplified implementation.
        /// </summary>
        public int ReadFromGroup(long offsetStart, long groupedPosition, long areaId, long areaGroupedEnd, byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();
            if (count == 0)
                return 0;
            if (groupedPosition < 0)
                throw new ArgumentOutOfRangeException(nameof(groupedPosition));

            // Trim to areaGroupedEnd when provided
            if (areaGroupedEnd >= 0)
            {
                if (groupedPosition >= areaGroupedEnd)
                    return 0;
                long maxAvailable = areaGroupedEnd - groupedPosition;
                if (maxAvailable <= 0)
                    return 0;
                if (count > (int)Math.Min(int.MaxValue, maxAvailable))
                    count = (int)maxAvailable;
            }

            using (Stream stream = _openGroupedStream(offsetStart))
            {
                if (stream == null)
                    return 0;

                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Seek(groupedPosition, SeekOrigin.Begin);
                        int total = 0;
                        while (total < count)
                        {
                            int read = stream.Read(buffer, offset + total, count - total);
                            if (read <= 0)
                                break;
                            total += read;
                        }
                        return
                            total;
                    }
                    else
                    {
                        // Non-seekable: read and discard until groupedPosition, then read into buffer
                        const int SKIP_BUF = 64 * 1024;
                        byte[] skip = new byte[SKIP_BUF];
                        long toSkip = groupedPosition;
                        while (toSkip > 0)
                        {
                            int tr = (int)Math.Min(SKIP_BUF, toSkip);
                            int rr = stream.Read(skip, 0, tr);
                            if (rr <= 0)
                                return 0; // EOF while skipping
                            toSkip -= rr;
                        }

                        int total = 0;
                        while (total < count)
                        {
                            int read = stream.Read(buffer, offset + total, count - total);
                            if (read <= 0)
                                break;
                            total += read;
                        }
                        return total;
                    }
                }
                catch
                {
                    // On any error, return what we have (or 0) — keep behavior simple and fail-safe.
                    return 0;
                }
            }
        }

        public void Dispose()
        {
            // No long-lived resources are held in this simplified implementation.
        }
    }
}