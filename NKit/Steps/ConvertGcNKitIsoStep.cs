using Nanook.NKit.Configuration;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    /// <summary>
    /// Converts any GameCube source image to NKit v01 (.nkit.iso) format.
    /// GameCube only — direct port of NkitWriterGc (v1).
    ///
    /// Sections arrive as sequential 2MB raw disc chunks. The carry buffer accumulates
    /// them and the state machine drives through header, gap, and file phases. Gap
    /// encoding is delegated to <see cref="GcNkitGapEncoder"/>, which handles all
    /// block classification and produces a ready-to-write encoded buffer per gap.
    /// </summary>
    internal sealed class ConvertGcNKitIsoStep : StepBase, IStep
    {
        private string _outName;
        private const string _OutExt = "nkit.iso"; // file extension — distinct from the format name "nkitiso"
        private const int _JunkProbe = 0x30;

        private const int _NkitHdrPos    = WiiConsts.NKitHeaderPos;
        private const int _NkitSrcCrcOff = _NkitHdrPos + WiiConsts.NKitHdrSrcCrcOffset;
        private const int _NkitMagicOff  = _NkitHdrPos + WiiConsts.NKitHdrMagicCrcOffset;
        private const int _NkitSrcLenOff = _NkitHdrPos + WiiConsts.NKitHdrSrcLenOffset;
        private const int _NkitJunkIdOff = _NkitHdrPos + WiiConsts.NKitHdrJunkIdOffset;
        private const int _DolPtrOff     = WiiConsts.DolPtrOffset;
        private const int _FstPtrOff     = WiiConsts.FstPtrOffset;
        private const int _FstSizeOff    = WiiConsts.FstSizeOffset;
        private const int _HdrSz         = WiiConsts.BootBinSize;

        // ── Per-image state ───────────────────────────────────────────────────────────

        private long   _imageSize;
        private int    _fstPtr;
        private int    _fstPad;
        private int    _fstSize;
        private int    _hdrAreaSize;
        private byte[] _hdr;
        private byte[] _hdrToFst;
        private byte[] _fst;
        private long   _srcPos;
        private long   _dstPos;
        private long   _nullsPos;
        private long   _mainDolAddr;
        private long   _dstMainDolAddr;
        private byte[] _discId;
        private byte[] _rawDiscId;  // original disc ID bytes (before junk substitution)
        private byte   _discNo;
        private long   _fstFileAlignment; // -1=default 0x8000 rule, 0=preserve disc offset, >0=custom boundary
        private List<GcEntry> _entries;
        private int    _eIdx;
        private GcPhase _phase;

        // ── Carry buffer ──────────────────────────────────────────────────────────────

        private byte[] _carry;
        private int    _carryLen;

        // ── Gap encoder ───────────────────────────────────────────────────────────────

        private GcNkitGapEncoder _gapEncoder;
        private long             _gapBytesWritten; // bytes written to outWrite during current gap

        // ── Section junk map ─────────────────────────────────────────────────────────
        // Pre-classified disc regions from section.Items (DataType.NJunk / Fill / Data).
        // Built in Process() from the SectionProcessor's gap analysis so the encoder
        // can skip NJunk.Fill for regions already verified by the pipeline.

        private System.Collections.Generic.List<GcNkitGapEncoder.ClassifiedRegion> _sectionJunkMap;

        // ── DOL-beyond-FST protection ─────────────────────────────────────────────────
        // When the main DOL sits past the FST area (e.g. translation-patched images),
        // _mainDolEnd is computed once from the DOL header so the encoder can force
        // those blocks to NonJunk even if they coincidentally match the junk pattern.

        private long _mainDolEnd; // exclusive disc end of DOL; 0 = not yet computed

        // ── Junk cache (shared with isJunkBytes) ──────────────────────────────────────

        private byte[] _junkCache;
        private long   _junkCacheBlockStart;

        // ── CRC snapshots ─────────────────────────────────────────────────────────────
        // Mirrors v1 NCrc.Snapshot pattern:
        //   snapshot 0: hdr  (0x440, NKit magic = 0 when written)
        //   snapshot 1: hdrToFst
        //   snapshot 2: fst  (source offsets when written; patched at finalise)
        //   snapshot 3: file/gap/padding data

        private Crc  _crcHdr;
        private Crc  _crcHdrToFst;
        private Crc  _crcFst;
        private Crc  _crcFiles;
        private long _filesLen;

        // ── Contracts ─────────────────────────────────────────────────────────────────

        internal override bool ContractReqPatch    => false;
        internal override bool ContractReqChk      => true;
        internal override bool ContractFullScan    => true;
        internal override bool ContractIsLossy     => false;
        internal override bool ContractIsExpand    => false;
        internal override bool ContractIsFix       => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc      => false;
        internal override bool ContractCanHash     => false;
        internal override string ComponentTag      => LogScopes.StepConvertGcNKitIso;

        public override string ProposedName() => $"{_outName}.{_OutExt}";

        internal ConvertGcNKitIsoStep(IStepContextConstruct context) : base()
        {
            ValidationResult result = ConfigSettingsFormatValidator.ValidateNkitIsoFormat(context.StepConfig);
            if (!result.IsValid)
                throw new HandledException($"Convert - {result.ErrorMessage}");
            _outName = context.SourceImageName;
            context.AddSettingsInfo("ConvertTo", "NKit ISO [Lossless, GameCube only]");
            base.CheckContract(context.StepInfo);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);
            _imageSize           = context.ImageSize;
            _srcPos              = 0;
            _dstPos              = 0;
            _nullsPos            = 0;
            _dstMainDolAddr      = 0;
            _entries             = null;
            _eIdx                = 0;
            _phase               = GcPhase.Header;
            _carry               = new byte[WiiConsts.WiiGroupSize];
            _carryLen            = 0;
            _gapEncoder          = new GcNkitGapEncoder();
            _mainDolEnd          = 0;
            _sectionJunkMap      = new System.Collections.Generic.List<GcNkitGapEncoder.ClassifiedRegion>();
            _junkCache           = new byte[NJunk.JunkBlockSize];
            _junkCacheBlockStart = -1L;
            _crcHdr              = new Crc();
            _crcHdrToFst         = new Crc();
            _crcFst              = new Crc();
            _crcFiles            = new Crc();
            _filesLen            = 0;

            byte[] hd    = context.HeaderData;
            _hdr          = new byte[_HdrSz];
            Array.Copy(hd, 0, _hdr, 0, _HdrSz);
            _mainDolAddr  = _hdr.ReadUInt32B(_DolPtrOff);
            _discNo       = hd[WiiConsts.DataHdrDiscNoOffset];

            // Per-disc overrides from fix data
            Nintendo.WiiGc.FixData fixData = context.Settings.FixData<Nintendo.WiiGc.FixData>();
            _fstFileAlignment = fixData?.FstFileAlignment ?? -1L;

            // Use junk ID substitution if present (some discs generate junk with a different ID)
            _rawDiscId = new byte[] { hd[0], hd[1], hd[2], hd[3] };
            _discNo    = hd[WiiConsts.DataHdrDiscNoOffset];
            string forceJunkId = fixData?.ForceJunkId;
            if (!string.IsNullOrEmpty(forceJunkId) && forceJunkId.Length >= 4)
                _discId = new byte[] { (byte)forceJunkId[0], (byte)forceJunkId[1], (byte)forceJunkId[2], (byte)forceJunkId[3] };
            else
                _discId = _rawDiscId;

            long fstPtr  = _hdr.ReadUInt32B(_FstPtrOff);
            long fstSize = _hdr.ReadUInt32B(_FstSizeOff);
            long fstPad  = fstSize + (fstSize % 4 == 0 ? 0 : 4 - (fstSize % 4));
            _fstPtr      = (int)fstPtr;
            _fstPad      = (int)fstPad;
            _fstSize     = (int)fstSize;
            _hdrAreaSize = (int)(_HdrSz + Math.Max(0, fstPtr - _HdrSz) + fstPad);

            _hdrToFst = new byte[Math.Max(0, _fstPtr - _HdrSz)];
            _fst      = new byte[_fstPad];
        }

        public void Patched(ISection section) { }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.ImageOffset == 0)
                base.OutStream.NewPart(_outName, _OutExt, true);

            // Build disc-offset classification map from section.Items — lets the gap encoder
            // skip NJunk.Fill for regions the SectionProcessor has already verified.
            _sectionJunkMap.Clear();
            if (section.Items != null)
            {
                foreach (SectionItem si in section.Items)
                {
                    ISectionData gap = si.Gap;
                    if (gap == null) continue;

                    long baseDisc = section.ImageOffset;
                    if (gap.DataType == DataType.NJunk || gap.DataType == DataType.Fill)
                    {
                        _sectionJunkMap.Add(new GcNkitGapEncoder.ClassifiedRegion
                        {
                            DiscStart = baseDisc + gap.FsOffset,
                            DiscEnd   = baseDisc + gap.FsOffset + gap.FsSize,
                            Type      = gap.DataType,
                            DataNulls = gap.DataNulls,
                            FillByte  = gap.FillByte
                        });
                    }
                    else if (gap.DataType == DataType.Other)
                    {
                        foreach (ISectionData gi in si.GapInfo)
                        {
                            if (gi.DataType == DataType.NJunk || gi.DataType == DataType.Fill)
                            {
                                _sectionJunkMap.Add(new GcNkitGapEncoder.ClassifiedRegion
                                {
                                    DiscStart = baseDisc + gi.FsOffset,
                                    DiscEnd   = baseDisc + gi.FsOffset + gi.FsSize,
                                    Type      = gi.DataType,
                                    DataNulls = gi.DataNulls,
                                    FillByte  = gi.FillByte
                                });
                            }
                        }
                    }
                }
            }

            int count = (int)section.Size;
            if (_carryLen + count > _carry.Length)
            {
                byte[] bigger = new byte[_carryLen + count + WiiConsts.WiiGroupSize];
                Array.Copy(_carry, 0, bigger, 0, _carryLen);
                _carry = bigger;
            }
            Array.Copy(section.Decrypted, 0, _carry, _carryLen, count);
            _carryLen += count;

            drive();

            // If this is the last section and the trailing gap encoder is still open,
            // complete it now — the disc ends here, remaining bytes are junk/zeros.
            if (section.ImageOffset + section.Size >= _imageSize && _gapEncoder.InProgress)
                completeTrailingGap();
        }

        public override void ProcessResults()
        {
            drive();
            if (_gapEncoder.InProgress)
                completeTrailingGap();
            finalise();
        }

        private void completeTrailingGap()
        {
            // Feed remaining carry blocks, then complete. Any bytes beyond the carry
            // (i.e. beyond the image end) are treated as junk — Complete() will emit
            // the correct AllJunk or Mixed header based on blocks seen so far.
            // Pre-fill junk blocks for remaining carry data in parallel.
            long firstTrailingOff = _gapEncoder.GapDiscStart + (_gapEncoder.GapLength - _gapEncoder.Remaining);
            if (_carryLen > 0)
            {
                _gapEncoder.SetSectionJunkMap(_sectionJunkMap);
                _gapEncoder.PrefillJunkCache(firstTrailingOff, _carryLen);
            }

            int carryIdx = 0;
            while (_gapEncoder.Remaining > 0)
            {
                int bLen = (int)Math.Min(GcNkitGapEncoder.BlockSize, _gapEncoder.Remaining);
                if (_carryLen - carryIdx < bLen)
                    break;
                long discOff = _gapEncoder.GapDiscStart + (_gapEncoder.GapLength - _gapEncoder.Remaining);
                _gapEncoder.Feed(_carry, carryIdx, bLen, discOff);
                carryIdx += bLen;
            }
            consumeCarry(carryIdx);
            _gapEncoder.Complete();
            _dstPos += _gapBytesWritten;
        }

        // ── State machine ─────────────────────────────────────────────────────────────

        private void drive()
        {
            if (_phase == GcPhase.Header)
            {
                if (_carryLen < _hdrAreaSize)
                    return;

                if (_hdrToFst.Length > 0)
                    Array.Copy(_carry, _HdrSz, _hdrToFst, 0, _hdrToFst.Length);
                int copyFst = Math.Min(_fstPad, _carryLen - _fstPtr);
                if (copyFst > 0)
                    Array.Copy(_carry, _fstPtr, _fst, 0, copyFst);

                byte[] hdrOut = new byte[_HdrSz];
                Array.Copy(_hdr, 0, hdrOut, 0, _HdrSz);
                Array.Clear(hdrOut, _NkitHdrPos, WiiConsts.NKitHeaderSize); // magic=0
                base.OutStream.Write(hdrOut, 0, hdrOut.Length);
                _crcHdr.Sum(hdrOut, 0, hdrOut.Length);

                base.OutStream.Write(_hdrToFst, 0, _hdrToFst.Length);
                _crcHdrToFst.Sum(_hdrToFst, 0, _hdrToFst.Length);

                base.OutStream.Write(_fst, 0, _fst.Length);
                _crcFst.Sum(_fst, 0, _fst.Length);

                _dstPos   = _hdrAreaSize;
                _nullsPos = _hdrAreaSize + WiiConsts.DataNullsCount;

                consumeCarry(_hdrAreaSize);

                _entries = buildEntries();
                _eIdx    = 0;
                _phase   = GcPhase.Gap;
            }

            while (_eIdx < _entries.Count)
            {
                GcEntry e = _entries[_eIdx];

                // ── Gap phase ─────────────────────────────────────────────────────────
                if (_phase == GcPhase.Gap)
                {
                    if (e.GapLength > 0 || e.JunkFileLength > 0)
                    {
                        if (!processGap(ref e))
                            return;
                        _entries[_eIdx] = e;
                    }
                    _phase = GcPhase.File;
                }

                // ── File phase ────────────────────────────────────────────────────────
                if (_phase == GcPhase.File)
                {
                    if (e.FstFile == null)
                    {
                        _eIdx++;
                        break;
                    }

                    long fileSzPadded = e.FstFile.PaddedLength;

                    // First time entering this file — need at least the junk probe + alignment bytes.
                    // We also need enough to do the junk check (first _JunkProbe bytes).
                    int minNeeded = Math.Min((int)fileSzPadded, _JunkProbe);
                    if (e.FileWritten == 0 && _carryLen < minNeeded)
                        return;

                    // Apply alignment padding — only once, on first entry (FileWritten == 0).
                    if (e.FileWritten == 0)
                    {
                        long align = e.FstFile.Alignment;
                        if (align == 0) // preserve: pad to original disc offset
                        {
                            long pad = Math.Max(0, e.FstFile.DataOffset - _dstPos);
                            if (pad > 0)
                            {
                                byte[] zeros = new byte[(int)pad];
                                outWrite(zeros, 0, (int)pad);
                                _dstPos += pad;
                            }
                        }
                        else if (align > 0 && _dstPos % align != 0)
                        {
                            long pad = align - (_dstPos % align);
                            byte[] zeros = new byte[(int)pad];
                            outWrite(zeros, 0, (int)pad);
                            _dstPos += pad;
                        }

                        // Update FST offset to compacted output position — must happen before any write.
                        _fst.WriteUInt32B(e.FstFile.FstAddrOffset, (uint)_dstPos);
                        if (e.FstFile.DataOffset == _mainDolAddr)
                            _dstMainDolAddr = _dstPos;

                        if (isFileJunk(_carry, 0, e.FstFile, _srcPos))
                        {
                            // Junk file: zero FST size; propagate JunkFile info to next gap.
                            _fst.WriteUInt32B(e.FstFile.FstAddrOffset + 4, 0);
                            if (_eIdx + 1 < _entries.Count)
                            {
                                GcEntry next = _entries[_eIdx + 1];
                                next.JunkFileLength    = (uint)e.FstFile.Length;
                                next.JunkFstAddrOffset = e.FstFile.FstAddrOffset;
                                int nullCount = 0;
                                for (int k = 0; k < Math.Min(e.FstFile.Length, (long)WiiConsts.DataNullsCount); k++)
                                {
                                    if (_carry[k] != 0) break;
                                    nullCount++;
                                }
                                next.JunkFileNulls  = nullCount;
                                _entries[_eIdx + 1] = next;
                            }
                            consumeCarry((int)fileSzPadded);
                            _eIdx++;
                            _phase = GcPhase.Gap;
                            continue;
                        }
                    }

                    // Stream the file data from carry in whatever chunks are available.
                    // Come back next section if not all data is in carry yet.
                    long remaining = fileSzPadded - e.FileWritten;
                    int  available = (int)Math.Min(_carryLen, remaining);
                    if (available > 0)
                    {
                        outWrite(_carry, 0, available);
                        _dstPos       += available;
                        e.FileWritten += available;
                        consumeCarry(available);
                        _entries[_eIdx] = e;
                    }

                    if (e.FileWritten < fileSzPadded)
                        return; // need more data

                    // File complete.
                    long newNulls = _srcPos + WiiConsts.DataNullsCount;
                    if (newNulls % 4 != 0)
                        newNulls += 4 - (newNulls % 4);
                    _nullsPos = newNulls;
                    _eIdx++;
                    _phase = GcPhase.Gap;
                }
            }
        }

        // ── Gap processing ────────────────────────────────────────────────────────────

        private bool processGap(ref GcEntry e)
        {
            if (!_gapEncoder.InProgress)
            {
                // Write JunkFile header first if a previous file was junk-encoded.
                if (e.JunkFileLength > 0)
                {
                    _dstPos += writeUInt32BE((uint)((_entries[_eIdx].JunkFileNulls << 2) | 0b11u));
                    _dstPos += writeUInt32BE(e.JunkFileLength);
                    if (e.JunkFstAddrOffset >= 0)
                        _fst.WriteUInt32B(e.JunkFstAddrOffset + 4, 0);
                    if (e.GapLength == 0)
                    {
                        // V1 always writes a gap header even for zero-length gaps following
                        // a JunkFile — it adds an empty Junk block, producing a 4-byte AllJunk
                        // header with length 0.
                        _dstPos += writeUInt32BE(0x00000000u); // AllJunk, length=0
                        return true;
                    }
                }

                // Compute leading-nulls count (the nullsPos window).
                long maxNulls  = Math.Max(0, _nullsPos - _srcPos);
                int  leadNulls = (int)(e.GapLength < maxNulls ? e.GapLength
                                     : e.GapLength >= 0x40000 && !e.IsFirstOrLast ? 0L
                                     : maxNulls);

                // Compute DOL protection range when the DOL is beyond the FST area.
                long dolStart = -1L;
                long dolEnd   = 0L;
                if (_mainDolAddr > _hdrAreaSize)
                {
                    dolStart = _mainDolAddr;
                    if (_mainDolEnd == 0)
                    {
                        // Lazily compute dolEnd from the DOL header in carry.
                        long dolOff = _mainDolAddr - _srcPos;
                        if (dolOff >= 0 && dolOff + 0x90 <= _carryLen)
                        {
                            long dolSize = 0;
                            for (int di = 0; di < 18; di++)
                            {
                                long secOff  = _carry.ReadUInt32B((int)(dolOff + di * 4));
                                long secSize = _carry.ReadUInt32B((int)(dolOff + 0x48 + di * 4));
                                if (secOff > 0 && secSize > 0)
                                    dolSize = Math.Max(dolSize, secOff + secSize);
                            }
                            // Sanity check: dolSize must be within the disc image.
                            // On discs where files have been relocated, the boot header DOL pointer
                            // may reference a different file's data — reading DOL section headers
                            // from that position produces garbage sizes. Clamp to image size.
                            if (dolSize > _imageSize - _mainDolAddr)
                                dolSize = 0;
                            _mainDolEnd = _mainDolAddr + Math.Max(dolSize, 0x100L);
                        }
                    }
                    dolEnd = _mainDolEnd;
                }

                _gapEncoder.Begin(e.GapDiscStart, e.GapLength, leadNulls,
                                  _discId, _discNo, dolStart, dolEnd,
                                  (buf, off, cnt) => { outWrite(buf, off, cnt); _gapBytesWritten += cnt; });
                _gapBytesWritten = 0;
            }

            // Pre-fill junk blocks for this carry section in parallel — eliminates
            // synchronous NJunk.Fill calls during the per-block classification loop.
            // Skipped in DirectScanMode (v1-equivalent path does per-block NJunk.Fill inline).
            int  carryIdx       = 0;
            long firstGapDiscOff = e.GapDiscStart + (e.GapLength - _gapEncoder.Remaining);
            int  availForPrefill = (int)Math.Min(_carryLen - carryIdx, _gapEncoder.Remaining);
            if (!_gapEncoder.DirectScanMode && availForPrefill > 0)
            {
                _gapEncoder.SetSectionJunkMap(_sectionJunkMap);
                _gapEncoder.PrefillJunkCache(firstGapDiscOff, availForPrefill);
            }

            // Feed carry into the encoder block-by-block (serial, preserves RLE order).
            while (_gapEncoder.Remaining > 0)
            {
                int bLen = (int)Math.Min(GcNkitGapEncoder.BlockSize, _gapEncoder.Remaining);
                if (_carryLen - carryIdx < bLen)
                    break;
                long discOff = e.GapDiscStart + (e.GapLength - _gapEncoder.Remaining);
                bool done    = _gapEncoder.Feed(_carry, carryIdx, bLen, discOff);
                carryIdx += bLen;
                if (done)
                    break;
            }
            consumeCarry(carryIdx);

            if (_gapEncoder.Remaining > 0)
                return false;

            // Gap complete — all bytes already written to outWrite by the encoder.
            _gapEncoder.Complete();
            _dstPos += _gapBytesWritten;
            if (e.JunkFstAddrOffset >= 0 && e.JunkFileLength == 0)
                _fst.WriteUInt32B(e.JunkFstAddrOffset + 4, 0);

            return true;
        }

        // ── File junk check ───────────────────────────────────────────────────────────

        private bool isFileJunk(byte[] carry, int offset, GcFstFile file, long srcPos)
        {
            // Zero-length files are always treated as junk (no bytes to preserve).
            if (file.Length == 0) return true;

            int nullsInFile = (int)Math.Max(0, _nullsPos - srcPos);
            int probeLen    = (int)Math.Min(_JunkProbe, file.Length);

            if (probeLen <= nullsInFile) return false;

            // V1 counts actual leading-zero bytes (not nullsPos window) to pass to junk compare.
            // This matches JunkStream.Compare's countNulls logic exactly.
            int actualLeadNulls = 0;
            for (int i = 0; i < Math.Min(file.Length, (long)WiiConsts.DataNullsCount); i++)
            {
                if (carry[offset + i] != 0) break;
                actualLeadNulls++;
            }

            if (!isJunkBytes(carry, offset, probeLen, file.DataOffset, actualLeadNulls))
                return false;
            if (file.Length <= _JunkProbe) return true;

            return isJunkBytes(carry, offset + probeLen, file.Length - probeLen,
                file.DataOffset + probeLen, 0);
        }

        private bool isJunkBytes(byte[] data, int offset, int length, long discOffset, int leadNulls)
        {
            long blockStart = (discOffset / NJunk.JunkBlockSize) * NJunk.JunkBlockSize;
            if (_junkCacheBlockStart != blockStart)
            {
                NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, _junkCache);
                _junkCacheBlockStart = blockStart;
            }

            int junkOff = (int)(discOffset - blockStart);
            for (int i = 0; i < length; i++, junkOff++)
            {
                if (junkOff >= NJunk.JunkBlockSize)
                {
                    blockStart += NJunk.JunkBlockSize;
                    NJunk.Fill(_discId, _discNo, blockStart, WiiConsts.FullSizeGameCube, blockStart, _junkCache);
                    _junkCacheBlockStart = blockStart;
                    junkOff = 0;
                }

                byte actual = data[offset + i];
                if (leadNulls > 0)
                {
                    if (actual != 0x00) return false;
                    leadNulls--;
                }
                else if (actual != _junkCache[junkOff])
                    return false;
            }
            return true;
        }

        // ── Finalise ─────────────────────────────────────────────────────────────────

        private void finalise()
        {
            if (_dstPos % 0x800 != 0)
            {
                long pad = 0x800 - (_dstPos % 0x800);
                outWrite(new byte[(int)pad], 0, (int)pad);
                _dstPos += pad;
            }

            long nkitSize = _dstPos;
            uint srcCrc   = this.Context.Scan.Crc;

            if (_dstMainDolAddr != 0 && _dstMainDolAddr != _mainDolAddr)
                _hdr.WriteUInt32B(_DolPtrOff, (uint)_dstMainDolAddr);
            _hdr.WriteString(_NkitHdrPos, 8, WiiConsts.NKitIdV1);
            _hdr.WriteUInt32B(_NkitSrcCrcOff, srcCrc);
            _hdr.WriteUInt32B(_NkitSrcLenOff, (uint)_imageSize);
            // Write junk ID substitution if the disc uses a different ID for junk generation
            bool junkIdSubstituted = _discId[0] != _rawDiscId[0] || _discId[1] != _rawDiscId[1]
                                  || _discId[2] != _rawDiscId[2] || _discId[3] != _rawDiscId[3];
            if (junkIdSubstituted)
                _hdr.WriteUInt32B(_NkitJunkIdOff, (uint)((_discId[0] << 24) | (_discId[1] << 16) | (_discId[2] << 8) | _discId[3]));
            else
                _hdr.WriteUInt32B(_NkitJunkIdOff, 0);

            uint pCrc0 = Crc.Compute(_hdr);
            uint pCrc1 = _crcHdrToFst.Value;
            uint pCrc2 = Crc.Compute(_fst);
            uint pCrc3 = _crcFiles.Value;

            uint zeroCrc = pCrc0;
            zeroCrc = ~Crc.Combine(~zeroCrc, ~pCrc1, _hdrToFst.Length);
            zeroCrc = ~Crc.Combine(~zeroCrc, ~pCrc2, _fst.Length);
            zeroCrc = ~Crc.Combine(~zeroCrc, ~pCrc3, _filesLen);

            uint magicCrc = GcCrcForce.Calculate(zeroCrc, nkitSize, srcCrc, _NkitMagicOff, 0);
            _hdr.WriteUInt32B(_NkitMagicOff, magicCrc);

            base.OutStream.CrcPatch(0, ms =>
            {
                ms.Write(_hdr, 0, _hdr.Length);
                ms.Write(_hdrToFst, 0, _hdrToFst.Length);
                ms.Write(_fst, 0, _fst.Length);
            });

            base.ProcessingComplete();
            base.ProcessResults();
        }

        // ── Output helpers ────────────────────────────────────────────────────────────

        private void outWrite(byte[] data, int offset, int count)
        {
            base.OutStream.Write(data, offset, count);
            _crcFiles.Sum(data, offset, count);
            _filesLen += count;
        }

        private long writeUInt32BE(uint value)
        {
            byte[] b = value.ToBytesBE();
            outWrite(b, 0, 4);
            return 4;
        }

        private void consumeCarry(int count)
        {
            _srcPos += count;
            if (count >= _carryLen)
            {
                _carryLen = 0;
            }
            else
            {
                _carryLen -= count;
                Array.Copy(_carry, count, _carry, 0, _carryLen);
            }
        }

        // ── Entry list ────────────────────────────────────────────────────────────────

        private List<GcEntry> buildEntries()
        {
            List<GcFstFile> files = new List<GcFstFile>();
            if (_fst.Length >= 12)
            {
                int total = (int)_fst.ReadUInt32B(8);
                int strTableOff = total * 12; // FST string table starts after all entries
                for (int i = 1; i * 12 + 11 < _fst.Length && i < total; i++)
                {
                    if (_fst[i * 12] != 0x00) continue;
                    int nameOff = (int)_fst.ReadUInt32B(i * 12) & 0xFFFFFF; // low 24 bits = string offset
                    // Read null-terminated name from string table
                    string name = "";
                    int nOff = strTableOff + nameOff;
                    while (nOff < _fst.Length && _fst[nOff] != 0)
                        name += (char)_fst[nOff++];
                    // Extract extension (lowercase, including dot)
                    int dotIdx = name.LastIndexOf('.');
                    string ext = dotIdx >= 0 ? name.Substring(dotIdx).ToLowerInvariant() : "";
                    files.Add(new GcFstFile
                    {
                        DataOffset    = _fst.ReadUInt32B(i * 12 + 4),
                        Length        = (int)_fst.ReadUInt32B(i * 12 + 8),
                        FstAddrOffset = i * 12 + 4,
                        Extension     = ext
                    });
                }
            }
            files.Sort((a, b) =>
            {
                int c = a.DataOffset.CompareTo(b.DataOffset);
                return c != 0 ? c : a.Length.CompareTo(b.Length);
            });

            // v1 alignment extensions: files with these extensions get 0x8000 alignment
            // even when their length is not a multiple of 0x8000.
            string[] _AlignExts = { ".tgc" };

            // Compute per-file alignment — mirrors v1 NkitFormat.GetConvertFstFiles.
            foreach (GcFstFile f in files)
            {
                if (_fstFileAlignment == 0)
                    f.Alignment = 0;
                else if (_fstFileAlignment == -1 && f.DataOffset % 0x8000 == 0 &&
                         (f.Length % 0x8000 == 0 || System.Array.IndexOf(_AlignExts, f.Extension) >= 0))
                    f.Alignment = 0x8000;
                else if (_fstFileAlignment > 0 && f.DataOffset % _fstFileAlignment == 0)
                    f.Alignment = _fstFileAlignment;
                else
                    f.Alignment = -1;
            }

            List<GcEntry> result = new List<GcEntry>();
            long prevEnd = _fstPtr + _fstPad;

            // V1 NkitFormat.GetConvertFstFiles returns null when any inter-file gap is negative
            // (disc where files have been relocated with mismatched boot header). In that case
            // NkitWriterGc falls back to a single ProcessGap
            // covering imageSize - srcPos as one big gap — no per-file processing.
            // Replicate that: if any gap is negative, return a single tail entry.
            for (int i = 0; i < files.Count; i++)
            {
                long end = (i == 0) ? prevEnd : files[i - 1].DataOffset + files[i - 1].PaddedLength;
                long gap = files[i].DataOffset - end;
                if (gap < 0)
                {
                    // Bad image — single gap covering rest of disc, no files
                    long tailGap = _imageSize - prevEnd;
                    result.Add(new GcEntry
                    {
                        GapLength     = tailGap < 0 ? 0 : tailGap,
                        GapDiscStart  = prevEnd,
                        IsFirstOrLast = true,
                        FstFile       = null
                    });
                    return result;
                }
                result.Add(new GcEntry
                {
                    GapLength     = gap,
                    GapDiscStart  = end,
                    IsFirstOrLast = (i == 0),
                    FstFile       = files[i]
                });
            }

            if (files.Count > 0)
            {
                GcFstFile last = files[files.Count - 1];
                long end  = last.DataOffset + last.PaddedLength;
                long tail = _imageSize - end;
                result.Add(new GcEntry
                {
                    GapLength     = tail < 0 ? 0 : tail,
                    GapDiscStart  = end,
                    IsFirstOrLast = true,
                    FstFile       = null
                });
            }

            return result;
        }

        // ── Types ─────────────────────────────────────────────────────────────────────

        private enum GcPhase { Header, Gap, File }

        private class GcFstFile
        {
            public long   DataOffset;
            public int    Length;
            public long   PaddedLength => Length + (Length % 4 == 0 ? 0 : 4 - (Length % 4));
            public int    FstAddrOffset;
            public long   Alignment;   // -1=none, 0=preserve disc offset, >0=align boundary
            public string Extension;   // lowercase file extension including dot, e.g. ".tgc"
        }

        private struct GcEntry
        {
            public long      GapLength;
            public long      GapDiscStart;
            public bool      IsFirstOrLast;
            public GcFstFile FstFile;
            public uint      JunkFileLength;
            public int       JunkFstAddrOffset;
            public int       JunkFileNulls;
            public long      FileWritten;   // bytes of file data already written (for streaming large files)
        }
    }

    /// <summary>
    /// Faithful port of v1 CrcForce.cs (Nayuki algorithm).
    /// Uses the extended Euclidean algorithm for reciprocalMod and the correct
    /// bit-32 polynomial-reduction check — matching v1's output exactly.
    /// </summary>
    internal static class GcCrcForce
    {
        private const long _Polynomial = 0x104C11DB7L;

        public static uint Calculate(uint currentCrc, long totalLength, uint targetCrc, long patchOffset, uint existingValue)
        {
            uint tgt   = reverseBits(targetCrc);
            uint cur   = reverseBits(currentCrc);
            uint delta = cur ^ tgt;
            delta = (uint)multiplyMod(reciprocalMod(powMod(2, (totalLength - patchOffset) * 8)), delta & 0xFFFFFFFFL);
            uint result = existingValue ^ reverseBits(delta);
            return swapBytes(result);
        }

        private static uint swapBytes(uint x)
        {
            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00u) >> 8) | ((x & 0x00FF00FFu) << 8);
        }

        private static uint reverseBits(uint x)
        {
            uint result = 0;
            for (int i = 0; i < 32; i++)
                result = (result << 1) | ((x >> i) & 1u);
            return result;
        }

        // Russian peasant multiplication in GF(2^32).
        // Polynomial reduction uses bit 32 of x (v1 exact: ((x >> 32) & 1) != 0).
        private static long multiplyMod(long x, long y)
        {
            long z = 0;
            while (y != 0)
            {
                z ^= x * (y & 1);
                y >>= 1;
                x <<= 1;
                if (((x >> 32) & 1) != 0)
                    x ^= _Polynomial;
            }
            return z;
        }

        private static long powMod(long x, long y)
        {
            long z = 1;
            while (y != 0)
            {
                if ((y & 1) != 0)
                    z = multiplyMod(z, x);
                x = multiplyMod(x, x);
                y >>= 1;
            }
            return z;
        }

        // Extended Euclidean algorithm — v1 exact, avoids powMod overflow.
        private static long reciprocalMod(long x)
        {
            long y = x;
            x = _Polynomial;
            long a = 0;
            long b = 1;
            while (y != 0)
            {
                long[] divRem = divideAndRemainder(x, y);
                long c = a ^ multiplyMod(divRem[0], b);
                x = y;
                y = divRem[1];
                a = b;
                b = c;
            }
            return a;
        }

        private static long[] divideAndRemainder(long x, long y)
        {
            if (y == 0)
                throw new InvalidOperationException("GcCrcForce: division by zero");
            if (x == 0)
                return new long[] { 0, 0 };
            int ydeg = getDegree(y);
            long z = 0;
            for (int i = getDegree(x) - ydeg; i >= 0; i--)
            {
                if (((x >> (i + ydeg)) & 1) != 0)
                {
                    x ^= y << i;
                    z |= 1L << i;
                }
            }
            return new long[] { z, x };
        }

        private static int getDegree(long x)
        {
            int degree = 0;
            while (x != 0)
            {
                x = (long)((ulong)x >> 1); // logical shift to handle all bit patterns
                degree++;
            }
            return degree - 1;
        }
    }
}
