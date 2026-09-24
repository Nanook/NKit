using NKitDataStore.Interfaces;

namespace NKitDataStore
{
    /// <summary>
    /// Utility that collates offset records and areas from an IImageReader and exposes
    /// collections grouped by area. This class does not modify any existing store state.
    /// It also provides helpers to query which offsets overlap a given buffer (area + buffer index + buffer size).
    /// </summary>
    public class OffsetsManager
    {
        private readonly IImageReader _reader;

        // populated on BuildIndex
        private List<AreaRecord> _areas = new List<AreaRecord>();
        private List<OffsetRecord> _offsets = new List<OffsetRecord>();

        // indexes
        private Dictionary<long, List<OffsetRecord>> _byArea = new Dictionary<long, List<OffsetRecord>>();

        // pre-split into fixed sections for fast lookup
        private readonly long _sectionSize;
        private readonly int _storeBlockSize;
        private Dictionary<long, List<SectionRecord>> _sectionsByArea = new Dictionary<long, List<SectionRecord>>();

        private readonly object _sync = new object();

        /// <summary>
        /// Returns the effective section size for an area: the area's own SectionSize if set, otherwise the global default.
        /// </summary>
        private long getEffectiveSectionSize(AreaRecord area) => area.SectionSize > 0 ? area.SectionSize : _sectionSize;

        /// <summary>
        /// Creates an OffsetsManager that will split areas into sections of the given size (default 2MiB)
        /// and optionally consider store block size (default 0x10000) for consumers that need block-aware mapping.
        /// BuildIndex will be called during construction to populate internal structures.
        /// </summary>
        public OffsetsManager(IImageReader reader, long sectionSize = 2L * 1024 * 1024, int storeBlockSize = 0x10000)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            if (sectionSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(sectionSize));
            if (storeBlockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(storeBlockSize));
            _sectionSize = sectionSize;
            _storeBlockSize = storeBlockSize;

            // Populate indexes immediately for fast subsequent queries
            BuildIndex();
        }

        /// <summary>
        /// Merges offset records from a secondary (aux) reader into this manager and rebuilds indexes.
        /// Areas are kept from the primary reader; only offsets that don't already exist (by image offset)
        /// are added from the aux reader.
        /// </summary>
        public void MergeAuxOffsets(IImageReader auxReader)
        {
            if (auxReader == null)
                return;

            lock (_sync)
            {
                // Get existing offset positions for dedup
                HashSet<long> existingOffsets = new HashSet<long>(_offsets.Select(o => o.Offset));

                // Add aux offsets that aren't already in the primary and belong to a known area.
                // Skip BlockPadding records — they contain security/hash data that is specific to
                // the store's internal format and can cause overflow in reconstruction.
                foreach (OffsetRecord auxOffset in auxReader.GetOffsets())
                {
                    if (auxOffset.Type == BlockType.BlockPadding)
                        continue;
                    if (!existingOffsets.Contains(auxOffset.Offset) && FindAreaForOffset(auxOffset.Offset) != null)
                    {
                        _offsets.Add(auxOffset);
                        existingOffsets.Add(auxOffset.Offset);
                    }
                }

                // Sort and rebuild all indexes with the merged offset set
                _offsets = _offsets.OrderBy(o => o.Offset).ToList();
                rebuildIndexes();
            }
        }

        /// <summary>
        /// Build internal indexes from the image reader. Safe to call multiple times; rebuilds indexes.
        /// </summary>
        public void BuildIndex()
        {
            lock (_sync)
            {
                _areas = _reader.GetAreas().OrderBy(a => a.Offset).ToList();
                _offsets = _reader.GetOffsets().OrderBy(o => o.Offset).ToList();
                rebuildIndexes();
            }
        }

        /// <summary>
        /// Rebuilds all internal indexes from the current _areas and _offsets lists.
        /// Must be called within _sync lock.
        /// </summary>
        private void rebuildIndexes()
        {
            _byArea = new Dictionary<long, List<OffsetRecord>>();
            foreach (AreaRecord area in _areas)
                _byArea[area.Offset] = new List<OffsetRecord>();

            // Assign each offset to the area that contains it (first match). If none found, group under key 0.
            foreach (OffsetRecord off in _offsets)
            {
                AreaRecord? area = FindAreaForOffset(off.Offset);
                long areaKey = area?.Offset ?? 0L;
                if (!_byArea.TryGetValue(areaKey, out List<OffsetRecord>? list))
                {
                    list = new List<OffsetRecord>();
                    _byArea[areaKey] = list;
                }
                list.Add(off);
            }

            // Ensure lists are ordered by offset for deterministic iteration
            foreach (long key in _byArea.Keys.ToList())
                _byArea[key] = _byArea[key].OrderBy(o => o.Offset).ToList();

            // build section index per-area using per-area section size
            _sectionsByArea = new Dictionary<long, List<SectionRecord>>();
            foreach (AreaRecord area in _areas)
            {
                DataStride stride = area.GetDataStride();
                List<SectionRecord> sections = new List<SectionRecord>();
                long areaStart = area.Offset;
                long areaEnd = area.Offset + area.Size;
                long areaSectionSize = getEffectiveSectionSize(area);

                for (long s = areaStart; s < areaEnd; s += areaSectionSize)
                {
                    long sSize = Math.Min(areaSectionSize, areaEnd - s);
                    long stored = stride.GetCleanSize(areaStart - area.Offset, sSize);
                    sections.Add(new SectionRecord(s, sSize, stored));
                }

                // if there were no sections (zero-sized area) still create empty list
                _sectionsByArea[area.Offset] = sections;

                // populate segments for each offset in this area
                if (_byArea.TryGetValue(area.Offset, out List<OffsetRecord>? offs) && offs.Count > 0)
                {
                    // Precompute file-like groups for gap calculations. We need group's image end and stored start/size.
                    List<(long StartImage, long TotalStored, long StoredStart)> groups = offs
                        .Where(o => o.Type == BlockType.File || o.Type == BlockType.FileSystem)
                        .GroupBy(o => o.OffsetStart)
                        .Select(g => (StartImage: g.Key, TotalStored: g.Sum(o => o.Size), StoredStart: g.Key - area.Offset))
                        .OrderBy(x => x.StartImage)
                        .ToList();

                    // add segments as before
                    foreach (OffsetRecord off in offs)
                    {
                        long offStartImage = off.Offset; // image offset where this stored data group begins (hashed/image coordinates)

                        // offset.Size is stored data length (fs length) � convert to hashed/image length for intersection math
                        long offImageLength = stride.GetStridedSize(offStartImage - area.Offset, off.Size, true); // computeHashedLengthForStoredLength(off.Size, area);
                        long offEndImage = offStartImage + offImageLength;

                        // find first section index that could intersect
                        long idxL = (offStartImage - areaStart) / areaSectionSize;
                        int idx = idxL > int.MaxValue ? 0 : (int)idxL;
                        if (idx < 0)
                            idx = 0;

                        for (int si = idx; si < sections.Count; si++)
                        {
                            SectionRecord sec = sections[si];
                            long secStart = sec.ImageOffset;
                            long secEnd = sec.ImageOffset + sec.Size;

                            // For 0-size files, use special logic since offEndImage == offStartImage
                            if ((off.Size == 0 ? offStartImage : offEndImage) <= secStart)
                                break; // no further intersections
                            if (offStartImage >= secEnd)
                                continue; // section is before the data

                            long intersectStartImage = Math.Max(offStartImage, secStart);
                            long intersectEndImage = Math.Min(offEndImage, secEnd);
                            if (intersectStartImage < intersectEndImage || off.Size == 0) // allow 0 length
                            {
                                // compute stored/fs offsets & sizes corresponding to the intersection image-range
                                long sourceOffsetFs = stride.GetCleanSize(offStartImage - area.Offset, intersectStartImage - offStartImage);
                                long segSizeFs = stride.GetCleanSize(intersectStartImage - area.Offset, intersectEndImage - intersectStartImage);

                                OffsetSegment seg = new OffsetSegment
                                {
                                    Source = off,
                                    SourceOffset = sourceOffsetFs, // SourceOffset and Size are in stored/fs coordinates
                                    Size = segSizeFs,
                                    SegmentOffset = intersectStartImage - secStart // SegmentOffset remains image-relative within section
                                };
                                sec.SegmentsInternal.Add(seg);
                            }
                        }
                    }

                    // ensure segments ordered in each section (by image-relative SegmentOffset)
                    foreach (SectionRecord sec in sections)
                        sec.SegmentsInternal.Sort((a, b) => a.SegmentOffset.CompareTo(b.SegmentOffset));

                    // Now compute GapOffset for each section by scanning groups in order.
                    long prevEndFs = 0;
                    int gi = 0;
                    for (int si = 0; si < sections.Count; si++)
                    {
                        SectionRecord sec = sections[si];
                        // advance groups whose image-end is <= section image offset
                        while (gi < groups.Count)
                        {
                            (long StartImage, long TotalStored, long StoredStart) g = groups[gi];
                            long groupImageLength = stride.GetStridedSize(g.StartImage, g.TotalStored, true);
                            long groupEndImage = g.StartImage + groupImageLength;
                            if (groupEndImage <= sec.ImageOffset)
                            {
                                long groupEndFs = stride.OffsetToClean(g.StoredStart) + g.TotalStored;
                                if (groupEndFs > prevEndFs)
                                    prevEndFs = groupEndFs;
                                gi++;
                                continue;
                            }
                            break;
                        }

                        // If this section begins with a file (or is a continuation of a file) then GapOffset should be -1
                        // Otherwise set GapOffset to the previous group's FS end (0 if none)
                        if (sec.SegmentsInternal.Count > 0 && sec.SegmentsInternal[0].Source.Type == BlockType.File && sec.SegmentsInternal[0].SegmentOffset == 0) // section begins with data (either start of a file/group or continuation) -> not a gap
                            sec.GapOffset = -1;
                        else // section starts with a gap -> provide FS offset where previous file/group ended
                            sec.GapOffset = prevEndFs;
                    }
                }
            }
        }
        /// <summary>
        /// Returns a snapshot of the areas parsed from the reader.
        /// </summary>
        public IReadOnlyList<AreaRecord> GetAreas()
        {
            lock (_sync)
                return _areas.ToList();
        }

        /// <summary>
        /// Returns a snapshot of all offsets parsed from the reader.
        /// </summary>
        public IReadOnlyList<OffsetRecord> GetOffsets()
        {
            lock (_sync)
                return _offsets.ToList();
        }

        /// <summary>
        /// Returns offsets grouped by the area's starting offset. The key is the area's Image-offset.
        /// Offsets that did not match any area are grouped under key 0.
        /// </summary>
        public IReadOnlyDictionary<long, IReadOnlyList<OffsetRecord>> GetOffsetsByArea()
        {
            lock (_sync)
                return _byArea.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<OffsetRecord>)kv.Value.ToList());
        }

        /// <summary>
        /// Returns offsets for a specific area (identified by the area's Image offset). Returns empty list if none.
        /// </summary>
        public IReadOnlyList<OffsetRecord> GetOffsetsForArea(long areaOffset)
        {
            lock (_sync)
            {
                if (_byArea.TryGetValue(areaOffset, out List<OffsetRecord>? list))
                    return list.ToList();
                return Array.Empty<OffsetRecord>();
            }
        }

        /// <summary>
        /// Returns the list of pre-computed sections for an area (split by the configured section size).
        /// If the area is unknown an empty list is returned.
        /// </summary>
        public IReadOnlyList<SectionRecord> GetSectionsForArea(long areaOffset)
        {
            lock (_sync)
            {
                if (_sectionsByArea.TryGetValue(areaOffset, out List<SectionRecord>? list))
                    return list.Select(s => s.Clone()).ToList();
                return Array.Empty<SectionRecord>();
            }
        }

        /// <summary>
        /// Returns the SectionRecord for the provided absolute image offset. The offset must align to the section boundary
        /// within its containing area (i.e. (imageOffset - area.Offset) % sectionSize == 0). If not aligned, an ArgumentException is thrown.
        /// If the area exists but contains no offsets intersecting the section, an empty SectionRecord is returned (represents a gap).
        /// </summary>
        public SectionRecord GetSectionByImageOffset(long imageOffset)
        {
            lock (_sync)
            {
                AreaRecord? area = FindAreaForOffset(imageOffset);
                if (area == null)
                    throw new ArgumentException($"No area contains image offset {imageOffset}", nameof(imageOffset));

                long areaSectionSize = getEffectiveSectionSize(area);
                long areaOffset = imageOffset - area.Offset;
                if (areaOffset % areaSectionSize != 0)
                    throw new ArgumentException($"Image offset {imageOffset} does not align to section size {areaSectionSize} for area starting at {area.Offset}", nameof(imageOffset));

                if (!_sectionsByArea.TryGetValue(area.Offset, out List<SectionRecord>? sections))
                    return new SectionRecord(imageOffset, Math.Min(areaSectionSize, area.Offset + area.Size - imageOffset), area.GetDataStride().GetCleanSize(areaOffset, Math.Min(areaSectionSize, area.Offset + area.Size - imageOffset)));

                SectionRecord? sec = sections.FirstOrDefault(s => s.ImageOffset == imageOffset);
                if (sec == null)
                    return new SectionRecord(imageOffset, Math.Min(areaSectionSize, area.Offset + area.Size - imageOffset), area.GetDataStride().GetCleanSize(areaOffset, Math.Min(areaSectionSize, area.Offset + area.Size - imageOffset)));

                return sec.Clone();
            }
        }

        /// <summary>
        /// Convenience helper: returns the ordered list of OffsetSegments required to populate the section
        /// that starts at the provided image offset. The imageOffset must align to the configured section size
        /// within its containing area (same rules as GetSectionByImageOffset). If the section contains no
        /// data this returns an empty list (represents a gap).
        /// </summary>
        public IReadOnlyList<OffsetSegment> GetSegmentsForSection(long imageOffset)
        {
            lock (_sync)
            {
                SectionRecord sec = GetSectionByImageOffset(imageOffset);
                return sec.Segments.ToList();
            }
        }

        /// <summary>
        /// Find the AreaRecord that contains the provided absolute image offset.
        /// Returns null when no containing area exists.
        /// </summary>
        public AreaRecord? FindAreaForOffset(long absoluteOffset)
        {
            // areas are ordered; simple linear scan is fine as areas are usually few
            foreach (AreaRecord a in _areas)
            {
                if (absoluteOffset >= a.Offset && absoluteOffset < a.Offset + a.Size)
                    return a;
            }
            return null;
        }
    }

    /// <summary>
    /// Represents a fixed-size section within an area. Sections are created by OffsetsManager when indexing the image.
    /// </summary>
    public class SectionRecord
    {
        internal SectionRecord(long imageOffset, long size, long storedSize)
        {
            ImageOffset = imageOffset;
            Size = size;
            StoredSize = storedSize;
            GapOffset = 0; // default: no previous file (gap starts at area start)
        }

        public long ImageOffset { get; }
        public long Size { get; }

        /// <summary>
        /// Size of the data when stored (fs/clean-data size). For strided areas this may be smaller than Image Size.
        /// </summary>
        public long StoredSize { get; }

        public IReadOnlyList<OffsetSegment> Segments => _segments.AsReadOnly();

        private readonly List<OffsetSegment> _segments = new List<OffsetSegment>();

        internal List<OffsetSegment> SegmentsInternal => _segments;

        /// <summary>
        /// If this section begins with a gap, this contains the area-FS offset (stored bytes from area start)
        /// where the previous file/group ended. If this section starts with a file or is a continuation of a file,
        /// this value is -1 and not relevant.
        /// </summary>
        public long GapOffset { get; internal set; }

        internal SectionRecord Clone()
        {
            SectionRecord c = new SectionRecord(ImageOffset, Size, StoredSize);
            foreach (OffsetSegment s in _segments)
                c._segments.Add(s.Clone());
            c.GapOffset = this.GapOffset;
            return c;
        }
    }

    /// <summary>
    /// Represents a clipped portion of an OffsetRecord that lives inside a SectionRecord.
    /// SourceOffset and Size are expressed in stored/fs bytes. SegmentOffset is image-relative inside the section.
    /// </summary>
    public class OffsetSegment
    {
        public OffsetRecord Source { get; internal set; } = null!;

        /// <summary>
        /// Offset into the Source OffsetRecord where this segment begins (relative to Source.Offset), expressed in stored/fs bytes.
        /// </summary>
        public long SourceOffset { get; internal set; }

        /// <summary>
        /// Offset within the Section where this segment begins (relative to Section.ImageOffset), expressed in image/hashed bytes.
        /// </summary>
        public long SegmentOffset { get; internal set; }

        /// <summary>
        /// Size of this segment expressed in stored/fs bytes.
        /// </summary>
        public long Size { get; internal set; }

        internal OffsetSegment Clone()
        {
            return new OffsetSegment
            {
                Source = this.Source,
                SourceOffset = this.SourceOffset,
                SegmentOffset = this.SegmentOffset,
                Size = this.Size
            };
        }
    }
}