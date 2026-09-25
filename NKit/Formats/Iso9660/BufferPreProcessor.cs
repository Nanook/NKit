using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class BufferPreProcessor : IBufferPreProcessor
    {
        private IImageContext _context;
        private IFileSystemInfo _fsInfo;
        private bool _ps3Tested;

        public BufferPreProcessor(IImageContext context)
        {
            _ps3Tested = false;
            _context = context;
        }

        public void Complete()
        {
            _context = null;
            _fsInfo = null;
        }

        public long ImageSize => _context.ImageInfo.ImageSize;

        // NKitCore path: file-system view supplied explicitly.
        public void GetFiles(IFileSystemInfo fsInfo, IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            _fsInfo = fsInfo;
            getFilesCore(fsInfo, buffer, fst, ref fstState);
        }

        // PS3/Iso9660 decryption (when present) is handled per-section; no serial spanning decrypt is
        // required in the pre-process stage on the NKitCore path.
        public void PreDecrypt(IFileSystemInfo fsInfo, IBuffer buffer) { }

        // UDF backup structures (backup anchor + partition mirror) live only in the LAST ~0x8000 of
        // a UDF volume. Only scan sections whose end falls within this trailing window, and only when
        // UDF is present — so the per-sector signature scan runs on at most the final section(s), not
        // across the whole image (which doubled processing time).
        private const long UdfTailWindow = 0x100000; // 1 MiB safety margin over the ~0x8000 spec size

        // Read-time: scan this FileSystem section's buffer for end-of-image UDF markers and add them
        // to the FST (no seek — the buffer is the section being read). Mirrors the legacy sequential-
        // read discovery, but bounded to the UDF tail so it is effectively free elsewhere.
        public void DiscoverMarkers(IFileSystemInfo fsInfo, IBuffer buffer)
        {
            if (buffer.Type != AreaType.FileSystem || !(fsInfo is FileSystemInfo isoFs) || !isoFs.AllFoldersParsed || !isoFs.HasUdf)
                return;
            // The backup structures sit at the end of the AREA (each session on a multi-session disc
            // has its own), so bound the scan to the trailing window of this area's extent — not the
            // whole image (which would miss an earlier session's backup).
            if (buffer.ImageOffset + buffer.Size < isoFs.AreaEnd - UdfTailWindow)
                return;
            isoFs.DiscoverTailMarkersBlock(buffer);
        }

        private void getFilesCore(IFileSystemInfo fsInfo, IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            buffer.FileStartIndex = -1;
            buffer.FileEndIndex = -1;

            // Read the file list from the parsed, frozen AreaView (populated up front by the IImage)
            // when available; the view's Primary is the immutable snapshot of the same ordered list
            // the section processors index, so FileStartIndex/EndIndex stay valid. Fall back to the
            // passed live list only when no view was built (e.g. invalid/empty file system).
            IFileSystemView view = fsInfo?.AreaView?.Primary;
            int count = view != null ? view.FileCount : (fst?.Count ?? 0);
            if (count == 0)
                return;

            long baseOffset = ((Buffer)buffer).AreaInfo?.BaseOffset ?? 0;
            //convert to fs
            long areaOffset = Buffer.OffsetToFsOffset(baseOffset + buffer.AreaOffset, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);
            long size = Buffer.OffsetToFsOffset(buffer.Size, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);

            if (!(fstState == -1 || fstState >= count))
            {
                IFsFile file = fileAt(view, fst, fstState);
                //skip passed files

                while (file != null && file.FsOffset + file.FsSize + file.PostGapSize <= areaOffset)
                    file = fstState + 1 < count ? fileAt(view, fst, ++fstState) : null;

                while (file != null)
                {
                    if (areaOffset < file.FsOffset + file.FsSize + file.PostGapSize && areaOffset + size > file.FsOffset)
                    {
                        if (buffer.FileStartIndex == -1)
                            buffer.FileStartIndex = fstState;

                        buffer.FileEndIndex = fstState;

                        if (_context.ImageInfo.SystemType == SystemType.PS3 && !_ps3Tested)
                            ps3Test(buffer, baseOffset, (FstFile)file);

                        if (file.FsOffset + file.FsSize + file.PostGapSize < areaOffset + size && fstState + 1 < count) //move to the next file (could be multiples in this block)
                        {
                            file = fileAt(view, fst, ++fstState);
                            continue;
                        }
                    }
                    break;
                }

                if (file == null)
                    fstState = -1; //done
            }

        }

        // Index into the frozen view when present, else the passed live list (invalid-FS fallback).
        private static IFsFile fileAt(IFileSystemView view, List<IFsFile> fst, int index)
            => view != null ? view.File(index) : fst[index];

        private void ps3Test(IBuffer buffer, long baseOffset, FstFile file)
        {
            PlayStation3 ps3 = ((ImageHeader)_context.Header).Ps3;
            if (!ps3.EncryptionTestSuccess) //If the Image is seekable then the header will indicate so
            {
                int offset = (int)(file.FsOffset - baseOffset - buffer.AreaOffset);
                if (offset >= 0 && offset < (buffer.Size - 0x800) && buffer.AreaInfo.IsEncrypted)
                {
                    _ps3Tested = true; //don't try again
                    ps3.Ps3FileTest(buffer.Encrypted, buffer.ImageOffset + offset, file.Name, offset);
                    if (ps3.EncryptionTestSuccess)
                    {
                        if (ps3.IsEncrypted != buffer.AreaInfo.IsEncrypted)
                            throw new HandledException($"Encryption [File test] - {(ps3.IsEncrypted ? "Encrypted" : "Decrypted")} does not match - {(buffer.AreaInfo.IsEncrypted ? "Encrypted" : "Decrypted")}");
                        _context.Log.Info(() => $"Encryption [NoSeek File test] - {(ps3.IsEncrypted ? "Encrypted" : "Decrypted")}");
                    }
                }
            }
        }
    }
}