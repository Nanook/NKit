using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    public interface ISectionCompleter
    {
        Task<SectionCompleteResult> CompleteAsync(ISectionContext ctx, ISection section, CancellationToken ct);
        Task FinalizeAsync(ISectionContext ctx, CancellationToken ct);
    }
}