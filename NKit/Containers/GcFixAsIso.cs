using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GcFileSystemInfo = Nanook.NKit.Nintendo.WiiGc.FileSystemInfo;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// GameCube Fix decorator over a source IAsIso.
    ///
    /// During a Fix task, a compacted/reordered GameCube source has its files at the wrong
    /// offsets (and sometimes wrong order), a shrunk size, and possibly a scrubbed system area.
    /// The legacy path corrects this block-by-block downstream (FsProcessor cache/move/gap), which
    /// forces the preprocessor to hold blocks back and emit a variable number of output blocks.
    ///
    /// GcFixAsIso instead computes the corrected layout ONCE up front (mirroring the NKitAsIso
    /// decoded model): the corrected system area/FST from the recovery fix data, each file placed
    /// at its correct output offset (reorder = read from the file's source offset), and gaps filled
    /// with post-file nulls + generated junk per the FileAnalysis rules. It then presents a
    /// correctly-sized, correctly-ordered GameCube image so the downstream pipeline sees an
    /// already-correct source and does no block moving.
    ///
    /// Serving only: the layout is a list of output segments (Src = copy from the inner source at a
    /// given offset; Fst = corrected system-area bytes; Zero = nulls; Junk = generated GC junk).
    /// Falls back to transparent pass-through of the inner source when no fix file matches.
    /// </summary>
    internal sealed class GcFixAsIso : Stream, IAsIso
    {
        private enum GcSegType { Src, Fst, Zero, Junk }

        private struct GcSeg
        {
            public long Offset;     // output offset where this segment starts
            public long Length;     // bytes
            public GcSegType Type;
            public long SrcOffset;  // for Src: offset in the inner source
            public long FstOffset;  // for Fst: offset into the corrected system-area bytes
            public long IsoEnd => Offset + Length;
        }

        private readonly IAsIso _inner;
        private readonly Stream _innerStream;
        private long _position;
        private long _size;             // corrected output size (FixSize) or inner size when pass-through
        private bool _passThrough;      // true when no fix applies: serve inner unchanged

        /// <summary>The wrapped source container, so a consumer can reach the real underlying
        /// container (e.g. RvzAsIso) for container-specific probes when this decorator is in the chain.</summary>
        internal IAsIso Inner => _inner;

        // Layout (built once, immutable after Prepare)
        private List<GcSeg> _segments;
        private byte[] _correctedSystemArea; // corrected boot/bi2/appldr/maindol/fst bytes (from FsRecover)
        private byte[] _junkId;
        private int _discNo;
        private long _junkStartFsOffset;
        private byte[] _junkBlock;      // reusable 0x40000 junk buffer
        private long _junkBlockStart = -1;
        private bool _junkBlockValid;

        // Data patches (from the fix yaml, keyed by disc Id8). Applied as an overlay onto the
        // served bytes: the old FsProcessor path applied these via SectionProcessor.fsFixPatch, but
        // that step is bypassed for a GcFixAsIso-corrected image (IsGcFixApplied short-circuits it),
        // so the decorator must apply them to the final bytes itself. DataPatches.Offset is an
        // absolute image offset (the whole GC disc is one linear image starting at 0).
        private DataPatches[] _patches;

        // Injected before the first read (from NKitInput.Open, before the Image is constructed, so
        // the layout is built before the Image reads the header).
        private FixData _fixData;
        private long _fixSize;
        private ILogScope _log;
        private bool _prepared;
        private bool _fixRequested;

        // The corrected layout is built during Image construction (the header read happens before
        // the task prints its params banner / starts the progress bar). Logging the fixes inline
        // there makes them appear ABOVE the banner. So buffer them here and flush once real
        // processing starts (Image.Read calls FlushLog), landing them inside the progress area.
        private readonly List<(bool detail, string message)> _pendingLog = new List<(bool, string)>();
        private bool _logFlushed;

        private void logFix(string message, bool detail = false) => _pendingLog.Add((detail, message));

        // Emit the buffered fix messages. Called by the Image on its first processing read so the
        // lines appear after the params banner. Idempotent.
        internal void FlushLog()
        {
            if (_logFlushed)
                return;
            _logFlushed = true;
            if (_log == null)
                return;
            foreach ((bool detail, string message) in _pendingLog)
            {
                string m = message;
                if (detail)
                    _log.Detail(() => m);
                else
                    _log.Info(() => m);
            }
            _pendingLog.Clear();
        }

        internal GcFixAsIso(IAsIso inner)
        {
            _inner = inner;
            _innerStream = (Stream)inner;
            _size = inner.Size; // inner is already constructed when wrapped post-detection
            _position = 0;
        }

        // Decide whether a GameCube source should be wrapped for the Fix task and, if so, build the
        // decorator. Follows the same `Create` convention as the other AsIso containers so the
        // wrap decision lives with the decorator, not the caller. Returns null when it should not
        // wrap (not GameCube, not a Fix step, source already NKit-decoded or already this
        // decorator). Wraps ONLY on the actual fix step: the verify step (StepType == Verify)
        // re-reads the already-corrected output and must pass through un-wrapped.
        public static IAsIso Create(IAsIso iso, SystemType detectedSystem, IImageContext context)
        {
            if (detectedSystem != SystemType.GameCube || context.TaskType != TaskType.Fix
                || context.StepInfo?.StepType == TaskType.Verify
                || iso is NKitAsIso || iso is GcFixAsIso)
                return null;

            GcFixAsIso dec = new GcFixAsIso(iso);
            dec.SetFixData(ResolveFixData(context), WiiConsts.FullSizeGameCube, context.Log);
            return dec;
        }

        // Best-effort targeted FixData resolution. If it can't be loaded the decorator simply
        // passes the source through, so a failure here never makes matters worse than an unwrapped
        // read. Shared by both Fix decorators via GcFixAsIso (WiiFixAsIso.Create calls this).
        internal static FixData ResolveFixData(IImageContext context)
        {
            try
            {
                context.Settings.LoadFixData(new FixData());
                return context.Settings.FixData<FixData>();
            }
            catch { return null; /* best-effort; decorator passes through if unresolved */ }
        }

        // Wire the targeted fix data + target size. Presence of this call means the Fix task is
        // active; the container builds the corrected layout on its first read. fixSize is the
        // full GameCube disc size (WiiConsts.FullSizeGameCube).
        internal void SetFixData(FixData fixData, long fixSize, ILogScope log)
        {
            _fixData = fixData;
            _fixSize = fixSize;
            _log = log;
            _fixRequested = true;
        }

        // True once the corrected layout has been successfully built (files placed, gaps/junk,
        // corrected header + size). The GC Image reflects this into ImageInfo.IsGcFixApplied so the
        // downstream pipeline passes the already-correct data through unchanged.
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
        // Report estimated size so the GameCube Image sizes the output to the full GC disc size
        // (lenCalc => FullSizeGameCube) rather than the smaller compacted source size — otherwise
        // reads are truncated at the source length and the reordered tail is lost.
        public bool SizeEstimated => true;
        public Checksums Checksums => _inner.Checksums;
        public Checksums CustomChecksums() => _inner.CustomChecksums();
        public NKitHeader NKitHeader => _inner.NKitHeader;
        public void SetRemovedBlock(Action<MetaData> setBlock) => _inner.SetRemovedBlock(setBlock);
        public void Complete() => _inner.Complete();

        public long Size => _prepared ? _size : _inner.Size;

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
                // Any failure => transparent pass-through of the inner source (never worse than
                // not wrapping at all). The downstream Fix step still runs.
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

            // Minimal GameCube ImageInfo for the FS parse + FST recovery (Multiplier 1, Fix mode, no
            // hashes). This is self-contained — the decorator does not depend on the Image's
            // ImageInfo (which is not yet created at wrap time).
            ImageInfo gcInfo = new ImageInfo
            {
                Type = ImageType.GameCube,
                SystemType = SystemType.GameCube,
                Mode = ReadMode.Fix,
                Multiplier = 1L,
                FixSize = _fixSize,
                SourceHasHashes = false,
            };

            // 1. Parse the SOURCE system area/FST via a FileSystemInfo (the tested path).
            GcFileSystemInfo srcFs = readSourceFileSystem(gcInfo);
            if (srcFs == null || srcFs.FileSystem == null)
            {
                _passThrough = true;
                return;
            }

            // 2. Build the corrected system-area FST (deterministic, from fix data) — see
            //    GetCorrectedFstData below.
            //    Setup() must run first: it resolves FixData.FixPath from the fixFiles folder, which
            //    is what enables GcBinFiles to scan and load the fst/appldr .bin files. Without it
            //    FixPath is null and GcBinFiles is silently empty (mirrors WiiFixAsIso.Setup(Id8)).
            _fixData.Setup(srcFs.Id8);

            //    Filter fix files by the source disc Id8 and include apploaders — mirrors
            //    FsProcessor.Create so we only try the candidates that apply to THIS disc (trying a
            //    foreign fst throws "not enough space for AppLoader").
            List<FixFileItem> allGcFix = _fixData.GcBinFiles ?? new List<FixFileItem>();
            int fstCount = allGcFix.Count(a => a is FstFileItem);
            int aplCount = allGcFix.Count(a => a is ApploaderFileItem);
            logFix($"[GC Fix] fixFiles: {fstCount} fst, {aplCount} appldr from '{_fixData.FixPath?.FullName ?? "(no path)"}'");

            IEnumerable<FixFileItem> gcFixFiles = allGcFix
                .Where(a => a is FstFileItem)
                .Cast<FstFileItem>()
                .Where(a => a.Id8 == srcFs.Id8)
                .Cast<FixFileItem>()
                .Concat(allGcFix.Where(a => a is ApploaderFileItem));

            // Collect the "Fix file '...' applied" / scrub messages into our buffer (pass a
            // list so they are NOT written to the log inline during construction — FlushLog emits
            // them later, after the banner).
            List<string> appliedLog = new List<string>();
            byte[] correctedFst = GetCorrectedFstData(gcFixFiles, srcFs, gcInfo, _log, out bool alreadyValid, appliedLog);
            foreach (string m in appliedLog)
                logFix(m);
            if (correctedFst == null)
            {
                // No fix file matched: pass through (the Fix step will still DAT-match / report).
                logFix($"[GC Fix] No matching fix file for disc Id8 '{srcFs.Id8}' — nothing to recover (passing source through)");
                _passThrough = true;
                return;
            }

            if (alreadyValid)
                logFix($"[GC Fix] System area / FST already valid for '{srcFs.Id8}' (fix file confirmed)");
            else
                logFix($"[GC Fix] Corrected system area / FST for '{srcFs.Id8}' from fix file (FST CRC {Crc.Compute(correctedFst):X8})");

            // Data patches (from the fix yaml, keyed by disc Id8). The old FsProcessor path applied
            // these downstream in SectionProcessor.fsFixPatch, but that step is bypassed once the
            // image is GcFixAsIso-corrected (IsGcFixApplied short-circuits processGcAndRvtH), so we
            // apply them here as an overlay on the served bytes (see applyPatches in Read).
            _patches = (_fixData.DataPatches != null && _fixData.DataPatches.Length != 0) ? _fixData.DataPatches : null;
            if (_patches != null)
            {
                logFix($"[GC Fix] Applying {_patches.Length} data patch(es) from fix yaml");
                foreach (DataPatches p in _patches)
                    logFix($"[GC Fix]   patch at 0x{p.Offset:X} ({p.Data.Length} bytes)", detail: true);
            }

            // 3. Snapshot the SOURCE file layout (FullName -> current source offset) BEFORE
            //    SetOutputFileSystem overwrites FileSystem.Files with the corrected layout. This is
            //    how we map a reordered/relocated output file back to where it lives in the source
            //    (mirrors FsProcessor caching inFiles before outFiles).
            Dictionary<string, long> srcOffsetByName = new Dictionary<string, long>();
            foreach (FstFile sf in srcFs.FileSystem.Files.Cast<FstFile>())
                if (!sf.IsSystemFile)
                    srcOffsetByName[sf.FullName] = sf.FsOffset;

            // 4. Apply the corrected FST to get the reordered/relocated OUTPUT file layout.
            //    SetOutputFileSystem rebuilds FileSystem.Files with corrected FsOffset + per-file
            //    PostGap analysis, sizing the disc to FixSize.
            long fixSize = _fixSize;
            long srcSize = _inner.Size;
            srcFs.SetOutputFileSystem(correctedFst, fixSize);

            if (srcSize != fixSize)
                logFix($"[GC Fix] Corrected disc size {srcSize:N0} -> {fixSize:N0} bytes");

            _correctedSystemArea = correctedFst;
            // Junk id: honor a junkIdSubstitutions override from the fix yaml (some discs junk with
            // an id other than their own header id). This mirrors the old path, which wired the
            // substitution through ImageInfo.FixJunkId -> FileSystemInfo.ForceJunkId. Without it we
            // fall back to the disc header id. (readSourceFileSystem builds srcFs before Setup() has
            // resolved ForceJunkId, so apply the override here.)
            if (!string.IsNullOrWhiteSpace(_fixData.ForceJunkId))
            {
                _junkId = Encoding.ASCII.GetBytes(_fixData.ForceJunkId);
                logFix($"[GC Fix] Using junk id substitution '{_fixData.ForceJunkId}' (not disc header id)");
            }
            else
                _junkId = srcFs.JunkId ?? correctedFst.Read(0, 4);
            _discNo = correctedFst[6];
            // Junk start offset must match the proven old path exactly: the UNALIGNED FST end plus
            // the DataNullsCount leading nulls (FileSystemInfo.JunkStartFsOffset). This only sets
            // where junk begins within its first block (NJunk.Fill zero-clears before it); it does
            // NOT change the junk pattern (which is keyed on id/disc/absolute offset). Aligning it
            // up to 4 bytes shifted that boundary and broke discs where the unaligned value was
            // correct, so use the source FileSystemInfo value verbatim.
            _junkStartFsOffset = srcFs.JunkStartFsOffset;
            _junkBlock = new byte[NJunk.JunkBlockSize];
            _size = fixSize;

            // 5. Build the output segment map: each output file served from its SOURCE offset
            //    (reorder-safe), gaps filled with post-file nulls + junk, size corrected.
            buildSegments(srcFs, fixSize, srcOffsetByName);
            // (IsGcFixApplied is set on the Image's ImageInfo by WiiGc.Image.Setup, reading
            //  IsCorrected, since the decorator has no reference to the Image's ImageInfo.)
        }

        // Read the source system area (boot.bin .. end of FST) and parse it into a FileSystemInfo,
        // mirroring how the pipeline populates GC fs info.
        private GcFileSystemInfo readSourceFileSystem(ImageInfo gcInfo)
        {
            // boot.bin holds FST ptr/size at fixed offsets.
            byte[] boot = new byte[WiiConsts.BootBinSize];
            _innerStream.Position = 0;
            readExact(_innerStream, boot, 0, boot.Length);

            long fstPtr = boot.ReadUInt32B(WiiConsts.FstPtrOffset); // multiplier 1 for GC
            long fstSize = boot.ReadUInt32B(WiiConsts.FstSizeOffset);
            long fstEnd = fstPtr + fstSize;
            if (fstEnd <= 0 || fstEnd > _inner.Size)
                return null;

            // Round the system-area read up to a GC block so the Buffer/AreaInfo math is happy.
            long sysLen = fstEnd;
            long blk = WiiConsts.WiiSectorSize; // 0x8000
            if (sysLen % blk != 0)
                sysLen += blk - (sysLen % blk);

            byte[] sysArea = new byte[sysLen];
            _innerStream.Position = 0;
            readExact(_innerStream, sysArea, 0, (int)Math.Min(sysLen, _inner.Size));

            AreaInfo ai = new AreaInfo(0, AreaType.FileSystem, 0);
            ai.SetBlock((int)blk, 0, (int)blk, (int)WiiConsts.WiiGroupSize);
            ai.SetSecurity(false, false, false);

            Buffer buf = new Buffer(false, sysArea);
            buf.ReInitialise(ai, true);
            buf.Update(0, 0, sysArea.Length, 0, false, false);

            // For GC the FileSystemInfo 'header' arg is only stored (Wii-only parsing is gated
            // off); the real FST comes from SetFsInfo below. Pass the source system bytes.
            GcFileSystemInfo fs = new GcFileSystemInfo(null, 0, sysArea, gcInfo, null, _inner.Size, _inner.Size);
            fs.SetFsInfo(buf, 0);
            return fs;
        }

        private void buildSegments(GcFileSystemInfo srcFs, long fixSize, Dictionary<string, long> srcOffsetByName)
        {
            _segments = new List<GcSeg>();
            List<IFsFile> files = srcFs.FileSystem.Files;

            long dst = 0;
            long sysLen = srcFs.FstOffset + srcFs.FstSize; // corrected system-area length (boot..fst)

            // System area: served from the corrected FST bytes.
            _segments.Add(new GcSeg { Offset = 0, Length = sysLen, Type = GcSegType.Fst, FstOffset = 0 });
            dst = sysLen;

            int movedCount = 0;   // files whose output offset differs from their source offset
            long junkTotal = 0;   // total junk bytes regenerated in gaps

            // Files in output order. For each file: emit its data from the SOURCE offset (the file
            // may have moved/reordered), then the post-file gap (ExpectedNulls zeros then junk).
            foreach (FstFile f in files.Cast<FstFile>().Where(a => !a.IsSystemFile).OrderBy(a => a.FsOffset))
            {
                if (f.FsOffset > dst)
                {
                    // gap before this file (should be covered by prior post-gap, but guard)
                    emitGap(dst, f.FsOffset - dst);
                    junkTotal += f.FsOffset - dst;
                    dst = f.FsOffset;
                }

                // File data: output offset == f.FsOffset (corrected); source offset == where the
                // same-named file lives in the source (reorder/move-safe). Fall back to the
                // output offset if the name is unexpectedly absent (no-move degenerate case).
                long srcOff = srcOffsetByName.TryGetValue(f.FullName, out long so) ? so : f.FsOffset;
                if (srcOff != f.FsOffset)
                {
                    movedCount++;
                    // Per-file detail at Detail level (can be verbose on heavily-reordered discs).
                    logFix($"[GC Fix]   moved '{f.FullName}' src 0x{srcOff:X} -> out 0x{f.FsOffset:X} ({f.FsSize:N0} bytes)", detail: true);
                }
                _segments.Add(new GcSeg { Offset = f.FsOffset, Length = f.FsSize, Type = GcSegType.Src, SrcOffset = srcOff });
                dst = f.FsOffset + f.FsSize;

                long gap = f.PostGapSize;
                if (gap > 0)
                {
                    int nulls = f.Analysis.ExpectedNulls;
                    if (nulls > 0)
                    {
                        _segments.Add(new GcSeg { Offset = dst, Length = nulls, Type = GcSegType.Zero });
                        dst += nulls;
                    }
                    long junk = gap - nulls;
                    if (junk > 0)
                    {
                        _segments.Add(new GcSeg { Offset = dst, Length = junk, Type = GcSegType.Junk });
                        junkTotal += junk;
                        dst += junk;
                    }
                }
            }

            // Trailing fill to the corrected size.
            if (dst < fixSize)
            {
                junkTotal += fixSize - dst;
                emitGap(dst, fixSize - dst);
            }

            int fileCount = files.Count(a => !((FstFile)a).IsSystemFile);
            logFix($"[GC Fix] Rebuilt layout: {fileCount} files ({movedCount} reordered/relocated), {junkTotal:N0} bytes gap/junk regenerated");
        }

        private void emitGap(long dst, long len) =>
            // GC trailing/inter gaps: junk for the junk region, zeros beyond the sector-aligned tail.
            _segments.Add(new GcSeg { Offset = dst, Length = len, Type = GcSegType.Junk });

        // ── Stream / reads ──────────────────────────────────────────────────────
        public override int Read(byte[] buffer, int offset, int count)
        {
            // Fix data is wired (at Open) before the Image reads anything, so build the corrected
            // layout on the very first read — the Image then reads a fully corrected GameCube ISO
            // (including the corrected system-area header).
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
            long startPos = _position; // output offset this Read covers: [startPos, _position)

            while (copied < toRead)
            {
                GcSeg seg = findSegment(_position);
                long segRemaining = seg.IsoEnd - _position;
                int chunk = (int)Math.Min(toRead - copied, segRemaining);
                long offsetInSeg = _position - seg.Offset;

                switch (seg.Type)
                {
                    case GcSegType.Src:
                        _innerStream.Position = seg.SrcOffset + offsetInSeg;
                        readExact(_innerStream, buffer, offset + copied, chunk);
                        break;
                    case GcSegType.Fst:
                        Array.Copy(_correctedSystemArea, seg.FstOffset + offsetInSeg, buffer, offset + copied, chunk);
                        break;
                    case GcSegType.Zero:
                        Array.Clear(buffer, offset + copied, chunk);
                        break;
                    case GcSegType.Junk:
                        fillJunk(buffer, offset + copied, _position, chunk);
                        break;
                }

                copied += chunk;
                _position += chunk;
            }

            // Overlay any fix-yaml data patches that fall in the range we just produced. Replaces
            // the equivalent SectionProcessor.fsFixPatch step, which is bypassed for a corrected
            // GameCube image. Absolute image offsets, so they map directly onto the output stream.
            applyPatches(buffer, offset, startPos, copied);

            return copied;
        }

        // Overwrite bytes of the just-produced output window [winStart, winStart+winLen) with any
        // overlapping data patches. buffer[bufBase + (patchAbs - winStart)] receives the patch data.
        private void applyPatches(byte[] buffer, int bufBase, long winStart, int winLen)
        {
            if (_patches == null || winLen == 0)
                return;

            long winEnd = winStart + winLen;
            foreach (DataPatches p in _patches)
            {
                long pStart = p.Offset;
                long pEnd = p.Offset + p.Data.Length;
                long from = Math.Max(pStart, winStart);
                long to = Math.Min(pEnd, winEnd);
                if (to <= from)
                    continue; // no overlap with this read window

                int srcOff = (int)(from - pStart);      // offset into patch data
                int dstOff = bufBase + (int)(from - winStart); // offset into caller buffer
                int len = (int)(to - from);
                Array.Copy(p.Data, srcOff, buffer, dstOff, len);
            }
        }

        private GcSeg findSegment(long pos)
        {
            // binary search (segments are sorted, contiguous, cover [0, _size))
            int lo = 0, hi = _segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                GcSeg s = _segments[mid];
                if (pos < s.Offset)
                    hi = mid - 1;
                else if (pos >= s.IsoEnd)
                    lo = mid + 1;
                else
                    return s;
            }
            throw new HandledException($"GcFixAsIso: no segment for offset 0x{pos:X}");
        }

        private void fillJunk(byte[] buffer, int bufOffset, long isoOffset, int count)
        {
            while (count > 0)
            {
                long blockStart = isoOffset - (isoOffset % NJunk.JunkBlockSize);
                if (!_junkBlockValid || _junkBlockStart != blockStart)
                {
                    NJunk.Fill(_junkId, _discNo, _junkStartFsOffset, _size, blockStart, _junkBlock);
                    _junkBlockStart = blockStart;
                    _junkBlockValid = true;
                }
                int inBlock = (int)(isoOffset - blockStart);
                int chunk = (int)Math.Min(count, NJunk.JunkBlockSize - inBlock);
                Array.Copy(_junkBlock, inBlock, buffer, bufOffset, chunk);
                bufOffset += chunk;
                isoOffset += chunk;
                count -= chunk;
            }
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
                _inner?.Dispose();
            base.Dispose(disposing);
        }

        // ── GameCube FST recovery (migrated from the removed FsRecover) ─────────────────────────
        // Deterministically rebuilds the corrected system-area FST bytes for a GameCube source from
        // the fix data, or returns null if no fix file matches (or the current FST is already
        // valid). GcFixAsIso is the sole consumer, building an up-front output layout from this.
        // 'alreadyValid' is set true when a matching fix file confirms the current FST is correct.
        // 'appliedLog' (optional): when supplied, the "Fix file '...' applied" / scrub messages are
        // COLLECTED into it instead of being written to 'log', so they can be emitted later (after
        // the params banner) rather than during Image construction.
        private static byte[] GetCorrectedFstData(IEnumerable<FixFileItem> fixFiles, GcFileSystemInfo fsInfo, ImageInfo imageInfo, ILogScope log, out bool alreadyValid, List<string> appliedLog)
        {
            alreadyValid = false;
            if (fsInfo?.FileSystem == null || fixFiles == null)
                return null;

            uint currentCrc = Crc.Compute(fsInfo.FstData);
            foreach (FstFileItem rfst in fixFiles.Where(a => a is FstFileItem))
            {
                rfst.Populate(log);
                ApploaderFileItem rapl = (ApploaderFileItem)fixFiles.FirstOrDefault(a => a is ApploaderFileItem && a.Crc == rfst.AppLoadCrc);

                byte[] fstData = bruteForceValidHeader(rfst, rapl, fsInfo, imageInfo, log, appliedLog);
                if (fstData != null)
                {
                    alreadyValid = currentCrc == Crc.Compute(fstData);
                    return fstData;
                }
            }
            return null;
        }

        private static byte[] bruteForceValidHeader(FstFileItem fst, ApploaderFileItem appldr, GcFileSystemInfo fsInfo, ImageInfo imageInfo, ILogScope log, List<string> appliedLog)
        {
            int hdrScrub = 0;
            int bi2Scrub = 0;
            int maindolScrub = 0;
            byte[] bootBin = fsInfo.BootBin; //clone
            byte[] bi2bin = fsInfo.Bi2Bin; //clone
            byte[] maindol = fsInfo.MainDol; //clone
            byte[] fstData = new byte[fst.FstOffset + fst.FstData.Length];
            bi2bin.Write8(0x1b, (byte)fst.Region);

            foreach (int scrub in new int[] { 0, 1, 2 })
            {
                if (scrub == 1)
                {
                    hdrScrub = nullData(bootBin, 0x300, 4);
                    bi2Scrub = nullData(bi2bin, 0x500 - WiiConsts.BootBinSize, 4);
                    if (hdrScrub == 0 && bi2Scrub == 0)
                        continue; //no change
                }
                else if (scrub == 2)
                {
                    hdrScrub = nullData(bootBin, 0x80, 0x3F0 - 0x80);
                    bi2Scrub = nullData(bi2bin, 0x30, bi2bin.Length - (0x30 + 0x60)); //0x30 at start and 0x50 from end (conflict I, II + Splinter cell have 0x40 at end)
                    if (hdrScrub == 0 && bi2Scrub == 0)
                        continue; //no change
                }

                bootBin.WriteString(WiiConsts.DataHdrIdOffset, 6, fst.Id8);
                bootBin.Write8(WiiConsts.DataHdrDiscNoOffset, byte.Parse(fst.Id8.Substring(6, 2)));
                bootBin.Write8(WiiConsts.DataHdrRevisionOffset, byte.Parse(fst.Id8.Substring(8, 2)));
                bootBin.Write(WiiConsts.DataHdrTitleOffset, fst.Title, fst.Title.Length);
                bootBin.WriteString(WiiConsts.DataHdrIdOffset, 6, fst.Id8);
                bootBin.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)(fst.MainDolOffset / imageInfo.Multiplier));
                bootBin.WriteUInt32B(WiiConsts.FstPtrOffset, (uint)(fst.FstOffset / imageInfo.Multiplier));
                bootBin.WriteUInt32B(WiiConsts.FstSizeOffset, (uint)(fst.FstData.Length / imageInfo.Multiplier));
                bootBin.WriteUInt32B(WiiConsts.FstSizeMaxOffset, (uint)(fst.MaxFst / imageInfo.Multiplier));
                fstData.Write(0, bootBin, 0, bootBin.Length);
                int dstPos = bootBin.Length;

                fstData.Write(dstPos, bi2bin, 0, bi2bin.Length);
                dstPos += bi2bin.Length;

                byte[] al = appldr == null ? fsInfo.Appldr : appldr.ReadAllData(log);

                if (al.Length > fstData.Length - dstPos)
                    throw new HandledException("Not enough space for AppLoader - check this is not a Datel image as they are not supported for Fix");

                fstData.Write(dstPos, al, 0, al.Length);
                dstPos += al.Length;

                int l = (int)(Math.Min(bootBin.ReadUInt32B(WiiConsts.DolPtrOffset), fst.FstOffset) - dstPos);
                fstData.Clear(dstPos, l, 0);
                dstPos += l;

                if (maindol != null)
                {
                    fstData.Write(dstPos, maindol, 0, Math.Min((int)(fst.FstOffset - dstPos), maindol.Length));
                    dstPos += Math.Min((int)(fst.FstOffset - dstPos), maindol.Length);
                }

                int padding = (int)(fst.FstOffset - dstPos);
                fstData.Clear(dstPos, (int)padding, 0);
                dstPos += padding;

                fstData.Write(dstPos, fst.FstData, 0, fst.FstData.Length);
                dstPos += fst.FstData.Length;

                if (Crc.Compute(fstData, 0, dstPos) == fst.PostFstCrc)
                {
                    string m1 = $"Fix file '{fst.DisplayName}' applied (ID, Title, Region, Sys file offsets)";
                    string m2 = appldr != null ? $"Fix file '{appldr.DisplayName}' applied" : null;
                    string m3 = (hdrScrub != 0 || bi2Scrub != 0 || maindolScrub != 0)
                        ? $"Bytes scrubbed in hdr.bin ({hdrScrub}), bi2.bin ({bi2Scrub}), main.dol ({maindolScrub})"
                        : null;
                    if (appliedLog != null)
                    {
                        appliedLog.Add(m1);
                        if (m2 != null) appliedLog.Add(m2);
                        if (m3 != null) appliedLog.Add(m3);
                    }
                    else
                    {
                        log?.Info(() => m1);
                        if (m2 != null) log?.Info(() => m2);
                        if (m3 != null) log?.Info(() => m3);
                    }
                    return fstData;
                }
            }
            return null;
        }

        private static int nullData(byte[] data, int offset, int length)
        {
            int headerRemoved = 0;
            for (int i = 0; i < length; i++)
            {
                if (data[i + offset] != 0)
                {
                    headerRemoved++;
                    data[i + offset] = 0;
                }
            }
            return headerRemoved;
        }
    }
}