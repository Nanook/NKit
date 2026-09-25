using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Nanook.NKit.Steps.Shared
{
    internal class CircularSequenceQueue<T>
    {
        private class CsqThread<U>
        {
            public int Idx;
            public T Item;
            public bool Complete;
        }

        private readonly Action<T> _processItem;
        private readonly Action<T> _completeItem;
        private CsqThread<T>[] _q;
        private int _sIdx;
        private int _eIdx;
        private int _diff;
        private object _lock;

        [DebuggerStepThrough]
        public CircularSequenceQueue(IEnumerable<T> poolItems, Action<T> processItem, Action<T> completeItem)
        {
            _sIdx = 0;
            _eIdx = 0;
            _diff = 0;
            _lock = new object();
            IsComplete = false;
            _processItem = processItem;
            _completeItem = completeItem;
            T[] items = poolItems.Skip(1).ToArray();
            FillItem = poolItems.First();
            ItemCount = items.Length;
            _q = new CsqThread<T>[ItemCount];
            for (int i = 0; i < ItemCount; i++)
                _q[i] = new CsqThread<T>() { Complete = false, Item = items[i], Idx = i };
        }

        public T FillItem { get; set; }
        public int ItemCount { get; }
        public bool IsComplete { get; private set; }


        [DebuggerStepThrough]
        private void itemComplete(CsqThread<T> itm)
        {
            _processItem(itm.Item);
            itm.Complete = true;

            lock (_lock)
            {
                while (itm.Idx == _sIdx && itm.Complete) //next in sequence
                {
                    _completeItem(itm.Item);
                    itm.Complete = false;
                    _diff--;
                    _sIdx++;
                    if (_sIdx == ItemCount)
                        _sIdx = 0;
                    itm = _q[_sIdx];
                    Monitor.Pulse(_lock);
                }
            }
        }

        [DebuggerStepThrough]
        public void ItemComplete()
        {
            lock (_lock)
            {
                if (IsComplete)
                    return;

                while (true)
                {
                    if (_diff < ItemCount)
                    {
                        CsqThread<T> itm = _q[_eIdx];

                        T tmp = itm.Item; //swap the fill item in
                        itm.Item = FillItem;
                        FillItem = tmp;

                        _diff++;
                        _eIdx++;
                        if (_eIdx == ItemCount)
                            _eIdx = 0;
                        itm.Complete = false;
                        ThreadPool.QueueUserWorkItem(a => itemComplete(itm));
                        break;
                    }
                    else
                        Monitor.Wait(_lock); //wait for a slot
                }
            }
        }

        /// <summary>
        /// No more items to be set
        /// </summary>
        [DebuggerStepThrough]
        public void Complete()
        {
            lock (_lock)
            {
                while (_diff != 0)
                    Monitor.Wait(_lock); //wait for a slot
                IsComplete = true;
            }
        }

    }
}