using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// A single structured log event. Created by <see cref="ILogScope"/> methods,
    /// routed to <see cref="ILogSink"/> implementations by the <see cref="ILogBus"/>.
    /// Sinks decide how to format/filter — producers never build display strings.
    /// </summary>
    internal sealed class LogEvent
    {
        /// <summary>When the event occurred (UTC).</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Severity.</summary>
        public LogLevel Level { get; }

        /// <summary>Human-readable message. Sinks may prepend scope context when rendering.</summary>
        public string Message { get; }

        /// <summary>
        /// Optional structured properties attached to this specific event (not inherited from scope).
        /// May be null if no properties were supplied.
        /// </summary>
        public IReadOnlyDictionary<string, object> Properties { get; }

        /// <summary>Optional exception associated with this event.</summary>
        public Exception Exception { get; }

        /// <summary>The scope that emitted this event. Walk <see cref="ILogScope.Parent"/> for the full chain.</summary>
        public ILogScope Scope { get; }

        public LogEvent(
            DateTime timestampUtc,
            LogLevel level,
            string message,
            IReadOnlyDictionary<string, object> properties,
            Exception exception,
            ILogScope scope)
        {
            TimestampUtc = timestampUtc;
            Level = level;
            Message = message;
            Properties = properties;
            Exception = exception;
            Scope = scope;
        }
    }
}