using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    internal class BlockingThreadQueue<T>
    {
        private class threadState
        {
            public ushort ThreadIndex;
            public object Object;
            public bool Allocated;
            public uint Index;
            public override string ToString() => $"{Index}";
        }

        private class finaliseState
        {
            public threadState ThreadState;
            public long FinaliseIndex;
            public override string ToString() => $"{FinaliseIndex} : {ThreadState?.Index}";
        }

        private readonly object _lq = new object();
        private readonly object _lp = new object();
        private readonly object _lf = new object();
        private Queue<T> _q;
        private volatile int _tc;
        private long _fc;
        private long _fp; //finalise process
        private bool _complete;

        private threadState[] _threadsState;
        private finaliseState[] _finaliseState;

        public BlockingThreadQueue(int maxQueue, int maxParallel, bool useFinalise)
        {
            _q = new Queue<T>();

            this.MaxQueue = maxQueue;
            this.MaxParallel = maxParallel;
            UseFinalise = useFinalise;
            _threadsState = new threadState[maxParallel];
            for (ushort i = 0; i < _threadsState.Length; i++)
                _threadsState[i] = new threadState() { ThreadIndex = i, Object = null };

            _finaliseState = new finaliseState[maxParallel];
            for (int i = 0; i < _finaliseState.Length; i++)
                _finaliseState[i] = new finaliseState() { FinaliseIndex = i, ThreadState = null };
        }

        public int MaxQueue { get; }
        public int MaxParallel { get; }
        public bool UseFinalise { get; }
        public int QueueCount => _q.Count;
        public int ProcessingCount => _tc;

        internal void Init()
        {
            _q.Clear();
            _tc = 0;
            _fc = 0;
            _fp = 0;
            for (int i = 0; i < _threadsState.Length; i++)
            {
                _threadsState[i].Allocated = false;
                _threadsState[i].Object = null;
                _threadsState[i].Index = 0;
            }

            for (int i = 0; i < _finaliseState.Length; i++)
                _finaliseState[i].ThreadState = null;
            _complete = false;
        }

        public void Add(T item)
        {
            lock (_lq)
            {
                if (_complete)
                    throw new Exception("Items can not be added when Complete");

                while (_q.Count == this.MaxQueue)
                    Monitor.Wait(_lq);

                _q.Enqueue(item);
                Monitor.PulseAll(_lq); // Use PulseAll for better reliability
            }
        }

        public void AddComplete()
        {
            lock (_lq)
            {
                _complete = true;
                Monitor.PulseAll(_lq); // Use PulseAll
            }
        }

        public Task Process(Action<T, int> process, Action<T> finalise)
        {
            if (this.UseFinalise && finalise == null)
                throw new Exception("finalise can not be null when UseFinalise is true");

            return Task.Run(() =>
            {
                int tc = this.MaxParallel;
                bool exit = false;

                Task finaliseTask = null;
                if (this.UseFinalise)
                    finaliseTask = finaliseProcess(finalise);

                while (!exit)
                {
                    threadState state;
                    lock (_lq)
                    {
                        while (!exit && _q.Count == 0)
                        {
                            if (_complete)
                                exit = true;
                            else
                                Monitor.Wait(_lq);
                        }
                        if (exit)
                            break;

                        // Move thread allocation logic inside queue lock to prevent race conditions
                        lock (_lp)
                        {
                            while (_tc >= tc)
                                Monitor.Wait(_lp);

                            _tc++;
                            state = _threadsState.First(a => !a.Allocated);
                            state.Allocated = true;
                        }

                        state.Object = _q.Dequeue();
                        state.Index = (uint)Interlocked.Read(ref _fc);
                        Interlocked.Increment(ref _fc);
                        Monitor.PulseAll(_lq);
                    }

                    ThreadPool.QueueUserWorkItem(obj =>
                    {
                        threadState ts = (threadState)obj;
                        try
                        {
                            process((T)ts.Object, (int)ts.ThreadIndex);
                        }
                        finally
                        {
                            if (this.UseFinalise)
                                finaliseObject(ts);
                            else
                                completeProcessing(ts);
                        }
                    }, state);
                }

                // Fixed: Remove double lock and use proper waiting pattern
                while (_tc > 0)
                {
                    lock (_lp)
                    {
                        if (_tc > 0)
                            Monitor.Wait(_lp, 100); // Add timeout to prevent infinite wait
                    }
                }

                if (this.UseFinalise && finaliseTask != null)
                {
                    // Signal finalise to complete
                    lock (_lf)
                        Monitor.PulseAll(_lf);
                    finaliseTask.Wait();
                }
            });
        }

        private void completeProcessing(threadState state)
        {
            lock (_lp)
            {
                state.Object = null;
                state.Allocated = false;
                _tc--;
                Monitor.PulseAll(_lp); // Use PulseAll
            }
        }

        private void finaliseObject(threadState ts)
        {
            lock (_lf)
            {
                finaliseState fts = _finaliseState.First(a => a.ThreadState == null);
                fts.ThreadState = ts;
                Monitor.PulseAll(_lf); // Use PulseAll
            }
        }

        private Task finaliseProcess(Action<T> finalise)
        {
            return Task.Run(() =>
            {
                while (true)
                {
                    finaliseState fts = null;

                    lock (_lf)
                    {
                        // Check completion condition first
                        if (_complete && _fp >= Interlocked.Read(ref _fc))
                            break;

                        fts = _finaliseState.FirstOrDefault(a => a.ThreadState?.Index == _fp);
                        if (fts == null)
                        {
                            Monitor.Wait(_lf, 100); // Add timeout
                            continue; // Don't pulse when waiting
                        }
                    }

                    if (fts != null)
                    {
                        Interlocked.Increment(ref _fp);
                        threadState ts = fts.ThreadState;
                        finalise((T)ts.Object);

                        lock (_lf)
                            fts.ThreadState = null;

                        completeProcessing(ts);
                    }
                }
            });
        }
    }
}