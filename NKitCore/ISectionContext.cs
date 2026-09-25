using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    public interface ISectionContext : IDisposable
    {
        Guid StreamId { get; }
        int SectionSize { get; }
        CancellationToken Cancellation { get; }
        IReadOnlyList<IArea> AreasSnapshot { get; }

        // Pool helpers
        ISection RentSection();
        void ReturnSection(ISection section);

        // Area gating
        void RegisterArea(IArea area);
        Task WaitForAreaCompletionAsync(int areaIndex, CancellationToken ct);
        void IncrementOutstandingSections(int areaIndex);
        void DecrementOutstandingSections(int areaIndex);

        /// <summary>Return current pool metrics: free count and total created.</summary>
        (int FreeCount, int TotalCreated) GetPoolMetrics();
    }
}