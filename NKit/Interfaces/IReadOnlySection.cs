using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// A RESTRICTED, read-only view of a processed section block, handed to an external embedding
    /// consumer via <see cref="NKitProcessor.OnSection"/>. It is the public "block feed" contract:
    /// a consumer receives these in image order and can read the processed data without NKit writing
    /// to disk.
    ///
    /// <para>
    /// Deliberately NARROWER than the internal <see cref="ISection"/>: it does NOT expose the raw,
    /// pooled, mutable <c>Decrypted</c>/<c>Encrypted</c> buffers, the <c>Write*</c> mutators, the
    /// seek IV, or anything that would let a consumer drive/corrupt the pipeline (the internal
    /// <c>ISectionProcessor</c>). Byte access is copy-out only (<see cref="ReadBytes"/> /
    /// <see cref="Read"/>). See NKitVault/10 Refactor/Public API and Embedding Model.md.
    /// </para>
    /// </summary>
    public interface IReadOnlySection
    {
        /// <summary>Absolute offset of this block within the disc image.</summary>
        long ImageOffset { get; }

        /// <summary>Size of this block in bytes.</summary>
        long Size { get; }

        /// <summary>Offset of this block within its area.</summary>
        long AreaOffset { get; }

        /// <summary>Offset of this block within the area's file-system space (no hash gaps).</summary>
        long FsOffset { get; }

        /// <summary>File-system size represented by this block.</summary>
        long FsSize { get; }

        /// <summary>The area kind this block belongs to (ImageHeader, PartitionHeader, FileSystem, ...).</summary>
        AreaType Type { get; }

        /// <summary>CRC32 of this block (encrypted/source form).</summary>
        uint Crc { get; }

        /// <summary>CRC32 of this block in decrypted form (0 when not applicable).</summary>
        uint CrcDecrypted { get; }

        /// <summary>XXHash64 of this block.</summary>
        ulong XxHash { get; }

        /// <summary>True when this block's data is encrypted in the current form.</summary>
        bool IsEncrypted { get; }

        /// <summary>Area geometry/typing metadata for this block.</summary>
        AreaInfo AreaInfo { get; }

        /// <summary>
        /// The frozen, immutable file-system view for this block's area (Primary / System / per-FS).
        /// Null until the area's file system is fully parsed. Safe to read in parallel.
        /// </summary>
        IAreaFileSystemView AreaFileSystem { get; }

        /// <summary>The per-file items this block contributes to (read-only).</summary>
        IEnumerable<IReadOnlySectionItem> Items { get; }

        /// <summary>Copy <paramref name="size"/> bytes from <paramref name="fsOffset"/> into a new array.</summary>
        byte[] ReadBytes(int fsOffset, int size);

        /// <summary>Copy <paramref name="size"/> bytes from <paramref name="fsOffset"/> into <paramref name="toStream"/>.</summary>
        void Read(int fsOffset, int size, Stream toStream);
    }

    /// <summary>
    /// A read-only view of one file's contribution within an <see cref="IReadOnlySection"/> — the
    /// file it belongs to and where its data sits in the block. The mutable/parse members of the
    /// internal <c>ISectionItem</c>/<c>ISectionData</c> are not exposed.
    /// </summary>
    public interface IReadOnlySectionItem
    {
        /// <summary>The file this item contributes data to (null for a pure gap).</summary>
        IFsFile FsFile { get; }

        /// <summary>Absolute image offset of this item.</summary>
        long ImageOffset { get; }

        /// <summary>Offset of this item's data within the containing section (file-system space).</summary>
        long FileOffsetInSection { get; }

        /// <summary>Size in bytes of this item's data in this section.</summary>
        long Size { get; }

        /// <summary>The logical file systems this item appears in (e.g. iso9660 / joliet / udf).</summary>
        IReadOnlyList<string> FileSystems { get; }
    }
}
