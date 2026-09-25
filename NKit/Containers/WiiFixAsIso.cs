using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WiiFileSystemInfo = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Wii Fix decorator over a source IAsIso.
    ///
    /// During a Fix task a Wii source may be compacted (partitions packed early, e.g. WBFS with the
    /// Game partition at a low offset) and/or missing its update partition or channel/VC partitions.
    /// The standard disc layout has the Game (data) partition at 0xF800000 with any channels/VC
    /// placed around it by a fixed algorithm. In Fix mode we know the disc must be corrected, so
    /// WiiFixAsIso lays out the correct disc up front (mirroring GcFixAsIso / the NKitAsIso model):
    ///
    ///   - Rebuild the partition table: Game @ 0xF800000, remaining partitions slid to follow.
    ///   - MOVE each existing partition's data to its corrected offset (served from the source
    ///     offset — a partition-granularity remap).
    ///   - Leave the Update partition region BLANK in place at 0x50000; the downstream Fix step
    ///     brute-forces / fills it if the CRC match fails (unchanged behaviour).
    ///   - (Follow-up) insert missing channels/VC from recovery files and apply the ID-based fixes.
    ///   - Pad with zeros to the full disc size.
    ///
    /// Unlike GcFixAsIso this is NOT a pass-through: the SectionProcessor still unscrubs / rebuilds
    /// hashes / encrypts each existing partition's CONTENT in place at its new offset. WiiFixAsIso
    /// only owns the LAYOUT (boundaries + partition placement + size); it does not touch the
    /// encrypted/hashed partition bytes it serves.
    ///
    /// Falls back to transparent pass-through of the inner source if the layout cannot be built.
    /// </summary>
    internal sealed class WiiFixAsIso : Stream, IAsIso
    {
        private enum WiiSegType { Src, Header, Zero, File }

        private struct WiiSeg
        {
            public long Offset;     // output offset where this segment starts
            public long Length;     // bytes
            public WiiSegType Type;
            public long SrcOffset;  // for Src: offset in the inner source
            public long HdrOffset;  // for Header: offset into the corrected disc header bytes
            public string FilePath; // for File: recovery file (or archive) path to serve (inserted channel/VC)
            public string InnerEntryName; // for File: archive inner entry name (null = plain file)
            public long IsoEnd => Offset + Length;
        }

        private readonly IAsIso _inner;
        private readonly Stream _innerStream;
        private long _position;
        private long _size;             // corrected output size or inner size when pass-through
        private bool _passThrough;

        /// <summary>The wrapped source container. Lets a consumer reach the real underlying
        /// container (e.g. RvzAsIso) for container-specific probes when this decorator is in the
        /// chain — otherwise a `_iso as RvzAsIso` cast would see the decorator and return null.</summary>
        internal IAsIso Inner => _inner;

        private List<WiiSeg> _segments;
        private byte[] _correctedHeader; // corrected 0x50000 disc header (partition table rebuilt)

        // Output offset ranges of moved existing partitions, so the downstream reader can mark
        // them appropriately if needed (not currently required — SectionProcessor reads the
        // corrected table and processes each partition normally at its new offset).

        // Injected before the first read (from NKitInput.Open, before the Image is constructed).
        private FixData _fixData;
        private long _fixSize;      // caller's pre-parse size hint (SetFixData shares its shape with
                                    // GcFixAsIso); NOT used for Wii sizing — _discSize is derived
                                    // from the corrected partition layout in buildLayout instead.
        private long _discSize;     // corrected disc size derived from the actual partition layout
        private ILogScope _log;
        private bool _prepared;
        private bool _fixRequested;

        // The corrected layout (partition-table reflow) is built during Image construction — the
        // header read happens before the task prints its params banner / starts the progress bar,
        // so logging the fixes inline there makes them appear ABOVE the banner. Buffer them and
        // flush once real processing starts (WiiGc.Image.Read calls FlushLog), landing them inside
        // the progress area. Captured via a throwaway Log whose sink appends here.
        private readonly List<(string message, LogLevel level)> _pendingLog = new List<(string, LogLevel)>();
        private bool _logFlushed;

        // A capturing Log passed into the WiiPartitionLayout.Apply* helpers (which log via a Log,
        // and are also used by the non-decorator path with a live log — so we must NOT change their
        // API). Messages land in _pendingLog and are replayed by FlushLog through the real _log.
        private Log buildCaptureLog()
        {
            Log capture = new Log();
            capture.Initialise(LogLevel.Detail, LogLevel.None,
                (msg, lvl) => _pendingLog.Add((msg, lvl)), null);
            return capture;
        }

        // Emit the buffered fix messages through the real log. Called by the Image on its first
        // processing read so the lines appear after the params banner. Idempotent.
        internal void FlushLog()
        {
            if (_logFlushed)
                return;
            _logFlushed = true;
            if (_log == null)
                return;
            foreach ((string message, LogLevel level) in _pendingLog)
            {
                // The capture sink received the already-formatted line (trailing newline + any
                // debug prefix). Re-emitting through Info/Detail would re-append a newline, so strip
                // it and let the real log format once.
                string m = message?.TrimEnd('\r', '\n') ?? "";
                switch (level)
                {
                    case LogLevel.Detail: _log.Detail(() => m); break;
                    case LogLevel.Error: _log.Error(() => m); break;
                    default: _log.Info(() => m); break;
                }
            }
            _pendingLog.Clear();
        }

        internal WiiFixAsIso(IAsIso inner)
        {
            _inner = inner;
            _innerStream = (Stream)inner;
            _size = inner.Size;
            _position = 0;
        }

        // Decide whether a Wii source should be wrapped for the Fix task and, if so, build the
        // decorator. Follows the same `Create` convention as the other AsIso containers so the
        // wrap decision lives with the decorator, not the caller. Returns null when it should not
        // wrap (not Wii, not a Fix step, source already NKit-decoded or already this decorator).
        // Wraps ONLY on the actual fix step: the verify step (StepType == Verify) re-reads the
        // already-corrected output and must pass through un-wrapped.
        //
        // The size passed here is only a nominal — WiiFixAsIso derives the exact single-/dual-layer
        // size from the corrected partition layout in buildLayout. (The shared SetFixData shape is
        // meaningful for GcFixAsIso, where the size is the fixed FullSizeGameCube.)
        public static IAsIso Create(IAsIso iso, SystemType detectedSystem, IImageContext context)
        {
            // A decoded NKitAsIso source is NOT wrapped (mirrors GcFixAsIso): NKitAsIso already
            // returns the correct decoded disc — partition table reflowed, offsets repacked, the
            // update partition reinserted from recovery (or null-filled when the recovery file is
            // missing). The decoded NKitAsIso stays the outer IAsIso and the proven in-pipeline
            // reflow (WiiGc.Image.applyWiiPartitionTableFixes / applyDataPartitionFixes) does the
            // layout, with the Fix step brute-forcing the (null-filled) update partition. This is
            // the single-step decoded-NKit Wii Fix (no separate Expand) — the same shape GameCube
            // already uses. Wrapping here as well would DOUBLE-CORRECT the layout (both this
            // decorator and the in-pipeline reflow authoring partition offsets), shifting per-area
            // CRCs and producing a VerifyFailed.
            if (detectedSystem != SystemType.Wii || context.TaskType != TaskType.Fix
                || context.StepInfo?.StepType == TaskType.Verify
                || iso is NKitAsIso || iso is WiiFixAsIso)
                return null;

            WiiFixAsIso dec = new WiiFixAsIso(iso);
            dec.SetFixData(GcFixAsIso.ResolveFixData(context), WiiConsts.FullSizeWii5, context.Log);
            return dec;
        }

        internal void SetFixData(FixData fixData, long fixSize, ILogScope log)
        {
            _fixData = fixData;
            _fixSize = fixSize;
            _log = log;
            _fixRequested = true;
        }

        internal bool IsCorrected => _prepared && !_passThrough;

        public int Construct(Stream stream, bool allowSeek)
        {
            int r = _inner.Construct(stream, allowSeek);
            _size = _inner.Size;
            _position = 0;
            return r;
        }

        // ── IAsIso pass-through metadata ────────────────────────────────────────
        public ContainerType Format => _inner.Format;
        public bool Seekable => _inner.Seekable;
        public bool SeekRequired => _inner.SeekRequired;
        public long RealPosition => _inner.RealPosition;
        public long RealSize => _inner.RealSize;
        // Report estimated size so the Wii Image sizes the output to the full disc size rather than
        // the smaller compacted source size.
        public bool SizeEstimated => true;
        public Checksums Checksums => _inner.Checksums;
        public Checksums CustomChecksums() => _inner.CustomChecksums();
        public NKitHeader NKitHeader => _inner.NKitHeader;
        public void SetRemovedBlock(Action<MetaData> setBlock) => _inner.SetRemovedBlock(setBlock);
        public void Complete() => _inner.Complete();

        // Reading Size builds the layout so the corrected disc size (single- vs dual-layer,
        // derived from the actual partition table) is known before the WiiGc Image constructor
        // reads _iso.Size to compute its own FixSize.
        public long Size
        {
            get
            {
                if (!_prepared)
                    prepare();
                return _size;
            }
        }

        // ── Layout build (once, on first read) ──────────────────────────────────
        private void prepare()
        {
            if (_prepared)
                return;
            _prepared = true;

            try
            {
                buildLayout();
            }
            catch
            {
                _passThrough = true;
                _segments = null;
            }

            if (_passThrough)
                _size = _inner.Size;
        }

        private void buildLayout()
        {
            if (!_fixRequested || _fixData == null)
            {
                _passThrough = true;
                return;
            }

            // 1. Read the source disc header (0x50000) and parse the partition table.
            byte[] hdr = new byte[WiiConsts.WiiDiscHdrSize];
            _innerStream.Position = 0;
            readExact(_innerStream, hdr, 0, hdr.Length);
            ImageHeader header = new ImageHeader(hdr, true);

            List<PartitionInfo> existing = header.Partitions.ToList();
            if (existing.Count == 0)
            {
                _passThrough = true;
                return;
            }

            // Target the fix data to THIS disc so WiiChannels (recovery files, needs FixPath) and
            // ChannelCount (redumpChannels[id8] from the fix yaml) are populated. The decorator
            // resolves FixData at NKitInput.Open — before the disc Id8 is known — so it must be set
            // up here with the header Id8 now that we have parsed the header.
            _fixData.Setup(header.Id8);

            // 2. Read ONLY the Game (data) partition header from the source, to get its on-disc
            //    span. We never read channel/VC regions from the source: channels/VC are ALWAYS
            //    replaced from the recovery (fix) files, so there is no need to touch the source
            //    near its (possibly compacted/truncated) end. Header-block = field 0x2b8 x4;
            //    data = field 0x2bc x4.
            PartitionInfo game = existing.FirstOrDefault(a => a.Type == PartitionType.Game);
            if (game == null)
            {
                _passThrough = true;
                return;
            }

            // Evaluate every pass-through decision that can be made from the disc HEADER (offset 0)
            // BEFORE touching the Game partition header far forward in the source. The inner source
            // is forward-only (e.g. a disc inside a zip/7z): once we seek it to the Game partition
            // header (~0xF800000) its released floor advances past 0, so a pass-through that then
            // re-reads the image from offset 0 would fail ("Cannot seek to 0; ... released"). By
            // deciding the header-only pass-through cases first we never advance the source for a
            // disc we are not going to correct. Only the segment-build path (which serves offset 0
            // from the corrected header, never re-reading the source head) reads the Game header.

            // Do we have recovery channels/VC for this disc?
            System.Collections.Generic.List<FixPartition> recovery =
                _fixData.WiiChannels?.Where(a => a.Id == header.Id8).ToList() ?? new System.Collections.Generic.List<FixPartition>();

            int reqChannels = _fixData.ChannelCount;
            int haveChannels = existing.Count(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game);

            // Nothing to do only when the disc is already fully correct: Game at 0xF800000, all
            // required channels present, already full size, and no partition-table quirk still
            // pending. This is the common no-op / verify-pass case (the verify pass re-reads the
            // corrected output, which must pass straight through). Any other state is corrected.
            //
            // The RSB/WBM quirk is that channel/VC partitions sit in table 0; once they are in
            // table 1 (which our re-add produces, and which a genuinely-correct disc already has)
            // there is nothing left to fix. Detecting the residual table-0 condition — rather than
            // the disc ID alone — lets an already-correct RSB output pass through on verify while a
            // still-broken WBM source is routed into the fix path.
            bool wbmTablePending = header.Data.ReadString(0, 4).StartsWith("RSB") &&
                header.Partitions.Any(a =>
                    a.Type != PartitionType.Update && a.Type != PartitionType.Game && a.Table == 0);
            bool channelsComplete = haveChannels >= reqChannels;
            bool gameInPlace = game.ImageOffset == WiiConsts.WiiDefaultDataPtnOffset;
            // Already a standard full-disc size (single- or dual-layer)? The verify pass re-reads a
            // corrected output that is exactly FullSizeWii5/FullSizeWii9, so compare against those
            // rather than the caller's pre-parse size guess.
            bool fullSize = _inner.Size == WiiConsts.FullSizeWii5 || _inner.Size == WiiConsts.FullSizeWii9;
            if (channelsComplete && gameInPlace && fullSize && !wbmTablePending)
            {
                _passThrough = true;
                return;
            }

            // If channels/VC are missing and we have NO recovery files to supply them, we cannot
            // lay them out from a compacted/truncated source safely (we always replace, not copy).
            // Pass through to the existing pipeline for that case.
            if (haveChannels < reqChannels && recovery.Count == 0)
            {
                _passThrough = true;
                return;
            }

            // User rule: do NOT edit the update partition or channels when their fix (recovery)
            // versions do not exist — the image may already be fine as-is (e.g. a scrubbed but
            // otherwise-complete WBFS that has its own valid update + channels). WiiFixAsIso always
            // REPLACES channels/update from recovery (it never copies them from the source), so if
            // there is NO recovery available at all we must not drop them. When the disc already
            // has its required channels, there are no recovery channel files for it, AND no
            // recovery update-partition files exist to replace the update, there is nothing for
            // WiiFixAsIso to contribute: pass through to the legacy pipeline, which preserves the
            // existing partitions and performs the unscrub/reflow without discarding the update.
            // Discs WITH recovery (e.g. the Wii BackUp Disc, which supplies its update from a
            // recovery file and needs the ID swap / update replace) still take the fix path below.
            bool haveRecoveryUpdate = _fixData.WiiUpdatePartitions?.Any(a => a.Filename != null) ?? false;
            if (recovery.Count == 0 && channelsComplete && !haveRecoveryUpdate)
            {
                _passThrough = true;
                return;
            }

            // Committed to building the corrected layout. Only now read the Game partition header
            // far forward in the source — the segment path serves offset 0 from _correctedHeader
            // and never re-reads the source head, so advancing the forward-only source here is
            // safe. (All pass-through exits above are header-only and leave the source at 0.)
            // Header-block = field 0x2b8 x4; data = field 0x2bc x4.
            byte[] gph = new byte[0x400];
            _innerStream.Position = game.ImageOffset;
            readExact(_innerStream, gph, 0, gph.Length);
            uint gameHdrBlocks = gph.ReadUInt32B(WiiConsts.WiiPrtHdrSizeOffset);   // header block / 4
            uint gameDataBlocks = gph.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset); // hashed data / 4
            long gameSpan = (gameHdrBlocks * 4L) + (gameDataBlocks * 4L);
            long gameSrcOffset = game.ImageOffset;

            // Disc-type detection. Fix does not support RVT (non-retail) images — the existing
            // pipeline rejects them explicitly (WiiGc.Image: "RVT ... not currently supported for
            // Fix"). RVT-R uses the RVT issuer; RVT-H additionally has no hashed partition data
            // (H3/partition-size zero). Detect from the Game partition header and pass through so
            // the downstream reader raises the proper unsupported error rather than us producing a
            // wrong-sized retail image.
            bool isRvt = WiiFileSystemInfo.GetIssuer(gph) == WiiConsts.RvtIssuer
                      || WiiFileSystemInfo.GetIssuer(gph) == WiiConsts.RvtIssuer.Rot13Words();
            if (isRvt)
            {
                _passThrough = true;
                return;
            }

            // 3. Apply ALL deterministic Wii layout/header fixes here (owned by WiiFixAsIso):
            //    a) the disc-ID swap quirk (010E->4 for a RELS game);
            //    b) ALWAYS replace channels/VC — drop existing non-update/non-game partitions and
            //       re-add from recovery (source is never read for those regions). Re-adding VC as
            //       table 1 also subsumes the RSB/WBM "table 0->1" fix, so no separate table move
            //       is needed;
            //    c) move Game to 0xF800000 + add channel/VC placeholders (shared table fixes);
            //    d) the offset walk so every partition lands at its correct offset.
            //    Only the update partition is left blank (brute-forced downstream at the end).
            //
            //    NOTE: the disc-ID swap uses the Game partition's content ID (RELS...) which the
            //    decorator does not resolve here (needs decrypted data), so it is a no-op for now;
            //    game.Id is null. The RSB/WBM case is handled by the channel re-add (table 1).
            Log fixLog = buildCaptureLog(); // buffer layout-fix messages; flushed after the banner
            WiiPartitionLayout.ApplyIdSwap(header, game.Id, fixLog);
            header.RemovePartitionChannels();
            // The update partition region is left BLANK in place (zero-filled) and brute-forced
            // downstream at the end. So it must NOT remain advertised in the corrected partition
            // table: if the WiiGc Image walks the table and finds an Update entry pointing at the
            // blanked 0x50000 region, it reads a zeroed partition header, derives a bogus span and
            // overruns the source reader. Discs that already lack an update entry (e.g. the
            // truncated SSBB) fix correctly, so removing it here makes every disc take that same
            // known-good shape. The downstream Fix step re-inserts the update partition.
            header.RemoveUpdatePartition(0);
            WiiPartitionLayout.ApplyPartitionTableFixes(header, _fixData, _inner.Size, hdr, fixLog);
            WiiPartitionLayout.ApplyDataPartitionFixes(header, WiiConsts.WiiDefaultDataPtnOffset, gameSpan, fixLog);
            header.UpdateRepair();
            _correctedHeader = header.Data;

            // Record the Game partition's source location + span for the segment builder.
            Dictionary<PartitionInfo, (long srcOffset, long span)> srcSpan = new Dictionary<PartitionInfo, (long, long)>();
            PartitionInfo gameNow = header.Partitions.FirstOrDefault(a => a.Type == PartitionType.Game);
            if (gameNow != null)
                srcSpan[gameNow] = (gameSrcOffset, gameSpan);

            // Determine the corrected disc size from the actual (corrected) partition layout rather
            // than a guess from the compacted source size. Compute the end offset of the furthest
            // non-update partition (Game span is known; each channel/VC span comes from its
            // recovery FixPartition), then pick the standard retail size that contains it using the
            // SAME rule as WiiGc.Image.lenCalc (the pre-refactor size authority):
            //   content that fits within the oversized-DVD5 window (<= FullSizeWiiOversized) is a
            //   single-layer disc (FullSizeWii5); anything larger is dual-layer (FullSizeWii9).
            // Using FullSizeWiiOversized (not FullSizeWii5) as the threshold matches lenCalc's
            // snap-down: a disc whose partitions end just past the exact DVD5 size is still a
            // single-layer disc and must not be over-padded to ~8.5GB. This makes a compacted 8GB
            // disc (whose source may be < FullSizeWii5) expand to the correct dual-layer size while
            // a slightly-oversized single-layer disc stays single-layer.
            long contentEnd = WiiConsts.WiiDiscHdrSize;
            foreach (PartitionInfo p in header.Partitions.Where(a => a.Type != PartitionType.Update))
            {
                long span = srcSpan.TryGetValue(p, out (long srcOffset, long span) s)
                    ? s.span
                    : (p.FixPartition?.Length ?? 0);
                contentEnd = Math.Max(contentEnd, p.ImageOffset + span);
            }
            _discSize = contentEnd > WiiConsts.FullSizeWiiOversized ? WiiConsts.FullSizeWii9 : WiiConsts.FullSizeWii5;

            // 4. Build the output segment map from the corrected partition table.
            buildSegments(header, srcSpan);

            _size = _discSize;
        }

        private void buildSegments(ImageHeader header, Dictionary<PartitionInfo, (long srcOffset, long span)> srcSpan)
        {
            _segments = new List<WiiSeg>();

            // Corrected disc header at offset 0 (covers 0..0x50000).
            _segments.Add(new WiiSeg { Offset = 0, Length = WiiConsts.WiiDiscHdrSize, Type = WiiSegType.Header, HdrOffset = 0 });
            long dst = WiiConsts.WiiDiscHdrSize; // 0x50000

            // Every non-update partition in its corrected order/offset. Existing partitions are
            // served from their source span (a move); inserted channel/VC placeholders are served
            // from their recovery file. Gaps (and the blank update region) are zero-filled — the
            // downstream fills/brute-forces the update region. Reads past the (compacted) source
            // EOF zero-fill safely, which is what makes a full-size image from a compacted source.
            foreach (PartitionInfo p in header.Partitions
                         .Where(a => a.Type != PartitionType.Update)
                         .OrderBy(a => a.ImageOffset))
            {
                if (p.ImageOffset > dst)
                {
                    _segments.Add(new WiiSeg { Offset = dst, Length = p.ImageOffset - dst, Type = WiiSegType.Zero });
                    dst = p.ImageOffset;
                }

                long span;
                if (srcSpan.TryGetValue(p, out (long srcOffset, long span) s))
                {
                    // Game partition — move its bytes from the source offset. If the source is
                    // truncated (compacted WBFS shorter than the partition span), serve only what
                    // the source actually contains and PAD the remainder with zeros — never read
                    // past the source's real content (which would overrun the block reader).
                    span = s.span;
                    long avail = Math.Max(0, _inner.Size - s.srcOffset);
                    long fromSrc = Math.Min(span, avail);
                    if (fromSrc > 0)
                        _segments.Add(new WiiSeg { Offset = p.ImageOffset, Length = fromSrc, Type = WiiSegType.Src, SrcOffset = s.srcOffset });
                    if (span > fromSrc)
                        _segments.Add(new WiiSeg { Offset = p.ImageOffset + fromSrc, Length = span - fromSrc, Type = WiiSegType.Zero });
                }
                else if (p.IsPlaceholder && p.FixPartition?.Filename != null && System.IO.File.Exists(p.FixPartition.Filename))
                {
                    // inserted channel/VC — serve raw bytes from the recovery file. Filename is the
                    // on-disk path (a plain data file, or a streamable archive when InnerEntryName
                    // is set — the single entry named as the archive without its extension).
                    span = p.FixPartition.Length;
                    _segments.Add(new WiiSeg { Offset = p.ImageOffset, Length = span, Type = WiiSegType.File, FilePath = p.FixPartition.Filename, InnerEntryName = p.FixPartition.InnerEntryName });
                }
                else
                {
                    // best-effort: a partition we cannot supply (missing recovery file). Keep the
                    // table entry correct but leave the region blank; carry on.
                    span = p.FixPartition?.Length ?? 0;
                    if (span > 0)
                        _segments.Add(new WiiSeg { Offset = p.ImageOffset, Length = span, Type = WiiSegType.Zero });
                }
                dst = p.ImageOffset + span;
            }

            // Trailing fill to the corrected full disc size (single- or dual-layer).
            if (dst < _discSize)
                _segments.Add(new WiiSeg { Offset = dst, Length = _discSize - dst, Type = WiiSegType.Zero });
        }

        // ── Stream / reads ──────────────────────────────────────────────────────
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_prepared)
                prepare();

            if (_passThrough)
            {
                _innerStream.Position = _position;
                int n = _innerStream.Read(buffer, offset, count);
                _position += n;
                return n;
            }

            long available = _size - _position;
            if (available <= 0)
                return 0;

            int toRead = (int)Math.Min(count, available);
            int copied = 0;

            while (copied < toRead)
            {
                WiiSeg seg = findSegment(_position);
                long segRemaining = seg.IsoEnd - _position;
                int chunk = (int)Math.Min(toRead - copied, segRemaining);
                long offsetInSeg = _position - seg.Offset;

                switch (seg.Type)
                {
                    case WiiSegType.Src:
                        _innerStream.Position = seg.SrcOffset + offsetInSeg;
                        readSrcSafe(_innerStream, buffer, offset + copied, chunk);
                        break;
                    case WiiSegType.Header:
                        Array.Copy(_correctedHeader, seg.HdrOffset + offsetInSeg, buffer, offset + copied, chunk);
                        break;
                    case WiiSegType.Zero:
                        Array.Clear(buffer, offset + copied, chunk);
                        break;
                    case WiiSegType.File:
                        readFromRecoveryFile(seg.FilePath, seg.InnerEntryName, offsetInSeg, buffer, offset + copied, chunk);
                        break;
                }

                copied += chunk;
                _position += chunk;
            }

            return copied;
        }

        private WiiSeg findSegment(long pos)
        {
            int lo = 0, hi = _segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                WiiSeg s = _segments[mid];
                if (pos < s.Offset)
                    hi = mid - 1;
                else if (pos >= s.IsoEnd)
                    lo = mid + 1;
                else
                    return s;
            }
            throw new HandledException($"WiiFixAsIso: no segment for offset 0x{pos:X}");
        }

        // Lazily-opened recovery source for the currently-served inserted partition (segments are
        // walked forward, so caching the single most-recent stream is sufficient). A recovery file
        // is either a plain on-disk data file (seekable FileStream) or a single entry inside a
        // streamable archive (forward-only stream via SourceFileSystemReader).
        private Stream _recoveryStream;
        private SourceFileSystemReader _recoveryReader; // non-null when the current source is archive-backed
        private string _recoveryPath;
        private string _recoveryInner;                  // inner entry name (null = plain file)
        private long _recoveryPos;                       // current read position within the recovery source

        private void readFromRecoveryFile(string path, string innerName, long fileOffset, byte[] buffer, int bufOffset, int count)
        {
            if (_recoveryPath != path || _recoveryInner != innerName)
            {
                openRecoverySource(path, innerName);
                _recoveryPath = path;
                _recoveryInner = innerName;
            }

            if (innerName == null)
            {
                // Plain file — random access.
                _recoveryStream.Position = fileOffset;
                readExact(_recoveryStream, buffer, bufOffset, count); // zero-fills past EOF
                return;
            }

            // Archive entry — forward-only. Segments for a given inserted partition are walked
            // forward, so fileOffset is monotonic; skip forward if the caller jumped ahead, and
            // re-open (rewind) only on the rare backward request.
            if (fileOffset < _recoveryPos)
            {
                openRecoverySource(path, innerName); // rewind by re-opening the entry
            }
            skipForward(_recoveryStream, fileOffset - _recoveryPos);
            _recoveryPos = fileOffset;

            int got = readExactCounted(_recoveryStream, buffer, bufOffset, count); // zero-fills past EOF
            _recoveryPos += got;
        }

        private void openRecoverySource(string path, string innerName)
        {
            closeRecoverySource();
            _recoveryPos = 0;

            if (innerName == null)
            {
                _recoveryStream = File.OpenRead(path);
                return;
            }

            // Locate the inner entry by mask (archivePath//innerName) using the same scan/read
            // primitives the DatManager uses, then open it as a forward stream.
            FileMask mask = FileMask.CreateLocalMask($"{path}//{innerName}", false);
            FileItem entry = SourceFileSystem.GetLocalArchiveFiles(mask, false, _log, null)
                .FirstOrDefault(a => string.Equals(a.FileName, innerName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                // Missing entry: serve zeros (readExactCounted returns 0 -> caller region zero-fills).
                _recoveryStream = Stream.Null;
                return;
            }
            _recoveryReader = SourceFileSystem.CreateReader(entry, _log, null);
            _recoveryStream = _recoveryReader.OpenRead(entry) ?? Stream.Null;
        }

        private void closeRecoverySource()
        {
            try { _recoveryStream?.Dispose(); } catch { }
            try { _recoveryReader?.Dispose(); } catch { }
            _recoveryStream = null;
            _recoveryReader = null;
        }

        // Skip forward n bytes on a forward-only stream by reading and discarding.
        private static void skipForward(Stream s, long n)
        {
            if (n <= 0)
                return;
            byte[] tmp = new byte[(int)Math.Min(n, 0x10000)];
            while (n > 0)
            {
                int want = (int)Math.Min(n, tmp.Length);
                int got = s.Read(tmp, 0, want);
                if (got == 0)
                    return; // past EOF
                n -= got;
            }
        }

        // Like readExact, but returns the number of real bytes read (before the zero-fill), so the
        // forward position can be advanced correctly for an archive entry.
        private static int readExactCounted(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, offset + total, count - total);
                if (n == 0)
                {
                    Array.Clear(buffer, offset + total, count - total); // zero-fill past EOF
                    return total;
                }
                total += n;
            }
            return total;
        }

        private static void readExact(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, offset + total, count - total);
                if (n == 0)
                {
                    Array.Clear(buffer, offset + total, count - total); // zero-fill past EOF
                    return;
                }
                total += n;
            }
        }

        // Read Game-partition bytes from the source, tolerating a source that cannot serve the full
        // requested span. A compacted WBFS reports a virtual (full-disc) size but only has blocks up
        // to the last stored data; asking its block reader for a region beyond the block map throws
        // (rather than returning a short read) because the underlying array copy overruns. The Game
        // partition's real content lies within the mapped blocks; anything past that is padding for
        // a truncated/compacted disc, which the SectionProcessor re-derives (unscrub/hash/encrypt)
        // downstream. So on a read failure past the mapped source we zero-fill the remainder — the
        // same outcome a raw .iso reaches naturally via a short read.
        private static void readSrcSafe(Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n;
                try
                {
                    n = s.Read(buffer, offset + total, count - total);
                }
                catch (Exception)
                {
                    Array.Clear(buffer, offset + total, count - total); // source can't serve past here
                    return;
                }
                if (n == 0)
                {
                    Array.Clear(buffer, offset + total, count - total); // zero-fill past EOF
                    return;
                }
                total += n;
            }
        }

        // ── Stream surface ──────────────────────────────────────────────────────
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => this.Size;
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current) _position += offset;
            else if (origin == SeekOrigin.End) _position = this.Size + offset;
            else _position = offset;
            return _position;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closeRecoverySource();
                _inner?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}