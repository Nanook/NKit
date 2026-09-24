using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Source-reading abstraction implemented by <see cref="NKitInput"/> and consumed by the
    /// NKitCore pipeline (NKitCoreRunner / C2SectionFactory). Relocated out of the (now-removed)
    /// legacy NKitEngine.cs so it survives the old-engine removal.
    /// </summary>
    internal interface IInput
    {
        bool Open(Stream stream, bool canUseCustomChkSum, SystemType filterSystem, SystemType defaultSystem, out SystemType detectedSystem);

        int Read(IBuffer buffer, out IFileSystemInfo fsInfo);
        long Position { get; set; }
    }
}