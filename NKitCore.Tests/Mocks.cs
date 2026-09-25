using NKitCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore.Tests
{
    /// <summary>An in-memory stream of a given total length split into one or more areas.</summary>
    internal sealed class MockStream : IStream
    {
        private readonly long _length;
        public MockStream(IReadOnlyList<IArea> areas, long length)
        {
            InitialAreas = areas;
            _length = length;
        }
        public IReadOnlyList<IArea> InitialAreas { get; }
        public long? Length => _length;

        public Task<int> ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken ct)
        {
            // Fill buffer with a deterministic pattern derived from offset (so processing can verify).
            var span = buffer.Span;
            for (int i = 0; i < span.Length; i++)
                span[i] = (byte)((offset + i) & 0xFF);
            return Task.FromResult(buffer.Length);
        }
    }

    /// <summary>Records every section seen at pre-process, in call order (single serial stage).</summary>
    internal sealed class RecordingPreProcess : ISectionPreProcess
    {
        public readonly ConcurrentQueue<long> Order = new ConcurrentQueue<long>();
        public Task PreProcessAsync(ISectionContext ctx, ISection section, CancellationToken ct)
        {
            Order.Enqueue(section.SequenceNumber);
            return Task.CompletedTask;
        }
        public Task OnSeekAsync(ISectionContext ctx, SeekRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Processor with configurable per-sequence delay. Use <see cref="DelayForSeq"/> to make a
    /// specific block slow (simulates a heavy section that others race ahead of).
    /// </summary>
    internal sealed class DelayProcessor : ISectionProcess
    {
        public Func<long, int> DelayForSeq = _ => 0;               // ms per sequence
        public readonly ConcurrentDictionary<long, int> ProcessedCount = new ConcurrentDictionary<long, int>();

        public async Task ProcessAsync(ISectionContext ctx, ISection section, CancellationToken ct)
        {
            var ms = DelayForSeq(section.SequenceNumber);
            if (ms > 0) await Task.Delay(ms, ct).ConfigureAwait(false);
            ProcessedCount.AddOrUpdate(section.SequenceNumber, 1, (_, v) => v + 1);
        }
        public Task OnSeekAsync(ISectionContext ctx, SeekRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Ordered-output completer. Records the exact emit order and the count per sequence so tests
    /// can assert strict ordering and exactly-once emission. Optional per-seq write delay simulates
    /// a slow (write-bound) Out stage. Also asserts single-writer by tracking concurrent entries.
    /// </summary>
    internal sealed class RecordingCompleter : ISectionCompleter
    {
        public readonly List<long> EmitOrder = new List<long>();
        public readonly ConcurrentDictionary<long, int> EmitCount = new ConcurrentDictionary<long, int>();
        public Func<long, int> WriteDelayForSeq = _ => 0;

        private int _concurrentWriters;
        public int MaxConcurrentWriters;
        public bool Finalized;

        public async Task<SectionCompleteResult> CompleteAsync(ISectionContext ctx, ISection section, CancellationToken ct)
        {
            int now = Interlocked.Increment(ref _concurrentWriters);
            // Track the peak concurrency observed inside the completer (must stay 1).
            int prevMax;
            do { prevMax = MaxConcurrentWriters; } while (now > prevMax && Interlocked.CompareExchange(ref MaxConcurrentWriters, now, prevMax) != prevMax);

            try
            {
                var ms = WriteDelayForSeq(section.SequenceNumber);
                if (ms > 0) await Task.Delay(ms, ct).ConfigureAwait(false);
                lock (EmitOrder) { EmitOrder.Add(section.SequenceNumber); }
                EmitCount.AddOrUpdate(section.SequenceNumber, 1, (_, v) => v + 1);
                return SectionCompleteResult.Continue();
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentWriters);
            }
        }

        public Task FinalizeAsync(ISectionContext ctx, CancellationToken ct)
        {
            Finalized = true;
            return Task.CompletedTask;
        }
    }

    internal static class TestHarness
    {
        public static (MockStream stream, DefaultSectionContext ctx) Build(int areaCount, long areaSize, int sectionSize)
        {
            var areas = new List<IArea>();
            long off = 0;
            for (int i = 0; i < areaCount; i++) { areas.Add(new Area(i, off, areaSize)); off += areaSize; }
            var stream = new MockStream(areas, off);
            var ctx = new DefaultSectionContext(Guid.NewGuid(), sectionSize, () => new MemorySection(sectionSize));
            return (stream, ctx);
        }

        public static long ExpectedSectionCount(long areaCount, long areaSize, int sectionSize)
        {
            long perArea = (areaSize + sectionSize - 1) / sectionSize;
            return perArea * areaCount;
        }
    }
}