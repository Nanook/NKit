using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Nanook.NKit.Steps.Shared
{
    // Ordered parallel queue backed by dedicated worker threads.
    //
    // The previous implementation dispatched work via ThreadPool.QueueUserWorkItem. When
    // ItemComplete() is called from a thread that is itself on (or blocking) the thread pool
    // (e.g. the NKit pipeline completer/writer task), and the pipeline workers are also on
    // the thread pool, the pool can saturate: QueueUserWorkItem callbacks never run, Complete()
    // blocks forever on Monitor.Wait, and the pipeline hangs.
    //
    // Fix: ItemCount dedicated Thread instances are created at construction. Each thread waits
    // on its own slot semaphore, processes its item, then signals the in-order completer.
    // No thread-pool involvement — no starvation risk.
    internal class CircularSequenceQueue<T>
    {
        private class Slot
        {
            public int       Idx;
            public T         Item;
            public volatile bool Complete;
            public SemaphoreSlim Ready = new SemaphoreSlim(0, 1); // signalled when work is available
        }

        private readonly Action<T> _processItem;
        private readonly Action<T> _completeItem;
        private readonly Slot[]    _slots;
        private readonly Thread[]  _workers;

        private int  _sIdx;   // next slot to complete in order
        private int  _eIdx;   // next slot to fill
        private int  _diff;   // slots currently in flight
        private readonly object _lock = new object();

        private volatile bool _shutdown;

        [DebuggerStepThrough]
        public CircularSequenceQueue(IEnumerable<T> poolItems, Action<T> processItem, Action<T> completeItem)
        {
            _sIdx = 0;
            _eIdx = 0;
            _diff = 0;
            IsComplete = false;
            _processItem = processItem;
            _completeItem = completeItem;

            T[] items = poolItems.Skip(1).ToArray();
            FillItem = poolItems.First();
            ItemCount = items.Length;

            _slots = new Slot[ItemCount];
            for (int i = 0; i < ItemCount; i++)
                _slots[i] = new Slot { Idx = i, Item = items[i], Complete = false };

            _workers = new Thread[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                int idx = i;
                _workers[i] = new Thread(() => workerLoop(idx)) { IsBackground = true, Name = $"CircularSeqQueue-{idx}" };
                _workers[i].Start();
            }
        }

        public T    FillItem  { get; set; }
        public int  ItemCount { get; }
        public bool IsComplete { get; private set; }

        // Called by the producer (completer thread) to submit the current FillItem for
        // parallel processing. Blocks only if all slots are in use (backpressure).
        [DebuggerStepThrough]
        public void ItemComplete()
        {
            lock (_lock)
            {
                if (IsComplete)
                    return;

                while (_diff == ItemCount)
                    Monitor.Wait(_lock); // wait for a slot to free up

                Slot slot = _slots[_eIdx];

                // Swap the fill item into this slot; caller gets the slot's previous item back
                T tmp      = slot.Item;
                slot.Item  = FillItem;
                FillItem   = tmp;

                slot.Complete = false;
                _diff++;
                _eIdx++;
                if (_eIdx == ItemCount)
                    _eIdx = 0;

                slot.Ready.Release(); // wake the dedicated worker for this slot
            }
        }

        // Wait for all in-flight items to finish, in order.
        [DebuggerStepThrough]
        public void Complete()
        {
            lock (_lock)
            {
                while (_diff != 0)
                    Monitor.Wait(_lock);
                IsComplete = true;
            }
        }

        // ── Dedicated worker loop ─────────────────────────────────────────────────────

        private void workerLoop(int idx)
        {
            Slot slot = _slots[idx];
            while (true)
            {
                slot.Ready.Wait(); // block until ItemComplete() signals this slot

                if (_shutdown)
                    return;

                _processItem(slot.Item);
                slot.Complete = true;

                // Drain the in-order completion chain from this slot forward.
                lock (_lock)
                {
                    while (_slots[_sIdx].Complete)
                    {
                        Slot s = _slots[_sIdx];
                        _completeItem(s.Item);
                        s.Complete = false;
                        _diff--;
                        _sIdx++;
                        if (_sIdx == ItemCount)
                            _sIdx = 0;
                        Monitor.Pulse(_lock); // wake ItemComplete() or Complete() if waiting
                    }
                }
            }
        }
    }
}
