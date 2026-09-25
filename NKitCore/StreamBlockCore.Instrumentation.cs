using System;
using System.Collections.Generic;
using System.Threading;

namespace NKitCore
{
    public sealed partial class StreamBlockCore<TSection, TContext>
        where TSection : class, ISection
        where TContext : class, ISectionContext
    {
        // Sliding-window instrumentation. Records per-stage busy time (ticks) into time buckets so a
        // recent-window stage breakdown and throughput can be reported. Bytes are counted ONCE, at
        // the Process stage, so Total MB / MB/s reflect the real image size (not double-counted).
        private sealed class Instrumentation
        {
            private readonly int _bucketCount;
            private readonly long _bucketMs;
            private readonly long[] _bucketTimestampsMs;
            private readonly Dictionary<Stage, long[]> _stageBuckets;
            private readonly long[] _processBytesBuckets;
            private readonly object _lock = new object();
            private long _totalProcessedBytes;
            private readonly int _windowSeconds;

            public Instrumentation(int windowSeconds = 5, int bucketMs = 500)
            {
                _windowSeconds = Math.Max(1, windowSeconds);
                _bucketMs = Math.Max(50, bucketMs);
                _bucketCount = Math.Max(1, (int)(_windowSeconds * 1000L / _bucketMs));
                _bucketTimestampsMs = new long[_bucketCount];
                _processBytesBuckets = new long[_bucketCount];
                _stageBuckets = new Dictionary<Stage, long[]>();
                foreach (Stage s in _AllStages) _stageBuckets[s] = new long[_bucketCount];
            }

            public void Record(Stage stage, int lengthBytes, TimeSpan duration)
            {
                if (stage == Stage.Process && lengthBytes > 0)
                    Interlocked.Add(ref _totalProcessedBytes, lengthBytes);

                long nowMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                int idx = (int)(nowMs / _bucketMs % _bucketCount);
                long start = nowMs / _bucketMs * _bucketMs;
                lock (_lock)
                {
                    if (_bucketTimestampsMs[idx] != start)
                    {
                        foreach (Stage sk in _stageBuckets.Keys) _stageBuckets[sk][idx] = 0L;
                        _processBytesBuckets[idx] = 0L;
                        _bucketTimestampsMs[idx] = start;
                    }
                    _stageBuckets[stage][idx] += duration.Ticks;
                    if (stage == Stage.Process && lengthBytes > 0)
                        _processBytesBuckets[idx] += lengthBytes;
                }
            }

            public (IReadOnlyDictionary<Stage, double> fractions, double thruMBps, double totalMB) Snapshot()
            {
                Dictionary<Stage, long> sums = new Dictionary<Stage, long>();
                long totalTicks = 0;
                long windowBytes = 0;
                lock (_lock)
                {
                    foreach (Stage s in _AllStages)
                    {
                        long sum = 0;
                        long[] arr = _stageBuckets[s];
                        for (int b = 0; b < _bucketCount; b++) sum += arr[b];
                        sums[s] = sum;
                        totalTicks += sum;
                    }
                    for (int b = 0; b < _bucketCount; b++) windowBytes += _processBytesBuckets[b];
                }

                Dictionary<Stage, double> frac = new Dictionary<Stage, double>();
                if (totalTicks > 0)
                    foreach (Stage s in _AllStages) frac[s] = (double)sums[s] / totalTicks;
                else
                    foreach (Stage s in _AllStages) frac[s] = 0.0;

                double thru = (double)windowBytes / (1024.0 * 1024.0) / _windowSeconds;
                double totalMB = (double)Interlocked.Read(ref _totalProcessedBytes) / (1024.0 * 1024.0);
                return (frac, thru, totalMB);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // Built-in progress/telemetry line formatter (DISABLED — baked in for a future engine
        // refactor that will surface progress logging directly from the core).
        //
        // Today the NKit test runner (NKitCoreRunner.TraceStats) formats this exact line from the
        // metricsSink snapshot. The intent of the refactor is to move that responsibility INTO the
        // core so any host gets consistent telemetry for free (Trace / console / a stats file),
        // driven by the same metrics timer that already calls the sink. When enabling: uncomment the
        // block below, and from ProcessAsync's metrics loop call
        //   FormatStatsLine(GetInstrumentationSnapshot(), started, cfgTag)
        // then route the string to Trace/Console/file as desired (see the env-gated sinks used by the
        // runner: NKIT_CORE_STATS_CONSOLE / NKIT_CORE_STATS_FILE).
        //
        // Columns: [NKitCore <elapsed>s] <MB/s>  Util <busy/workers>  In% Proc% Out% (stage time
        // fractions over the sliding window)  Workers <busy>/<total>  Queue <waiting>  Ready <held for
        // ordered Out>  Total <MB processed>  [<cfgTag>].
        //
        // internal static string FormatStatsLine(InstrumentationSnapshot snap, DateTime started, string cfgTag)
        // {
        //     if (snap == null) return null;
        //
        //     double f(Stage s) => (snap.StageFractions != null && snap.StageFractions.TryGetValue(s, out double v)) ? v : 0.0;
        //
        //     // "In" = section create + producer->process handoff; "Proc" = parallel Process stage;
        //     // "Out" = ordered writer (completer) stage.
        //     double inFrac   = f(Stage.SectionCreate) + f(Stage.ProducerToProcess);
        //     double procFrac = f(Stage.Process);
        //     double outFrac  = f(Stage.Out);
        //
        //     int workers  = Math.Max(1, snap.CurrentWorkers);
        //     double util  = Math.Min(1.0, (double)snap.ProcessingInFlight / workers);
        //     double elapsed = (DateTime.UtcNow - started).TotalSeconds;
        //
        //     // Workers shown busy/total: ProcessingInFlight (inside ProcessAsync now) over CurrentWorkers
        //     // (live pool). Both are lock-free interlocked reads on the snapshot.
        //     string workersCol = snap.ProcessingInFlight + "/" + snap.CurrentWorkers;
        //
        //     return string.Format(
        //         "[NKitCore {0,6:F1}s] {1,7:F1} MB/s  Util {2,4:P0}  In {3,5:P0}  Proc {4,5:P0}  Out {5,5:P0}  Workers {6,5}  Queue {7}  Ready {8}  Total {9:F1} MB  [{10}]",
        //         elapsed, snap.ThroughputMBps, util, inFrac, procFrac, outFrac,
        //         workersCol, snap.QueueLength, snap.ReadyHeld, snap.TotalProcessedMB, cfgTag);
        // }
        //
        // Optional sink helper (also disabled): write the formatted line to Trace, and — when the
        // corresponding env vars are set — to the console and/or an append-only stats file. Kept env-
        // gated so it is inert unless a host opts in.
        //
        // internal static void EmitStatsLine(InstrumentationSnapshot snap, DateTime started, string cfgTag)
        // {
        //     string line = FormatStatsLine(snap, started, cfgTag);
        //     if (line == null) return;
        //     try
        //     {
        //         System.Diagnostics.Trace.WriteLine(line);
        //         if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NKIT_CORE_STATS_CONSOLE")))
        //             System.Console.WriteLine(line);
        //         string statsFile = System.Environment.GetEnvironmentVariable("NKIT_CORE_STATS_FILE");
        //         if (!string.IsNullOrEmpty(statsFile))
        //             System.IO.File.AppendAllText(statsFile, line + System.Environment.NewLine);
        //     }
        //     catch { /* telemetry must never affect processing */ }
        // }
        // ─────────────────────────────────────────────────────────────────────────────────────────
    }
}