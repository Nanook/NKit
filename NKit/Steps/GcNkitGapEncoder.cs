using Nanook.NKit.Nintendo.WiiGc;
using System;

namespace Nanook.NKit
{
    /// <summary>
    /// Encodes a single GameCube disc gap region into the NKit v01 gap format.
    /// Direct port of v1 Gap.cs — same block classification, run-length encoding,
    /// and header format.
    ///
    /// Streaming design: once the gap type is determined to be Mixed (first non-Junk,
    /// non-AllScrub block seen), the 4-byte Mixed header is written immediately via the
    /// caller-supplied write callback, followed by any buffered Junk descriptors from
    /// before the transition.  All subsequent descriptors and raw NonJunk bytes are
    /// streamed directly.
    ///
    /// Peak allocation is a small pre-header buffer (a few descriptors at most) plus one
    /// ~50 MiB NonJunk run window.  For AllJunk / AllScrub gaps the only output is a
    /// single 4-byte header — nothing is buffered.
    ///
    /// Usage per gap:
    ///   1. <see cref="Begin"/> — records write callback and stream position
    ///   2. <see cref="Feed"/> per 256-byte block until it returns true
    ///   3. <see cref="Complete"/> — flushes trailing run, writes gap header,
    ///      returns total bytes written (header + descriptors + raw data)
    /// </summary>
    internal sealed class GcNkitGapEncoder
    {
        // ── Constants ─────────────────────────────────────────────────────────────────

        /// <summary>Block size used for classification (v1 Gap.BlockSize = 0x100).</summary>
        public const int BlockSize = 0x100;

        // Pre-header buffer: holds Junk/Scrub descriptors written before the gap type
        // is confirmed as Mixed.  At most a handful of 4-byte descriptor words.
        private const int _PreHdrBufSize = 256; // 64 descriptors × 4 bytes — ample

        // NonJunk accumulation window: capped at ~52 MiB (203777 blocks × 256 bytes + 4).
        private const int _NonJunkBufSize = (int)(_MaxNonJunkBlocks * BlockSize) + 4;

        // Gap header type bits (low 2 bits) — v1 GapType enum
        private const uint _TypeAllJunk  = 0u;
        private const uint _TypeAllScrub = 1u;
        private const uint _TypeMixed    = 2u;

        // Block descriptor top-2-bit types — v1 GapBlockType enum
        private const uint _BlkJunk    = 0u;
        private const uint _BlkNonJunk = 1u;
        private const uint _BlkScrub   = 2u;

        // classifyBlock return values
        private const int _ClassJunk    = 0;
        private const int _ClassScrub   = 1;
        private const int _ClassNonJunk = 2;

        // V1 splits NonJunk runs at 203777 blocks (~50 MiB MemoryStream flush limit).
        private const uint _MaxNonJunkBlocks = 203777u;

        // ── Streaming state ───────────────────────────────────────────────────────────

        private Action<byte[], int, int> _write;

        // Pre-header buffer: Junk/Scrub descriptors written before Mixed is confirmed.
        private byte[] _preHdrBuf;
        private int    _preHdrLen;
        private bool   _headerWritten; // true once the 4-byte gap header is in the stream

        // NonJunk accumulation window
        private byte[] _nonJunkBuf;
        private int    _nonJunkBufLen;

        // Scratch 4-byte buffer for single descriptor writes
        private readonly byte[] _desc = new byte[4];

        // ── Per-gap state ─────────────────────────────────────────────────────────────

        private long  _gapDiscStart;
        private long  _gapLength;
        private long  _remaining;
        private int   _leadingNulls;
        private bool  _inProgress;

        // Gap-type tracking
        private bool  _allJunk;
        private bool  _allScrub;

        // Current Junk/Scrub run
        private uint  _runType;
        private uint  _runCount;
        private bool  _runActive;
        private byte  _runFill;

        // Current NonJunk run
        private uint  _nonJunkCount;
        private bool  _nonJunkActive;

        // DOL protection range
        private long  _dolStart;
        private long  _dolEnd;

        // Junk generation cache (persists across gaps)
        private byte[]   _junkCache;
        private long     _junkCacheBlockStart;
        private byte[]   _discId;
        private byte     _discNo;

        // Pool of reusable 256KB junk arrays — avoids per-call allocation in PrefillJunkCache.
        private byte[][] _junkPool;

        // Junk multi-block cache: keyed by junk block start offset → 256KB junk bytes.
        // Populated by PrefillJunkCache before a batch of Feed calls.
        // classifyBlock checks this first before falling back to the single-block _junkCache.
        private System.Collections.Generic.Dictionary<long, byte[]> _junkBatchCache;

        // Pre-classified disc regions from section.Items — set by SetSectionJunkMap().
        // classifyBlock checks these before generating junk, short-circuiting NJunk.Fill
        // for regions the pipeline SectionProcessor has already verified.
        // Pre-classified disc regions from section.Items — set by SetSectionJunkMap().
        private System.Collections.Generic.List<ClassifiedRegion> _classifiedRegions;
        // Index cache for sequential access — avoids full list scan on every classifyBlock.
        private int _classifiedIdx;

        /// <summary>
        /// A pre-classified disc region derived from a section's SectionItems gap analysis.
        /// </summary>
        internal struct ClassifiedRegion
        {
            public long     DiscStart;
            public long     DiscEnd;    // exclusive
            public DataType Type;       // NJunk or Fill — only these are useful for fast-path
            public int      DataNulls;
            public byte     FillByte;
        }

        // ── Public API ────────────────────────────────────────────────────────────────

        public bool InProgress   => _inProgress;
        public long Remaining    => _remaining;
        public long GapDiscStart => _gapDiscStart;
        public long GapLength    => _gapLength;

        public GcNkitGapEncoder()
        {
            _preHdrBuf           = new byte[_PreHdrBufSize];
            // _nonJunkBuf and _junkBatchCache allocated lazily on first use
            _junkCache           = new byte[NJunk.JunkBlockSize];
            _junkCacheBlockStart = -1L;
        }

        public void Begin(long gapDiscStart, long gapLength, int leadingNulls,
                          byte[] discId, byte discNo, long dolStart, long dolEnd,
                          Action<byte[], int, int> write)
        {
            _write          = write;
            _gapDiscStart   = gapDiscStart;
            _gapLength      = gapLength;
            _remaining      = gapLength;
            _leadingNulls   = leadingNulls;
            _discId         = discId;
            _discNo         = discNo;
            _dolStart       = dolStart;
            _dolEnd         = dolEnd;
            _allJunk        = true;
            _allScrub       = true;
            _runActive      = false;
            _runType        = 0;
            _runCount       = 0;
            _runFill        = 0;
            _nonJunkActive  = false;
            _nonJunkCount   = 0;
            _nonJunkBufLen  = 0;
            _preHdrLen      = 0;
            _headerWritten  = false;
            _inProgress     = true;
        }

        /// <summary>
        /// Pre-generate all 256KB junk blocks covering <paramref name="length"/> bytes
        /// starting at <paramref name="firstDiscOffset"/> in parallel.  Call this once
        /// per carry section before the Feed loop to eliminate synchronous NJunk.Fill
        /// calls during classification.
        /// </summary>
        /// <summary>
        /// Supply pre-classified disc regions from the section's SectionItems.
        /// classifyBlock will return NJunk/Fill directly for regions covered here,
        /// avoiding NJunk.Fill calls for blocks already verified by the pipeline.
        /// Call before each PrefillJunkCache / Feed batch.
        /// </summary>
        public void SetSectionJunkMap(System.Collections.Generic.List<ClassifiedRegion> regions)
        {
            if (_classifiedRegions == null)
                _classifiedRegions = new System.Collections.Generic.List<ClassifiedRegion>();
            _classifiedRegions.Clear();
            if (regions != null)
            {
                _classifiedRegions.AddRange(regions);
                _classifiedRegions.Sort((a, b) => a.DiscStart.CompareTo(b.DiscStart));
            }
            _classifiedIdx = 0;
        }

        public void PrefillJunkCache(long firstDiscOffset, int length)
        {
            if (length <= 0)
                return;

            long jbStart = (firstDiscOffset       / NJunk.JunkBlockSize) * NJunk.JunkBlockSize;
            long jbEnd   = ((firstDiscOffset + length - 1) / NJunk.JunkBlockSize) * NJunk.JunkBlockSize;
            int  jbCount = (int)((jbEnd - jbStart) / NJunk.JunkBlockSize) + 1;

            _junkBatchCache ??= new System.Collections.Generic.Dictionary<long, byte[]>();
            _junkBatchCache.Clear();

            // Grow the pool of reusable junk arrays as needed
            if (_junkPool == null || _junkPool.Length < jbCount)
            {
                _junkPool = new byte[Math.Max(jbCount, 8)][];
                for (int j = 0; j < _junkPool.Length; j++)
                    _junkPool[j] = new byte[NJunk.JunkBlockSize];
            }

            if (jbCount == 1)
            {
                // Single block: skip thread overhead.
                // Skip if fully covered by a classified NJunk region (no fill needed).
                if (!isJunkCoveredByRegion(jbStart, jbStart + NJunk.JunkBlockSize))
                {
                    NJunk.Fill(_discId, _discNo, jbStart, WiiConsts.FullSizeGameCube, jbStart, _junkPool[0]);
                    _junkBatchCache[jbStart] = _junkPool[0];
                }
            }
            else
            {
                System.Threading.Tasks.Parallel.For(0, jbCount,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) },
                    j =>
                    {
                        long bs = jbStart + (long)j * NJunk.JunkBlockSize;
                        if (!isJunkCoveredByRegion(bs, bs + NJunk.JunkBlockSize))
                            NJunk.Fill(_discId, _discNo, bs, WiiConsts.FullSizeGameCube, bs, _junkPool[j]);
                    });

                for (int j = 0; j < jbCount; j++)
                {
                    long bs = jbStart + (long)j * NJunk.JunkBlockSize;
                    if (!isJunkCoveredByRegion(bs, bs + NJunk.JunkBlockSize))
                        _junkBatchCache[bs] = _junkPool[j];
                }
            }

            // Also update the single-block cache to the last generated block
            _junkCacheBlockStart = jbStart + (long)(jbCount - 1) * NJunk.JunkBlockSize;
            Array.Copy(_junkPool[jbCount - 1], _junkCache, NJunk.JunkBlockSize);
        }

        public bool Feed(byte[] data, int offset, int length, long discOffset)
        {
            bool isFirst   = (_gapLength - _remaining == 0);
            int  leadNulls = isFirst ? _leadingNulls : 0;
            int  bType     = classifyBlock(data, offset, length, discOffset, leadNulls);
            processClassified(data, offset, length, bType);

            _remaining -= length;
            return _remaining <= 0;
        }

        /// <summary>
        /// Complete the gap. Flushes any pending run and writes the gap header.
        /// </summary>
        public void Complete()
        {
            flushNonJunk();
            flushRun();

            uint gapType = _allJunk ? _TypeAllJunk : _allScrub ? _TypeAllScrub : _TypeMixed;

            uint hdr = _gapLength >= 0xFFFFFFFCL
                ? (uint)(0xFFFFFFFCL | gapType)
                : (uint)((_gapLength & ~3L) | gapType);

            if (!_headerWritten)
            {
                // AllJunk or AllScrub: write the final 4-byte header now.
                // No descriptors were emitted so the pre-header buffer is empty.
                writeWord(hdr);
            }
            // Mixed: header was already written (correct type) — nothing more to do.

            _inProgress = false;
        }

        // ── Internal helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Apply one classified block to the serial run-length state machine.
        /// Must be called in disc-offset order.
        /// </summary>
        private void processClassified(byte[] data, int offset, int length, int bType)
        {
            if (bType != _ClassJunk)  _allJunk  = false;
            if (bType != _ClassScrub || data[offset] != 0x00) _allScrub = false;

            uint nkitType = bType == _ClassNonJunk ? _BlkNonJunk
                          : bType == _ClassScrub   ? _BlkScrub
                          :                          _BlkJunk;

            if (!_headerWritten && (nkitType == _BlkNonJunk || (nkitType == _BlkScrub && data[offset] != 0x00)))
                flushPreHeaderAsMixed();

            if (nkitType == _BlkNonJunk)
            {
                flushRun();

                if (!_nonJunkActive)
                {
                    if (_nonJunkBuf == null)
                        _nonJunkBuf = new byte[_NonJunkBufSize];
                    _nonJunkBufLen = 4;
                    _nonJunkActive = true;
                    _nonJunkCount  = 0;
                }

                Array.Copy(data, offset, _nonJunkBuf, _nonJunkBufLen, length);
                _nonJunkBufLen += length;
                _nonJunkCount++;

                if (_nonJunkCount >= _MaxNonJunkBlocks)
                    flushNonJunk();
            }
            else
            {
                flushNonJunk();

                if (_runActive && _runType == nkitType
                    && (_runType != _BlkScrub || data[offset] == _runFill))
                {
                    _runCount++;
                }
                else
                {
                    flushRun();
                    _runType   = nkitType;
                    _runCount  = 1;
                    _runActive = true;
                    _runFill   = (nkitType == _BlkScrub) ? data[offset] : (byte)0;
                }
            }
        }

        /// <summary>
        /// Transition to Mixed: write the Mixed gap header then flush any Junk/Scrub
        /// descriptors that were buffered before the type was known.
        /// </summary>
        private void flushPreHeaderAsMixed()
        {
            uint hdr = _gapLength >= 0xFFFFFFFCL
                ? (uint)(0xFFFFFFFCL | _TypeMixed)
                : (uint)((_gapLength & ~3L) | _TypeMixed);

            writeWord(hdr);
            _headerWritten = true;

            // Flush any descriptors buffered while type was still ambiguous
            if (_preHdrLen > 0)
            {
                _write(_preHdrBuf, 0, _preHdrLen);
                _preHdrLen = 0;
            }
        }

        private void flushNonJunk()
        {
            if (!_nonJunkActive)
                return;

            uint w = (_BlkNonJunk << 30) | _nonJunkCount;
            _nonJunkBuf[0] = (byte)(w >> 24);
            _nonJunkBuf[1] = (byte)(w >> 16);
            _nonJunkBuf[2] = (byte)(w >>  8);
            _nonJunkBuf[3] = (byte) w;

            _write(_nonJunkBuf, 0, _nonJunkBufLen);

            _nonJunkActive = false;
            _nonJunkCount  = 0;
            _nonJunkBufLen = 0;
        }

        private void flushRun()
        {
            if (!_runActive)
                return;

            uint count = _runCount;
            if (_runType == _BlkJunk)
            {
                const uint _MaxCount = 0x3FFFFFFFu;
                while (count > 0)
                {
                    uint chunk = count > _MaxCount ? _MaxCount : count;
                    count -= chunk;
                    appendDescriptor((_BlkJunk << 30) | chunk);
                }
            }
            else // ByteFill (Scrub)
            {
                const uint _MaxCount = 0x3FFFFFu;
                while (count > 0)
                {
                    uint chunk = count > _MaxCount ? _MaxCount : count;
                    count -= chunk;
                    appendDescriptor((_BlkScrub << 30) | (chunk << 8) | (uint)_runFill);
                }
            }

            _runActive = false;
        }

        /// <summary>
        /// Append a 4-byte descriptor to either the pre-header buffer (if header not
        /// yet written) or directly to the stream.
        /// </summary>
        private void appendDescriptor(uint word)
        {
            if (!_headerWritten)
            {
                // Still possibly AllJunk/AllScrub — buffer instead of streaming.
                if (_preHdrLen + 4 > _preHdrBuf.Length)
                {
                    // Grow pre-header buffer (rare, only for degenerate images).
                    byte[] bigger = new byte[_preHdrBuf.Length * 2];
                    Array.Copy(_preHdrBuf, 0, bigger, 0, _preHdrLen);
                    _preHdrBuf = bigger;
                }
                _preHdrBuf[_preHdrLen    ] = (byte)(word >> 24);
                _preHdrBuf[_preHdrLen + 1] = (byte)(word >> 16);
                _preHdrBuf[_preHdrLen + 2] = (byte)(word >>  8);
                _preHdrBuf[_preHdrLen + 3] = (byte) word;
                _preHdrLen += 4;
            }
            else
            {
                writeWord(word);
            }
        }

        private void writeWord(uint word)
        {
            _desc[0] = (byte)(word >> 24);
            _desc[1] = (byte)(word >> 16);
            _desc[2] = (byte)(word >>  8);
            _desc[3] = (byte) word;
            _write(_desc, 0, 4);
        }

        // ── Block classification ──────────────────────────────────────────────────────

        private bool isJunkCoveredByRegion(long start, long end)
        {
            if (_classifiedRegions == null) return false;
            // Linear scan — list is small (one section's gap items, typically 1-4 entries)
            foreach (ClassifiedRegion r in _classifiedRegions)
            {
                if (r.Type == DataType.NJunk && r.DiscStart <= start && end <= r.DiscEnd)
                    return true;
            }
            return false;
        }

        private int classifyBlock(byte[] data, int offset, int length, long discOffset, int leadNulls)
        {
            if (_dolStart >= 0 && _dolEnd > 0
                && discOffset < _dolEnd && discOffset + length > _dolStart)
                return _ClassNonJunk;

            // Fast path: check pre-classified regions from section.Items (sorted by DiscStart).
            // Advance _classifiedIdx to skip past regions that end before this block.
            if (_classifiedRegions != null && _classifiedRegions.Count > 0)
            {
                long blockEnd = discOffset + length;
                // Skip past regions that end before this block (sequential access)
                while (_classifiedIdx < _classifiedRegions.Count
                       && _classifiedRegions[_classifiedIdx].DiscEnd <= discOffset)
                    _classifiedIdx++;

                if (_classifiedIdx < _classifiedRegions.Count)
                {
                    ClassifiedRegion r = _classifiedRegions[_classifiedIdx];
                    if (r.DiscStart <= discOffset && blockEnd <= r.DiscEnd)
                    {
                        if (r.Type == DataType.NJunk)  return _ClassJunk;
                        if (r.Type == DataType.Fill)   return _ClassScrub;
                        // DataType.Data or other — fall through to full scan
                    }
                }
            }

            long   blockStart = (discOffset / NJunk.JunkBlockSize) * NJunk.JunkBlockSize;
            bool   useBatch   = _junkBatchCache != null && _junkBatchCache.ContainsKey(blockStart);
            byte[] junk       = null;

            if (useBatch)
            {
                junk = _junkBatchCache[blockStart];
            }
            else
            {
                if (_junkCacheBlockStart != blockStart)
                {
                    NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, _junkCache);
                    _junkCacheBlockStart = blockStart;
                }
                junk = _junkCache;
            }

            int  junkOff    = (int)(discOffset - blockStart);
            bool isJunk     = true;
            int  leadRemain = leadNulls;
            byte fillByte   = data[offset];
            bool allSame    = true;

            for (int i = 0; i < length; i++)
            {
                if (junkOff + i >= NJunk.JunkBlockSize)
                {
                    blockStart += NJunk.JunkBlockSize;
                    if (useBatch && _junkBatchCache.ContainsKey(blockStart))
                    {
                        junk = _junkBatchCache[blockStart];
                    }
                    else
                    {
                        NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, _junkCache);
                        _junkCacheBlockStart = blockStart;
                        junk = _junkCache;
                        useBatch = false;
                    }
                    junkOff -= NJunk.JunkBlockSize;
                }

                byte actual   = data[offset + i];
                byte expected = junk[junkOff + i];

                if (actual != fillByte) allSame = false;

                if (leadRemain > 0)
                {
                    if (actual != 0x00) isJunk = false;
                    leadRemain--;
                }
                else if (isJunk && actual != expected)
                {
                    isJunk = false;
                }

                // Early exit once both classification flags are determined
                if (!isJunk && !allSame) break;
            }

            if (isJunk)  return _ClassJunk;
            if (allSame) return _ClassScrub;
            return _ClassNonJunk;
        }
    }
}
