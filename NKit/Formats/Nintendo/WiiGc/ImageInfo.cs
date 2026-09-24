namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class ImageInfo : IImageInfo
    {
        // Section-processor log scope, wired from the Image's IImageContext.Log so the processor
        // (which only holds IImageInfo) can emit a per-area Detail summary + per-section Trace line.
        // Null when no Log / not initialised. Public getter satisfies IImageInfo; set is internal.
        public ILogScope SectionLog { get; internal set; }

        // Areas already summarised to [Sect]. SHARED across the whole SectionProcessor POOL (all
        // processors reference this one ImageInfo), so an area is logged exactly once even though
        // many pooled processors handle its sections. Thread-safe: guarded by its own lock.
        private readonly System.Collections.Generic.HashSet<int> _loggedAreas = new System.Collections.Generic.HashSet<int>();
        internal bool TryMarkAreaLogged(int areaNo)
        {
            lock (_loggedAreas)
                return _loggedAreas.Add(areaNo); // true = first time this area is seen
        }

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

        public CdType? CdDiscType { get; internal set; }
        public MediaType MediaType { get; internal set; }
        public SourceFileTrack[] Tracks { get; internal set; }

        public bool IsFullImage { get; internal set; }
        public long Multiplier { get; internal set; }
        public bool OutputEncryption { get; internal set; }
        public bool OutputHashes { get; internal set; }
        public bool Patching { get; internal set; }
        public bool SourceSupportsEncryption { get; internal set; }
        public bool SourceHasEncryptedHashes { get; internal set; }
        public bool SourceHasEncryption { get; internal set; }
        public bool SourceHasHashes { get; internal set; }

        public bool IsNkit { get; internal set; }
        // True when the source is an NKit image already fully decoded by NKitAsIso (clean
        // partition data in standard block layout). The old in-pipeline NKit block-shuffle /
        // FST-reconstruction preprocessor (FsNKitFormat) must be skipped for this source.
        public bool IsNkitDecoded { get; internal set; }

        // True when a GameCube Fix source has already been corrected up-front by GcFixAsIso
        // (files placed at correct offsets, gaps junk-filled, size corrected). The in-pipeline
        // GC fix gap-fill / FST-patch (fsSectionGapFill/fsFixPatch) must be skipped so the
        // already-correct decorator output is passed through unchanged.
        public bool IsGcFixApplied { get; internal set; }

        // True when a Wii Fix source has already had its LAYOUT corrected up-front by WiiFixAsIso
        // (Game partition slid to 0xF800000, partition table rebuilt, blank update region, sized to
        // the full disc). The in-pipeline partition-table reflow (applyWiiPartitionTableFixes /
        // applyDataPartitionFixes) must be skipped since the layout is already correct. The
        // SectionProcessor still unscrubs/hashes/encrypts partition content, and the Fix step still
        // brute-forces the update partition.
        public bool IsWiiFixLayoutApplied { get; internal set; }
        // Underlying compact NKit container of a decoded source (Gcz or Iso), for reporting SrcType.
        public ContainerType NKitSourceContainer { get; internal set; }
        public bool IsNkitUpdateRemoved { get; internal set; }
        public uint NKitUpdatePartitionCrc { get; internal set; }
        public FixPartition NKitUpdatePartition { get; internal set; }
        public byte[] FixJunkId { get; internal set; }

        // For a decoded NKit (NKitAsIso) Wii source: given a partition's output image offset and a
        // 0x200000 block-space group offset within it, returns true when that group has PRESERVED
        // (non-recreatable) hashes. NKitAsIso reproduces those hashes verbatim on read, so the
        // SectionProcessor must NOT regenerate them. Null for non-NKitAsIso sources.
        public System.Func<long, long, bool> IsPreservedHashGroup { get; internal set; }
    }
}