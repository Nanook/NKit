using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    // Fixed-parallelism work queue backed by dedicated worker threads.
    //
    // The previous implementation dispatched work items via ThreadPool.QueueUserWorkItem and
    // wrapped Process() in Task.Run. When called from a thread-pool context (e.g. the NKit
    // pipeline's producer task) with MaxParallel=10, up to 11 thread-pool threads were needed
    // simultaneously. Combined with 15 pipeline worker tasks also on the thread pool this caused
    // thread-pool starvation: QueueUserWorkItem callbacks could not start, _inFlight never
    // reached zero, and the caller's .Wait() blocked forever — reproducing as a pipeline hang
    // at ~99% of a conversion.
    //
    // Fix: MaxParallel dedicated Thread instances are created at construction and reused across
    // all Process() calls. They never compete with the thread pool. Process() returns a Task
    // that completes (via TaskCompletionSource) when the last worker finishes its item after
    // AddComplete() has been called. The caller's existing .Wait() on that Task is correct and
    // harmless — it unblocks as soon as the last item is processed.
    internal class BlockingThreadQueue<T>
    {
        private class WorkItem
        {
            public T Object;
            public uint Index;
            public int ThreadIndex;
        }

        private class FinaliseSlot
        {
            public WorkItem Item;
            public long ExpectedIndex;
        }

        // ── Shared state ──────────────────────────────────────────────────────────────

        private readonly object _lq = new object();   // guards _q, _complete, _batchActive
        private readonly object _lp = new object();   // guards _inFlight
        private readonly object _lf = new object();   // guards finalise slots

        private readonly Queue<WorkItem> _q = new Queue<WorkItem>();
        private volatile bool _complete;    // no more items will be Added in this batch
        private volatile bool _batchActive; // true between Init() and batch completion
        private volatile bool _shutdown;    // true when object is no longer needed

        private int _inFlight;              // items dispatched but not yet finished
        private long _enqueueCount;         // monotonic counter for ordering (per batch)
        private long _finaliseNext;         // next index the finalise thread expects (per batch)

        private Action<T, int> _process;
        private Action<T> _finalise;

        private TaskCompletionSource<bool> _batchTcs; // signalled when _inFlight hits 0 after _complete

        private readonly Thread[] _workers;
        private Thread _finaliseThread;
        private readonly FinaliseSlot[] _finaliseSlots;

        // ── Public surface ────────────────────────────────────────────────────────────

        public int MaxQueue { get; }
        public int MaxParallel { get; }
        public bool UseFinalise { get; }
        public int QueueCount { get { lock (_lq) return _q.Count; } }
        public int ProcessingCount { get { lock (_lp) return _inFlight; } }

        public BlockingThreadQueue(int maxQueue, int maxParallel, bool useFinalise)
        {
            this.MaxQueue = maxQueue;
            this.MaxParallel = maxParallel;
            this.UseFinalise = useFinalise;

            if (useFinalise)
            {
                _finaliseSlots = new FinaliseSlot[maxParallel];
                for (int i = 0; i < maxParallel; i++)
                    _finaliseSlots[i] = new FinaliseSlot();
            }

            _workers = new Thread[maxParallel];
            for (int i = 0; i < maxParallel; i++)
            {
                int idx = i;
                _workers[i] = new Thread(() => workerLoop(idx)) { IsBackground = true, Name = $"BlockingThreadQueue-{idx}" };
                _workers[i].Start();
            }

            if (useFinalise)
            {
                _finaliseThread = new Thread(finaliseLoop) { IsBackground = true, Name = "BlockingThreadQueue-Finalise" };
                _finaliseThread.Start();
            }
        }

        // Called by ImageBlockReader before each new batch of blocks.
        // Must be called before Process() and before the first Add().
        internal void Init()
        {
            lock (_lf)
            {
                if (_finaliseSlots != null)
                {
                    _finaliseNext = 0;
                    foreach (FinaliseSlot s in _finaliseSlots)
                        s.Item = null;
                }
            }

            lock (_lp)
            {
                _inFlight = 0;
            }

            lock (_lq)
            {
                _q.Clear();
                _complete = false;
                _batchActive = false;
                _enqueueCount = 0;
                _batchTcs = null;
                Monitor.PulseAll(_lq);
            }
        }

        // Registers the process (and optional finalise) delegate for this batch and returns a
        // Task that completes when every item added via Add()/AddComplete() has been processed.
        // The caller adds items AFTER calling Process(), then calls AddComplete(), then awaits
        // or .Wait()s the returned Task — matching ImageBlockReader's existing pattern exactly.
        public Task Process(Action<T, int> process, Action<T> finalise)
        {
            if (this.UseFinalise && finalise == null)
                throw new Exception("finalise cannot be null when UseFinalise is true");

            _process = process;
            _finalise = finalise;

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lq)
            {
                _batchTcs = tcs;
                _batchActive = true;
                Monitor.PulseAll(_lq); // wake workers in case items arrive before they check
            }

            return tcs.Task;
        }

        public void Add(T item)
        {
            lock (_lq)
            {
                if (_complete)
                    throw new Exception("Items cannot be added after AddComplete");

                while (_q.Count == this.MaxQueue)
                    Monitor.Wait(_lq);

                WorkItem wi = new WorkItem { Object = item, Index = (uint)_enqueueCount++, ThreadIndex = -1 };
                _q.Enqueue(wi);
                lock (_lp)
                    _inFlight++;

                Monitor.PulseAll(_lq);
            }
        }

        // Permanently stop the worker (and finalise) threads and release them. After this the queue
        // can no longer process batches. Idempotent and safe to call from Dispose/Release on the
        // owner (ImageBlockReader). Without this the MaxParallel dedicated background threads created
        // in the constructor block forever on Monitor.Wait and are never reclaimed — one set per
        // ImageBlockReader (so one set per image container), which across a long-lived host (the UI,
        // or a batch CLI run) leaks ~MaxParallel threads and their sync handles per image.
        public void Shutdown()
        {
            // Flip the flag and wake every thread blocked in workerLoop/finaliseLoop so they observe
            // _shutdown and return. Pulse under BOTH locks because the two loops wait on different
            // monitors (_lq for workers, _lf for the finalise thread).
            lock (_lq)
            {
                _shutdown = true;
                Monitor.PulseAll(_lq);
            }
            if (_finaliseSlots != null)
            {
                lock (_lf)
                    Monitor.PulseAll(_lf);
            }

            // Join so the threads are fully torn down before we return (bounded, defensive — the
            // loops exit promptly once pulsed; never join the current thread).
            if (_workers != null)
            {
                foreach (Thread t in _workers)
                {
                    try { if (t != null && t.IsAlive && t != Thread.CurrentThread) t.Join(2000); }
                    catch { /* never throw from teardown */ }
                }
            }
            try { if (_finaliseThread != null && _finaliseThread.IsAlive && _finaliseThread != Thread.CurrentThread) _finaliseThread.Join(2000); }
            catch { /* never throw from teardown */ }
        }

        public void AddComplete()
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lq)
            {
                _complete = true;
                Monitor.PulseAll(_lq); // wake workers so they see _complete when queue drains
            }
            // If no items were ever added (or all finished before AddComplete was called),
            // _inFlight is already 0 — signal completion now.
            lock (_lp)
            {
                if (_inFlight == 0)
                    tcs = _batchTcs;
            }
            tcs?.TrySetResult(true);
        }

        // ── Worker thread loop ────────────────────────────────────────────────────────

        private void workerLoop(int threadIndex)
        {
            while (true)
            {
                WorkItem wi = null;

                lock (_lq)
                {
                    while (!_shutdown)
                    {
                        if (_batchActive && _q.Count > 0)
                            break;
                        Monitor.Wait(_lq);
                    }
                    if (_shutdown)
                        return;

                    wi = _q.Dequeue();
                    wi.ThreadIndex = threadIndex;
                    Monitor.PulseAll(_lq); // wake Add() if it was blocked on MaxQueue
                }

                try
                {
                    _process((T)wi.Object, threadIndex);
                }
                finally
                {
                    if (this.UseFinalise)
                        enqueueForFinalise(wi);
                    else
                        decrementInFlight(wi);
                }
            }
        }

        private void decrementInFlight(WorkItem wi)
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lp)
            {
                _inFlight--;
                if (_inFlight == 0)
                {
                    // Only signal completion once _complete is set (no more items will arrive).
                    // If _complete is not yet set, the last item could arrive after we check —
                    // AddComplete() will not call us again, so we re-check inside Add's lock path.
                    // In practice _inFlight can only reach 0 after _complete because Add() increments
                    // _inFlight before the worker can decrement it.
                    if (_complete)
                        tcs = _batchTcs;
                }
            }
            tcs?.TrySetResult(true);
        }

        // ── Finalise thread loop ──────────────────────────────────────────────────────

        private void enqueueForFinalise(WorkItem wi)
        {
            lock (_lf)
            {
                FinaliseSlot slot = _finaliseSlots.First(s => s.Item == null);
                slot.Item = wi;
                slot.ExpectedIndex = wi.Index;
                Monitor.PulseAll(_lf);
            }
        }

        private void finaliseLoop()
        {
            while (true)
            {
                WorkItem wi = null;

                lock (_lf)
                {
                    while (!_shutdown)
                    {
                        FinaliseSlot slot = _finaliseSlots.FirstOrDefault(s => s.Item != null && s.ExpectedIndex == _finaliseNext);
                        if (slot != null)
                        {
                            wi = slot.Item;
                            slot.Item = null;
                            _finaliseNext++;
                            break;
                        }
                        Monitor.Wait(_lf, 50);
                    }
                    if (_shutdown)
                        return;
                }

                if (wi != null)
                {
                    try
                    {
                        _finalise((T)wi.Object);
                    }
                    finally
                    {
                        decrementInFlight(wi);
                    }
                }
            }
        }
    }
}
