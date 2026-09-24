using NKitCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using C2 = NKitCore;

namespace Nanook.NKit.Engine.Core
{
    /// <summary>
    /// Runner for the NKitCore pipeline (worker-drains-in-order output with a dedicated ordered
    /// writer; no backlog / no separate completion loop). Binds the NKit stage adapters to the
    /// NKitCore.* interfaces. The adapters are C2-prefixed for historical reasons.
    /// </summary>
    internal sealed class NKitCoreRunner
    {
        private readonly IImage _image;
        private readonly IInput _input;
        private readonly NKitStepContext _stepContext;
        private readonly TheOverseer _overseer;
        private readonly IBufferPreProcessor _preProcessor;
        private readonly Action<EngineStats> _statsCallback;

        public NKitCoreRunner(IImage image, IInput input, NKitStepContext stepContext, TheOverseer overseer, IBufferPreProcessor preProcessor, Action<EngineStats> statsCallback = null)
        {
            _image = image;
            _input = input;
            _stepContext = stepContext;
            _overseer = overseer;
            _preProcessor = preProcessor;
            _statsCallback = statsCallback;
        }

        public IEnumerable<Nanook.NKit.ISection> Run()
        {
            CancellationToken ct = _stepContext.CancelToken ?? CancellationToken.None;

            // SectionProcessors + buffers are EXPENSIVE to construct and are meant to be created once
            // and reused (the legacy engine kept one per worker slot). Count creations so a churn bug
            // (pool ceiling too small => drop + recreate) is visible in telemetry.
            long processorsCreated = 0;
            Func<C2.ISection> sectionFactory = () =>
            {
                Interlocked.Increment(ref processorsCreated);
                ISectionProcessor sp = _image.CreateSectionProcessor();
                sp.Buffer = _image.CreateBuffer();
                return new C2Section(sp);
            };

            // ---- Autoscale + window config (computed BEFORE the pool so the pool is sized to the
            // maximum number of sections that can be live at once). ----
            // Hard SAFETY ceiling only (not a target) — intentionally NOT the logical core count.
            // Processing is often I/O/decode-latency bound, so useful concurrency can exceed the
            // core count; the autoscaler's throughput brake (not this ceiling) governs the real
            // worker count and typically settles well below it.
            int maxWorkers = NKitCoreConsts.MaxWorkers;
            if (maxWorkers < 1) maxWorkers = 1;

            // Worker count is fully autoscaled — there is no user "parallelism" knob any more. Start
            // at a sensible concurrency (the logical-processor count, clamped to the ceiling) so the
            // pool is warm from the outset; the autoscaler climbs above this when it clearly helps and
            // brakes on any throughput regression, so the ceiling is never a real constraint.
            int startWorkers = Environment.ProcessorCount;
            if (startWorkers < 1) startWorkers = 1;
            if (startWorkers > maxWorkers) startWorkers = maxWorkers;

            int lookAhead = NKitCoreConsts.LookAhead;
            if (lookAhead < 1) lookAhead = 1;

            // The pool free-list MUST be at least the maximum number of sections that can be live at
            // once (window = maxWorkers + lookAhead), plus a small margin. If it were smaller, returned
            // processors would be dropped and then re-created on the next Rent — recreating the
            // expensive SectionProcessor/buffer every cycle. Sizing it to the window guarantees each
            // processor is constructed ONCE and reused for the whole run, matching the legacy design.
            int maxPoolFree = maxWorkers + lookAhead + NKitCoreConsts.PoolFreeMargin;

            C2ImageStream stream = new C2ImageStream(_image);
            C2SectionContext context = new C2SectionContext(_stepContext, _image.SectionSize, sectionFactory, ct, maxPoolFree);

            BlockingCollection<C2Section> completed = new BlockingCollection<C2Section>(
                new ConcurrentQueue<C2Section>(), Math.Max(2, startWorkers * 2));

            // Engine-stage Detail scopes ([In]/[Pre]/[Out]; [Proc#N] built per-worker inside the
            // process adapter). Resolved from the Log's prefix-scope cache; null-safe when no Log.
            ILogScope stageLog = _stepContext?.Log;
            ILogScope inScope = stageLog?.ScopeFor(LogScopes.In);
            ILogScope preScope = stageLog?.ScopeFor(LogScopes.Pre);
            ILogScope outScope = stageLog?.ScopeFor(LogScopes.Out);
            // Worker scale up/down is cross-cutting engine info: no pipeline STAGE scope — it uses
            // a "Core"-prefixed scope so it renders as "[Core]".
            ILogScope poolScope = stageLog?.ScopeFor(LogScopes.Core);

            C2SectionFactory factory = new C2SectionFactory(_image, _input, stream, inScope);
            C2PreProcessAdapter pre = new C2PreProcessAdapter(_preProcessor, preScope);
            C2ProcessAdapter proc = new C2ProcessAdapter();
            C2CompleterAdapter completer = new C2CompleterAdapter(_stepContext, _overseer, sec => completed.Add(sec, ct), outScope);

            StreamBlockCore<C2Section, C2SectionContext>.AutoscaleOptions autoscale = new C2.StreamBlockCore<C2Section, C2SectionContext>.AutoscaleOptions
            {
                MinWorkers = NKitCoreConsts.MinWorkers,
                StartWorkers = startWorkers,
                MaxWorkers = maxWorkers,
                LookAheadDepth = lookAhead,
                SampleIntervalMs = NKitCoreConsts.SampleIntervalMs,
                IdleWorkerMinMs = NKitCoreConsts.IdleWorkerMinMs,
            };

            DateTime started = DateTime.UtcNow;
            string statsFile = NKitCoreConsts.StatsFile;
            // "cap" is the HARD SAFETY CEILING (logical core count), not a target — the autoscaler
            // climbs on throughput/saturation and stops itself well before this in most workloads.
            // "procs" is how many SectionProcessors have been CONSTRUCTED: it must settle at roughly
            // the window size (maxWorkers + lookAhead) and then stop growing. If it keeps climbing,
            // the pool is churning expensive processors (a perf bug).
            int metricsIntervalMs = NKitCoreConsts.StatsIntervalMs;

            StreamBlockCore<C2Section, C2SectionContext> core = new C2.StreamBlockCore<C2Section, C2SectionContext>(
                maxParallelism: maxWorkers,
                autoscaleOptions: autoscale,
                metricsSink: snap =>
                {
                    TraceStats(snap, started, statsFile, $"core cap={maxWorkers} procs={Interlocked.Read(ref processorsCreated)}");
                    RaiseStats(snap);
                },
                metricsIntervalMs: metricsIntervalMs,
                workerEvent: evt => LogWorkerEvent(poolScope, evt));

            Task run = Task.Run(async () =>
            {
                try
                {
                    await core.ProcessAsync(context, stream, factory, pre, proc, completer, ct).ConfigureAwait(false);
                }
                finally
                {
                    completed.CompleteAdding();
                }
            }, ct);

            try
            {
                foreach (C2Section sec in completed.GetConsumingEnumerable())
                    yield return sec.Processor;

                run.GetAwaiter().GetResult();
            }
            finally
            {
                // Deterministically release the per-image section pool + buffers the moment this
                // image's run ends (normal completion, fault, or the consumer abandoning the
                // enumeration early via iterator Dispose). Without this the pooled 2 MiB section
                // buffers linger in the free-list until GC, which under a large multi-image run
                // (e.g. thousands of nkit.gcz verifies) stacks up as heap/LOH pressure.
                try { context.Dispose(); } catch { }
                // The BlockingCollection is created per image and owns internal SemaphoreSlim wait
                // handles (OS handles). Dispose it so those handles are released at image completion
                // rather than leaking one set per image across a long-lived host (the UI) — handle
                // and working-set creep even when the managed heap stays flat.
                try { completed.Dispose(); } catch { }
            }
        }

        // Convert the engine snapshot into the public EngineStats DTO and raise it to the host UI.
        // Stage mapping (honest to what the engine measures):
        //   In   = SectionCreate        (reading / creating the section from the image)
        //   Pre  = ProducerToProcess    (producer->process handoff; PreDecrypt/DiscoverMarkers/GetFiles)
        //   Proc = Process              (parallel section processing)
        //   Out  = Out                  (ordered output / write)
        // Log a worker-pool lifecycle transition as a [Pool] Detail line, e.g.:
        //   [Pool:DataProcessing] Worker 5 spawned (pool 5) - scale-up: proc-bound
        //   [Pool:DataProcessing] Worker 5 retired (pool 4) - scale-down
        // WorkerId is the STABLE logical id (not a thread id), so the count reflects real workers.
        private static void LogWorkerEvent(ILogScope pool, C2.StreamBlockCore<C2Section, C2SectionContext>.WorkerEvent evt)
        {
            if (pool == null || !pool.IsEnabled(LogLevel.Detail)) return;
            string verb;
            switch (evt.Kind)
            {
                case C2.StreamBlockCore<C2Section, C2SectionContext>.WorkerEventKind.Spawned: verb = "spawned"; break;
                case C2.StreamBlockCore<C2Section, C2SectionContext>.WorkerEventKind.Retired: verb = "retired"; break;
                default: verb = "ended"; break;
            }
            // pool is a "Core"-prefixed scope → renders as "[Core]".
            pool.Log(LogLevel.Detail,
                $"Worker {evt.WorkerId} {verb} (pool {evt.CurrentWorkers})"
                + (string.IsNullOrEmpty(evt.Reason) ? "" : $" - {evt.Reason}"));
        }

        private void RaiseStats(C2.StreamBlockCore<C2Section, C2SectionContext>.InstrumentationSnapshot snap)
        {
            if (snap == null || _statsCallback == null)
                return;
            try
            {
                double f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage s)
                    => (snap.StageFractions != null && snap.StageFractions.TryGetValue(s, out double v)) ? v : 0.0;

                _statsCallback(new EngineStats
                {
                    ThroughputMBps = snap.ThroughputMBps,
                    InFraction = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.SectionCreate),
                    PreFraction = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.ProducerToProcess),
                    ProcFraction = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.Process),
                    OutFraction = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.Out),
                    BusyWorkers = (int)snap.ProcessingInFlight,
                    TotalWorkers = snap.CurrentWorkers
                });
            }
            catch { }
        }

        private static void TraceStats(C2.StreamBlockCore<C2Section, C2SectionContext>.InstrumentationSnapshot snap, DateTime started, string statsFile, string cfgTag)
        {
            if (snap == null) return;
            try
            {
                double f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage s)
                    => (snap.StageFractions != null && snap.StageFractions.TryGetValue(s, out double v)) ? v : 0.0;

                double inFrac = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.SectionCreate)
                              + f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.ProducerToProcess);
                double procFrac = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.Process);
                double outFrac = f(C2.StreamBlockCore<C2Section, C2SectionContext>.Stage.Out);

                int workers = Math.Max(1, snap.CurrentWorkers);
                double util = Math.Min(1.0, (double)snap.ProcessingInFlight / workers);
                double elapsed = (DateTime.UtcNow - started).TotalSeconds;

                // Workers is shown as busy/total: ProcessingInFlight (workers currently inside
                // ProcessAsync) over CurrentWorkers (live pool). Both are lock-free interlocked reads.
                string workersCol = snap.ProcessingInFlight + "/" + snap.CurrentWorkers;
                string line = string.Format(
                    "[NKitCore {0,6:F1}s] {1,7:F1} MB/s  Util {2,4:P0}  In {3,5:P0}  Proc {4,5:P0}  Out {5,5:P0}  Workers {6,5}  Queue {7}  Ready {8}  Total {9:F1} MB  [{10}]",
                    elapsed, snap.ThroughputMBps, util, inFrac, procFrac, outFrac,
                    workersCol, snap.QueueLength, snap.ReadyHeld, snap.TotalProcessedMB, cfgTag);

                // Per-interval core stats are OFF by default (they flood the VS Output/Debug window).
                // Opt in for perf debugging by setting NKitCoreConsts.StatsConsole (console echo) or
                // NKitCoreConsts.StatsFile (file append). The Trace line is gated on the same flag so
                // nothing is emitted unless stats are explicitly enabled.
                if (NKitCoreConsts.StatsConsole)
                {
                    Trace.WriteLine(line);
                    try { Console.WriteLine(line); } catch { }
                }
                if (!string.IsNullOrEmpty(statsFile))
                {
                    try { System.IO.File.AppendAllText(statsFile, line + System.Environment.NewLine); } catch { }
                }
            }
            catch { }
        }
    }

    // ── C2 stage adapters (bind NKit types to the NKitCore.* interfaces) ─────────────────────────

    internal sealed class C2Section : C2.ISection
    {
        public C2Section(ISectionProcessor processor) { this.Processor = processor; }
        public ISectionProcessor Processor { get; }
        public long SequenceNumber { get; set; }
        public int AreaIndex { get; set; }
        public long ImageOffset { get; set; }
        public long AreaOffset { get; set; }
        public bool IsLastInArea { get; set; }
        public bool IsLastInStream { get; set; }
        public int Length { get; set; }
        public void Reset()
        {
            SequenceNumber = 0; AreaIndex = 0; ImageOffset = 0; AreaOffset = 0;
            IsLastInArea = false; IsLastInStream = false; Length = 0;
        }
    }

    internal sealed class C2SectionContext : C2.DefaultSectionContext
    {
        private readonly ConcurrentDictionary<int, IFileSystemData> _areaFileSystems = new ConcurrentDictionary<int, IFileSystemData>();

        public C2SectionContext(NKitStepContext stepContext, int sectionSize, Func<C2.ISection> sectionFactory, CancellationToken cancellation, int maxPoolFree)
            : base(Guid.NewGuid(), sectionSize, sectionFactory, cancellation, maxPoolFree)
        {
            this.StepContext = stepContext;
        }

        public NKitStepContext StepContext { get; }

        public void PublishAreaFileSystem(int areaIndex, IFileSystemData fsInfo)
        {
            if (fsInfo != null) _areaFileSystems[areaIndex] = fsInfo;
        }

        public IFileSystemData GetAreaFileSystem(int areaIndex)
            => _areaFileSystems.TryGetValue(areaIndex, out IFileSystemData fs) ? fs : null;

        // Drop the large per-section payload (the 2 MiB Decrypted/Encrypted Buffer arrays and the
        // parsed FileSystemData) as the pool is drained on Dispose, so image completion frees them
        // deterministically rather than leaving them rooted in the free-list until GC.
        protected override void ReleaseSection(C2.ISection section)
        {
            if (section is C2Section sec && sec.Processor != null)
            {
                sec.Processor.Buffer = null;
                sec.Processor.FileSystemData = null;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            _areaFileSystems.Clear();
        }
    }

    internal sealed class C2ImageStream : C2.IStream
    {
        private readonly List<C2.IArea> _areas = new List<C2.IArea>();

        public C2ImageStream(IImage image)
        {
            long size = image.Size;
            _areas.Add(new C2.Area(0, 0, size));
            this.Length = size;
        }

        public IReadOnlyList<C2.IArea> InitialAreas => _areas;
        public long? Length { get; }

        public C2.IArea GetOrAdd(int index, long imageOffset, long size)
        {
            lock (_areas)
            {
                for (int i = 0; i < _areas.Count; i++)
                    if (_areas[i].Index == index) return _areas[i];
                C2.Area a = new C2.Area(index, imageOffset, size);
                _areas.Add(a);
                return a;
            }
        }

        public Task<int> ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken ct)
            => throw new NotSupportedException("C2ImageStream is driven sequentially via IImage.Read in the factory.");
    }

    internal sealed class C2SectionFactory : C2.ISectionFactory
    {
        private readonly IImage _image;
        private readonly IInput _input;
        private readonly C2ImageStream _stream;
        private readonly ILogScope _log; // [In] stage scope (Detail)
        private int _lastAreaNo = -1;

        public C2SectionFactory(IImage image, IInput input, C2ImageStream stream, ILogScope log)
        {
            _image = image;
            _input = input;
            _stream = stream;
            _log = log;
        }

        public Task ConstructAsync(C2.ISectionContext ctx, C2.IStream stream, CancellationToken ct) => Task.CompletedTask;
        public Task SetupAsync(C2.ISectionContext ctx, C2.IStream stream, CancellationToken ct) => Task.CompletedTask;
        public Task SeekAsync(C2.ISectionContext ctx, C2.IStream stream, C2.SeekRequest req, CancellationToken ct) => Task.CompletedTask;

        public Task<C2.ISection> CreateNextSectionAsync(C2.ISectionContext ctx, C2.IStream stream, C2.IArea area, long offset, CancellationToken ct)
        {
            C2SectionContext context = (C2SectionContext)ctx;
            C2Section sec = (C2Section)ctx.RentSection();

            int read = _input.Read(sec.Processor.Buffer, out IFileSystemInfo fsInfo);
            if (read == 0)
            {
                ctx.ReturnSection(sec);
                return Task.FromResult<C2.ISection>(null);
            }

            IBuffer buf = sec.Processor.Buffer;
            AreaInfo ai = buf.AreaInfo;
            int areaNo = ai.AreaNo;
            long areaEnd = _image.CurrentAreaEndImageOffset;

            sec.ImageOffset = buf.ImageOffset;
            sec.AreaOffset = buf.AreaOffset;
            sec.Length = buf.Size;
            sec.AreaIndex = areaNo;
            sec.Processor.FileSystemData = fsInfo;

            bool newArea = buf.AreaOffset == 0 || areaNo != _lastAreaNo;
            if (newArea)
            {
                long areaSize = areaEnd - ai.ImageOffset;
                C2.IArea a = _stream.GetOrAdd(areaNo, ai.ImageOffset, areaSize);
                a.UpdateSize(areaSize);
                context.PublishAreaFileSystem(areaNo, fsInfo);
                _lastAreaNo = areaNo;

                // [In] Detail: per-area geometry, CONFIRMED once the boundary is resolved (coarse —
                // once per area, not per section; guarded so the hot path does nothing when off).
                // This is the IImage reader's area sizing/typing, distinct from the container decode
                // and from [Pre]'s filesystem-parse confirmation — helps localise a bug to the
                // reader's area walk. All values are already in hand (no extra work).
                if (_log != null && _log.IsEnabled(LogLevel.Detail))
                    _log.Log(LogLevel.Detail,
                        $"Area {areaNo} [{ai.Type}] offset 0x{ai.ImageOffset:X} size 0x{areaSize:X}"
                        + (fsInfo != null ? $" ptn {fsInfo.Type}" : "")
                        + (ai.IsEncryptionSupported ? " enc-on-disc" : "")
                        + (ai.IsEncrypted ? " enc-in-source" : ""));
            }

            sec.IsLastInArea = (buf.ImageOffset + buf.Size) >= areaEnd;
            sec.IsLastInStream = (buf.ImageOffset + buf.Size) >= _image.Size;

            if (sec.IsLastInArea && !sec.IsLastInStream)
                _stream.GetOrAdd(areaNo + 1, areaEnd, Math.Max(0, _image.Size - areaEnd));

            return Task.FromResult<C2.ISection>(sec);
        }
    }

    internal sealed class C2PreProcessAdapter : C2.ISectionPreProcess
    {
        private readonly IBufferPreProcessor _preProcessor;
        private readonly ILogScope _log; // [Pre] stage scope (Detail)
        private int _fstOutIndex;

        public C2PreProcessAdapter(IBufferPreProcessor preProcessor, ILogScope log)
        {
            _preProcessor = preProcessor;
            _log = log;
        }

        public Task PreProcessAsync(C2.ISectionContext ctx, C2.ISection section, CancellationToken ct)
        {
            C2Section sec = (C2Section)section;
            IBuffer buf = sec.Processor.Buffer;
            IFileSystemInfo fsInfo = (IFileSystemInfo)sec.Processor.FileSystemData;

            bool areaStart = buf.AreaOffset == 0;
            if (areaStart) _fstOutIndex = 0;

            _preProcessor.PreDecrypt(fsInfo, buf);
            _preProcessor.DiscoverMarkers(fsInfo, buf);
            if (fsInfo?.FileSystem != null)
                _preProcessor.GetFiles(fsInfo, buf, fsInfo.FileSystem.Files, ref _fstOutIndex);

            // [Pre] Detail: at each area start, report the resolved file count (coarse — per area).
            if (areaStart && _log != null && _log.IsEnabled(LogLevel.Detail))
            {
                int files = fsInfo?.FileSystem?.Files?.Count ?? 0;
                _log.Log(LogLevel.Detail,
                    $"Area {sec.AreaIndex} pre-process: FST files {files}");
            }

            return Task.CompletedTask;
        }

        public Task OnSeekAsync(C2.ISectionContext ctx, C2.SeekRequest req, CancellationToken ct)
        {
            _fstOutIndex = 0;
            return Task.CompletedTask;
        }
    }

    internal sealed class C2ProcessAdapter : C2.ISectionProcess
    {
        // Worker lifecycle is now reported accurately via the core's worker-event callback (the
        // [Pool] scope), keyed on the STABLE logical worker id. The former per-section [Proc#N] line
        // keyed on managed-thread-id was misleading (one logical worker hops thread-pool threads
        // across awaits, inflating the count), so it has been removed. Per-section logging would be
        // Trace-level and is out of scope here.
        public C2ProcessAdapter() { }

        public Task ProcessAsync(C2.ISectionContext ctx, C2.ISection section, CancellationToken ct)
        {
            ISectionProcessor p = ((C2Section)section).Processor;
            p.Update();
            p.Process();
            p.Complete();
            return Task.CompletedTask;
        }

        public Task OnSeekAsync(C2.ISectionContext ctx, C2.SeekRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    internal sealed class C2CompleterAdapter : C2.ISectionCompleter
    {
        private readonly NKitStepContext _stepContext;
        private readonly TheOverseer _overseer;
        private readonly Action<C2Section> _onCompleted;
        private readonly ILogScope _log; // [Out] stage scope (Detail)

        public C2CompleterAdapter(NKitStepContext stepContext, TheOverseer overseer, Action<C2Section> onCompleted, ILogScope log)
        {
            _stepContext = stepContext;
            _overseer = overseer;
            _onCompleted = onCompleted;
            _log = log;
        }

        public Task<C2.SectionCompleteResult> CompleteAsync(C2.ISectionContext ctx, C2.ISection section, CancellationToken ct)
        {
            C2Section sec = (C2Section)section;
            ISectionProcessor ns = sec.Processor;

            ns.PostProcess();
            _stepContext.Scan.SectionProcessed(ns);
            _overseer.SectionProcessed(ns);
            _stepContext.Step.Process(ns);

            // [Out] Detail: per-area completion (coarse — on the last section of an area, ordered).
            if (sec.IsLastInArea && _log != null && _log.IsEnabled(LogLevel.Detail))
                _log.Log(LogLevel.Detail,
                    $"Area {sec.AreaIndex} completed (emitted through 0x{sec.ImageOffset + sec.Length:X})");

            _onCompleted?.Invoke(sec);
            return Task.FromResult(C2.SectionCompleteResult.Continue());
        }

        public Task FinalizeAsync(C2.ISectionContext ctx, CancellationToken ct) => Task.CompletedTask;
    }
}