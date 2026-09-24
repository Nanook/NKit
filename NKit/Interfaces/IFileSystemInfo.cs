namespace Nanook.NKit
{
    //CsqThread safe readonly data for section processor
    public interface IFileSystemData
    {
        long ImageOffset { get; }
        long Size { get; }
        IFileSystem FileSystem { get; }
        bool InvalidFileSystem { get; }
        PartitionType Type { get; }

        bool AllFoldersParsed { get; }
        FidelityFileList FidelityFiles { get; }

        /// <summary>
        /// A safe, read-only view of this area's file system for parallel read consumers — a
        /// collection of per-file-system views plus System and merged Primary projections over an
        /// immutable snapshot. Null until the file system is fully parsed (<see cref="AllFoldersParsed"/>).
        /// The element type stays <see cref="IFsFile"/>; the snapshot LIST is frozen once published,
        /// so indexed reads are race-free without a lock or a clone of file data.
        /// </summary>
        IAreaFileSystemView AreaView { get; }
    }
    internal interface IFileSystemInfo : IFileSystemData
    {
        AreaInfo AreaInfo { get; }
        void Complete();
    }

}