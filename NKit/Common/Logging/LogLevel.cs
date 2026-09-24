namespace Nanook.NKit
{
    /// <summary>
    /// Severity / verbosity of a log event, from most to least critical, plus <see cref="None"/>
    /// used as a host-level "disabled" threshold for console/file output.
    /// <para>
    /// This is the single logging level enum for the whole of NKit. It merges the former host-level
    /// config enum (which had <c>None</c>/<c>Debug</c>) with the pipeline severity enum: the legacy
    /// <c>Debug</c> name is now <see cref="Trace"/> (stored settings that still say "Debug" are
    /// normalised to Trace when read).
    /// </para>
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Logging disabled (host console/file threshold only — never emitted as an event level).</summary>
        None = 0,

        /// <summary>Unrecoverable failure — hash mismatch, I/O error, corrupt data.</summary>
        Error = 1,

        /// <summary>Unexpected but recoverable — fallback path taken, area skipped.</summary>
        Warning = 2,

        /// <summary>Key milestones — operation started/finished, area discovered, output written.</summary>
        Info = 3,

        /// <summary>Per-section / per-stage decisions — "hash matched cache", "seeking to offset X".</summary>
        Detail = 4,

        /// <summary>Noisy internals — block allocation, queue transitions, worker wake/sleep.</summary>
        Trace = 5,
    }
}