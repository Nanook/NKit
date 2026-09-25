using System;
using System.Collections.Generic;
using System.Threading;

namespace Nanook.NKit.Runtime
{
    /// <summary>
    /// Default <see cref="ILogScope"/> implementation. A node in the context tree
    /// (Application → Operation → Source → Container → Area → Section → Stage).
    /// <para>
    /// Emit is deferral-safe: every <c>Log</c> overload checks <see cref="IsEnabled"/> BEFORE
    /// invoking a message/property delegate, so nothing filtered is ever computed. A
    /// <see cref="LogEvent"/> is built (and its delegates materialised) exactly once, then handed
    /// to the bus for lock-free fan-out.
    /// </para>
    /// <para>
    /// Scopes are safe to use concurrently: a single scope may be logged from many worker threads,
    /// and children may be created concurrently. The only mutable state is <see cref="Status"/>
    /// (guarded) — properties are immutable once the scope is created.
    /// </para>
    /// </summary>
    internal sealed class LogScope : ILogScope
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyProps =
            new Dictionary<string, object>(0);

        private readonly LogBus _bus;
        private readonly object _statusLock = new object();
        private ScopeStatus _status;
        private int _disposed; // 0/1 via Interlocked

        // Cache of prefix-tagged child scopes so ScopeFor returns a stable [prefix] scope without
        // recreating one per call. Keyed by prefix tag; created lazily under _prefixLock.
        private readonly object _prefixLock = new object();
        private Dictionary<string, ILogScope> _prefixScopes;

        public LogScope(LogBus bus, ILogScope parent, string name, IReadOnlyDictionary<string, object> properties)
        {
            _bus = bus;
            Parent = parent;
            Name = name ?? string.Empty;
            Properties = properties ?? EmptyProps;
            _status = ScopeStatus.Running;
        }

        public string Name { get; }

        public ILogScope Parent { get; }

        public IReadOnlyDictionary<string, object> Properties { get; }

        public ScopeStatus Status
        {
            get => _status;
            set
            {
                ScopeStatus old;
                lock (_statusLock)
                {
                    old = _status;
                    if (old == value) return;
                    _status = value;
                }
                _bus.DispatchStatus(this, old, value);
            }
        }

        public ILogScope BeginScope(string name, IReadOnlyDictionary<string, object> properties = null) => new LogScope(_bus, this, name, properties);

        public ILogScope ScopeFor(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return this;

            lock (_prefixLock)
            {
                if (_prefixScopes == null)
                    _prefixScopes = new Dictionary<string, ILogScope>();

                ILogScope s;
                if (_prefixScopes.TryGetValue(prefix, out s))
                    return s;

                s = BeginScope(prefix,
                    new Dictionary<string, object> { { ScopePrefix.PrefixKey, prefix } });
                _prefixScopes[prefix] = s;
                return s;
            }
        }

        public bool IsEnabled(LogLevel level) => _bus.IsEnabled(level);

        // ── Eager string overloads (contract) ──────────────────────────────────

        public void Log(LogLevel level, string message)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level, message, null, null);
        }

        public void Log(LogLevel level, string message,
            IReadOnlyDictionary<string, object> properties)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level, message, properties, null);
        }

        public void Log(LogLevel level, string message, Exception exception)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level, message, null, exception);
        }

        // ── Deferred delegate overloads (P5: nothing filtered is computed) ──────

        public void Log(LogLevel level, Func<string> message)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level, message != null ? message() : null, null, null);
        }

        public void Log(LogLevel level, Func<string> message,
            Func<IReadOnlyDictionary<string, object>> properties)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level,
                message != null ? message() : null,
                properties != null ? properties() : null,
                null);
        }

        public void Log(LogLevel level, Func<string> message, Exception exception)
        {
            if (!_bus.IsEnabled(level)) return;
            Emit(level, message != null ? message() : null, null, exception);
        }

        private void Emit(LogLevel level, string message,
            IReadOnlyDictionary<string, object> properties, Exception exception)
        {
            LogEvent evt = new LogEvent(
                DateTime.UtcNow,
                level,
                message ?? string.Empty,
                properties,
                exception,
                this);
            _bus.Dispatch(evt);
        }

        public void ReportProgress(long current, long? total = null) => _bus.DispatchProgress(this, current, total);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // Only auto-complete a scope still Running; preserve an explicit terminal status
            // (Error / Skipped / Done) set by the caller earlier.
            if (_status == ScopeStatus.Running)
                Status = ScopeStatus.Done;
        }
    }
}