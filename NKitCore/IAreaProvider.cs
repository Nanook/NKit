using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    public interface IAreaProvider
    {
        Task<IReadOnlyList<IArea>> DiscoverAreasAsync(IStream stream, CancellationToken ct);
        Task SeekToAsync(long imageOffset, CancellationToken ct);
    }
}