using Nanook.NKit;
using Nanook.NKit.Runtime;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

// Grouped by [Trait("Area","Logging")] as well as the namespace (see NKitLogTests.cs note).
namespace NKit.Tests.Logging
{
    /// <summary>
    /// Tests for the async sink base: background draining, clean flush, and drop-on-overflow (never
    /// block the producer). These validate the "never intrusive" guarantee.
    /// </summary>
    [Trait("Area", "Logging")]
    public class NKitLogAsyncSinkTests
    {
        // Async sink whose Write blocks on a gate, so we can force the bounded queue to fill.
        private sealed class GatedAsyncSink : AsyncLogSink
        {
            private readonly ManualResetEventSlim _gate;
            public readonly List<string> Written = new List<string>();
            public long DroppedReported;

            public GatedAsyncSink(ManualResetEventSlim gate, int capacity)
                : base(LogLevel.Trace, capacity)
            {
                _gate = gate;
            }

            protected override void Write(LogEvent evt)
            {
                _gate.Wait();
                lock (Written) Written.Add(evt.Message);
            }

            protected override void WriteDroppedMarker(long count) => Interlocked.Add(ref DroppedReported, count);
        }

        [Fact]
        public void DrainsAndFlushesAllEvents()
        {
            using ManualResetEventSlim gate = new ManualResetEventSlim(true); // open — writes proceed
            using LogBus bus = new LogBus(LogLevel.Trace);
            GatedAsyncSink sink = new GatedAsyncSink(gate, 1024);
            bus.AddSink(sink);
            ILogScope s = bus.CreateScope("root");

            for (int i = 0; i < 100; i++)
                s.Log(LogLevel.Detail, "m" + i);

            sink.Flush(); // waits for the drain thread to write everything queued
            Assert.Equal(100, sink.Written.Count);
        }

        [Fact]
        public void OverflowDropsAndNeverBlocksProducer()
        {
            // Gate CLOSED: the drain thread blocks in Write on the first item, so the bounded queue
            // fills and further adds must DROP (not block the producer).
            using ManualResetEventSlim gate = new ManualResetEventSlim(false);
            using LogBus bus = new LogBus(LogLevel.Trace);
            GatedAsyncSink sink = new GatedAsyncSink(gate, 4); // tiny capacity
            bus.AddSink(sink);
            ILogScope s = bus.CreateScope("root");

            // Emit far more than capacity. If this blocked, the test would hang; a timeout guard
            // via a worker thread proves non-blocking.
            Thread producer = new Thread(() =>
            {
                for (int i = 0; i < 10000; i++)
                    s.Log(LogLevel.Detail, "m");
            });
            producer.Start();
            bool finished = producer.Join(TimeSpan.FromSeconds(5));

            Assert.True(finished, "producer blocked — logging applied backpressure (must never happen)");

            // Release the drain thread and flush; a dropped-count marker must have been recorded.
            gate.Set();
            sink.Flush();
            Assert.True(sink.DroppedReported > 0, "expected some events to be dropped under overflow");
        }
    }
}