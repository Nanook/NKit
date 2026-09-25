using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    internal class DataStoreWiiFormatter : IDataStoreSystemFormatter
    {
        private readonly IImageWriter _imageWriter;
        private readonly IStepContext _context;
        private readonly IDataStore _dataStore;
        private long _discJunkNulls;
        private List<GapRange> _storedRanges;

        // Aux DataStore and writer for routing update partition blocks to a sidecar store.
        // Opened when auxSetName is provided (convention-discovered by DedupeStep).
        private readonly IDataStore _auxDataStore;
        private readonly IImageWriter _auxWriter;

        // Cached update partition offset ranges for routing Other sections to aux.
        // Populated when the ImageHeader section is processed.
        private List<(long Start, long End)> _updatePartitionRanges;

        public string ImageFileName { get; }

        public bool HasAux => _auxWriter != null;

        public DataStoreWiiFormatter(string dedupePath, string imageName, long shardSize, IStepContext context, int blockSize = 0, string setName = null, string auxSetName = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(dedupePath)) throw new ArgumentNullException(nameof(dedupePath));

            // ensure directory exists
            if (!Directory.Exists(dedupePath))
                Directory.CreateDirectory(dedupePath);

            _storedRanges = new List<GapRange>();
            string resolvedSetName = setName ?? context.SystemType.ToString();
            _dataStore = new NKitDataStore.DataStore(dedupePath);
            if (_dataStore.GetSetInfo(resolvedSetName) == null)
                _dataStore.CreateSet(resolvedSetName, shardSize, blockSize);

            ImageFileName = Container.DataStoreAsIso.GetImageFileName(imageName, ImageFormat.Iso);
            _imageWriter = _dataStore.AddImage(resolvedSetName, imageName, context.SystemType.ToString(), ImageFormat.Iso);
            _imageWriter.CompressionParallelism = 16;

            // Aux writer setup — aux set discovered by convention in DedupeStep
            if (!string.IsNullOrEmpty(auxSetName))
            {
                _auxDataStore = new NKitDataStore.DataStore(dedupePath);
                if (_auxDataStore.GetSetInfo(auxSetName) == null)
                    _auxDataStore.CreateSet(auxSetName, 50L * 1024 * 1024 * 1024, blockSize);
                _auxWriter = _auxDataStore.AddImage(auxSetName, imageName, context.SystemType.ToString(), ImageFormat.Iso);
                _auxWriter.CompressionParallelism = 16;
            }

            // keep zero-filled fallbacks; per-partition scrub patterns will be cached when PartitionHeader sections are persisted
        }

        public DataStride GetStrideForPartition(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.AreaInfo?.Properties != null && a.AreaInfo.Properties["Partition"] != null && (int)a.AreaInfo.Properties["Partition"] == partitionId);
            if (area == null)
                return null;

            AreaInfo ai = area.AreaInfo;
            if (ai.BlockSize != ai.BlockFsSize)
            {
                return new DataStride
                {
                    SourceBlockSize = ai.BlockSize,
                    DataOffset = ai.BlockFsOffset,
                    DataLength = ai.BlockFsSize
                };
            }

            return null;
        }

        public long GetPartitionImageOffset(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.AreaInfo?.Properties != null && a.AreaInfo.Properties["Partition"] != null && (int)a.AreaInfo.Properties["Partition"] == partitionId);
            return area?.ImageOffset ?? 0;
        }

        public AreaMetadata BuildAreaMetadata(ScanArea scanArea)
        {
            if (scanArea == null)
                return null;

            Properties props = scanArea.AreaInfo?.Properties;
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, scanArea.Type.ToString());

            try
            {
                switch (scanArea.Type)
                {
                    case AreaType.ImageHeader:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.ID, (props["ID"] as string) ?? "");
                            metadata.Set(AreaValueType.DiscNo, props["DiscNo"] is int di ? di : 0);
                            metadata.Set(AreaValueType.Revision, props["Revision"] is int rev ? rev : 0);
                            metadata.Set(AreaValueType.Region, (props["Region"] as string) ?? "");
                            metadata.Set(AreaValueType.Title, (props["Title"] as string) ?? "");
                            metadata.Set(AreaValueType.Partitions, props["Partitions"] is int p ? p : 0);
                        }
                        break;
                    case AreaType.PartitionHeader:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int pi ? pi : 0);
                            metadata.Set(AreaValueType.PartitionType, (props["PartitionType"] as string) ?? "");
                            metadata.Set(AreaValueType.ContentSha, (props["ContentSha"] as string) ?? "");
                            metadata.Set(AreaValueType.CommonKeyCrc, props["CommonKeyCrc"] is uint ck ? (long)ck : 0);
                            metadata.Set(AreaValueType.TitleKeyCrc, props["TitleKeyCrc"] is uint tk ? (long)tk : 0);
                            if (props["Signed"] != null)
                                metadata.Set(AreaValueType.Signed, (props["Signed"] as string) ?? "");
                        }
                        break;
                    case AreaType.FileSystem:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int pi2 ? pi2 : 0);
                            metadata.Set(AreaValueType.ID, (props["ID"] as string) ?? "");
                            metadata.Set(AreaValueType.DiscNo, props["DiscNo"] is int dn ? dn : 0);
                            metadata.Set(AreaValueType.Revision, props["Revision"] is int rv ? rv : 0);
                            metadata.Set(AreaValueType.Title, (props["Title"] as string) ?? "");
                            metadata.Set(AreaValueType.Encrypted, props["Encrypted"] is bool enc ? enc : false);
                            metadata.Set(AreaValueType.HasFileSystem, props["HasFileSystem"] is bool hfs ? hfs : false);
                            metadata.Set(AreaValueType.SystemDataCrc, props["SystemDataCrc"] is uint sdc ? (long)sdc : 0);
                            metadata.Set(AreaValueType.JunkID, (props["JunkID"] as string) ?? "");
                            metadata.Set(AreaValueType.JunkLeadingNulls, props["JunkLeadingNulls"] is ulong jln ? (long)jln : 0);
                            metadata.Set(AreaValueType.JunkEndNullsOffset, props["JunkEndNullsOffset"] is ulong jeno ? (long)jeno : 0);
                        }
                        break;
                    case AreaType.Other:
                        if (props != null)
                        {
                            metadata.Set(AreaValueType.UpdatePartitionRemoved, props["UpdatePartitionRemoved"] is bool upr ? upr : false);
                            metadata.Set(AreaValueType.Partition, props["Partition"] is int p3 ? p3 : 0);
                            if (props["JunkID"] != null)
                                metadata.Set(AreaValueType.JunkID, (props["JunkID"] as string) ?? "");
                        }
                        break;
                    default:
                        break;
                }
            }
            catch
            {
                // ignore metadata failures
            }

            return metadata;
        }

        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null)
        {
            // Route file writes to aux when the offset falls within an update partition range
            IImageWriter writer = (_auxWriter != null && isWithinUpdatePartitionRange(imageOffset))
                ? _auxWriter
                : _imageWriter;
            return writer.BeginWriteStream(imageOffset, type, imageOffset, stride, strideOriginOffset);
        }

        /// <summary>
        /// Begins a file write using the specified writer (primary or aux).
        /// </summary>
        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, IImageWriter writer, long? strideOriginOffset = null) =>
            writer.BeginWriteStream(imageOffset, type, imageOffset, stride, strideOriginOffset);

        public void FinalizeFileWrite(long imageOffset, Stream stream) => stream?.Dispose();

        public long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset)
        {
            // Find the area representing this partition to get stride
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem && a.ImageOffset == partitionImageOffset);
            if (area == null)
                return partitionImageOffset + fsOffset;

            AreaInfo ai = area.AreaInfo;
            // Map clean fsOffset to strided image offset
            long fullBlocks = fsOffset / ai.BlockFsSize;
            int rem = (int)(fsOffset % ai.BlockFsSize);
            long imageOffset = partitionImageOffset + (fullBlocks * ai.BlockSize) + ai.BlockFsOffset + rem;
            return imageOffset;
        }

        // New overload that accepts an ISection and will format scrub mask + exceptions
        public void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride)
        {
            if (section == null)
                return;

            try
            {
                // Only valid when striding (hashes present)
                AreaInfo ai = section.AreaInfo;
                if (ai == null || ai.BlockSize == ai.BlockFsSize || ai.BlockFsSize == 0)
                    return;

                // Determine number of filesystem blocks (sectors) to cover. If caller provided a DataStride and indicates fs coordinates
                // we attempt to derive the equivalent strided size; otherwise default to a single area block.
                int sectors = (int)(section.Size / WiiConsts.WiiSectorSize);
                int sectBase = (int)((imageOffset - section.ImageOffset) / ai.BlockSize);
                byte[] state = section.State.Bytes;

                if (!state.Any(a => a != 0) && section.IsCreatable)
                    return; // nothing to store

                int stateLen = state.Length;

                int hashBlocksLen = sectors * WiiConsts.WiiSectorHashSize; // 0x400 per sector
                byte[] outb = new byte[1 + stateLen + hashBlocksLen];
                outb[0] = (byte)stateLen;
                if (stateLen > 0)
                    Array.Copy(state, 0, outb, 1, stateLen);

                // Fill hash blocks directly (one 0x400 block per sector) into outb after state
                int sectOff = sectBase * WiiConsts.WiiSectorSize;
                for (int i = 0; i < sectors; i++)
                {
                    int destPos = 1 + stateLen + (i * WiiConsts.WiiSectorHashSize);
                    Array.Copy(section.Decrypted, sectOff, outb, destPos, WiiConsts.WiiSectorHashSize);
                    sectOff += WiiConsts.WiiSectorSize;
                }

                int total = 1 + stateLen + (sectors * WiiConsts.WiiSectorHashSize);
                // BlockPadding MUST always be written to the primary writer regardless of
                // which store the section's data is routed to. During image reconstruction,
                // MergeAuxOffsets skips BlockPadding records from the aux store, so if
                // BlockPadding is written to aux it won't be available for re-encryption.
                // This prevents area CRC accumulator bleed where missing hash blocks cause
                // incorrect re-encryption during verification of subsequent images.
                _imageWriter.WriteData(imageOffset, outb, 0, total, BlockType.BlockPadding, null, imageOffset);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"PersistBlockPadding(ISection) failed at {imageOffset:X}: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines which writer (primary or aux) should receive data for the given section.
        /// Update partition sections are routed to the aux writer when available.
        /// </summary>
        private IImageWriter getWriterForSection(ISection section)
        {
            if (_auxWriter == null)
                return _imageWriter;

            // Route any section whose image offset falls within an update partition range to aux.
            // This covers PartitionHeader, FileSystem, and Other sections belonging to the update partition.
            if (isWithinUpdatePartitionRange(section.ImageOffset))
                return _auxWriter;

            return _imageWriter;
        }

        /// <summary>
        /// Checks whether the given image offset falls within any cached update partition range.
        /// </summary>
        private bool isWithinUpdatePartitionRange(long imageOffset)
        {
            if (_updatePartitionRanges == null)
                return false;
            return _updatePartitionRanges.Any(r => imageOffset >= r.Start && imageOffset < r.End);
        }

        /// <summary>
        /// Writes data using the specified writer. If the writer is the aux writer and the
        /// write fails, logs the error and falls back to writing to the primary writer.
        /// </summary>
        private void writeDataWithAuxFallback(IImageWriter writer, long imageOffset, byte[] data, int offset, int size, BlockType blockType)
        {
            if (writer == _auxWriter)
            {
                try
                {
                    writer.WriteData(imageOffset, data, offset, size, blockType);
                }
                catch (Exception ex)
                {
                    _context.Log?.Error(() => $"Aux write failed at {imageOffset:X}, falling back to primary: {ex.Message}");
                    _imageWriter.WriteData(imageOffset, data, offset, size, blockType);
                }
            }
            else
            {
                writer.WriteData(imageOffset, data, offset, size, blockType);
            }
        }

        public void ProcessSection(ISection section)
        {
            DataStride stride = new DataStride { SourceBlockSize = section.AreaInfo.BlockSize != section.AreaInfo.BlockFsSize ? section.AreaInfo.BlockSize : 0x8000, DataOffset = section.AreaInfo.BlockSize != section.AreaInfo.BlockFsSize ? section.AreaInfo.BlockFsOffset : 0, DataLength = section.AreaInfo.BlockSize != section.AreaInfo.BlockFsSize ? section.AreaInfo.BlockFsSize : 0x8000 };
            _storedRanges = null;

            if (section.Type == AreaType.ImageHeader)
            {
                ImageHeader ih = new ImageHeader(section.Decrypted, true);
                _discJunkNulls = ih.Partitions.FirstOrDefault(a => a.Type != PartitionType.Update)?.ImageOffset ?? 0;

                // Build update partition offset ranges from the ImageHeader partition table.
                // Each update partition spans from its start offset to the start of the next
                // partition (sorted by offset). This covers both the PartitionHeader and
                // FileSystem areas that belong to the update partition.
                if (_auxWriter != null && ih.Partitions.Length > 0)
                {
                    PartitionInfo[] sorted = ih.Partitions.OrderBy(p => p.ImageOffset).ToArray();
                    _updatePartitionRanges = new List<(long Start, long End)>();
                    for (int i = 0; i < sorted.Length; i++)
                    {
                        if (sorted[i].Type == PartitionType.Update)
                        {
                            long start = sorted[i].ImageOffset;
                            // End is the start of the next partition, or disc size if last
                            long end = (i + 1 < sorted.Length)
                                ? sorted[i + 1].ImageOffset
                                : _context.ImageInfo?.ImageSize ?? long.MaxValue;
                            if (end > start)
                                _updatePartitionRanges.Add((start, end));
                        }
                    }
                }

                _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
            }
            else if (section.Type == AreaType.PartitionHeader)
            {
                IImageWriter writer = getWriterForSection(section);
                writeDataWithAuxFallback(writer, section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
            }
            else if (section.Type == AreaType.Other) // Other areas the have junk / fill are not correct. Or not filled with 0
            {
                IImageWriter writer = getWriterForSection(section);
                DataType dt = _discJunkNulls != 0 && section.ImageOffset > _discJunkNulls ? DataType.NJunk : DataType.Fill;
                foreach (ISectionItem item in section.Items.Where(i => (i.Gap != null && i.Gap.DataType != dt) || i.Gap.FillByte != 0))
                {
                    IEnumerable<ISectionData> gaps = (item.GapInfo?.Count ?? 0) == 0 ? new[] { item.Gap } : item.GapInfo.Where(a => a.DataType != dt || a.FillByte != 0);
                    foreach (ISectionData gap in gaps)
                        writeDataWithAuxFallback(writer, section.ImageOffset + gap.Offset, section.Decrypted, (int)gap.FsOffset, (int)gap.FsSize, BlockType.Other);
                }
            }
            else if (section.Type == AreaType.FileSystem)
            {
                IImageWriter writer = getWriterForSection(section);
                _storedRanges = GetGapsAndNonCreatableDataRanges(section, stride).ToList();

                if (_storedRanges.Any(a => a.ImageOffset < section.ImageOffset || a.ImageOffset + a.Size > section.ImageOffset + section.Size))
                {
                    _context.Log?.Error(() => $"ProcessSection: Calculated gap/non-creatable data ranges exceed section bounds at {section.ImageOffset:X}");
                    return;
                }

                foreach (GapRange persist in _storedRanges)
                {
                    if (writer == _auxWriter)
                    {
                        try
                        {
                            Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, writer);
                            section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                            FinalizeFileWrite(persist.ImageOffset, writeStream);
                        }
                        catch (Exception ex)
                        {
                            _context.Log?.Error(() => $"Aux write failed at {persist.ImageOffset:X}, falling back to primary: {ex.Message}");
                            Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, _imageWriter);
                            section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                            FinalizeFileWrite(persist.ImageOffset, writeStream);
                        }
                    }
                    else
                    {
                        Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride, writer);
                        section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                        FinalizeFileWrite(persist.ImageOffset, writeStream);
                    }
                }
            }
        }

        public bool ShouldPreserveFile(ISection section, IFsFile file)
        {
            // Skip junk files and zero-length files - they should be invisible to gap checking
            if (file == null)
                return false;

            if (file.FsSize == 0 && (file.PostGapSize == 0 || _storedRanges.Any(a => section.FsOffset + a.FsOffset <= file.FsOffset && section.FsOffset + a.FsOffset + a.Size >= file.FsOffset)))
                return false; // we must not store this as it is covered or matchines another file

            return !file.IsMissing;
        }

        internal static List<GapRange> GetGapsAndNonCreatableDataRanges(ISection section, DataStride stride)
        {
            List<GapRange> ranges = new List<GapRange>();

            if (section.Type != AreaType.FileSystem)
                return ranges;

            long sectionFsStart = section.FsOffset;
            long sectionFsEnd = section.FsOffset + section.FsSize;

            foreach (ISectionItem item in section.Items)
            {
                // Handle pre-file gaps (gap-only items with no file, e.g. gap at start of
                // a content area before the first file). These contain data that must be preserved.
                // Note: item.Gap.FsOffset is section-relative (buffer-local), so we use it directly
                // without area-relative clipping. The gap is guaranteed to be within this section
                // since it was created from this section's buffer.
                // Skip NJunk gaps — they are regeneratable and should not be preserved.
                if (item.File == null && item.Gap != null && item.Gap.FsSize > 0 && item.Gap.DataType != DataType.NJunk)
                {
                    if (item.GapInfo == null || item.GapInfo.Count == 0)
                    {
                        // Single gap with no sub-parts — persist the whole gap
                        long imageOffset = section.ImageOffset + stride.CleanToOffset(item.Gap.FsOffset, false);
                        ranges.Add(new GapRange(imageOffset, item.Gap.FsOffset, (int)item.Gap.FsSize, DataType.Other, 0));
                    }
                    else
                    {
                        // Gap has sub-parts (GapInfo) — persist each "Other" sub-part individually
                        foreach (ISectionData gd in item.GapInfo.Where(a => a.DataType == DataType.Other))
                        {
                            long imageOffset = section.ImageOffset + stride.CleanToOffset(gd.FsOffset, false);
                            ranges.Add(new GapRange(imageOffset, gd.FsOffset, (int)gd.FsSize, DataType.Other, 0));
                        }
                    }
                }

                // preserve any post file gaps that aren't nulls (padding with 0x1c nulls - or up to gap size if less)
                if (item.File != null)
                {
                    ISectionData gap = item.Gap?.DataType == DataType.NJunk ? item.Gap : item.GapInfo.FirstOrDefault(a => a.DataType == DataType.NJunk);
                    if (gap != null && item.FsFile is FstFile file)
                    {
                        // Calculate expected creatable null count based on file ending and alignment
                        int nc = (int)Math.Min(file.PostGapSize, WiiConsts.DataNullsCount + (int)(file.Analysis.FsOffset % 4 == 0 ? 0 : (4 - (file.Analysis.FsOffset % 4))));
                        if (nc != Math.Min(file.PostGapSize, gap.DataNulls)) // Check if this matches the actual data nulls from the gap
                        {
                            long gapFsOffset = file.PostGapFsOffset;
                            long gapEndFsOffset = gapFsOffset + Math.Min(nc, file.PostGapSize);
                            if (gapFsOffset < sectionFsEnd && gapEndFsOffset > sectionFsStart) // Check if gap overlaps with this section (handles spillover from previous sections)
                            {
                                long clippedStart = Math.Max(gapFsOffset, sectionFsStart); // Clip to section boundaries
                                int clippedSize = (int)(Math.Min(gapEndFsOffset, sectionFsEnd) - clippedStart);
                                if (clippedSize > 0)
                                {
                                    long sectionRelativeOffset = clippedStart - sectionFsStart;
                                    long imageOffset = section.ImageOffset + stride.CleanToOffset(sectionRelativeOffset, false);
                                    ranges.Add(new GapRange(imageOffset, sectionRelativeOffset, clippedSize, DataType.Other, 0));
                                }
                            }
                        }
                    }

                    // Handle post-file gaps with DataType "Other" — these contain real data
                    // that cannot be regenerated (e.g. CDi/ISO9660 gap data between files).
                    // Persist each "Other" sub-part from GapInfo, or the whole gap if no sub-parts.
                    if (item.Gap != null && item.Gap.FsSize > 0 && item.Gap.DataType == DataType.Other)
                    {
                        if (item.GapInfo == null || item.GapInfo.Count == 0)
                        {
                            long imageOffset = section.ImageOffset + stride.CleanToOffset(item.Gap.FsOffset, false);
                            ranges.Add(new GapRange(imageOffset, item.Gap.FsOffset, (int)item.Gap.FsSize, DataType.Other, 0));
                        }
                        else
                        {
                            foreach (ISectionData gd in item.GapInfo.Where(a => a.DataType == DataType.Other))
                            {
                                long imageOffset = section.ImageOffset + stride.CleanToOffset(gd.FsOffset, false);
                                ranges.Add(new GapRange(imageOffset, gd.FsOffset, (int)gd.FsSize, DataType.Other, 0));
                            }
                        }
                    }
                }

                // Handle Fill gaps
                IEnumerable<ISectionData> gaps = item.Gap?.DataType == DataType.Fill ? new[] { item.Gap } : item.GapInfo?.Where(a => a.DataType == DataType.Fill) ?? Enumerable.Empty<ISectionData>();
                foreach (ISectionData gap in gaps)
                    ranges.Add(new GapRange(section.ImageOffset + gap.Offset, gap.FsOffset, (int)gap.FsSize, DataType.Fill, gap.FillByte));
            }

            foreach (NonCreatableData item in (section.NonCreatableItems ?? Enumerable.Empty<NonCreatableData>()).Where(i => i.IsFs && i.Type == NonCreatableDataType.Filler))
            {
                ISectionData md = section.Items.EnumSectionData().FirstOrDefault(a => item.Offset == a.FsOffset && item.Size == a.FsSize);
                if (md != null) // non creatable data
                    ranges.Add(new GapRange(item.ImageSectionOffset + stride.CleanToOffset(item.Offset, false), md.FsOffset, (int)md.FsSize, DataType.Other, md.FillByte));
            }

            if (ranges.Count == 0) // Sort by FsOffset and merge overlapping/adjacent ranges
                return ranges;

            // Deduplicate exact duplicates (same ImageOffset, FsOffset, and Size) before merging.
            // This eliminates redundant entries from multiple code paths producing the same range,
            // while preserving legitimately different ranges that share an offset but differ in size.
            HashSet<(long ImageOffset, long FsOffset, int Size)> seen = new HashSet<(long, long, int)>();
            List<GapRange> deduped = new List<GapRange>(ranges.Count);
            foreach (GapRange r in ranges.OrderBy(r => r.FsOffset))
            {
                if (seen.Add((r.ImageOffset, r.FsOffset, r.Size)))
                    deduped.Add(r);
            }

            List<GapRange> merged = new List<GapRange>();
            foreach (GapRange next in deduped)
            {
                if (merged.Count == 0) // first
                    merged.Add(next);
                else
                {
                    GapRange last = merged[merged.Count - 1];
                    if (next.FsOffset <= last.FsOffset + last.Size && next.DataType == last.DataType && next.FillByte == last.FillByte) // Merge: extend current to cover both (only if same DataType)
                        merged[merged.Count - 1] = new GapRange(last.ImageOffset, last.FsOffset, (int)(Math.Max(last.FsOffset + last.Size, next.FsOffset + next.Size) - last.FsOffset), last.DataType, last.FillByte);
                    else // No overlap or different DataType: save current and start new
                        merged.Add(next);
                }
            }
            return merged;
        }

        /// <summary>
        /// Creates area records in both primary and aux stores from the scan's area list.
        /// The CRC values (sra.Crc) are computed upstream by Scan.SectionProcessed() and are
        /// specific to the current image — the formatter is instantiated fresh per image by
        /// DedupeStep.Initialise(), so no stale state can carry over between images.
        /// </summary>
        public void CreateAreas(IEnumerable<ScanArea> areas)
        {
            if (_imageWriter == null || areas == null) return;
            foreach (ScanArea sra in areas)
            {
                try
                {
                    AreaMetadata meta = BuildAreaMetadata(sra);
                    uint crc = sra.Crc;
                    ulong xx = 0;
                    AreaInfo ai = sra.AreaInfo;
                    int sectionSize = ai?.SectionSize ?? 0x200000; // Default to 2MB if not specified

                    if (ai != null && ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, sectionSize, meta);
                    else
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);

                    // Write area to aux store so it has complete image records
                    if (_auxWriter != null)
                    {
                        try
                        {
                            if (ai != null && ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                                _auxWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, sectionSize, meta);
                            else
                                _auxWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                        }
                        catch (Exception auxEx)
                        {
                            _context.Log?.Info(() => $"CreateArea (aux) failed for offset {sra.ImageOffset:X}: {auxEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _context.Log?.Info(() => $"CreateArea failed for offset {sra.ImageOffset:X}: {ex.Message}");
                }
            }
        }

        public void FinalizeImage(long size, uint crc, ulong xxHash)
        {
            try
            {
                _imageWriter?.FinalizeImage(size, crc, xxHash);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"FinalizeImage failed: {ex.Message}");
                throw;
            }

            // Finalize aux writer so the aux store has matching image metadata
            try
            {
                _auxWriter?.FinalizeImage(size, crc, xxHash);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"FinalizeImage (aux) failed: {ex.Message}");
            }
        }

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        public void BuildFileSystemYaml(Scan scan)
        {
            if (_imageWriter == null || scan == null)
                return;

            FsYaml yaml = new FsYaml();
            FsYamlNode inlineRoot = null;
            HashSet<string> systemDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sys" };

            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                bool isGamePartition = area.FsInfo.Type == PartitionType.Game;
                string ptnName = isGamePartition
                    ? null
                    : area.FsInfo.Type.ToString().ToUpper();

                if (inlineRoot == null)
                    inlineRoot = yaml.AddFileSystem(".", area.ImageOffset);

                // Non-game partitions (UPDATE etc.) are system directories
                if (!isGamePartition && ptnName != null)
                    systemDirNames.Add(ptnName);

                AreaInfo ai = area.AreaInfo;
                DataStride stride = null;
                if (ai.BlockSize > 0 && ai.BlockFsSize > 0 && ai.BlockSize != ai.BlockFsSize)
                    stride = new DataStride { SourceBlockSize = ai.BlockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize };

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    long imageOffset = stride != null
                        ? area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
                        : area.ImageOffset + file.FsOffset;

                    // System files (!!name) go under sys/, regular files under files/
                    string subDir = file.IsSystemFile ? "sys" : "files";
                    string fileName = file.IsSystemFile ? file.Name.Substring(2) : file.Name;
                    string dirPath = file.Path?.Trim('/') ?? "";
                    string fullPath;

                    if (isGamePartition)
                    {
                        // Game/DATA partition files at root level under sys/ or files/
                        fullPath = string.IsNullOrEmpty(dirPath)
                            ? $"/{subDir}/{fileName}"
                            : $"/{subDir}/{dirPath}/{fileName}";
                    }
                    else
                    {
                        // Non-game partitions: under partition directory (e.g., UPDATE/sys/...)
                        fullPath = string.IsNullOrEmpty(dirPath)
                            ? $"/{ptnName}/{subDir}/{fileName}"
                            : $"/{ptnName}/{subDir}/{dirPath}/{fileName}";
                    }

                    inlineRoot.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc);
                }
            }

            // Mark system directories (sys at root level, and non-game partition directories like UPDATE)
            if (inlineRoot?.Children != null)
            {
                foreach (FsYamlNode child in inlineRoot.Children)
                {
                    if (child.IsDirectory && systemDirNames.Contains(child.Name))
                        child.IsSystem = true;
                }
            }

            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(yaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);

            // Write filesystem data to aux store so it has complete image records
            try
            {
                _auxWriter?.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"BuildFileSystemYaml (aux) failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _auxWriter?.Dispose(); } catch { }
            try { _auxDataStore?.Dispose(); } catch { }
            try { _imageWriter?.Dispose(); } catch { }
            try { _dataStore?.Dispose(); } catch { }
        }
    }
}