namespace Nanook.NKit
{
    /// <summary>
    /// A public, engine-agnostic snapshot of pipeline telemetry for UI display. Populated from the
    /// NKitCore StreamBlockCore instrumentation and raised via <c>NKitProcessor.StatsEvent</c>.
    /// Stage fractions are 0..1 and (In + Pre + Proc + Out) sum to roughly 1 — they show where the
    /// wall-clock is spent, i.e. which stage is the bottleneck.
    /// </summary>
    public sealed class EngineStats
    {
        /// <summary>Instantaneous throughput in MiB/s (MB in the engine's decimal sense).</summary>
        public double ThroughputMBps { get; internal set; }

        /// <summary>Fraction of time in the read/section-create stage (0..1).</summary>
        public double InFraction { get; internal set; }

        /// <summary>Fraction of time in the producer→process handoff / pre-process stage (0..1).</summary>
        public double PreFraction { get; internal set; }

        /// <summary>Fraction of time in the parallel process stage (0..1).</summary>
        public double ProcFraction { get; internal set; }

        /// <summary>Fraction of time in the ordered output/write stage (0..1).</summary>
        public double OutFraction { get; internal set; }

        /// <summary>Worker threads currently inside Process (busy).</summary>
        public int BusyWorkers { get; internal set; }

        /// <summary>Total live worker threads in the pool.</summary>
        public int TotalWorkers { get; internal set; }
    }
}