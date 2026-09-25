using System.Collections.Generic;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class BufferPreProcess : IBufferPreProcessor
    {
        private IImageContext _context;

        public BufferPreProcess(IImageContext context)
        {
            _context = context;
        }

        public void Complete() => _context = null;

        public long ImageSize => _context.ImageInfo.ImageSize;

        //NKitCore path: file-system view supplied explicitly (no reliance on any internal state).
        public void GetFiles(IFileSystemInfo fsInfo, IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            if (buffer.Type != AreaType.FileSystem || fsInfo == null || fsInfo.InvalidFileSystem)
                return;
            getFilesCore(fsInfo, buffer, fst, ref fstState);
        }

        // Wii/GC decrypt per-section inside their SectionProcessor (each block carries its own hashes /
        // seek IV), so no serial spanning decrypt is required in the pre-process stage.
        public void PreDecrypt(IFileSystemInfo fsInfo, IBuffer buffer) { }

        // Wii/GC have no gap-scan tail markers.
        public void DiscoverMarkers(IFileSystemInfo fsInfo, IBuffer buffer) { }

        private void getFilesCore(IFileSystemInfo fsInfo, IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            buffer.FileStartIndex = -1;
            buffer.FileEndIndex = -1;

            // Assign the section's file coverage from the parsed, FROZEN AreaView built up front by
            // the IImage (readFileSystemUpFront parses the whole FST at the start of the filesystem
            // area). Primary is the immutable snapshot of the same ordered list the section
            // processors index, so FileStartIndex/EndIndex stay valid without reading the live,
            // mutable FileSystem.Files. Fall back to the passed live list only when no view was
            // built (invalid/empty file system). Mirrors the Iso9660 preprocessor.
            IFileSystemView view = fsInfo?.AreaView?.Primary;
            int count = view != null ? view.FileCount : (fst?.Count ?? 0);
            if (count == 0)
                return;

            //convert to fs
            long areaOffset = Buffer.OffsetToFsOffset(buffer.AreaOffset, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);
            long size = Buffer.OffsetToFsOffset(buffer.Size, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);

            if (!(fstState == -1 || fstState >= count))
            {
                IFsFile file = fileAt(view, fst, fstState);

                //skip passed files
                while (file != null && file.FsOffset + file.FsSize + file.PostGapSize <= areaOffset)
                    file = fstState + 1 < count ? fileAt(view, fst, ++fstState) : null;

                while (true)
                {
                    if (file != null && areaOffset < file.FsOffset + file.FsSize + file.PostGapSize && areaOffset + size > file.FsOffset)
                    {
                        if (buffer.FileStartIndex == -1)
                            buffer.FileStartIndex = fstState;

                        buffer.FileEndIndex = fstState;

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
    }
}