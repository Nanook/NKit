using Nanook.NKit.ImageBuilderStreams;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using NKitDataStore; // for DataStride, OffsetRecord, etc.
using NKitDataStore.Interfaces;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Stream-based access to Wii disc images with NJunk generation support.
    /// Extends NKitDataStore.ImageBuilder to provide Wii-specific gap filling for reproducible junk data.
    /// </summary>
    public class ImageBuilderWiiStream : ImageBuilder
    {
        private readonly IImageReader _imageReader;
        private readonly IBlockProvider _wiiBlockProvider;

        /// <summary>
        /// Normalized context for each area containing all pre-computed properties.
        /// Populated once during construction to avoid repeated lookups.
        /// </summary>
        private class WiiAreaContext
        {
            // Common properties
            public AreaRecord Area { get; set; }
            public bool IsFileSystem { get; set; }
            public bool IsOther { get; set; }

            // Other areas share the disc-level _junkGenerator
            public ImageBuilderNJunkGenerator JunkGenerator { get; set; }

            // FileSystem-specific properties (null for Other areas)
            public PartitionInfo Partition { get; set; }
            public byte[] PartitionKey { get; set; }
            public bool IsEncrypted { get; set; }
            public long FsSize { get; set; }
        }

        // Pre-computed context for all areas (indexed by area ID)
        private readonly Dictionary<long, WiiAreaContext> _areaContexts = new Dictionary<long, WiiAreaContext>();

        // Disc-level NJunk generator (shared by implicit disc 'Other' areas)
        private readonly ImageBuilderNJunkGenerator _discJunkGenerator;

        // Disc header fields parsed from ImageHeader area
        private string _discId;
        private int _discNo;
        private string _discType;

        /// <summary>
        /// Creates a new ImageBuilderWiiStream for reading Wii disc images.
        /// /// <param name="reader">The image reader to read data from.</param>
        /// <param name="bufferSize">Buffer size for caching (default: 2MB).</param>
        /// <param name="maxCachedBuffers">Maximum number of buffers to cache (default: 16).</param>
        public ImageBuilderWiiStream(IImageReader reader, int maxCachedBuffers = 0x10, bool disposeReader = true, IBlockProvider blockProvider = null, ImageBufferCache sharedBufferCache = null, OffsetsManagerCacheResult cachedOffsets = null)
            : base(reader, maxCachedBuffers, blockProvider: blockProvider, disposeReader: disposeReader, sharedBufferCache: sharedBufferCache, cachedOffsets: cachedOffsets)
        {
            _imageReader = reader ?? throw new ArgumentNullException(nameof(reader));
            _wiiBlockProvider = blockProvider;

            // Pre-parse all areas and build normalized context
            _discJunkGenerator = new ImageBuilderNJunkGenerator();
            buildAreaContexts();

            // Initialize disc-level NJunk generator
            initializeDiscLevelJunk();
        }

        /// <summary>
        /// Builds normalized WiiAreaContext for all areas, pre-computing all properties.
        /// </summary>
        private void buildAreaContexts()
        {
            List<AreaRecord> areas = _imageReader.GetAreas().OrderBy(a => a.Offset).ToList();

            // First pass: Extract disc header info
            foreach (AreaRecord area in areas)
            {
                string fsType = area.Metadata.GetString(AreaValueType.FsType);
                if (fsType == "ImageHeader")
                {
                    _discId = area.Metadata.GetString(AreaValueType.ID);
                    _discNo = (int)(area.Metadata.GetLong(AreaValueType.DiscNo) ?? 0);
                    _discType = area.Metadata.GetString(AreaValueType.Type);
                    break;
                }
            }

            // Second pass: Build context for each area
            foreach (AreaRecord area in areas)
            {
                string fsType = area.Metadata.GetString(AreaValueType.FsType);
                bool isFileSystem = string.Equals(fsType, "FileSystem", StringComparison.OrdinalIgnoreCase);
                bool isOther = string.Equals(fsType, "Other", StringComparison.OrdinalIgnoreCase);

                WiiAreaContext context = new WiiAreaContext
                {
                    Area = area,
                    IsFileSystem = isFileSystem,
                    IsOther = isOther,
                };

                if (isFileSystem)
                    populateFileSystemContext(context, area, areas);
                else if (isOther)
                    context.JunkGenerator = _discJunkGenerator;

                _areaContexts[area.Id] = context;
            }
        }

        /// <summary>
        /// Populates FileSystem-specific properties for the context.
        /// </summary>
        private void populateFileSystemContext(WiiAreaContext context, AreaRecord area, List<AreaRecord> allAreas)
        {
            // Build partition info
            PartitionType ptype = PartitionType.Game;
            string partitionTypeStr = area.Metadata.GetString(AreaValueType.PartitionType);
            if (!string.IsNullOrEmpty(partitionTypeStr) && Enum.TryParse<PartitionType>(partitionTypeStr, out PartitionType parsed))
                ptype = parsed;

            PartitionInfo partition = new PartitionInfo(ptype, area.Offset, 0, 0);
            string id = area.Metadata.GetString(AreaValueType.ID);
            if (!string.IsNullOrEmpty(id))
                partition.Id = id;

            context.Partition = partition;

            // Extract block parameters
            int blockSize = (int)(area.Metadata.GetLong(AreaValueType.BlockSize) ?? 0x8000);
            int hashSize = (int)(area.Metadata.GetLong(AreaValueType.HashSize) ?? 0);
            int blockFsOffset = hashSize;
            int blockFsSize = blockSize - blockFsOffset;

            // Compute FsSize
            context.FsSize = Buffer.OffsetToFsOffset(area.Size, blockSize, blockFsOffset, blockFsSize);

            // Extract junk and encryption info
            string junkId = area.Metadata.GetString(AreaValueType.JunkID);
            int discNo = (int)area.Metadata.GetLong(AreaValueType.DiscNo);
            long junkLeadingNulls = area.Metadata.GetLong(AreaValueType.JunkLeadingNulls) ?? 0;
            context.IsEncrypted = area.Metadata.GetBool(AreaValueType.Encrypted) ?? false;

            // Compute totalSize and baseFs for junk generation
            long totalSize = area.GetDataStride().OffsetToClean(context.FsSize);

            // Find header area - it should be the immediately preceding area
            AreaRecord headerArea = null;
            if (area.Index > 0)
            {
                AreaRecord prevArea = allAreas[area.Index - 1];
                if (string.Equals(prevArea.Metadata.GetString(AreaValueType.FsType), "PartitionHeader", StringComparison.OrdinalIgnoreCase))
                    headerArea = prevArea;
            }

            if (headerArea != null && headerArea.Size > 0)
            {
                try
                {
                    byte[] hdr = null;

                    // Try primary reader first
                    try
                    {
                        using (Stream stream = _imageReader.OpenStream(headerArea.Offset))
                            hdr = stream.ReadBytes(headerArea.Size);
                    }
                    catch { }

                    // If primary returned null/empty/zeros, try aux reader (partition header may be in aux store)
                    if ((hdr == null || hdr.Length == 0 || hdr.All(b => b == 0))
                        && _wiiBlockProvider is AuxBlockProvider auxBp && auxBp.AuxReader != null)
                    {
                        try
                        {
                            using (Stream stream = auxBp.AuxReader.OpenStream(headerArea.Offset))
                                hdr = stream.ReadBytes(headerArea.Size);
                        }
                        catch { }
                    }

                    if (hdr != null && hdr.Length > 0)
                    {
                        WiaPartition ptn = ConvertWiiGcRvzStep.CreateWiaPartition(hdr, headerArea.Offset, hdr.Length, null, true, _imageReader.Image.Size, out bool isRvtH);
                        if (isRvtH)
                            totalSize = _imageReader.Image.Size;

                        context.PartitionKey = ptn.Key;
                    }
                }
                catch { }
            }

            // Create and initialize partition-specific junk generator if JunkId is present
            if (!string.IsNullOrEmpty(junkId))
            {
                context.JunkGenerator = new ImageBuilderNJunkGenerator();
                context.JunkGenerator.InitializePartition(junkId, discNo, junkLeadingNulls, totalSize, 0);
            }
        }

        /// <summary>
        /// Initializes disc-level NJunk generator from metadata.
        /// </summary>
        private void initializeDiscLevelJunk()
        {
            try
            {
                AreaRecord[] gameAreas = _areaContexts.Select(a => a.Value.Area).ToArray();
                AreaRecord gameArea = gameAreas.FirstOrDefault(a => a.Metadata.GetString(AreaValueType.PartitionType) == "Game") ?? gameAreas.FirstOrDefault(a => a.Offset > WiiConsts.WiiDefaultDataPtnOffset);
                long totalSize = _imageReader.Image.Size;
                long startOffset = gameAreas.FirstOrDefault(a => !string.IsNullOrEmpty(a.Metadata.GetString(AreaValueType.PartitionType)) && a.Metadata.GetString(AreaValueType.PartitionType) != "Update")?.Offset ?? totalSize; //if not found use nulls for whole image (freeloader)
                if (gameArea != null && gameAreas.Length > gameArea.Index)
                    gameArea = gameAreas[gameArea.Index + 1];

                string discJunkId = gameArea?.Metadata.GetString(AreaValueType.JunkID);
                if (string.IsNullOrEmpty(discJunkId) && _discId?.Length >= 4)
                    discJunkId = _discId.Substring(0, 4);

                if (!string.IsNullOrEmpty(discJunkId))
                {
                    if (_discType == "RVT-R") // limit RVT-R not RVT-H or retail
                        totalSize = totalSize < WiiConsts.FullSizeWii9 ? WiiConsts.FullSizeWii5 : WiiConsts.FullSizeWii9;
                    _discJunkGenerator.InitializeDisc(discJunkId, _discNo, startOffset, totalSize, 0);
                }
            }
            catch { }
        }

        /// <summary>
        /// Called when the stream position moves into a different area.
        /// Updates current partition context for NJunk generation.
        /// </summary>
        protected override void OnAreaChanged(AreaRecord newArea, AreaRecord oldArea) => base.OnAreaChanged(newArea, oldArea);

        /// <summary>
        /// Fills gaps in the reconstructed image with format-specific data.
        /// For Wii partitions with JunkID, generates reproducible NJunk data.
        /// Otherwise fills with zeros (default behavior).
        /// </summary>
        /// <param name="area">The area containing the gap.</param>
        /// <param name="cleanAreaOffset">Clean offset within the area.</param>
        /// <param name="cleanBufferOffset">Clean offset within the buffer being filled.</param>
        /// <param name="size">Size of the gap in clean bytes.</param>
        // NOTE: The base class now provides an overload that includes the original gap start.
        // We override the new signature below to correctly apply file-start padding only when
        // the fragment represents the start of the gap.

        // New overload receives the original gap start (clean-area coordinates) so we can
        // apply file-start padding (alignment + 0x1C zeros) only when this fragment is the
        // very start of the gap (offsetInGap == cleanAreaOffset).
        protected override void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, BufferContext context)
        {
            if (size <= 0)
                return;

            long fragStart = cleanAreaOffset;
            int remaining = (int)size;
            int destOffset = (int)cleanBufferOffset;

            // Lookup pre-computed context
            if (!_areaContexts.TryGetValue(context.Area.Id, out WiiAreaContext areaContext))
            {
                base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
                return;
            }

            // Use the pre-initialized junk generator from the context
            if (areaContext.JunkGenerator != null)
            {
                bool applyFilePadding = areaContext.IsFileSystem; // FileSystem areas use align+0x1C padding
                areaContext.JunkGenerator.WriteJunkWithPadding(context.Area, ref offsetInGap, cleanAreaOffset, ref fragStart, ref remaining, ref destOffset, applyFilePadding, areaContext.IsFileSystem, (src, srcOff, off, len) => WriteToBuffer(src, srcOff, off, len, context));
                return;
            }

            base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
        }


        protected override void OnBufferComplete(int validSize, BufferContext context)
        {
            base.OnBufferComplete(validSize, context);

            // Lookup pre-computed context
            if (!_areaContexts.TryGetValue(context.Area.Id, out WiiAreaContext areaContext))
                return;

            // Only process encrypted filesystem areas
            if (!areaContext.IsFileSystem || !areaContext.IsEncrypted)
                return;

            try
            {
                byte[] buffer = context.Buffer;
                AreaRecord area = context.Area;
                long imageOffset = context.ImageOffset;
                byte[] titleKey = areaContext.PartitionKey;

                // WiiSecurity expects buffers at least WiiGroupSize
                if (buffer.Length >= WiiConsts.WiiGroupSize)
                {
                    WiiSecurity sec = new WiiSecurity(buffer.Length);
                    try
                    {
                        bool creatable = (string)context.Items["Creatable"] == "1";
                        byte[] buff = (byte[])context.Items["State"]!;
                        BitState state = buff == null || buff.Length == 0 ? null : new BitState(buff);

                        sec.Populate(titleKey, buffer, 0, buffer, 0, validSize, false, false, creatable, imageOffset, null, state, !creatable);

                        if (creatable)
                        {
                            sec.MarkDirty();
                            sec.IsValid(creatable, out _);
                        }
                        sec.Encrypt();
                    }
                    catch { }
                }
            }
            catch { }
        }

        protected override void OnBufferPopulatedWithSection(int validSize, BufferContext context)
        {
            context.Items["Creatable"] = "1";
            context.Items["State"] = null;

            OffsetSegment seg = context.Section?.Segments?.FirstOrDefault(seg => seg.Source.Type == BlockType.BlockPadding); // only can be 1 per buffer
            if (seg == null)
                return;

            OffsetRecord src = seg.Source;
            long segImageStart = src.Offset + seg.SourceOffset;
            long segImageEnd = segImageStart + seg.Size;
            long bufImageEnd = context.ImageOffset + validSize;

            int segSize = (int)seg.Size;
            if (segSize <= 0)
                return;

            int blkSize = _imageReader.Info.BlockSize; //this needs to be set in the db and read
            int blocksNeeded = (segSize + blkSize - 1) / blkSize;

            // Read first block to determine header/state
            BlockRecord firstBlock = _imageReader.GetBlock(src.GetBlockAt(0));
            if (firstBlock == null || firstBlock.Data == null || firstBlock.Data.Length == 0)
                return;

            if (firstBlock.Data[0] > 0)
            {
                context.Items["Creatable"] = firstBlock.Data.Length > firstBlock.Data[0] ? "0" : "1"; // there were hashes
                context.Items["State"] = firstBlock.Data.Read(1, firstBlock.Data[0]);
            }

            // Cache blocks for this segment (small, at most a couple blocks)
            BlockRecord[] cachedBlocks = new BlockRecord[blocksNeeded];
            cachedBlocks[0] = firstBlock;

            int segBuffSize = 0; // total bytes present from blocks

            for (int bi = 0; bi < blocksNeeded; bi++)
            {
                BlockRecord blockRecord = cachedBlocks[bi];
                if (blockRecord == null)
                {
                    BlockKey key = src.GetBlockAt(bi);
                    blockRecord = _imageReader.GetBlock(key);
                    cachedBlocks[bi] = blockRecord;
                }

                if (blockRecord == null || blockRecord.Data == null)
                    break;

                int availableInSeg = Math.Min(blockRecord.Data.Length, segSize - (bi * blkSize));
                if (availableInSeg <= 0)
                    break;

                segBuffSize += availableInSeg;
            }

            if (segBuffSize == 0)
                return;

            int headerSkip = 1 + Math.Max(0, (int)firstBlock.Data[0]);
            int payloadLen = Math.Max(0, Math.Min(segBuffSize - headerSkip, segSize - headerSkip));
            if (payloadLen <= 0)
                return;

            int chunkSize = WiiConsts.WiiSectorHashSize;
            int chunks = payloadLen / chunkSize;

            long relBufStart = context.ImageOffset - segImageStart; // can be negative
            long relBufEnd = bufImageEnd - segImageStart; // exclusive

            int strideBlockSize = context.Stride.SourceBlockSize;
            long firstCi = relBufStart <= 0 ? 0 : Math.Max(0, (relBufStart + strideBlockSize - 1) / strideBlockSize);
            long lastCi = Math.Min(chunks - 1, (relBufEnd - 1) / strideBlockSize);

            // Copy chunks directly from cached blocks into context.Buffer
            for (long ci = firstCi; ci <= lastCi; ci++)
            {
                int payloadOffset = headerSkip + (int)(ci * chunkSize);
                int destPos = (int)(segImageStart + (ci * (long)strideBlockSize) - context.ImageOffset);
                if (payloadOffset < 0 || payloadOffset + chunkSize > segSize || destPos < 0 || destPos + chunkSize > context.Buffer.Length)
                    continue;

                int remaining = chunkSize;
                int srcOffset = payloadOffset; // offset within segment
                int outPos = destPos;

                copyFromCachedBlocks(src, cachedBlocks, segSize, blkSize, srcOffset, context.Buffer, outPos, remaining); // if copied < remaining, we didn't have enough data for this chunk - skip silently
            }
        }

        // Helper that copies up to 'count' bytes from the segment (using cachedBlocks) into dest.
        // Returns number of bytes actually copied.
        private int copyFromCachedBlocks(OffsetRecord src, BlockRecord[] cachedBlocks, int segSize, int blkSize, int srcOffset, byte[] dest, int destOffset, int count)
        {
            int remaining = count;
            int sOffset = srcOffset;
            int dPos = destOffset;

            while (remaining > 0)
            {
                int bi = sOffset / blkSize;
                int offInBlock = sOffset % blkSize;

                BlockRecord block = (bi >= 0 && bi < cachedBlocks.Length) ? cachedBlocks[bi] : null;
                if (block == null)
                {
                    BlockKey key = src.GetBlockAt(bi);
                    block = _imageReader.GetBlock(key);
                    if (bi >= 0 && bi < cachedBlocks.Length)
                        cachedBlocks[bi] = block;
                }

                if (block == null || block.Data == null)
                    break; // missing data

                int blockStartInSeg = bi * blkSize;
                int availableInSeg = Math.Min(block.Data.Length, segSize - blockStartInSeg);
                int copyFromBlock = Math.Min(availableInSeg - offInBlock, remaining);
                if (copyFromBlock <= 0)
                    break;

                Array.Copy(block.Data, offInBlock, dest, dPos, copyFromBlock);

                remaining -= copyFromBlock;
                sOffset += copyFromBlock;
                dPos += copyFromBlock;
            }

            return count - remaining;
        }

        protected override void Dispose(bool disposing) =>
            // Do not dispose _imageReader here. Readers may be pooled/shared; lifetime
            // is managed by the caller or ImageReaderPool.
            base.Dispose(disposing);
    }
}