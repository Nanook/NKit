using System;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    /// <summary>Simple <see cref="Memory{Byte}"/>-backed section used by the default factory and tests.</summary>
    public sealed class MemorySection : IMemorySection
    {
        private readonly byte[] _buffer;
        public MemorySection(int size) { _buffer = new byte[size]; }

        public Memory<byte> Buffer => _buffer;
        public long SequenceNumber { get; set; }
        public int AreaIndex { get; set; }
        public long ImageOffset { get; set; }
        public long AreaOffset { get; set; }
        public bool IsLastInArea { get; set; }
        public bool IsLastInStream { get; set; }
        public int Length { get; set; }

        public void Reset()
        {
            SequenceNumber = 0; AreaIndex = 0; ImageOffset = 0; AreaOffset = 0;
            IsLastInArea = false; IsLastInStream = false; Length = 0;
        }
    }

    /// <summary>
    /// Default factory: reads each area sequentially into <see cref="MemorySection"/> buffers via
    /// <see cref="IStream.ReadAtAsync"/>. Used by the core's own tests.
    /// </summary>
    public sealed class DefaultSectionFactory : ISectionFactory
    {
        public Task ConstructAsync(ISectionContext ctx, IStream stream, CancellationToken ct) => Task.CompletedTask;
        public Task SetupAsync(ISectionContext ctx, IStream stream, CancellationToken ct) => Task.CompletedTask;
        public Task SeekAsync(ISectionContext ctx, IStream stream, SeekRequest req, CancellationToken ct) => Task.CompletedTask;

        public async Task<ISection> CreateNextSectionAsync(ISectionContext ctx, IStream stream, IArea area, long offset, CancellationToken ct)
        {
            long areaEnd = area.ImageOffset + (area.Size ?? 0);
            long remainingInArea = areaEnd - offset;
            if (remainingInArea <= 0) return null;

            int toRead = (int)Math.Min(ctx.SectionSize, remainingInArea);
            ISection sec = ctx.RentSection();
            sec.ImageOffset = offset;
            sec.AreaOffset = offset - area.ImageOffset;
            sec.AreaIndex = area.Index;
            sec.Length = toRead;

            int read = await stream.ReadAtAsync(offset, ((IMemorySection)sec).Buffer.Slice(0, toRead), ct).ConfigureAwait(false);
            if (read <= 0) { ctx.ReturnSection(sec); return null; }

            sec.Length = read;
            sec.IsLastInArea = (sec.AreaOffset + read) >= (area.Size ?? 0);
            sec.IsLastInStream = (stream.Length ?? 0) <= (offset + read);
            return sec;
        }
    }
}