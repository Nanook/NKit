namespace Nanook.NKit
{
    internal interface IImageInfo
    {
        long ImageSize { get; }
        long ReadLength { get; }
        long FixSize { get; }
        long SourceSize { get; }
        SystemType SystemType { get; }
        IStepsImageInfo StepImageInfo { get; }
        ImageType Type { get; }
        ContainerType ContainerType { get; }
        ReadMode Mode { get; set; }
        uint FixCrc { get; }
        IFixData FixData { get; }
        bool IsFolderIndex { get; }
        IImageArea[] SourceAreas { get; }

        bool IsFullImage { get; }
        long Multiplier { get; }
        bool OutputEncryption { get; }
        bool OutputHashes { get; }
        bool Patching { get; }
        bool SourceSupportsEncryption { get; }
        bool SourceHasEncryptedHashes { get; }
        bool SourceHasEncryption { get; }
        bool SourceHasHashes { get; }

        CdType? CdDiscType { get; }
        MediaType MediaType { get; }
        SourceFileTrack[] Tracks { get; }

        // Per-section log scope (the reader's stage scope, e.g. [In]/[Out]), wired from the Image's
        // IImageContext.Log so the SectionProcessor (which only holds IImageInfo) can emit a
        // per-section Trace line. Null when no Log / not initialised.
        ILogScope SectionLog { get; }
    }
}