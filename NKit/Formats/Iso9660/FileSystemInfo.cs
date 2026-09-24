using System;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FileSystemInfo : IFileSystemInfo
    {
        private readonly long _imageSize;

        public AreaInfo AreaInfo { get; }

        public FileSystemInfo(long imageOffset, long imageSize, ImageHeader header, AreaInfo areaInfo, long physicalVolumeSize)
        {
            this.ImageOffset = imageOffset;
            _imageSize = imageSize;
            this.Header = header;
            this.AreaInfo = areaInfo;
            this.FsSize = Math.Max(header.PvdSectorCount * areaInfo.BlockFsSize, header.FsSize);
            this.Size = Buffer.FsOffsetToOffset(this.FsSize, areaInfo.BlockSize, areaInfo.BlockFsOffset, areaInfo.BlockFsSize, true);
            if (Size == 0)
                this.Size = imageSize;
            this.InvalidFileSystem = header.Pvds == null || (header.Pvds.Count == 1 && header.Pvds.ContainsKey(FsType.System) && header.Pvds[FsType.System].ImageOffset == 0);
            this.FileSystem = new Fst(header, this.InvalidFileSystem ? 0 : physicalVolumeSize);
            this.FidelityFiles = header.FstContext.FidelityFiles;
        }
        public ImageHeader Header { get; }
        public PartitionType Type => PartitionType.Other;

        public long ImageOffset { get; private set; }
        public long FsSize { get; private set; }
        public long Size { get; private set; }
        public IFileSystem FileSystem { get; private set; }

        public bool InvalidFileSystem { get; set; }

        public bool AllFoldersParsed => ((Fst)this.FileSystem).AllFolderRecordsParsed;

        // UDF present — only then can there be tail markers (backup AVDP / partition mirror).
        public bool HasUdf => ((Fst)this.FileSystem).HasUdf;

        // Total image size (used to bound the tail scan to the end of the image).
        public long ImageSize => _imageSize;

        // Image offset just past this AREA's file-system extent. The UDF backup structures sit at the
        // end of EACH area/session (a multi-session disc has one per session), not just the whole
        // image, so the tail scan is bounded to each area's end. Falls back to image size when the
        // extent is unknown.
        public long AreaEnd => this.Size > 0 && this.Size < _imageSize ? this.ImageOffset + this.Size : _imageSize;
        public FidelityFileList FidelityFiles { get; internal set; }

        private IAreaFileSystemView _areaView;
        public IAreaFileSystemView AreaView => _areaView ??= AreaFileSystemView.TryBuild(this);

        // Force the frozen AreaView snapshot to rebuild on next access. Needed when late tail markers
        // are added to a SHARED FileSystemInfo AFTER its view was first frozen (XBox video2 reuses
        // video1's FST; its tail markers are discovered at video2 setup — after video1 froze the
        // view). Safe because the core processes areas in order: video1 is fully complete before
        // video2 setup, so no reader is using the old snapshot when it is invalidated.
        public void ResetAreaView() => _areaView = null;

        public void ProcessBlock(IBuffer buffer) => ((Fst)this.FileSystem).SetFsData(buffer);

        // Scan a fed TAIL buffer directly for end-of-image system markers (UDF AVDP backup /
        // partition mirror, mkisofs) and add them to the FST — bypassing the file-relative gap-walk
        // navigation of SetFsData/processFileGaps (which does not scan the trailing region because
        // the last file's post-gap "covers" it). Used by the up-front tail feed.
        public void DiscoverTailMarkersBlock(IBuffer buffer)
        {
            IFileSystem fs = this.FileSystem;
            int before = fs.Files.Count;
            ((Fst)fs).DiscoverTailMarkers(buffer);
            if (fs.Files.Count == before)
                return;
            // The frozen AreaView (built during an earlier area's processing) will NOT reflect these
            // late additions to the live FST list — GetFiles reads Primary (the frozen snapshot). So
            // register any newly-added SYSTEM entries on the view's DiscoveredFiles, which Primary
            // surfaces past the frozen count (tail-extension). Deduped by FsOffset, so idempotent.
            if (this.AreaView is AreaFileSystemView view)
            {
                System.Collections.Generic.IReadOnlyList<IFsFile> files = fs.Files;
                for (int i = 0; i < files.Count; i++)
                {
                    if (files[i].IsSystemFile)
                        view.AddDiscovered(files[i]);
                }
            }
        }

        public void Complete()
        {

        }

        //Written to by Image (Read threads), Access by Fix (Wr
        internal IrdFileResults IrdResults { get; set; }

        internal void DebugFiles()
        {
            //StringBuilder sb = new StringBuilder();
            //foreach (FstFile f in FileSystem?.Files)
            //    sb.AppendLine(f.ToString());

            //Trace.WriteLine(sb.ToString());
        }

    }
}