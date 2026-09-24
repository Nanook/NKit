using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{
    /// <summary>
    /// ISO9660 file-system reader driving the existing <see cref="Fst.SetFsData"/> parse through the
    /// <see cref="IFileSystemReader"/> pending-region model. The IImage (via
    /// <see cref="FileSystemCoverage"/>) reads the regions this reader needs and feeds them back.
    ///
    /// <para>
    /// ISO9660 directory extents are discovered from the volume descriptors / path table and held in
    /// the parse context's Ahead list until their block is fed. In practice the directory blocks
    /// precede the file data, so feeding the file-system area's leading blocks resolves everything
    /// with no seeking; the pending mechanism handles the rare case of a directory extent that lands
    /// after (or out of order with) the sequential cursor. The reader reports each still-pending
    /// directory extent as an image-offset region so the coverage loop can fetch it.
    /// </para>
    ///
    /// <para>
    /// <see cref="RequireFullFileSystemUpFront"/> is always true: the whole file system is resolved
    /// before any data section of the area is emitted, which makes the published file list immutable
    /// and avoids the concurrent-insert index-shift hazard of the old lockstep parse. Directory
    /// extents can have unknown size until read, so a pending region is fetched at a generous read
    /// length that comfortably covers any single directory extent.
    /// </para>
    /// </summary>
    internal sealed class Iso9660FileSystemReader : IFileSystemReader
    {
        private readonly FileSystemInfo _fsInfo;
        private readonly Fst _fst;
        private readonly long _areaImageOffset;
        private readonly int _readLength;

        public Iso9660FileSystemReader(FileSystemInfo fsInfo, long areaImageOffset, int readLength)
        {
            _fsInfo = fsInfo;
            _fst = (Fst)fsInfo.FileSystem;
            _areaImageOffset = areaImageOffset;
            _readLength = readLength;
        }

        // Correctness invariant for ISO9660 (offset-sorted FST): always resolve fully up front so
        // the published file list is immutable when parallel section processors read it.
        public bool RequireFullFileSystemUpFront => true;

        public bool Complete => _fsInfo.AllFoldersParsed || (_fst.Context.Ahead.Count == 0 && _fst.AllFolderRecordsParsed);

        public IReadOnlyList<FsRegionRequest> Pending
        {
            get
            {
                OrderedList<FstFile> ahead = _fst.Context.Ahead;
                if (ahead.Count == 0)
                    return System.Array.Empty<FsRegionRequest>();

                FsRegionRequest[] r = new FsRegionRequest[ahead.Count];
                for (int i = 0; i < ahead.Count; i++)
                {
                    // Directory extent -> absolute image offset. Read a generous length: extents can
                    // report unknown size (-1) until parsed, and Fst.Current requires the whole
                    // extent present in the fed block (RangeComplete), so read at least the area's
                    // section-sized block which comfortably covers a directory extent.
                    // len is a RAW span consumed by ResolveThrough as raw area bytes (Buffer.Update
                    // treats the read size as raw). FsSize is FS-space (2048) and must NOT be mixed
                    // in: for framed tracks (e.g. Mode2 2352/2048) the raw extent is larger than the
                    // FS size. _readLength is the area's block-aligned SectionSize, which comfortably
                    // covers any single directory extent's raw framing.
                    long imageOffset = _areaImageOffset + _fst.Context.FsOffToOff(ahead[i].FsOffset);
                    r[i] = new FsRegionRequest(imageOffset, _readLength);
                }
                return r;
            }
        }

        public void ProcessBlock(IBuffer buffer) => _fsInfo.ProcessBlock(buffer);
    }
}