using System;
using System.Collections.Generic;

namespace NKitCore
{
    public sealed partial class StreamBlockCore<TSection, TContext>
        where TSection : class, ISection
        where TContext : class, ISectionContext
    {
        public sealed class AutoscaleOptions
        {
            public int MinWorkers { get; set; } = 1;
            // Where the pool STARTS (and the floor it won't shrink below while proc-bound). Set this to
            // the legacy fixed parallelism so the pipeline is never slower than the old core out of the
            // gate: it begins fully provisioned and the autoscaler only climbs ABOVE this when there's
            // clear benefit, or trims below only when work is provably not proc-bound (Out congested /
            // pipeline draining). 0 = use MinWorkers.
            public int StartWorkers { get; set; } = 0;
            // HARD SAFETY CEILING, not a tuning target. The autoscaler does NOT climb toward this; it
            // stops adding workers as soon as the marginal-utility brake fires (a new worker stops
            // raising throughput). This exists only so a pathological workload can never spawn more
            // threads than the machine has logical cores. Defaults to the core count.
            public int MaxWorkers { get; set; } = Environment.ProcessorCount;
            public int SampleIntervalMs { get; set; } = 200;
            public int CooldownMs { get; set; } = 500;
            // A worker must have been idle at least this long before it is a scale-down candidate.
            public int IdleWorkerMinMs { get; set; } = 2000;

            // Marginal-utility brake: after a scale-up, if throughput did not improve by at least this
            // fraction AND proc is no longer saturated, stop adding workers (the added worker isn't
            // helping — the bottleneck is a serial pre/out stage or I/O). Throughput is only ever a
            // BRAKE here, never the primary scale-up trigger (per-sample throughput is too noisy on
            // real workloads). Scale-UP is driven by the proc-saturation signal; scale-DOWN by the
            // live-section pressure dropping below LowWater.
            public double MinScaleUpGainFraction { get; set; } = 0.10;
            public int ScaleEvaluationMs { get; set; } = 600;

            // Enable the throughput brake described above. When false, the controller scales purely on
            // saturation/pressure (which is what settles cleanly on noisy real workloads).
            public bool EnableMarginalUtilityScaling { get; set; } = true;
            public double ThroughputImprovementRatio { get; set; } = 0.05;

            // Scale-DOWN trigger: when live-section pressure (waiting + processing) falls below this,
            // there isn't enough work to keep the current pool busy — shed a worker. Scale-UP pressure
            // trigger (in addition to the saturation latch) is HighWater.
            public int LowWater { get; set; } = 1;
            public int HighWater { get; set; } = int.MaxValue; // saturation latch is the real trigger

            // Writer look-ahead depth. The producer may admit up to (currentWorkers + this many) live
            // sections, so processing can run AHEAD of a slow ordered-Out writer by this many sections.
            // Without it, an Out-bound workload starves the writer: it finishes section N and N+1 isn't
            // processed yet, so Out idles. Legacy achieved this with a large maxInFlight (128). This
            // keeps memory bounded (a small multiple of the section size) while keeping the writer fed.
            public int LookAheadDepth { get; set; } = 8;
        }

        public enum Stage
        {
            SectionCreate,
            ProducerToProcess,
            Process,
            Out
        }

        private static readonly Stage[] _AllStages =
            new[] { Stage.SectionCreate, Stage.ProducerToProcess, Stage.Process, Stage.Out };

        public sealed class InstrumentationSnapshot
        {
            public double ThroughputMBps { get; set; }
            public double TotalProcessedMB { get; set; }
            public IReadOnlyDictionary<Stage, double> StageFractions { get; set; }
            public long QueueLength { get; set; }       // sections waiting to be processed
            public long ProcessingInFlight { get; set; } // sections currently in a worker's Process
            public long ReadyHeld { get; set; }          // processed, waiting for their in-order Out turn
            public int CurrentWorkers { get; set; }
            public int PoolFreeCount { get; set; }
            public int PoolTotalCreated { get; set; }
        }

        /// <summary>Worker-pool lifecycle transitions, reported via the worker-event callback.</summary>
        public enum WorkerEventKind
        {
            /// <summary>A new worker was spawned (pool grew).</summary>
            Spawned,
            /// <summary>A worker exited cooperatively because the autoscaler asked the pool to shrink.</summary>
            Retired,
            /// <summary>A worker exited because the pipeline drained (end of run) or faulted.</summary>
            Ended,
        }

        /// <summary>
        /// A worker-pool lifecycle event. Reported to the optional worker-event callback so a host can
        /// log the pool scaling up/down. Engine-agnostic (no logging dependency in the core): the host
        /// decides how to render it. <see cref="WorkerId"/> is the STABLE logical worker id (1-based),
        /// not a thread id, so counts map 1:1 to real pool workers.
        /// </summary>
        public readonly struct WorkerEvent
        {
            public WorkerEvent(WorkerEventKind kind, int workerId, int currentWorkers, string reason)
            {
                Kind = kind;
                WorkerId = workerId;
                CurrentWorkers = currentWorkers;
                Reason = reason;
            }

            public WorkerEventKind Kind { get; }
            /// <summary>Stable logical worker id (1-based), not a thread id.</summary>
            public int WorkerId { get; }
            /// <summary>Live worker count AFTER this transition.</summary>
            public int CurrentWorkers { get; }
            /// <summary>Short reason/context (e.g. "start", "scale-up: proc-bound", "drained").</summary>
            public string Reason { get; }
        }
    }
}