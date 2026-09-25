namespace Nanook.NKit.Vfs
{
    /// <summary>
    /// Implements IFsFileParts for NkFs multi-extent entries.
    /// Wraps the extent chain data resolved from NkFs.GetExtents().
    /// </summary>
    internal class NkFsFileParts : IFsFileParts
    {
        internal NkFsFileParts(List<IFsFilePart> parts, long size)
        {
            Parts = parts;
            Size = size;
        }

        public List<IFsFilePart> Parts { get; }
        public long Size { get; }

        /// <summary>
        /// Returns 0 — combined hash is not available for a multi-extent file.
        /// </summary>
        public ulong XxHash => 0;

        /// <summary>
        /// Returns 0 — combined CRC is not available for a multi-extent file.
        /// </summary>
        public uint Crc => 0;
    }

    /// <summary>
    /// A single extent within a multi-extent NkFs file.
    /// Tracks its 0-based index in the chain, cumulative byte offset from file start,
    /// and reference to the NkFsFileItem for that extent.
    /// </summary>
    internal class NkFsFilePart : IFsFilePart
    {
        internal NkFsFilePart(int index, long offsetInFile, IFsFile fsFile)
        {
            Index = index;
            OffsetInFile = offsetInFile;
            FsFile = fsFile;
        }

        public int Index { get; }
        public long OffsetInFile { get; }
        public IFsFile FsFile { get; }

        public override string ToString() => $"Index:{Index}, OffInFile:{OffsetInFile:x9}, Size:{FsFile.FsSize:x9}, Name:{FsFile.Name}";
    }
}