using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    /// <summary>
    /// Thread-based section pipeline with a dedicated ordered-output writer.
    ///
    /// Pipeline shape:
    ///   Producer (1 LongRunning thread) -> [readToPreProc BlockingCollection]
    ///     -> PreProcess (1 LongRunning thread) -> [preProcToWorkers BlockingCollection]
    ///       -> Workers (N LongRunning threads, autoscaled) -> [_ready ordered dict]
    ///         -> Writer (1 LongRunning thread, ordered)
    ///
    /// Why LongRunning threads instead of async Task.Run:
    ///  - The producer calls IImage.Read() / DataStore I/O synchronously. On a LongRunning
    ///    dedicated thread that blocking call is harmless; on a thread-pool Task it stalls
    ///    the pool and makes CancellationToken propagation unreliable.
    ///  - BlockingCollection.Add/TryTake observes CancellationToken directly, so a user
    ///    cancel unwinds every stage cleanly within milliseconds without a watchdog.
    ///  - ManualResetEventSlim replaces the TCS + Task.WhenAny(wake, Task.Delay(25)) pattern
    ///    for immediate wake-on-slot-released, cutting per-section gate latency to zero.
    ///
    /// Memory is bounded by the admission gate: total live sections never exceeds
    /// currentWorkers + lookAhead. The reorder set (_ready) is bounded by that same window.
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
        private long _queueOccupancy;      // waiting to be processed (readToPreProc + preProcToWorkers)
        private long _processingInFlight;  // in a worker's ProcessAsync
        private long _readyHeld;           // processed, waiting for their Out turn
        private int _currentWorkers;       // live worker count (varies with autoscaling)

        // Dynamic admission: the producer is admitted while the number of LIVE sections
        // (in queues + processing + ready) stays within currentWorkers + lookAhead.
        private long _liveSections;

        // The proc-bound scale-down floor (= legacy StartWorkers).
        private int _procFloor = 1;

        // Proc-saturation latch (0/1). Set by the admission gate when the producer is held back
        // purely because every worker is busy. The autoscaler consumes (resets) it each tick.
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
            int workerCeiling = Math.Max(1, _autoscale.MaxWorkers) + 1;
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

            // Reset per-run counters.
            Interlocked.Exchange(ref _liveSections, 0);

            // Admission gate. ManualResetEventSlim wakes the producer IMMEDIATELY when a slot is
            // freed (ReleaseInFlight) instead of polling every 25 ms with Task.Delay.
            ManualResetEventSlim admitWake = new ManualResetEventSlim(false);
            void SignalAdmit() { admitWake.Set(); }
            _releaseInFlight = () => { Interlocked.Decrement(ref _liveSections); admitWake.Set(); };

            // Synchronous admission: runs on the LongRunning producer thread so blocking is fine.
            void admit(CancellationToken admitToken)
            {
                while (true)
                {
                    admitToken.ThrowIfCancellationRequested();
                    long live = Interlocked.Read(ref _liveSections);
                    int workers = Volatile.Read(ref _currentWorkers);
                    int window = workers + Math.Max(1, _autoscale.LookAheadDepth);
                    if (live < window)
                    {
                        Interlocked.Increment(ref _liveSections);
                        return;
                    }
                    long readyHeld = Interlocked.Read(ref _readyHeld);
                    long processing = Interlocked.Read(ref _processingInFlight);
                    bool outCongested = readyHeld >= workers;
                    bool procSaturated = !outCongested && processing >= workers;
                    if (procSaturated)
                        Volatile.Write(ref _procSaturationStall, 1);

                    admitWake.Reset();
                    // Re-check after reset to avoid missing a signal that arrived between the failed
                    // live < window test and the Reset call.
                    live = Interlocked.Read(ref _liveSections);
                    workers = Volatile.Read(ref _currentWorkers);
                    window = workers + Math.Max(1, _autoscale.LookAheadDepth);
                    if (live < window)
                    {
                        Interlocked.Increment(ref _liveSections);
                        admitWake.Set(); // restore so concurrent waiters are not missed
                        return;
                    }
                    admitWake.Wait(25, admitToken); // wakes immediately on Set(); 25 ms backstop
                }
            }

            // Writer wake: workers Set this after publishing to _ready so the writer re-checks
            // the ordered head without spinning.
            ManualResetEventSlim writerWake = new ManualResetEventSlim(false);
            void SignalWriter() { writerWake.Set(); }

            // Inter-stage queues. Bounded BlockingCollections: each stage blocks on Add/TryTake in
            // its own dedicated LongRunning thread, so a slow read can never stall the thread pool.
            BlockingCollection<ISection> readToPreProc = new BlockingCollection<ISection>(
                new ConcurrentQueue<ISection>(), Math.Max(2, _parallelism));
            BlockingCollection<ISection> preProcToWorkers = new BlockingCollection<ISection>(
                new ConcurrentQueue<ISection>(), Math.Max(2, _autoscale.MaxWorkers + 1));

            long sequence = 0;
            Exception firstError = null;
            void Fault(Exception ex)
            {
                if (Interlocked.CompareExchange(ref firstError, ex, null) == null)
                {
                    try { linkedCts.Cancel(); } catch { }
                }
            }

            sectionFactory.ConstructAsync(context, stream, token).GetAwaiter().GetResult();
            sectionFactory.SetupAsync(context, stream, token).GetAwaiter().GetResult();

            // ---- Producer (LongRunning: safe to block on IImage.Read / DataStore I/O) ----
            Task producer = Task.Factory.StartNew(() =>
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
                            admit(token); // synchronous; wakes immediately when a slot is released

                            ISection sec;
                            Stopwatch sw = Stopwatch.StartNew();
                            try
                            {
                                sec = sectionFactory.CreateNextSectionAsync(context, stream, area, offset, token)
                                                    .GetAwaiter().GetResult();
                            }
                            catch
                            {
                                ReleaseInFlight();
                                throw;
                            }
                            sw.Stop();

                            if (sec == null || sec.Length == 0)
                            {
                                ReleaseInFlight();
                                break; // area exhausted
                            }
                            _instr.Record(Stage.SectionCreate, sec.Length, sw.Elapsed);

                            sec.SequenceNumber = Interlocked.Increment(ref sequence);
                            sec.AreaIndex = area.Index;
                            context.IncrementOutstandingSections(area.Index);

                            Interlocked.Increment(ref _queueOccupancy);
                            readToPreProc.Add(sec, token);

                            offset += sec.Length;

                            if (sec.IsLastInArea)
                            {
                                // Block until all in-flight sections for this area clear the ordered
                                // writer (DecrementOutstandingSections fires the TCS). Safe on a
                                // dedicated LongRunning thread.
                                context.WaitForAreaCompletionAsync(area.Index, token).GetAwaiter().GetResult();
                                break;
                            }
                            if (sec.IsLastInStream)
                                break;
                        }
                    }
                    readToPreProc.CompleteAdding();
                }
                catch (Exception ex)
                {
                    try { readToPreProc.CompleteAdding(); } catch { }
                    Fault(ex);
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // ---- Serial pre-process (LongRunning) ----
            Task preTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    foreach (ISection sec in readToPreProc.GetConsumingEnumerable(token))
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        preProcess.PreProcessAsync(context, sec, token).GetAwaiter().GetResult();
                        sw.Stop();
                        _instr.Record(Stage.ProducerToProcess, 0, sw.Elapsed);
                        preProcToWorkers.Add(sec, token);
                    }
                    preProcToWorkers.CompleteAdding();
                }
                catch (OperationCanceledException) { try { preProcToWorkers.CompleteAdding(); } catch { } }
                catch (Exception ex) { try { preProcToWorkers.CompleteAdding(); } catch { } Fault(ex); }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // ---- Dynamic workers + hill-climbing autoscaler ----
            //
            // Workers are spawned on demand and self-exit when asked. A worker only ever exits
            // BETWEEN sections, holding nothing, so removing one can never lose a section or
            // stall the ordered writer.
            ConcurrentDictionary<int, Task> workerTasks = new ConcurrentDictionary<int, Task>();
            int workerIdSeq = 0;
            object scaleLock = new object();
            int pendingStops = 0;

            int MinFloor() => Math.Max(1, _autoscale.MinWorkers);

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
                workerTasks[id] = Task.Factory.StartNew(() =>
                {
                    bool claimedStop = false;
                    try
                    {
                        while (!claimedStop)
                        {
                            // Co-operative scale-down: check before taking a section so a worker
                            // always exits between sections holding nothing.
                            if (Volatile.Read(ref pendingStops) > 0 && TryClaimStop())
                            {
                                claimedStop = true;
                                break;
                            }

                            ISection sec;
                            // 50 ms timeout gives the scale-down check a chance to run without
                            // busy-spin; the autoscaler interval is 500 ms so this is sufficient.
                            if (!preProcToWorkers.TryTake(out sec, 50, token))
                            {
                                if (preProcToWorkers.IsCompleted)
                                    break;
                                continue;
                            }

                            Interlocked.Decrement(ref _queueOccupancy);
                            Interlocked.Increment(ref _processingInFlight);
                            Stopwatch sw = Stopwatch.StartNew();
                            try
                            {
                                processor.ProcessAsync(context, sec, token).GetAwaiter().GetResult();
                                sw.Stop();
                                _instr.Record(Stage.Process, sec.Length, sw.Elapsed);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _processingInFlight);
                            }

                            lock (_gate)
                            {
                                _ready[sec.SequenceNumber] = sec;
                                Interlocked.Increment(ref _readyHeld);
                            }
                            SignalWriter();
                        }
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == token) { }
                    catch (Exception ex) { Fault(ex); }
                    finally
                    {
                        workerTasks.TryRemove(id, out _);
                        if (!claimedStop) Interlocked.Decrement(ref _currentWorkers);
                        RaiseWorker(claimedStop ? WorkerEventKind.Retired : WorkerEventKind.Ended, id,
                            claimedStop ? "scale-down" : "drained");
                        SignalAdmit();
                    }
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            // Start FULLY PROVISIONED at StartWorkers so we are never slower than the old core out
            // of the gate. The autoscaler climbs above this only when it clearly helps, and trims
            // below it only when work is provably not proc-bound.
            Volatile.Write(ref _currentWorkers, 0);
            int procFloor = Math.Max(MinFloor(), _autoscale.StartWorkers > 0 ? _autoscale.StartWorkers : MinFloor());
            procFloor = Math.Min(procFloor, Math.Max(1, _autoscale.MaxWorkers));
            Volatile.Write(ref _procFloor, procFloor);
            int startWorkers = procFloor;
            for (int i = 0; i < startWorkers; i++) SpawnWorker("start");

            // ---- Saturation-driven marginal-utility hill-climb (unchanged autoscaler logic) ----
            CancellationTokenSource autoscaleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task autoscaler = Task.Run(async () =>
            {
                CancellationToken at = autoscaleCts.Token;
                int floor = MinFloor();

                double CurrentThroughput()
                {
                    try { return _instr.Snapshot().thruMBps; } catch { return 0.0; }
                }

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

                double throughputBeforeLastScaleUp = 0.0;
                DateTime lastScaleUpTime = DateTime.MinValue;
                bool awaitingScaleUpEvaluation = false;
                bool scaleUpDisallowed = false;
                DateTime brakeEngagedTime = DateTime.MinValue;
                DateTime lastScale = DateTime.UtcNow - TimeSpan.FromMilliseconds(_autoscale.CooldownMs);

                double gainThreshold = Math.Max(_autoscale.ThroughputImprovementRatio, _autoscale.MinScaleUpGainFraction * 0.5);
                double reprobeMs = Math.Max(2000, _autoscale.ScaleEvaluationMs * 5);

                try
                {
                    for (int _w = 0; _w < _autoscale.ScaleEvaluationMs && !at.IsCancellationRequested; _w += 25)
                        await Task.Delay(25).ConfigureAwait(false);

                    while (!at.IsCancellationRequested && WorkPending())
                    {
                        int workers = Volatile.Read(ref _currentWorkers);
                        long pressure = Pressure();
                        long readyHeld = Interlocked.Read(ref _readyHeld);
                        bool cooldownElapsed = (DateTime.UtcNow - lastScale).TotalMilliseconds > _autoscale.CooldownMs;

                        bool sawProcSaturation = Interlocked.Exchange(ref _procSaturationStall, 0) == 1;

                        if (_autoscale.EnableMarginalUtilityScaling && awaitingScaleUpEvaluation &&
                            (DateTime.UtcNow - lastScaleUpTime).TotalMilliseconds >= _autoscale.ScaleEvaluationMs)
                        {
                            double now = CurrentThroughput();
                            double baseline = Math.Max(0.0001, throughputBeforeLastScaleUp);
                            double gainRatio = (now - throughputBeforeLastScaleUp) / baseline;
                            if (gainRatio < gainThreshold)
                            {
                                scaleUpDisallowed = true;
                                brakeEngagedTime = DateTime.UtcNow;
                                long busy = Interlocked.Read(ref _processingInFlight);
                                bool fullySaturated = busy >= workers;
                                bool regressed = gainRatio < -_autoscale.ThroughputImprovementRatio;
                                if ((regressed || !fullySaturated) && workers > Volatile.Read(ref _procFloor))
                                    RequestShrink();
                            }
                            awaitingScaleUpEvaluation = false;
                        }

                        if (scaleUpDisallowed && !awaitingScaleUpEvaluation
                            && (DateTime.UtcNow - brakeEngagedTime).TotalMilliseconds >= reprobeMs)
                            scaleUpDisallowed = false;

                        bool outCongested = readyHeld >= workers;
                        bool procBound = pressure > _autoscale.HighWater || sawProcSaturation;

                        int desired = workers;
                        if (procBound && workers < _autoscale.MaxWorkers && cooldownElapsed
                            && !outCongested && !(_autoscale.EnableMarginalUtilityScaling && scaleUpDisallowed))
                        {
                            desired = workers + 1;
                            lastScale = DateTime.UtcNow;
                            throughputBeforeLastScaleUp = CurrentThroughput();
                            lastScaleUpTime = DateTime.UtcNow;
                            awaitingScaleUpEvaluation = true;
                        }
                        else
                        {
                            int shrinkFloor = (outCongested || !procBound) ? floor : procFloor;
                            bool lowPressure = pressure < _autoscale.LowWater;
                            if ((lowPressure || outCongested) && workers > shrinkFloor && cooldownElapsed)
                            {
                                desired = workers - 1;
                                lastScale = DateTime.UtcNow;
                                scaleUpDisallowed = false;
                                awaitingScaleUpEvaluation = false;
                            }
                        }

                        if (desired > workers) SpawnWorker(sawProcSaturation ? "scale-up: proc-saturated" : "scale-up: proc-bound");
                        else if (desired < workers)
                        {
                            int reqFloor = (outCongested || !procBound) ? floor : procFloor;
                            lock (scaleLock)
                            {
                                if (pendingStops == 0 && Volatile.Read(ref _currentWorkers) > reqFloor)
                                    pendingStops = 1;
                            }
                        }

                        for (int _w = 0; _w < _autoscale.SampleIntervalMs && !at.IsCancellationRequested; _w += 25)
                            await Task.Delay(25).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
            }, autoscaleCts.Token);

            // ---- Dedicated ordered writer (LongRunning) ----
            // ManualResetEventSlim replaces TCS + Task.WhenAny(wake, Task.Delay(25)) for
            // immediate wake when workers publish a section.
            Task writerTask = Task.Factory.StartNew(() =>
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
                            // every created section has been emitted.
                            if (producer.IsCompleted
                                && Interlocked.Read(ref _processingInFlight) == 0
                                && Interlocked.Read(ref _queueOccupancy) == 0
                                && Interlocked.Read(ref _readyHeld) == 0
                                && Interlocked.Read(ref _nextToComplete) > Interlocked.Read(ref sequence))
                                return;

                            writerWake.Reset();
                            // Double-check after reset to avoid missing a section published between
                            // the failed _ready lookup and the Reset.
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
                                writerWake.Wait(25, token); // wakes immediately on Set(); 25 ms backstop
                                token.ThrowIfCancellationRequested();
                                continue;
                            }
                        }

                        // Complete outside the lock.
                        Stopwatch sw = Stopwatch.StartNew();
                        try
                        {
                            SectionCompleteResult result = completer.CompleteAsync(context, sec, token)
                                                                    .GetAwaiter().GetResult();
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
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

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
                            await Task.Delay(_metricsIntervalMs).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                }, mt);
            }

            try
            {
                // Producer + pre-process finish first; that completes preProcToWorkers, so every
                // worker's TryTake eventually sees IsCompleted and exits.
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
                try { readToPreProc.Dispose(); } catch { }
                try { preProcToWorkers.Dispose(); } catch { }
                try { admitWake.Dispose(); } catch { }
                try { writerWake.Dispose(); } catch { }
            }

            if (firstError != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();

            await completer.FinalizeAsync(context, token).ConfigureAwait(false);
        }

        // inFlight release indirection: set at the top of ProcessAsync to a lambda that
        // decrements _liveSections and signals the admission gate.
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
