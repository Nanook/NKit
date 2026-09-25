using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NKitCore
{
    /// <summary>
    /// Async section pipeline with a WORKER-DRAINS-IN-ORDER output stage.
    ///
    /// Pipeline shape:
    ///   Producer (1) -> [prodToProc channel] -> PreProcess (1, serial) -> [procQueue channel]
    ///                 -> Workers (N) each: Process, then act as the ordered Out writer when it is
    ///                    their section's turn.
    ///
    /// The ordered output is NOT a separate stage with a growing backlog. Instead, when a worker
    /// finishes processing section S it publishes S into a small <c>ready</c> map and, if S is the
    /// next section to emit and no other worker is currently writing, that same worker becomes the
    /// writer: it drains every consecutive ready section (calling the completer in order) until it
    /// hits one not yet ready, then relinquishes the writer role and goes back to processing.
    ///
    /// Why this avoids the ordered-Out head-of-line problem:
    ///  - The section the writer needs next (<c>_nextToComplete</c>) is ALWAYS already created and
    ///    somewhere in the pipeline (a worker has it or will). No admission/backlog gate can block
    ///    it, because admission only holds back NEW (higher-sequence) sections — never the head.
    ///  - Exactly one writer runs at a time (a single gate lock guards the claim), and the "publish
    ///    ready + claim/relinquish writer" decision is atomic under that lock, so there is no window
    ///    where a section is ready but nobody writes it, and none where two workers write at once.
    ///  - Running N = parallelism + 1 workers means that while one worker is writing, the configured
    ///    number of workers are still processing.
    ///
    /// Memory is bounded by the producer admission semaphore (maxInFlight): total live sections
    /// (in channels + processing + ready) never exceeds it. Ready-held sections are naturally at most
    /// the reorder distance, capped by maxInFlight.
    /// </summary>
    public sealed partial class StreamBlockCore<TSection, TContext>
        where TSection : class, ISection
        where TContext : class, ISectionContext
    {
        private readonly int _parallelism;
        private readonly int _maxInFlight;
        private readonly AutoscaleOptions _autoscale;
        private readonly Instrumentation _instr;
        private readonly Action<InstrumentationSnapshot> _metricsSink;
        private readonly Action<WorkerEvent> _workerEvent;
        private readonly int _metricsIntervalMs;

        // Ordered-output shared state (guarded by _gate).
        private readonly object _gate = new object();
        private long _nextToComplete = 1;
        private readonly Dictionary<long, ISection> _ready = new Dictionary<long, ISection>();

        // Occupancy counters for instrumentation / autoscaling.
        private long _queueOccupancy;      // waiting to be processed (prodToProc + procQueue)
        private long _processingInFlight;  // in a worker's ProcessAsync
        private long _readyHeld;           // processed, waiting for their Out turn
        private int _currentWorkers;       // live worker count (varies with autoscaling)

        // Dynamic admission: the producer is admitted while the number of LIVE sections
        // (in-channel + processing + ready) stays within currentWorkers + 1. Because the window
        // tracks the live worker count, the reorder set (ReadyHeld) is bounded by the worker count
        // AT ALL SCALES — add/remove a worker and the memory window moves with it.
        private long _liveSections;

        // The proc-bound scale-down floor (= legacy StartWorkers). While proc is the bottleneck the
        // pool is never trimmed below this, so we never regress below the old core's fixed parallelism.
        // Scale-down below it is only allowed when work is provably not proc-bound (Out congested or
        // the pipeline is draining), handled by the autoscaler choosing MinWorkers as the floor then.
        private int _procFloor = 1;

        // Proc-saturation latch (0/1). Set by the admission gate when the producer is held back PURELY
        // because every worker is busy (window full, nothing stuck waiting to be emitted). This is the
        // noise-free "another worker would have work to do" signal the autoscaler hill-climbs on;
        // throughput is used only as a brake. The autoscaler consumes (resets) it each tick.
        private long _procSaturationStall;

        public StreamBlockCore(
            int maxParallelism = 4,
            int maxInFlight = 0,
            AutoscaleOptions autoscaleOptions = null,
            Action<InstrumentationSnapshot> metricsSink = null,
            int metricsIntervalMs = 1000,
            Action<WorkerEvent> workerEvent = null)
        {
            _parallelism = Math.Max(1, maxParallelism);
            _autoscale = autoscaleOptions ?? new AutoscaleOptions { MinWorkers = 1, MaxWorkers = _parallelism };
            // Total live sections cap == worker count. This makes the reorder window (sections that
            // are being processed OR finished-and-waiting for their in-order Out turn) bounded by the
            // number of workers, NOT a separate backlog. Each worker holds at most one finished
            // section while it waits for its turn; once all admission slots are held the producer
            // stalls, so no worker can race ahead piling completed sections into _ready. Hence
            // ReadyHeld never exceeds the worker count — there is no "backlog". Never deadlocks: the
            // head section is always already admitted (created before the higher-sequence sections
            // that fill the slots), so it is always either in a worker or already in _ready.
            int workerCeiling = Math.Max(1, _autoscale.MaxWorkers) + 1; // +1 = writer headroom (see workers)
            _maxInFlight = maxInFlight > 0 ? maxInFlight : workerCeiling;
            _metricsSink = metricsSink;
            _metricsIntervalMs = Math.Max(100, metricsIntervalMs);
            _workerEvent = workerEvent;
            _instr = new Instrumentation();
        }

        public async Task ProcessAsync(
            TContext context,
            IStream stream,
            ISectionFactory sectionFactory,
            ISectionPreProcess preProcess,
            ISectionProcess processor,
            ISectionCompleter completer,
            CancellationToken ct)
        {
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, context.Cancellation);
            CancellationToken token = linkedCts.Token;

            // Dynamic admission gate. The producer may create a new section only while the number of
            // LIVE sections (created but not yet emitted) is below currentWorkers + 1. Because the
            // limit follows the live worker count, the reorder window (ReadyHeld) shrinks/grows with
            // the pool — so autoscaling never lets memory balloon. A refreshable TCS wakes the
            // producer when a section is emitted (a slot frees) or the worker count grows.
            Interlocked.Exchange(ref _liveSections, 0);
            object admitLock = new object();
            TaskCompletionSource<object> admitWake = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            void SignalAdmit()
            {
                lock (admitLock) { admitWake.TrySetResult(null); }
            }
            // Called when a section is fully emitted (slot frees) so the producer can re-check.
            _releaseInFlight = () => { Interlocked.Decrement(ref _liveSections); SignalAdmit(); };

            // Dedicated-writer wake signal. Workers pulse this after publishing a ready section so the
            // single writer task re-checks the ready set instead of spinning. Refreshable TCS.
            object writerLock = new object();
            TaskCompletionSource<object> writerWake = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            void SignalWriter()
            {
                lock (writerLock) { writerWake.TrySetResult(null); }
            }

            async Task AdmitAsync(CancellationToken admitToken)
            {
                while (true)
                {
                    admitToken.ThrowIfCancellationRequested();
                    long live = Interlocked.Read(ref _liveSections);
                    int workers = Volatile.Read(ref _currentWorkers);
                    // Window = workers + look-ahead, so processing can run ahead of a slow ordered-Out
                    // writer (keeps Out fed instead of starved). Memory stays bounded and tiny vs image.
                    int window = workers + Math.Max(1, _autoscale.LookAheadDepth);
                    if (live < window)
                    {
                        Interlocked.Increment(ref _liveSections);
                        return;
                    }

                    // Blocked. Latch WHY: if the window is full but few sections are stuck waiting for
                    // their Out turn (ReadyHeld small), then every worker is genuinely busy processing
                    // and another worker would have work — proc saturation. If instead ReadyHeld is
                    // large (processed sections piling up because the ordered Out stage is slow), more
                    // workers cannot help, so we do NOT latch. This is the noise-free scale-up cue.
                    long readyHeld = Interlocked.Read(ref _readyHeld);
                    long processing = Interlocked.Read(ref _processingInFlight);
                    bool outCongested = readyHeld >= workers;      // Out can't keep up with processed work
                    bool procSaturated = !outCongested && processing >= workers; // all workers busy
                    if (procSaturated)
                        Volatile.Write(ref _procSaturationStall, 1);

                    Task wake;
                    lock (admitLock)
                    {
                        if (admitWake.Task.IsCompleted)
                            admitWake = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                        wake = admitWake.Task;
                    }
                    await Task.WhenAny(wake, Task.Delay(25, admitToken)).ConfigureAwait(false);
                }
            }

            // prodToProc: producer -> serial pre-process. Small; the pre-process is a single reader.
            Channel<ISection> prodToProc = Channel.CreateBounded<ISection>(new BoundedChannelOptions(Math.Max(2, _parallelism))
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

            // procQueue: pre-process -> workers. Multi-reader.
            Channel<ISection> procQueue = Channel.CreateBounded<ISection>(new BoundedChannelOptions(Math.Max(2, _autoscale.MaxWorkers + 1))
            { SingleReader = false, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

            long sequence = 0;
            Exception firstError = null;
            void Fault(Exception ex)
            {
                if (Interlocked.CompareExchange(ref firstError, ex, null) == null)
                {
                    try { linkedCts.Cancel(); } catch { }
                }
            }

            await sectionFactory.ConstructAsync(context, stream, token).ConfigureAwait(false);
            await sectionFactory.SetupAsync(context, stream, token).ConfigureAwait(false);

            // ---- Producer ----
            Task producer = Task.Run(async () =>
            {
                try
                {
                    IReadOnlyList<IArea> areas = stream.InitialAreas;
                    for (int ai = 0; ai < areas.Count; ai++)
                    {
                        IArea area = areas[ai];
                        context.RegisterArea(area);
                        long offset = area.ImageOffset;

                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            await AdmitAsync(token).ConfigureAwait(false);

                            ISection sec;
                            Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                sec = await sectionFactory.CreateNextSectionAsync(context, stream, area, offset, token).ConfigureAwait(false);
                            }
                            catch
                            {
                                _releaseInFlight();
                                throw;
                            }
                            sw.Stop();

                            if (sec == null || sec.Length == 0)
                            {
                                _releaseInFlight();
                                break; // area exhausted
                            }
                            _instr.Record(Stage.SectionCreate, sec.Length, sw.Elapsed);

                            sec.SequenceNumber = Interlocked.Increment(ref sequence);
                            sec.AreaIndex = area.Index;
                            context.IncrementOutstandingSections(area.Index);

                            Interlocked.Increment(ref _queueOccupancy);
                            await prodToProc.Writer.WriteAsync(sec, token).ConfigureAwait(false);

                            offset += sec.Length;

                            if (sec.IsLastInArea)
                            {
                                await context.WaitForAreaCompletionAsync(area.Index, token).ConfigureAwait(false);
                                break;
                            }
                            if (sec.IsLastInStream)
                                break;
                        }
                    }
                    prodToProc.Writer.Complete();
                }
                catch (Exception ex)
                {
                    prodToProc.Writer.Complete(ex);
                    Fault(ex);
                }
            }, token);

            // ---- Serial pre-process ----
            Task preTask = Task.Run(async () =>
            {
                try
                {
                    ChannelReader<ISection> reader = prodToProc.Reader;
                    while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out ISection sec))
                        {
                            Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                            await preProcess.PreProcessAsync(context, sec, token).ConfigureAwait(false);
                            sw.Stop();
                            _instr.Record(Stage.ProducerToProcess, 0, sw.Elapsed);
                            await procQueue.Writer.WriteAsync(sec, token).ConfigureAwait(false);
                        }
                    }
                    procQueue.Writer.Complete();
                }
                catch (Exception ex)
                {
                    procQueue.Writer.Complete(ex);
                    Fault(ex);
                }
            }, token);

            // ---- Dynamic workers (process + ordered-out) + hill-climbing autoscaler ----
            //
            // Workers are spawned on demand and self-exit when asked. A worker only ever exits
            // BETWEEN sections, holding nothing (the drain-in-order model means an idle worker has no
            // section pinned), so removing one can never lose a section or stall the ordered writer.
            // This makes scale-down trivial and safe — no cancellation, no orphaned work.
            ConcurrentDictionary<int, Task> workerTasks = new ConcurrentDictionary<int, Task>();
            int workerIdSeq = 0;
            // Shrink coordination. All worker-count changes go through _scaleLock so the floor
            // (max(1, MinWorkers)) is enforced race-free: a worker can only exit if doing so keeps
            // _currentWorkers at or above the floor, and the decrement happens atomically with the
            // decision. _pendingStops caps outstanding "please exit" requests at one at a time — the
            // autoscaler issues one and waits to see it take effect before issuing another, so it can
            // never pile up tokens and undershoot to zero.
            object scaleLock = new object();
            int pendingStops = 0;

            int MinFloor() => Math.Max(1, _autoscale.MinWorkers);

            // A worker (holding nothing, between sections) asks permission to exit. Returns true only
            // if a stop was requested AND removing this worker keeps the pool at/above the floor. The
            // count is decremented here, atomically, so two workers cannot both pass and undershoot.
            bool TryClaimStop()
            {
                lock (scaleLock)
                {
                    if (pendingStops <= 0) return false;
                    if (Volatile.Read(ref _currentWorkers) <= MinFloor()) return false;
                    pendingStops--;
                    Interlocked.Decrement(ref _currentWorkers);
                    return true;
                }
            }

            void RaiseWorker(WorkerEventKind kind, int id, string reason)
            {
                Action<WorkerEvent> cb = _workerEvent;
                if (cb == null) return;
                try { cb(new WorkerEvent(kind, id, Volatile.Read(ref _currentWorkers), reason)); }
                catch { /* a host log sink must never abort processing */ }
            }

            void SpawnWorker(string reason)
            {
                int id = Interlocked.Increment(ref workerIdSeq);
                Interlocked.Increment(ref _currentWorkers);
                RaiseWorker(WorkerEventKind.Spawned, id, reason);
                workerTasks[id] = Task.Run(async () =>
                {
                    ChannelReader<ISection> reader = procQueue.Reader;
                    bool claimedStop = false;
                    try
                    {
                        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                        {
                            // Co-operative shrink: if the autoscaler has asked for fewer workers,
                            // claim one stop and exit now (between sections, holding nothing).
                            if (Volatile.Read(ref pendingStops) > 0 && TryClaimStop())
                            {
                                claimedStop = true;
                                return;
                            }

                            while (reader.TryRead(out ISection sec))
                            {
                                Interlocked.Decrement(ref _queueOccupancy);
                                Interlocked.Increment(ref _processingInFlight);
                                try
                                {
                                    Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                                    await processor.ProcessAsync(context, sec, token).ConfigureAwait(false);
                                    sw.Stop();
                                    _instr.Record(Stage.Process, sec.Length, sw.Elapsed);
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref _processingInFlight);
                                }

                                // Publish into the ready set and wake the dedicated writer. Workers
                                // never write themselves (no claim/relinquish churn); a single
                                // long-lived writer keeps the ordered Out stage warm and overlaps it
                                // with parallel processing, matching the legacy consumer's behaviour.
                                lock (_gate)
                                {
                                    _ready[sec.SequenceNumber] = sec;
                                    Interlocked.Increment(ref _readyHeld);
                                }
                                SignalWriter();
                            }
                        }
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == token) { }
                    catch (Exception ex) { Fault(ex); }
                    finally
                    {
                        workerTasks.TryRemove(id, out _);
                        // If we exited via TryClaimStop the count was already decremented there.
                        // Otherwise (channel drained or fault) decrement now.
                        if (!claimedStop) Interlocked.Decrement(ref _currentWorkers);
                        // Report the exit: a cooperative shrink (autoscaler) is Retired; a channel
                        // drain at end-of-run (or fault) is Ended.
                        RaiseWorker(claimedStop ? WorkerEventKind.Retired : WorkerEventKind.Ended, id,
                            claimedStop ? "scale-down" : "drained");
                        // A worker leaving frees an admission slot's worth of window; wake the producer.
                        SignalAdmit();
                    }
                }, token);
            }

            // Start FULLY PROVISIONED at StartWorkers (the legacy fixed parallelism) so we are never
            // slower than the old core out of the gate. The autoscaler climbs above this only when it
            // clearly helps, and trims below it only when work is provably not proc-bound.
            Volatile.Write(ref _currentWorkers, 0);
            int procFloor = Math.Max(MinFloor(), _autoscale.StartWorkers > 0 ? _autoscale.StartWorkers : MinFloor());
            procFloor = Math.Min(procFloor, Math.Max(1, _autoscale.MaxWorkers));
            Volatile.Write(ref _procFloor, procFloor);
            int startWorkers = procFloor;
            for (int i = 0; i < startWorkers; i++) SpawnWorker("start");

            // ---- Saturation-driven marginal-utility hill-climb (ported from the proven legacy core) ----
            //
            // Real workloads make per-sample THROUGHPUT far too noisy to hill-climb on directly (a run
            // of expensive-to-compress blocks swings MB/s wildly regardless of worker count). So the
            // primary scale-UP trigger is the PROC-SATURATION latch: the producer got blocked purely
            // because every worker was busy (and the ordered Out stage was NOT the reason). That is
            // direct, noise-free evidence that one more worker would have real work to do.
            //
            // Add ONE worker at a time (so marginal utility is measurable per step). After each add,
            // wait ScaleEvaluationMs then judge: if the producer is STILL proc-saturated, demand is
            // provably unmet — keep going. Otherwise, if throughput didn't rise by ThroughputImprovementRatio,
            // the added worker didn't help (serial pre/Out or I/O bound) — engage a brake and stop adding.
            // Scale DOWN when live-section pressure falls below LowWater. This settles at ~4 on the
            // write-bound Wii convert (matching legacy parallelism 4) and climbs high on a CPU-bound scan.
            CancellationTokenSource autoscaleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task autoscaler = Task.Run(async () =>
            {
                CancellationToken at = autoscaleCts.Token;
                int floor = MinFloor();

                double CurrentThroughput()
                {
                    try { return _instr.Snapshot().thruMBps; } catch { return 0.0; }
                }

                // Combined live pressure = sections waiting to process + sections currently processing.
                long Pressure() => Interlocked.Read(ref _queueOccupancy) + Interlocked.Read(ref _processingInFlight);

                bool WorkPending() => !producer.IsCompleted || Pressure() > 0;

                void RequestShrink()
                {
                    lock (scaleLock)
                    {
                        if (pendingStops == 0 && Volatile.Read(ref _currentWorkers) > floor)
                            pendingStops = 1;
                    }
                }

                // --- marginal-utility state ---
                double throughputBeforeLastScaleUp = 0.0;
                DateTime lastScaleUpTime = DateTime.MinValue;
                bool awaitingScaleUpEvaluation = false;
                bool scaleUpDisallowed = false; // brake: added worker didn't help — stop climbing
                DateTime brakeEngagedTime = DateTime.MinValue;
                DateTime lastScale = DateTime.UtcNow - TimeSpan.FromMilliseconds(_autoscale.CooldownMs);

                // Effective "did the last worker help?" threshold. This is what actually caps the climb
                // (there is no target worker count — MaxWorkers is only a hard safety ceiling). Adding a
                // worker must raise throughput by at least this fraction to be kept.
                double gainThreshold = Math.Max(_autoscale.ThroughputImprovementRatio, _autoscale.MinScaleUpGainFraction * 0.5);
                // After the brake fires, re-probe (try one more worker) this often, in case a later
                // phase of the workload is CPU-bound and could use more. If it still doesn't help, the
                // brake re-engages — so we explore upward periodically without thrashing.
                double reprobeMs = Math.Max(2000, _autoscale.ScaleEvaluationMs * 5);

                try
                {
                    await Task.Delay(_autoscale.ScaleEvaluationMs, at).ConfigureAwait(false); // warm up

                    while (!at.IsCancellationRequested && WorkPending())
                    {
                        int workers = Volatile.Read(ref _currentWorkers);
                        long pressure = Pressure();
                        long readyHeld = Interlocked.Read(ref _readyHeld);
                        bool cooldownElapsed = (DateTime.UtcNow - lastScale).TotalMilliseconds > _autoscale.CooldownMs;

                        // Consume the proc-saturation latch (reset each tick so a decision reflects only
                        // the most recent interval).
                        bool sawProcSaturation = Interlocked.Exchange(ref _procSaturationStall, 0) == 1;

                        // Evaluate the previous scale-up now that it has had time to act.
                        if (_autoscale.EnableMarginalUtilityScaling && awaitingScaleUpEvaluation &&
                            (DateTime.UtcNow - lastScaleUpTime).TotalMilliseconds >= _autoscale.ScaleEvaluationMs)
                        {
                            double now = CurrentThroughput();
                            double baseline = Math.Max(0.0001, throughputBeforeLastScaleUp);
                            double gainRatio = (now - throughputBeforeLastScaleUp) / baseline;
                            // The added worker didn't raise throughput enough — it isn't helping, so
                            // stop climbing. (While still proc-saturated the laggy sliding-window reading
                            // is ambiguous, but if adding a worker truly helped, throughput would rise;
                            // if it didn't after a full evaluation window, more workers won't either.)
                            if (gainRatio < gainThreshold)
                            {
                                // The added worker didn't visibly raise throughput — STOP CLIMBING.
                                scaleUpDisallowed = true;
                                brakeEngagedTime = DateTime.UtcNow;
                                // A clear REGRESSION (throughput actually dropped, not merely failed to
                                // improve) means the extra worker is making things worse — e.g. added
                                // contention/thrash — so shed it even if the pool looks saturated. Only
                                // when the gain was merely insufficient (flat/slightly positive) do we
                                // keep a fully-saturated worker, since a flat reading there is usually
                                // just noise / an Out-paced phase and shedding caused steady-state slowdown.
                                // Never shrink below the proc floor (legacy parallelism) either.
                                long busy = Interlocked.Read(ref _processingInFlight);
                                bool fullySaturated = busy >= workers;
                                bool regressed = gainRatio < -_autoscale.ThroughputImprovementRatio; // got worse
                                if ((regressed || !fullySaturated) && workers > Volatile.Read(ref _procFloor))
                                    RequestShrink();
                            }
                            awaitingScaleUpEvaluation = false;
                        }

                        // Periodic re-probe: if the brake is on but the workload may have shifted, allow
                        // one more upward try. If that worker doesn't help, the brake re-engages above.
                        if (scaleUpDisallowed && !awaitingScaleUpEvaluation
                            && (DateTime.UtcNow - brakeEngagedTime).TotalMilliseconds >= reprobeMs)
                        {
                            scaleUpDisallowed = false;
                        }

                        // Out stage congested (processed sections piling up in _ready): more proc workers
                        // cannot help — never scale up in this state.
                        bool outCongested = readyHeld >= workers;
                        // Proc is the bottleneck when pressure is high OR the producer stalled purely on
                        // worker saturation (which the tight admission gate otherwise hides).
                        bool procBound = pressure > _autoscale.HighWater || sawProcSaturation;

                        int desired = workers;
                        if (procBound && workers < _autoscale.MaxWorkers && cooldownElapsed
                            && !outCongested && !(_autoscale.EnableMarginalUtilityScaling && scaleUpDisallowed))
                        {
                            desired = workers + 1;                 // one at a time
                            lastScale = DateTime.UtcNow;
                            throughputBeforeLastScaleUp = CurrentThroughput();
                            lastScaleUpTime = DateTime.UtcNow;
                            awaitingScaleUpEvaluation = true;
                        }
                        else
                        {
                            // Scale DOWN. The floor depends on WHY: while proc is the bottleneck, never
                            // trim below the proc floor (legacy parallelism) — those workers are earning
                            // their keep. Only when work is provably not proc-bound (Out congested, or
                            // pressure has genuinely dropped) may we go all the way down to MinWorkers.
                            int shrinkFloor = (outCongested || !procBound) ? floor : procFloor;
                            bool lowPressure = pressure < _autoscale.LowWater;
                            if ((lowPressure || outCongested) && workers > shrinkFloor && cooldownElapsed)
                            {
                                desired = workers - 1;
                                lastScale = DateTime.UtcNow;
                                scaleUpDisallowed = false;         // re-allow scale-up if demand rises again
                                awaitingScaleUpEvaluation = false;
                            }
                        }

                        if (desired > workers) SpawnWorker(sawProcSaturation ? "scale-up: proc-saturated" : "scale-up: proc-bound");
                        else if (desired < workers)
                        {
                            // Enforce the chosen floor at request time (TryClaimStop enforces the hard
                            // MinWorkers floor; this prevents dipping below the proc floor while busy).
                            int reqFloor = (outCongested || !procBound) ? floor : procFloor;
                            lock (scaleLock)
                            {
                                if (pendingStops == 0 && Volatile.Read(ref _currentWorkers) > reqFloor)
                                    pendingStops = 1;
                            }
                        }

                        await Task.Delay(_autoscale.SampleIntervalMs, at).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
            }, autoscaleCts.Token);

            // ---- Dedicated ordered writer ----
            // A single long-lived task owns the ordered Out stage. It drains consecutive ready
            // sections in _nextToComplete order, calling the completer OUTSIDE the lock, and awaits a
            // wake pulse when the head isn't ready yet. This keeps the writer thread warm and lets the
            // tail (PostProcess/xxHash/Step.Process) overlap with parallel processing — with no
            // per-section writer claim/relinquish handoff. Exactly one writer => ordering guaranteed.
            Task writerTask = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        ISection sec;
                        long seq;
                        lock (_gate)
                        {
                            seq = Interlocked.Read(ref _nextToComplete);
                            _ready.TryGetValue(seq, out sec);
                            if (sec != null)
                            {
                                _ready.Remove(seq);
                                Interlocked.Decrement(ref _readyHeld);
                            }
                        }

                        if (sec == null)
                        {
                            // Head not ready. Done when producer finished, no work is in flight, and
                            // there is nothing waiting: every created section has been emitted.
                            if (producer.IsCompleted
                                && Interlocked.Read(ref _processingInFlight) == 0
                                && Interlocked.Read(ref _queueOccupancy) == 0
                                && Interlocked.Read(ref _readyHeld) == 0
                                && Interlocked.Read(ref _nextToComplete) > Interlocked.Read(ref sequence))
                                return;

                            Task wake;
                            lock (writerLock)
                            {
                                if (writerWake.Task.IsCompleted)
                                    writerWake = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                                wake = writerWake.Task;
                            }
                            await Task.WhenAny(wake, Task.Delay(25, token)).ConfigureAwait(false);
                            token.ThrowIfCancellationRequested();
                            continue;
                        }

                        // Write outside the lock.
                        Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            SectionCompleteResult result = await completer.CompleteAsync(context, sec, token).ConfigureAwait(false);
                            _ = result; // seek not implemented in this core
                        }
                        finally
                        {
                            sw.Stop();
                            _instr.Record(Stage.Out, 0, sw.Elapsed);
                            try { context.DecrementOutstandingSections(sec.AreaIndex); } catch { }
                            try { context.ReturnSection(sec); } catch { }
                            ReleaseInFlight();
                        }

                        Interlocked.Increment(ref _nextToComplete);
                    }
                }
                catch (OperationCanceledException oce) when (oce.CancellationToken == token) { }
                catch (Exception ex) { Fault(ex); }
            }, token);

            // ---- Metrics timer ----
            CancellationTokenSource metricsCts = null;
            Task metricsTask = null;
            if (_metricsSink != null)
            {
                metricsCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                CancellationToken mt = metricsCts.Token;
                metricsTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!mt.IsCancellationRequested)
                        {
                            try { _metricsSink(GetSnapshot(context)); } catch { }
                            await Task.Delay(_metricsIntervalMs, mt).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                }, mt);
            }

            try
            {
                // Producer + serial pre-process finish first; that completes procQueue, so every
                // worker's WaitToReadAsync eventually returns false and the worker exits.
                await Task.WhenAll(producer, preTask).ConfigureAwait(false);

                // Stop the autoscaler so it no longer spawns new workers, then drain all live worker
                // tasks (the dictionary shrinks as each exits). Loop because the set is dynamic.
                autoscaleCts.Cancel();
                try { await autoscaler.ConfigureAwait(false); } catch { }
                while (true)
                {
                    Task[] remaining = workerTasks.Values.ToArray();
                    if (remaining.Length == 0) break;
                    await Task.WhenAll(remaining).ConfigureAwait(false);
                }

                // All workers are done: every remaining ready section is now published. Pulse the
                // dedicated writer so it drains the tail and observes the completion condition.
                SignalWriter();
                await writerTask.ConfigureAwait(false);
            }
            finally
            {
                try { autoscaleCts.Dispose(); } catch { }
                if (metricsCts != null)
                {
                    metricsCts.Cancel();
                    try { await (metricsTask ?? Task.CompletedTask).ConfigureAwait(false); } catch { }
                    metricsCts.Dispose();
                }
            }

            if (firstError != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();

            // Everything processed and emitted; let the completer finalise.
            await completer.FinalizeAsync(context, token).ConfigureAwait(false);
        }

        // inFlight release indirection so the dedicated writer (which does not capture the local
        // admission state) can free a slot. Set at the top of ProcessAsync.
        private Action _releaseInFlight;
        private void ReleaseInFlight() => _releaseInFlight?.Invoke();

        private InstrumentationSnapshot GetSnapshot(TContext context)
        {
            (IReadOnlyDictionary<Stage, double> frac, double thru, double totalMB) = _instr.Snapshot();
            InstrumentationSnapshot snap = new InstrumentationSnapshot
            {
                StageFractions = frac,
                ThroughputMBps = thru,
                TotalProcessedMB = totalMB,
                QueueLength = Interlocked.Read(ref _queueOccupancy),
                ProcessingInFlight = Interlocked.Read(ref _processingInFlight),
                ReadyHeld = Interlocked.Read(ref _readyHeld),
                CurrentWorkers = Volatile.Read(ref _currentWorkers),
            };
            try { (int FreeCount, int TotalCreated) pm = context.GetPoolMetrics(); snap.PoolFreeCount = pm.FreeCount; snap.PoolTotalCreated = pm.TotalCreated; } catch { }
            return snap;
        }

        public InstrumentationSnapshot GetInstrumentationSnapshot(TContext context) => GetSnapshot(context);
    }
}