using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    public interface ISectionPreProcess
    {
        Task PreProcessAsync(ISectionContext ctx, ISection section, CancellationToken ct);
        // Called by the core when a seek occurs so preprocessors can reset any per-stream state.
        Task OnSeekAsync(ISectionContext ctx, SeekRequest req, CancellationToken ct);
    }
}