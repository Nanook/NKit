using System.Collections.Generic;

namespace Nanook.NKit
{
    internal interface IBufferPreProcessor
    {
        long ImageSize { get; }

        // NKitCore pipeline: the section's file-system view is supplied explicitly. Assigns each
        // section's file coverage (FileStartIndex/EndIndex) from the frozen, up-front-parsed FST.
        void GetFiles(IFileSystemInfo fsInfo, IBuffer fullBuffer, List<IFsFile> files, ref int fstOutIndex);

        // NKitCore pipeline: perform any decryption that MUST run linearly/serially because it chains
        // across section boundaries (e.g. WiiU hashless game partitions use AES-CBC without a per-block
        // seek IV, so the whole partition must be decrypted as one continuous stream). Called on the
        // serial pre-process stage, in image order, operating in-place on the section's own buffer
        // (Encrypted -> Decrypted). Formats that decrypt per-section inside their SectionProcessor
        // (Wii/GC) implement this as a no-op.
        void PreDecrypt(IFileSystemInfo fsInfo, IBuffer buffer);

        // Read-time discovery: scan THIS section's buffer for end-of-image system markers (UDF backup
        // anchor / partition mirror, mkisofs) that are only identifiable once read, and add them to
        // the FST. Runs on the serial pre-process stage as each FileSystem section streams past — no
        // seek, exactly as the legacy sequential read did. No-op for formats without gap markers.
        void DiscoverMarkers(IFileSystemInfo fsInfo, IBuffer buffer);

        void Complete();
    }
}