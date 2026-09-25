namespace Nanook.NKit
{
    /// <summary>
    /// Optional sink capability: declares the most-verbose <see cref="LogLevel"/> the sink wants.
    /// The <see cref="ILogBus"/> uses this to compute an <b>effective level</b> across all sinks so
    /// the hot-path <see cref="ILogScope.IsEnabled(LogLevel)"/> gate reflects the most verbose
    /// consumer — detail wanted only by a file sink is neither computed-and-discarded nor silently
    /// dropped.
    /// <para>
    /// A sink that does not implement this is treated as wanting whatever the bus minimum allows
    /// (it may still filter internally). Implementing it lets a quiet console sink coexist with a
    /// verbose file sink without either losing events.
    /// </para>
    /// </summary>
    internal interface ILevelledSink
    {
        /// <summary>The most-verbose level this sink will actually emit.</summary>
        LogLevel MinimumLevel { get; }
    }
}