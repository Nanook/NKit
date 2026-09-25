using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;


namespace NKit.Tests.Engine.ImageReading
{
    /// <summary>
    /// Tests the shared <see cref="FileSystemCoverage"/> loop that drives an
    /// <see cref="IFileSystemReader"/> from the IImage side: reading pending regions via the
    /// BufferStream (CanSeekTo-guarded, cursor-restored, Retain-bracketed) until coverage is met.
    /// </summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "ImageReading")]
    public class FileSystemCoverageTests
    {
        private const int Size = 0x100000;

        private static byte[] numberData()
        {
            byte[] d = new byte[Size];
            for (int i = 0; i < d.Length; i += 4)
                d.WriteUInt32B(i, (uint)i);
            return d;
        }

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

        // Minimal IBuffer for the coverage loop — only Decrypted + Update are exercised.
        private sealed class StubBuffer : IBuffer
        {
            public byte[] Decrypted { get; }
            public long LastImageOffset { get; private set; } = -1;
            public int LastSize { get; private set; }
            public StubBuffer(int size)
            {
                Decrypted = new byte[size];
            }

            public void Update(long imageOffset, long areaOffset, int size, int fsIndex, bool isEncrypted, bool skippedTo)
            {
                LastImageOffset = imageOffset;
                LastSize = size;
            }
            // Unused members
            public AreaInfo AreaInfo => null;
            public long ImageOffset => 0;
            public long AreaOffset => 0;
            public int Size => 0;
            public int BlockSize => 0;
            public int BlockFsOffset => 0;
            public int BlockFsSize => 0;
            public byte[] Encrypted => null;
            public bool IsEncrypted => false;
            public int FileEndIndex { get; set; }
            public int FileStartIndex { get; set; }
            public IPatchInfo PatchInfo => null;
            public List<MetaData> MissingData => null;
            public AreaType Type => AreaType.FileSystem;
            public bool SkippedTo => false;
            public IBuffer Clone() => throw new NotSupportedException();
            public void CopyTo(IBuffer buffer) => throw new NotSupportedException();
            public byte[] ReadFsBytes(int fsOffset, int fsSize) => throw new NotSupportedException();
            public void ReadFs(int f, byte[] d, int a, int b, int c, int e, int g) => throw new NotSupportedException();
            public void WriteFs(byte[] s, int a, int b, int c, int d, int e, int f) => throw new NotSupportedException();
            public bool ProcessFsData(int f, int s, Func<byte[], int, int, int, bool> p) => throw new NotSupportedException();
            public long FsOffset => 0;
            public int FsSize => 0;
            public int FsIndex => 0;
            public void ReadFsToStream(int f, Stream s, int z) => throw new NotSupportedException();
            public void TestFsRange(long f, long s, RangeResult r) => throw new NotSupportedException();
            public void TestRange(long o, long s, RangeResult r) => throw new NotSupportedException();
            public void TestRangeBounds(long o, long s, long co, long cs, RangeResult r) => throw new NotSupportedException();
            public void ReInitialise(AreaInfo a, bool c) { }
            public void WriteFsFromStream(int f, Stream s, int z) => throw new NotSupportedException();
            public void SetFsMissingData(int f, int s, MetaDataType t, byte b) => throw new NotSupportedException();
            public void ClearFs(int f, int s) => throw new NotSupportedException();
            public uint CrcFsData(int f, int s) => 0;
            public ulong XxHashFsData(int f, int s) => 0;
        }

        // Synthetic reader: fed a queue of pending regions; records which offsets it was given,
        // and pops the region it just received. Complete when the queue drains.
        private sealed class StubReader : IFileSystemReader
        {
            private readonly List<FsRegionRequest> _pending;
            public readonly List<long> Fed = new List<long>();
            public bool RequireFullFileSystemUpFront { get; set; }

            public StubReader(bool requireFull, params FsRegionRequest[] pending)
            {
                RequireFullFileSystemUpFront = requireFull;
                _pending = new List<FsRegionRequest>(pending);
            }

            public void ProcessBlock(IBuffer buffer)
            {
                StubBuffer b = (StubBuffer)buffer;
                Fed.Add(b.LastImageOffset);
                // Remove the region matching what was fed.
                int idx = _pending.FindIndex(r => r.ImageOffset == b.LastImageOffset);
                if (idx >= 0) _pending.RemoveAt(idx);
            }

            public IReadOnlyList<FsRegionRequest> Pending => _pending;
            public bool Complete => _pending.Count == 0;
        }

        // Reader that reports a pending region but never consumes it on ProcessBlock (models a
        // malformed FS / non-progressing pass).
        private sealed class StuckReader : IFileSystemReader
        {
            private readonly List<FsRegionRequest> _pending;
            public int ProcessCalls;
            public StuckReader(FsRegionRequest region)
            {
                _pending = new List<FsRegionRequest> { region };
            }

            public void ProcessBlock(IBuffer buffer) => ProcessCalls++; // never drains _pending
            public IReadOnlyList<FsRegionRequest> Pending => _pending;
            public bool Complete => _pending.Count == 0;
            public bool RequireFullFileSystemUpFront => true;
        }

        private static AreaInfo area() => new AreaInfo(0, AreaType.FileSystem, 0);

        private static BufferStream forwardOnly() => new BufferStream(new ForwardOnlyStream(numberData()));

        [Fact]
        public void AlreadyComplete_DoesNothing()
        {
            BufferStream s = forwardOnly();
            StubReader r = new StubReader(false); // no pending => Complete
            long before = s.Position;
            FileSystemCoverage.ResolveThrough(r, s, area(), long.MaxValue, 0, () => new StubBuffer(0x1000));
            Assert.Empty(r.Fed);
            Assert.Equal(before, s.Position);
        }

        [Fact]
        public void FullUpFront_ResolvesAllPending()
        {
            BufferStream s = forwardOnly();
            StubReader r = new StubReader(true,
                new FsRegionRequest(0x1000, 0x800),
                new FsRegionRequest(0x4000, 0x800));
            FileSystemCoverage.ResolveThrough(r, s, area(), 0 /*ignored when full*/, 0, () => new StubBuffer(0x1000));
            Assert.True(r.Complete);
            Assert.Contains(0x1000L, r.Fed);
            Assert.Contains(0x4000L, r.Fed);
        }

        [Fact]
        public void CoverageThrough_ResolvesOnlyInRange()
        {
            BufferStream s = forwardOnly();
            StubReader r = new StubReader(false,
                new FsRegionRequest(0x1000, 0x800),   // below target
                new FsRegionRequest(0x40000, 0x800)); // above target
            // Not full-up-front; resolve only regions below 0x2000.
            FileSystemCoverage.ResolveThrough(r, s, area(), 0x2000, 0, () => new StubBuffer(0x1000));
            Assert.Contains(0x1000L, r.Fed);
            Assert.DoesNotContain(0x40000L, r.Fed);
            Assert.False(r.Complete); // the out-of-range region is still pending
        }

        [Fact]
        public void RestoresCursorAfterResolving()
        {
            BufferStream s = forwardOnly();
            s.Position = 0x8000; // caller's sequential cursor
            StubReader r = new StubReader(true, new FsRegionRequest(0x1000, 0x800));
            FileSystemCoverage.ResolveThrough(r, s, area(), long.MaxValue, 0, () => new StubBuffer(0x1000));
            Assert.Equal(0x8000, s.Position); // cursor restored
        }

        [Fact]
        public void OutOfReach_DegradesGracefully_NoThrow()
        {
            BufferStream s = forwardOnly(); // forward-only: CanSeekTo false beyond reach limit
            // A region far beyond the 8.5 GiB reach limit from floor 0 — CanSeekTo returns false.
            long farOffset = 9L * 1024 * 1024 * 1024;
            StubReader r = new StubReader(true, new FsRegionRequest(farOffset, 0x800));
            // Must not throw; the region is skipped and the loop ends (no progress).
            FileSystemCoverage.ResolveThrough(r, s, area(), long.MaxValue, 0, () => new StubBuffer(0x1000));
            Assert.Empty(r.Fed);
            Assert.False(r.Complete); // unresolved, but no crash
        }

        [Fact]
        public void NonProgressingLoop_Breaks()
        {
            BufferStream s = forwardOnly();
            StuckReader r = new StuckReader(new FsRegionRequest(0x1000, 0x800));
            // Reader never drains its pending on ProcessBlock — loop must break, not spin.
            FileSystemCoverage.ResolveThrough(r, s, area(), long.MaxValue, 0, () => new StubBuffer(0x1000));
            // It made at most one pass (fed once) then saw no progress and stopped.
            Assert.True(r.ProcessCalls <= 1);
            Assert.False(r.Complete);
        }
    }
}