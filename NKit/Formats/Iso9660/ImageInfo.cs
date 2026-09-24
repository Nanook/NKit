namespace Nanook.NKit.Iso.Iso9660
{
    internal class ImageInfo : IImageInfo
    {
        // Per-section log scope, wired from the Image's IImageContext.Log (satisfies IImageInfo).
        public ILogScope SectionLog { get; internal set; }

        public long ImageSize { get; internal set; } //Same as above, if nkit it refers to the converted size
        public long ReadLength { get; internal set; } //Same as above, if nkit it refers to the converted size
        public long FixSize { get; internal set; } //same as above, but in the case of truncated isos, it's the full recovered size
        public long SourceSize { get; internal set; } //size of the source file on the disc in which ever format
        public SystemType SystemType { get; internal set; }
        public IStepsImageInfo StepImageInfo { get; internal set; }
        public ImageType Type { get; internal set; }
        public ContainerType ContainerType { get; internal set; }
        public ReadMode Mode { get; set; }
        public uint FixCrc { get; internal set; }
        public IFixData FixData { get; set; }
        public bool IsFolderIndex { get; internal set; }
        public IImageArea[] SourceAreas { get; internal set; }

        public bool IsFullImage { get; internal set; }
        public long Multiplier { get; internal set; }
        public bool OutputEncryption { get; internal set; }
        public bool OutputHashes { get; internal set; }
        public bool Patching { get; internal set; }
        public bool SourceSupportsEncryption { get; internal set; }
        public bool SourceHasEncryptedHashes { get; internal set; }
        public bool SourceHasEncryption { get; internal set; }
        public bool SourceHasHashes { get; internal set; }

        public CdType? CdDiscType { get; internal set; }
        public MediaType MediaType { get; internal set; }
        public SourceFileTrack[] Tracks { get; internal set; }

        //Written to by Image (Read threads), Access by Fix (Output CsqThread) Only on completion to avoid threading issues (no locking required)
        internal IrdFileResults IrdResults { get; set; }
    }
}