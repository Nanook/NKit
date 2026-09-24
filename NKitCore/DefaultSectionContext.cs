using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    /// <summary>
    /// Thread-safe <see cref="ISectionContext"/>: a pool of section shells (via a factory), area
    /// registration and per-area outstanding counters so callers can wait for area completion.
    ///
    /// The pool free-list is bounded by <paramref name="maxPoolFree"/> so returned sections are
    /// reused rather than re-allocated (large per-section buffers), but the free list never grows
    /// without bound. There is deliberately no aggressive "shrink to 2" trim — the live section
    /// count is already bounded by the core's admission control, so keeping up to maxPoolFree shells
    /// warm avoids re-allocating large byte[] buffers as the pipeline breathes.
    /// </summary>
    public class DefaultSectionContext : ISectionContext
    {
        private readonly ConcurrentQueue<ISection> _pool = new ConcurrentQueue<ISection>();
        private int _freeCount;
        private int _totalCreated;
        private readonly int _maxPoolFree;
        private readonly List<IArea> _areas = new List<IArea>();
        private readonly ConcurrentDictionary<int, AreaState> _areaStates = new ConcurrentDictionary<int, AreaState>();
        private readonly Func<ISection> _sectionFactory;

        private class AreaState
        {
            public int Outstanding;
            public TaskCompletionSource<object> Tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public DefaultSectionContext(Guid streamId, int sectionSize, Func<ISection> sectionFactory, CancellationToken cancellation = default, int maxPoolFree = 0)
        {
            StreamId = streamId;
            SectionSize = sectionSize;
            _sectionFactory = sectionFactory ?? throw new ArgumentNullException(nameof(sectionFactory));
            Cancellation = cancellation;
            // Default free-list ceiling scales with logical CPUs (a reasonable proxy for pipeline
            // width); callers may override.
            _maxPoolFree = maxPoolFree > 0 ? maxPoolFree : Math.Max(4, Environment.ProcessorCount + 2);
        }

        public Guid StreamId { get; }
        public int SectionSize { get; }
        public CancellationToken Cancellation { get; }
        public IReadOnlyList<IArea> AreasSnapshot => _areas.AsReadOnly();

        public ISection RentSection()
        {
            if (_pool.TryDequeue(out ISection s))
            {
                Interlocked.Decrement(ref _freeCount);
                return s;
            }
            Interlocked.Increment(ref _totalCreated);
            return _sectionFactory();
        }

        public void ReturnSection(ISection section)
        {
            if (section == null) return;
            try { section.Reset(); } catch { }

            // Keep the free list bounded; drop (let GC reclaim) sections beyond the ceiling.
            if (Interlocked.Increment(ref _freeCount) <= _maxPoolFree)
            {
                _pool.Enqueue(section);
            }
            else
            {
                Interlocked.Decrement(ref _freeCount);
                Interlocked.Decrement(ref _totalCreated);
            }
        }

        public (int FreeCount, int TotalCreated) GetPoolMetrics()
            => (Volatile.Read(ref _freeCount), Volatile.Read(ref _totalCreated));

        public void RegisterArea(IArea area)
        {
            lock (_areas) { _areas.Add(area); }
            _areaStates.GetOrAdd(area.Index, _ => new AreaState());
        }

        public Task WaitForAreaCompletionAsync(int areaIndex, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);

            AreaState st = _areaStates.GetOrAdd(areaIndex, _ => new AreaState());
            if (Volatile.Read(ref st.Outstanding) == 0) return Task.CompletedTask;

            Task<object> task = st.Tcs.Task;
            if (ct == CancellationToken.None) return task;
            return waitWithCancellationAsync(task, ct);
        }

        private static async Task waitWithCancellationAsync(Task task, CancellationToken ct)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                Task completed = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
        }

        public void IncrementOutstandingSections(int areaIndex)
        {
            AreaState st = _areaStates.GetOrAdd(areaIndex, _ => new AreaState());
            int newValue = Interlocked.Increment(ref st.Outstanding);
            if (newValue == 1)
                Interlocked.Exchange(ref st.Tcs, new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public void DecrementOutstandingSections(int areaIndex)
        {
            if (!_areaStates.TryGetValue(areaIndex, out AreaState st)) return;
            int remaining = Interlocked.Decrement(ref st.Outstanding);
            if (remaining <= 0)
            {
                try { st.Tcs.TrySetResult(null); } catch { }
            }
        }

        /// <summary>
        /// Hook to release resources held by a pooled section as the pool is drained on Dispose.
        /// The base ISection carries no payload the core owns, so this is a no-op here; subclasses
        /// whose section wraps a large buffer/processor override this to drop those references so
        /// they are freed deterministically at image completion rather than lingering until GC.
        /// </summary>
        protected virtual void ReleaseSection(ISection section) { }

        private bool _disposed;

        /// <summary>
        /// Deterministically release everything this context holds at image completion: drain the
        /// pooled section free-list (releasing each section's payload via <see cref="ReleaseSection"/>),
        /// complete any outstanding area-wait TCS so no awaiter hangs, and clear the area lists. The
        /// context is created per image (per <c>Run</c>), so disposing it here frees the per-image
        /// section buffers immediately instead of leaving them rooted in the pool until GC.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            while (_pool.TryDequeue(out ISection s))
            {
                try { ReleaseSection(s); } catch { }
            }
            Interlocked.Exchange(ref _freeCount, 0);

            foreach (AreaState st in _areaStates.Values)
            {
                try { st.Tcs.TrySetResult(null); } catch { }
            }
            _areaStates.Clear();

            lock (_areas) { _areas.Clear(); }
        }
    }
}