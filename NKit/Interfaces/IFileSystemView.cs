using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// The logical file-system family a view represents. This is the PUBLIC, out-stage-safe
    /// discriminator over the internal per-link FsType: it lets extract steps pick or skip a
    /// logical file system (e.g. XBox's XDVDFS game partition vs an ISO9660 volume, or the
    /// preferred name-space on a multi-FS ISO) WITHOUT casting to an internal format type.
    /// Values are unordered; use them for identity, not priority.
    /// </summary>
    public enum FileSystemKind
    {
        /// <summary>No per-file-system typing available (e.g. Nintendo, which has one FS per image), or not yet known.</summary>
        Unknown = 0,
        Iso9660,
        Romeo,
        RockRidge,
        Joliet,
        Udf,
        Cdi,
        Cdxa,
        ElTorito,
        System,
        /// <summary>Microsoft XBox / XBox360 XDVDFS game-partition file system.</summary>
        XDvdFs,
        Other
    }

    /// <summary>
    /// A safe, read-only view over an area's parsed file system, handed to read consumers (the
    /// preprocessor and section processors) instead of the live mutable collection.
    ///
    /// <para>
    /// Safety model: an area's file system is FULLY parsed before its view is published (see
    /// <see cref="IFileSystemData.AllFoldersParsed"/> / the reader contract); the view then exposes
    /// an IMMUTABLE snapshot of the file list. Consumers read it by index over a stable count, so
    /// parallel section processors can read it while later areas are still being parsed on the read
    /// thread — with no lock and no clone of the file data (the <see cref="IFsFile"/> elements are
    /// shared, but the list is frozen once published).
    /// </para>
    ///
    /// <para>
    /// An area can hold multiple logical file systems (ISO9660 / Joliet / UDF / XDVDFS). The view
    /// exposes each as its own <see cref="IFileSystemView"/> in <see cref="FileSystems"/>, a
    /// <see cref="System"/> view of the system entries (Wii/GC boot.bin family; ISO/XBox volume
    /// markers — the files flagged <see cref="IFsFile.IsSystemFile"/>), and a merged
    /// <see cref="Primary"/> view over the whole ordered list. The section pipeline's
    /// <c>FileStartIndex</c>/<c>FileEndIndex</c> index window addresses <see cref="Primary"/>.
    /// These are projections over the one snapshot — no copy of file data.
    /// </para>
    /// </summary>
    public interface IAreaFileSystemView
    {
        long ImageOffset { get; }
        long Size { get; }
        PartitionType Type { get; }
        bool InvalidFileSystem { get; }

        /// <summary>
        /// The dominant/primary logical file-system family of this area (e.g.
        /// <see cref="FileSystemKind.XDvdFs"/> for an XBox game partition), or
        /// <see cref="FileSystemKind.Unknown"/> when the format is single-FS/untyped (Nintendo).
        /// Extract steps use this to select or skip an area WITHOUT casting to an internal format type.
        /// </summary>
        FileSystemKind Kind { get; }

        /// <summary>Merged view over the whole area's ordered file list (what the section index window addresses).</summary>
        IFileSystemView Primary { get; }

        /// <summary>The system/boot/volume entries (<see cref="IFsFile.IsSystemFile"/>); empty when the format has none.</summary>
        IFileSystemView System { get; }

        /// <summary>One view per logical file system present in the area (ISO9660 / Joliet / UDF / XDVDFS).</summary>
        IReadOnlyList<IFileSystemView> FileSystems { get; }

        /// <summary>
        /// Blocks of data IDENTIFIED in the image but NOT part of the file-system folder tree — e.g.
        /// the UDF backup anchor / partition mirror at the volume tail, or an mkisofs signature
        /// sector. They are worth reporting in a scan (an otherwise-unknown block whose purpose is
        /// known) but they belong to no folder. Unlike the frozen <see cref="Primary"/> snapshot
        /// this list is APPEND-ONLY and can grow as the normal sequential read streams past a
        /// block that is only identifiable once read (so large images are never fully read up front
        /// just to find a tail marker). Every entry is flagged <see cref="IFsFile.IsSystemFile"/>.
        /// </summary>
        IReadOnlyList<IFsFile> DiscoveredFiles { get; }
    }

    /// <summary>
    /// A read-only view of ONE logical file system (or the merged/system projection). Iterate by
    /// index up to <see cref="FileCount"/> — never <c>foreach</c> a live list. The element is the
    /// shared <see cref="IFsFile"/>; the LIST is an immutable published snapshot, so indexed reads
    /// are stable for the view's lifetime.
    /// </summary>
    public interface IFileSystemView
    {
        int FileCount { get; }
        IFsFile File(int index);

        /// <summary>
        /// The logical file-system family this view represents (ISO9660 / Joliet / UDF / XDVDFS…),
        /// or <see cref="FileSystemKind.Unknown"/> for the merged (<see cref="IAreaFileSystemView.Primary"/>),
        /// system, or an untyped single-FS projection.
        /// </summary>
        FileSystemKind Kind { get; }

        /// <summary>Root folder of this logical file system, or null for the merged/system projection.</summary>
        IFsFolder Root { get; }

        /// <summary>Index-based enumerable (captures the count; iterates <see cref="File"/> by index).</summary>
        IEnumerable<IFsFile> Files { get; }
    }
}