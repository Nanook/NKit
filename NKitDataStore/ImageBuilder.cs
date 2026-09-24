using NKitDataStore.Interfaces;
using System.Buffers;
using System.Diagnostics;

namespace NKitDataStore
{
    /// <summary>
    /// A seekable, readable stream that reconstructs disc image data from an IImageReader.
    /// Buffers data in fixed-size chunks for efficient random access while supporting strided formats.
    /// </summary>
    public class ImageBuilder : Stream
    {
        private static int _NextInstanceId = 0;
        public int InstanceId { get; }
        /// <summary>
        /// Optional cryptographic key discovered while reading image metadata/areas.
        /// Derived builders (for example WiiU) may populate this when a persisted key
        /// is found in the data store so callers can access it after construction.
        /// </summary>
        public byte[]? Key { get; protected set; }

        /// <summary>
        /// Per-buffer context passed through the buffer population lifecycle.
        /// </summary>
        protected class BufferContext
        {
            public AreaRecord Area { get; set; } = null!;
            public byte[] Buffer { get; set; } = null!;
            // Reference to the owning cache entry so callers can access metadata (Stride, ValidDataSize, MarkWritten)
            // Use object to avoid exposing internal BufferCacheEntry type through the public BufferContext API
            public object? BufferEntry { get; set; }
            // The effective DataStride to use for this buffer (set at creation time). Prefer this over recalculating.
            public DataStride Stride { get; set; } = null!;
            public long ImageOffset { get; set; }
            public long AreaOffset { get; set; }
            public long AreaFsOffset { get; set; }
            public long BufferCleanSize { get; set; }
            public SectionRecord? Section { get; set; }
            public Dictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
        }

        private readonly IImageReader _reader;
        private readonly IBlockProvider _blockProvider;
        private readonly int _bufferSize;
        private readonly OffsetsManager _offsetsManager;
        private readonly List<AreaRecord> _areas;
        private readonly Dictionary<long, OffsetRecord> _offsetsByPosition;
        // Actual store block size read from the image's set info. Falls back to _DefaultBlockSize when not available.
        private readonly int _storeBlockSize;
        /// <summary>
        /// When false, Dispose will NOT call _reader.Dispose(). Use this when the reader
        /// is shared/pooled and its lifetime is managed externally (e.g. VFS mount).
        /// </summary>
        private readonly bool _disposeReader;

        private long _position;
        public Action<byte[], int, int>? OnDataRead { get; set; }
        private long _hashedPosition;

        /// <summary>
        /// Initializes a new ImageBuilder with the specified buffer size.
        /// /// </summary>
        /// <param name="reader">The image reader to read data from.</param>
        /// <param name="bufferSize">The size of each buffer (must be constant for reuse). Recommended: 0x200000 (2MB).</param>
        /// <param name="maxCachedBuffers">Maximum number of buffers to cache (default: 16).</param>
        /// <param name="blockProvider"></param>
        // Local per-ImageBuilder cache to avoid relying on shared static cache state
        // which caused interference when multiple tests ran in parallel.
        // Implemented as an LRU cache keyed by buffer key so we can evict oldest entries
        // when the cache grows beyond _maxCacheSize.
        private readonly Dictionary<long, BufferCacheEntry>? _localCache;
        private readonly LinkedList<long>? _lruList; // most-recent at front
        private readonly Dictionary<long, LinkedListNode<long>>? _lruNodes;
        private readonly int _maxCacheSize;

        /// <summary>
        /// When non-null, buffer lookups delegate to this shared per-image cache instead of the local LRU.
        /// The shared cache is NOT disposed by this ImageBuilder — the caller (MountResourceManager) manages its lifetime.
        /// </summary>
        private readonly ImageBufferCache? _sharedCache;

        public ImageBuilder(IImageReader reader, int maxCachedBuffers = 0x10, IBlockProvider? blockProvider = null, bool disposeReader = true, ImageBufferCache? sharedBufferCache = null, OffsetsManagerCacheResult? cachedOffsets = null)
        {
            InstanceId = Interlocked.Increment(ref _NextInstanceId);
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _disposeReader = disposeReader;
            _sharedCache = sharedBufferCache;
            // Determine the effective store block size from the reader's set info
            _storeBlockSize = _reader.Info.BlockSize;
            _blockProvider = blockProvider ?? new ReaderBlockProvider(reader);
            _maxCacheSize = maxCachedBuffers;

            // Only allocate local cache structures when no shared cache is provided
            if (_sharedCache == null)
            {
                _localCache = new Dictionary<long, BufferCacheEntry>();
                _lruList = new LinkedList<long>();
                _lruNodes = new Dictionary<long, LinkedListNode<long>>();
            }

            if (cachedOffsets != null)
            {
                // Use pre-built data directly — skip SQLite queries and OffsetsManager construction
                _areas = cachedOffsets.Areas;
                _offsetsByPosition = cachedOffsets.OffsetsByPosition;
                _offsetsManager = cachedOffsets.OffsetsManager;
            }
            else
            {
                // Current construction logic: query areas, query offsets, build OffsetsManager
                _areas = _reader.GetAreas().OrderBy(a => a.Offset).ToList();

                // Build offset lookup dictionary for fast position-based queries
                _offsetsByPosition = new Dictionary<long, OffsetRecord>();
                foreach (OffsetRecord offset in _reader.GetOffsets().OrderBy(o => o.Offset))
                    _offsetsByPosition[offset.Offset] = offset;

                // Determine section size for OffsetsManager
                int sectionSize = _areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();
                AreaRecord? firstArea = _areas.FirstOrDefault();
                if (firstArea != null && firstArea.SectionSize > 0)
                    sectionSize = firstArea.SectionSize;

                // Build an OffsetsManager split by section size for fast per-section lookups
                _offsetsManager = new OffsetsManager(_reader, sectionSize: sectionSize, storeBlockSize: _storeBlockSize);
            }

            // Determine buffer size: use the LARGEST section size across all areas
            // This ensures all buffers are big enough to handle any area's section size
            _bufferSize = _areas.Where(a => a.SectionSize > 0).Select(a => a.SectionSize).DefaultIfEmpty(0).Max();

            // Initialize position at start of first area (or 0 if no areas)
            _position = _areas.Count > 0 ? _areas[0].Offset : 0;
            _hashedPosition = _position;
            // initialize area state by reporting entry into the starting area (oldArea == null)
            updateCurrentArea(previousPosition: -1);

            // If the block provider is an AuxBlockProvider, merge offset records from both
            // the split reader and aux reader so that blocks stored in auxiliary stores are
            // included in the offset map for reconstruction.
            if (_blockProvider is AuxBlockProvider auxProvider)
            {
                // Merge split store offsets first (per-game filler)
                if (auxProvider.SplitReader != null)
                {
                    try
                    {
                        _offsetsManager.MergeAuxOffsets(auxProvider.SplitReader);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"MergeAuxOffsets (split) failed for image '{_reader.Image?.Name}': {ex}");
                    }
                }

                // Merge shared aux store offsets (video partition)
                if (auxProvider.AuxReader != null)
                {
                    try
                    {
                        _offsetsManager.MergeAuxOffsets(auxProvider.AuxReader);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"MergeAuxOffsets failed for image '{_reader.Image?.Name}': {ex}");
                        // Continue without aux offsets — blocks in aux produce zeros
                    }
                }
            }

            // Prefer the cache owned by ImageReader (if available). Sharing the same
            // per-image cache prevents cross-instance interference when multiple
            // ImageBuilder/ImageReader pairs operate on the same image.
            // Use a local per-builder cache to avoid shared static cache state causing
            // interference between test runs. This keeps behavior deterministic for tests
            // and does not attempt to manage global cache lifetimes.
            // The local cache maps (areaId<<32|bufferIndex) => BufferCacheEntry
            // It intentionally does not implement eviction; tests use small numbers.
        }



        /// <summary>
        /// Gets the current size of the buffer cache (for testing purposes).
        /// </summary>
        internal int BufferCacheSize => _sharedCache != null ? _sharedCache.Count : (_localCache?.Count ?? 0);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length => _reader.Image.Size;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > Length)
                    throw new ArgumentOutOfRangeException(nameof(value), "Position must be within stream bounds");

                long previous = _position;
                _position = value;
                updateCurrentArea(previous);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (_position >= Length)
                return 0;

            int totalRead = 0;
            int remaining = count;

            while (remaining > 0 && _position < Length)
            {
                // Determine which area we're in
                AreaRecord? area = getAreaAtPosition(_position);
                if (area == null)
                    break;

                long maxFromOriginalArea = area.Offset + area.Size - _position;

                // Get or create the buffer; bufferOffset is position relative to start of the canonical buffer
                BufferCacheEntry cachedBuffer = getOrCreateBuffer(area, _position, out int bufferOffset);

                // Calculate how much we can read from this buffer, clamped to the original area boundary
                int availableInBuffer = (int)Math.Min(Math.Min(cachedBuffer.ValidDataSize - bufferOffset, remaining), maxFromOriginalArea);
                if (availableInBuffer <= 0)
                    break;

                // Copy data from cached buffer to output
                Array.Copy(cachedBuffer.Data, bufferOffset, buffer, offset + totalRead, availableInBuffer);

                if (OnDataRead != null)
                {
                    if (_position == _hashedPosition)
                    {
                        OnDataRead(buffer, offset + totalRead, availableInBuffer);
                        _hashedPosition += availableInBuffer;
                    }
                    else
                        OnDataRead = null; // Invalidate on seek or out of order read
                }

                totalRead += availableInBuffer;
                remaining -= availableInBuffer;
                long previousPos = _position;
                _position += availableInBuffer;

                // Update area if we crossed a boundary
                updateCurrentArea(previousPos);
            }

            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            Position = newPosition;
            return _position;
        }

        public override void Flush()
        {
            // Read-only stream, nothing to flush
        }

        public override void SetLength(long value) => throw new NotSupportedException("Cannot set length on a read-only stream");

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Cannot write to a read-only stream");

        private void updateCurrentArea(long previousPosition)
        {
            long position = _position;
            // compute old and new area from current position and cached offsets
            AreaRecord? oldArea = previousPosition >= 0 ? getAreaAtPosition(previousPosition) : null;
            AreaRecord? newArea = getAreaAtPosition(position);

            // if the area changed, notify derived class (do not store mutable area state)
            if (newArea != null && (newArea.Id != oldArea?.Id))
                OnAreaChanged(newArea, oldArea); // Notify derived class of area change
        }

        private AreaRecord? getAreaAtPosition(long position)
        {
            // Binary search would be more efficient for many areas
            foreach (AreaRecord area in _areas)
            {
                if (position >= area.Offset && position < area.Offset + area.Size)
                    return area;
            }
            return null;
        }

        /// <summary>
        /// Called when the stream position moves into a different area.
        /// Derived classes can override this to perform area-specific initialization.
        /// </summary>
        /// <param name="newArea">The area being entered.</param>
        /// <param name="oldArea">The previous area (null if this is the first area).</param>
        protected virtual void OnAreaChanged(AreaRecord newArea, AreaRecord? oldArea)
        {
            // Base implementation does nothing
        }

        /// <summary>
        /// Called when a buffer has been fully populated by PopulateBuffer.
        /// Derived classes can override to perform post-processing such as hashing or encryption.
        /// </summary>
        /// <param name="validSize">Number of bytes in the buffer array that contain valid strided data.</param>
        /// <param name="context">Per-buffer context containing area, offsets and other useful values.</param>
        protected virtual void OnBufferComplete(int validSize, BufferContext context)
        {
            // Base implementation does nothing
        }

        /// <summary>
        /// Hook invoked after a buffer has been populated and the corresponding SectionRecord
        /// has been resolved. Derived classes can override to apply stored corrections (for
        /// example, BlockPadding/hash blocks) prior to standard OnBufferComplete processing.
        /// The default implementation does nothing.
        /// </summary>
        protected virtual void OnBufferPopulatedWithSection(int validSize, BufferContext context)
        {
            // default: no-op
        }

        /// <summary>
        /// Gets the effective section size for an area.
        /// Prefers the area's stored SectionSize, falls back to global _bufferSize.
        /// </summary>
        private int getEffectiveSectionSize(AreaRecord area) => area.SectionSize > 0 ? area.SectionSize : _bufferSize;

        /// <summary>
        /// Translates an absolute image offset to a canonical source offset for population.
        /// Override in derived classes to redirect section population to a deduplicated source address.
        /// The default implementation returns <paramref name="imageOffset"/> unchanged.
        /// </summary>
        /// <param name="imageOffset">The absolute image offset to translate.</param>
        /// <param name="size">The size of the section being populated.</param>
        protected virtual long TranslateImageOffset(long imageOffset, long size) => imageOffset;

        private BufferCacheEntry getOrCreateBuffer(AreaRecord area, long absolutePosition, out int bufferOffset)
        {
            // Translate the actual position to canonical coordinates.
            // Using the real position (not section-aligned) means bufferOffset stays within
            // validDataSize even when the Other area's section is larger than the canonical section.
            long canonicalPosition = TranslateImageOffset(absolutePosition, getEffectiveSectionSize(area));

            AreaRecord? canonicalArea = getAreaAtPosition(canonicalPosition);
            if (canonicalArea == null)
                canonicalArea = area;

            int canonicalSectionSize = getEffectiveSectionSize(canonicalArea);
            long canonicalRelOffset = canonicalPosition - canonicalArea.Offset;
            long bufferIndex = canonicalRelOffset / canonicalSectionSize;
            long areaRelativeStart = bufferIndex * canonicalSectionSize;
            long absoluteStart = canonicalArea.Offset + areaRelativeStart;

            bufferOffset = (int)(canonicalPosition - absoluteStart);
            area = canonicalArea;

            long cacheKey = getBufferKey(area.Id, bufferIndex);

            // Delegate to shared cache when available
            if (_sharedCache != null)
            {
                AreaRecord factoryArea = area;
                long factoryAbsoluteStart = absoluteStart;
                long factoryBufferDataSize = Math.Min(getEffectiveSectionSize(factoryArea), factoryArea.Size - areaRelativeStart);

                const int maxSharedRetries = 3;
                for (int sharedAttempt = 0; sharedAttempt < maxSharedRetries; sharedAttempt++)
                {
                    BufferCacheEntry sharedEntry = _sharedCache.GetOrCreate(cacheKey, () =>
                    {
                        BufferCacheEntry entry = new BufferCacheEntry(_bufferSize, factoryArea.GetDataStride())
                        {
                            AreaId = factoryArea.Id,
                            BufferIndex = bufferIndex
                        };

                        if (factoryBufferDataSize <= 0)
                        {
                            entry.ValidDataSize = 0;
                            entry.SetReady();
                            return entry;
                        }

                        entry.ValidDataSize = (int)factoryBufferDataSize;

                        BufferContext ctx = new BufferContext
                        {
                            Area = factoryArea,
                            Buffer = entry.Data,
                            BufferEntry = entry,
                            Stride = entry.Stride,
                            ImageOffset = factoryAbsoluteStart,
                            AreaOffset = areaRelativeStart,
                            AreaFsOffset = entry.Stride.OffsetToClean(areaRelativeStart),
                            BufferCleanSize = entry.Stride.GetCleanSize(areaRelativeStart, factoryBufferDataSize)
                        };

                        try
                        {
                            populateBuffer(ctx);
                            try
                            {
                                OnBufferPopulatedWithSection(entry.ValidDataSize, ctx);
                                OnBufferComplete(entry.ValidDataSize, ctx);
                            }
                            catch { }

                            entry.SetReady();
                        }
                        catch (Exception)
                        {
                            entry.SetFailed();
                            throw;
                        }

                        return entry;
                    });

                    // Wait for readiness if another thread is populating
                    if (!sharedEntry.IsReady)
                    {
                        bool ready = sharedEntry.WaitReady();
                        if (!ready || sharedEntry.IsFailed)
                        {
                            // Timed out or failed — retry (the cache internally removes failed entries)
                            continue;
                        }
                    }
                    else if (sharedEntry.IsFailed)
                    {
                        // Entry was marked failed before we checked — retry
                        continue;
                    }

                    return sharedEntry;
                }

                // Exhausted retries — make one final attempt that will throw on failure
                BufferCacheEntry finalEntry = _sharedCache.GetOrCreate(cacheKey, () =>
                {
                    BufferCacheEntry entry = new BufferCacheEntry(_bufferSize, factoryArea.GetDataStride())
                    {
                        AreaId = factoryArea.Id,
                        BufferIndex = bufferIndex
                    };

                    if (factoryBufferDataSize <= 0)
                    {
                        entry.ValidDataSize = 0;
                        entry.SetReady();
                        return entry;
                    }

                    entry.ValidDataSize = (int)factoryBufferDataSize;

                    BufferContext ctx = new BufferContext
                    {
                        Area = factoryArea,
                        Buffer = entry.Data,
                        BufferEntry = entry,
                        Stride = entry.Stride,
                        ImageOffset = factoryAbsoluteStart,
                        AreaOffset = areaRelativeStart,
                        AreaFsOffset = entry.Stride.OffsetToClean(areaRelativeStart),
                        BufferCleanSize = entry.Stride.GetCleanSize(areaRelativeStart, factoryBufferDataSize)
                    };

                    try
                    {
                        populateBuffer(ctx);
                        try
                        {
                            OnBufferPopulatedWithSection(entry.ValidDataSize, ctx);
                            OnBufferComplete(entry.ValidDataSize, ctx);
                        }
                        catch { }

                        entry.SetReady();
                    }
                    catch (Exception)
                    {
                        entry.SetFailed();
                        throw;
                    }

                    return entry;
                });

                if (!finalEntry.IsReady)
                    finalEntry.WaitReady();

                return finalEntry;
            }

            // Use local per-builder LRU cache
            if (_localCache!.TryGetValue(cacheKey, out BufferCacheEntry? existing))
            {
                // Move to most-recent
                if (_lruNodes!.TryGetValue(cacheKey, out LinkedListNode<long>? node))
                {
                    _lruList!.Remove(node);
                    _lruList.AddFirst(node);
                }
                return existing;
            }

            // Evict if necessary to make room for the new buffer
            while (_localCache!.Count >= _maxCacheSize)
            {
                LinkedListNode<long>? tail = _lruList!.Last;
                if (tail == null)
                    break;

                long removeKey = tail.Value;
                _lruList.RemoveLast();
                _lruNodes!.Remove(removeKey);
                if (_localCache.TryGetValue(removeKey, out BufferCacheEntry? removedEntry))
                    _localCache.Remove(removeKey); // No special disposal required for BufferCacheEntry
            }

            // Create and register placeholder entry BEFORE population to prevent re-entrancy
            BufferCacheEntry entry = new BufferCacheEntry(_bufferSize, area.GetDataStride())
            {
                AreaId = area.Id,
                BufferIndex = bufferIndex
            };

            LinkedListNode<long> newNode = new LinkedListNode<long>(cacheKey);
            _lruList!.AddFirst(newNode);
            _lruNodes![cacheKey] = newNode;
            _localCache![cacheKey] = entry;

            // Calculate buffer data size from canonical area coordinates
            long bufferDataSize = Math.Min(getEffectiveSectionSize(area), area.Size - areaRelativeStart);
            if (bufferDataSize <= 0)
            {
                entry.ValidDataSize = 0;
                return entry;
            }

            entry.ValidDataSize = (int)bufferDataSize;

            BufferContext ctx = new BufferContext
            {
                Area = area,
                Buffer = entry.Data,
                BufferEntry = entry,
                Stride = entry.Stride,
                ImageOffset = absoluteStart,
                AreaOffset = areaRelativeStart,
                AreaFsOffset = entry.Stride.OffsetToClean(areaRelativeStart),
                BufferCleanSize = entry.Stride.GetCleanSize(areaRelativeStart, bufferDataSize)
            };
            populateBuffer(ctx);
            try
            {
                OnBufferPopulatedWithSection(entry.ValidDataSize, ctx);
                OnBufferComplete(entry.ValidDataSize, ctx);
            }
            catch { }

            // Post-population eviction: population may have caused nested buffer creations
            // (re-entrancy). Ensure we trim any excess so the cache does not grow past the limit.
            while (_localCache!.Count > _maxCacheSize)
            {
                LinkedListNode<long>? tail2 = _lruList!.Last;
                if (tail2 == null)
                    break;
                long removeKey2 = tail2.Value;
                _lruList.RemoveLast();
                _lruNodes!.Remove(removeKey2);
                if (_localCache.TryGetValue(removeKey2, out BufferCacheEntry? removedEntry2))
                    _localCache.Remove(removeKey2);
            }

            return entry;
        }

        private void populateBuffer(BufferContext context)
        {
            BufferCacheEntry buffer = context.BufferEntry as BufferCacheEntry ?? throw new InvalidOperationException("BufferEntry required in BufferContext");
            AreaRecord area = context.Area;
            // Use values already prepared in the BufferContext to avoid recomputation
            DataStride areaStrideLocal = context.Stride;
            long bufFsOffset = context.AreaFsOffset;
            long bufFsSize = context.BufferCleanSize;
            long bufFsEnd = bufFsOffset + bufFsSize;

            //Debug.WriteLine($"Populating buffer for Area {area.Id}, ImageOffset=0x{context.ImageOffset:X} BufferIndex {buffer.BufferIndex}");
            //Debug.WriteLine($"Populating buffer for Area {area.Id}, BufferIndex {buffer.BufferIndex}, FsRange [{bufFsOffset:X}-{bufFsEnd:X})");

            // Cache FS type lookup once
            string? fsType = area.Metadata.GetString(AreaValueType.FsType);
            bool isOther = string.Equals(fsType, "Other", StringComparison.OrdinalIgnoreCase);
            bool isFileSystem = string.Equals(fsType, "FileSystem", StringComparison.OrdinalIgnoreCase);

            SectionRecord? section = _offsetsManager.GetSectionByImageOffset(context.ImageOffset);
            context.Section = section;
            if (section == null)
            {
                OnGapFill(0, bufFsOffset, 0, bufFsSize, context);
                return;
            }

            // prevFileEndFs is taken directly from the precomputed Section.GapOffset
            long prevFileEndFs = section.GapOffset;
            IReadOnlyList<OffsetSegment> segments = section.Segments;
            if (segments == null || segments.Count == 0)
            {
                long cleanGapSize = bufFsSize;
                long offsetInGap = isOther ? bufFsOffset : (section.GapOffset > 0) ? Math.Max(0, bufFsOffset - section.GapOffset) : 0;
                OnGapFill(offsetInGap, bufFsOffset, 0, cleanGapSize, context);
                return;
            }

            // Determine previous file end (area-FS) from the section's precomputed GapOffset
            long lastProcessedPosition = bufFsOffset;
            bool lastWas0ByteFile = false;

            // Collect gap ranges encountered during first pass so second pass can overlay Other-data
            List<(long cleanStart, long bufferOffset, long size)> gapRanges = new List<(long cleanStart, long bufferOffset, long size)>();

            // FIRST PASS: Process primary segments based on area type
            // FileSystem areas: File and FileSystem types only
            // Other areas: nothing (all will be gaps for second pass)
            // Regular areas: all segments
            foreach (OffsetSegment seg in segments.Where(a => (isFileSystem && (a.Source.Type == BlockType.File || a.Source.Type == BlockType.FileSystem)) || (!isFileSystem && !isOther)))
            {
                OffsetRecord src = seg.Source;

                // compute clean-area start of this segment: clean start of source + seg.SourceOffset
                long srcCleanBase = areaStrideLocal.OffsetToClean(src.Offset - area.Offset);
                long segCleanStart = srcCleanBase + seg.SourceOffset;
                long segCleanEnd = segCleanStart + seg.Size;

                long writeStart = Math.Max(segCleanStart, bufFsOffset);
                long writeEnd = Math.Min(segCleanEnd, bufFsEnd);

                // gap before this segment
                if (lastProcessedPosition < writeStart)
                {
                    long gapSize = writeStart - lastProcessedPosition;
                    long gapOffsetInBuffer = lastProcessedPosition - bufFsOffset;
                    // compute offset-in-gap for this buffer start: bytes into gap already consumed before this fragment
                    long offsetInGap = 0;
                    if (section.GapOffset > 0 && lastProcessedPosition == bufFsOffset && !lastWas0ByteFile) // 0 byte files make it look like we need to add the gap
                        offsetInGap = Math.Max(0, lastProcessedPosition - prevFileEndFs);
                    //Debug.WriteLine($"[PopulateBuffer] Pre-segment gap: Section.GapOffset=0x{section.GapOffset:X}, PrevFileEndFs=0x{prevFileEndFs:X}, FragStart=0x{lastProcessedPosition:X}, OffsetInGap=0x{offsetInGap:X}, Size=0x{gapSize:X}");
                    // Record the gap range and notify consumer
                    gapRanges.Add((lastProcessedPosition, gapOffsetInBuffer, gapSize));
                    // gap fragment begins at clean-area offset = lastProcessedPosition, and maps to buffer offset gapOffsetInBuffer
                    OnGapFill(offsetInGap, lastProcessedPosition, gapOffsetInBuffer, gapSize, context);
                }

                // pass stored-based base to PopulatePhysicalData
                long absCleanRecordBase = srcCleanBase;
                populatePhysicalData(src, writeStart, writeEnd, context);

                lastProcessedPosition = writeEnd;
                lastWas0ByteFile = seg.Size == 0;
            }

            // final gap to buffer end
            if (lastProcessedPosition < bufFsEnd)
            {
                long gapSize = bufFsEnd - lastProcessedPosition;
                long gapOffsetInBuffer = lastProcessedPosition - bufFsOffset;
                long offsetInGap = isOther ? lastProcessedPosition : (section.GapOffset > 0 && lastProcessedPosition == bufFsOffset ? Math.Max(0, lastProcessedPosition - prevFileEndFs) : 0);
                gapRanges.Add((lastProcessedPosition, gapOffsetInBuffer, gapSize));
                OnGapFill(offsetInGap, lastProcessedPosition, gapOffsetInBuffer, gapSize, context);
            }

            // SECOND PASS: For FileSystem and Other area types, overlay stored BlockType.Other data onto gaps
            // This pass uses segments already available - no need to re-fetch from _offsetsManager
            if ((isFileSystem || isOther) && gapRanges.Count > 0)
            {
                // Process Other segments from the existing segments collection
                foreach (OffsetSegment seg in segments.Where(a => a.Source.Type == BlockType.Other && a.Source.HasBlocks))
                {
                    long srcCleanBase = areaStrideLocal.OffsetToClean(seg.Source.Offset - area.Offset);
                    long segCleanStart = srcCleanBase + seg.SourceOffset;
                    long segCleanEnd = segCleanStart + seg.Size;

                    // Clip to this buffer's clean range
                    long writeStart = Math.Max(segCleanStart, bufFsOffset);
                    long writeEnd = Math.Min(segCleanEnd, bufFsEnd);
                    if (writeStart >= writeEnd)
                        continue;

                    // For each recorded file-to-file gap, overlay any overlap with this Other segment
                    foreach ((long cleanStart, long bufferOffset, long size) gap in gapRanges)
                    {
                        long fragStart = gap.cleanStart;
                        long fragEnd = gap.cleanStart + gap.size;
                        long overlapStart = Math.Max(fragStart, writeStart);
                        long overlapEnd = Math.Min(fragEnd, writeEnd);
                        if (overlapStart >= overlapEnd)
                            continue;

                        try
                        {
                            // Use the same absCleanRecordBase calculation used elsewhere
                            long absCleanRecordBase = srcCleanBase;
                            populatePhysicalData(seg.Source, overlapStart, overlapEnd, context);
                        }
                        catch { }
                    }
                }
            }

            // Update ValidDataSize from MaxWrittenOffset if any writes occurred (MaxWrittenOffset is in same coords as MarkWritten)
            if (buffer.MaxWrittenOffset > 0)
            {
                int maxWritten = buffer.MaxWrittenOffset;
                if (maxWritten > buffer.Data.Length)
                    maxWritten = buffer.Data.Length;
                // Use the actual written extent; do not shrink below the expected bufferDataSize.
                // Keep the larger of the originally expected valid size and the max written offset.
                int expected = buffer.ValidDataSize; // previously set to bufferDataSize
                buffer.ValidDataSize = Math.Max(expected, Math.Min(maxWritten, buffer.Data.Length));
                //Debug.WriteLine($"    PopulateBuffer: ImageOffset=0x{context.ImageOffset:X} MaxWrittenOffset=0x{maxWritten:X}, Final ValidDataSize=0x{buffer.ValidDataSize:X}");
            }
        }

        private void populatePhysicalData(OffsetRecord offsetRecord, long offsetStart, long offsetEnd, BufferContext context)
        {
            if (!offsetRecord.HasBlocks)
                return;

            // Obtain buffer entry and area from context
            BufferCacheEntry buffer = context.BufferEntry as BufferCacheEntry ?? throw new InvalidOperationException("BufferEntry required in BufferContext");
            AreaRecord area = context.Area;
            DataStride stride = buffer.Stride;
            long absCleanRecordBase = stride.OffsetToClean(offsetRecord.Offset - area.Offset);

            // Determine clean offsets within the record
            long cleanOffsetInRecordStart = offsetStart - absCleanRecordBase;
            long cleanOffsetInRecordEnd = offsetEnd - absCleanRecordBase;

            // Clamp to record clean size
            long recordCleanSize = offsetRecord.Size;
            if (cleanOffsetInRecordStart < 0)
                cleanOffsetInRecordStart = 0;
            if (cleanOffsetInRecordEnd > recordCleanSize)
                cleanOffsetInRecordEnd = recordCleanSize;

            long cleanBytesToCopy = Math.Max(0, cleanOffsetInRecordEnd - cleanOffsetInRecordStart);
            if (cleanBytesToCopy == 0)
                return;

            // Compute buffer-relative CLEAN start (where within this buffer's clean view we should write)
            // Prefer clean offset from context when available (more accurate if stride or area changed)
            long bufFsOffset = context.AreaFsOffset;
            long cleanBufferOffsetForWrite = offsetStart - bufFsOffset; // clean bytes offset inside buffer

            //Debug.WriteLine($"      Populating physical data from blocks: CLEAN [{cleanOffsetInRecordStart:X}-{cleanOffsetInRecordEnd:X}), BufferCleanOffset 0x{cleanBufferOffsetForWrite:X}");

            // Sequentially read blocks and copy the required clean range into the buffer.
            long remainingToWrite = cleanBytesToCopy;
            long posInRecord = cleanOffsetInRecordStart; // position within the offset record (clean coords)

            while (remainingToWrite > 0)
            {
                int blockIndex = (int)(posInRecord / _storeBlockSize);
                int srcOffsetInBlockActual = (int)(posInRecord % _storeBlockSize);

                // Get the block key at this index
                BlockKey key;
                try
                {
                    key = offsetRecord.GetBlockAt(blockIndex);
                }
                catch
                {
                    break;
                }

                BlockRecord? blockRecord = _blockProvider.GetBlock(offsetRecord, blockIndex);
                if (blockRecord == null || blockRecord.Data == null)
                {
                    // advance to next block
                    long advance = _storeBlockSize - srcOffsetInBlockActual;
                    posInRecord += advance;
                    remainingToWrite -= Math.Min(remainingToWrite, advance);
                    continue;
                }

                int avail = blockRecord.Data.Length - srcOffsetInBlockActual;
                if (avail <= 0)
                {
                    posInRecord += _storeBlockSize - srcOffsetInBlockActual;
                    remainingToWrite -= Math.Min(remainingToWrite, _storeBlockSize - srcOffsetInBlockActual);
                    continue;
                }

                int toCopy = (int)Math.Min(remainingToWrite, avail);

                // Destination in buffer (clean coordinates): start at cleanBufferOffsetForWrite plus offset into requested range
                long destCleanStart = cleanBufferOffsetForWrite + (posInRecord - cleanOffsetInRecordStart);

                // Defensive clamp
                if (destCleanStart < 0 || destCleanStart + toCopy > buffer.ValidDataSize)
                {
                    if (destCleanStart >= buffer.ValidDataSize)
                        break;
                    toCopy = (int)Math.Min(toCopy, buffer.ValidDataSize - destCleanStart);
                }

                copy(blockRecord.Data, srcOffsetInBlockActual, buffer.Data, (int)destCleanStart, buffer.Stride, toCopy);
                buffer.MarkWritten((int)destCleanStart, toCopy, "block");
                //Debug.WriteLine($"        WROTE (seq): blockIndex={blockIndex}, srcOff=0x{srcOffsetInBlockActual:X}, len=0x{toCopy:X}, dest=0x{destCleanStart:X}");

                posInRecord += toCopy;
                remainingToWrite -= toCopy;
            }
        }

        private long getBufferKey(long areaId, long bufferIndex) => (areaId << 32) | (uint)bufferIndex;

        /// <summary>
        /// Resolves the OffsetStart grouping key for the offset record that contains the given
        /// absolute image position. OpenStream expects an OffsetStart value, not an arbitrary position.
        /// </summary>
        private long? resolveOffsetStartForPosition(long absolutePosition)
        {
            IEnumerable<OffsetRecord> exactMatch = _reader.GetOffsets(absolutePosition);
            if (exactMatch.Any())
                return absolutePosition;

            IEnumerable<OffsetRecord> containing = _reader.GetOffsetsInRange(absolutePosition, 1);
            if (containing.Any())
                return containing.First().OffsetStart;

            return null;
        }

        /// <summary>
        /// New overload: callers should supply offsetInGap (number of clean bytes already consumed from the start of the gap
        /// before this fragment), the fragment's clean-area offset (cleanAreaOffset) and cleanBufferOffset (destination offset inside the buffer's clean coordinates).
        /// If offsetInGap == 0 this fragment starts at the beginning of the gap.
        /// Default implementation writes zeros into the requested buffer region.
        /// </summary>
        protected virtual void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, BufferContext context)
        {
            //Debug.WriteLine($"    OnGapFill: areaId={context.Area.Id}, offsetInGap=0x{offsetInGap:X}, cleanAreaOffset=0x{cleanAreaOffset:X}, cleanBufferOffset=0x{cleanBufferOffset:X}, size=0x{size:X}");
            if (size > 0)
            {
                // Use shared ArrayPool to avoid large temporary allocations when filling gaps.
                ArrayPool<byte> pool = ArrayPool<byte>.Shared;
                int toWrite = (int)size;
                byte[]? rented = null;
                try
                {
                    rented = pool.Rent(toWrite);
                    // Ensure rented region is zeroed only for the requested length
                    Array.Clear(rented, 0, toWrite);
                    WriteToBuffer(rented, 0, cleanBufferOffset, toWrite, context);
                    //Debug.WriteLine($"    OnGapFill: wrote zeros to cleanBufferOffset=0x{cleanBufferOffset:X}, size=0x{size:X}");
                }
                finally
                {
                    if (rented != null)
                        pool.Return(rented);
                }
            }
        }

        /// <summary>
        /// Writes data to the buffer being assembled using clean (non-strided) addresses.
        /// </summary>
        protected void WriteToBuffer(byte[] source, int sourceOffset, long cleanBufferOffset, int length, BufferContext context)
        {
            BufferCacheEntry current = context.BufferEntry as BufferCacheEntry
                ?? throw new InvalidOperationException("BufferEntry required in BufferContext.");
            // Validate against actual physical buffer size (defensive). Use Data.Length
            // because writes are performed into the physical strided array offsets.
            if (cleanBufferOffset < 0 || cleanBufferOffset + length > current.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(cleanBufferOffset),
                    $"Write would exceed buffer bounds. Buffer size: {current.Data.Length}, offset: {cleanBufferOffset}, length: {length}");

            copy(source, sourceOffset, current.Data, (int)cleanBufferOffset, current.Stride, length);
            // Record that this region of the buffer has been populated by generated data so later
            // stages (e.g., ApplyStoredCorrections) see the correct written ranges.
            try
            {
                current.MarkWritten((int)cleanBufferOffset, length, "generated");
            }
            catch { }
        }

        private void copy(byte[] src, int srcOffset, byte[] dst, int dstOffset, DataStride dstStride, int copyLen)
        {
            int origCopy = copyLen;
            int sOff = srcOffset; // (int)FsOffsetToOffset(srcOffset, srcBlockSize, srcBlockFsOffset, srcBlockFsSize, false);
            int sBlk = 0x8000;
            int sNxt = 0x8000 - sBlk;
            int sEnd = 0 + 0x8000;
            int sRmn = sEnd - (sOff % 0x8000);

            int dOff = (int)dstStride.CleanToOffset(dstOffset, false);
            int dBlk = dstStride.DataLength;
            int dNxt = dstStride.SourceBlockSize - dBlk;
            int dEnd = dstStride.DataOffset + dstStride.DataLength;
            int dRmn = dEnd - (dOff % dstStride.SourceBlockSize);

            int sz;
            while (copyLen != 0)
            {
                sz = Math.Min((sRmn < dRmn) ? sRmn : dRmn, copyLen); //which is least

                Array.Copy(src, sOff, dst, dOff, sz);

                sOff += sz;
                sRmn -= sz;
                if (sRmn == 0)
                {
                    sRmn = sBlk;
                    sOff += sNxt;
                }

                dOff += sz;
                dRmn -= sz;
                if (dRmn == 0)
                {
                    dRmn = dBlk;
                    dOff += dNxt;
                }

                copyLen -= sz;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_blockProvider as IDisposable)?.Dispose();

                // Clear local cache entries when using local cache (no global release is necessary).
                // When using shared cache, do NOT dispose it — the caller manages its lifetime.
                _localCache?.Clear();
                if (_disposeReader)
                {
                    try
                    {
                        _reader.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
            base.Dispose(disposing);
        }

    }
}