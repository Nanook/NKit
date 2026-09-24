using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using NKitDataStore; // for DataStride, OffsetRecord, etc.
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    /// <summary>
    /// Minimal ImageBuilder for WiiU images.
    /// This provides a small, buildable template mirroring the Wii stream implementation
    /// and can be extended to add full security and NJunk support.
    /// </summary>
    public class ImageBuilderWiiUStream : ImageBuilder
    {
        private readonly IImageReader _imageReader;
        private ImageHeader _header;

        private class WiiUAreaContext
        {
            public AreaRecord Area { get; set; }
            public bool IsFileSystem { get; set; }
            public bool IsOther { get; set; }
            public bool IsEncrypted { get; set; }
            public int BlockSize { get; set; }
            public int HashSize { get; set; }
            public bool HasHashes { get; set; }
            public ContentHeader ContentHeader { get; set; }
            public SiData SiData { get; set; }
            public PartitionType PartitionType { get; set; }
            public int PartitionId { get; set; }
            public int ContentIndex { get; set; }
            public byte[][] SeekIvs { get; set; }
        }

        // AreaStream helpers removed. ImageBuilder base class handles section population and seeking.


        private readonly Dictionary<long, WiiUAreaContext> _areaContexts = new Dictionary<long, WiiUAreaContext>();
        // Maps (partitionId, contentIndex) -> canonical FileSystem WiiUAreaContext for deduplication lookups
        private readonly Dictionary<(int partition, int contentIndex), WiiUAreaContext> _canonicalAreaByContent = new Dictionary<(int, int), WiiUAreaContext>();
        // Cached joined ImageHeader + PartitionTable data for populating pre-partition Other gap areas
        private byte[] _headerPtData;
        private readonly bool _encrypt;
        private readonly IBlockProvider _wiiu_blockProvider;

        public ImageBuilderWiiUStream(IImageReader reader, int maxCachedBuffers = 0x10, bool disposeReader = true, bool encrypt = true, IBlockProvider blockProvider = null, ImageBufferCache sharedBufferCache = null, OffsetsManagerCacheResult cachedOffsets = null)
            : base(reader, maxCachedBuffers, blockProvider: blockProvider, disposeReader: disposeReader, sharedBufferCache: sharedBufferCache, cachedOffsets: cachedOffsets)
        {
            _imageReader = reader ?? throw new ArgumentNullException(nameof(reader));
            _encrypt = encrypt;
            _wiiu_blockProvider = blockProvider;
            // _areaContexts will store all per-area information needed to serve data

            buildAreaContexts();
            try { initializeFromAreas(); } catch { }
        }

        /// <summary>
        /// Returns discovered content areas (index, image offset, size, optional App name) discovered
        /// while building area contexts. Safe public API to avoid reflection for callers that want to
        /// enumerate .app entries derived from content headers.
        /// </summary>
        public IEnumerable<(int Index, long ImageOffset, long Size, string AppName)> GetContentAreas()
        {
            List<(int, long, long, string)> list = new List<(int, long, long, string)>();
            foreach (KeyValuePair<long, WiiUAreaContext> kv in _areaContexts)
            {
                try
                {
                    WiiUAreaContext ctx = kv.Value;
                    if (ctx == null)
                        continue;
                    ContentHeader ch = ctx.ContentHeader;
                    if (ch == null)
                        continue;
                    string appName = null;
                    try { appName = ctx.Area?.Metadata?.GetString(AreaValueType.FileName); } catch { appName = null; }
                    if (string.IsNullOrEmpty(appName))
                        try { appName = ctx.Area?.Metadata?.GetString(AreaValueType.App); } catch { appName = null; }
                    list.Add(((int)ch.Index, ch.ImageOffset, ch.Size, appName ?? ""));
                }
                catch { }
            }

            foreach ((int, long, long, string) item in list)
                yield return item;
        }

        private byte[] readAreaBytes(AreaRecord area)
        {
            byte[] data = null;
            try
            {
                using (Stream s = _imageReader.OpenStream(area.Offset))
                    data = s.ReadBytes((int)area.Size);
            }
            catch { }

            // If primary returned null/empty/zeros, try aux reader (area data may be in aux store)
            if ((data == null || data.Length == 0 || data.All(b => b == 0))
                && _wiiu_blockProvider is AuxBlockProvider auxBp && auxBp.AuxReader != null)
            {
                try
                {
                    using (Stream s = auxBp.AuxReader.OpenStream(area.Offset))
                        data = s.ReadBytes((int)area.Size);
                }
                catch { }
            }

            return data ?? new byte[area.Size];
        }

        private void ensureHeaderPtData(byte[] data, int destOffset, int length)
        {
            int repeatUnit = (int)WiiUConsts.DiscContentsOffset + WiiUConsts.DefaultSectorSize;
            _headerPtData ??= new byte[repeatUnit];
            Array.Copy(data, 0, _headerPtData, destOffset, Math.Min(data.Length, length));
        }

        private void applyTitleKey(AreaRecord area)
        {
            try
            {
                string titleKeyHex = area.Metadata.GetString(AreaValueType.TitleKey);
                if (string.IsNullOrEmpty(titleKeyHex))
                    return;

                byte[] titleKey = titleKeyHex.HexToBytes();
                if (titleKey == null)
                    return;

                _header ??= new ImageHeader(null);
                if (_header.Key == null || _header.Key.Length == 0)
                    _header.Key = titleKey;
                Key = _header.Key;
            }
            catch { }
        }

        private void initializeFromAreas()
        {
            List<AreaRecord> areas = _imageReader.GetAreas().OrderBy(a => a.Offset).ToList();
            // area-level contexts are used; no temporary scan area tracking required here
            Dictionary<long, Nintendo.WiiU.FileSystemInfo> fsInfos = new Dictionary<long, Nanook.NKit.Nintendo.WiiU.FileSystemInfo>();
            long currentPartitionHeaderOffset = -1;

            foreach (AreaRecord area in areas)
            {
                string fsType = area.Metadata.GetString(AreaValueType.FsType);
                //try { Trace.WriteLine($"[Area] Id={area.Id} Offset=0x{area.Offset:X} Size=0x{area.Size:X} FsType={fsType}"); } catch { }
                if (fsType == WiiUConsts.FsTypeImageHeader)
                {
                    try
                    {
                        byte[] data = readAreaBytes(area);
                        _header = new ImageHeader(data) { Key = null };
                        ensureHeaderPtData(data, 0, (int)WiiUConsts.DiscContentsOffset);
                        applyTitleKey(area);
                    }
                    catch { }
                }
                else if (fsType == WiiUConsts.FsTypePartitionTable)
                {
                    try
                    {
                        byte[] data = readAreaBytes(area);
                        _header ??= new ImageHeader(null);
                        _header.Update(data);
                        ensureHeaderPtData(data, (int)WiiUConsts.DiscContentsOffset, WiiUConsts.DefaultSectorSize);
                    }
                    catch { }
                }
                else if (fsType == WiiUConsts.FsTypePartitionHeader)
                {
                    try
                    {
                        byte[] data = readAreaBytes(area);
                        try
                        {
                            Nintendo.WiiU.FileSystemInfo fsInfo = new Nintendo.WiiU.FileSystemInfo(area.Offset, _imageReader.Image.Size, _header, data);
                            fsInfos[area.Offset] = fsInfo;
                            currentPartitionHeaderOffset = area.Offset;
                        }
                        catch { }
                    }
                    catch { }
                }
                else if (fsType == WiiUConsts.FsTypeFstBlock)
                {
                    try
                    {
                        byte[] data = readAreaBytes(area);
                        if (currentPartitionHeaderOffset != -1 && fsInfos.TryGetValue(currentPartitionHeaderOffset, out Nintendo.WiiU.FileSystemInfo fsInfo))
                        {
                            fsInfo.ProcessBlock(area.Offset, data, data.Length, true, null);

                            // Disc-mode read-back: the reconstructed disc bytes must be RE-ENCRYPTED
                            // with the same per-partition title key the original source used, so the
                            // WiiU Image reader (which re-derives its key from the SI partition ticket)
                            // can decrypt the Game FST. The decrypted title key is stored on the
                            // FstBlock area (write side: DataStoreWiiUFormatter, Game → SiData.KeyTitle;
                            // for a wiped Game partition this is NKitWipeTitleKey).
                            //
                            // The re-encryption gate (OnBufferPopulatedWithSection → encryptHashless /
                            // encrypt) reads the key from the FstBlock/FileSystem area's
                            // WiiUAreaContext.SiData.KeyTitle. So hydrate:
                            //   - fsInfo.SiData (propagated to all in-range area contexts below), and
                            //   - the partition's WiiUSiData (kept consistent), and
                            //   - the FstBlock area's own WiiUAreaContext.SiData directly.
                            try
                            {
                                string fstTitleKeyHex = area.Metadata.GetString(AreaValueType.TitleKey);
                                if (!string.IsNullOrEmpty(fstTitleKeyHex))
                                {
                                    byte[] fstTitleKey = fstTitleKeyHex.HexToBytes();
                                    if (fstTitleKey != null && fstTitleKey.Length > 0)
                                    {
                                        // The stored FstBlock TitleKey is AUTHORITATIVE: it is the
                                        // per-partition decrypted title key captured at scan time
                                        // (wiped Game partition = NKitWipeTitleKey / all zeros). It must
                                        // OVERRIDE any KeyTitle inherited from the partition table's
                                        // WiiUSiData, which on reconstruction can carry a stale/wrong key
                                        // and would cause the reconstructed FST to be re-encrypted with a
                                        // key the reader cannot reproduce.
                                        SiData si = fsInfo.SiData;
                                        if (si == null)
                                        {
                                            PartitionInfo ptn = _header?.GetPartition(fsInfo.ImageOffset);
                                            si = ptn?.WiiUSiData ?? new SiData();
                                            if (ptn != null)
                                                ptn.WiiUSiData = si;
                                            fsInfo.SiData = si;
                                        }
                                        si.KeyTitle = fstTitleKey;

                                        // Set the FstBlock area's own context SiData directly; the
                                        // propagation loop later covers FileSystem/Other contexts in range.
                                        if (_areaContexts.TryGetValue(area.Id, out WiiUAreaContext fstCtx))
                                            fstCtx.SiData = si;
                                    }
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            _header ??= new ImageHeader(null);
                            Nintendo.WiiU.FileSystemInfo tmpFs = new Nintendo.WiiU.FileSystemInfo(area.Offset, _imageReader.Image.Size, _header, (SiData)null, data.Length);
                            tmpFs.ProcessBlock(area.Offset, data, data.Length, true, null);
                            fsInfos[area.Offset] = tmpFs;

                            ContentHeader cnt = tmpFs.FstBlock.GetContentHeader(area.Offset);
                            int contentIndex = (int)(area.Metadata.GetLong(AreaValueType.ContentIndex) ?? 0);
                            byte[] currentKey = Key;
                            SiData si = WiiUSecurityContext.BuildSiData(contentIndex, null, null, null, _imageReader.Image.Format, _header, ref currentKey);
                            Key = currentKey;
                            if (tmpFs.FileSystem?.Files != null)
                            {
                                byte[] tmd = null, tik = null, cert = null;
                                WiiUSecurityContext.ReadContentFiles(tmpFs.FileSystem.Files, offset => _imageReader.OpenStream(offset), cnt != null ? cnt.ImageOffset : tmpFs.ImageOffset,
                                    b => tmd = b, b => tik = b, b => cert = b);
                                si = WiiUSecurityContext.BuildSiData(contentIndex, tmd, tik, cert, _imageReader.Image.Format, _header, ref currentKey);
                                Key = currentKey;
                            }

                            if (si != null)
                            {
                                try { _header.Update((int)tmpFs.ImageOffset, si); } catch { }
                                try { if (_header.SiData == null) _header.SiData = new System.Collections.Generic.List<SiData>(); _header.SiData.Add(si); } catch { }
                                try { tmpFs.SiData = si; } catch { }
                            }
                        }
                        applyTitleKey(area);
                    }
                    catch { }
                }
                else if (fsType == WiiUConsts.FsTypeOther)
                {
                    // nothing to do for Other areas in this template
                }
                else if (fsType == WiiUConsts.FsTypeFileSystem)
                {
                    applyTitleKey(area);
                    // Attempt to populate SiData for SI partitions by reading files from the stored filesystem
                    try
                    {
                        // find matching partition header (the last header offset <= this area offset)
                        long partKey = fsInfos.Keys.Where(k => k <= area.Offset).DefaultIfEmpty(-1).Max();
                        if (partKey != -1 && fsInfos.TryGetValue(partKey, out Nintendo.WiiU.FileSystemInfo fsInfo) && fsInfo.FstBlock != null)
                        {
                            ContentHeader cntHeader = fsInfo.FstBlock.GetContentHeader(area.Offset);
                            if (cntHeader != null && fsInfo.Type == PartitionType.Si)
                            {
                                byte[] tmd = null, tik = null, cert = null;
                                WiiUSecurityContext.ReadContentFiles(fsInfo.FileSystem?.Files.Where(ff => ((FstFile)ff).WiiUAppIndex == cntHeader.Index), offset => _imageReader.OpenStream(offset), cntHeader.ImageOffset,
                                    b => tmd = b, b => tik = b, b => cert = b);

                                if (tmd != null || tik != null || cert != null)
                                {
                                    try
                                    {
                                        byte[] currentKey = Key;
                                        SiData si = WiiUSecurityContext.BuildSiData(cntHeader.Index, tmd, tik, cert, _imageReader.Image.Format, _header, ref currentKey);
                                        Key = currentKey;
                                        if (si == null)
                                            continue;

                                        _header.SiData.Add(si);

                                        PartitionInfo ptn = _header.Partitions.FirstOrDefault(a => a.WiiUTitleId == si.TitleId);
                                        if (ptn != null) ptn.WiiUSiData = si;

                                        // Also attach the SiData to the FileSystemInfo so callers that
                                        // reference fsInfo.SiData (like stream builders) will get it.
                                        fsInfo.SiData = si;

                                        // attach to all area contexts that belong to this partition
                                        foreach (KeyValuePair<long, WiiUAreaContext> kv in _areaContexts.Where(kv => kv.Value.Area.Offset >= fsInfo.ImageOffset && kv.Value.Area.Offset < fsInfo.ImageOffset + fsInfo.Size))
                                            kv.Value.SiData = si;

                                        // If a title key was discovered for this SI partition prefer exposing it
                                        try { if (si.KeyTitle != null && Key == null) Key = si.KeyTitle; } catch { }
                                    }
                                    catch { }
                                }
                            }
                            // Store content header and partition type for this FileSystem area if available
                            try
                            {
                                ContentHeader cnt = fsInfo.FstBlock.GetContentHeader(area.Offset);
                                if (cnt != null && _areaContexts.TryGetValue(area.Id, out WiiUAreaContext ctx))
                                {
                                    cnt = cnt.Clone();
                                    ctx.ContentHeader = cnt;
                                    ctx.PartitionType = fsInfo.Type;
                                    // Ensure HasHashes matches what was recorded in the DataStore,
                                    // as initial FST parsing might have lacked TMD info.
                                    cnt.HasHashes = ctx.HasHashes;
                                    cnt.Index = ctx.ContentIndex;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            // Ensure SiData discovered on any canonical FileSystem area is propagated to
            // all area contexts that belong to the same partition/contentIndex so callers
            // (e.g. WiiUSecurity.Populate) can find the title keys from any area context.
            try
            {
                // First, map any SiData and content headers discovered in fsInfos to all
                // area contexts that fall within the same partition image range. This
                // mirrors how the live Image class maps SiData for partitions.
                foreach (Nintendo.WiiU.FileSystemInfo fs in fsInfos.Values)
                {
                    try
                    {
                        // ensure fs.SiData is synchronized with any associations discovered 
                        // after it was created (e.g., from the SI partition).
                        if (fs.SiData == null && _header?.Partitions != null)
                            fs.SiData = _header.GetPartition(fs.ImageOffset)?.WiiUSiData;

                        foreach (WiiUAreaContext ctx in _areaContexts.Values.Where(c => c.Area.Offset >= fs.ImageOffset && c.Area.Offset < fs.ImageOffset + fs.Size))
                        {
                            try
                            {
                                ctx.PartitionType = fs.Type;
                                if (fs.FstBlock != null)
                                {
                                    try
                                    {
                                        ContentHeader cnt = fs.FstBlock.GetContentHeader(ctx.Area.Offset);
                                        if (cnt != null)
                                        {
                                            cnt = cnt.Clone();
                                            ctx.ContentHeader = cnt;
                                            cnt.HasHashes = ctx.HasHashes;
                                            cnt.Index = ctx.ContentIndex;
                                        }
                                    }
                                    catch { }
                                }

                                if (fs.SiData != null)
                                    ctx.SiData = fs.SiData;
                                else if (_header?.SiData != null && ctx.ContentHeader != null)
                                {
                                    // Try matching by TitleId if it was discovered in the FST block
                                    SiData match = _header.SiData.FirstOrDefault(s => s.TitleId == (ulong)ctx.ContentHeader.TitleId);
                                    if (match != null)
                                        ctx.SiData = match;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Fallback: ensure SiData discovered on any canonical FileSystem area is propagated
                // to any remaining contexts that still lack SiData by using the canonical mapping.
                foreach (WiiUAreaContext ctx in _areaContexts.Values)
                {
                    if (ctx.SiData == null)
                    {
                        if (_canonicalAreaByContent.TryGetValue((ctx.PartitionId, ctx.ContentIndex), out WiiUAreaContext canonical) && canonical?.SiData != null)
                            ctx.SiData = canonical.SiData;
                    }
                }
            }
            catch { }
        }

        private void buildAreaContexts()
        {
            List<AreaRecord> areas = _imageReader.GetAreas().OrderBy(a => a.Offset).ToList();
            PartitionType partitionType = PartitionType.Other;
            foreach (AreaRecord area in areas)
            {
                string fsType = area.Metadata.GetString(AreaValueType.FsType);
                bool isFileSystem = string.Equals(fsType, WiiUConsts.FsTypeFileSystem, StringComparison.OrdinalIgnoreCase);
                bool isOther = string.Equals(fsType, WiiUConsts.FsTypeOther, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(fsType, WiiUConsts.FsTypePartitionHeader, StringComparison.OrdinalIgnoreCase))
                    partitionType = Enum.Parse<PartitionType>(area.Metadata.GetString(AreaValueType.PartitionType));
                WiiUAreaContext ctx = new WiiUAreaContext
                {
                    Area = area,
                    IsFileSystem = isFileSystem,
                    IsOther = isOther,
                    IsEncrypted = area.Metadata.GetBool(AreaValueType.Encrypted) ?? false,
                    PartitionType = partitionType,
                    PartitionId = (int)(area.Metadata.GetLong(AreaValueType.Partition) ?? 0),
                    ContentIndex = (int)(area.Metadata.GetLong(AreaValueType.ContentIndex) ?? 0),
                };


                if (isFileSystem)
                {
                    ctx.BlockSize = (int)(area.Metadata.GetLong(AreaValueType.BlockSize) ?? WiiUConsts.DefaultSectorSize);
                    ctx.HashSize = (int)(area.Metadata.GetLong(AreaValueType.HashSize) ?? WiiUConsts.HashSize);
                    ctx.HasHashes = ctx.HashSize > 0;
                    string ptype = area.Metadata.GetString(AreaValueType.PartitionType);
                    if (!string.IsNullOrEmpty(ptype) && Enum.TryParse<PartitionType>(ptype, out PartitionType parsed))
                        ctx.PartitionType = parsed;

                    string seekIvStr = area.Metadata.GetString(AreaValueType.SeekIv);
                    if (!string.IsNullOrEmpty(seekIvStr))
                    {
                        string[] parts = seekIvStr.Split('|');
                        ctx.SeekIvs = new byte[parts.Length][];
                        for (int i = 0; i < parts.Length; i++)
                            ctx.SeekIvs[i] = string.IsNullOrEmpty(parts[i]) ? null : parts[i].HexToBytes();
                    }
                }

                _areaContexts[area.Id] = ctx;
            }

            // Build reverse lookup: (partitionId, contentIndex) -> canonical FileSystem area
            foreach (WiiUAreaContext ctx in _areaContexts.Values.Where(c => c.IsFileSystem))
                _canonicalAreaByContent[(ctx.PartitionId, ctx.ContentIndex)] = ctx;
        }

        protected override long TranslateImageOffset(long imageOffset, long size)
        {
            // Find the area containing this section-aligned offset
            WiiUAreaContext ctx = _areaContexts.Values.FirstOrDefault(c => imageOffset >= c.Area.Offset && imageOffset < c.Area.Offset + c.Area.Size);
            if (ctx == null || !ctx.IsOther)
                return imageOffset;

            // Look up the canonical FileSystem area for this repeated content.
            // If there is no canonical FileSystem area to fold back to, this Other area holds
            // unique data that must be read from its own stored bytes, so leave the offset as-is.
            if (!_canonicalAreaByContent.TryGetValue((ctx.PartitionId, ctx.ContentIndex), out WiiUAreaContext canonical))
                return imageOffset;

            // A RepeatedApp Other area repeats the canonical FileSystem content. All of its
            // sections must be reconstructed by folding back to the canonical FileSystem area so
            // they share the canonical area's decryption/offset context. Sections whose decrypted
            // content is unique (the first occurrence and the truncated tail) are also persisted to
            // the DataStore, but reading them back via the stored path re-derives them at the Other
            // area's own offset context and yields the wrong plaintext. Folding every section to the
            // canonical area (as the non-persisted repeats already do) reconstructs them correctly.
            //
            // For unencrypted canonical areas, the Other area's stored bytes (if any) may differ
            // from the canonical content — use stored data when it exists, fold only when nothing
            // was stored (CRC-dedup skipped an identical section).
            if (!ctx.IsEncrypted)
            {
                // Unencrypted ContentRepeat: prefer stored data when it exists; fold only when
                // nothing was stored (CRC-dedup skipped an identical section).
                try
                {
                    if (_imageReader is ImageReader concreteReader && size > 0)
                    {
                        long sectionStart = ctx.Area.Offset + ((imageOffset - ctx.Area.Offset) / size * size);
                        long sectionSize = Math.Min(size, ctx.Area.Offset + ctx.Area.Size - sectionStart);
                        IEnumerable<OffsetRecord> offs = concreteReader.GetOffsetsInRange(sectionStart, sectionSize);
                        if (offs != null && offs.Any())
                            return imageOffset; // stored data exists; use it (no crypto context needed)
                    }
                }
                catch { }
                // No stored data — fall through to fold from canonical
            }

            // Encrypted ContentRepeat (or unencrypted with no stored data): fold to canonical.
            long offsetWithinOther = imageOffset - ctx.Area.Offset;
            long wrappedOffset = offsetWithinOther % canonical.Area.Size;
            return canonical.Area.Offset + wrappedOffset;
        }

        protected override void OnAreaChanged(AreaRecord newArea, AreaRecord oldArea) => base.OnAreaChanged(newArea, oldArea);

        protected override void OnGapFill(long offsetInGap, long cleanAreaOffset, long cleanBufferOffset, long size, BufferContext context)
        {
            // Pre-partition Other gap: fill from cached ImageHeader + PartitionTable data
            if (_headerPtData != null && _areaContexts.TryGetValue(context.Area.Id, out WiiUAreaContext areaCtx) && areaCtx.IsOther
                && !_canonicalAreaByContent.ContainsKey((areaCtx.PartitionId, areaCtx.ContentIndex)))
            {
                int repeatLen = _headerPtData.Length;
                long srcOffset = cleanAreaOffset % repeatLen;
                int remaining = (int)size;
                int destPos = (int)cleanBufferOffset;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(remaining, repeatLen - srcOffset);
                    Array.Copy(_headerPtData, (int)srcOffset, context.Buffer, destPos, chunk);
                    remaining -= chunk;
                    destPos += chunk;
                    srcOffset = 0;
                }
                return;
            }
            base.OnGapFill(offsetInGap, cleanAreaOffset, cleanBufferOffset, size, context);
        }

        protected override void OnBufferPopulatedWithSection(int validSize, BufferContext context)
        {
            // Basic implementation that restores hash chunks from BlockPadding segments
            context.Items[WiiUConsts.ContextItemCreatable] = "1";
            context.Items[WiiUConsts.ContextItemState] = null;

            WiiUAreaContext areaContext = _areaContexts[context.Area.Id];
            if (!areaContext.IsFileSystem && !areaContext.IsOther)
            {
                // Non-content areas (FstBlock, PartitionHeader, PartitionTable, ImageHeader)
                // use continuous flat CBC encryption.
                if (_encrypt && areaContext.IsEncrypted && ((_header?.Key != null && _header.Key.Length > 0) || areaContext.SiData?.KeyTitle != null))
                    encryptHashless(validSize, context, areaContext);
                return;
            }

            // Other (repeater) areas that are encrypted go through WiiUSecurity like
            // FileSystem areas — they represent content data that needs per-sector
            // encryption.  Skip hash restoration below (no BlockPadding for Other).
            if (areaContext.IsOther)
            {
                if (_encrypt && areaContext.IsEncrypted && ((_header?.Key != null && _header.Key.Length > 0) || areaContext.SiData?.KeyTitle != null))
                    this.encrypt(validSize, context, areaContext);
                return;
            }

            OffsetSegment seg = context.Section?.Segments?.FirstOrDefault(s => s.Source.Type == BlockType.BlockPadding);
            if (seg != null)
            {
                OffsetRecord src = seg.Source;
                long segImageStart = src.Offset + seg.SourceOffset;
                long bufImageEnd = context.ImageOffset + validSize;

                int segSize = (int)seg.Size;
                if (segSize <= 0)
                    return;

                int blkSize = _imageReader.Info.BlockSize;
                int blocksNeeded = (segSize + blkSize - 1) / blkSize;

                BlockRecord firstBlock = _imageReader.GetBlock(src.GetBlockAt(0));
                if (firstBlock == null || firstBlock.Data == null || firstBlock.Data.Length == 0)
                    return;

                if (firstBlock.Data[0] > 0)
                {
                    context.Items[WiiUConsts.ContextItemCreatable] = firstBlock.Data.Length > firstBlock.Data[0] ? "0" : "1";
                    context.Items[WiiUConsts.ContextItemState] = firstBlock.Data.Read(1, firstBlock.Data[0]);
                }

                BlockRecord[] cachedBlocks = new BlockRecord[blocksNeeded];
                cachedBlocks[0] = firstBlock;

                int segBuffSize = 0;
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

                int chunkSize = WiiUConsts.HashSize;
                int chunks = payloadLen / chunkSize;

                long relBufStart = context.ImageOffset - segImageStart;
                long relBufEnd = bufImageEnd - segImageStart;

                int strideBlockSize = context.Stride.SourceBlockSize;
                long firstCi = relBufStart <= 0 ? 0 : Math.Max(0, (relBufStart + strideBlockSize - 1) / strideBlockSize);
                long lastCi = Math.Min(chunks - 1, (relBufEnd - 1) / strideBlockSize);

                for (long ci = firstCi; ci <= lastCi; ci++)
                {
                    int payloadOffset = headerSkip + (int)(ci * chunkSize);
                    int destPos = (int)(segImageStart + (ci * (long)strideBlockSize) - context.ImageOffset);
                    if (payloadOffset < 0 || payloadOffset + chunkSize > segSize || destPos < 0 || destPos + chunkSize > context.Buffer.Length)
                        continue;

                    int remaining = chunkSize;
                    int srcOffset = payloadOffset;
                    int outPos = destPos;

                    copyFromCachedBlocks(src, cachedBlocks, segSize, blkSize, srcOffset, context.Buffer, outPos, remaining);
                }
            }

            // Apply filesystem encryption only when a key is available. This restores the
            // previous behavior (decrypted output when no key), but will re-apply
            // WiiU security when the title/header key is present.
            if (_encrypt && areaContext.IsEncrypted && ((_header?.Key != null && _header.Key.Length > 0) || areaContext.SiData?.KeyTitle != null))
                this.encrypt(validSize, context, areaContext);
        }

        private void encryptHashless(int validSize, BufferContext context, WiiUAreaContext areaContext)
        {
            // If this area is encrypted but NOT a FileSystem area, apply hashless
            // encryption immediately and return. These areas (PartitionHeader, FstBlock,
            // etc.) do not contain the BlockPadding segments used for filesystem
            // hash reconstruction and therefore must be handled separately.
            try
            {
                byte[] key = _header.Key;
                // Prefer explicit handling per-area type similar to Image.Read logic:
                // - PartitionTable should use the image header key
                // - FstBlock uses the title key for Game partitions, otherwise the header key
                // - PartitionHeader and others default to the image header key
                string fsType = areaContext.Area.Metadata.GetString(AreaValueType.FsType);
                AreaType type = fsType == WiiUConsts.FsTypePartitionHeader ? AreaType.PartitionHeader : AreaType.Other;

                byte[] iv = new byte[16];
                bool isFlatContainer = _imageReader.Image.Format != ImageFormat.Iso && _imageReader.Image.Format != ImageFormat.Bin;
                key = WiiUSecurityContext.GetActiveEncryptionKey(areaContext.PartitionType, type, isFlatContainer, _header.Key, areaContext.SiData?.KeyTitle);

                // Non-content hashless areas (ImageHeader, PartitionTable, PartitionHeader, FstBlock)
                // are read back by the WiiU Image reader with a ZERO IV (DecryptPartitionTable /
                // DecryptFst both pass new byte[0x10]). The reconstruction MUST re-encrypt them with
                // the same zero IV. Using a content-index IV here (iv[1] = ContentIndex) corrupts the
                // Game partition's FST so it fails to decrypt on read-back. The content-index IV only
                // applies to actual content data, which is handled by encrypt(), not this method.

                if (key == null)
                    key = (byte[])WiiUConsts.KeyCommon.Clone();

                WiiUSecurity.EncryptHashless(context.Buffer, context.Buffer, 0, validSize, key, iv);
            }
            catch { }
        }

        private void encrypt(int validSize, BufferContext context, WiiUAreaContext areaContext)
        {
            // If this area requires encryption, attempt to re-apply it.
            // Handles both FileSystem and encrypted Other (repeater) areas.
            if (!areaContext.IsEncrypted || (!areaContext.IsFileSystem && !areaContext.IsOther))
                return;

            ContentHeader cnt = areaContext.ContentHeader;
            SiData si = areaContext.SiData;

            // Track whether the ContentHeader was available from FST parsing.
            // When absent (CDN/APP mode without partition headers) hashless content
            // must use flat CBC encryption with a content-index IV, matching the
            // WiiUSecurityHashless decryption used during the original scan.
            bool isSynthetic = cnt == null;

            // When ContentHeader or SiData are unavailable (APP/FST format, which has
            // no partition headers) build synthetic objects with the properties that
            // WiiUSecurity needs.  This mirrors SectionProcessor.Process which always
            // passes a real cntHeader and siData to WiiUSecurity.Populate.
            if (cnt == null)
            {
                byte[] key = _header?.Key ?? (byte[])WiiUConsts.KeyCommon.Clone();
                if (key == null || key.Length == 0)
                    return;

                cnt = WiiUSecurityContext.CreateSyntheticForApp(areaContext.ContentIndex, context.Area.Size, areaContext.HasHashes, _header);
            }
            else
            {
                // Force sync anyway in case it was wrong from FST parsing
                cnt.HasHashes = areaContext.HasHashes;
            }

            if (si == null)
            {
                byte[] key = _header?.Key ?? (byte[])WiiUConsts.KeyCommon.Clone();
                if (key == null || key.Length == 0)
                    return;

                si = new SiData { KeyTitle = key };
            }
            else if (si.KeyTitle == null || si.KeyTitle.Length == 0)
            {
                si.KeyTitle = _header?.Key ?? Key;
            }

            // Hashless content that was originally encrypted with flat CBC mode:
            //  - SeekIvs present: scan captured CBC state for Game partition hashless FS
            //    (disc/ISO mode or multi-section CDN content).
            //  - Synthetic ContentHeader: CDN/APP mode where all content is treated as
            //    Game partition during scanning and uses WiiUSecurityHashless with
            //    iv[1] = contentIndex.  Single-section content may not have SeekIvs
            //    (CreateSeekIv returns null when the computed IV matches the initial IV)
            //    so we must also check the synthetic flag.
            // Non-Game hashless content on WUD discs (SI, Update) uses per-sector
            // encryption with offset-based IVs — those areas have real ContentHeaders
            // (isSynthetic=false) and no SeekIvs, so they fall through to the
            // WiiUSecurity path below.
            bool isFlat = _imageReader.Image.Format != ImageFormat.Iso && _imageReader.Image.Format != ImageFormat.Bin;

            if (!cnt.HasHashes && (isSynthetic || areaContext.SeekIvs != null))
            {
                // Prefer the partition/title key when available (Game partitions use title key),
                // fall back to the image header key otherwise.
                byte[] cbcKey = WiiUSecurityContext.GetActiveEncryptionKey(areaContext.PartitionType, isFlat, _header?.Key, si?.KeyTitle);
                if (cbcKey == null || cbcKey.Length == 0)
                    return;

                byte[] cbcIv;
                if (context.AreaOffset != 0 && areaContext.SeekIvs != null)
                {
                    int sectionIdx = (int)(context.AreaOffset / context.Area.SectionSize);
                    cbcIv = (sectionIdx >= 0 && sectionIdx < areaContext.SeekIvs.Length && areaContext.SeekIvs[sectionIdx] != null)
                        ? areaContext.SeekIvs[sectionIdx]
                        : new byte[16];
                }
                else
                {
                    cbcIv = new byte[16];
                    cbcIv[1] = (byte)cnt.Index;
                }

                WiiUSecurity.EncryptHashless(context.Buffer, context.Buffer, 0, validSize, cbcKey, cbcIv);
                return;
            }

            // Hashed content or WUD non-Game hashless: per-sector encryption
            PartitionType ptype = isFlat ? PartitionType.Game : areaContext.PartitionType;
            AreaType areaType = areaContext.IsFileSystem ? AreaType.FileSystem : AreaType.Other;

            // Use the full buffer length (not just validSize) to match how
            // SectionProcessor passes base.Buffer.Size to WiiUSecurity.Populate.
            // This ensures _usedSectors covers the populated region correctly.
            long remainingInArea = context.Area.Size - context.AreaOffset;
            int populateSize = (int)Math.Min((long)context.Buffer.Length, remainingInArea);

            WiiUSecurity sec = new WiiUSecurity(_header);
            sec.Populate(cnt, si, ptype, areaType, context.Buffer, context.Buffer, populateSize, false, false, context.AreaOffset, null);
            sec.Encrypt();
        }

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
                    break;

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