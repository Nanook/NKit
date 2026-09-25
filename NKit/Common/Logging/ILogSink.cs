namespace Nanook.NKit
{
    /// <summary>
    /// Receives log events and progress updates. Implementations decide how to render,
    /// filter, and store them (console, file, UI, etc.).
    /// <para>
    /// Sinks must be thread-safe — events may arrive from multiple workers concurrently.
    /// </para>
    /// </summary>
    internal interface ILogSink
    {
        /// <summary>
        /// Called for every log event that passes the bus-level filter.
        /// </summary>
        void Emit(LogEvent evt);

        /// <summary>
        /// Called when a scope reports progress. Sinks that don't care about progress
        /// can leave this as a no-op.
        /// </summary>
        void EmitProgress(ILogScope scope, long current, long? total);

        /// <summary>
        /// Called when a scope's <see cref="ILogScope.Status"/> changes.
        /// Useful for UI sinks that render a task tree.
        /// </summary>
        void EmitStatusChange(ILogScope scope, ScopeStatus oldStatus, ScopeStatus newStatus);

        /// <summary>
        /// Called once when the bus is being disposed, giving the sink a chance to flush
        /// any buffered output.
        /// </summary>
        void Flush();
    }
}