using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Shorthand methods so callers don't have to spell out <see cref="LogLevel"/> on every call.
    /// Classification is by SCOPE (the bracketed source tag) and LEVEL only — there is no category
    /// axis. Use <see cref="ILogScope.ScopeFor(string)"/> to obtain a prefix-tagged stage/phase
    /// scope (e.g. a "Core" scope renders "[Core]").
    /// </summary>
    public static class LogScopeExtensions
    {
        // ── Level shortcuts (eager string) ─────────────────────────

        public static void Error(this ILogScope scope, string message)
            => scope.Log(LogLevel.Error, message);

        public static void Error(this ILogScope scope, string message, Exception ex)
            => scope.Log(LogLevel.Error, message, ex);

        public static void Warn(this ILogScope scope, string message)
            => scope.Log(LogLevel.Warning, message);

        public static void Info(this ILogScope scope, string message)
            => scope.Log(LogLevel.Info, message);

        public static void Detail(this ILogScope scope, string message)
            => scope.Log(LogLevel.Detail, message);

        public static void Trace(this ILogScope scope, string message)
            => scope.Log(LogLevel.Trace, message);

        // ── Structured property shortcuts ──────────────────────────

        public static void Info(this ILogScope scope, string message,
            IReadOnlyDictionary<string, object> properties)
            => scope.Log(LogLevel.Info, message, properties);

        public static void Detail(this ILogScope scope, string message,
            IReadOnlyDictionary<string, object> properties)
            => scope.Log(LogLevel.Detail, message, properties);

        // ── Deferred (Func<string>) level shortcuts ────────────────
        // Prefer these whenever the message interpolates or computes — the delegate is invoked only
        // when the level is enabled. Use the plain-string overloads above only for literals.

        public static void Error(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Error, message);

        public static void Error(this ILogScope scope, Func<string> message, Exception ex)
            => scope.Log(LogLevel.Error, message, ex);

        public static void Warn(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Warning, message);

        public static void Info(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Info, message);

        public static void Detail(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Detail, message);

        public static void Detail(this ILogScope scope, Func<string> message,
            Func<IReadOnlyDictionary<string, object>> properties)
            => scope.Log(LogLevel.Detail, message, properties);

        public static void Trace(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Trace, message);

        // ── Info summary prefixes (see InfoPrefix) ─────────────────
        // Emit an Info line stamped with a machine-readable leading prefix so a host console can
        // classify/colour it. The prefix is literal leading TEXT in the message, not a scope; the
        // delegate is still only invoked when Info is enabled.

        /// <summary>Info line stamped with <see cref="InfoPrefix.Title"/>.</summary>
        public static void InfoTitle(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Info, () => InfoPrefix.Stamp(InfoPrefix.Title, message()));

        /// <summary>Info line stamped with <see cref="InfoPrefix.InParam"/>.</summary>
        public static void InfoInParam(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Info, () => InfoPrefix.Stamp(InfoPrefix.InParam, message()));

        /// <summary>Info line stamped with <see cref="InfoPrefix.OutParam"/>.</summary>
        public static void InfoOutParam(this ILogScope scope, Func<string> message)
            => scope.Log(LogLevel.Info, () => InfoPrefix.Stamp(InfoPrefix.OutParam, message()));
    }
}