using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nanook.NKit.Container
{
    // NKit gap-encoding format types. NKitAsIso is the sole decoder of NKit sources (raw .nkit /
    // .nkit.gcz are detected first in NKitInput's container chain and fully decoded up-front here),
    // so the format definition lives with the decoder. The former in-pipeline decoder (FsNKitFormat
    // / FsProcessor) was removed.
    internal enum GapType
    {
        AllJunk = 0b00,
        AllBlockFilled = 0b01,
        Mixed = 0b10,
        JunkFile = 0b11
    }

    internal enum GapBlockType
    {
        Junk = 0b00,
        NonJunk = 0b01,
        ByteFill = 0b10,
        Repeat = 0b11
    }

    /// <summary>
    /// Container that decodes NKit format and presents clean ISO data.
    /// 
    /// On first Read: caches the entire NKit source stream, parses the structure 
    /// (header, FST, file entries, gap encodings), calculates the true ISO offset
    /// for every file, and rebuilds the patched FST/header.
    /// 
    /// Subsequent reads serve data on demand from the cached source at the correct
    /// ISO positions. Junk gaps are filled with the correct disc junk pattern.
    /// NonJunk gap data (preserved bytes that didn't match junk) is served from
    /// the cached source. Scrubbed gaps are zeros or byte-filled.
    /// 
    /// Memory usage: ~source file size (cached NKit), plus small overhead for the
    /// offset map and rebuilt header/FST. NOT the full decoded image size.
    /// </summary>
    internal partial class NKitAsIso : Stream, IAsIso
    {
        // Size of a gap-encoding run block (Mixed gaps encode counts in units of this).
        public const long GapBlockSize = 0x100; //256

        private Stream _sourceStream;
        private long _position;         // virtual read position in the output ISO stream
        private long _imageSize;        // full decoded image size
        private bool _isWii;
        private bool _isGameCube;
        private string _nkitVersion;
        private uint _nkitCrc;
        private uint _nkitImageSize;
        private string _junkId;
        private uint _updatePartitionCrc;
        private Action<MetaData> _setRemovedBlock;

        // True when the source NKit had its update partition removed. NKitAsIso reinserts the
        // update partition itself (from a recovery file supplied via the recovery resolver) so the
        // decoded image is complete; if no recovery file is supplied the region is null-filled.
        public bool NKitUpdateRemoved => _updatePartitionCrc != 0;

        // CRC32 of the removed update partition (from the NKit header at 0x218); used by the caller
        // to locate the matching recovery file. 0 when the update partition is retained.
        public uint NKitUpdatePartitionCrc => _updatePartitionCrc;

        // True when the decoded source is a Wii image (set during Construct).
        public bool IsWii => _isWii;

        // True when the decoded source is a GameCube image (set during Construct).
        public bool IsGameCube => _isGameCube;

        // One-line format summary emitted once per source at Detail level (see IAsIso.FormatSummary).
        public string FormatSummary =>
            $"{Nanook.NKit.LogScopes.Tag(Nanook.NKit.LogScopes.NKit)}{_nkitVersion} {(_isWii ? "Wii" : _isGameCube ? "GC" : "?")} decoded 0x{_imageSize:X}"
            + $" srcCrc {_nkitCrc:X8} junkId '{_junkId}'"
            + $" updRemoved:{(_updatePartitionCrc != 0 ? $"y(crc {_updatePartitionCrc:X8})" : "n")}";

        // Recovery-partition lookup. NKitAsIso owns the update-reinsertion policy (whether it is
        // needed, when to log) and performs the lookup against FixData (which owns the recovery-
        // file naming/scan rules via FindUpdatePartition). The Wii domain layer (WiiGc.Image),
        // which resolves the targeted FixData, supplies it here (plus a logger) before the first
        // read that triggers the parse.
        private FixData _recoveryFixData;
        private Action<string> _recoveryLog;

        // Resolved recovery update-partition file to reinsert (populated during parse from the
        // recovery folder scan). Null => the update region is null-filled.
        private string _updatePartitionFile;
        private long _updatePartitionFileLen;
        private string _updatePartitionDisplayName;
        private System.IO.FileStream _updatePartitionStream; // lazy, opened on first read of the region
        private bool _updatePartitionUsed;                   // guards the one-time first-use log
        // Output offset range [start,end) of the reinserted update partition, so the downstream
        // reader can mark it as already-encrypted+hashed (pass-through, not re-encrypted).
        private long _insertedUpdateStart = -1;
        private long _insertedUpdateEnd = -1;

        // Wire the (already targeted) recovery FixData and logger. Call before the first Read
        // (which triggers the structure parse). NKitAsIso asks FixData for the recovery partition
        // (basic CRC lookup) when it decodes an update-removed image, and logs (via log) at first
        // read of the region.
        public void SetRecoveryFixData(FixData fixData, Action<string> log)
        {
            _recoveryFixData = fixData;
            _recoveryLog = log;
        }

        // True if the given OUTPUT offset falls within a reinserted, already-encrypted update
        // partition (so downstream must pass it through unchanged, not re-encrypt/re-hash).
        public bool IsInsertedEncryptedPartition(long imageOffset) =>
            _insertedUpdateStart >= 0 && imageOffset >= _insertedUpdateStart && imageOffset < _insertedUpdateEnd;

        // True if the 0x200000 group at block-space offset `areaOffset` within the Wii partition
        // that starts at output offset `partitionImageOffset` has PRESERVED (non-recreatable)
        // hashes. NKit read reproduces these verbatim (readWiiPartition fills the hash gaps), so
        // the SectionProcessor must NOT regenerate hashes for such a group. Returns false when the
        // offset is not a decoded Wii partition or the group has recreatable hashes.
        public bool IsPreservedHashGroup(long partitionImageOffset, long areaOffset)
        {
            if (_segments == null)
                return false;
            int idx = findSegment(partitionImageOffset);
            if (idx < 0 || idx >= _segments.Count)
                return false;
            Segment seg = _segments[idx];
            return seg.Type == SegmentType.WiiPartition && (seg.Map?.IsGroupPreserved(areaOffset) ?? false);
        }

        // Cached source data — read once from stream, never re-read.
        // _src is the byte[] used for parse-time control reads (gap words, header fields);
        // the compact NKit source is well under 2 GiB in practice so a single array is safe.
        private byte[] _src;

        // Large-capacity buffered source for on-demand Wii data reads. Holds the same bytes as
        // _src but is not limited by the ~2 GiB single-array ceiling. The Wii partition decode
        // map records long source offsets into this store; Read() pulls file/preserved bytes
        // from here on demand so partitions are never expanded into memory.
        private NKitStream.SegmentedMemoryStream _srcStore;
        private long _srcLength;        // logical length of the buffered Wii source (_srcStore)

        // ── long-indexed accessors over the buffered Wii source (_srcStore) ──────
        // The Wii parse reads only small control regions and copies small header pieces; these
        // helpers replace the byte[] _src indexing so the compact source may exceed 2 GiB.

        /// <summary>Read a big-endian uint32 from the buffered Wii source at a long offset.</summary>
        private uint srcReadU32B(long offset)
        {
            Span<byte> b = stackalloc byte[4];
            _srcStore.Position = offset;
            int got = 0;
            while (got < 4)
            {
                int r = _srcStore.Read(b.Slice(got));
                if (r <= 0) break;
                got += r;
            }
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        /// <summary>Copy <paramref name="count"/> bytes from the buffered Wii source into a byte[].</summary>
        private void srcCopy(long srcOffset, byte[] dst, int dstOffset, int count)
        {
            _srcStore.Position = srcOffset;
            int got = 0;
            while (got < count)
            {
                int r = _srcStore.Read(dst, dstOffset + got, count - got);
                if (r <= 0) { Array.Clear(dst, dstOffset + got, count - got); break; }
                got += r;
            }
        }

        // Rebuilt output segments: header (with NKit fields cleared) + patched FST
        private byte[] _hdr;            // 0x440 bytes — output disc header
        private byte[] _hdrToFst;       // bi2, apploader, dol, padding up to FST
        private byte[] _fst;            // patched FST with correct output offsets

        // Offset map: sorted list of segments describing the output ISO layout
        private List<Segment> _segments;
        private bool _parsed;

        // Streaming (non-caching) mode: decode forward without caching the full source.
        // Reads the source strictly sequentially (no seeking). The FST and header are
        // patched in-memory as file positions become known; once the full image has
        // been read, HeaderBlock returns the patched header + hdrToFst + FST region
        // for the caller to write over the output start.
        private bool _streaming;
        private byte[] _headerBlock;    // patched header+hdrToFst+fst, available after full read
        private bool _streamComplete;

        // Forward-only streaming decoder state
        private List<IFsFile> _sFiles;      // parsed FST files
        private int _sFileIdx;              // next file index to process
        private long _sDstPos;              // current output position
        private long _sNullsPos;            // nulls tracking (v1)
        private long _sMainDolAddr;         // original dol address for patching
        private long _sImageEnd;            // == _imageSize
        private bool _sBeforeFirstFile;     // still need to process FST->first-file gap
        private System.Collections.Generic.Queue<EmitOp> _sQueue; // pending output ops

        // Junk generation parameters
        private byte[] _junkIdBytes;    // 4-byte disc ID for junk generation
        private int _discNo;            // disc number (byte at 0x06)
        private byte[] _junkBlock;      // reusable 0x40000-byte buffer for junk generation
        private long _junkBlockStart = -1; // isoOffset of the block currently in _junkBlock (disc junk)
        private bool _junkBlockValid;   // true when _junkBlock holds a valid disc-junk block
        private byte[] _junkBlockId;    // junk id the cached _junkBlock was generated with
        private long _junkBlockStartOffset; // startOffset the cached _junkBlock was generated with

        #region Segment Types

        private enum SegmentType
        {
            Data,       // raw data from _src at a given source offset
            Header,     // data from _hdr
            HdrToFst,   // data from _hdrToFst
            Fst,        // data from _fst
            Zero,       // gap filled with zeros (scrubbed)
            Fill,       // gap filled with a specific byte value
            Junk,       // gap filled with generated disc junk pattern
            Buffer,     // pre-built block-space data (small: Wii partition header block)
            WiiPartition, // on-demand block-space partition data (FS run map + hash gaps)
            WiiSrcData, // raw bytes copied on demand from the buffered Wii source (_srcStore)
            UpdateFile  // raw bytes read on demand from the reinserted update-partition recovery file
        }

        private struct Segment
        {
            public long IsoOffset;      // where this segment starts in the output ISO
            public long Length;          // how many bytes
            public SegmentType Type;
            public int SrcOffset;       // offset into _src (for Data), or _hdr/_hdrToFst/_fst (for those types)
            public byte FillByte;       // for Fill type
            public byte[] Buffer;       // for Buffer type: pre-built block-space bytes (small)
            public WiiPartitionMap Map; // for WiiPartition type: on-demand FS run map
            public long SrcOffsetL;     // for WiiSrcData: long offset into the buffered Wii source

            // For Junk type only: the disc-level junk context resolved when the segment was built.
            // The trailing filler after a Game partition uses THAT partition's boot JunkId and its
            // JunkStartFsOffset (matching SectionProcessor.createJunk's post-Game 'Other' rule),
            // not the disc-header id. Null JunkIdOverride => use the disc-level _junkIdBytes/0.
            public byte[] JunkIdOverride; // 4-byte junk id for this junk segment (else disc id)
            public long JunkStartOffset;  // startOffset arg for NJunk.Fill (else 0)

            public long IsoEnd => IsoOffset + Length;
        }

        // ── Wii on-demand partition decode map ──────────────────────────────────
        // A partition's block-space data is produced live from these FS-space runs; the
        // partition is never expanded into a buffer. Each run says how to generate a
        // contiguous range of FS-space bytes; block-space output inserts a 0x400 zero hash
        // gap at the start of every 0x8000 sector (hashing/encryption happen downstream).

        private enum WiiFsRunKind
        {
            Data,   // copy FsLength bytes from the buffered source at SrcOffset
            Junk,   // generate disc junk (FS-space, partition JunkId/DiscNo/FsSize)
            Zero,   // zero fill
            Fill    // constant byte fill (FillByte)
        }

        private struct WiiFsRun
        {
            public long FsOffset;   // start within the partition's contiguous FS space
            public long FsLength;   // length in FS space
            public WiiFsRunKind Kind;
            public long SrcOffset;  // for Data: offset into the buffered source
            public byte FillByte;   // for Fill
        }

        private sealed class WiiPartitionMap
        {
            public long PartFsSize;         // contiguous FS size of the partition
            public byte[] JunkId;           // 4-byte partition boot id for FS-space junk
            public int DiscNo;              // partition disc no (boot[6])
            public List<WiiFsRun> Runs;     // sorted, contiguous, cover [0, PartFsSize)
            // Small overlay written over the very start of the partition FS (patched
            // sub-header + hdrToFst + FST). Overrides run data for offsets it covers.
            public byte[] HeadOverlay;      // FS-space bytes at offset 0
            public int LastRunIndex;        // cache for near-sequential run lookups in Read()
            // FS offset where junk begins (FstOffset+FstSize+DataNullsCount). Passed to
            // NJunk.Fill as startOffset so it zeros the pre-junk leading portion of the first
            // junk block, matching SectionProcessor.createJunk exactly.
            public long JunkStartFsOffset;

            // Preserved (non-recreatable) hashes for this partition, reproduced verbatim on read.
            // NKit read must output the disc EXACTLY as stored, so groups whose hashes could not be
            // recreated at encode time have their raw 0x400-per-sector hash bytes stored here and
            // copied back into the block-space hash gaps (rather than regenerated). Layout mirrors
            // WiiHashStore: PreservedHashes is the packed per-sector hash bytes for preserved groups
            // (ascending); PreservedHashMap maps a group's block-space (hashed) offset -> byte
            // offset into PreservedHashes. Empty/null when no group is preserved.
            public byte[] PreservedHashes;
            public Dictionary<long, long> PreservedHashMap;

            public bool IsGroupPreserved(long blockGroupOffset) =>
                PreservedHashMap != null && PreservedHashMap.ContainsKey(blockGroupOffset);
        }

        // Forward-only streaming emit operation. Either literal bytes (Data holds them
        // already read from the source) or a generated fill (Junk/Zero/Fill of Length).
        private struct EmitOp
        {
            public SegmentType Type;    // Data (literal), Junk, Zero, Fill
            public byte[] Data;         // for Data: the literal bytes
            public int DataOffset;      // consumed offset within Data
            public long Length;         // remaining length to emit
            public long IsoOffset;      // output offset where this op begins (for junk gen)
            public byte FillByte;       // for Fill
        }

        #endregion

        #region Static Factory

        /// <summary>
        /// Detect NKit format from the header bytes. Returns null if not NKit.
        /// Supports both .nkit.iso (raw NKit) and .nkit.gcz (GCZ-compressed NKit).
        /// </summary>
        public static IAsIso Create(byte[] header) => Create(header, false);

        /// <summary>
        /// Detect NKit format. If <paramref name="streaming"/> is true, the decoder
        /// runs in non-caching mode: it decodes the source forward without caching the
        /// full source in memory, patching the FST/header as file positions are found.
        /// After the full image is read, <see cref="HeaderBlock"/> exposes the patched
        /// header + hdrToFst + FST region to write over the output start.
        /// </summary>
        public static IAsIso Create(byte[] header, bool streaming)
        {
            // Raw NKit is a disc image with "NKIT" at 0x200 in the disc header. A WBFS that holds
            // an NKit-encoded image ALSO has an NKit disc-header copy at 0x200 (e.g. an NKit
            // GameCube image reads "NKITGC" there), but the file is a WBFS container and must be
            // decoded by WbfsAsIso (which reconstructs the NKit junk internally). Do NOT claim a
            // WBFS here — "WBFS" magic at offset 0 takes precedence over the 0x200 NKit marker.
            if (header.ReadString(0, 4) != "WBFS")
            {
                // Check for raw NKit: "NKIT" at offset 0x200 in disc header
                string id = header.ReadString(0x200, 4);
                if (id == "NKIT")
                    return new NKitAsIso() { _streaming = streaming };
            }

            // Check for NKit inside GCZ: GCZ magic 0x01C00BB1 (little-endian 0xB10BC001)
            if (header.ReadUInt32L(0) == 0xB10BC001)
                return new NKitAsIso() { _isGcz = true, _streaming = streaming };

            return null;
        }

        #endregion

        private bool _isGcz;
        private GczAsIso _gczStream; // decompressor when source is .nkit.gcz

        /// <summary>
        /// In streaming mode, once the full image has been read, this returns the
        /// patched header block (disc header + hdr-to-FST + FST) with all file offsets
        /// and sizes corrected. Write this over the start of the output stream.
        /// Returns null if not in streaming mode or the read is not yet complete.
        /// </summary>
        public byte[] HeaderBlock => (_streaming && _streamComplete) ? _headerBlock : null;

        /// <summary>True when running in non-caching streaming mode.</summary>
        public bool IsStreaming => _streaming;

        private NKitAsIso()
        {
            _parsed = false;
        }

        public int Construct(Stream stream, bool allowSeek)
        {
            // If source is GCZ, set up the decompressor first
            if (_isGcz)
            {
                _gczStream = new GczAsIso();
                _gczStream.Construct(stream, allowSeek);
                _sourceStream = _gczStream; // read NKit data from the decompressed stream
            }
            else
            {
                _sourceStream = stream;
            }

            // Read the disc header to get NKit metadata
            byte[] discHeader = new byte[0x440];
            _sourceStream.Read(discHeader, 0, discHeader.Length);
            _sourceStream.Position = 0;

            _nkitVersion = discHeader.ReadString(0x200, 8);
            if (_nkitVersion != "NKIT v01")
                throw new HandledException($"{_nkitVersion} not supported by this version");

            _nkitCrc = discHeader.ReadUInt32B(0x208);
            _nkitImageSize = discHeader.ReadUInt32B(0x210);
            _junkId = discHeader.ReadString(0x214, 4);
            _updatePartitionCrc = discHeader.ReadUInt32B(0x218);

            _isGameCube = discHeader.ReadUInt32B(0x1c) == 0xc2339f3d;
            _isWii = discHeader.ReadUInt32B(0x18) == 0x5d1c9ea3;

            // Wii must always decode cached: the partition has to be read in full to obtain the
            // preserved-hash bitmask and data, so forward-only streaming is not supported.
            if (_isWii)
                _streaming = false;

            _imageSize = _nkitImageSize * (_isWii ? 4L : 1L);

            // Expose the stored source (original full-disc) CRC and size the way RVZ/CISO/CSO
            // sources do — via Checksums / CustomChecksums() / NKitHeader — so Verify can compare
            // the decoded output against them (InChecksums) without relying on the IsNkit flag.
            this.Checksums = new Checksums { Size = _imageSize };
            this.Checksums.Crc = _nkitCrc;
            NKitHeader hdr = new NKitHeader(1, true, true, false, false, false, HeaderKeyType.None, 0, false, false, false)
            {
                Size = _imageSize
            };
            hdr.Checksums.Crc = _nkitCrc;
            this.NKitHeader = hdr;

            _position = 0;
            return 0;
        }

        #region Parsing — Cache Source and Build Offset Map

        /// <summary>
        /// Cache the source stream and parse the NKit structure to build the offset map.
        /// Called lazily on first Read().
        /// </summary>
        private void ParseStructure()
        {
            if (_parsed) return;

            if (_isGameCube)
            {
                // GameCube compact sources are well under 2 GiB — a single byte[] cache is fine
                // and the GC parse/Read paths index it directly. EnsureGameCubeHeader may already
                // have allocated _src and read its front (header region) — read only the REMAINDER
                // forward (the source is forward-only; it cannot be rewound). When the header fast
                // path was not used, read the whole source from 0 as before.
                if (_src == null)
                {
                    _src = new byte[_sourceStream.Length];
                    _sourceStream.Position = 0;
                    _sourceStream.Read(_src, 0, _src.Length);
                }
                else if (_gcSrcFilled < _src.Length)
                {
                    readExactSrc(_gcSrcFilled, (int)(_src.Length - _gcSrcFilled));
                    _gcSrcFilled = _src.Length;
                }
                ParseGameCube();
            }
            else if (_isWii)
            {
                // The compact Wii source can exceed the ~2 GiB single-array ceiling (e.g. a
                // GCZ-compressed Wii NKit whose decompressed .nkit is > 2 GiB). Buffer it into
                // the segmented store; the parse and on-demand Read() both address it via long
                // offsets, so no giant byte[] is allocated.
                _srcStore = new NKitStream.SegmentedMemoryStream();
                _sourceStream.Position = 0;
                _srcStore.FillFrom(_sourceStream, _sourceStream.Length);
                _srcLength = _sourceStream.Length;
                parseWii();
            }
            else
                throw new HandledException("NKit format: unable to determine system type");

            _parsed = true;
        }

        // ── Forward-only streaming decoder ──────────────────────────────────────
        // Reads the source strictly sequentially. No seeking. Emits output on demand.

        /// <summary>Sequentially read exactly <paramref name="length"/> bytes from the source.</summary>
        private byte[] readSeq(int length)
        {
            byte[] b = new byte[length];
            int read = 0;
            while (read < length)
            {
                int r = _sourceStream.Read(b, read, length - read);
                if (r <= 0) break;
                read += r;
            }
            return b;
        }

        private void InitStreaming()
        {
            if (_parsed) return;
            if (!_isGameCube)
                throw new NotImplementedException("NKitAsIso streaming: only GameCube supported");

            _sQueue = new System.Collections.Generic.Queue<EmitOp>();
            _sImageEnd = _imageSize;
            _sourceStream.Position = 0;

            // 1. Disc header (0x440) — read sequentially, clear NKit fields
            _hdr = readSeq(WiiConsts.BootBinSize);
            Array.Clear(_hdr, 0x200, WiiConsts.NKitHeaderSize);

            if (_junkId != null && _junkId != "\0\0\0\0")
                _junkIdBytes = Encoding.ASCII.GetBytes(_junkId);
            else
                _junkIdBytes = new byte[] { _hdr[0], _hdr[1], _hdr[2], _hdr[3] };
            _discNo = _hdr[6];
            _junkBlock = new byte[NJunk.JunkBlockSize];

            _sMainDolAddr = _hdr.ReadUInt32B(WiiConsts.DolPtrOffset);
            long fstOffset = _hdr.ReadUInt32B(WiiConsts.FstPtrOffset);
            int fstSize = (int)_hdr.ReadUInt32B(WiiConsts.FstSizeOffset);
            int fstSizeAligned = fstSize + (fstSize % 4 == 0 ? 0 : 4 - (fstSize % 4));

            // 2. Hdr-to-FST (sequential)
            int hdrToFstSize = (int)(fstOffset - _hdr.Length);
            _hdrToFst = readSeq(hdrToFstSize);

            // 3. FST (sequential)
            _fst = readSeq(fstSizeAligned);

            // Track source position sequentially (header + hdrToFst + fst consumed)
            _streamSrcPos = _hdr.Length + hdrToFstSize + fstSizeAligned;

            // Parse FST file list
            Fst parsedFst = Fst.Parse(_fst, 0, null, 1L);
            if (parsedFst == null || parsedFst.Files == null || parsedFst.Files.Count == 0)
                throw new HandledException("NKitAsIso: unable to parse FST");
            _sFiles = parsedFst.Files;

            // Emit header + hdrToFst + FST as the first output (FST offsets get patched
            // in-memory as files are positioned; the final patched copy is in HeaderBlock)
            _sDstPos = 0;
            enqueueData(_hdr, 0);
            enqueueData(_hdrToFst, 0);
            enqueueData(_fst, 0);   // note: _fst is patched in-place during the walk

            _sNullsPos = _sDstPos + WiiConsts.DataNullsCount;
            _sFileIdx = 0;
            _sBeforeFirstFile = true;

            _parsed = true;
        }

        private long _streamSrcPos; // sequential source position (monotonic)

        private void enqueueData(byte[] data, int dataOffset)
        {
            long len = data.Length - dataOffset;
            if (len <= 0) return;
            _sQueue.Enqueue(new EmitOp { Type = SegmentType.Data, Data = data, DataOffset = dataOffset, Length = len, IsoOffset = _sDstPos });
            _sDstPos += len;
        }
        private void enqueueGenerated(SegmentType type, long length, byte fillByte)
        {
            if (length <= 0) return;
            _sQueue.Enqueue(new EmitOp { Type = type, Length = length, IsoOffset = _sDstPos, FillByte = fillByte });
            _sDstPos += length;
        }

        /// <summary>
        /// Advance the decoder one step, reading the next chunk from the source
        /// sequentially and enqueueing output ops. Returns false when fully done.
        /// </summary>
        private bool advanceStreaming()
        {
            if (_sDstPos >= _sImageEnd)
                return false;

            // Process the pre-first-file gap once
            if (_sBeforeFirstFile)
            {
                _sBeforeFirstFile = false;
                FstFile first = (FstFile)_sFiles[0];
                long firstGapLen = first.FsOffset - _streamSrcPos;
                if (firstGapLen > 0)
                {
                    streamGap(first, true, firstGapLen);
                    return true;
                }
                // fall through to file loop if no pre-gap
            }

            if (_sFileIdx < _sFiles.Count)
            {
                FstFile ff = (FstFile)_sFiles[_sFileIdx];
                bool isLast = _sFileIdx == _sFiles.Count - 1;

                // Skip alignment padding by reading and discarding it sequentially
                if (_streamSrcPos < ff.FsOffset)
                {
                    int skip = (int)(ff.FsOffset - _streamSrcPos);
                    readSeq(skip); // discard
                    _streamSrcPos += skip;
                }

                if (ff.FsOffset == _sMainDolAddr)
                    _hdr.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)_sDstPos);

                _fst.WriteUInt32B(ff.FstPtrOffset, (uint)_sDstPos);

                long fileSize = ff.FsSize;
                if (fileSize > 0)
                {
                    long alignedSize = fileSize + (fileSize % 4 == 0 ? 0 : 4 - (fileSize % 4));
                    alignedSize = Math.Min(alignedSize, _sImageEnd - _sDstPos);
                    int toCopy = (int)alignedSize;
                    if (toCopy > 0)
                    {
                        byte[] fileData = readSeq(toCopy);
                        _streamSrcPos += toCopy;
                        enqueueData(fileData, 0);
                    }
                    _sNullsPos = _sDstPos + WiiConsts.DataNullsCount;
                }

                // Gap after this file
                long nkitGapLen;
                if (isLast)
                    nkitGapLen = _sourceStream.Length - _streamSrcPos;
                else
                    nkitGapLen = ((FstFile)_sFiles[_sFileIdx + 1]).FsOffset - _streamSrcPos;

                if (nkitGapLen > 0 && _sDstPos < _sImageEnd)
                    streamGap(ff, isLast, nkitGapLen);
                else if (ff.FsSize == 0)
                    _sNullsPos = _sDstPos + WiiConsts.DataNullsCount;

                _sFileIdx++;
                return true;
            }

            // Final gap after last file
            long finalGapLen = _sourceStream.Length - _streamSrcPos;
            if (finalGapLen > 0 && _sDstPos < _sImageEnd)
            {
                streamGap(null, true, finalGapLen);
                if (_sDstPos < _sImageEnd)
                    enqueueGenerated(SegmentType.Zero, _sImageEnd - _sDstPos, 0);
                return true;
            }

            // Pad remaining with zeros
            if (_sDstPos < _sImageEnd)
            {
                enqueueGenerated(SegmentType.Zero, _sImageEnd - _sDstPos, 0);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Decode a single gap, reading gap encoding words sequentially from the source
        /// and enqueueing output ops. Mirrors parseGcGap's logic exactly.
        /// </summary>
        private void streamGap(FstFile file, bool firstOrLastFile, long nkitGapLen)
        {
            if (nkitGapLen == 0)
            {
                if (file != null && file.FsSize == 0)
                    _sNullsPos = _sDstPos + WiiConsts.DataNullsCount;
                return;
            }

            long size = readSeq(4).ReadUInt32B(0);
            _streamSrcPos += 4;
            GapType gt = (GapType)(size & 0b11);
            size &= 0xFFFFFFFC;

            if (size == 0xFFFFFFFC) // Wii only
            {
                size = 0xFFFFFFFCL + readSeq(4).ReadUInt32B(0);
                _streamSrcPos += 4;
            }

            long junkFileLen = 0;

            if (gt == GapType.JunkFile)
            {
                _sNullsPos = _sDstPos + Math.Min(_sNullsPos - _sDstPos, 0);

                long nulls = (size & 0xFC) >> 2;
                junkFileLen = readSeq(4).ReadUInt32B(0);
                _streamSrcPos += 4;

                if (file != null)
                    _fst.WriteUInt32B(file.FstPtrOffset + 4, (uint)junkFileLen);

                long junkFileLenAligned = junkFileLen + (junkFileLen % 4 == 0 ? 0 : 4 - (junkFileLen % 4));

                if (junkFileLenAligned > 0)
                {
                    if (nulls > 0)
                        enqueueGenerated(SegmentType.Zero, nulls, 0);
                    long junkPart = junkFileLenAligned - nulls;
                    if (junkPart > 0)
                        enqueueGenerated(SegmentType.Junk, junkPart, 0);
                }

                if (nkitGapLen <= 8)
                    return;

                size = readSeq(4).ReadUInt32B(0);
                _streamSrcPos += 4;
                gt = (GapType)(size & 0b11);
                size &= 0xFFFFFFFC;
            }
            else if (file != null && file.FsSize == 0)
            {
                _sNullsPos = _sDstPos + WiiConsts.DataNullsCount;
            }

            if (size == 0)
                return;

            long maxNulls = Math.Max(0, _sNullsPos - _sDstPos);
            long gapNulls;
            if (size < maxNulls)
                gapNulls = size;
            else
                gapNulls = size >= 0x40000 && !firstOrLastFile ? 0 : maxNulls;

            if (gt == GapType.AllJunk)
            {
                if (gapNulls > 0)
                    enqueueGenerated(SegmentType.Zero, gapNulls, 0);
                long junkPart = size - gapNulls;
                if (junkPart > 0)
                    enqueueGenerated(SegmentType.Junk, junkPart, 0);
            }
            else if (gt == GapType.AllBlockFilled)
            {
                enqueueGenerated(SegmentType.Zero, size, 0);
            }
            else // GapType.Mixed
            {
                long prg = size;
                byte btByte = 0x00;
                GapBlockType bt = GapBlockType.Junk;

                while (prg > 0)
                {
                    long blk = readSeq(4).ReadUInt32B(0);
                    _streamSrcPos += 4;

                    GapBlockType btType = (GapBlockType)(blk >> 30);
                    bool btRepeat = btType == GapBlockType.Repeat;
                    if (!btRepeat)
                        bt = btType;

                    long cnt = 0x3FFFFFFF & blk;
                    long bytes;

                    if (bt == GapBlockType.NonJunk)
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        int toCopy = (int)bytes;
                        if (toCopy > 0)
                        {
                            byte[] d = readSeq(toCopy);
                            _streamSrcPos += toCopy;
                            enqueueData(d, 0);
                        }
                    }
                    else if (bt == GapBlockType.ByteFill)
                    {
                        if (!btRepeat)
                        {
                            btByte = (byte)(0xFF & cnt);
                            cnt >>= 8;
                        }
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        if (btByte == 0x00)
                            enqueueGenerated(SegmentType.Zero, bytes, 0);
                        else
                            enqueueGenerated(SegmentType.Fill, bytes, btByte);
                    }
                    else // Junk
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        maxNulls = Math.Max(0, _sNullsPos - _sDstPos);
                        long localNulls;
                        if (prg < maxNulls)
                            localNulls = bytes;
                        else
                            localNulls = bytes >= 0x40000 && !firstOrLastFile ? 0 : Math.Min(maxNulls, bytes);

                        if (localNulls > 0)
                            enqueueGenerated(SegmentType.Zero, localNulls, 0);
                        long junkBytes = bytes - localNulls;
                        if (junkBytes > 0)
                            enqueueGenerated(SegmentType.Junk, junkBytes, 0);
                    }

                    prg -= bytes;
                }
            }
        }

        private void finalizeStreaming()
        {
            if (_headerBlock != null) return;
            _headerBlock = new byte[_hdr.Length + _hdrToFst.Length + _fst.Length];
            Array.Copy(_hdr, 0, _headerBlock, 0, _hdr.Length);
            Array.Copy(_hdrToFst, 0, _headerBlock, _hdr.Length, _hdrToFst.Length);
            Array.Copy(_fst, 0, _headerBlock, _hdr.Length + _hdrToFst.Length, _fst.Length);
        }

        // GameCube header size cached by ParseGameCubeHeader so the full parse (ParseGameCube)
        // reuses the same head extent (0x440 boot header + hdrToFst + aligned FST).
        private int _gcHeadEnd;
        private bool _gcHeaderParsed;
        private int _gcSrcFilled; // bytes of _src filled so far via the GC header fast path

        // Read exactly count bytes from the (forward-only) source into _src at the given offset.
        private void readExactSrc(long srcOffset, int count)
        {
            int got = 0;
            while (got < count)
            {
                int r = _sourceStream.Read(_src, (int)srcOffset + got, count - got);
                if (r <= 0)
                    break;
                got += r;
            }
        }

        // GameCube fast path: read ONLY the front of the source (boot header + hdrToFst + FST) and
        // build the decoded header + header segments, without the whole-source read or the file/gap
        // walk. Allocates _src full-size but fills only the head; ParseStructure fills the remainder
        // forward when the full parse is later triggered by a read into file data.
        private void EnsureGameCubeHeader()
        {
            if (_gcHeaderParsed)
                return;

            if (_src == null)
                _src = new byte[_sourceStream.Length];

            _sourceStream.Position = 0;

            // Read the 0x440 boot header to learn the FST location/size.
            readExactSrc(0, WiiConsts.BootBinSize);
            long fstOffset = _src.ReadUInt32B(WiiConsts.FstPtrOffset);
            int fstSize = (int)_src.ReadUInt32B(WiiConsts.FstSizeOffset);
            int fstSizeAligned = fstSize + (fstSize % 4 == 0 ? 0 : 4 - (fstSize % 4));
            int headEnd = (int)fstOffset + fstSizeAligned;

            // Read the remainder of the head region (hdrToFst + FST), then build the header.
            readExactSrc(WiiConsts.BootBinSize, headEnd - WiiConsts.BootBinSize);
            _gcSrcFilled = headEnd;

            ParseGameCubeHeader();
        }

        // Phase A (cheap, header only). Builds the decoded disc header (_hdr), hdrToFst and FST and
        // emits the three header segments — enough to serve the IImage's construction-time header
        // read (0x440) WITHOUT the full file/gap walk (ParseGameCube) that reads the whole compact
        // source. The FST offsets and the main.dol pointer are patched later by ParseGameCube (the
        // walk); the IImage only reads identification fields (Id/Title/Region/DiscNo) from this
        // header at construction, so the pre-patch header is correct for that use. Reads only the
        // front of the source (up to the FST end).
        private void ParseGameCubeHeader()
        {
            if (_gcHeaderParsed)
                return;

            _segments = new List<Segment>();

            // 1. Disc header (0x440) — copy, clear NKit fields
            _hdr = new byte[WiiConsts.BootBinSize];
            Array.Copy(_src, 0, _hdr, 0, _hdr.Length);
            int srcPos = _hdr.Length;

            Array.Clear(_hdr, 0x200, WiiConsts.NKitHeaderSize);

            if (_junkId != null && _junkId != "\0\0\0\0")
                _junkIdBytes = Encoding.ASCII.GetBytes(_junkId);
            else
                _junkIdBytes = new byte[] { _hdr[0], _hdr[1], _hdr[2], _hdr[3] };
            _discNo = _hdr[6];
            _junkBlock = new byte[NJunk.JunkBlockSize];

            long fstOffset = _hdr.ReadUInt32B(WiiConsts.FstPtrOffset);
            int fstSize = (int)_hdr.ReadUInt32B(WiiConsts.FstSizeOffset);
            int fstSizeAligned = fstSize + (fstSize % 4 == 0 ? 0 : 4 - (fstSize % 4));

            // 2. Hdr-to-FST (bi2, apploader, dol, padding)
            int hdrToFstSize = (int)(fstOffset - _hdr.Length);
            _hdrToFst = new byte[hdrToFstSize];
            Array.Copy(_src, srcPos, _hdrToFst, 0, hdrToFstSize);
            srcPos += hdrToFstSize;

            // 3. FST — will be patched with correct output offsets by ParseGameCube
            _fst = new byte[fstSizeAligned];
            Array.Copy(_src, srcPos, _fst, 0, fstSizeAligned);
            srcPos += fstSizeAligned;
            _gcHeadEnd = srcPos;

            // 4. Emit header segments (Header/HdrToFst/Fst reference _hdr/_hdrToFst/_fst by kind;
            // ParseGameCube patches _hdr/_fst in place so the emitted segments pick up the patches).
            long dstPos = 0;
            _segments.Add(new Segment { IsoOffset = dstPos, Length = _hdr.Length, Type = SegmentType.Header, SrcOffset = 0 });
            dstPos += _hdr.Length;
            _segments.Add(new Segment { IsoOffset = dstPos, Length = _hdrToFst.Length, Type = SegmentType.HdrToFst, SrcOffset = 0 });
            dstPos += _hdrToFst.Length;
            _segments.Add(new Segment { IsoOffset = dstPos, Length = _fst.Length, Type = SegmentType.Fst, SrcOffset = 0 });

            _gcHeaderParsed = true;
        }

        private void ParseGameCube()
        {
            // Phase A: header + segments (idempotent). Rebuilds _segments with the 3 header
            // segments; the walk below appends the data/gap segments.
            ParseGameCubeHeader();

            int srcPos = _gcHeadEnd;
            long dstPos = _hdr.Length + _hdrToFst.Length + _fst.Length;

            long mainDolAddr = _hdr.ReadUInt32B(WiiConsts.DolPtrOffset);

            //──────────────────────────────────────────────────────────────────
            // 5. Parse FST file list
            //──────────────────────────────────────────────────────────────────
            Fst parsedFst = Fst.Parse(_fst, 0, null, 1L);
            if (parsedFst == null || parsedFst.Files == null || parsedFst.Files.Count == 0)
                throw new HandledException("NKitAsIso: unable to parse FST");

            List<IFsFile> files = parsedFst.Files;

            //──────────────────────────────────────────────────────────────────
            // 6. Walk files + gaps, building segment map and patching FST
            //    NKit format: gap encoding is ALWAYS present after FST and after
            //    every file. The encoding specifies how much output to generate.
            //    Files are packed sequentially in the source with gap encodings
            //    between them.
            //──────────────────────────────────────────────────────────────────
            long nullsPos = dstPos + WiiConsts.DataNullsCount;

            // First: parse the gap between FST end and first file
            long firstGapLen = (long)((FstFile)files[0]).FsOffset - srcPos;
            if (firstGapLen > 0 && dstPos < _imageSize)
                dstPos = parseGcGap(ref srcPos, dstPos, ref nullsPos, (FstFile)files[0], true, firstGapLen);

            // Then process each file sequentially: copy data, parse gap after
            // Gap encoding is only present when there's space between files in the NKit source
            for (int i = 0; i < files.Count; i++)
            {
                FstFile ff = (FstFile)files[i];
                bool isLast = i == files.Count - 1;

                // Skip alignment padding in the NKit source between gap encoding and file data
                // (the NKit preserves 32K alignment padding as zero bytes in the source)
                if (srcPos < (int)ff.FsOffset)
                    srcPos = (int)ff.FsOffset;

                // Patch main.dol pointer if this file was at the original dol address
                if (ff.FsOffset == mainDolAddr)
                    _hdr.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)dstPos);

                // Patch FST offset for this file to its decoded output position
                _fst.WriteUInt32B(ff.FstPtrOffset, (uint)dstPos);

                // Emit file data segment (4-byte aligned)
                long fileSize = ff.FsSize;
                if (fileSize > 0)
                {
                    long alignedSize = fileSize + (fileSize % 4 == 0 ? 0 : 4 - (fileSize % 4));
                    alignedSize = Math.Min(alignedSize, _imageSize - dstPos);
                    int toCopy = (int)Math.Min(alignedSize, _src.Length - srcPos);
                    if (toCopy > 0)
                    {
                        _segments.Add(new Segment { IsoOffset = dstPos, Length = toCopy, Type = SegmentType.Data, SrcOffset = srcPos });
                        srcPos += toCopy;
                        dstPos += toCopy;
                    }
                    nullsPos = dstPos + WiiConsts.DataNullsCount;
                }

                // Compute NKit gap length: space between end of this file and start of next in NKit source
                long nkitGapLen;
                if (isLast)
                    nkitGapLen = _src.Length - srcPos;
                else
                    nkitGapLen = ((FstFile)files[i + 1]).FsOffset - srcPos;

                // Parse gap encoding if there are gap bytes in the NKit source
                if (nkitGapLen > 0 && dstPos < _imageSize)
                    dstPos = parseGcGap(ref srcPos, dstPos, ref nullsPos, ff, isLast, nkitGapLen);
                else if (ff.FsSize == 0)
                    nullsPos = dstPos + WiiConsts.DataNullsCount;
            }

            // Final gap after last file (always present)
            long finalGapLen = _src.Length - srcPos;
            if (finalGapLen > 0 && dstPos < _imageSize)
                dstPos = parseGcGap(ref srcPos, dstPos, ref nullsPos, null, true, finalGapLen);

            // Pad with zeros to full image size if needed
            if (dstPos < _imageSize)
            {
                _segments.Add(new Segment { IsoOffset = dstPos, Length = _imageSize - dstPos, Type = SegmentType.Zero });
                dstPos = _imageSize;
            }
        }

        /// <summary>
        /// Parses a single NKit-encoded gap and adds segments to the map.
        /// Follows v1's writeGap logic exactly for nulls handling.
        /// nkitGapLen = bytes available in source for this gap (used for JunkFile early return).
        /// Returns the new dstPos after the gap.
        /// </summary>
        private long parseGcGap(ref int srcPos, long dstPos, ref long nullsPos, FstFile file, bool firstOrLastFile, long nkitGapLen)
        {
            if (nkitGapLen == 0)
            {
                if (file != null && file.FsSize == 0)
                    nullsPos = dstPos + WiiConsts.DataNullsCount;
                return dstPos;
            }

            if (srcPos + 4 > _src.Length)
                return dstPos;

            long size = _src.ReadUInt32B(srcPos);
            srcPos += 4;
            GapType gt = (GapType)(size & 0b11);
            size &= 0xFFFFFFFC;

            if (size == 0xFFFFFFFC) // Wii only, handle gracefully
            {
                size = 0xFFFFFFFCL + _src.ReadUInt32B(srcPos);
                srcPos += 4;
            }

            long junkFileLen = 0;

            // JunkFile: zero-byte file whose data was disc junk
            if (gt == GapType.JunkFile)
            {
                // v1: nullsPos = Math.Min(nullsPos - dstPos, 0) — effectively nullsPos = dstPos
                nullsPos = dstPos + Math.Min(nullsPos - dstPos, 0);

                long nulls = (size & 0xFC) >> 2;
                junkFileLen = _src.ReadUInt32B(srcPos);
                srcPos += 4;

                // Patch FST size for this junk file
                if (file != null)
                    _fst.WriteUInt32B(file.FstPtrOffset + 4, (uint)junkFileLen);

                long junkFileLenAligned = junkFileLen + (junkFileLen % 4 == 0 ? 0 : 4 - (junkFileLen % 4));

                // Write: leading nulls then junk pattern for the file
                if (junkFileLenAligned > 0)
                {
                    if (nulls > 0)
                    {
                        _segments.Add(new Segment { IsoOffset = dstPos, Length = nulls, Type = SegmentType.Zero });
                        dstPos += nulls;
                    }
                    long junkPart = junkFileLenAligned - nulls;
                    if (junkPart > 0)
                    {
                        _segments.Add(new Segment { IsoOffset = dstPos, Length = junkPart, Type = SegmentType.Junk });
                        dstPos += junkPart;
                    }
                }

                // v1: if (file.GapLength <= 8) return — only the junk file encoding, no following gap
                if (nkitGapLen <= 8)
                    return dstPos;

                // Read following gap
                size = _src.ReadUInt32B(srcPos);
                srcPos += 4;
                gt = (GapType)(size & 0b11);
                size &= 0xFFFFFFFC;
            }
            else if (file != null && file.FsSize == 0)
            {
                // Last zero byte file was legit
                nullsPos = dstPos + WiiConsts.DataNullsCount;
            }

            if (size == 0)
                return dstPos;

            // Main gap: calculate leading nulls (v1 logic)
            long maxNulls = Math.Max(0, nullsPos - dstPos);
            long gapNulls;
            if (size < maxNulls)
                gapNulls = size;
            else
                gapNulls = size >= 0x40000 && !firstOrLastFile ? 0 : maxNulls;

            if (gt == GapType.AllJunk)
            {
                // Leading nulls then junk pattern
                if (gapNulls > 0)
                {
                    _segments.Add(new Segment { IsoOffset = dstPos, Length = gapNulls, Type = SegmentType.Zero });
                    dstPos += gapNulls;
                }
                long junkPart = size - gapNulls;
                if (junkPart > 0)
                {
                    _segments.Add(new Segment { IsoOffset = dstPos, Length = junkPart, Type = SegmentType.Junk });
                    dstPos += junkPart;
                }
            }
            else if (gt == GapType.AllBlockFilled)
            {
                // All scrubbed zeros
                _segments.Add(new Segment { IsoOffset = dstPos, Length = size, Type = SegmentType.Zero });
                dstPos += size;
            }
            else // GapType.Mixed
            {
                long prg = size;
                byte btByte = 0x00;
                GapBlockType bt = GapBlockType.Junk;

                while (prg > 0)
                {
                    if (srcPos + 4 > _src.Length)
                        break;

                    long blk = _src.ReadUInt32B(srcPos);
                    srcPos += 4;

                    GapBlockType btType = (GapBlockType)(blk >> 30);
                    bool btRepeat = btType == GapBlockType.Repeat;
                    if (!btRepeat)
                        bt = btType;

                    long cnt = 0x3FFFFFFF & blk;
                    long bytes;

                    if (bt == GapBlockType.NonJunk)
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        int toCopy = (int)Math.Min(bytes, _src.Length - srcPos);
                        if (toCopy > 0)
                        {
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = toCopy, Type = SegmentType.Data, SrcOffset = srcPos });
                            srcPos += toCopy;
                        }
                    }
                    else if (bt == GapBlockType.ByteFill)
                    {
                        if (!btRepeat)
                        {
                            btByte = (byte)(0xFF & cnt);
                            cnt >>= 8;
                        }
                        bytes = Math.Min(cnt * GapBlockSize, prg);

                        if (btByte == 0x00)
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = bytes, Type = SegmentType.Zero });
                        else
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = bytes, Type = SegmentType.Fill, FillByte = btByte });
                    }
                    else // Junk
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);

                        // v1: recalculate maxNulls fresh for each junk block
                        maxNulls = Math.Max(0, nullsPos - dstPos);
                        long localNulls;
                        if (prg < maxNulls)
                            localNulls = bytes;
                        else
                            localNulls = bytes >= 0x40000 && !firstOrLastFile ? 0 : Math.Min(maxNulls, bytes);

                        if (localNulls > 0)
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = localNulls, Type = SegmentType.Zero });
                        long junkBytes = bytes - localNulls;
                        if (junkBytes > 0)
                            _segments.Add(new Segment { IsoOffset = dstPos + localNulls, Length = junkBytes, Type = SegmentType.Junk });
                    }

                    prg -= bytes;
                    dstPos += bytes;
                }
            }

            return dstPos;
        }

        #endregion

        #region Stream Implementation — Offset-Mapped Reads

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_streaming)
                return ReadStreaming(buffer, offset, count);

            if (!_parsed)
            {
                // GameCube fast path: a read that stays within the decoded header region (0x440 +
                // hdrToFst + FST) is served after a CHEAP header-only parse that reads just the
                // front of the source — NOT the whole-source read + file/gap walk. The IImage's
                // construction read (0x440) hits this, so it no longer forces the full decode at
                // Open. The full parse runs on the first read that reaches file data (below), which
                // rebuilds _segments with the full map; the header segments are identical either way.
                bool servedByHeaderOnly = false;
                if (_isGameCube)
                {
                    EnsureGameCubeHeader();
                    long headerEnd = _hdr.Length + _hdrToFst.Length + _fst.Length;
                    servedByHeaderOnly = _position + count <= headerEnd;
                }
                if (!servedByHeaderOnly)
                    ParseStructure();
            }

            long available = _imageSize - _position;
            if (available <= 0)
                return 0;

            int toRead = (int)Math.Min(count, available);
            int totalCopied = 0;

            while (totalCopied < toRead)
            {
                long readPos = _position + totalCopied;
                int remaining = toRead - totalCopied;

                // Find the segment containing readPos
                int segIdx = findSegment(readPos);
                if (segIdx < 0)
                {
                    // Past all segments — fill with zeros
                    Array.Clear(buffer, offset + totalCopied, remaining);
                    totalCopied += remaining;
                    break;
                }

                Segment seg = _segments[segIdx];
                long offsetInSeg = readPos - seg.IsoOffset;
                int segRemaining = (int)Math.Min(seg.Length - offsetInSeg, remaining);

                switch (seg.Type)
                {
                    case SegmentType.Data:
                        Array.Copy(_src, seg.SrcOffset + (int)offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.Header:
                        Array.Copy(_hdr, (int)offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.HdrToFst:
                        Array.Copy(_hdrToFst, (int)offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.Fst:
                        Array.Copy(_fst, (int)offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.Zero:
                        Array.Clear(buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.Fill:
                        for (int i = 0; i < segRemaining; i++)
                            buffer[offset + totalCopied + i] = seg.FillByte;
                        break;

                    case SegmentType.Junk:
                        fillJunk(buffer, offset + totalCopied, seg.IsoOffset + offsetInSeg, segRemaining, seg.JunkIdOverride, seg.JunkStartOffset);
                        break;

                    case SegmentType.Buffer:
                        Array.Copy(seg.Buffer, (int)offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.WiiPartition:
                        readWiiPartition(seg.Map, offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.WiiSrcData:
                        copyFromSrcStore(seg.SrcOffsetL + offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;

                    case SegmentType.UpdateFile:
                        copyFromUpdateFile(seg.SrcOffsetL + offsetInSeg, buffer, offset + totalCopied, segRemaining);
                        break;
                }

                totalCopied += segRemaining;
            }

            _position += totalCopied;
            return totalCopied;
        }

        // Current op being consumed (front of the logical queue, mutable)
        private EmitOp _sCurrent;
        private bool _sHasCurrent;

        /// <summary>
        /// Forward-only streaming read. Consumes the current emit op and the pending
        /// queue; when both are empty and not complete, advances the decoder (reading
        /// the source sequentially, never seeking backward).
        /// </summary>
        private int ReadStreaming(byte[] buffer, int offset, int count)
        {
            if (!_parsed)
                InitStreaming();

            long available = _imageSize - _position;
            if (available <= 0)
            {
                _streamComplete = true;
                finalizeStreaming();
                return 0;
            }

            int toRead = (int)Math.Min(count, available);
            int totalCopied = 0;

            while (totalCopied < toRead)
            {
                if (!_sHasCurrent)
                {
                    if (_sQueue.Count > 0)
                    {
                        _sCurrent = _sQueue.Dequeue();
                        _sHasCurrent = true;
                    }
                    else if (!advanceStreaming())
                    {
                        break;
                    }
                    else
                    {
                        continue; // decoder enqueued more ops
                    }
                }

                int chunk = (int)Math.Min(_sCurrent.Length, toRead - totalCopied);

                switch (_sCurrent.Type)
                {
                    case SegmentType.Data:
                        Array.Copy(_sCurrent.Data, _sCurrent.DataOffset, buffer, offset + totalCopied, chunk);
                        _sCurrent.DataOffset += chunk;
                        break;
                    case SegmentType.Zero:
                        Array.Clear(buffer, offset + totalCopied, chunk);
                        break;
                    case SegmentType.Fill:
                        for (int i = 0; i < chunk; i++)
                            buffer[offset + totalCopied + i] = _sCurrent.FillByte;
                        break;
                    case SegmentType.Junk:
                        // Streaming (forward-only) is GC-only (Wii forces non-streaming), and GC
                        // uses the disc-level junk id, so no per-segment junk override is needed here.
                        fillJunk(buffer, offset + totalCopied, _sCurrent.IsoOffset, chunk);
                        break;
                }

                _sCurrent.IsoOffset += chunk;
                _sCurrent.Length -= chunk;
                if (_sCurrent.Length <= 0)
                    _sHasCurrent = false;

                totalCopied += chunk;
            }

            _position += totalCopied;
            if (_position >= _imageSize)
            {
                _streamComplete = true;
                finalizeStreaming();
            }
            return totalCopied;
        }

        /// <summary>
        /// Binary search for the segment containing the given ISO offset.
        /// </summary>
        private int findSegment(long isoOffset)
        {
            int lo = 0, hi = _segments.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                Segment seg = _segments[mid];
                if (isoOffset < seg.IsoOffset)
                    hi = mid - 1;
                else if (isoOffset >= seg.IsoEnd)
                    lo = mid + 1;
                else
                    return mid;
            }
            return -1; // not found — past all segments
        }

        /// <summary>
        /// Fill buffer with generated disc junk pattern at the given ISO offset.
        /// Uses NJunk.Fill() with a reusable 0x40000-byte block buffer.
        /// </summary>
        private void fillJunk(byte[] buffer, int bufOffset, long isoOffset, int count, byte[] junkIdOverride = null, long junkStartOverride = 0)
        {
            // Resolve the junk context for this segment. Disc-level filler defaults to the disc id
            // and startOffset 0. But the trailing filler AFTER a Game partition must use that
            // partition's boot JunkId and its JunkStartFsOffset (matching
            // SectionProcessor.createJunk's post-Game 'Other' rule) — supplied here as overrides.
            byte[] junkId = junkIdOverride ?? _junkIdBytes;
            long startOffset = junkStartOverride;

            int written = 0;
            while (written < count)
            {
                // Determine which 0x40000 junk block this offset falls in
                long blockStart = isoOffset / NJunk.JunkBlockSize * NJunk.JunkBlockSize;
                int offsetInBlock = (int)(isoOffset - blockStart);

                // Generate the full junk block only when it differs from the one cached in
                // _junkBlock. Reads typically arrive as many sub-0x40000 slices of the same block,
                // so regenerating it every call (NJunk.Fill is expensive) is a major slowdown. The
                // cache is keyed on blockStart AND the junk context (id/startOffset), so a switch
                // between disc-level and post-Game partition junk correctly regenerates.
                if (!_junkBlockValid || _junkBlockStart != blockStart
                    || !ReferenceEquals(_junkBlockId, junkId) || _junkBlockStartOffset != startOffset)
                {
                    NJunk.Fill(junkId, _discNo, startOffset, _imageSize, blockStart, _junkBlock);
                    _junkBlockStart = blockStart;
                    _junkBlockId = junkId;
                    _junkBlockStartOffset = startOffset;
                    _junkBlockValid = true;
                }

                // Copy the relevant portion to the output buffer
                int available = NJunk.JunkBlockSize - offsetInBlock;
                int toCopy = Math.Min(available, count - written);
                Array.Copy(_junkBlock, offsetInBlock, buffer, bufOffset + written, toCopy);

                written += toCopy;
                isoOffset += toCopy;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: _position = offset; break;
                case SeekOrigin.Current: _position += offset; break;
                case SeekOrigin.End: _position = _imageSize + offset; break;
            }
            if (_position < 0) _position = 0;
            if (_position > _imageSize) _position = _imageSize;
            return _position;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _imageSize;
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion

        #region IAsIso Implementation

        public long Size => _imageSize;
        public bool SizeEstimated => false;
        public bool Seekable => true;
        public bool SeekRequired => false;
        public long RealPosition => _sourceStream?.Position ?? 0;
        public long RealSize => _sourceStream?.Length ?? 0;
        public ContainerType Format => ContainerType.Iso;

        // The underlying compact NKit container the source was wrapped in (Gcz for .nkit.gcz,
        // Iso for a raw .nkit.iso). Format stays Iso because the decoded stream IS a plain ISO;
        // this is only for reporting the true source type (e.g. "NKit.Gcz").
        public ContainerType NKitSourceContainer => _isGcz ? ContainerType.Gcz : ContainerType.Iso;
        public Checksums Checksums { get; private set; }
        public NKitHeader NKitHeader { get; private set; }

        // The NKit header's stored CRC is the EXPECTED (source) checksum — it is surfaced via
        // Checksums (like RVZ/CISO) and flows to SrcParts for verification. It is NOT a calculated
        // value, so CustomChecksums() must return null: the pipeline computes the calculated CRC
        // from the actual decoded read (InChkStream) and Verify compares calculated-vs-expected.
        // (Only ChdAsIso returns a non-null CustomChecksums — it is the unique case that can only
        // calculate a checksum over the full decoded file via a live verify pass.)
        public Checksums CustomChecksums() => null;

        public void SetRemovedBlock(Action<MetaData> setBlock) => _setRemovedBlock = setBlock;

        public void Complete()
        {
            _src = null;
            _hdr = null;
            _hdrToFst = null;
            _fst = null;
            _segments = null;
            _junkBlock = null;
            try { _srcStore?.Dispose(); } catch { }
            _srcStore = null;
            try { _updatePartitionStream?.Dispose(); } catch { }
            _updatePartitionStream = null;
            _ptnJunkBlock = null;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _src = null;
                _hdr = null;
                _hdrToFst = null;
                _fst = null;
                _segments = null;
                _junkBlock = null;
                try { _srcStore?.Dispose(); } catch { }
                _srcStore = null;
                try { _gczStream?.Dispose(); } catch { }
                try { _sourceStream?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}