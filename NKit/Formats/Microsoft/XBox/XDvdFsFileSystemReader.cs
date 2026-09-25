using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Microsoft.XBox
{
    /// <summary>
    /// XDVDFS (XBox game partition) file-system reader. Replaces the inline recursive <c>readFs</c>
    /// seek walk in <c>XBox.Image</c> with the <see cref="IFileSystemReader"/> pending-region model,
    /// so the IImage (via <see cref="FileSystemCoverage"/>) owns all stream reads/seeks and the walk
    /// is cache-safe and CanSeekTo-guarded.
    ///
    /// <para>
    /// XDVDFS directory entries form an on-disk tree: each directory block lists child files and
    /// subdirectories, whose blocks can lie anywhere in the partition (before or after the parent).
    /// This reader mirrors the original <c>readFs</c> recursion exactly: it maintains an explicit
    /// queue of directory entries to visit (seeded with the root FST entry); on
    /// <see cref="ProcessBlock"/> it parses the fed directory block via <c>Fst.Add</c> and enqueues
    /// the child directory entries that block revealed. <see cref="Pending"/> exposes the queued
    /// directories as image-offset regions for the coverage loop to fetch.
    /// </para>
    /// </summary>
    internal sealed class XDvdFsFileSystemReader : IFileSystemReader
    {
        private readonly XDvdFsFileSystemInfo _fsInfo;
        private readonly Fst _fst;
        private readonly long _areaImageOffset;
        private readonly int _blockSize;
        private readonly long _imageSize;

        // Directory entries discovered but not yet parsed (their block not yet fed). Mirrors the
        // recursion frontier of the original readFs.
        private readonly List<FstFile> _queue = new List<FstFile>();
        private bool _complete;

        // After the directory tree is walked, ONE final pass over the partition's leading
        // volume/header region is fed through the gap scan (Fst.SetFsData with the tree already
        // parsed) to discover the system markers that are not directory entries — the XBox
        // layout-tool signature (__xbox_hdr_) and the __volume_ marker. These live just before the
        // first content file. Doing this up front (before the view is published) keeps the FST
        // build linear: nothing mutates the file system once section processing begins.
        private bool _gapScanDone;
        private long _gapScanOffset;   // image offset of the leading region to gap-scan
        private long _gapScanLength;   // length of that region (up to the first content file)

        public XDvdFsFileSystemReader(XDvdFsFileSystemInfo fsInfo, long areaImageOffset, int blockSize, long imageSize)
        {
            _fsInfo = fsInfo;
            _fst = (Fst)fsInfo.FileSystem;
            _areaImageOffset = areaImageOffset;
            _blockSize = blockSize;
            _imageSize = imageSize;

            // Seed with the root FST directory (the last non-volume system entry — same selection
            // the original readFs used: Files.Where(FsOffset != Volume.FsOffet).LastOrDefault()).
            FstFile root = (FstFile)_fst.Files
                .Where(a => a.FsOffset != fsInfo.Header.Volume.FsOffet)
                .LastOrDefault();
            if (root != null && !string.IsNullOrEmpty(root.Name) && _areaImageOffset + root.FsOffset < _imageSize)
                _queue.Add(root);
            else
            {
                // Nothing to walk — no tree, so no leading-region gap scan either. Mark both phases
                // done so Complete is true immediately (mirrors the original readFs no-op case).
                _complete = true;
                _gapScanDone = true;
            }
        }

        // XBox always needs the whole directory tree resolved before emitting data sections.
        public bool RequireFullFileSystemUpFront => true;

        // Complete once the directory tree is walked AND the leading-region gap scan has run.
        public bool Complete => _complete && _gapScanDone;

        public IReadOnlyList<FsRegionRequest> Pending
        {
            get
            {
                // Phase 1: directory blocks still to walk.
                if (_queue.Count != 0)
                {
                    FsRegionRequest[] r = new FsRegionRequest[_queue.Count];
                    for (int i = 0; i < _queue.Count; i++)
                        r[i] = new FsRegionRequest(_areaImageOffset + _queue[i].FsOffset, _queue[i].FsSize);
                    return r;
                }
                // Phase 2: after the tree is walked, one leading-region gap scan for the
                // non-directory system markers (__xbox_hdr_ / __volume_).
                if (_complete && !_gapScanDone && _gapScanLength > 0)
                    return new[] { new FsRegionRequest(_gapScanOffset, _gapScanLength) };
                return System.Array.Empty<FsRegionRequest>();
            }
        }

        public void ProcessBlock(IBuffer buffer)
        {
            // Phase 2: the leading-region gap scan (tree already walked). Run the gap scan only
            // (AllFolderRecordsParsed is set, so SetFsData does not re-walk the tree) to append the
            // non-directory system markers, then finish.
            if (_complete && !_gapScanDone)
            {
                long gapFsOffset = buffer.ImageOffset - _areaImageOffset;
                if (gapFsOffset == _gapScanOffset - _areaImageOffset)
                {
                    _fst.SetFsData(buffer);
                    _gapScanDone = true;
                }
                return;
            }

            if (_complete || _queue.Count == 0)
                return;

            // Match the queued directory whose region this block covers.
            long fsOffset = buffer.ImageOffset - _areaImageOffset;
            int idx = _queue.FindIndex(d => d.FsOffset == fsOffset);
            if (idx < 0)
                return; // block does not correspond to a queued directory (defensive)

            FstFile dirEntry = _queue[idx];
            _queue.RemoveAt(idx);

            // Parse the directory block into the FST (mirrors readFs: Add(dirEntry.Parent, ...)).
            _fst.Add((FstFolder)dirEntry.Parent, buffer.Decrypted, 0, dirEntry.FsSize, _blockSize);

            // Enqueue the child directory entries this block revealed (mirrors readFs recursion
            // over dirEntry.Parent.Folders whose single file is a DirectoryEntry).
            foreach (FstFolder fld in dirEntry.Parent.Folders)
            {
                if (fld.Files.Count == 1 && ((FstFile)fld.Files[0]).Type == FsItemType.DirectoryEntry)
                {
                    FstFile child = (FstFile)fld.Files[0];
                    if (!string.IsNullOrEmpty(child.Name) && _areaImageOffset + child.FsOffset < _imageSize)
                        _queue.Add(child);
                }
            }

            if (_queue.Count == 0)
            {
                _complete = true;
                _fst.AllFolderRecordsParsed = true; // matches the inline walk's terminal flag

                // Queue the leading-region gap scan: from the partition start up to the first
                // content file (the system markers __xbox_hdr_ / __volume_ live in this region).
                // Read a full section block so processFileGaps sees the whole leading region.
                long firstFileFsOffset = long.MaxValue;
                foreach (IFsFile f in _fst.Files)
                {
                    if (!f.IsSystemFile && f.FsOffset < firstFileFsOffset)
                        firstFileFsOffset = f.FsOffset;
                }
                _gapScanOffset = _areaImageOffset;
                _gapScanLength = firstFileFsOffset == long.MaxValue
                    ? _blockSize
                    : System.Math.Min(firstFileFsOffset, 0x200000);
                if (_gapScanLength <= 0)
                    _gapScanDone = true; // nothing to scan
            }
        }
    }
}