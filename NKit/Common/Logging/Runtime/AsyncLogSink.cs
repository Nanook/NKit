using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Nanook.NKit.Runtime
{
    /// <summary>
    /// Base class for sinks that perform I/O (console, file, structured). Guarantees NON-INTRUSIVE
    /// logging: <see cref="Emit"/> only enqueues onto a bounded queue and returns; a dedicated
    /// background thread drains and does all formatting/I/O. The worker/emit path never blocks on
    /// I/O and never applies backpressure.
    /// <para>
    /// Overflow policy is DROP (never block the producer). When the bounded queue is full an event
    /// is dropped and a running dropped-count is kept; the next successfully-written event is
    /// preceded by a single "N events dropped" marker so loss is visible. This is the recorded
    /// tradeoff: possible line loss under extreme burst is accepted as the price of guaranteed
    /// non-intrusiveness. High-frequency per-section events live at Trace to bound this.
    /// </para>
    /// </summary>
    internal abstract class AsyncLogSink : ILogSink, ILevelledSink, IDisposable
    {
        private readonly BlockingCollection<LogEvent> _queue;
        private readonly Thread _drain;
        private readonly LogLevel _minimumLevel;
        private long _dropped;
        private volatile bool _completed;

        protected AsyncLogSink(LogLevel minimumLevel, int capacity = 8192)
        {
            if (capacity < 1) capacity = 1;
            _minimumLevel = minimumLevel;
            _queue = new BlockingCollection<LogEvent>(new ConcurrentQueue<LogEvent>(), capacity);
            _drain = new Thread(DrainLoop)
            {
                IsBackground = true,
                Name = GetType().Name + ".drain"
            };
            _drain.Start();
        }

        /// <summary>The most-verbose level this sink emits (drives the bus effective level).</summary>
        public LogLevel MinimumLevel => _minimumLevel;

        public void Emit(LogEvent evt)
        {
            if (evt == null || _completed) return;
            if ((int)evt.Level > (int)_minimumLevel) return; // sink-side level filter

            // Non-blocking add: drop on overflow rather than stall the producer.
            if (!_queue.TryAdd(evt))
                Interlocked.Increment(ref _dropped);
        }

        // Progress and status are cheap and are handled synchronously by default. Override if a
        // sink needs to marshal them (e.g. a UI sink). They must remain non-blocking.
        public virtual void EmitProgress(ILogScope scope, long current, long? total) { }

        public virtual void EmitStatusChange(ILogScope scope, ScopeStatus oldStatus, ScopeStatus newStatus) { }

        public void Flush()
        {
            // Signal no more items and wait for the drain thread to finish writing what is queued.
            if (_completed) return;
            _completed = true;
            try { _queue.CompleteAdding(); } catch { }
            try { _drain.Join(TimeSpan.FromSeconds(5)); } catch { }
        }

        private void DrainLoop()
        {
            try
            {
                foreach (LogEvent evt in _queue.GetConsumingEnumerable())
                {
                    long dropped = Interlocked.Exchange(ref _dropped, 0);
                    if (dropped > 0)
                    {
                        try { WriteDroppedMarker(dropped); } catch { }
                    }
                    try { Write(evt); }
                    catch { /* a write failure must never crash the drain thread */ }
                }
            }
            catch { /* CompleteAdding races etc. — never propagate from the background thread */ }
        }

        /// <summary>Write one event (formatting + I/O). Runs on the drain thread only.</summary>
        protected abstract void Write(LogEvent evt);

        /// <summary>
        /// Emit a marker noting that <paramref name="count"/> events were dropped under load.
        /// Default is a no-op; override to surface it in the sink's output.
        /// </summary>
        protected virtual void WriteDroppedMarker(long count) { }

        public virtual void Dispose()
        {
            Flush();
            _queue.Dispose();
        }
    }
}