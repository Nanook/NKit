using NKitStream;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NKitStream.Tests
{
    /// <summary>
    /// Concrete subclass backed by a <see cref="byte"/> array so tests control exactly
    /// what the source returns.
    /// </summary>
    internal sealed class MemorySourceStream : BlockBufferedStream
    {
        private readonly byte[] _source;
        private int _sourcePos;

        public MemorySourceStream(byte[] source, int blockSize = 64)
            : base(blockSize)
        {
            _source = source;
        }

        public int SourceReadCount { get; private set; }

        protected override ValueTask<int> ReadSourceAsync(Memory<byte> buffer, CancellationToken ct)
        {
            SourceReadCount++;
            int available = _source.Length - _sourcePos;
            if (available <= 0)
                return new ValueTask<int>(0);

            int toCopy = Math.Min(available, buffer.Length);
            _source.AsSpan(_sourcePos, toCopy).CopyTo(buffer.Span);
            _sourcePos += toCopy;
            return new ValueTask<int>(toCopy);
        }

        protected override int ReadSource(Span<byte> buffer)
        {
            SourceReadCount++;
            int available = _source.Length - _sourcePos;
            if (available <= 0)
                return 0;

            int toCopy = Math.Min(available, buffer.Length);
            _source.AsSpan(_sourcePos, toCopy).CopyTo(buffer);
            _sourcePos += toCopy;
            return toCopy;
        }
    }

    public class BlockBufferedStreamTests
    {
        private static byte[] MakeSequential(int length)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
                data[i] = (byte)(i & 0xFF);
            return data;
        }

        // ── Basic sequential reads ─────────────────────────────────────

        [Fact]
        public async Task Read_FullSource_ReturnsAllBytes()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            var buf = new byte[200];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(200, total);
            Assert.Equal(source, buf);
            Assert.Equal(200L, stream.Position);
        }

        [Fact]
        public async Task Read_BeyondSource_ReturnsZero()
        {
            var source = new byte[] { 1, 2, 3 };
            using var stream = new MemorySourceStream(source, blockSize: 16);

            var buf = new byte[10];
            int n = await stream.ReadAsync(buf.AsMemory(), CancellationToken.None);
            Assert.Equal(3, n);

            n = await stream.ReadAsync(buf.AsMemory(), CancellationToken.None);
            Assert.Equal(0, n);
            Assert.True(stream.IsSourceExhausted);
        }

        [Fact]
        public async Task Read_EmptyBuffer_ReturnsZero()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            int n = await stream.ReadAsync(Memory<byte>.Empty, CancellationToken.None);
            Assert.Equal(0, n);
            Assert.Equal(0L, stream.Position);
        }

        // ── Spanning reads across blocks ───────────────────────────────

        [Fact]
        public async Task Read_SpanningMultipleBlocks_ReturnsCorrectData()
        {
            var source = MakeSequential(256);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            // Read 100 bytes which spans blocks 0..3 (32*3 = 96, so 4 blocks)
            var buf = new byte[100];
            int total = 0;
            while (total < 100)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(100, total);
            Assert.Equal(source.AsSpan(0, 100).ToArray(), buf);
        }

        // ── Peek ───────────────────────────────────────────────────────

        [Fact]
        public async Task Peek_DoesNotAdvancePosition()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            var buf = new byte[10];
            int n = await stream.PeekAsync(buf, CancellationToken.None);
            Assert.Equal(10, n);
            Assert.Equal(0L, stream.Position);

            // Peek again — same data
            var buf2 = new byte[10];
            n = await stream.PeekAsync(buf2, CancellationToken.None);
            Assert.Equal(10, n);
            Assert.Equal(buf, buf2);
        }

        [Fact]
        public async Task Peek_Then_Read_ReturnsSameData()
        {
            var source = MakeSequential(50);
            using var stream = new MemorySourceStream(source, blockSize: 16);
            stream.BeginBuffering();

            var peekBuf = new byte[20];
            await stream.PeekAsync(peekBuf, CancellationToken.None);

            var readBuf = new byte[20];
            await stream.ReadAsync(readBuf, CancellationToken.None);

            Assert.Equal(peekBuf, readBuf);
            Assert.Equal(20L, stream.Position);
        }

        // ── Mark / Rewind ──────────────────────────────────────────────

        [Fact]
        public async Task Mark_Rewind_RestoresPosition()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            var buf1 = new byte[20];
            await stream.ReadAsync(buf1, CancellationToken.None);
            Assert.Equal(20L, stream.Position);

            long mark = stream.Mark();
            Assert.Equal(20L, mark);

            // Read more
            var buf2 = new byte[30];
            await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(50L, stream.Position);

            // Rewind
            stream.Rewind(mark);
            Assert.Equal(20L, stream.Position);

            // Re-read should give same data as buf2
            var buf3 = new byte[30];
            await stream.ReadAsync(buf3, CancellationToken.None);
            Assert.Equal(buf2, buf3);
        }

        [Fact]
        public async Task Mark_Rewind_ThenContinuePastCacheSeamlessly()
        {
            // Source is 200 bytes, block size 32.
            // Read 50, mark, read 50 more (to 100), rewind to 50, then read 150
            // — the first 50 come from cache, the last 100 from source seamlessly.
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            // Read to position 50
            var discard = new byte[50];
            await stream.ReadAsync(discard, CancellationToken.None);

            long mark = stream.Mark(); // 50

            // Read ahead to 100 (cached up to 100 after this)
            var ahead = new byte[50];
            await stream.ReadAsync(ahead, CancellationToken.None);
            Assert.Equal(100L, stream.Position);

            // Rewind to 50
            stream.Rewind(mark);
            Assert.Equal(50L, stream.Position);

            // Now read 150 bytes: 50 from cache [50..100), 100 from source [100..200)
            var full = new byte[150];
            int totalRead = 0;
            while (totalRead < 150)
            {
                int n = await stream.ReadAsync(full.AsMemory(totalRead), CancellationToken.None);
                if (n == 0) break;
                totalRead += n;
            }

            Assert.Equal(150, totalRead);
            Assert.Equal(source.AsSpan(50, 150).ToArray(), full);
            Assert.Equal(200L, stream.Position);
        }

        // ── Seek ───────────────────────────────────────────────────────

        [Fact]
        public async Task Seek_Forward_Then_Read()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            stream.Seek(100);
            Assert.Equal(100L, stream.Position);

            var buf = new byte[20];
            int n = await stream.ReadAsync(buf, CancellationToken.None);
            Assert.Equal(20, n);
            Assert.Equal(source.AsSpan(100, 20).ToArray(), buf);
        }

        [Fact]
        public async Task Seek_BackWithinCache()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            // Read 60 bytes
            var buf = new byte[60];
            await stream.ReadAsync(buf, CancellationToken.None);

            // Seek back to 10
            stream.Seek(10);
            Assert.Equal(10L, stream.Position);

            var buf2 = new byte[20];
            await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(source.AsSpan(10, 20).ToArray(), buf2);
        }

        [Fact]
        public void Seek_BelowFloor_Throws()
        {
            using var stream = new MemorySourceStream(new byte[100], blockSize: 32);
            stream.Release(50);

            Assert.Throws<InvalidOperationException>(() => stream.Seek(49));
        }

        // ── Release / Floor ────────────────────────────────────────────

        [Fact]
        public async Task Release_FreesBlocksBelowFloor()
        {
            var source = MakeSequential(256);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            // Read 128 bytes — blocks 0..3 (4 blocks)
            var buf = new byte[128];
            await stream.ReadAsync(buf, CancellationToken.None);

            // Release up to 96 — blocks 0,1,2 (offsets 0..95) are entirely below 96
            stream.Release(96);
            Assert.Equal(96L, stream.Floor);

            // Can still read forward
            var buf2 = new byte[32];
            int n = await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(32, n);
            Assert.Equal(source.AsSpan(128, 32).ToArray(), buf2);
        }

        [Fact]
        public async Task Release_SameOffsetTwice_IsNoOp()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            await stream.ReadAsync(new byte[50], CancellationToken.None);
            stream.Release(32);
            stream.Release(32); // should not throw
            Assert.Equal(32L, stream.Floor);
        }

        [Fact]
        public async Task Release_Then_Seek_Back_Throws()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            await stream.ReadAsync(new byte[50], CancellationToken.None);
            stream.Release(40);

            Assert.Throws<InvalidOperationException>(() => stream.Seek(30));
        }

        [Fact]
        public async Task Release_Then_Rewind_BelowFloor_Throws()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            await stream.ReadAsync(new byte[50], CancellationToken.None);
            long mark = stream.Mark(); // 50

            await stream.ReadAsync(new byte[20], CancellationToken.None);
            stream.Release(60); // floor is now 60, mark was 50

            Assert.Throws<InvalidOperationException>(() => stream.Rewind(mark));
        }

        // ── Seek ahead, read, rewind ──────────────────────────────────

        [Fact]
        public async Task Seek_Read_Rewind_FullPattern()
        {
            var source = MakeSequential(300);
            using var stream = new MemorySourceStream(source, blockSize: 64);
            stream.BeginBuffering();

            // Read first 50 bytes
            var header = new byte[50];
            await stream.ReadAsync(header, CancellationToken.None);
            Assert.Equal(50L, stream.Position);

            // Mark, seek ahead, read, rewind
            long mark = stream.Mark();
            stream.Seek(200);
            var peek = new byte[20];
            await stream.ReadAsync(peek, CancellationToken.None);
            Assert.Equal(source.AsSpan(200, 20).ToArray(), peek);

            stream.Rewind(mark);
            Assert.Equal(50L, stream.Position);

            // Continue sequential read — first chunk is from cache, rest from source
            var rest = new byte[250];
            int totalRead = 0;
            while (totalRead < 250)
            {
                int n = await stream.ReadAsync(rest.AsMemory(totalRead), CancellationToken.None);
                if (n == 0) break;
                totalRead += n;
            }

            Assert.Equal(250, totalRead);
            Assert.Equal(source.AsSpan(50, 250).ToArray(), rest);
        }

        // ── Dispose ────────────────────────────────────────────────────

        [Fact]
        public async Task Dispose_ThenRead_Throws()
        {
            var stream = new MemorySourceStream(new byte[10], blockSize: 16);
            stream.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask());
        }

        [Fact]
        public void Dispose_Twice_IsNoOp()
        {
            var stream = new MemorySourceStream(new byte[10], blockSize: 16);
            stream.Dispose();
            stream.Dispose(); // should not throw
        }

        // ── Constructor validation ─────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Constructor_InvalidBlockSize_Throws(int blockSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySourceStream(new byte[10], blockSize));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(100)]
        [InlineData(4096)]
        public void Constructor_ValidBlockSize_DoesNotThrow(int blockSize)
        {
            using var stream = new MemorySourceStream(new byte[10], blockSize);
            Assert.NotNull(stream);
        }

        // ── Edge: block-boundary aligned reads ─────────────────────────

        [Fact]
        public async Task Read_ExactlyOneBlock()
        {
            var source = MakeSequential(64);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            var buf = new byte[64];
            int n = await stream.ReadAsync(buf, CancellationToken.None);
            Assert.Equal(64, n);
            Assert.Equal(source, buf);
        }

        [Fact]
        public async Task Read_ExactlyTwoBlocks_Sequentially()
        {
            var source = MakeSequential(128);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            var buf1 = new byte[64];
            await stream.ReadAsync(buf1, CancellationToken.None);
            var buf2 = new byte[64];
            await stream.ReadAsync(buf2, CancellationToken.None);

            Assert.Equal(source.AsSpan(0, 64).ToArray(), buf1);
            Assert.Equal(source.AsSpan(64, 64).ToArray(), buf2);
        }

        // ── Source read count (cache effectiveness) ────────────────────

        [Fact]
        public async Task CachedRewind_DoesNotHitSourceAgain()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            var buf = new byte[50];
            await stream.ReadAsync(buf, CancellationToken.None);
            int readsAfterFirstPass = stream.SourceReadCount;

            // Rewind and re-read — should come from cache, no new source reads
            stream.Seek(0);
            var buf2 = new byte[50];
            await stream.ReadAsync(buf2, CancellationToken.None);

            Assert.Equal(readsAfterFirstPass, stream.SourceReadCount);
            Assert.Equal(buf, buf2);
        }

        // ── Pass-through mode ──────────────────────────────────────────

        [Fact]
        public async Task PassThrough_Read_GoesDirectToSource()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            Assert.False(stream.IsBuffering);

            var buf = new byte[200];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(200, total);
            Assert.Equal(source, buf);
            Assert.Equal(200L, stream.Position);
        }

        [Fact]
        public async Task PassThrough_Peek_WorksViaTempCache()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            Assert.False(stream.IsBuffering);

            // Peek without buffering — temporarily caches data
            var peekBuf = new byte[10];
            int n = await stream.PeekAsync(peekBuf, CancellationToken.None);
            Assert.Equal(10, n);
            Assert.Equal(0L, stream.Position); // position unchanged
            Assert.Equal(source.AsSpan(0, 10).ToArray(), peekBuf);

            // Next read returns the same data (from temp cache) and auto-releases
            var readBuf = new byte[10];
            n = await stream.ReadAsync(readBuf, CancellationToken.None);
            Assert.Equal(10, n);
            Assert.Equal(peekBuf, readBuf);
            Assert.Equal(10L, stream.Position);
        }

        [Fact]
        public async Task PassThrough_SeekBackward_ThrowsBecauseFloor()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            await stream.ReadAsync(new byte[50], CancellationToken.None);

            // In pass-through Floor tracks Position, so backward seek hits the floor check
            Assert.Throws<InvalidOperationException>(() => stream.Seek(10));
            Assert.True(stream.Floor >= 50, $"Floor should track Position in pass-through, was {stream.Floor}");
        }

        [Fact]
        public async Task PassThrough_SeekForward_ThenRead()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            // Forward seek in pass-through is allowed
            stream.Seek(100);
            Assert.Equal(100L, stream.Position);

            var buf = new byte[20];
            int n = await stream.ReadAsync(buf, CancellationToken.None);
            Assert.Equal(20, n);
            Assert.Equal(source.AsSpan(100, 20).ToArray(), buf);
        }

        // ── BeginBuffering / EndBuffering lifecycle ────────────────────

        [Fact]
        public async Task BeginEnd_Buffering_Lifecycle()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            // Phase 1: pass-through read
            var buf1 = new byte[40];
            await stream.ReadAsync(buf1, CancellationToken.None);
            Assert.Equal(40L, stream.Position);
            Assert.False(stream.IsBuffering);

            // Phase 2: begin buffering, read, peek, rewind
            stream.BeginBuffering();
            Assert.True(stream.IsBuffering);

            long mark = stream.Mark(); // 40
            var buf2 = new byte[60];
            await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(100L, stream.Position);

            var peekBuf = new byte[10];
            await stream.PeekAsync(peekBuf, CancellationToken.None);
            Assert.Equal(source.AsSpan(100, 10).ToArray(), peekBuf);

            stream.Rewind(mark);
            Assert.Equal(40L, stream.Position);

            // Phase 3: end buffering — remaining cached data is drained
            stream.EndBuffering();
            Assert.False(stream.IsBuffering);

            // Read through the cached region and beyond
            var buf3 = new byte[160];
            int total = 0;
            while (total < buf3.Length)
            {
                int n = await stream.ReadAsync(buf3.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(160, total);
            Assert.Equal(source.AsSpan(40, 160).ToArray(), buf3);
            Assert.Equal(200L, stream.Position);
        }

        [Fact]
        public void BeginBuffering_WhenAlreadyBuffering_IsNoOp()
        {
            using var stream = new MemorySourceStream(new byte[10], blockSize: 16);
            stream.BeginBuffering();
            stream.BeginBuffering(); // should not throw
            Assert.True(stream.IsBuffering);
        }

        [Fact]
        public void EndBuffering_WhenNotBuffering_IsNoOp()
        {
            using var stream = new MemorySourceStream(new byte[10], blockSize: 16);
            stream.EndBuffering(); // should not throw
            Assert.False(stream.IsBuffering);
        }

        [Fact]
        public async Task EndBuffering_ReleasesBlocksBehindPosition()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            stream.BeginBuffering();
            await stream.ReadAsync(new byte[100], CancellationToken.None);

            // Rewind partway
            stream.Seek(60);
            stream.EndBuffering();

            // Floor should have advanced to at least position (60)
            Assert.True(stream.Floor >= 60, $"Floor should be >= 60, was {stream.Floor}");

            // Cannot seek backward anymore
            Assert.Throws<InvalidOperationException>(() => stream.Seek(50));
        }

        [Fact]
        public async Task EndBuffering_DrainsCachedData_ThenPassThrough()
        {
            var source = MakeSequential(300);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            // Read 50 in pass-through
            await stream.ReadAsync(new byte[50], CancellationToken.None);

            // Buffer, read ahead, rewind, end buffering
            stream.BeginBuffering();
            await stream.ReadAsync(new byte[100], CancellationToken.None); // pos=150, buffered to 150
            stream.Seek(50);  // rewind to 50
            stream.EndBuffering();

            int sourceReadsBefore = stream.SourceReadCount;

            // Read the cached portion [50..150) — should NOT hit source
            var cached = new byte[100];
            int total = 0;
            while (total < 100)
            {
                int n = await stream.ReadAsync(cached.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(100, total);
            Assert.Equal(source.AsSpan(50, 100).ToArray(), cached);

            // Now we're at 150, cache is drained. Next read should hit source (pass-through).
            int sourceReadsAfterDrain = stream.SourceReadCount;

            var fresh = new byte[50];
            int n2 = await stream.ReadAsync(fresh, CancellationToken.None);
            Assert.Equal(50, n2);
            Assert.Equal(source.AsSpan(150, 50).ToArray(), fresh);
            Assert.True(stream.SourceReadCount > sourceReadsAfterDrain,
                "Expected source to be hit after cache was drained");
        }

        [Fact]
        public async Task MultipleBufferingCycles()
        {
            var source = MakeSequential(400);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            // Cycle 1: pass-through
            var buf1 = new byte[100];
            await stream.ReadAsync(buf1, CancellationToken.None);
            Assert.Equal(source.AsSpan(0, 100).ToArray(), buf1);

            // Cycle 2: buffer, peek, rewind, end
            stream.BeginBuffering();
            long mark1 = stream.Mark(); // 100
            await stream.ReadAsync(new byte[50], CancellationToken.None);
            stream.Rewind(mark1);
            stream.EndBuffering();

            // Drain cached data
            var buf2 = new byte[50];
            await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(source.AsSpan(100, 50).ToArray(), buf2);

            // Cycle 3: pass-through again
            var buf3 = new byte[100];
            int total = 0;
            while (total < 100)
            {
                int n = await stream.ReadAsync(buf3.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(100, total);
            Assert.Equal(source.AsSpan(150, 100).ToArray(), buf3);

            // Cycle 4: buffer again
            stream.BeginBuffering();
            long mark2 = stream.Mark(); // 250
            await stream.ReadAsync(new byte[50], CancellationToken.None);
            stream.Rewind(mark2);
            stream.EndBuffering();

            // Drain and read rest
            var buf4 = new byte[150];
            total = 0;
            while (total < 150)
            {
                int n = await stream.ReadAsync(buf4.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(150, total);
            Assert.Equal(source.AsSpan(250, 150).ToArray(), buf4);
        }

        // ── EndBuffering while rewound ─────────────────────────────────

        [Fact]
        public async Task EndBuffering_WhileRewound_DrainsCorrectly()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            stream.BeginBuffering();
            await stream.ReadAsync(new byte[100], CancellationToken.None); // pos=100, buffered=100
            stream.Seek(40); // rewind to 40

            // EndBuffering while position (40) is behind buffered (100)
            stream.EndBuffering();
            Assert.False(stream.IsBuffering);
            Assert.Equal(40L, stream.Position);

            // Reads should drain cached data [40..100), then pass-through from 100+
            var buf = new byte[160];
            int total = 0;
            while (total < 160)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(160, total);
            Assert.Equal(source.AsSpan(40, 160).ToArray(), buf);
        }

        // ── Seek backward with cached blocks (no active buffering) ─────

        [Fact]
        public async Task SeekBackward_WithCachedBlocks_WorksWithoutBuffering()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 32);

            // Buffer a region, then end buffering while rewound
            stream.BeginBuffering();
            await stream.ReadAsync(new byte[100], CancellationToken.None);
            stream.Seek(50);
            stream.EndBuffering(); // Floor=50, blocks [50..100) kept, position=50

            // Read forward to 80
            await stream.ReadAsync(new byte[30], CancellationToken.None);
            Assert.Equal(80L, stream.Position);

            // Seek backward to 64 — blocks still cover this range,
            // and Floor should still allow it (auto-release advances floor
            // to position on each read, but in block-aligned steps)
            long floor = stream.Floor;
            if (floor <= 64)
            {
                stream.Seek(64);
                Assert.Equal(64L, stream.Position);

                var buf = new byte[16];
                int n = await stream.ReadAsync(buf, CancellationToken.None);
                Assert.Equal(16, n);
                Assert.Equal(source.AsSpan(64, 16).ToArray(), buf);
            }
        }

        // ── Rewind with active buffering resumes caching ───────────────

        [Fact]
        public async Task Rewind_WhileBuffering_ContinuesCachingPastFrontier()
        {
            var source = MakeSequential(300);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            stream.BeginBuffering();

            // Read to 100
            await stream.ReadAsync(new byte[100], CancellationToken.None);
            long mark = stream.Mark(); // 100

            // Read to 200 (Buffered may overshoot to a block boundary since
            // ensureBufferedAsync fills in block-sized chunks)
            await stream.ReadAsync(new byte[100], CancellationToken.None);
            Assert.True(stream.Buffered >= 200,
                $"Expected Buffered >= 200, got {stream.Buffered}");

            // Rewind to 100
            stream.Rewind(mark);

            // Read 200 bytes — first 100 from cache, next 100 from source.
            // Since buffering is still active, new source data is also cached.
            var buf = new byte[200];
            int total = 0;
            while (total < 200)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(200, total);
            Assert.Equal(source.AsSpan(100, 200).ToArray(), buf);
            Assert.Equal(300L, stream.Position);

            // Buffered should have extended to 300 (new data was cached)
            Assert.Equal(300L, stream.Buffered);

            // Since buffering is still active, we can rewind all the way
            // back to the start and re-read from cache
            stream.Seek(0);
            int sourceReadsBefore = stream.SourceReadCount;
            var full = new byte[300];
            total = 0;
            while (total < 300)
            {
                int n = await stream.ReadAsync(full.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }
            Assert.Equal(300, total);
            Assert.Equal(source, full);
            Assert.Equal(sourceReadsBefore, stream.SourceReadCount); // all from cache
        }

        // ── Pass-through then buffer (gap-block regression) ────────────

        [Fact]
        public async Task PassThrough_ThenBuffer_DoesNotAllocateGapBlocks()
        {
            // Regression: after a long pass-through read, _firstBlockOffset could stay
            // at 0, causing getOrAllocateBlockAt to allocate thousands of empty gap blocks
            // when transitioning to buffered mode.
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 16);

            // Pass-through read moves position/buffered/floor to 100
            var discard = new byte[100];
            await stream.ReadAsync(discard, CancellationToken.None);
            Assert.Equal(100L, stream.Position);
            Assert.False(stream.IsBuffering);

            // Now switch to buffered mode and read 50 more
            stream.BeginBuffering();
            var buf = new byte[50];
            int n = await stream.ReadAsync(buf, CancellationToken.None);
            Assert.Equal(50, n);
            Assert.Equal(source.AsSpan(100, 50).ToArray(), buf);

            // Verify correctness: rewind and re-read
            stream.Seek(100);
            var buf2 = new byte[50];
            n = await stream.ReadAsync(buf2, CancellationToken.None);
            Assert.Equal(50, n);
            Assert.Equal(buf, buf2);
        }

        // ── Synchronous API ────────────────────────────────────────────

        [Fact]
        public void Sync_Read_FullSource_ReturnsAllBytes()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 64);

            var buf = new byte[200];
            int total = 0;
            while (total < buf.Length)
            {
                int n = stream.Read(buf.AsSpan(total));
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(200, total);
            Assert.Equal(source, buf);
            Assert.Equal(200L, stream.Position);
        }

        [Fact]
        public void Sync_Peek_DoesNotAdvancePosition()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            var buf = new byte[10];
            int n = stream.Peek(buf);
            Assert.Equal(10, n);
            Assert.Equal(0L, stream.Position);

            var buf2 = new byte[10];
            n = stream.Peek(buf2);
            Assert.Equal(buf, buf2);
        }

        [Fact]
        public void Sync_Read_Buffered_MarkRewind()
        {
            var source = MakeSequential(100);
            using var stream = new MemorySourceStream(source, blockSize: 32);
            stream.BeginBuffering();

            var buf1 = new byte[30];
            stream.Read(buf1);
            long mark = stream.Mark();

            var buf2 = new byte[20];
            stream.Read(buf2);
            stream.Rewind(mark);

            var buf3 = new byte[20];
            stream.Read(buf3);
            Assert.Equal(buf2, buf3);
        }

        [Fact]
        public void Sync_Read_BeyondSource_ReturnsZero()
        {
            var source = new byte[] { 1, 2, 3 };
            using var stream = new MemorySourceStream(source, blockSize: 16);

            var buf = new byte[10];
            int n = stream.Read(buf);
            Assert.Equal(3, n);

            n = stream.Read(buf);
            Assert.Equal(0, n);
            Assert.True(stream.IsSourceExhausted);
        }

        [Fact]
        public void Sync_ReadSource_NotOverridden_Throws()
        {
            // Verify the default ReadSource throws NotSupportedException
            // when a subclass doesn't override it.
            using var stream = new AsyncOnlySourceStream(new byte[10], blockSize: 16);
            Assert.Throws<NotSupportedException>(() => stream.Read(new byte[5]));
        }

        // ── Non-power-of-2 block sizes ─────────────────────────────────

        [Fact]
        public async Task Read_NonPowerOf2BlockSize_WorksCorrectly()
        {
            var source = MakeSequential(200);
            using var stream = new MemorySourceStream(source, blockSize: 50);
            stream.BeginBuffering();

            var buf = new byte[200];
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total), CancellationToken.None);
                if (n == 0) break;
                total += n;
            }

            Assert.Equal(200, total);
            Assert.Equal(source, buf);
        }

        [Fact]
        public async Task MarkRewind_NonPowerOf2BlockSize_WorksCorrectly()
        {
            var source = MakeSequential(150);
            using var stream = new MemorySourceStream(source, blockSize: 37);
            stream.BeginBuffering();

            var buf1 = new byte[50];
            await stream.ReadAsync(buf1, CancellationToken.None);
            long mark = stream.Mark();

            var buf2 = new byte[40];
            await stream.ReadAsync(buf2, CancellationToken.None);
            stream.Rewind(mark);

            var buf3 = new byte[40];
            await stream.ReadAsync(buf3, CancellationToken.None);
            Assert.Equal(buf2, buf3);
        }
    }

    /// <summary>
    /// Subclass that only supports async reads — does NOT override ReadSource.
    /// Used to verify the default NotSupportedException.
    /// </summary>
    internal sealed class AsyncOnlySourceStream : BlockBufferedStream
    {
        private readonly byte[] _source;
        private int _sourcePos;

        public AsyncOnlySourceStream(byte[] source, int blockSize = 64)
            : base(blockSize)
        {
            _source = source;
        }

        protected override ValueTask<int> ReadSourceAsync(Memory<byte> buffer, CancellationToken ct)
        {
            int available = _source.Length - _sourcePos;
            if (available <= 0)
                return new ValueTask<int>(0);

            int toCopy = Math.Min(available, buffer.Length);
            _source.AsSpan(_sourcePos, toCopy).CopyTo(buffer.Span);
            _sourcePos += toCopy;
            return new ValueTask<int>(toCopy);
        }
    }
}