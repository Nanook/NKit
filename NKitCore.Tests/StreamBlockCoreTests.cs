using NKitCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NKitCore.Tests
{
    public class StreamBlockCoreTests
    {
        private static StreamBlockCore<MemorySection, DefaultSectionContext> NewCore(int parallelism)
            => new StreamBlockCore<MemorySection, DefaultSectionContext>(
                   maxParallelism: parallelism,
                   autoscaleOptions: new StreamBlockCore<MemorySection, DefaultSectionContext>.AutoscaleOptions
                   { MinWorkers = 1, MaxWorkers = parallelism });

        private static async Task Run(
            StreamBlockCore<MemorySection, DefaultSectionContext> core,
            MockStream stream, DefaultSectionContext ctx,
            RecordingPreProcess pre, DelayProcessor proc, RecordingCompleter comp,
            int timeoutSec = 30)
        {
            var factory = new DefaultSectionFactory();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            await core.ProcessAsync(ctx, stream, factory, pre, proc, comp, cts.Token).ConfigureAwait(false);
        }

        [Fact]
        public async Task EmitsAllSections_InStrictOrder_ExactlyOnce()
        {
            int sectionSize = 64 * 1024;
            long areaSize = 4L * 1024 * 1024;
            int areaCount = 3;
            var (stream, ctx) = TestHarness.Build(areaCount, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor();
            var comp = new RecordingCompleter();

            await Run(NewCore(8), stream, ctx, pre, proc, comp);

            long expected = TestHarness.ExpectedSectionCount(areaCount, areaSize, sectionSize);
            Assert.Equal(expected, comp.EmitOrder.Count);

            // strict ascending order 1..expected
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);

            // exactly once each
            Assert.All(comp.EmitCount.Values, v => Assert.Equal(1, v));
            Assert.Equal((int)expected, comp.EmitCount.Count);

            // every section was processed exactly once
            Assert.All(proc.ProcessedCount.Values, v => Assert.Equal(1, v));
            Assert.True(comp.Finalized);
        }

        [Fact]
        public async Task SingleWriter_NeverConcurrent()
        {
            int sectionSize = 32 * 1024;
            long areaSize = 8L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(1, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor();
            // small random processing jitter to force out-of-order completion
            var rnd = new Random(1234);
            proc.DelayForSeq = _ => rnd.Next(0, 4);
            var comp = new RecordingCompleter { WriteDelayForSeq = _ => 1 };

            await Run(NewCore(8), stream, ctx, pre, proc, comp);

            Assert.Equal(1, comp.MaxConcurrentWriters); // never two writers at once
            // still strictly ordered
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);
        }

        [Fact]
        public async Task SlowHeadBlock_DoesNotDeadlock_And_OutputStaysOrdered()
        {
            // Make sequence 1 (the FIRST/head block) very slow while all others are fast. Workers
            // will race far ahead completing 2..N, which must WAIT (not deadlock) for 1 to finish,
            // then drain in order. This is the head-of-line scenario the design targets.
            int sectionSize = 16 * 1024;
            long areaSize = 8L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(1, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor { DelayForSeq = seq => seq == 1 ? 400 : 0 };
            var comp = new RecordingCompleter();

            await Run(NewCore(8), stream, ctx, pre, proc, comp, timeoutSec: 30);

            long expected = TestHarness.ExpectedSectionCount(1, areaSize, sectionSize);
            Assert.Equal(expected, comp.EmitOrder.Count);
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);
        }

        [Fact]
        public async Task SlowMidBlock_DoesNotDeadlock()
        {
            // A slow block in the MIDDLE: workers complete everything after it and must hold until
            // the mid block emits, in order.
            int sectionSize = 16 * 1024;
            long areaSize = 8L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(1, areaSize, sectionSize);
            long total = TestHarness.ExpectedSectionCount(1, areaSize, sectionSize);
            long midSeq = total / 2;
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor { DelayForSeq = seq => seq == midSeq ? 300 : 0 };
            var comp = new RecordingCompleter();

            await Run(NewCore(8), stream, ctx, pre, proc, comp, timeoutSec: 30);

            Assert.Equal(total, comp.EmitOrder.Count);
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);
        }

        [Fact]
        public async Task WriteBound_Out_DoesNotLoseOrdering_Or_Sections()
        {
            // Slow serial Out (write-bound). Workers finish fast; the single writer paces emission.
            int sectionSize = 32 * 1024;
            long areaSize = 4L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(2, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor();
            var comp = new RecordingCompleter { WriteDelayForSeq = _ => 2 };

            await Run(NewCore(8), stream, ctx, pre, proc, comp, timeoutSec: 40);

            long expected = TestHarness.ExpectedSectionCount(2, areaSize, sectionSize);
            Assert.Equal(expected, comp.EmitOrder.Count);
            Assert.Equal(1, comp.MaxConcurrentWriters);
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(16)]
        public async Task StressRandomDelays_Ordered_ExactlyOnce_NoDeadlock(int parallelism)
        {
            int sectionSize = 8 * 1024;
            long areaSize = 3L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(4, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var rnd = new Random(parallelism * 99 + 7);
            var proc = new DelayProcessor { DelayForSeq = _ => rnd.Next(0, 6) };
            var comp = new RecordingCompleter { WriteDelayForSeq = _ => rnd.Next(0, 3) };

            await Run(NewCore(parallelism), stream, ctx, pre, proc, comp, timeoutSec: 60);

            long expected = TestHarness.ExpectedSectionCount(4, areaSize, sectionSize);
            Assert.Equal(expected, comp.EmitOrder.Count);
            Assert.Equal(1, comp.MaxConcurrentWriters);
            for (int i = 0; i < comp.EmitOrder.Count; i++)
                Assert.Equal(i + 1, comp.EmitOrder[i]);
            Assert.All(comp.EmitCount.Values, v => Assert.Equal(1, v));
        }

        [Fact]
        public async Task Cancellation_StopsPromptly()
        {
            int sectionSize = 16 * 1024;
            long areaSize = 64L * 1024 * 1024; // large so it can't finish before cancel
            var (stream, ctx) = TestHarness.Build(1, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor { DelayForSeq = _ => 5 };
            var comp = new RecordingCompleter();
            var factory = new DefaultSectionFactory();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(200);
            var core = NewCore(4);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await core.ProcessAsync(ctx, stream, factory, pre, proc, comp, cts.Token));
        }

        [Fact]
        public async Task MemoryBounded_ReadyHeld_StaysSmall()
        {
            // With a slow head block and fast rest, the ready-held set (buffers awaiting their turn)
            // must stay bounded by the in-flight cap, not grow to the whole image.
            int sectionSize = 8 * 1024;
            long areaSize = 16L * 1024 * 1024;
            var (stream, ctx) = TestHarness.Build(1, areaSize, sectionSize);
            var pre = new RecordingPreProcess();
            var proc = new DelayProcessor { DelayForSeq = seq => seq == 1 ? 500 : 1 };
            var comp = new RecordingCompleter();

            long maxReadyHeld = 0;
            // Pin the pool (Min==Start==Max==8) and a fixed look-ahead so the reorder window is
            // deterministic: window = workers + LookAheadDepth = 8 + 4 = 12.
            const int workers = 8;
            const int lookAhead = 4;
            var opts = new StreamBlockCore<MemorySection, DefaultSectionContext>.AutoscaleOptions
            {
                MinWorkers = workers,
                StartWorkers = workers,
                MaxWorkers = workers,
                LookAheadDepth = lookAhead,
            };
            var core = new StreamBlockCore<MemorySection, DefaultSectionContext>(
                maxParallelism: workers,
                autoscaleOptions: opts,
                metricsSink: snap => { var r = snap.ReadyHeld; if (r > Interlocked.Read(ref maxReadyHeld)) Interlocked.Exchange(ref maxReadyHeld, r); },
                metricsIntervalMs: 20);

            var factory = new DefaultSectionFactory();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await core.ProcessAsync(ctx, stream, factory, pre, proc, comp, cts.Token);

            long total = TestHarness.ExpectedSectionCount(1, areaSize, sectionSize);
            // The admission window (workers + look-ahead) bounds the total live sections, so ReadyHeld
            // (finished sections awaiting their in-order Out turn) can never exceed it — far below the
            // whole image. Allow the full window as the ceiling.
            Assert.True(maxReadyHeld <= workers + lookAhead, $"ready-held peaked at {maxReadyHeld} (workers {workers}, lookAhead {lookAhead}; total sections {total})");
            Assert.True(maxReadyHeld < total, "ready-held should be far below the whole image");
        }
    }
}