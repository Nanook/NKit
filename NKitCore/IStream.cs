using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NKitCore
{
    public interface IStream
    {
        IReadOnlyList<IArea> InitialAreas { get; }
        Task<int> ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken ct);
        /// <summary>Optional: total length if known. Null if unknown.</summary>
        long? Length { get; }
    }
}