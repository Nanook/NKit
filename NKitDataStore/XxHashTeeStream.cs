using System.Security.Cryptography;

namespace NKitDataStore;

/// <summary>
/// A read-only Stream wrapper that feeds all bytes read through it into a HashAlgorithm instance
/// (typically XXHash64). Used during directory ingestion to compute an aggregate hash
/// without re-reading data from the DataStore.
/// </summary>
internal sealed class XxHashTeeStream : Stream
{
    private readonly Stream _inner;
    private readonly HashAlgorithm _hasher;

    public XxHashTeeStream(Stream inner, HashAlgorithm hasher)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _inner.Read(buffer, offset, count);
        if (bytesRead > 0)
            _hasher.TransformBlock(buffer, offset, bytesRead, null, 0);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        if (bytesRead > 0)
            _hasher.TransformBlock(buffer, offset, bytesRead, null, 0);
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}