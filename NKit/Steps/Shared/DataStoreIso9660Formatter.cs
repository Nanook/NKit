using Nanook.NKit.Iso.Iso9660;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// Implements IDataStoreSystemFormatter for all ISO9660-based system types
    /// (PS1, PS2, PS3, PSP, SegaCD, Saturn, Dreamcast, CDi, Default, PcEngine).
    /// Handles stride computation based on track sector mode, area metadata creation
    /// with physical offset and block size information, multi-track area creation,
    /// filesystem YAML generation, PS3 encryption metadata, and non-conforming
    /// sector header detection (BlockPadding).
    /// </summary>
    internal class DataStoreIso9660Formatter : IDataStoreSystemFormatter
    {
        private readonly IImageWriter _imageWriter;
        private readonly IStepContext _context;
        private readonly IDataStore _dataStore;

        /// <summary>Standard CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00</summary>

        public string ImageFileName { get; }

        public DataStoreIso9660Formatter(string dedupePath, string imageName, long shardSize, IStepContext context, int blockSize, string setName)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(dedupePath)) throw new ArgumentNullException(nameof(dedupePath));

            if (!Directory.Exists(dedupePath))
                Directory.CreateDirectory(dedupePath);

            // Validate track files before opening the DataStore — abort early if any are missing
            ValidateTrackFiles();

            string resolvedSetName = setName ?? context.SystemType.ToString();
            _dataStore = new DataStore(dedupePath);
            if (_dataStore.GetSetInfo(resolvedSetName) == null)
                _dataStore.CreateSet(resolvedSetName, shardSize, blockSize);

            // Determine the correct ImageFormat based on the source index file type.
            // CUE/BIN images use ImageFormat.Cue so reconstruction knows to expect
            // strided sectors and a stored CUE index file.
            ImageFormat imageFormat = ImageFormat.Iso;
            IndexFileType indexType = context.SourceFile?.IndexFile?.FileType ?? IndexFileType.None;
            if (indexType == IndexFileType.Cue)
                imageFormat = ImageFormat.Cue;
            else if (indexType == IndexFileType.Gdi)
                imageFormat = ImageFormat.Gdi;
            else if (indexType == IndexFileType.None && IsChdFolderIndex(context))
            {
                // CHD sources carry their track layout INSIDE the container (ChdMetaData), so
                // SourceFile.IndexFile is null. Without this the image would be stored as a
                // single-file ImageFormat.Iso even though it is logically an indexed image.
                if (context.ImageInfo?.MediaType == MediaType.GD)
                {
                    // Dreamcast GD-ROM. The raw CHD track bytes are stored VERBATIM (no pad/pregap
                    // mutation — preservation-safe), and the CHD's own track metadata is persisted as
                    // a loose chd.meta.txt file (see WriteChdMetaFile). Storing ImageFormat.Chd tells
                    // read-back (DataStoreAsIso) to rehydrate the CHD tracks via ChdMetaData.Parse so
                    // the source presents identically to a live CHD file — export can then run the
                    // existing GdRomWriter path to produce a correct CUE or GDI (the chdman PAD frames
                    // and pregap geometry are reconstructed from the stored metadata, since Pad != 0
                    // is what gates GdRomWriter.adjustChdTracks). We deliberately do NOT convert at
                    // store time because there is no single canonical layout (redump CUE vs tosec GDI
                    // differ) and editing the bytes risks corrupting a preserved image.
                    imageFormat = ImageFormat.Chd;
                }
                else
                {
                    // Multi-track CD CHD (PS1/Saturn/SegaCD etc.). These came from a standard redump
                    // CUE and need no pad/pregap synthesis, so the existing CUE storage is correct.
                    // Store as CUE so reconstruction expects strided sectors and a stored CUE index.
                    imageFormat = ImageFormat.Cue;
                }
            }

            // ImageFileName uses .bin for CUE and Chd (the data file) — the .cue index / chd.meta.txt
            // are stored separately as loose files. Using .cue/.chd here would trigger index file
            // resolution in the verify source file scanner.
            string displayExtension = (imageFormat == ImageFormat.Cue || imageFormat == ImageFormat.Chd) ? ".bin" : Container.DataStoreAsIso.GetImageExtension(imageFormat);
            ImageFileName = imageName + displayExtension;
            _imageWriter = _dataStore.AddImage(resolvedSetName, imageName, context.SystemType.ToString(), imageFormat);
            _imageWriter.CompressionParallelism = 16;
        }

        /// <summary>
        /// Computes the DataStride for a given partition based on the track's block size and mode.
        /// Returns null for cooked (0x800) sectors indicating no striding is required.
        /// </summary>
        public DataStride GetStrideForPartition(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.ElementAtOrDefault(partitionId);
            if (area == null)
                return null;

            AreaInfo ai = area.AreaInfo;
            int blockSize = ai.BlockSize;
            int blockFsSize = ai.BlockFsSize;
            int blockFsOffset = ai.BlockFsOffset;

            if (blockSize == 0x800)
            {
                // Cooked sectors — no striding needed
                return null;
            }

            if (blockSize == 0x930)
            {
                if (area.Type == AreaType.Audio)
                {
                    // Audio: full 2352 bytes are non-recreatable content
                    return new DataStride
                    {
                        SourceBlockSize = 0x930,
                        DataOffset = 0,
                        DataLength = 0x930
                    };
                }

                // Use the AreaInfo's BlockFsOffset and BlockFsSize which are set by the Image scanner
                if (blockFsSize > 0 && blockFsOffset >= 0 && blockFsSize != blockSize)
                {
                    return new DataStride
                    {
                        SourceBlockSize = blockSize,
                        DataOffset = blockFsOffset,
                        DataLength = blockFsSize
                    };
                }

                // Fallback: determine from track mode properties
                Properties props = ai.Properties;
                if (props != null)
                {
                    long mode1Count = props.Get<long>("Mode1", 0);
                    long mode2Form2Count = props.Get<long>("Mode2Form2", 0);
                    long mode2Form1Count = props.Get<long>("Mode2Form1", 0);

                    if (mode2Form2Count > 0 && mode2Form2Count >= mode1Count && mode2Form2Count >= mode2Form1Count)
                    {
                        // Mode2Form2: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x914
                        return new DataStride
                        {
                            SourceBlockSize = 0x930,
                            DataOffset = 0x18,
                            DataLength = 0x914
                        };
                    }
                    else if (mode2Form1Count > 0 || (mode1Count == 0 && mode2Form1Count == 0 && mode2Form2Count == 0 && blockFsOffset == 0x18))
                    {
                        // Mode2/Mode2Form1: SourceBlockSize=0x930, DataOffset=0x18, DataLength=0x800
                        return new DataStride
                        {
                            SourceBlockSize = 0x930,
                            DataOffset = 0x18,
                            DataLength = 0x800
                        };
                    }
                    else
                    {
                        // Mode1Raw: SourceBlockSize=0x930, DataOffset=0x10, DataLength=0x800
                        return new DataStride
                        {
                            SourceBlockSize = 0x930,
                            DataOffset = 0x10,
                            DataLength = 0x800
                        };
                    }
                }

                // Default for 0x930 without mode info: Mode1Raw
                return new DataStride
                {
                    SourceBlockSize = 0x930,
                    DataOffset = 0x10,
                    DataLength = 0x800
                };
            }

            // Unrecognized block size — skip stride computation and log warning
            _context.Log?.Info(() => $"DataStoreIso9660Formatter: Unrecognized block size 0x{blockSize:X} for area {partitionId}, skipping stride computation.");
            return null;
        }

        /// <summary>
        /// Returns the absolute image offset for the start of a given partition (area index).
        /// </summary>
        public long GetPartitionImageOffset(int partitionId)
        {
            ScanArea area = _context.Scan?.Areas?.ElementAtOrDefault(partitionId);
            return area?.ImageOffset ?? 0;
        }

        /// <summary>
        /// Builds area metadata for an ISO9660 scan area, storing BlockSize, PhysicalOffset,
        /// Track, Session, Type, stride metadata, and PS3 encryption fields.
        /// </summary>
        public AreaMetadata BuildAreaMetadata(ScanArea scanArea)
        {
            if (scanArea == null)
                return null;

            Properties props = scanArea.AreaInfo?.Properties;
            if (props == null || props.Keys.Length == 0)
                return null;

            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, scanArea.Type.ToString());

            AreaInfo ai = scanArea.AreaInfo;

            // BlockSize
            metadata.Set(AreaValueType.BlockSize, ai.BlockSize);

            // Track and Session
            if (props["Track"] != null)
                metadata.Set(AreaValueType.Track, (int)props["Track"]);
            if (props["Session"] != null)
                metadata.Set(AreaValueType.Session, (int)props["Session"]);

            switch (scanArea.Type)
            {
                case AreaType.FileSystem:
                    // PhysicalOffset — starting LBA for sector address computation.
                    // track.PhysicalOffset is stored in bytes (LBA * blockSize), so divide
                    // by blockSize to store the actual sector number (LBA).
                    if (props["PhysicalOffset"] != null)
                    {
                        long physOffBytes = (long)(ulong)props["PhysicalOffset"];
                        long physOffLba = ai.BlockSize > 0 ? physOffBytes / ai.BlockSize : physOffBytes;
                        metadata.Set(AreaValueType.PhysicalOffset, physOffLba);
                    }
                    else
                    {
                        // Fallback: derive PhysicalOffset from the SourceFileTrack when the scan
                        // property is not set (e.g. Dreamcast continuation partitions).
                        SourceFileTrack physTrack = _context.SourceFile?.IndexFile?.Items?.FirstOrDefault(
                            t => t.ImageOffset <= scanArea.ImageOffset && t.ImageOffset + t.Size > scanArea.ImageOffset);
                        if (physTrack != null && physTrack.PhysicalOffset != 0)
                        {
                            long physOffLba = ai.BlockSize > 0 ? physTrack.PhysicalOffset / ai.BlockSize : physTrack.PhysicalOffset;
                            metadata.Set(AreaValueType.PhysicalOffset, physOffLba);
                        }
                    }

                    // AreaOffsetBase
                    if (props["AreaOffsetBase"] != null)
                        metadata.Set(AreaValueType.AreaOffsetBase, (long)(ulong)props["AreaOffsetBase"]);

                    // SessionOffsetBase
                    if (props["SessionOffsetBase"] != null)
                        metadata.Set(AreaValueType.SessionOffsetBase, (long)(ulong)props["SessionOffsetBase"]);

                    // Header info (PVD)
                    if (props["HeaderSize"] != null)
                        metadata.Set(AreaValueType.HeaderSize, (long)(ulong)props["HeaderSize"]);
                    if (props["HeaderCrc"] != null)
                        metadata.Set(AreaValueType.HeaderCrc, (long)(uint)props["HeaderCrc"]);
                    if (props["HeaderXxHash"] != null)
                        metadata.Set(AreaValueType.HeaderXxHash, (long)(ulong)props["HeaderXxHash"]);

                    // PVD sector count
                    if (props["PvdSectorCount"] != null)
                        metadata.Set(AreaValueType.PvdSectorCount, (long)(ulong)props["PvdSectorCount"]);

                    // Track type info from mode counts
                    string trackType = determineTrackType(props);
                    if (!string.IsNullOrEmpty(trackType))
                        metadata.Set(AreaValueType.Type, trackType);

                    // PS3 encryption metadata — per-area based on region (odd regions are encrypted).
                    // PS3 discs have alternating plaintext/encrypted regions. The scan sets
                    // IsEncryptionSupported per-area from PlayStation3.IsEncryptionSupported(areaNo)
                    // which returns Regions[areaNo].Encrypted (true for odd indices).
                    // However, DecryptionValid/TitleKeyCrc are only stored on AreaNo==0 in SetScanProperties,
                    // so we also check the area index: odd-indexed areas are encrypted regions.
                    bool isPs3EncryptedRegion = ai.IsEncryptionSupported || (scanArea.AreaInfo.AreaNo % 2 != 0);
                    byte[] ps3Key = _context.SourceFile?.Key;
                    if (isPs3EncryptedRegion && ps3Key != null && ps3Key.Length > 0)
                    {
                        metadata.Set(AreaValueType.Encrypted, true);
                        metadata.Set(AreaValueType.TitleKey, ps3Key.ToHexString());
                    }
                    else if (isPs3EncryptedRegion)
                    {
                        metadata.Set(AreaValueType.Encrypted, true);
                        metadata.Set(AreaValueType.TitleKeyMissing, true);
                    }
                    if (props["TitleKeyCrc"] != null)
                        metadata.Set(AreaValueType.TitleKeyCrc, (long)(uint)props["TitleKeyCrc"]);
                    if (props["ThreeKey"] != null)
                        metadata.Set(AreaValueType.ThreeKey, (string)props["ThreeKey"]);
                    if (props["DecryptionValid"] != null)
                        metadata.Set(AreaValueType.DecryptionValid, (bool)props["DecryptionValid"]);

                    break;

                case AreaType.Audio:
                    metadata.Set(AreaValueType.Type, "Audio");
                    if (props["Duration"] != null)
                        metadata.Set(AreaValueType.Duration, ((TimeSpan)props["Duration"]).Ticks);
                    break;

                case AreaType.Other:
                    if (props["Partition"] != null)
                        metadata.Set(AreaValueType.Partition, (int)props["Partition"]);
                    break;

                default:
                    break;
            }

            // Set FileName metadata for CUE/GDI tracks (after all other metadata is set)
            string trackFileName = getTrackFileName(scanArea);
            if (!string.IsNullOrEmpty(trackFileName))
                metadata.Set(AreaValueType.FileName, trackFileName);

            return metadata;
        }

        /// <summary>
        /// True when the source is a CHD (or other container without an external index file)
        /// whose contents are logically an indexed/multi-track disc (GD-ROM or a multi-track CD).
        /// CHD carries its track layout inside the container, so SourceFile.IndexFile is null and
        /// the indexed nature must be read from the CHD-aware ImageInfo instead.
        /// </summary>
        private static bool IsChdFolderIndex(IStepContext context)
        {
            // Only applies when there is no external CUE/GDI index file to key off.
            if (context?.SourceFile?.IndexFile != null)
                return false;

            IImageInfo info = context?.ImageInfo;
            if (info == null)
                return false;

            // IsFolderIndex is set true by the Iso9660 reader for CHD GD-ROM and multi-track CD.
            // Require more than one track so a plain single-track ISO-in-CHD stays ImageFormat.Iso.
            return info.IsFolderIndex && (info.Tracks?.Length ?? 0) > 1;
        }

        /// <summary>
        /// Returns the effective track list to use for filename/index generation: the external
        /// CUE/GDI index tracks when present, otherwise the CHD-carried tracks from ImageInfo.
        /// Returns null when neither is available (plain ISO).
        /// </summary>
        private static SourceFileTrack[] GetEffectiveTracks(IStepContext context)
        {
            SourceFileTrack[] tracks = context?.SourceFile?.IndexFile?.Items;
            if (tracks != null && tracks.Length > 0)
                return tracks;
            return context?.ImageInfo?.Tracks;
        }

        /// <summary>
        /// Returns the track filename for the given scan area by looking up the corresponding
        /// SourceFileTrack in the IndexFile. Returns null when no IndexFile is present (plain ISO).
        /// When the track has no explicit filename, derives one from the image name and track number.
        /// </summary>
        private string getTrackFileName(ScanArea scanArea)
        {
            // Prefer the external CUE/GDI index tracks; fall back to the CHD-carried tracks
            // (ImageInfo.Tracks) when there is no external index file so CHD-sourced discs
            // still get per-track FileName metadata (and thus multi-track reconstruction).
            SourceFileTrack[] tracks = _context.SourceFile?.IndexFile?.Items;
            if (tracks == null || tracks.Length == 0)
                tracks = GetEffectiveTracks(_context);
            if (tracks == null || tracks.Length == 0)
                return null;

            // Track property is 0-based; fallback to AreaNo which is also 0-based
            int trackIndex = scanArea.AreaInfo?.Properties?.Get<int>("Track", -1) ?? -1;
            if (trackIndex < 0)
                trackIndex = scanArea.AreaInfo?.AreaNo ?? -1;

            if (trackIndex < 0)
                return null;

            // Find matching track: TrackIndex from CUE/GDI parser is 1-based,
            // but the "Track" property on scan areas is 0-based (TrackIndex - 1).
            // So we match t.TrackIndex == trackIndex + 1.
            SourceFileTrack track = tracks.FirstOrDefault(t => t.TrackIndex == trackIndex + 1);
            if (track == null && trackIndex >= 0 && trackIndex < tracks.Length)
                track = tracks[trackIndex];

            if (track != null && !string.IsNullOrEmpty(track.FileName))
                return track.FileName;

            // Derive filename when not explicitly set (use 1-based track number for display)
            string baseName = _context.SourceFile?.Name ?? "image";
            int displayTrackNo = trackIndex + 1;
            return $"{baseName} (Track {displayTrackNo:D2}).bin";
        }

        /// <summary>
        /// Determines the track type string from mode count properties.
        /// </summary>
        private static string determineTrackType(Properties props)
        {
            long mode1 = props.Get<long>("Mode1", 0);
            long mode2Form1 = props.Get<long>("Mode2Form1", 0);
            long mode2Form2 = props.Get<long>("Mode2Form2", 0);

            if (mode1 > 0 && mode1 >= mode2Form1 && mode1 >= mode2Form2)
                return "Mode1";
            if (mode2Form1 > 0 && mode2Form1 >= mode2Form2)
                return "Mode2Form1";
            if (mode2Form2 > 0)
                return "Mode2Form2";
            if (mode1 > 0)
                return "Mode1";
            // Default for data tracks with no mode info
            return null;
        }

        /// <summary>
        /// Creates one area record per track with block size, track type, and byte offset metadata.
        /// For raw-sector tracks, stride metadata is included in the area record.
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

                    if (ai != null && ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                    {
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, ai.BlockSize, ai.BlockFsOffset, ai.BlockFsSize, sectionSize, meta);
                    }
                    else
                    {
                        _imageWriter.CreateArea(sra.ImageOffset, sra.Size, crc, xx, sectionSize, meta);
                    }
                }
                catch (Exception ex)
                {
                    _context.Log?.Info(() => $"CreateArea failed for offset {sra.ImageOffset:X}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Processes a section for data tracks, detecting non-conforming sector headers
        /// (sync pattern, MSF, mode byte mismatches) and persisting gap data.
        /// </summary>
        public void ProcessSection(ISection section)
        {
            if (section == null) return;

            AreaInfo ai = section.AreaInfo;
            int blockSize = ai.BlockSize;

            DataStride stride = (blockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                ? new DataStride { SourceBlockSize = blockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize }
                : new DataStride { SourceBlockSize = blockSize, DataOffset = 0, DataLength = blockSize };

            if (section.Type == AreaType.FileSystem)
            {
                // Persist gap/non-creatable data ranges
                List<GapRange> storedRanges = DataStoreWiiFormatter.GetGapsAndNonCreatableDataRanges(section, stride).ToList();

                if (storedRanges.Any(a => a.ImageOffset < section.ImageOffset || a.ImageOffset + a.Size > section.ImageOffset + section.Size))
                {
                    _context.Log?.Error(() => $"ProcessSection: Calculated gap/non-creatable data ranges exceed section bounds at {section.ImageOffset:X}");
                    return;
                }

                foreach (GapRange persist in storedRanges)
                {
                    Stream writeStream = BeginFileWrite(persist.ImageOffset, BlockType.Other, stride);
                    section.Read((int)persist.FsOffset, (int)persist.Size, writeStream);
                    FinalizeFileWrite(persist.ImageOffset, writeStream);
                }
            }
            else if (section.Type == AreaType.Audio)
            {
                // Audio tracks — store the raw audio data (no stride, full 2352-byte sectors).
                // Audio areas only exist in CUE/GDI disc images (regardless of container format
                // like NKit, 7z, etc.), so leading-zero trimming is always safe to apply here.
                //
                // Skip all leading zero bytes and start storing from the first non-zero byte.
                // This ensures different masterings with the same audio content (but different
                // amounts of leading silence) produce identical blocks for deduplication.
                byte[] data = section.Decrypted;
                int size = (int)section.Size;

                long firstNonZero = LeadingZeroScanner.FindFirstNonZero(data, 0, size);

                if (firstNonZero == size)
                {
                    // All bytes are zero — store TrimOffset = partition size, write no blocks.
                    AreaRecord area = _imageWriter.GetAreas().FirstOrDefault(a => a.Offset == section.ImageOffset);
                    if (area != null)
                    {
                        AreaMetadata meta = area.Metadata ?? new AreaMetadata();
                        meta.Set(AreaValueType.TrimOffset, (long)size);
                        _imageWriter.UpdateAreaMetadata(area.Id, meta);
                    }
                }
                else if (firstNonZero > 0)
                {
                    // Record the exact first-non-zero position as TrimOffset and write data from there.
                    AreaRecord area = _imageWriter.GetAreas().FirstOrDefault(a => a.Offset == section.ImageOffset);
                    if (area != null)
                    {
                        AreaMetadata meta = area.Metadata ?? new AreaMetadata();
                        meta.Set(AreaValueType.TrimOffset, firstNonZero);
                        _imageWriter.UpdateAreaMetadata(area.Id, meta);
                    }

                    long writeOffset = section.ImageOffset + firstNonZero;
                    _imageWriter.WriteData(writeOffset, data, (int)firstNonZero, size - (int)firstNonZero, BlockType.File);
                }
                else
                {
                    // No leading zeros — store verbatim.
                    _imageWriter.WriteData(section.ImageOffset, data, 0, size, BlockType.File);
                }
            }
            else if (section.Type == AreaType.Other)
            {
                // Other areas (pre-gap, lead-in, etc.) — store verbatim
                _imageWriter.WriteData(section.ImageOffset, section.Decrypted, 0, (int)section.Size, BlockType.Other);
            }
        }

        /// <summary>
        /// Opens a write stream for the provided absolute image offset.
        /// </summary>
        public Stream BeginFileWrite(long imageOffset, BlockType type, DataStride stride, long? strideOriginOffset = null) => _imageWriter.BeginWriteStream(imageOffset, type, imageOffset, stride, strideOriginOffset);

        /// <summary>
        /// Finalizes and closes a previously opened write stream.
        /// </summary>
        public void FinalizeFileWrite(long imageOffset, Stream stream) => stream?.Dispose();

        /// <summary>
        /// Converts a filesystem-relative offset into an absolute image offset using
        /// the partition base image offset and stride information.
        /// </summary>
        public long ToImageOffsetFromFsOffsets(long partitionImageOffset, long fsOffset)
        {
            ScanArea area = _context.Scan?.Areas?.FirstOrDefault(a => a.ImageOffset == partitionImageOffset);
            if (area == null)
                return partitionImageOffset + fsOffset;

            AreaInfo ai = area.AreaInfo;

            // For systems with AddressMode.Relative (PS3, Dreamcast), the filesystem offsets
            // include the area's BaseOffset (making them relative to _areaBaseOffset, i.e. the
            // session/partition-3 start for Dreamcast). To get the true image offset:
            //   imageOffset = areaBaseOffset + CleanToOffset(fsOffset)
            // where areaBaseOffset = partitionImageOffset - ai.BaseOffset.
            if (ai.FsAddressMode == AddressMode.Relative)
            {
                long areaBaseOffset = partitionImageOffset - ai.BaseOffset;
                if (ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
                {
                    long fullBlocks = fsOffset / ai.BlockFsSize;
                    int rem = (int)(fsOffset % ai.BlockFsSize);
                    return areaBaseOffset + (fullBlocks * ai.BlockSize) + ai.BlockFsOffset + rem;
                }
                return areaBaseOffset + fsOffset;
            }

            if (ai.BlockSize != ai.BlockFsSize && ai.BlockFsSize != 0)
            {
                // Map clean fsOffset to strided image offset
                long fullBlocks = fsOffset / ai.BlockFsSize;
                int rem = (int)(fsOffset % ai.BlockFsSize);
                long imageOffset = partitionImageOffset + (fullBlocks * ai.BlockSize) + ai.BlockFsOffset + rem;
                return imageOffset;
            }

            return partitionImageOffset + fsOffset;
        }

        /// <summary>
        /// Produces a Sector_Padding_Pack for the section's raw sectors and persists it
        /// as a single BlockType.BlockPadding record. The pack uses a compact bitmap + flag byte
        /// format that stores only non-recreatable bytes (sync, MSF, subheader, EDC, ECC,
        /// extended user data) for sectors that differ from deterministically computed values.
        /// </summary>
        public void FinaliseSectionAndPersistBlockPadding(long imageOffset, ISection section, bool isFs, DataStride stride)
        {
            if (section == null) return;

            AreaInfo ai = section.AreaInfo;
            int blockSize = ai.BlockSize;

            // Only applicable for raw sectors (0x930) — non-raw block sizes don't need padding
            if (blockSize != SectorPaddingPacker.RawSectorSize)
                return;

            // Audio tracks have no sector headers to validate
            if (section.Type == AreaType.Audio)
                return;

            try
            {
                byte[] sectionData = section.Decrypted;
                int sectionSize = (int)section.Size;
                int sectorCount = sectionSize / blockSize;

                // Prefer the per-sector breakdown the ISO9660 SectionProcessor already computed in
                // parallel (SectorPaddingPacker.Analyse), so we don't run the expensive
                // reconstruct-and-compare a second time here.
                Nanook.NKit.SectorFlags[] flags =
                    (section as Nanook.NKit.Iso.Iso9660.SectionProcessor)?.SectorPadding;

                byte[] packData;
                if (flags != null)
                {
                    // If the whole section is fully recreatable (no sync/MSF/EDC/ECC to store), skip
                    // the record entirely — the reader regenerates everything from the physical LBA
                    // when no BlockPadding segment is present (see ImageBuilderIso9660Stream). This is
                    // the byte-saving win from a correct IsCreatable.
                    if (SectorPaddingPacker.IsFullyCreatable(flags))
                        return;

                    packData = SectorPaddingPacker.Serialize(sectionData, flags);
                }
                else
                {
                    // Fallback (no precomputed breakdown available, e.g. a section not produced by the
                    // ISO9660 SectionProcessor): compute it here as before so behaviour never regresses.
                    // Determine the expected physical offset (LBA) for this section.
                    // track.PhysicalOffset is stored in bytes (LBA * blockSize), so divide by blockSize to get the LBA.
                    // Fallback: if no track covers this section (e.g. Dreamcast continuation partitions),
                    // use the area's PhysicalOffset property which is also in bytes.
                    SourceFileTrack track = _context.SourceFile?.IndexFile?.Items?.FirstOrDefault(a => a.ImageOffset <= section.ImageOffset && a.ImageOffset + a.Size > section.ImageOffset);
                    long physicalOffset;
                    long trackImageOffset;
                    if (track != null)
                    {
                        physicalOffset = track.PhysicalOffset;
                        trackImageOffset = track.ImageOffset;
                    }
                    else
                    {
                        // Fallback to area properties — PhysicalOffset is stored in bytes (LBA * blockSize)
                        object propPhysOff = ai.Properties?["PhysicalOffset"];
                        physicalOffset = propPhysOff != null ? (long)(ulong)propPhysOff : 0;
                        trackImageOffset = ai.ImageOffset;
                    }
                    long startLba = (physicalOffset / blockSize) + ((section.ImageOffset - trackImageOffset) / blockSize);

                    packData = SectorPaddingPacker.Pack(sectionData, sectorCount, startLba);
                }

                if (packData != null)
                    _imageWriter.WriteData(imageOffset, packData, BlockType.BlockPadding, imageOffset);
            }
            catch (Exception ex)
            {
                _context.Log?.Error(() => $"FinaliseSectionAndPersistBlockPadding failed at {imageOffset:X}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Determines if a file should be preserved in the datastore.
        /// Returns false for files that should be skipped (zero-length, missing).
        /// </summary>
        public bool ShouldPreserveFile(ISection section, IFsFile file)
        {
            if (file == null)
                return false;

            if (file.FsSize == 0)
                return false;

            return !file.IsMissing;
        }

        /// <summary>
        /// Generates filesystem YAML from the ISO9660 file system tree for data tracks
        /// with non-null FileSystem.
        /// </summary>
        public void BuildFileSystemYaml(Scan scan)
        {
            if (_imageWriter == null || scan == null)
                return;

            Dictionary<string, FsYaml> perTypeYaml = BuildPerTypeFsYaml(scan);

            // Always write per-type files so the mount can show type subfolders in system mode
            foreach ((string typeName, FsYaml yaml) in perTypeYaml)
            {
                byte[] nkfsBytes = NKitDataStore.NkFs.FromFsYaml(yaml).ToBytes();
                string fileName = $"filesystem.{typeName}.nkfs";
                _imageWriter.WriteFile(fileName, nkfsBytes, isSystem: true);
            }

            // Also write the unified filesystem.nkfs using the best (highest priority) type.
            // This is what non-system mode shows by default, and provides backward compatibility.
            if (perTypeYaml.Count > 0)
            {
                FsYaml unified = selectBestFsYaml(perTypeYaml);
                byte[] unifiedBytes = NKitDataStore.NkFs.FromFsYaml(unified).ToBytes();
                _imageWriter.WriteFile(NKitDataStore.DataStore.FileSystemNkfsRootPath, unifiedBytes, isSystem: true);
            }

            // Store CUE/GDI index file and auxiliary files
            StoreIndexAndAuxiliaryFiles();
        }

        /// <summary>
        /// Groups files by resolved filesystem type, building a separate FsYaml per type.
        /// Extension filesystems are merged into their parent type (RockRidge → iso9660, Cdxa → parent).
        /// When a file has both Iso9660 and RockRidge FstLinks, the RockRidge name is used for the iso9660 entry.
        /// Files with no FstLinks are skipped with a diagnostic log message.
        /// </summary>
        /// <param name="scan">The scan containing filesystem areas to process.</param>
        /// <returns>Dictionary keyed by resolved filesystem type name (lowercase), each containing a FsYaml with that type's files.</returns>
        internal Dictionary<string, FsYaml> BuildPerTypeFsYaml(Scan scan)
        {
            Dictionary<string, FsYaml> result = new Dictionary<string, FsYaml>(StringComparer.OrdinalIgnoreCase);

            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type != AreaType.FileSystem)
                    continue;

                IFileSystem fs = area.FsInfo?.FileSystem;
                if (fs?.Files == null || fs.Files.Count == 0)
                    continue;

                AreaInfo ai = area.AreaInfo;

                // Build a DataStride when the area uses strided blocks
                DataStride stride = null;
                if (ai.BlockSize > 0 && ai.BlockFsSize > 0 && ai.BlockSize != ai.BlockFsSize)
                    stride = new DataStride { SourceBlockSize = ai.BlockSize, DataOffset = ai.BlockFsOffset, DataLength = ai.BlockFsSize };

                // Use area number and type for the filesystem name (mirrors Scan.VirtualFs naming)
                string fsName = $"{ai.AreaNo + 1:D2} {ai.Type}";

                IEnumerable<IFsFile> fileSource = area.FsInfo?.FidelityFiles?.Entries
                    ?? (IEnumerable<IFsFile>)fs.Files;

                // Track system entry offsets already emitted to avoid duplicates from FidelityFiles
                // (FidelityFiles can contain both a displaced entry and its replacement at the same offset)
                HashSet<long> emittedSystemOffsets = null;

                foreach (IFsFile file in fileSource)
                {
                    if (file.IsMissing || string.IsNullOrEmpty(file.FullName))
                        continue;

                    // Cast to FstFile to access Links
                    FstFile fstFile = file as FstFile;
                    if (fstFile == null || fstFile.Links == null || fstFile.Links.Count == 0)
                    {
                        _context.Log?.Info(() => $"BuildPerTypeFsYaml: Skipping file '{file.FullName}' with no FstLinks.");
                        continue;
                    }

                    // Skip non-first split parts — the first part (SplitIndex == 0) emits all extents
                    if (fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2 && fstFile.SplitIndex > 0)
                        continue;

                    bool isSystemEntry = file.IsSystemFile;

                    // Deduplicate system entries: FidelityFiles may contain multiple FstFile objects
                    // at the same offset (displaced + replacement from size-collision handling).
                    // Only emit the first one encountered to prevent NkFs interpreting duplicates as extents.
                    if (isSystemEntry)
                    {
                        emittedSystemOffsets ??= new HashSet<long>();
                        if (!emittedSystemOffsets.Add(file.FsOffset))
                            continue; // Already emitted a system entry at this offset
                    }

                    long imageOffset;
                    if (isSystemEntry)
                    {
                        // System entries (UDF metadata) have FsOffset that is NOT area-relative —
                        // it already represents the correct position. Do not add area.ImageOffset.
                        imageOffset = stride != null
                            ? stride.CleanToOffset(file.FsOffset, false)
                            : file.FsOffset;
                    }
                    else
                    {
                        imageOffset = stride != null
                            ? area.ImageOffset + stride.CleanToOffset(file.FsOffset, false)
                            : area.ImageOffset + file.FsOffset;
                    }

                    // Determine which resolved types this file belongs to and the best name for each
                    // Key: resolved type name, Value: (fullPath for that type's entry)
                    Dictionary<string, string> typeEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Check if there's a RockRidge link (for name priority in iso9660)
                    FstLink rockRidgeLink = null;
                    foreach (FstLink link in fstFile.Links)
                    {
                        if (link.FsType == FsType.RockRidge)
                        {
                            rockRidgeLink = link;
                            break;
                        }
                    }

                    foreach (FstLink link in fstFile.Links)
                    {
                        string resolvedType = ResolveTargetFsType(link.FsType);

                        // If we already have an entry for this resolved type, apply name priority rules
                        if (typeEntries.ContainsKey(resolvedType))
                        {
                            // RockRidge name takes priority over Iso9660 for the iso9660 type
                            if (link.FsType == FsType.RockRidge && resolvedType == "iso9660")
                            {
                                string rrPath = buildFullPath(link);
                                typeEntries[resolvedType] = rrPath;
                            }
                            // Otherwise keep the existing entry (first wins, unless overridden by RockRidge)
                            continue;
                        }

                        // For iso9660 resolved type: if there's a RockRidge link, use its name
                        if (resolvedType == "iso9660" && rockRidgeLink != null && link.FsType == FsType.Iso9660)
                        {
                            string rrPath = buildFullPath(rockRidgeLink);
                            typeEntries[resolvedType] = rrPath;
                        }
                        else
                        {
                            string fullPath = buildFullPath(link);
                            typeEntries[resolvedType] = fullPath;
                        }
                    }

                    // Add file to each resolved type's FsYaml
                    foreach (KeyValuePair<string, string> entry in typeEntries)
                    {
                        string typeName = entry.Key;
                        string fullPath = entry.Value;

                        if (!result.TryGetValue(typeName, out FsYaml yaml))
                        {
                            yaml = new FsYaml();
                            result[typeName] = yaml;
                        }

                        // Get or create the filesystem node for this area within this type's FsYaml
                        FsYamlNode fsNode = getOrCreateFsNode(yaml, fsName, area.ImageOffset);

                        // Mark as system if the file is a system file or has a System FsType link
                        bool isSystem = isSystemEntry || fstFile.Links.Any(l => l.FsType == FsType.System);

                        // Emit split parts as same-name entries when file has ≥2 parts
                        // System entries (e.g. __file_ UDF descriptors) use their own offset/size even if
                        // they have SplitParts set — SplitParts on system entries refers to the target file, not the descriptor.
                        if (!isSystem && fstFile.SplitParts != null && fstFile.SplitParts.Parts.Count >= 2)
                        {
                            foreach (IFsFilePart part in fstFile.SplitParts.Parts.OrderBy(p => p.Index))
                            {
                                IFsFile partFile = part.FsFile;
                                long partImageOffset = ToImageOffsetFromFsOffsets(area.ImageOffset, partFile.FsOffset);
                                fsNode.AddFileByPath(fullPath, partImageOffset, partFile.FsSize, partFile.XxHash, partFile.Crc, isSystem);
                            }
                        }
                        else
                        {
                            fsNode.AddFileByPath(fullPath, imageOffset, file.FsSize, file.XxHash, file.Crc, isSystem);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Builds the full path string from a FstLink's parent folder path and encoded child name.
        /// </summary>
        private static string buildFullPath(FstLink link)
        {
            string parentPath = link.Parent?.Path ?? "";
            string name = link.EncodedChildName ?? "";
            if (string.IsNullOrEmpty(parentPath))
                return "/" + name;
            return parentPath + "/" + name;
        }

        /// <summary>
        /// Gets or creates a filesystem node within a FsYaml for the given area name and offset.
        /// Reuses an existing node if one with the same name already exists.
        /// </summary>
        private static FsYamlNode getOrCreateFsNode(FsYaml yaml, string fsName, long areaOffset)
        {
            foreach (FsYamlNode existing in yaml.FileSystems)
            {
                if (existing.Name == fsName)
                    return existing;
            }
            return yaml.AddFileSystem(fsName, areaOffset);
        }

        /// <summary>
        /// Priority order for filesystem types (highest priority first).
        /// Matches the mount's getBestFilesystem logic.
        /// </summary>
        private static readonly string[] _fsPriority = { "udf", "joliet", "rockridge", "romeo", "iso9660" };

        /// <summary>
        /// Selects the best (highest priority) FsYaml from the per-type dictionary.
        /// Used to produce the unified filesystem.nkfs that represents the default view.
        /// Skips the "system" type in the fallback path since the unified file should
        /// represent the best user-facing filesystem, not system metadata.
        /// </summary>
        private static FsYaml selectBestFsYaml(Dictionary<string, FsYaml> perTypeYaml)
        {
            foreach (string priority in _fsPriority)
            {
                if (perTypeYaml.TryGetValue(priority, out FsYaml yaml))
                    return yaml;
            }
            // Fallback: first non-system entry
            foreach (KeyValuePair<string, FsYaml> kvp in perTypeYaml)
            {
                if (!string.Equals(kvp.Key, "system", StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return perTypeYaml.Values.First();
        }

        /// <summary>
        /// Validates that all track files referenced by the CUE/GDI index are present and readable.
        /// Throws an InvalidOperationException if any referenced track file is missing or unreadable.
        /// This should be called early in the ingestion process before data processing begins.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a referenced track file is missing or unreadable, with a message indicating
        /// which file could not be read.
        /// </exception>
        public void ValidateTrackFiles()
        {
            IndexFile indexFile = _context.SourceFile?.IndexFile;
            if (indexFile?.Items == null || indexFile.Items.Length == 0)
                return;

            string sourceFolder = indexFile.Path;
            if (string.IsNullOrEmpty(sourceFolder))
                sourceFolder = _context.SourceFile?.BasePath;

            if (string.IsNullOrEmpty(sourceFolder))
                return;

            // Check if the source is archived — skip file validation for archived sources
            if (_context.SourceFile?.IsArchived == true)
                return;

            foreach (SourceFileTrack track in indexFile.Items)
            {
                if (string.IsNullOrEmpty(track.FileName))
                    continue;

                // Check if the track file is marked as missing
                if (track.FileIsMissing)
                {
                    string errorMsg = $"Track file '{track.FileName}' referenced by index file is missing. Aborting ingestion.";
                    _context.Log?.Error(() => errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }

                // Verify the file exists on disk and is readable
                string trackFilePath = Path.Combine(sourceFolder, track.FileName);
                if (!File.Exists(trackFilePath))
                {
                    string errorMsg = $"Track file '{track.FileName}' referenced by index file is missing at path '{trackFilePath}'. Aborting ingestion.";
                    _context.Log?.Error(() => errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }

                try
                {
                    // Attempt to open the file to verify it's readable
                    using (FileStream fs = File.Open(trackFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // Just verify we can open it — don't need to read content
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    string errorMsg = $"Track file '{track.FileName}' referenced by index file is unreadable: {ex.Message}. Aborting ingestion.";
                    _context.Log?.Error(() => errorMsg);
                    throw new InvalidOperationException(errorMsg, ex);
                }
            }
        }

        /// <summary>
        /// Persists the CHD-carried track layout for a CHD-sourced image (which has no external
        /// index file to copy). Behaviour depends on media type:
        /// <list type="bullet">
        /// <item>GD-ROM (Dreamcast): writes ONLY the authoritative <c>chd.meta.txt</c> (no synthesised
        /// .cue), because a GD-ROM CHD has no inherent cue/gdi representation — cue vs gdi is an
        /// output choice reconstructed on export from the CHD metadata.</item>
        /// <item>Non-GD multi-track CD (PS1/Saturn/etc., stored as CUE): writes a synthesised CUE
        /// index derived from the CHD tracks, which is the correct representation for those sources.</item>
        /// </list>
        /// No-op for plain single-track ISO-in-CHD sources.
        /// </summary>
        private void StoreChdGeneratedIndex()
        {
            if (!IsChdFolderIndex(_context))
                return;

            SourceFileTrack[] tracks = _context.ImageInfo?.Tracks;
            if (tracks == null || tracks.Length == 0)
                return;

            try
            {
                string baseName = _context.SourceFile?.Name ?? "image";
                SourceFileTrack[] ordered = tracks.OrderBy(t => t.TrackIndex).ToArray();

                // A GD-ROM CHD has NO inherent CUE or GDI representation — it is raw tracks + CHGD
                // metadata, and cue vs gdi is only a preservation/output CHOICE. Synthesising a .cue
                // sidecar here would fabricate a provenance the source never had (and any stray
                // .cue/.gdi in the source folder may belong to a different mastering). So for GD-ROM
                // we store ONLY the authoritative CHD metadata (chd.meta.txt); read-back rehydrates a
                // genuine CHD source from it and export produces the correct cue/gdi on demand.
                if (_context.ImageInfo?.MediaType == MediaType.GD)
                {
                    WriteChdMetaFile(ordered);
                    return;
                }

                // Non-GD multi-track CD CHD (PS1/Saturn/SegaCD etc.) is stored as ImageFormat.Cue —
                // these DID come from a standard redump CUE layout, so a synthesised CUE index is the
                // correct representation and is required for reconstruction. (No GDI is ever generated
                // here; GDI is a Dreamcast-only convert-to output.)
                List<string> fileNames = new List<string>(ordered.Length);
                foreach (SourceFileTrack t in ordered)
                {
                    string name = !string.IsNullOrEmpty(t.FileName)
                        ? t.FileName
                        : $"{baseName} (Track {t.TrackIndex:D2}).bin";
                    fileNames.Add(name);
                }

                string indexText = IndexFile.ToCue(fileNames, split: true, tracks: ordered);

                if (string.IsNullOrEmpty(indexText))
                    return;

                string indexFileName = baseName + ".cue";
                byte[] data = System.Text.Encoding.UTF8.GetBytes(indexText);
                _imageWriter.WriteFile(indexFileName, data);
                _context.Log?.Info(() => $"Generated and stored CUE index '{indexFileName}' ({data.Length} bytes) from CHD tracks.");
            }
            catch (Exception ex)
            {
                _context.Log?.Info(() => $"Failed to generate CHD index file: {ex.Message}");
            }
        }

        /// <summary>
        /// The loose datastore file name that stores the CHD's raw per-track metadata for a
        /// Dreamcast GD-ROM source. On read-back (DataStoreAsIso, ImageFormat.Chd) these lines are
        /// fed back through <see cref="Chd.ChdMetaData.Parse"/> so the deduped image presents
        /// identically to a live CHD file, letting the existing GdRomWriter export path reproduce
        /// the correct CUE/GDI (including chdman PAD-frame and pregap geometry).
        /// </summary>
        internal const string ChdMetaFileName = "chd.meta.txt";

        /// <summary>
        /// Reconstructs and stores the CHD's raw track metadata lines (exactly the KEY:VALUE form
        /// ChdMetaData.Parse consumes) plus the CHD block size, as a loose chd.meta.txt file.
        /// Field order is irrelevant — Parse reads via keyed regex lookups — but TAG is emitted
        /// first for readability. The bytes stored in the shard remain the unmodified CHD extraction.
        /// </summary>
        private void WriteChdMetaFile(SourceFileTrack[] tracks)
        {
            try
            {
                if (tracks == null || tracks.Length == 0)
                    return;

                // Recover the CHD block size from the track geometry: LogicalSize = Blocks * chdBlockSize.
                // (chdBlockSize is not exposed on IImageInfo; this is exact for GD-ROM CHDs.)
                int chdBlockSize = 0;
                foreach (SourceFileTrack t in tracks)
                {
                    if (t.Blocks > 0 && t.LogicalSize > 0)
                    {
                        chdBlockSize = (int)(t.LogicalSize / t.Blocks);
                        break;
                    }
                }

                StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("CHDBLOCKSIZE:").Append(chdBlockSize).Append('\n');

                foreach (SourceFileTrack t in tracks)
                {
                    if (t.RawItems == null || t.RawItems.Count == 0)
                    {
                        _context.Log?.Info(() => $"CHD meta: track {t.TrackIndex} has no RawItems; skipping meta persistence.");
                        return; // incomplete metadata — do not write a partial/misleading meta file
                    }

                    // Emit TAG first, then remaining keys. Values are single tokens (no spaces).
                    if (t.RawItems.TryGetValue("TAG", out string tag))
                        sb.Append("TAG:").Append(tag);
                    foreach (KeyValuePair<string, string> kv in t.RawItems)
                    {
                        if (kv.Key == "TAG")
                            continue;
                        sb.Append(' ').Append(kv.Key).Append(':').Append(kv.Value);
                    }
                    sb.Append('\n');
                }

                byte[] metaBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
                _imageWriter.WriteFile(ChdMetaFileName, metaBytes);
                _context.Log?.Info(() => $"Stored CHD track metadata '{ChdMetaFileName}' ({metaBytes.Length} bytes, {tracks.Length} tracks, blockSize {chdBlockSize}).");
            }
            catch (Exception ex)
            {
                _context.Log?.Info(() => $"Failed to write CHD meta file: {ex.Message}");
            }
        }

        /// <summary>
        /// Stores the CUE/GDI index file content and any auxiliary files (.sub, .txt, .nfo, .ccd, .img metadata)
        /// found in the source folder into the DataStore file store.
        /// Follows the same pattern as the WiiU formatter's storage of tik, cetk, and other non-content files.
        /// </summary>
        private void StoreIndexAndAuxiliaryFiles()
        {
            IndexFile indexFile = _context.SourceFile?.IndexFile;
            if (indexFile == null)
            {
                // CHD sources have no external index file — regenerate and store the .cue/.gdi
                // index text from the CHD-carried tracks so the image reconstructs as a proper
                // multi-track indexed image rather than a single flat file.
                StoreChdGeneratedIndex();
                return;
            }

            HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Store the CUE/GDI index file content
            if (indexFile.Data != null && indexFile.Data.Length > 0)
            {
                try
                {
                    string indexFileName = indexFile.FileName;
                    if (!string.IsNullOrEmpty(indexFileName))
                    {
                        _imageWriter.WriteFile(indexFileName, indexFile.Data);
                        written.Add(indexFileName);
                        _context.Log?.Info(() => $"Stored index file '{indexFileName}' ({indexFile.Data.Length} bytes) in DataStore file store.");
                    }
                }
                catch (Exception ex)
                {
                    _context.Log?.Info(() => $"Failed to write index file: {ex.Message}");
                }
            }

            // 2. Store any additional files already identified by the IndexFile parser
            if (indexFile.Additional != null)
            {
                foreach (FileItem addFile in indexFile.Additional)
                {
                    if (addFile == null || string.IsNullOrEmpty(addFile.FileName) || written.Contains(addFile.FileName))
                        continue;

                    try
                    {
                        byte[] fileData = addFile.Data;
                        if (fileData == null)
                        {
                            // Try to read from disk
                            string filePath = Path.Combine(addFile.Path ?? indexFile.Path ?? "", addFile.FileName);
                            if (File.Exists(filePath))
                                fileData = File.ReadAllBytes(filePath);
                        }

                        if (fileData != null && fileData.Length > 0)
                        {
                            _imageWriter.WriteFile(addFile.FileName, fileData);
                            written.Add(addFile.FileName);
                            _context.Log?.Info(() => $"Stored additional file '{addFile.FileName}' ({fileData.Length} bytes) in DataStore file store.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Log?.Info(() => $"Failed to write additional file '{addFile.FileName}': {ex.Message}");
                    }
                }
            }

            // 3. Scan source folder for auxiliary files not already stored
            ScanAndStoreAuxiliaryFiles(indexFile, written);
        }

        /// <summary>
        /// Scans the source folder for auxiliary files that share the same base name as the
        /// CUE/GDI index file but have a different extension, and are not referenced as track
        /// data files by the index. For example, if the index is "Game.cue", this stores
        /// "Game.sub", "Game.ccd", "Game.txt", etc.
        /// </summary>
        private void ScanAndStoreAuxiliaryFiles(IndexFile indexFile, HashSet<string> alreadyWritten)
        {
            string sourceFolder = indexFile.Path;
            if (string.IsNullOrEmpty(sourceFolder))
                sourceFolder = _context.SourceFile?.BasePath;

            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
                return;

            // If the source is archived, we can't scan the folder
            if (_context.SourceFile?.IsArchived == true)
                return;

            // Get the base name of the index file (without extension)
            string indexBaseName = Path.GetFileNameWithoutExtension(indexFile.FileName ?? "");
            if (string.IsNullOrEmpty(indexBaseName))
                return;

            // Build a set of track data file names referenced by the index to exclude
            HashSet<string> referencedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (indexFile.Items != null)
            {
                foreach (SourceFileTrack track in indexFile.Items)
                {
                    if (!string.IsNullOrEmpty(track.FileName))
                        referencedFileNames.Add(track.FileName);
                }
            }

            // Also exclude the index file itself
            if (!string.IsNullOrEmpty(indexFile.FileName))
                referencedFileNames.Add(indexFile.FileName);

            try
            {
                string[] files = Directory.GetFiles(sourceFolder);
                foreach (string filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);

                    // Skip if already written
                    if (alreadyWritten.Contains(fileName))
                        continue;

                    // Skip files referenced by the index (track data files)
                    if (referencedFileNames.Contains(fileName))
                        continue;

                    // Only store files that share the same base name as the index file
                    string fileBaseName = Path.GetFileNameWithoutExtension(fileName);
                    if (!string.Equals(fileBaseName, indexBaseName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        byte[] fileData = File.ReadAllBytes(filePath);
                        if (fileData.Length > 0)
                        {
                            _imageWriter.WriteFile(fileName, fileData);
                            alreadyWritten.Add(fileName);
                            _context.Log?.Info(() => $"Stored auxiliary file '{fileName}' ({fileData.Length} bytes) in DataStore file store.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _context.Log?.Info(() => $"Failed to read/write auxiliary file '{fileName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _context.Log?.Info(() => $"Failed to scan source folder for auxiliary files: {ex.Message}");
            }
        }

        /// <summary>
        /// Finalizes the image by setting its final size and checksums.
        /// </summary>
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
        }

        public bool AlreadyExists => _imageWriter?.AlreadyExists ?? false;

        /// <summary>
        /// Resolves the target nkfs file type for a given FsType.
        /// Extension filesystems are merged into their parent type:
        /// RockRidge always merges into "iso9660", Cdxa merges into its parent filesystem.
        /// All other types map to their lowercase string representation.
        /// </summary>
        /// <param name="fsType">The filesystem type to resolve.</param>
        /// <param name="parentFsType">The parent filesystem type for Cdxa resolution. Defaults to Iso9660 if null.</param>
        /// <returns>The lowercase target filesystem type name for nkfs file naming.</returns>
        internal static string ResolveTargetFsType(FsType fsType, FsType? parentFsType = null)
        {
            return fsType switch
            {
                FsType.RockRidge => "iso9660",
                FsType.Cdxa => (parentFsType ?? FsType.Iso9660).ToString().ToLowerInvariant(),
                FsType.System => "system",  // Changed: separate system file, merged at mount time
                _ => fsType.ToString().ToLowerInvariant()
            };
        }

        /// <summary>
        /// Disposes the image writer and data store.
        /// </summary>
        public void Dispose()
        {
            try { _imageWriter?.Dispose(); } catch { }
            try { _dataStore?.Dispose(); } catch { }
        }
    }
}