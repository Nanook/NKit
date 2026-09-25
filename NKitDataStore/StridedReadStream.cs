using System.Diagnostics;
using System.Text;

namespace NKitDataStore
{
    /// <summary>
    /// Reconstructs a strided stream from an underlying clean-data grouped stream.
    /// IMPORTANT: This implementation intentionally does NOT add an initial leading padding
    /// before the first data block. That means position 0 maps to the first byte of the
    /// first clean-data block. Subsequent blocks will have their padding (DataOffset)
    /// inserted before each data block.
    /// </summary>
    internal class StridedReadStream : Stream
    {
        // Read diagnostics. Off by default; flip to true to log strided-read positions/bytes
        // (code-controlled, no environment variable). Accessed via diagEnabled() so the const does
        // not make the `if` body compile-time-unreachable (CS0162) — mirrors DataStore.shouldProfile().
        private const bool _diag = false;
        private static bool diagEnabled() => _diag;

        private readonly Stream _cleanStream;
        private readonly DataStride _stride;
        private readonly bool _ownsStream;

        private readonly long _cleanLength;
        private readonly long _cleanBlockSize; // DataLength
        private readonly long _padSize; // DataOffset
        private readonly long _firstDataLen;
        private readonly long _remainingCleanAfterFirst;
        private readonly long _fullBlocksAfterFirst; // count of full clean blocks after first

        private long _position;

        public StridedReadStream(Stream cleanStream, DataStride stride, bool ownsStream)
        {
            _cleanStream = cleanStream ?? throw new ArgumentNullException(nameof(cleanStream));
            _stride = stride ?? throw new ArgumentNullException(nameof(stride));
            _ownsStream = ownsStream;

            // Expect clean stream length to be available (seekable). If not, attempt to read length.
            if (!_cleanStream.CanSeek)
                throw new NotSupportedException("StridedReadStream requires an underlying seekable clean stream.");

            _cleanLength = _cleanStream.Length;
            _cleanBlockSize = _stride.DataLength;
            _padSize = _stride.DataOffset;

            _firstDataLen = Math.Min(_cleanBlockSize, _cleanLength);
            _remainingCleanAfterFirst = Math.Max(0, _cleanLength - _firstDataLen);
            _fullBlocksAfterFirst = _remainingCleanAfterFirst / _cleanBlockSize;

            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                if (_cleanLength == 0)
                    return 0;
                long len = _firstDataLen;
                // each subsequent full block contributes pad + data
                len += _fullBlocksAfterFirst * (_padSize + _cleanBlockSize);
                long lastPartial = _remainingCleanAfterFirst % _cleanBlockSize;
                if (lastPartial > 0)
                    len += _padSize + lastPartial;
                return len;
            }
        }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();
            if (count == 0)
                return 0;

            long available = Length - _position;
            if (available <= 0)
                return 0;
            int toRead = (int)Math.Min(count, available);

            int written = 0;
            while (written < toRead)
            {
                // Determine where _position lies: first block, padding, or a data region
                if (_position < _firstDataLen)
                {
                    // In first data block (no leading padding)
                    long availInFirst = _firstDataLen - _position;
                    int take = (int)Math.Min(toRead - written, availInFirst);
                    // read from clean stream at same offset
                    _cleanStream.Position = _position; // clean pos == strided pos for first block
                    int r = _cleanStream.Read(buffer, offset + written, take);
                    written += r;
                    _position += r;
                    if (r < take)
                        break; // EOF on clean stream
                    continue;
                }

                // position is after first block
                long posAfterFirst = _position - _firstDataLen;
                long blockUnit = _padSize + _cleanBlockSize; // the repeated unit for b>=1
                long unitIndex = posAfterFirst / blockUnit; // 0-based (for block b = 1 + unitIndex)
                long unitOffset = posAfterFirst % blockUnit;

                if (unitOffset < _padSize)
                {
                    // we're in padding - fill zeros up to end of pad or request
                    long padAvail = _padSize - unitOffset;
                    int take = (int)Math.Min(toRead - written, padAvail);
                    Array.Clear(buffer, offset + written, take);
                    written += take;
                    _position += take;
                    continue;
                }

                // we're in data region of block (b = 1 + unitIndex)
                long offInData = unitOffset - _padSize; // offset within this clean data block
                long blockNumber = 1 + unitIndex; // clean block index
                long cleanPos = (blockNumber * _cleanBlockSize) + offInData;
                long cleanAvail = Math.Min(_cleanBlockSize - offInData, _cleanLength - cleanPos);
                if (cleanAvail <= 0)
                    break; // no more clean data
                int takeData = (int)Math.Min(toRead - written, cleanAvail);
                _cleanStream.Position = cleanPos;
                int rr = _cleanStream.Read(buffer, offset + written, takeData);
                written += rr;
                _position += rr;
                if (rr < takeData)
                    break; // EOF
            }

            // Ensure position invariant
            if (_position > Length)
                _position = Length;

            // Diagnostics: log brief info when enabled (prints start and end positions).
            // Off by default; flip _diag to true to enable (no environment variable).
            try
            {
                if (diagEnabled())
                {
                    int dump = Math.Min(16, written);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < dump; i++)
                        sb.AppendFormat("{0:X2}", buffer[offset + i]);
                    long startPos = _position - written;
                    long endPos = _position;
                    Debug.WriteLine($"[StridedReadStream] Start={startPos:X} End={endPos:X} Req={toRead} Ret={written} First{dump}={sb}");
                }
            }
            catch { }

            return written;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin: target = offset; break;
                case SeekOrigin.Current: target = _position + offset; break;
                case SeekOrigin.End: target = Length + offset; break;
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
            if (target < 0)
                throw new IOException("Attempt to seek before beginning of stream");
            _position = Math.Min(target, Length);

            // Also position underlying clean stream where sensible so callers that expect
            // the stream to be positioned (or to be able to read immediately) observe
            // the correct location. Map the strided/image-space position to the
            // corresponding clean-stream position and set it when possible.
            try
            {
                if (_cleanStream.CanSeek)
                {
                    long cleanPos = mapStridedToCleanPosition(_position);
                    // Clamp
                    if (cleanPos < 0)
                        cleanPos = 0;
                    if (cleanPos > _cleanLength)
                        cleanPos = _cleanLength;
                    _cleanStream.Position = cleanPos;
                }
            }
            catch { }

            return _position;
        }

        // Map a strided (image-space) position into an approximate position in the underlying
        // clean grouped stream. If the strided position points into padding, return the start
        // of the next clean block so a subsequent read will operate correctly.
        private long mapStridedToCleanPosition(long stridedPos)
        {
            if (stridedPos < 0) return 0;
            if (stridedPos < _firstDataLen) // first block maps directly
                return stridedPos;

            long posAfterFirst = stridedPos - _firstDataLen;
            long blockUnit = _padSize + _cleanBlockSize;
            long unitIndex = posAfterFirst / blockUnit; // 0-based for blocks after first
            long unitOffset = posAfterFirst % blockUnit;

            long blockNumber = 1 + unitIndex;
            if (unitOffset < _padSize) // in padding -> position to start of this block's clean data
                return blockNumber * _cleanBlockSize;
            else
            {
                long offInData = unitOffset - _padSize;
                return (blockNumber * _cleanBlockSize) + offInData;
            }
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownsStream)
                try { _cleanStream.Dispose(); } catch { }
            base.Dispose(disposing);
        }
    }
}