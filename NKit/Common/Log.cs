using Nanook.NKit.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{


    public class Log : IDisposable, ILogScope
    {
        public LogLevel ConsoleLevel { get; private set; }
        public LogLevel FileLevel { get; private set; }

        // ── NKitLog bus (additive migration) ───────────────────────────────────────────────
        // The legacy console/file behaviour below is UNCHANGED (Info stays exactly as the user
        // sees it). In addition, each Write is mirrored into an NKitLog bus so new ILogScope
        // consumers (Detail events threaded through the pipeline) and structured sinks can observe
        // events. The bus's own FileLogSink is what captures the new Detail lines to file; the
        // legacy _fileLog remains for byte-identical back-compat of existing output.
        private LogBus _bus;
        private ILogScope _rootScope;

        /// <summary>
        /// Root NKitLog scope for this run. New code threads child scopes from here (Operation →
        /// Source → Area …) and logs Detail events against them. Null until <see cref="Initialise"/>.
        /// </summary>
        public ILogScope Scope => _rootScope;

        /// <summary>The NKitLog bus backing this Log (for host sink registration). Null until init.</summary>
        internal ILogBus Bus => _bus;

        // ── ILogScope implementation (forwards to the root scope) ───────────────────────────
        // Log is the host-owned logging object; it also acts as the root ILogScope so contexts can
        // expose ILogScope instead of the concrete Log type (single logging interface). All members
        // forward to _rootScope and are null-safe before Initialise (no-op / defaults), matching the
        // old behaviour where logging before init was silently dropped. LogLevel/ScopeStatus are now
        // the single Nanook.NKit types (no legacy/new split), so no qualification is needed.
        string ILogScope.Name => _rootScope?.Name ?? "NKit";
        ILogScope ILogScope.Parent => _rootScope?.Parent;
        ScopeStatus ILogScope.Status
        {
            get => _rootScope?.Status ?? ScopeStatus.Running;
            set { if (_rootScope != null) _rootScope.Status = value; }
        }
        System.Collections.Generic.IReadOnlyDictionary<string, object> ILogScope.Properties
            => _rootScope?.Properties;

        ILogScope ILogScope.BeginScope(string name, System.Collections.Generic.IReadOnlyDictionary<string, object> properties)
            => _rootScope?.BeginScope(name, properties);

        bool ILogScope.IsEnabled(LogLevel level)
            => _rootScope?.IsEnabled(level) ?? false;

        void ILogScope.Log(LogLevel level, string message)
            => _rootScope?.Log(level, message);
        void ILogScope.Log(LogLevel level, string message, System.Collections.Generic.IReadOnlyDictionary<string, object> properties)
            => _rootScope?.Log(level, message, properties);
        void ILogScope.Log(LogLevel level, string message, Exception exception)
            => _rootScope?.Log(level, message, exception);
        void ILogScope.Log(LogLevel level, Func<string> message)
            => _rootScope?.Log(level, message);
        void ILogScope.Log(LogLevel level, Func<string> message, Func<System.Collections.Generic.IReadOnlyDictionary<string, object>> properties)
            => _rootScope?.Log(level, message, properties);
        void ILogScope.Log(LogLevel level, Func<string> message, Exception exception)
            => _rootScope?.Log(level, message, exception);

        void ILogScope.ReportProgress(long current, long? total)
            => _rootScope?.ReportProgress(current, total);

        // Cache of prefix-tagged child scopes so callers get a stable [prefix] scope without
        // recreating one per call. Keyed by prefix tag.
        private readonly Dictionary<string, ILogScope> _prefixScopes = new Dictionary<string, ILogScope>();

        /// <summary>
        /// A child scope carrying a source <c>[prefix]</c> tag (e.g. "Task", "Input", "Wii"). Sinks
        /// render the tag from the scope. Returns null before <see cref="Initialise"/>. Cached per
        /// prefix so repeated calls reuse the same scope.
        /// </summary>
        public ILogScope ScopeFor(string prefix)
        {
            if (_rootScope == null) return null;
            lock (_lock)
            {
                ILogScope s;
                if (_prefixScopes.TryGetValue(prefix, out s))
                    return s;
                s = _rootScope.BeginScope(prefix,
                    new Dictionary<string, object> { { ScopePrefix.PrefixKey, prefix } });
                _prefixScopes[prefix] = s;
                return s;
            }
        }

        public bool IsProcessing { get; private set; }

        // Live overall step progress (0..100), updated from Progress(). Sinks read this to prefix
        // every message with the current [NN%] so the many Detail lines each carry progress. Null
        // when not processing (no percent shown). Volatile int for lock-free cross-thread reads.
        private volatile int _currentPercent = -1;

        /// <summary>Current step progress as a percent (0..100), or null when no step is running.</summary>
        public int? CurrentPercent
        {
            get { int p = _currentPercent; return p < 0 ? (int?)null : p; }
        }

        private Queue<Tuple<string, LogLevel>> _cache;

        private Action<string, LogLevel> _consoleLog;
        private bool _disposedValue;
        private object _lock;
        private bool _processing;

        // The FirstChanceException diagnostic handler is subscribed to the AppDomain (a process-wide,
        // long-lived event) at most ONCE. Initialise runs per image on a reused Log, so subscribing
        // unconditionally leaked a new closure per image (and kept this Log rooted). Track the handler
        // so we subscribe once and unsubscribe on Dispose.
        private EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> _firstChanceHandler;

        // Snapshot of the config the current bus was built for, so a repeated Initialise with the
        // SAME settings reuses the existing bus instead of tearing it down and rebuilding a fresh
        // FileLogSink (background thread + BlockingCollection wait handles) every image. Rebuilding
        // per image leaked those OS handles/threads to finalization and grew unbounded over a large
        // multi-image run.
        private bool _busBuilt;
        private LogLevel _busConsoleLevel;
        private LogLevel _busFileLevel;
        private string _busFilePath;
        private Action<string, LogLevel> _busConsoleCallback;

        /// <summary>
        /// When true the console sink owns its own live/in-place progress rendering (the dynamic
        /// Spectre console), so this Log does NOT buffer messages during a step and does NOT emit
        /// the legacy ".1.2.3" progress dots — messages are written through immediately and the
        /// sink interleaves them with its footer. When false (redirected / piped / file), the
        /// legacy behaviour applies: cache messages while a progress line owns the console and emit
        /// the dot stream. The file log is unaffected either way.
        /// </summary>
        public bool Dynamic { get; private set; }

        public bool FileWriteError { get; private set; }

        public const string Section = "========================================";
        public const string Divider = "----------------------------------------";

        public Log()
        {
            _lock = new object();
            _processing = false;
            ConsoleLevel = LogLevel.None;
            FileLevel = LogLevel.None;
            _cache = new Queue<Tuple<string, LogLevel>>();
            FileWriteError = false;
        }

        // An event at <paramref name="level"/> is shown when the threshold <paramref name="test"/> is
        // at least as verbose. Higher enum value = more verbose (None=0 … Trace=5), so the event
        // passes when it is at or below the threshold. None as the threshold disables everything.
        private bool canLog(LogLevel level, LogLevel test)
        {
            if (test == LogLevel.None || level == LogLevel.None)
                return false;
            return (int)level <= (int)test;
        }

        public void Initialise(LogLevel consoleLevel, LogLevel fileLevel, Action<string, LogLevel> consoleLog, string fileLog, bool dynamic = false)
        {
            this.Dynamic = dynamic;
            lock (_lock)
            {
                this.ConsoleLevel = consoleLevel;
                this.FileLevel = fileLevel;

                // Subscribe the first-chance diagnostic ONCE (the AppDomain event is process-wide and
                // Initialise is called per image on a reused Log — an unconditional += leaked a
                // closure every image). If Debug is no longer enabled on a later Initialise, drop it.
                bool wantFirstChance = this.ConsoleLevel == LogLevel.Trace || this.FileLevel == LogLevel.Trace;
                if (wantFirstChance && _firstChanceHandler == null)
                {
                    _firstChanceHandler = (s, ea) =>
                    {
                        // Cooperative cancellation propagates VIA exceptions in .NET
                        // (OperationCanceledException / TaskCanceledException from awaited tokens and
                        // SemaphoreSlim.WaitAsync). They fire on every normal end-of-run pool
                        // shutdown and are always caught — pure noise, so the first-chance
                        // diagnostic skips them. Genuine first-chance exceptions are still logged.
                        if (ea.Exception is OperationCanceledException)
                            return;
                        this.Write(LogLevel.Trace, () => $"Exception (Could be handled): {ea.Exception}");
                    };
                    AppDomain.CurrentDomain.FirstChanceException += _firstChanceHandler;
                }
                else if (!wantFirstChance && _firstChanceHandler != null)
                {
                    AppDomain.CurrentDomain.FirstChanceException -= _firstChanceHandler;
                    _firstChanceHandler = null;
                }

                _consoleLog = consoleLog;

                // Validate the log path up front (preserves the legacy directory-vs-file guard),
                // then let the bus's FileLogSink own the actual file writing (single writer).
                if (!string.IsNullOrEmpty(fileLog) && fileLevel != LogLevel.None)
                {
                    if (Directory.Exists(fileLog))
                        throw new HandledException("LogOut is a directory and not a file");
                }

                initialiseBus(fileLevel, fileLog);

                // Surface a file-open failure the same way the legacy path did (FileWriteError +
                // a console notice), now sourced from the bus FileLogSink.
                if (_busFileSink != null && _busFileSink.FileWriteError)
                {
                    FileWriteError = true;
                    try { _consoleLog?.Invoke($"Failed to open log file '{fileLog}'{Environment.NewLine}", LogLevel.Error); } catch { }
                }
            }
        }

        private FileLogSink _busFileSink;
        private ConsoleCallbackSink _busConsoleSink;

        // Build (or rebuild) the NKitLog bus and its sinks. This is now the CENTRAL routing hub:
        // both the console (via ConsoleCallbackSink wrapping the host callback) and the file (via
        // FileLogSink) are bus sinks, each carrying its own level so the bus routes per-sink. The
        // bus minimum is the more-verbose of the two levels so neither sink is starved. Never throws
        // — logging must not break processing.
        private void initialiseBus(LogLevel fileLevel, string fileLog)
        {
            try
            {
                // REUSE the existing bus when nothing that affects it has changed. Initialise runs
                // per image on a Log reused for the whole app run; rebuilding here every time tore
                // down and recreated the async FileLogSink (background drain thread + BlockingCollection
                // wait handles) per image, leaking those OS handles/threads to finalization and growing
                // unbounded over a large multi-image run. When the console level, file level, file path
                // and console callback are unchanged, the current bus is already correct — keep it.
                if (_busBuilt
                    && _busConsoleLevel == this.ConsoleLevel
                    && _busFileLevel == this.FileLevel
                    && string.Equals(_busFilePath ?? "", fileLog ?? "", System.StringComparison.Ordinal)
                    && ReferenceEquals(_busConsoleCallback, _consoleLog))
                {
                    return; // bus already matches the requested config
                }

                if (_bus != null)
                {
                    try { _bus.Dispose(); } catch { }
                    _bus = null;
                    _rootScope = null;
                    _busFileSink = null;
                    _busConsoleSink = null;
                }
                _busBuilt = false;

                if (this.ConsoleLevel == LogLevel.None && this.FileLevel == LogLevel.None)
                    return; // default/disabled state — no bus

                LogLevel min = maxVerbosity(this.ConsoleLevel, this.FileLevel);
                _bus = new LogBus(min);

                // Console sink: wraps the host callback (CLI LiveConsole / UI append). Carries the
                // console level so the bus routes console-worthy events to it only.
                _busConsoleSink = null;
                if (_consoleLog != null && this.ConsoleLevel != LogLevel.None)
                {
                    _busConsoleSink = new ConsoleCallbackSink(_consoleLog, this.ConsoleLevel, () => this.CurrentPercent);
                    _bus.AddSink(_busConsoleSink);
                }

                // File sink: the single append-only file writer. Carries the file level.
                if (!string.IsNullOrEmpty(fileLog) && fileLevel != LogLevel.None)
                {
                    _busFileSink = new FileLogSink(fileLog, fileLevel);
                    _bus.AddSink(_busFileSink);
                }

                _rootScope = _bus.CreateScope("NKit");

                // Record what this bus was built for so a later Initialise with identical settings
                // reuses it (see the guard at the top) instead of churning a new async sink.
                _busBuilt = true;
                _busConsoleLevel = this.ConsoleLevel;
                _busFileLevel = this.FileLevel;
                _busFilePath = fileLog;
                _busConsoleCallback = _consoleLog;
                _prefixScopes.Clear();
            }
            catch
            {
                _bus = null;
                _rootScope = null;
                _busFileSink = null;
                _busConsoleSink = null;
                _busBuilt = false;
            }
        }

        // Which of two levels is the more verbose. The enum is ordered least→most verbose
        // (None=0 … Trace=5), so the higher numeric value wins.
        private static LogLevel maxVerbosity(LogLevel a, LogLevel b) => (int)a >= (int)b ? a : b;
        public void Write(LogLevel level, Func<string> message)
        {
            if (this.ConsoleLevel == LogLevel.None && this.FileLevel == LogLevel.None) //default state
                return;

            // CENTRALISED ROUTING: everything now flows through the NKitLog bus. The bus fans out to
            // the ConsoleCallbackSink (console level) and FileLogSink (file level), each honouring
            // its own level — so the independent console/file thresholds are preserved, centrally.
            if (_rootScope == null)
                return; // bus unavailable (shouldn't happen once Initialised) — drop silently

            // Non-dynamic console buffering: while a legacy dot-progress line owns the console, hold
            // console-bound messages so they don't corrupt the dot line. This is a CONSOLE-only
            // concern; the file sink is written immediately. We therefore split routing when buffering
            // is active: file/other sinks now, console after the dot line completes (flushCache).
            if (_processing && !this.Dynamic)
            {
                string msg = message();
                lock (_lock)
                    _cache.Enqueue(new Tuple<string, LogLevel>(msg, level));
                // Still write to the file sink immediately (buffering is only to protect the console
                // dot line).
                if (_busFileSink != null && canLog(level, this.FileLevel))
                    try { _busFileSink.Emit(BuildEvent(level, msg)); } catch { }
                return;
            }

            // Normal path: one bus emit routes to every sink at/under its level. Classification is
            // scope + level only — legacy callers have no stage scope, so they log on the root scope
            // (rendered without a tag). New code that wants a tag calls ScopeFor(...).Log(...) directly.
            try { _rootScope.Log(level, message); }
            catch { }
        }

        // Build a LogEvent equivalent to what the bus would produce, for the buffered-console path
        // where we write to the file sink directly (bypassing the console).
        private LogEvent BuildEvent(LogLevel level, string msg)
        {
            return new LogEvent(DateTime.UtcNow, level,
                msg, null, null, _rootScope);
        }

        private string preProgress(int step, string[] steps)
        {
            if (steps.Length == 1)
                return $"{steps[0]} : ";
            else
            {
                int padding = steps.Max(a => a.Length) - steps[step].Length + 1;
                return $"{steps[step]}{new string(' ', padding)}[{step + 1}/{steps.Length}] : ";
            }
        }

        public void Progress(int step, string[] steps, float pcnt, float lastPcnt, string stepCompleteMessage)
        {
            // Track live overall progress so every subsequent message can be prefixed with [NN%].
            // Clamp to 0..100; leave it set at 100 briefly on completion (cleared by the next step's
            // pcnt==0, or on dispose).
            _currentPercent = pcnt < 0f ? 0 : (pcnt > 1f ? 100 : (int)(pcnt * 100f));

            // Dynamic console: the sink renders its own in-place progress footer from ProgressEvent,
            // so emit NO dot stream here and never enter the buffered _processing state. (The file
            // log below still records the one-line progress summary at 100%.)
            if (!this.Dynamic && canLog(LogLevel.Info, this.ConsoleLevel))
            {
                if (pcnt == 0)
                {
                    _processing = true;
                    _consoleLog(preProgress(step, steps), LogLevel.Info);
                }

                int prg = (int)(pcnt * 20F);

                for (int i = (int)(lastPcnt * 20F) + 1; i <= prg; i++)
                    _consoleLog(i % 2 == 1 ? "." : (i / 2).ToString(), LogLevel.Info);

                if (pcnt == 1f)
                {
                    _consoleLog(string.Concat(" ", stepCompleteMessage, Environment.NewLine), LogLevel.Info);
                    _processing = false;
                    flushCache();
                }
            }
            // Progress is an INFO-level channel. At 100% write the one-line completion summary to the
            // FILE sink only (never the console footer, never Detail). Route it straight to the file
            // sink so it lands in the log without going through the console.
            if (pcnt == 1f && _busFileSink != null && canLog(LogLevel.Info, this.FileLevel))
            {
                try
                {
                    _busFileSink.Emit(new LogEvent(DateTime.UtcNow, LogLevel.Info,
                        $"{preProgress(step, steps)}.1.2.3.4.5.6.7.8.9.10 {stepCompleteMessage}",
                        null, null, ScopeFor("Results") ?? _rootScope));
                }
                catch { FileWriteError = true; }
            }
        }

        private void flushCache()
        {
            // The dot-progress line has finished; release buffered messages to the CONSOLE now. They
            // were already written to the file sink at buffer time, so route to the console sink only
            // (avoid a duplicate file write).
            lock (_lock)
            {
                while (_cache.Count != 0)
                {
                    Tuple<string, LogLevel> c = _cache.Dequeue();
                    if (_busConsoleSink != null && canLog(c.Item2, this.ConsoleLevel))
                        try { _busConsoleSink.Emit(BuildEvent(c.Item2, c.Item1)); } catch { }
                }
            }
        }

        // Category-free convenience overloads (classification is scope + level now). These are the
        // ones concrete-Log holders call after the A2 migration; they route through Write with a
        // neutral category (which scopeForCategory maps to the root scope = no extra tag).
        public void Info(Func<string> message) => this.Write(LogLevel.Info, message);
        public void Warn(Func<string> message) => this.Write(LogLevel.Info, message);
        public void Detail(Func<string> message) => this.Write(LogLevel.Detail, message);
        public void Error(Func<string> message) => this.Write(LogLevel.Error, message);
        public void Trace(Func<string> message) => this.Write(LogLevel.Trace, message);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _consoleLog = null;

                    // Unsubscribe the process-wide first-chance handler so this Log is not rooted by
                    // the AppDomain event after disposal.
                    if (_firstChanceHandler != null)
                    {
                        try { AppDomain.CurrentDomain.FirstChanceException -= _firstChanceHandler; } catch { }
                        _firstChanceHandler = null;
                    }

                    // Flush + dispose the NKitLog bus so its async FileLogSink drains cleanly.
                    try { _bus?.Dispose(); } catch { }
                    _bus = null;
                    _rootScope = null;
                    _busBuilt = false;
                    _busConsoleCallback = null;
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}