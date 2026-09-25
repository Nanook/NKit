using Nanook.NKit;
using System;
using System.IO;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Contract tests for <see cref="BufferStream"/> (the PeekStream replacement built on
    /// NKitStream's BlockBufferedStream). Exercises both a seekable base (fast path) and a
    /// forward-only base (buffered path) — the latter is the case PeekStream could not seek
    /// backward over reliably and which caused the Wii-fix-from-archive regression.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class BufferStreamTests
    {
        private const int Size = 0x100000;

        private static byte[] numberData()
        {
            byte[] d = new byte[Size];
            for (int i = 0; i < d.Length; i += 4)
                d.WriteUInt32B(i, (uint)i);
            return d;
        }

        // A base stream that reports CanSeek=false and refuses backward seeks — models a
        // forward-only archive entry (SourceStream over a zip).
        private sealed class ForwardOnlyStream : Stream
        {
            private readonly byte[] _data;
            private long _pos;
            public ForwardOnlyStream(byte[] data)
            {
                _data = data;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, _data.Length - _pos);
                if (n <= 0) return 0;
                Array.Copy(_data, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static void testBytes(long pos, byte[] data, int offset, int size)
        {
            for (int i = 0; i < size; i += 4)
                Assert.Equal((uint)(pos + i), data.ReadUInt32B(offset + i));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PeekDoesNotAdvancePosition(bool seekable)
        {
            using BufferStream c = make(seekable);
            byte[] data = new byte[0x1000];

            Assert.Equal(0x500, c.Read(data, 0, -0x500)); // peek 0x500, no advance
            Assert.Equal(0, c.Position);
            testBytes(0, data, 0, 0x500);

            Assert.Equal(0x500, c.Read(data, 0, -0x500)); // peek again, same bytes
            Assert.Equal(0, c.Position);
            testBytes(0, data, 0, 0x500);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PeekThenReadServesSameBytes(bool seekable)
        {
            using BufferStream c = make(seekable);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, -0x500);           // peek
            Assert.Equal(0, c.Position);
            c.Read(data, 0, 0x500);            // now consume the same bytes
            Assert.Equal(0x500, c.Position);
            testBytes(0, data, 0, 0x500);

            c.Read(data, 0, 0x500);            // continue forward
            Assert.Equal(0xA00, c.Position);
            testBytes(0x500, data, 0, 0x500);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForwardReadAdvances(bool seekable)
        {
            using BufferStream c = make(seekable);
            byte[] data = new byte[0x2000];

            Assert.Equal(0x1000, c.Read(data, 0, 0x1000));
            Assert.Equal(0x1000, c.Position);
            testBytes(0, data, 0, 0x1000);

            Assert.Equal(0x1000, c.Read(data, 0x1000, 0x1000));
            Assert.Equal(0x2000, c.Position);
            testBytes(0, data, 0, 0x2000);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BackwardSeekIntoHeadRegionWorks(bool seekable)
        {
            // This is the crux: after reading forward, seek back into the earlier region and
            // re-read. For the forward-only base this must be served from the buffer (the base
            // cannot seek back). Head region backward access mirrors the header/table/FST
            // re-reads the real reader performs.
            using BufferStream c = make(seekable);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, 0x800);            // read head
            Assert.Equal(0x800, c.Position);

            c.Seek(0x100, SeekOrigin.Begin);   // seek backward into what we already read
            Assert.Equal(0x100, c.Position);
            c.Read(data, 0, 0x400);
            Assert.Equal(0x500, c.Position);
            testBytes(0x100, data, 0, 0x400);

            c.Seek(0x800, SeekOrigin.Begin);   // forward again to continue
            c.Read(data, 0, 0x400);
            Assert.Equal(0xC00, c.Position);
            testBytes(0x800, data, 0, 0x400);
        }

        [Fact]
        public void OverreadReturnsAvailableOnly()
        {
            using BufferStream c = make(true);
            byte[] data = new byte[Size + 0x1000];
            Assert.Equal(Size, c.Read(data, 0, data.Length));
            testBytes(0, data, 0, Size);
        }

        [Fact]
        public void SeekableBaseReportsCanSeekTrue()
        {
            using BufferStream c = make(true);
            Assert.True(c.CanSeek);
        }

        [Fact]
        public void ForwardOnlyBaseReportsCanSeekFalse()
        {
            using BufferStream c = make(false);
            Assert.False(c.CanSeek); // arbitrary random-access consumers stay disabled
        }

        // ── CanSeekTo (offset-aware reach guard) ────────────────────────────────

        [Fact]
        public void CanSeekTo_SeekableBase_AlwaysTrue()
        {
            // A seekable source is served directly (uncached), so any offset is reachable for
            // free — even one far beyond the seek-reach limit.
            using BufferStream c = new BufferStream(new VirtualForwardStream(_bigLength, seekable: true));
            Assert.True(c.CanSeekTo(0));
            Assert.True(c.CanSeekTo(_bigLength - 1));
            Assert.True(c.CanSeekTo(SeekReachLimit + (10L * 1024 * 1024)));
        }

        [Fact]
        public void CanSeekTo_ForwardOnly_AllowsWithinReachLimit()
        {
            using BufferStream c = new BufferStream(new VirtualForwardStream(_bigLength, seekable: false));
            // Floor starts at 0. Anything up to the limit is allowed (would be cached), anything
            // beyond is refused so the consumer falls back to a calculated value.
            Assert.True(c.CanSeekTo(0));
            Assert.True(c.CanSeekTo(SeekReachLimit));
            Assert.True(c.CanSeekTo(SeekReachLimit - 1));
        }

        [Fact]
        public void CanSeekTo_ForwardOnly_RefusesBeyondReachLimit()
        {
            using BufferStream c = new BufferStream(new VirtualForwardStream(_bigLength, seekable: false));
            Assert.False(c.CanSeekTo(SeekReachLimit + 1));
            Assert.False(c.CanSeekTo(_bigLength - 1)); // e.g. the far PS3 eboot inside an archive
        }

        [Fact]
        public void CanSeekTo_ForwardOnly_MeasuredFromFloorNotFrontier()
        {
            // The guard measures reach from the released floor, not from the current read cursor,
            // so a consumer cannot defeat it by nudging forward without releasing: everything it
            // keeps counts. Read a little (advancing the frontier) WITHOUT releasing, then a far
            // offset must still be refused because the floor is still 0.
            using BufferStream c = new BufferStream(new VirtualForwardStream(_bigLength, seekable: false));
            byte[] tmp = new byte[0x1000];
            c.Read(tmp, 0, tmp.Length); // frontier advances, floor stays 0
            Assert.False(c.CanSeekTo(SeekReachLimit + 1));

            // After releasing, the floor moves up and the same absolute target comes within reach.
            long newFloor = 4L * 1024 * 1024 * 1024; // 4 GiB
            c.ReleaseTo(newFloor);
            Assert.True(c.CanSeekTo(newFloor + SeekReachLimit));
            Assert.False(c.CanSeekTo(newFloor + SeekReachLimit + 1));
        }

        // ── ReleaseTo / Retain (memory bound) ───────────────────────────────────

        [Fact]
        public void ReleaseTo_ForwardOnly_ReadBelowFloorThrows()
        {
            using BufferStream c = make(false);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, 0x800);      // read head so there is something to release
            c.ReleaseTo(0x400);          // free everything below 0x400

            c.Seek(0x100, SeekOrigin.Begin);
            Assert.Throws<InvalidOperationException>(() => c.Read(data, 0, 0x100));
        }

        [Fact]
        public void ReleaseTo_ForwardOnly_AtOrAboveFloorStillReadable()
        {
            using BufferStream c = make(false);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, 0x800);
            c.ReleaseTo(0x400);

            // At/above the floor still serves correctly.
            c.Seek(0x400, SeekOrigin.Begin);
            Assert.Equal(0x100, c.Read(data, 0, 0x100));
            testBytes(0x400, data, 0, 0x100);
        }

        [Fact]
        public void ReleaseTo_SeekableBase_NoOp_BackwardStillWorks()
        {
            // A seekable source caches nothing, so ReleaseTo is a no-op and backward access below
            // the "released" mark must still succeed (served directly from the source).
            using BufferStream c = make(true);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, 0x800);
            c.ReleaseTo(0x400);

            c.Seek(0x100, SeekOrigin.Begin);
            Assert.Equal(0x100, c.Read(data, 0, 0x100));
            testBytes(0x100, data, 0, 0x100);
        }

        [Fact]
        public void ForwardRead_WithReleaseTo_BoundsMemory_OverLargeSource()
        {
            // Regression guard for the PS3/WiiU OOM: a consumer reading a large forward-only
            // source strictly forward and calling ReleaseTo after each chunk (as the Image readers
            // do) must not retain the whole source. We can't measure RAM directly, but the floor
            // advancing means blocks below it were freed — a read there now throws. If ReleaseTo
            // were absent (the bug), every block from 0 to the frontier would stay resident.
            const long total = 2L * 1024 * 1024 * 1024; // 2 GiB virtual source
            const int chunk = 32 * 1024 * 1024;         // 32 MiB reads
            using BufferStream c = new BufferStream(new VirtualForwardStream(total, seekable: false));
            byte[] buf = new byte[chunk];

            long pos = 0;
            for (int i = 0; i < 8; i++) // read 256 MiB forward, releasing as we go
            {
                int n = c.Read(buf, 0, chunk);
                pos += n;
                c.ReleaseTo(pos); // mirror Image.Read: release up to the consumed mark
            }

            // Everything below the released mark has been freed: a read there must throw.
            c.Seek(0, SeekOrigin.Begin);
            Assert.Throws<InvalidOperationException>(() => c.Read(buf, 0, chunk));

            // But reading at/after the current position still works.
            c.Seek(pos, SeekOrigin.Begin);
            Assert.Equal(chunk, c.Read(buf, 0, chunk));
        }

        [Fact]
        public void Retain_ForwardOnly_PreventsReleaseBelowRetainFloor()
        {
            // Retain declares a region a consumer may seek back to (e.g. XBox FST re-read). A
            // later ReleaseTo above it must not free that region.
            using BufferStream c = make(false);
            byte[] data = new byte[0x1000];

            c.Read(data, 0, 0x800);
            c.Retain(0x200);            // must keep from 0x200 onward
            c.ReleaseTo(0x600);         // try to release past the retain floor

            c.Seek(0x200, SeekOrigin.Begin);
            Assert.Equal(0x100, c.Read(data, 0, 0x100)); // still available because retained
            testBytes(0x200, data, 0, 0x100);

            c.Retain(long.MaxValue);    // clear the retain constraint
        }

        // ── Clone (shared manager) ──────────────────────────────────────────────

        [Fact]
        public void Clone_SharesManager_IndependentPositions()
        {
            using BufferStream a = make(false);
            byte[] data = new byte[0x1000];

            a.Read(data, 0, 0x400);
            Assert.Equal(0x400, a.Position);

            using BufferStream b = a.Clone();
            Assert.Equal(0, b.Position);       // clone starts at 0, independent cursor

            b.Read(data, 0, 0x200);
            Assert.Equal(0x200, b.Position);
            Assert.Equal(0x400, a.Position);   // a is unaffected
            testBytes(0, data, 0, 0x200);
        }

        [Fact]
        public void Clone_ForwardOnly_ServesBackwardFromSharedCache()
        {
            // The clone reads the SAME forward-only source through the shared cache: bytes the
            // first view already pulled forward are served to the clone even though the base
            // cannot seek backward.
            using BufferStream a = make(false);
            byte[] data = new byte[0x1000];

            a.Read(data, 0, 0x800);            // advance the shared frontier
            using BufferStream b = a.Clone();
            b.Seek(0x100, SeekOrigin.Begin);   // clone reads a region already fetched
            Assert.Equal(0x200, b.Read(data, 0, 0x200));
            testBytes(0x100, data, 0, 0x200);
        }

        [Fact]
        public void Clone_LastDisposeReleasesSource()
        {
            // The manager is reference counted: the source is disposed only when the last view
            // detaches.
            byte[] d = numberData();
            TrackDisposeStream src = new TrackDisposeStream(d);
            BufferStream a = new BufferStream(src);
            BufferStream b = a.Clone();

            a.Dispose();
            Assert.False(src.Disposed);        // b still holds a reference
            b.Dispose();
            Assert.True(src.Disposed);         // last view gone -> source disposed
        }

        // The BufferStreamManager's SeekReachLimit is 8.5 GiB. Kept in sync here for the reach
        // tests; if the manager constant changes this must change too.
        private const long SeekReachLimit = 8704L * 1024 * 1024;
        private const long _bigLength = 48L * 1024 * 1024 * 1024; // 48 GiB, models a PS3 image

        // A forward-only stream that reports an arbitrary (possibly huge) Length WITHOUT
        // allocating it. Reads produce deterministic numberData-style bytes for the low region so
        // small reads still verify; far reads are never performed in these tests (CanSeekTo gates
        // them, and reading tens of GiB is not attempted).
        private sealed class VirtualForwardStream : Stream
        {
            private readonly long _length;
            private readonly bool _seekable;
            private long _pos;
            public VirtualForwardStream(long length, bool seekable) { _length = length; _seekable = seekable; }
            public override bool CanRead => true;
            public override bool CanSeek => _seekable;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position
            {
                get => _pos;
                set { if (!_seekable) throw new NotSupportedException(); _pos = value; }
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, _length - _pos);
                if (n <= 0) return 0;
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = (byte)((_pos + i) & 0xff);
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                if (!_seekable) throw new NotSupportedException();
                _pos = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _pos + offset,
                    SeekOrigin.End => _length + offset,
                    _ => _pos,
                };
                return _pos;
            }
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // Forward-only stream that records whether it was disposed, for refcount tests.
        private sealed class TrackDisposeStream : Stream
        {
            private readonly byte[] _data;
            private long _pos;
            public bool Disposed { get; private set; }
            public TrackDisposeStream(byte[] data)
            {
                _data = data;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = (int)Math.Min(count, _data.Length - _pos);
                if (n <= 0) return 0;
                Array.Copy(_data, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        private static BufferStream make(bool seekable)
        {
            byte[] d = numberData();
            Stream b = seekable ? new MemoryStream(d) : (Stream)new ForwardOnlyStream(d);
            return new BufferStream(b);
        }
    }
}