using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit.Steps.Shared
{
    internal class CircularBlockingQueue<T>
    {

        private readonly Action<T> _processItem;
        private readonly Action<T> _completeItem;
        private BlockingCollection<T> _bc; //leave one to write out while blocking
        private object _lock;
        private T[] _buffs;
        private List<T> _completed;
        private int _idx;

        [DebuggerStepThrough]
        public CircularBlockingQueue(IEnumerable<T> poolItems, Action<T> processItem, Action<T> completeItem)
        {
            _lock = new object();
            IsComplete = false;
            _processItem = processItem;
            _completeItem = completeItem;
            _buffs = poolItems.ToArray();
            FillItem = _buffs[0];
            _completed = new List<T>();
            ItemCount = _buffs.Length;
            _bc = new BlockingCollection<T>(_buffs.Length - 2);
            _idx = 1;

            Task.Run(() =>
            {
                T itm;
                lock (_lock)
                {
                    while (true)
                    {
                        while (!IsComplete && _bc.Count == 0)
                            Monitor.Wait(_lock);
                        if (IsComplete && _bc.Count == 0)
                            break;

                        itm = _bc.Take();

                        while (!_completed.Contains(itm))
                            Monitor.Wait(_lock);

                        _completeItem(itm);
                        _completed.Remove(itm);
                    }
                }
            });

        }

        public T FillItem { get; set; }
        public int ItemCount { get; }
        public bool IsComplete { get; private set; }


        [DebuggerStepThrough]
        public void ProcessItem()
        {
            _bc.Add(FillItem);
            ThreadPool.QueueUserWorkItem(a =>
            {
                _processItem((T)a);

                lock (_lock)
                {
                    _completed.Add((T)a);
                    Monitor.Pulse(_lock);
                }
            }, FillItem);
            FillItem = _buffs[_idx++]; //next item
            if (_idx >= _buffs.Length)
                _idx = 0;
        }

        [DebuggerStepThrough]
        public void Complete()
        {
            do
            {
                lock (_lock)
                {
                    IsComplete = true;
                    Monitor.Pulse(_lock);
                }
            }
            while (_bc.Count != 0);
        }

    }
}