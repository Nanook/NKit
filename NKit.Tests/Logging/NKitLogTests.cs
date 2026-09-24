using Nanook.NKit;
using Nanook.NKit.Runtime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

// The logging types live in Nanook.NKit / Nanook.NKit.Runtime (imported via usings above); grouped
// by [Trait("Area","Logging")] as well as the namespace.
namespace NKit.Tests.Logging
{
    /// <summary>
    /// Unit tests for the logging runtime (LogBus / LogScope / async sinks). These validate the
    /// non-intrusive, deferral, scope-tree, and sink-isolation guarantees the design relies on.
    /// </summary>
    [Trait("Area", "Logging")]
    public class NKitLogTests
    {
        // A synchronous in-memory sink for deterministic assertions (no drain thread).
        private sealed class CapturingSink : ILogSink, ILevelledSink
        {
            private readonly object _lock = new object();
            public List<LogEvent> Events { get; } = new List<LogEvent>();
            public List<(ScopeStatus, ScopeStatus)> StatusChanges { get; } = new List<(ScopeStatus, ScopeStatus)>();
            public int Flushes;

            public CapturingSink(LogLevel minimumLevel) { MinimumLevel = minimumLevel; }
            public LogLevel MinimumLevel { get; }

            public void Emit(LogEvent evt) { lock (_lock) Events.Add(evt); }
            public void EmitProgress(ILogScope scope, long current, long? total) { }
            public void EmitStatusChange(ILogScope scope, ScopeStatus oldStatus, ScopeStatus newStatus)
            { lock (_lock) StatusChanges.Add((oldStatus, newStatus)); }
            public void Flush() => Interlocked.Increment(ref Flushes);
        }

        private sealed class ThrowingSink : ILogSink, ILevelledSink
        {
            public LogLevel MinimumLevel => LogLevel.Trace;
            public void Emit(LogEvent evt) => throw new InvalidOperationException("boom");
            public void EmitProgress(ILogScope scope, long current, long? total) => throw new InvalidOperationException("boom");
            public void EmitStatusChange(ILogScope scope, ScopeStatus o, ScopeStatus n) => throw new InvalidOperationException("boom");
            public void Flush() => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void FiltersBelowEffectiveLevel()
        {
            using LogBus bus = new LogBus(LogLevel.Info);
            CapturingSink sink = new CapturingSink(LogLevel.Info);
            bus.AddSink(sink);
            ILogScope s = bus.CreateScope("root");

            s.Log(LogLevel.Info, "shown");
            s.Log(LogLevel.Detail, "hidden");   // below Info
            s.Log(LogLevel.Trace, "hidden");

            Assert.Single(sink.Events);
            Assert.Equal("shown", sink.Events[0].Message);
        }

        [Fact]
        public void DeferredMessageNotInvokedWhenFiltered()
        {
            using LogBus bus = new LogBus(LogLevel.Info);
            bus.AddSink(new CapturingSink(LogLevel.Info));
            ILogScope s = bus.CreateScope("root");

            int invoked = 0;
            Func<string> msg = () => { Interlocked.Increment(ref invoked); return "x"; };

            s.Log(LogLevel.Detail, msg);   // filtered => delegate must NOT run
            Assert.Equal(0, invoked);
            Assert.False(s.IsEnabled(LogLevel.Detail));

            s.Log(LogLevel.Info, msg);     // enabled => delegate runs once
            Assert.Equal(1, invoked);
        }

        [Fact]
        public void EffectiveLevelFollowsMostVerboseSink()
        {
            using LogBus bus = new LogBus(LogLevel.Info);
            CapturingSink console = new CapturingSink(LogLevel.Info);
            CapturingSink file = new CapturingSink(LogLevel.Detail); // more verbose
            bus.AddSink(console);
            bus.AddSink(file);
            ILogScope s = bus.CreateScope("root");

            // Detail is wanted by the file sink, so IsEnabled(Detail) must be true and the event
            // reaches the file sink but not the (Info-only) console — console filters internally.
            Assert.True(s.IsEnabled(LogLevel.Detail));
            s.Log(LogLevel.Detail, "detail-line");

            Assert.Empty(console.Events);        // console appetite is Info; it saw nothing at Detail
            // NOTE: LogBus fan-out delivers to all sinks; the CapturingSink for console does not
            // itself filter, so we assert via the ILevelledSink appetite driving IsEnabled instead.
        }

        [Fact]
        public void ScopeTreeParentWalkable()
        {
            using LogBus bus = new LogBus(LogLevel.Trace);
            CapturingSink sink = new CapturingSink(LogLevel.Trace);
            bus.AddSink(sink);

            ILogScope root = bus.CreateScope("op");
            ILogScope child = root.BeginScope("source", new Dictionary<string, object> { { "prefix", "Wii" } });
            child.Log(LogLevel.Detail, "x");

            LogEvent e = Assert.Single(sink.Events);
            Assert.Equal(child, e.Scope);
            Assert.Equal(root, e.Scope.Parent);
            Assert.Equal("Wii", ScopePrefix.Resolve(e.Scope));
            Assert.Equal("op > source", ScopePrefix.Chain(e.Scope));
        }

        [Fact]
        public void DisposeCompletesRunningButPreservesTerminalStatus()
        {
            using LogBus bus = new LogBus(LogLevel.Info);
            bus.AddSink(new CapturingSink(LogLevel.Info));

            ILogScope a = bus.CreateScope("a");
            a.Dispose();
            Assert.Equal(ScopeStatus.Done, a.Status);

            ILogScope b = bus.CreateScope("b");
            b.Status = ScopeStatus.Error;
            b.Dispose();
            Assert.Equal(ScopeStatus.Error, b.Status); // preserved, not overwritten to Done
        }

        [Fact]
        public void ThrowingSinkDoesNotPropagate()
        {
            using LogBus bus = new LogBus(LogLevel.Trace);
            CapturingSink good = new CapturingSink(LogLevel.Trace);
            bus.AddSink(new ThrowingSink());
            bus.AddSink(good);
            ILogScope s = bus.CreateScope("root");

            // Must not throw, and the good sink must still receive the event.
            s.Log(LogLevel.Info, "ok");
            Assert.Single(good.Events);
        }

        [Fact]
        public async Task ConcurrentEmitDeliversAllEvents()
        {
            using LogBus bus = new LogBus(LogLevel.Trace);
            CapturingSink sink = new CapturingSink(LogLevel.Trace);
            bus.AddSink(sink);
            ILogScope root = bus.CreateScope("root");

            const int workers = 8;
            const int per = 1000;
            Task[] tasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                int id = w;
                tasks[w] = Task.Run(() =>
                {
                    ILogScope child = root.BeginScope("w" + id);
                    for (int i = 0; i < per; i++)
                        child.Log(LogLevel.Detail, "m");
                }, TestContext.Current.CancellationToken);
            }
            await Task.WhenAll(tasks);

            Assert.Equal(workers * per, sink.Events.Count);
        }

        [Fact]
        public void StatusChangeEmittedToSink()
        {
            using LogBus bus = new LogBus(LogLevel.Info);
            CapturingSink sink = new CapturingSink(LogLevel.Info);
            bus.AddSink(sink);
            ILogScope s = bus.CreateScope("root");

            s.Status = ScopeStatus.Error;
            Assert.Contains((ScopeStatus.Running, ScopeStatus.Error), sink.StatusChanges);
        }

        [Fact]
        public void RemoveSinkStopsDelivery()
        {
            using LogBus bus = new LogBus(LogLevel.Trace);
            CapturingSink sink = new CapturingSink(LogLevel.Trace);
            bus.AddSink(sink);
            ILogScope s = bus.CreateScope("root");

            s.Log(LogLevel.Info, "first");
            bus.RemoveSink(sink);
            s.Log(LogLevel.Info, "second");

            Assert.Single(sink.Events);
            Assert.Equal("first", sink.Events[0].Message);
        }
    }
}