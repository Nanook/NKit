using System;

namespace Nanook.NKit
{
    /// <summary>
    /// Central log bus. Creates root scopes and routes events to registered sinks.
    /// <para>
    /// Typical usage: create one <see cref="ILogBus"/> at application startup, register sinks,
    /// then pass it (or root scopes created from it) into your processing pipeline.
    /// </para>
    /// </summary>
    internal interface ILogBus : IDisposable
    {
        /// <summary>
        /// The minimum level the bus will emit. Events below this level are discarded
        /// before reaching any sink, keeping the fast path cheap.
        /// </summary>
        LogLevel MinimumLevel { get; set; }

        /// <summary>Register a sink to receive events and progress updates.</summary>
        void AddSink(ILogSink sink);

        /// <summary>Remove a previously registered sink.</summary>
        void RemoveSink(ILogSink sink);

        /// <summary>
        /// Create a root scope. Typically one per application run or per top-level operation.
        /// </summary>
        ILogScope CreateScope(string name);
    }
}