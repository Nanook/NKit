namespace Nanook.NKit.Engine.Core
{
    /// <summary>
    /// Static tuning values for the NKitCore pipeline and the shared BufferStream cache.
    ///
    /// These replace the former NKIT_CORE_* environment variables — every value is now a
    /// compile-time constant with no runtime override. Adjust here to retune; there is no
    /// external switch.
    /// </summary>
    internal static class NKitCoreConsts
    {
        // ── Autoscaler / pipeline (NKitCoreRunner) ──────────────────────────────

        /// <summary>Lowest worker count the autoscaler may drop to.</summary>
        public const int MinWorkers = 1;

        /// <summary>
        /// Hard SAFETY ceiling for the autoscaler — a bound, NOT a target. Section processing is
        /// often I/O- or decode-latency-bound (workers spend time waiting, not saturating a core).
        /// The autoscaler's throughput brake is what actually stops the climb; this only caps a
        /// pathological runaway.
        /// <para>
        /// Previously this was ProcessorCount*2, which let the pool climb to ~18-26 workers on a
        /// typical box even though real workloads (e.g. a decode-bound Wii verify) only ever keep
        /// 2-3 busy. Each worker is a thread-pool Task, so the pool inflated to ~87 threads (+~1MB
        /// stack each) and ~1500 OS handles that the pool retires only slowly — a high, slow-to-
        /// release memory/handle plateau in a long-lived host (the UI). Capping at the logical-
        /// processor count keeps ample headroom (the autoscaler settles far below it here) while
        /// roughly halving the thread/handle plateau. Floor of 4 so small boxes still parallelise.
        /// </para>
        /// </summary>
        public static int MaxWorkers => System.Math.Max(4, System.Environment.ProcessorCount);

        /// <summary>
        /// Look-ahead depth: how many sections may be produced ahead of the processing workers.
        /// With the worker window this sizes the section pool free-list.
        /// </summary>
        public const int LookAhead = 8;

        /// <summary>Autoscaler throughput sample interval (ms).</summary>
        public const int SampleIntervalMs = 500;

        /// <summary>How long a worker must sit idle before the autoscaler retires it (ms).</summary>
        public const int IdleWorkerMinMs = 2000;

        /// <summary>Telemetry / progress-footer refresh interval (ms).</summary>
        public const int StatsIntervalMs = 500;

        /// <summary>Extra slack added to the pool free-list beyond the live-section window.</summary>
        public const int PoolFreeMargin = 2;

        // Note: MaxWorkers defaults to Environment.ProcessorCount and StartWorkers to the step's
        // parallelism — both are runtime values computed in NKitCoreRunner, not constants.

        // ── Diagnostics (NKitCoreRunner stats sink) ─────────────────────────────
        // Off by default. Set a path to append the per-interval core stats to a file, and/or
        // enable the console echo, for performance debugging. No env var involved.

        // static readonly (not const) so flipping these for a debug session does not trip
        // "unreachable code" warnings at the (compile-time-folded) use sites.

        /// <summary>When non-null, per-interval core stats are appended to this file.</summary>
        public static readonly string StatsFile = null;

        /// <summary>When true, per-interval core stats are also echoed to the console.</summary>
        public static readonly bool StatsConsole = false;

        // ── BufferStream cache (BufferStreamManager) ────────────────────────────

        /// <summary>
        /// Section-size buffer: the block size the forward-only cache reads/retains in one unit.
        /// </summary>
        public const int BufferBlockSize = 10 * 1024 * 1024; // 10 MiB

        /// <summary>
        /// A cache fill (a large read or a forward seek) that pulls at least this many bytes from
        /// the forward-only source emits the "Caching NNNMiB..." log line so the pause is visibly
        /// a cache fill, not a hang.
        /// </summary>
        public const long CacheLogThreshold = 100L * 1024 * 1024; // 100 MiB

        /// <summary>
        /// A view <c>Seek</c> is only logged when it is NOTABLE: a backward seek (any size) or a
        /// forward jump of at least this many bytes (a real reposition). Smaller forward seeks are
        /// the per-read padding/alignment steps that happen constantly and are NOT logged at all —
        /// they flooded the output. A zero-distance seek is never logged.
        /// </summary>
        public const long SeekLogDetailThreshold = 64 * 1024; // 64 KiB
    }
}