using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// DataStore formatter for Xbox and Xbox360 system types.
    /// Xbox sectors are cooked (0x800 block size) with no sector headers, so no striding is needed.
    /// Handles area metadata creation, gap persistence, filesystem YAML generation from XDvdFs,
    /// encryption key metadata storage, and optional aux store routing for non-recreatable filler/junk data.
    /// </summary>
    internal class DataStoreXboxFormatter : IDataStoreSystemFormatter
    {
        private readonly IImageWriter _imageWriter;
        private readonly IStepContext _context;
        private readonly IDataStore _dataStore;
        private List<GapRange> _storedRanges;

        // Cached full-image detection (computed once from image size)
        private readonly bool _isFullImage;

        // Aux DataStore and writer for routing filler/junk blocks to a sidecar store.
        // Opened when auxSetName is provided (convention-discovered by DedupeStep).
        private readonly IDataStore _auxDataStore;
        private readonly IImageWriter _auxWriter;

        // Per-game split DataStore and writer for game partition filler (AreaType.FileSystem gaps).
        // Opened when splitSetName is provided (Xbox/Xbox360 dual mode).
        private readonly IDataStore _splitDataStore;
        private readonly IImageWriter _splitWriter;

        public string ImageFileName { get; }

        public bool HasAux => _auxWriter != null;
        public bool HasSplit => _splitWriter != null;

        public DataStoreXboxFormatter(string dedupePath, string imageName, long shardSize, IStepContext context, int blockSize, string setName, string auxSetName, string splitSetName = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(dedupePath)) throw new ArgumentNullException(nameof(dedupePath));

            // Cache full-image detection once (used for video partition routing)
            long imageSize = context.ImageInfo?.ImageSize ?? 0;
            _isFullImage = Array.IndexOf(Microsoft.XBox.Consts.REDUMP_ISO_LENGTH, imageSize) >= 0;

            // Ensure directory exists
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

            // Aux writer setup — aux set discovered by convention in DedupeStep.
            // By the time the formatter is constructed, DedupeStep has already resolved
            // and optionally auto-created the aux set. If auxSetName is non-null, the set exists.
            // Standard aux stores (.aux suffix) use standard sharding (50GB).
            if (!string.IsNullOrEmpty(auxSetName))
            {
                try
                {
                    long auxShardSize = 50L * 1024 * 1024 * 1024; // standard sharding for shared aux

                    _auxDataStore = new NKitDataStore.DataStore(dedupePath);
                    if (_auxDataStore.GetSetInfo(auxSetName) == null)
                        _auxDataStore.CreateSet(auxSetName, auxShardSize, blockSize);
                    _auxWriter = _auxDataStore.AddImage(auxSetName, imageName, context.SystemType.ToString(), ImageFormat.Iso);
                    _auxWriter.CompressionParallelism = 16;
                }
                catch (Exception ex)
                {
                    // If aux setup fails, operate without shared aux writer
                    _context.Log?.Info(() => $"Shared aux store setup failed, operating without shared aux: {ex.Message}");
                    try { _auxWriter?.Dispose(); } catch { }
                    try { _auxDataStore?.Dispose(); } catch { }
                    _auxWriter = null;
                    _auxDataStore = null;
                }
            }

            // Split writer setup — per-game split store for game partition filler (Xbox/Xbox360 dual mode).
            // Split stores use embedded mode (shardSize=0) — a single file per game.
            if (!string.IsNullOrEmpty(splitSetName))
            {
                try
                {
                    _splitDataStore = new NKitDataStore.DataStore(dedupePath);
                    if (_splitDataStore.GetSetInfo(splitSetName) == null)
                        _splitDataStore.CreateSet(splitSetName, 0, blockSize); // shardSize=0 → embedded mode
                    _splitWriter = _splitDataStore.AddImage(splitSetName, imageName, context.SystemType.ToString(), ImageFormat.Iso);
                    _splitWriter.CompressionParallelism = 16;
                }
                catch (Exception ex)
                {
                    // If split setup fails, operate without split writer
                    _context.Log?.Info(() => $"Split store setup failed, operating without split writer: {ex.Message}");
                    try { _splitWriter?.Dispose(); } catch { }
                    try { _splitDataStore?.Dispose(); } catch { }
                    _splitWriter = null;
                    _splitDataStore = null;
                }
            }
        }

        /// <summary>
        /// Xbox uses cooked 0x800 sectors with no striding — always returns null.
        /// </summary>
        public DataStride GetStrideForPartition(int partitionId) => null;

        /// <summary>
        /// Returns the absolute image offset for the given partition area.
        /// </summary>
        public long GetPartitionImageOffset(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.Type == AreaType.FileSystem);
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
                    case AreaType.FileSystem:
                        metadata.Set(AreaValueType.BlockSize, 0x800L);
                        metadata.Set(AreaValueType.AreaOffsetBase, (long)scanArea.ImageOffset);

                        if (props != null)
                        {
                            // FsType from scan properties (XDvdFs or Iso9660 for video partitions)
                            string fsType = props["FsType"] as string;
                            if (!string.IsNullOrEmpty(fsType))
                                metadata.Set(AreaValueType.FsType, fsType);

                            // Version (XDvdFs version)
                            if (props["Version"] != null)
                                metadata.Set(AreaValueType.Version, (int)props["Version"]);

                            // AreaOffsetBase from scan properties
                            if (props["AreaOffsetBase"] != null)
                                metadata.Set(AreaValueType.AreaOffsetBase, (long)(ulong)props["AreaOffsetBase"]);

                            // Header CRC and size for video partitions (ISO9660 PVD)
                            if (props["HeaderCrc"] != null)
                                metadata.Set(AreaValueType.HeaderCrc, (long)(uint)props["HeaderCrc"]);
                            if (props["HeaderSize"] != null)
                                metadata.Set(AreaValueType.HeaderSize, (long)(ulong)props["HeaderSize"]);
                            if (props["HeaderXxHash"] != null)
                                metadata.Set(AreaValueType.HeaderXxHash, (long)(ulong)props["HeaderXxHash"]);

                            // HeaderDate
                            if (props["HeaderDate"] != null)
                                metadata.Set(AreaValueType.HeaderDate, (props["HeaderDate"] as string) ?? "");

                            // PhysicalOffset
                            if (props["PhysicalOffset"] != null)
                                metadata.Set(AreaValueType.PhysicalOffset, (long)(ulong)props["PhysicalOffset"]);

                            // PvdSectorCount for video partitions
                            if (props["PvdSectorCount"] != null)
                                metadata.Set(AreaValueType.PvdSectorCount, (long)(ulong)props["PvdSectorCount"]);

                            // SessionOffsetBase
                            if (props["SessionOffsetBase"] != null)
                                metadata.Set(AreaValueType.SessionOffsetBase, (long)(ulong)props["SessionOffsetBase"]);
                        }

                        // Encryption key metadata
                        byte[] key = _context.SourceFile?.Key;
                        if (key != null && key.Length > 0)
                            metadata.Set(AreaValueType.KeyCrc, (long)Crc.Compute(key));
                        else
                            metadata.Set(AreaValueType.TitleKeyMissing, true);

                        break;

                    case AreaType.Other:
                        metadata.Set(AreaValueType.BlockSize, 0x800L);
                        metadata.Set(AreaValueType.AreaOffsetBase, (long)scanArea.ImageOffset);
                        break;

                    default:
                        break;
                }
            }
            catch
            {
                // Ignore metadata failures
            }

            return metadata;
        }

        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null) => _imageWriter.BeginWriteStream(imageOffset, type, imageOffset, stride, strideOriginOffset);

        public void FinalizeFileWrite(long imageOffset, Stream stream) => stream?.Dispose();

        public long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset)
        {
            // Xbox has no striding. The filesystem offsets (f.FsOffset) are relative to the
            // area's AreaOffsetBase (stored in AreaInfo.BaseOffset during scan). To get the
            // absolute image offset: areaImageOffset + (fsOffset - areaOffsetBase).
            // For the main game partition (area at offset 0), BaseOffset is 0 so this
            // simplifies to just fsOffset. For video partitions at other offsets,
            // BaseOffset equals the area's AreaOffsetBase.
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.ImageOffset == partitionImageOffset);
            long baseOffset = area?.AreaInfo?.BaseOffset ?? 0;
            return partitionImageOffset + (fsOffset - baseOffset);
        }

        /// <summary>
        /// Xbox has no hash blocks, so this is minimal. Handle BlockPadding if video partition
        /// has non-standard data (unlikely for Xbox but included for completeness).
        /// </summary>
        public void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride)
        {
            // Xbox cooked sectors have no hash blocks or recreatable headers.
            // No block padding to persist.
        }

        public void ProcessSection(ISection section)
        {
            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            _storedRanges = null;

            if (section.Type == AreaType.ImageHeader)
            {
                // Xbox doesn't have a separate ImageHeader area in the same way as Wii,
                // but if one is present, write it to the primary store.
                _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
            }
            else if (section.Type == AreaType.Other)
            {
                // Video partition areas — write the ENTIRE section content to the shared aux store.
                // Xbox/Xbox360 video partitions (AreaType.Other) contain ISO9660 video/demo content
                // that is identical across games and therefore deduplicatable in the shared aux.
                // Unlike FileSystem gaps which are selectively persisted, video partition data is
                // written in its entirety because the whole section is auxiliary content.
                IImageWriter targetWriter = _auxWriter ?? _imageWriter;
                try
                {
                    targetWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                }
                catch (Exception ex) when (_auxWriter != null && targetWriter == _auxWriter)
                {
                    _context.Log?.Warn(() => $"Aux write failed for video partition at {section.ImageOffset:X} (size {section.Size:X}). " +
                        $"Falling back to primary: {ex.Message}");

                    // Fall back to primary for the entire section
                    _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                }
            }
            else if (section.Type == AreaType.FileSystem)
            {
                // Determine if this is an ISO9660 video partition or an XDvdFs game partition.
                // Xbox full images have both: the video/demo partition (ISO9660) and
                // the game partition (XDvdFs). Video partition content → shared aux (entire section).
                // Game partition filler → per-game split (non-zero gaps only).
                //
                // Detection: the XDvdFs game partition always starts at one of the known XISO_OFFSET
                // values. Any FileSystem area NOT at a known XDvdFs offset is a video partition.
                // For trimmed/XISO images (only one FileSystem area at offset 0), there's no video
                // partition — all data is game data and goes through the normal game partition path.
                // Use section.AreaInfo.ImageOffset for area lookup — section.ImageOffset is the chunk
                // position which may differ from the area start for chunked sections.
                long areaImageOffset = section.AreaInfo?.ImageOffset ?? section.ImageOffset;
                bool isXDvdFsGamePartition = Array.IndexOf(Microsoft.XBox.Consts.XISO_OFFSET, areaImageOffset) >= 0;

                // For full images, any FileSystem area NOT at a known XDvdFs offset is a video partition.
                // _isFullImage is cached from constructor (image size check against REDUMP_ISO_LENGTH).
                bool isVideoPartition = !isXDvdFsGamePartition && _isFullImage;

                if (isVideoPartition && _auxWriter != null)
                {
                    // ISO9660 video partition — write the ENTIRE section content to shared aux.
                    IImageWriter targetWriter = _auxWriter;
                    try
                    {
                        targetWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                    }
                    catch (Exception ex) when (_auxWriter != null && targetWriter == _auxWriter)
                    {
                        _context.Log?.Warn(() => $"Aux write failed for ISO9660 video partition at {section.ImageOffset:X} (size {section.Size:X}). " +
                            $"Falling back to primary: {ex.Message}");

                        // Fall back to primary for the entire section
                        _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
                    }
                }
                else
                {
                    // XDvdFs game partition — persist gaps where fill byte is non-zero or data type is not expected default
                    // Route gap/filler data to _splitWriter (per-game split store) when active (Requirement 4.2)
                    _storedRanges = GetGapsForXbox(section, stride).ToList();

                    if (_storedRanges.Any(a => a.ImageOffset < section.ImageOffset || a.ImageOffset + a.Size > section.ImageOffset + section.Size))
                    {
                        _context.Log?.Error(() => $"ProcessSection: Calculated gap ranges exceed section bounds at {section.ImageOffset:X}");
                        return;
                    }

                    // Track blocks written to split store for orphan logging on failure
                    int blocksWrittenToSplit = 0;
                    long firstOrphanOffset = 0;
                    long lastOrphanOffset = 0;
                    bool splitFailed = false; // Once set, all subsequent blocks route to primary

                    foreach (GapRange persist in _storedRanges)
                    {
                        // Route filler/junk (BlockType.Other gap records) to split writer when split is active and not failed
                        IImageWriter targetWriter = (_splitWriter != null && !splitFailed) ? _splitWriter : _imageWriter;
                        try
                        {
                            // Use section.Encrypted for gap data — it always contains the final/original data
                            // (section.Decrypted may have been modified by the pre-processor for gap regions)
                            byte[] src = section.Encrypted ?? section.Decrypted;
                            Stream writeStream = targetWriter.BeginWriteStream(persist.ImageOffset, BlockType.Other, persist.ImageOffset, stride, null);
                            writeStream.Write(src, (int)persist.FsOffset, persist.Size);
                            writeStream?.Dispose();

                            // Track successful split writes for orphan range reporting
                            if (targetWriter == _splitWriter)
                            {
                                if (blocksWrittenToSplit == 0)
                                    firstOrphanOffset = persist.ImageOffset;
                                lastOrphanOffset = persist.ImageOffset;
                                blocksWrittenToSplit++;
                            }
                        }
                        catch (Exception ex) when (_splitWriter != null && targetWriter == _splitWriter)
                        {
                            // Log orphaned blocks: blocks already written to split cannot be rolled back
                            if (blocksWrittenToSplit > 0)
                            {
                                _context.Log?.Warn(() => $"Split write failed at {persist.ImageOffset:X}. " +
                                    $"Orphaned {blocksWrittenToSplit} block(s) in range [{firstOrphanOffset:X}..{lastOrphanOffset:X}]. " +
                                    $"Falling back to primary: {ex.Message}");
                            }
                            else
                            {
                                _context.Log?.Warn(() => $"Split write failed at {persist.ImageOffset:X}. " +
                                    $"No orphaned blocks. Falling back to primary: {ex.Message}");
                            }

                            // Mark split as failed — all subsequent blocks in this section go to primary
                            splitFailed = true;

                            // Write the failed block to primary
                            byte[] src = section.Encrypted ?? section.Decrypted;
                            Stream writeStream = _imageWriter.BeginWriteStream(persist.ImageOffset, BlockType.Other, persist.ImageOffset, stride, null);
                            writeStream.Write(src, (int)persist.FsOffset, persist.Size);
                            writeStream?.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a file should be preserved in the datastore.
        /// Skip zero-length and missing files.
        /// Also skip files in ISO9660 video partitions — their entire section data
        /// is written to the shared aux store, so individual files should not be
        /// duplicated into the primary store.
        /// </summary>
        public bool ShouldPreserveFile(ISection section, IFsFile file)
        {
            if (file == null)
                return false;

            // ISO9660 video partition files are stored as whole-section data in aux — skip individual preservation
            if (section.Type == AreaType.FileSystem && _auxWriter != null)
            {
                long areaImageOffset = section.AreaInfo?.ImageOffset ?? section.ImageOffset;
                bool isXDvdFsGamePartition = Array.IndexOf(Microsoft.XBox.Consts.XISO_OFFSET, areaImageOffset) >= 0;
                if (!isXDvdFsGamePartition && _isFullImage)
                    return false;
            }

            if (file.FsSize == 0 && (file.PostGapSize == 0 || (_storedRanges != null && _storedRanges.Any(a => section.FsOffset + a.FsOffset <= file.FsOffset && section.FsOffset + a.FsOffset + a.Size >= file.FsOffset))))
                return false;

            return !file.IsMissing;
        }

        /// <summary>
        /// Gets gap ranges for Xbox FileSystem areas.
        /// Only gaps where the fill byte is non-zero OR the data type is not the expected default (XFiller pattern)
        /// are persisted. Gaps with fill byte 0x00 and standard filler data type are skipped.
        /// </summary>
        internal static List<GapRange> GetGapsForXbox(ISection section, DataStride stride)
        {
            List<GapRange> ranges = new List<GapRange>();

            if (section.Type != AreaType.FileSystem)
                return ranges;

            foreach (ISectionItem item in section.Items)
            {
                // Handle pre-file gaps (gap-only items with no file)
                // These always contain non-regeneratable data (disc headers, volume descriptors, etc.)
                // Persist unconditionally — the ImageBuilder fills gaps with zeros, so any
                // gap-only item that exists must have meaningful content.
                if (item.File == null && item.Gap != null && item.Gap.FsSize > 0)
                {
                    if ((item.GapInfo?.Count ?? 0) == 0)
                    {
                        // Single gap with no sub-parts — only persist if not Fill 0x00
                        if (item.Gap.FillByte != 0 || item.Gap.DataType != DataType.Fill)
                        {
                            long imageOffset = section.ImageOffset + item.Gap.Offset;
                            ranges.Add(new GapRange(imageOffset, item.Gap.FsOffset, (int)item.Gap.FsSize, DataType.Other, item.Gap.FillByte));
                        }
                    }
                    else
                    {
                        // Has GapInfo sub-parts — only persist non-Fill-0x00 sub-parts
                        foreach (ISectionData gap in item.GapInfo)
                        {
                            if (gap.FillByte != 0 || gap.DataType != DataType.Fill)
                            {
                                long imageOffset = section.ImageOffset + gap.Offset;
                                ranges.Add(new GapRange(imageOffset, gap.FsOffset, (int)gap.FsSize, DataType.Other, gap.FillByte));
                            }
                        }
                    }
                }

                // Handle post-file gaps
                if (item.File != null && item.Gap != null && item.Gap.FsSize > 0)
                {
                    IEnumerable<ISectionData> gaps = (item.GapInfo?.Count ?? 0) == 0
                        ? new[] { item.Gap }
                        : item.GapInfo.AsEnumerable();

                    foreach (ISectionData gap in gaps)
                    {
                        // Xbox: persist any gap that is NOT Fill with FillByte 0x00
                        if (gap.FillByte != 0 || gap.DataType != DataType.Fill)
                        {
                            long imageOffset = section.ImageOffset + gap.Offset;
                            ranges.Add(new GapRange(imageOffset, gap.FsOffset, (int)gap.FsSize, DataType.Other, gap.FillByte));
                        }
                    }
                }
            }

            // Handle non-creatable items (filler data that can't be regenerated)
            foreach (NonCreatableData item in (section.NonCreatableItems ?? Enumerable.Empty<NonCreatableData>()).Where(i => i.IsFs && i.Type == NonCreatableDataType.Filler))
            {
                ISectionData md = section.Items.EnumSectionData().FirstOrDefault(a => item.Offset == a.FsOffset && item.Size == a.FsSize);
                if (md != null)
                    ranges.Add(new GapRange(item.ImageSectionOffset + stride.CleanToOffset(item.Offset, false), md.FsOffset, (int)md.FsSize, DataType.Other, md.FillByte));
            }

            if (ranges.Count == 0)
                return ranges;

            // Deduplicate and merge overlapping/adjacent ranges
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
                if (merged.Count == 0)
                    merged.Add(next);
                else
                {
                    GapRange last = merged[merged.Count - 1];
                    if (next.FsOffset <= last.FsOffset + last.Size && next.DataType == last.DataType && next.FillByte == last.FillByte)
                        merged[merged.Count - 1] = new GapRange(last.ImageOffset, last.FsOffset, (int)(Math.Max(last.FsOffset + last.Size, next.FsOffset + next.Size) - last.FsOffset), last.DataType, last.FillByte);
                    else
                        merged.Add(next);
                }
            }
            return merged;
        }

        /// <summary>
        /// Creates area records in both primary and aux stores from the scan's area list.
        /// Mirrors area records to aux writer when aux is active.
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
                    int sectionSize = ai?.SectionSize ?? 0x200000;

                    _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);

                    // Mirror area to aux store so it has complete image records
                    if (_auxWriter != null)
                    {
                        try
                        {
                            _auxWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                        }
                        catch (Exception auxEx)
                        {
                            _context.Log?.Info(() => $"CreateArea (aux) failed for offset {sra.ImageOffset:X}: {auxEx.Message}");
                        }
                    }

                    // Mirror area to split store so it has complete image records
                    if (_splitWriter != null)
                    {
                        try
                        {
                            _splitWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                        }
                        catch (Exception splitEx)
                        {
                            _context.Log?.Info(() => $"CreateArea (split) failed for offset {sra.ImageOffset:X}: {splitEx.Message}");
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

            // Finalize split writer so the split store has matching image metadata
            try
            {
                _splitWriter?.FinalizeImage(size, crc, xxHash);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"FinalizeImage (split) failed: {ex.Message}");
            }
        }

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        /// <summary>
        /// Builds filesystem YAML from the XDvdFs file system tree and persists it to the data store.
        /// Mirrors YAML to aux writer when aux is active.
        /// </summary>
        public void BuildFileSystemYaml(Scan scan)
        {
            if (_imageWriter == null || scan == null)
                return;

            FsYaml yaml = new FsYaml();
            FsYamlNode inlineRoot = null;

            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                if (inlineRoot == null)
                    inlineRoot = yaml.AddFileSystem(".", area.ImageOffset);

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    long imageOffset = ToImageOffsetFromFsOffsets(area.ImageOffset, file.FsOffset);

                    // System files (!!name) go under sys/, regular files under files/
                    string subDir = file.IsSystemFile ? "sys" : "files";
                    string fileName = file.IsSystemFile ? file.Name.Substring(2) : file.Name;
                    string dirPath = file.Path?.Trim('/') ?? "";
                    string fullPath = string.IsNullOrEmpty(dirPath)
                        ? $"/{subDir}/{fileName}"
                        : $"/{subDir}/{dirPath}/{fileName}";

                    inlineRoot.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc);
                }
            }

            // Mark the sys directory as system
            if (inlineRoot?.Children != null)
            {
                foreach (FsYamlNode child in inlineRoot.Children)
                {
                    if (child.IsDirectory && child.Name.Equals("sys", StringComparison.OrdinalIgnoreCase))
                        child.IsSystem = true;
                }
            }

            byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(yaml).ToBytes();
            _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);

            // Mirror filesystem data to aux store so it has complete image records
            try
            {
                _auxWriter?.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"BuildFileSystemYaml (aux) failed: {ex.Message}");
            }

            // Mirror filesystem data to split store so it has complete image records
            try
            {
                _splitWriter?.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, nkfsBytes, isSystem: true);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"BuildFileSystemYaml (split) failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _splitWriter?.Dispose(); } catch { }
            try { _splitDataStore?.Dispose(); } catch { }
            try { _auxWriter?.Dispose(); } catch { }
            try { _auxDataStore?.Dispose(); } catch { }
            try { _imageWriter?.Dispose(); } catch { }
            try { _dataStore?.Dispose(); } catch { }
        }
    }
}