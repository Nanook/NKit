using System;

namespace Nanook.NKit.Runtime
{
    /// <summary>
    /// Default <see cref="ILogBus"/> implementation.
    /// <para>
    /// Fan-out is lock-free: sinks are held in a <c>volatile</c> array swapped under a small lock
    /// only on add/remove, and iterated without locking on the hot <see cref="Dispatch"/> path.
    /// </para>
    /// <para>
    /// Filtering uses an <b>effective level</b> = the most-verbose appetite across
    /// <see cref="MinimumLevel"/> and every registered sink. It is cached as a plain int so the
    /// hot-path check (<see cref="IsEnabled"/>) is a single comparison. Because the enum orders
    /// <c>Error = 0 … Trace = 4</c>, "more verbose" means a numerically higher value, so a level is
    /// enabled when <c>(int)level &lt;= _effectiveLevel</c>.
    /// </para>
    /// </summary>
    internal sealed class LogBus : ILogBus
    {
        private readonly object _sinkLock = new object();
        private volatile ILogSink[] _sinks = new ILogSink[0];
        private LogLevel _minimumLevel;

        // Cached effective threshold (numeric LogLevel). volatile => single-comparison hot path.
        private volatile int _effectiveLevel;

        private bool _disposed;

        public LogBus()
            : this(LogLevel.Info)
        {
        }

        public LogBus(LogLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
            RecomputeEffectiveLevel();
        }

        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set
            {
                _minimumLevel = value;
                RecomputeEffectiveLevel();
            }
        }

        public void AddSink(ILogSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            lock (_sinkLock)
            {
                ILogSink[] current = _sinks;
                ILogSink[] next = new ILogSink[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = sink;
                _sinks = next;
                RecomputeEffectiveLevel();
            }
        }

        public void RemoveSink(ILogSink sink)
        {
            if (sink == null) return;
            lock (_sinkLock)
            {
                ILogSink[] current = _sinks;
                int idx = Array.IndexOf(current, sink);
                if (idx < 0) return;
                ILogSink[] next = new ILogSink[current.Length - 1];
                Array.Copy(current, 0, next, 0, idx);
                Array.Copy(current, idx + 1, next, idx, current.Length - idx - 1);
                _sinks = next;
                RecomputeEffectiveLevel();
            }
        }

        public ILogScope CreateScope(string name) => new LogScope(this, null, name, null);

        /// <summary>
        /// True when any registered sink (or the bus minimum) wants this level. Used both to filter
        /// on emit and as the caller-facing <see cref="ILogScope.IsEnabled(LogLevel)"/> gate so
        /// callers can skip expensive computation entirely.
        /// </summary>
        internal bool IsEnabled(LogLevel level) => (int)level <= _effectiveLevel;

        internal void Dispatch(LogEvent evt)
        {
            if (evt == null || (int)evt.Level > _effectiveLevel)
                return;

            ILogSink[] sinks = _sinks; // volatile read of an immutable snapshot
            for (int i = 0; i < sinks.Length; i++)
            {
                // Honour a sink's declared appetite: a levelled sink that does not want this level
                // is skipped (so a quiet console sink and a verbose file sink coexist correctly).
                ILevelledSink lvl = sinks[i] as ILevelledSink;
                if (lvl != null && (int)evt.Level > (int)lvl.MinimumLevel)
                    continue;
                try { sinks[i].Emit(evt); }
                catch { /* a throwing sink must never abort processing */ }
            }
        }

        internal void DispatchProgress(ILogScope scope, long current, long? total)
        {
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++)
            {
                try { sinks[i].EmitProgress(scope, current, total); }
                catch { }
            }
        }

        internal void DispatchStatus(ILogScope scope, ScopeStatus oldStatus, ScopeStatus newStatus)
        {
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++)
            {
                try { sinks[i].EmitStatusChange(scope, oldStatus, newStatus); }
                catch { }
            }
        }

        private void RecomputeEffectiveLevel()
        {
            // Start at the bus minimum, then widen to the most-verbose sink appetite.
            int eff = (int)_minimumLevel;
            ILogSink[] sinks = _sinks;
            for (int i = 0; i < sinks.Length; i++)
            {
                ILevelledSink lvl = sinks[i] as ILevelledSink;
                // A sink that does not declare an appetite is assumed to want everything the bus
                // minimum allows (it can filter internally); it does not widen the effective level.
                if (lvl != null)
                {
                    int want = (int)lvl.MinimumLevel;
                    if (want > eff) eff = want;
                }
            }
            _effectiveLevel = eff;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ILogSink[] sinks;
            lock (_sinkLock)
            {
                sinks = _sinks;
                _sinks = new ILogSink[0];
            }
            for (int i = 0; i < sinks.Length; i++)
            {
                try { sinks[i].Flush(); }
                catch { }
                IDisposable d = sinks[i] as IDisposable;
                if (d != null)
                {
                    try { d.Dispose(); } catch { }
                }
            }
        }
    }
}