using System.Collections.Generic;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class BufferPreProcessor : IBufferPreProcessor
    {
        private IImageContext _context;
        private FileSystemInfo _fsInfo;
        private ContentHeader _cntHeader;
        private WiiUSecurityHashless _hashlessSec;

        public BufferPreProcessor(IImageContext context)
        {
            _context = context;
        }

        public void Complete()
        {
            _context = null;
            _fsInfo = null;
            _cntHeader = null;
            closeHashlessSec();
        }
        public long ImageSize => _context.ImageInfo.ImageSize;

        private void closeHashlessSec()
        {
            if (_hashlessSec != null)
            {
                _hashlessSec.Dispose();
                _hashlessSec = null;
            }
        }

        // NKitCore pipeline: WiiU game partitions that are encrypted but hashless have no per-block
        // seek IV, so they must be AES-CBC decrypted as one continuous stream. The legacy dual-buffer
        // Process() did this via a per-area WiiUSecurityHashless whose CBC state chains across sections.
        // Here we drive the same chaining serially on each section's own buffer (in-place Encrypted ->
        // Decrypted). The SectionProcessor then treats this content as already decrypted (see the
        // "non hashed decryption has already been done linearly by the preprocessor" path).
        public void PreDecrypt(IFileSystemInfo fsInfo, IBuffer buffer)
        {
            _fsInfo = (FileSystemInfo)fsInfo;
            if (_fsInfo != null) //no key mode
                _cntHeader = buffer.FsIndex == -1 ? null : _fsInfo.FstBlock.ContentHeaders[buffer.FsIndex];

            AreaType type = buffer.Type;
            bool isRepeatedAppOther = type == AreaType.Other && (_cntHeader?.RepeatedApp ?? false);
            if (type != AreaType.FileSystem && !isRepeatedAppOther)
                return;

            // Only hashless game partitions require the linear (spanning) decrypt.
            if (buffer.AreaInfo.IsEncrypted && !buffer.AreaInfo.HasSecurity && _fsInfo != null && _fsInfo.Type == PartitionType.Game)
            {
                if (buffer.AreaOffset == 0 || (isRepeatedAppOther && _cntHeader != null && buffer.AreaOffset % _cntHeader.Size == 0))
                {
                    closeHashlessSec();
                    _hashlessSec = new WiiUSecurityHashless((ImageHeader)_context.Header, _fsInfo.Type, buffer.FsIndex, _fsInfo.SiData?.KeyTitle, false); //uses common or title key internally
                }
                // AES-CBC requires 16-byte-aligned block counts. CDN system content sizes can be
                // non-aligned at the tail section; round up so TransformBlock doesn't throw.
                int decryptSize = ((int)buffer.Size + 15) & ~15;
                _hashlessSec.Process(buffer.Encrypted, 0, buffer.Decrypted, 0, decryptSize);
            }
        }

        // WiiU FST fully describes the content; no gap-scan tail markers.
        public void DiscoverMarkers(IFileSystemInfo fsInfo, IBuffer buffer) { }

        // NKitCore path: file-system view supplied explicitly.
        public void GetFiles(IFileSystemInfo fsInfo, IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            if (buffer.Type != AreaType.FileSystem || fsInfo == null || fsInfo.InvalidFileSystem)
                return;
            _fsInfo = (FileSystemInfo)fsInfo;
            getFilesCore(buffer, fst, ref fstState);
        }

        private void getFilesCore(IBuffer buffer, List<IFsFile> fst, ref int fstState)
        {
            //Buffer CiBuffer = (Buffer)ibuffer;
            int appIdx = buffer.FsIndex;

            buffer.FileStartIndex = -1;
            buffer.FileEndIndex = -1;
            //convert to fs
            long areaOffset = Buffer.OffsetToFsOffset(buffer.AreaOffset, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);
            long size = Buffer.OffsetToFsOffset(buffer.AreaOffset + buffer.Size, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize) - areaOffset;

            if (!(fstState == -1 || fst == null || fstState >= fst.Count))
            {
                FstFile file = (FstFile)fst[fstState];

                //skip passed files
                while (file != null && (file.WiiUAppIndex < appIdx || (file.FsOffset + file.FsSize + file.PostGapSize <= areaOffset)))
                    file = fstState + 1 < fst.Count ? (FstFile)fst[++fstState] : null;

                while (file != null && file.WiiUAppIndex == appIdx)
                {
                    if (areaOffset < file.FsOffset + file.FsSize + file.PostGapSize && areaOffset + size > file.FsOffset)
                    {
                        if (buffer.FileStartIndex == -1)
                            buffer.FileStartIndex = fstState;

                        buffer.FileEndIndex = fstState;

                        if (file.FsOffset + file.FsSize + file.PostGapSize < areaOffset + size && fstState + 1 < fst.Count) //move to the next file (could be multiples in this block)
                        {
                            file = (FstFile)fst[++fstState];
                            continue;
                        }
                    }
                    break;
                }

                if (file == null)
                    fstState = -1; //done
            }
        }

    }
}