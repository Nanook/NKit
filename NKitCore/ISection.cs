using System;

namespace NKitCore
{
    /// <summary>
    /// The core section contract. The core is generic over the section type (TSection) and never
    /// touches the section's payload/buffer — it only sequences, pools and orders sections.
    /// </summary>
    public interface ISection
    {
        long SequenceNumber { get; set; }
        int AreaIndex { get; set; }
        long ImageOffset { get; set; }
        long AreaOffset { get; set; }
        bool IsLastInArea { get; set; }
        bool IsLastInStream { get; set; }
        int Length { get; set; }
        void Reset();
    }

    /// <summary>A section whose payload is a single <see cref="Memory{Byte}"/> buffer.</summary>
    public interface IMemorySection : ISection
    {
        Memory<byte> Buffer { get; }
    }
}