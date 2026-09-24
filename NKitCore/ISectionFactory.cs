using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    /// <summary>
    /// Factory that produces <see cref="ISection"/> instances for areas of an <see cref="IStream"/>.
    /// Lifecycle: ConstructAsync (once) -> SetupAsync (once) -> CreateNextSectionAsync (repeated,
    /// null when the area is exhausted). SeekAsync repositions the factory's internal parsers.
    /// </summary>
    public interface ISectionFactory
    {
        Task<ISection> CreateNextSectionAsync(ISectionContext ctx, IStream stream, IArea area, long offset, CancellationToken ct);
        Task ConstructAsync(ISectionContext ctx, IStream stream, CancellationToken ct);
        Task SetupAsync(ISectionContext ctx, IStream stream, CancellationToken ct);
        Task SeekAsync(ISectionContext ctx, IStream stream, SeekRequest req, CancellationToken ct);
    }
}