using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// A hierarchical logging scope. Each scope carries context (name + properties) that is
    /// automatically inherited by all events and child scopes created within it.
    /// <para>
    /// Scopes form a tree: Application → Operation → Source → Container → Area → Section → Stage.
    /// Disposing a scope marks it complete and optionally updates its <see cref="Status"/>.
    /// </para>
    /// </summary>
    public interface ILogScope : IDisposable
    {
        /// <summary>Display name of this scope (e.g. "Area 3", "Process", "game.iso").</summary>
        string Name { get; }

        /// <summary>Parent scope, or null for the root.</summary>
        ILogScope Parent { get; }

        /// <summary>Current status of this scope node.</summary>
        ScopeStatus Status { get; set; }

        /// <summary>
        /// Properties set on this scope (not inherited from parent).
        /// Used by sinks for structured output and filtering.
        /// </summary>
        IReadOnlyDictionary<string, object> Properties { get; }

        // ── Child scopes ───────────────────────────────────────────

        /// <summary>Create a named child scope with optional properties.</summary>
        ILogScope BeginScope(string name, IReadOnlyDictionary<string, object> properties = null);

        /// <summary>
        /// Resolve a prefix-tagged child scope (rendered as e.g. <c>[In]</c>/<c>[Config]</c>/<c>[Out]</c>).
        /// This is the primary way pipeline/step/format code obtains a stage-tagged scope to log
        /// against. Implementations cache one child scope per prefix.
        /// </summary>
        ILogScope ScopeFor(string prefix);

        // ── Filtering ──────────────────────────────────────────────

        /// <summary>
        /// True when any enabled sink wants this level. Use as a gate around genuinely expensive
        /// computation so nothing that will be filtered out is ever computed (zero closure
        /// allocation when disabled):
        /// <code>
        /// if (scope.IsEnabled(LogLevel.Detail)) { var s = Expensive(); scope.Detail(() => s); }
        /// </code>
        /// </summary>
        bool IsEnabled(LogLevel level);

        // ── Logging (eager string) ─────────────────────────────────

        /// <summary>Log a message at the given level.</summary>
        void Log(LogLevel level, string message);

        /// <summary>Log a message with structured properties.</summary>
        void Log(LogLevel level, string message,
            IReadOnlyDictionary<string, object> properties);

        /// <summary>Log an exception with a message.</summary>
        void Log(LogLevel level, string message, Exception exception);

        // ── Logging (deferred delegates) ───────────────────────────
        // The message / properties delegates are invoked ONLY after the level check passes, so a
        // filtered event builds no string and allocates no dictionary.

        /// <summary>Log a lazily-built message; the delegate runs only if the level is enabled.</summary>
        void Log(LogLevel level, Func<string> message);

        /// <summary>Log a lazily-built message and lazily-built properties.</summary>
        void Log(LogLevel level, Func<string> message,
            Func<IReadOnlyDictionary<string, object>> properties);

        /// <summary>Log a lazily-built message with an exception.</summary>
        void Log(LogLevel level, Func<string> message, Exception exception);

        // ── Progress ───────────────────────────────────────────────

        /// <summary>
        /// Report progress for this scope. <paramref name="total"/> may be null if unknown.
        /// Progress is a separate data stream from log events — sinks handle it independently.
        /// </summary>
        void ReportProgress(long current, long? total = null);
    }
}