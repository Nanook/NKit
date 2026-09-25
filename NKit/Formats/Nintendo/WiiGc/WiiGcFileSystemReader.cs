using System.Collections.Generic;

namespace Nanook.NKit.Nintendo.WiiGc
{
    // Wii/GameCube filesystem reader. The FST always sits at the start of the (partition) data
    // area, so the WiiGc.Image reads boot.bin + the whole FST region up front and feeds it here in
    // one or more blocks; this reader drives the existing FileSystemInfo.SetFsInfo accumulation
    // until the file system is fully parsed. Because the Image supplies the complete FST region,
    // this reader never reports Pending regions — it is Complete as soon as SetFsInfo signals the
    // FST has been fully read.
    //
    // The parse logic itself remains in FileSystemInfo (SetFsInfo/process/setFileSystem); this
    // class only relocates the up-front FST-parse DRIVER (previously in BufferPreProcess). The
    // per-buffer file-coverage annotation (GetFiles) remains in BufferPreProcess, which the engine
    // calls directly, so the preprocessor is otherwise a pure pass-through.
    internal sealed class WiiGcFileSystemReader : IFileSystemReader
    {
        private readonly FileSystemInfo _fsInfo;
        private readonly long _partitionDataImageOffset;
        private bool _complete;

        public WiiGcFileSystemReader(FileSystemInfo fsInfo, long partitionDataImageOffset)
        {
            _fsInfo = fsInfo;
            _partitionDataImageOffset = partitionDataImageOffset;
            _complete = false;
        }

        // Wii/GC never seeks for out-of-order directory blocks; the Image hands over the whole FST
        // region, so nothing is ever outstanding.
        public IReadOnlyList<FsRegionRequest> Pending => System.Array.Empty<FsRegionRequest>();

        public bool Complete => _complete;

        // Wii/GC always parse the whole FST up front (it precedes the files), so this is implicitly
        // always true; the flag is only meaningful for the streaming (XBox/ISO) readers.
        public bool RequireFullFileSystemUpFront => true;

        public void ProcessBlock(IBuffer buffer)
        {
            if (_complete)
                return;

            // Drive the existing FST accumulation. SetFsInfo decrypts the block itself when the
            // area is encrypted (using the partition key held by _fsInfo) and returns true once the
            // full FST has been read.
            if (_fsInfo.SetFsInfo(buffer, _partitionDataImageOffset))
                _complete = true;
        }
    }
}