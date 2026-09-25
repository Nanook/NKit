using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Wii NKit decoder (partial of NKitAsIso).
    ///
    /// NKit Wii source layout:
    ///   [disc header 0x50000 incl. partition table @ 0x40000]
    ///   per partition (partition-table order):
    ///     [encoded filler gap before partition, if any]
    ///     [partition header block: ticket/TMD/cert/H3, size = field 0x2b8 &lt;&lt; 2]
    ///     [partition sub-header 0x440 (own NKit fields, dol/fst pointers)]
    ///     [hdr-to-FST region] [FST] [hash flags data — skipped, hashes deferred]
    ///     [files + gaps (same encoding as GameCube)]
    ///   [trailing filler to image size]
    ///
    /// Each partition's filesystem is decoded in FS space (contiguous, 0x7c00 sectors, no
    /// hashes), then converted to block space where each 0x8000 sector has a leading 0x400
    /// hash gap (left zero) via wiiFsLenToBlockLen. Junk is generated in FS space using the
    /// partition boot id. Hash generation and encryption are deferred to the parallel stage.
    /// </summary>
    internal partial class NKitAsIso
    {
        private void parseWii()
        {
            _segments = new List<Segment>();

            // Disc header (0x50000). NKitAsIso emits a self-describing DECODED image: the NKit
            // header block at 0x200 is cleared below so the source presents as a normal decoded ISO
            // (WiiGc.Image sees IsNkit=false, IsNkitDecoded=true). The correct repacked
            // partition-table offsets are written below, so no downstream patch step is needed —
            // a single-step conversion (e.g. straight to RVZ) works directly.
            _hdr = new byte[WiiConsts.WiiDiscHdrSize];
            srcCopy(0, _hdr, 0, _hdr.Length);

            // Clear the NKit header block (magic + fields) so the emitted disc header is clean and
            // the source presents as a normal decoded ISO (IsNkit=false downstream => no legacy
            // patch step, single-step conversion). Also zero the disc-header enc-hash marker at
            // 0x60. This is done for BOTH the update-retained and update-removed cases: NKitAsIso
            // now owns update-partition reinsertion itself (see reinsertRemovedUpdate below), so
            // the crashing legacy processNKitUpdatePartitionInfo path must NOT be triggered.
            Array.Clear(_hdr, WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize);
            _hdr.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0000);

            long srcPos = _hdr.Length;
            long dstPos = 0;

            _segments.Add(new Segment { IsoOffset = dstPos, Length = _hdr.Length, Type = SegmentType.Header });
            dstPos += _hdr.Length;

            // Disc-level junk parameters (filler between/after partitions). fillJunk() in the
            // base Read() uses _junkIdBytes/_discNo, so set them to the disc header values.
            _junkIdBytes = _hdr.Read(WiiConsts.DataHdrIdOffset, 4);
            _discNo = _hdr[WiiConsts.DataHdrDiscNoOffset];
            _junkBlock = new byte[NJunk.JunkBlockSize];

            // Update-removed images: the source disc-header table lists only the non-update
            // partitions, and a 0x8000 stub after the header holds the original partition table.
            // Reinsert the update partition into the output at its standard offset (0x50000),
            // rebuild the disc-header table to include it, and skip the source stub. Increment 1
            // fills the update region with NULLS (a valid disc; won't CRC-match until the real
            // recovery partition is inserted). Advances dstPos to the data-partition offset.
            if (NKitUpdateRemoved)
                reinsertRemovedUpdate(ref srcPos, ref dstPos);

            // Partitions to decode from the source. For the update-removed case the update was
            // already emitted by reinsertRemovedUpdate and its table entry written, so exclude it
            // here (its data is not in the source). The remaining (game/channel) partitions are
            // decoded normally and land at their repacked output offsets.
            List<WiiPartitionEntry> partitions = parseWiiPartitionTable(_hdr)
                .Where(p => !(NKitUpdateRemoved && p.Type == (uint)PartitionType.Update))
                .OrderBy(p => p.DiscOffset).ToList();

            // Two disc-level cursors, mirroring v1's Read():
            //   srcPos = position in the compact NKit source. Partition headers sit at their
            //            ORIGINAL disc offsets within the source; between a partition's data and
            //            the next partition header the source holds an encoded filler gap followed
            //            by zero padding up to that original offset.
            //   dstPos = position in the expanded OUTPUT image. Partitions are repacked here at
            //            their real decoded lengths; the partition table is rewritten to match
            //            (like v1's part.DiscOffset = dstPos; hdr.UpdateOffsets()).
            // Track the partition preceding each filler region so its junk matches the standard
            // SectionProcessor.createJunk rules: filler after a non-Game partition (e.g. Update),
            // or before the data-partition offset, is NULLS (fullSize=0); filler after a Game
            // partition uses that partition's JunkId. -1 type => no preceding partition (disc id).
            uint precedingType = 0xFFFFFFFF;
            byte[] precedingJunkId = null;
            long precedingJunkStart = 0; // preceding partition's JunkStartFsOffset (NJunk startOffset)

            foreach (WiiPartitionEntry part in partitions)
            {
                // Inter-partition filler. v1 keys this on the SOURCE cursor vs the partition's
                // ORIGINAL disc offset (part.DiscOffset > srcPos), not the output cursor. Decode
                // the encoded filler gap into the output, then discard the source zero-padding up
                // to the original offset so srcPos lands exactly on the next partition header.
                if (part.DiscOffset > srcPos)
                    srcPos = parseWiiFiller(srcPos, ref dstPos, part.DiscOffset, precedingType, precedingJunkId, precedingJunkStart);

                // Repack: this partition starts at the current output position. Rewrite the
                // partition table entry to the repacked offset (v1's UpdateOffsets equivalent).
                _hdr.WriteUInt32B(part.TableOffset, (uint)(dstPos / 4L));
                _hdr.WriteUInt32B(part.TableOffset + 4, part.Type);

                srcPos = parseWiiPartition(srcPos, ref dstPos, part, out byte[] partJunkId, out long partJunkStart);
                precedingType = part.Type;
                precedingJunkId = partJunkId;
                precedingJunkStart = partJunkStart;
            }

            // Trailing filler after the last partition, to the output image size.
            if (srcPos < _srcLength && dstPos < _imageSize)
                srcPos = parseWiiFiller(srcPos, ref dstPos, _imageSize, precedingType, precedingJunkId, precedingJunkStart);

            if (dstPos < _imageSize)
            {
                _segments.Add(new Segment { IsoOffset = dstPos, Length = _imageSize - dstPos, Type = SegmentType.Zero });
                dstPos = _imageSize;
            }
        }

        private struct WiiPartitionEntry
        {
            public long DiscOffset;
            public uint Type;
            public int TableOffset; // absolute offset within _hdr of this entry
        }

        // Mirrors ImageHeader.CreatePartitionInfos on the raw header bytes.
        private static List<WiiPartitionEntry> parseWiiPartitionTable(byte[] hdr)
        {
            List<WiiPartitionEntry> list = new List<WiiPartitionEntry>();
            int baseOff = WiiConsts.WiiDiscHdrPtnOffset; // 0x40000
            for (int tableIdx = 0; tableIdx < 4; tableIdx++)
            {
                uint count = hdr.ReadUInt32B(baseOff + (tableIdx * 8));
                if (count == 0)
                    continue;
                int tableOffset = (int)hdr.ReadUInt32B(baseOff + (tableIdx * 8) + 4) * 4;
                int entryBase = baseOff + (tableOffset - baseOff);
                for (int i = 0; i < count; i++)
                {
                    long partOffset = hdr.ReadUInt32B(entryBase + (i * 8)) * 4L;
                    uint partType = hdr.ReadUInt32B(entryBase + (i * 8) + 4);
                    list.Add(new WiiPartitionEntry { DiscOffset = partOffset, Type = partType, TableOffset = entryBase + (i * 8) });
                }
            }
            return list;
        }

        // Reinsert a removed Wii update partition into the OUTPUT and rebuild the disc-header
        // partition table so the emitted disc is complete and self-describing (no downstream
        // legacy patch/insert path). The compact update-removed source holds:
        //   [disc header 0x50000, table lists only non-update partitions]
        //   [0x8000 stub containing the ORIGINAL partition table]
        //   [first real (game) partition ...]
        // We place the update partition at WiiDefaultUpdatePtnOffset (0x50000) and the following
        // partitions at WiiDefaultDataPtnOffset (0xF800000, standard), and rewrite table 0 to list
        // the update first. Increment 1: the update region is emitted as NULLS (valid disc; will
        // not match the original CRC until the real recovery partition is inserted). The source
        // stub is skipped by the normal inter-partition filler in the main loop (its filler runs
        // from srcPos=0x50000 up to the first real partition's source offset).
        private void reinsertRemovedUpdate(ref long srcPos, ref long dstPos)
        {
            long updateStart = dstPos;                          // == WiiDiscHdrSize == 0x50000
            long dataStart = WiiConsts.WiiDefaultDataPtnOffset; // 0xF800000

            // Ask FixData for the matching recovery update partition (basic CRC lookup owned by
            // FixData). NKitAsIso owns the reinsertion policy; FixData owns the lookup. Null => no
            // recovery file: null-fill the region (valid, non-crashing disc).
            FixPartition up = _recoveryFixData?.FindUpdatePartition(_updatePartitionCrc);
            if (up != null && !string.IsNullOrEmpty(up.Filename) && up.Length > 0)
            {
                _updatePartitionFile = up.Filename;
                _updatePartitionFileLen = up.Length;
                _updatePartitionDisplayName = FixData.GetUpdatePartitionDisplayName(up) ?? up.Filename;
            }
            else
                _recoveryLog?.Invoke($"Update partition [*_{_updatePartitionCrc:X8}] not found in recovery files - filling with nulls");

            bool haveRecovery = _updatePartitionFile != null && _updatePartitionFileLen > 0;

            if (haveRecovery)
            {
                // Emit the recovery update partition verbatim (raw on-disc encrypted+hashed bytes)
                // at 0x50000, then null-fill the remaining gap up to the data-partition offset.
                // Record [start,end) so the downstream reader passes this region through unchanged
                // (already encrypted+hashed — no re-encrypt/re-hash).
                long fileLen = Math.Min(_updatePartitionFileLen, dataStart - updateStart);
                _segments.Add(new Segment { IsoOffset = updateStart, Length = fileLen, Type = SegmentType.UpdateFile, SrcOffsetL = 0 });
                _insertedUpdateStart = updateStart;
                _insertedUpdateEnd = dataStart;
                if (dataStart > updateStart + fileLen)
                    _segments.Add(new Segment { IsoOffset = updateStart + fileLen, Length = dataStart - (updateStart + fileLen), Type = SegmentType.Zero });
                dstPos = dataStart;
            }
            else if (dataStart > updateStart)
            {
                // No recovery file: null-fill the whole update region. A null region has no valid
                // partition header, so it is NOT listed as an Update partition (see table below) —
                // just leading filler. Valid, non-crashing disc (won't CRC-match the original).
                _segments.Add(new Segment { IsoOffset = updateStart, Length = dataStart - updateStart, Type = SegmentType.Zero });
                dstPos = dataStart;
            }

            int baseOff = WiiConsts.WiiDiscHdrPtnOffset;        // 0x40000
            List<WiiPartitionEntry> existing = parseWiiPartitionTable(_hdr).OrderBy(p => p.DiscOffset).ToList();

            // Skip the source stub (the 0x8000 region after the disc header holding the original
            // partition table) so srcPos lands on the first real partition's source bytes. Mirrors
            // the legacy reader's `_stream.Seek(WiiSectorSize)` for update-removed images.
            if (existing.Count > 0)
                srcPos = existing[0].DiscOffset;   // first real partition's SOURCE offset (e.g. 0x58000)

            // Rebuild table 0. When the recovery update partition was inserted, entry 0 is the
            // Update partition at 0x50000; the existing partitions follow. Without recovery, the
            // update is null-filled and NOT listed (the existing partitions are the only entries).
            // The main loop rewrites each existing partition's entry with its repacked OUTPUT
            // offset as it emits (parseWiiPartitionTable is re-read after this).
            Array.Clear(_hdr, baseOff, WiiConsts.WiiDiscHdrPtnSize);

            int entryPtr = baseOff + 0x20;                      // 0x40020
            int count = existing.Count + (haveRecovery ? 1 : 0);
            _hdr.WriteUInt32B(baseOff, (uint)count);
            _hdr.WriteUInt32B(baseOff + 4, (uint)(entryPtr / 4)); // pointer/4

            int idx = 0;
            if (haveRecovery)
            {
                // Entry 0: the reinserted update partition at its final output offset (0x50000).
                _hdr.WriteUInt32B(entryPtr, (uint)(WiiConsts.WiiDefaultUpdatePtnOffset / 4L));
                _hdr.WriteUInt32B(entryPtr + 4, (uint)PartitionType.Update);
                idx = 1;
            }
            foreach (WiiPartitionEntry p in existing)
            {
                int eOff = entryPtr + (idx * 8);
                // Preserve the partition's ORIGINAL SOURCE offset: parseWiiPartitionTable is
                // re-read after this and the main loop uses DiscOffset as the SOURCE cursor target.
                // The loop overwrites this entry with the repacked OUTPUT offset as it emits.
                _hdr.WriteUInt32B(eOff, (uint)(p.DiscOffset / 4L));
                _hdr.WriteUInt32B(eOff + 4, p.Type);
                idx++;
            }
        }

        // FS-space length -> block-space length for the standard Wii partition layout (each
        // 0x8000 sector = 0x400 hash gap + 0x7c00 data). Inlined from WiiBlockLayout.Wii's
        // FsLengthToBlockLength(0, fsLength) so this decoder owns its own block-layout math and
        // is not coupled to that helper class:
        //   FsLengthToBlockLength(0, len) = FsToBlock(len) - FsToBlock(0)
        //   FsToBlock(o) = (o / 0x7c00) * 0x8000 + 0x400 + (o % 0x7c00)   [+0x400 only, when o%0x7c00==0]
        //   FsToBlock(0) = 0x400
        private static long wiiFsLenToBlockLen(long fsLength)
        {
            if (fsLength == 0)
                return 0;
            long fsBlock = FsToBlock(fsLength) - FsToBlock(0);
            return fsBlock;

            static long FsToBlock(long fsOffset)
            {
                long rm = fsOffset % WiiConsts.WiiSectorFsSize;
                long offset = fsOffset / WiiConsts.WiiSectorFsSize * WiiConsts.WiiSectorSize;
                offset += WiiConsts.WiiSectorHashSize + (rm != 0 ? rm : 0);
                return offset;
            }
        }

        // WiiHashStore flags length for a partition FS size (mirrors WiiHashStore.intsCount).
        private static int wiiHashFlagsLength(long partitionFsSize)
        {
            long size = partitionFsSize / WiiConsts.WiiSectorFsSize * WiiConsts.WiiSectorSize;
            long groups = (size / WiiConsts.WiiGroupSize) + (size % WiiConsts.WiiGroupSize == 0L ? 0L : 1L);
            int ints = (int)((groups / 32L) + (groups % 32 == 0L ? 0L : 1L));
            return ints * 4;
        }

        // Decode inter-partition / trailing filler. Mirrors v1's writeFiller + trailing zero-skip.
        //
        // The source holds an encoded filler gap (4-byte-prefixed, same encoding as file gaps)
        // followed by zero padding up to the next partition header's ORIGINAL disc offset. This
        // method:
        //   1. Decodes the encoded filler gap into the OUTPUT (Zero/Junk/Fill segments), which
        //      advances dstPos by the decoded gap length and srcPos by the (small) gap words.
        //   2. Discards the remaining SOURCE zero padding up to targetSrcPos, advancing srcPos
        //      only (this padding produces no output; the output disc gap is the decoded gap).
        //
        // For the trailing filler after the last partition, targetSrcPos is the source length.
        private long parseWiiFiller(long srcPos, ref long dstPos, long targetSrcPos, uint precedingType, byte[] precedingJunkId, long precedingJunkStart)
        {
            long srcFillerLen = targetSrcPos - srcPos;
            if (srcFillerLen <= 0)
                return srcPos;

            // Determine the disc-filler junk context, matching SectionProcessor.createJunk:
            //   - after a Game partition (type 0): real junk keyed on that partition's JunkId and
            //     JunkStartFsOffset (the createJunk post-Game 'Other' rule)
            //   - otherwise (after Update/other, or no preceding partition): NULLS (fullSize=0)
            // discNo is always the disc header's value.
            bool fillerIsJunk = precedingType == (uint)PartitionType.Game && precedingJunkId != null;
            // Junk id/startOffset for the emitted Junk segments. Must be the GAME partition's boot
            // JunkId + JunkStartFsOffset — NOT the disc-header id/0 (fillJunk's disc-level default),
            // or the trailing post-Game junk mismatches (root cause of the Wii_Backup_USA decode bug).
            byte[] fillerJunkId = fillerIsJunk ? precedingJunkId : _junkIdBytes;
            long fillerJunkStart = fillerIsJunk ? precedingJunkStart : 0;

            // Decode the encoded filler gap directly against the buffered source (long offsets),
            // emitting disc-space Zero/Junk/Fill/WiiSrcData segments. The gap word's size drives
            // the OUTPUT length; the source consumed is just the encoding words.
            long nullsPos = dstPos + WiiConsts.DataNullsCount;
            srcPos = decodeWiiDiscGap(srcPos, ref dstPos, ref nullsPos, true, srcFillerLen, fillerIsJunk, fillerJunkId, fillerJunkStart);

            // Discard any residual source zero padding up to the next partition's original offset.
            if (srcPos < targetSrcPos)
                srcPos = targetSrcPos;

            return srcPos;
        }

        // Disc-space filler gap decoder. Mirrors parseGcGap's logic but reads control words from
        // the buffered source via srcReadU32B and emits disc-space segments (Zero/Junk/Fill and
        // store-backed WiiSrcData). Junk resolves as disc-level junk at read time.
        private long decodeWiiDiscGap(long srcPos, ref long dstPos, ref long nullsPos, bool firstOrLastFile, long nkitGapLen, bool fillerIsJunk, byte[] fillerJunkId, long fillerJunkStart)
        {
            // When this filler region is not a junk region (e.g. it follows the Update partition
            // or precedes the data partition), the standard reader produces NULLS there
            // (createJunk fullSize=0). In that case emit Zero for every would-be junk run.
            SegmentType junkSeg = fillerIsJunk ? SegmentType.Junk : SegmentType.Zero;
            // Build a Junk (or Zero) filler segment carrying the resolved junk context so fillJunk
            // uses the correct id/startOffset (the preceding Game partition's), not the disc default.
            Segment junkFiller(long off, long len) => junkSeg == SegmentType.Junk
                ? new Segment { IsoOffset = off, Length = len, Type = SegmentType.Junk, JunkIdOverride = fillerJunkId, JunkStartOffset = fillerJunkStart }
                : new Segment { IsoOffset = off, Length = len, Type = SegmentType.Zero };
            if (nkitGapLen == 0)
                return srcPos;
            if (srcPos + 4 > _srcLength)
                return srcPos;

            long size = srcReadU32B(srcPos); srcPos += 4;
            GapType gt = (GapType)(size & 0b11);
            size &= 0xFFFFFFFC;
            if (size == 0xFFFFFFFC) { size = 0xFFFFFFFCL + srcReadU32B(srcPos); srcPos += 4; }

            if (gt == GapType.JunkFile)
            {
                nullsPos = dstPos + Math.Min(nullsPos - dstPos, 0);
                long nulls = (size & 0xFC) >> 2;
                long junkFileLen = srcReadU32B(srcPos); srcPos += 4;
                long jfAligned = junkFileLen + (junkFileLen % 4 == 0 ? 0 : 4 - (junkFileLen % 4));
                if (jfAligned > 0)
                {
                    if (nulls > 0) { _segments.Add(new Segment { IsoOffset = dstPos, Length = nulls, Type = SegmentType.Zero }); dstPos += nulls; }
                    long jp = jfAligned - nulls;
                    if (jp > 0) { _segments.Add(junkFiller(dstPos, jp)); dstPos += jp; }
                }
                if (nkitGapLen <= 8) return srcPos;
                size = srcReadU32B(srcPos); srcPos += 4;
                gt = (GapType)(size & 0b11);
                size &= 0xFFFFFFFC;
            }

            if (size == 0) return srcPos;

            long maxNulls = Math.Max(0, nullsPos - dstPos);
            long gapNulls = size < maxNulls ? size : (size >= 0x40000 && !firstOrLastFile ? 0 : maxNulls);

            if (gt == GapType.AllJunk)
            {
                if (gapNulls > 0) { _segments.Add(new Segment { IsoOffset = dstPos, Length = gapNulls, Type = SegmentType.Zero }); dstPos += gapNulls; }
                long jp = size - gapNulls;
                if (jp > 0) { _segments.Add(junkFiller(dstPos, jp)); dstPos += jp; }
            }
            else if (gt == GapType.AllBlockFilled)
            {
                _segments.Add(new Segment { IsoOffset = dstPos, Length = size, Type = SegmentType.Zero });
                dstPos += size;
            }
            else // Mixed
            {
                long prg = size;
                byte btByte = 0x00;
                GapBlockType bt = GapBlockType.Junk;
                while (prg > 0)
                {
                    if (srcPos + 4 > _srcLength) break;
                    long blk = srcReadU32B(srcPos); srcPos += 4;
                    GapBlockType btType = (GapBlockType)(blk >> 30);
                    bool btRepeat = btType == GapBlockType.Repeat;
                    if (!btRepeat) bt = btType;
                    long cnt = 0x3FFFFFFF & blk;
                    long bytes;

                    if (bt == GapBlockType.NonJunk)
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        long toCopy = Math.Min(bytes, _srcLength - srcPos);
                        if (toCopy > 0)
                        {
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = toCopy, Type = SegmentType.WiiSrcData, SrcOffsetL = srcPos });
                            srcPos += toCopy;
                            dstPos += toCopy;
                        }
                        if (bytes - toCopy > 0)
                        {
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = bytes - toCopy, Type = SegmentType.Zero });
                            dstPos += bytes - toCopy;
                        }
                    }
                    else if (bt == GapBlockType.ByteFill)
                    {
                        if (!btRepeat) { btByte = (byte)(0xFF & cnt); cnt >>= 8; }
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        if (btByte == 0x00)
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = bytes, Type = SegmentType.Zero });
                        else
                            _segments.Add(new Segment { IsoOffset = dstPos, Length = bytes, Type = SegmentType.Fill, FillByte = btByte });
                        dstPos += bytes;
                    }
                    else // Junk
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        maxNulls = Math.Max(0, nullsPos - dstPos);
                        long localNulls = prg < maxNulls ? bytes : (bytes >= 0x40000 && !firstOrLastFile ? 0 : Math.Min(maxNulls, bytes));
                        if (localNulls > 0) { _segments.Add(new Segment { IsoOffset = dstPos, Length = localNulls, Type = SegmentType.Zero }); }
                        long jb = bytes - localNulls;
                        if (jb > 0) { _segments.Add(junkFiller(dstPos + localNulls, jb)); }
                        dstPos += bytes;
                    }
                    prg -= bytes;
                }
            }
            return srcPos;
        }

        // Decode one Wii partition into an on-demand block-space map segment (no expansion).
        // Outputs the partition's junk id (boot id bytes 0-3) and JunkStartFsOffset for
        // inter-partition/trailing filler junk context (matching SectionProcessor.createJunk).
        private long parseWiiPartition(long srcPos, ref long dstPos, WiiPartitionEntry part, out byte[] partJunkIdOut, out long partJunkStartOut)
        {
            // Partition header block (ticket/TMD/cert/H3). Size from field 0x2b8 (<<2):
            // 0x20000 for retail, 0x8000 for RVT-H. Read the value from the header itself.
            int partHeaderBlockSize = (int)(srcReadU32B(srcPos + WiiConsts.WiiPrtHdrSizeOffset) * 4L);
            byte[] partHeaderBlock = new byte[partHeaderBlockSize];
            srcCopy(srcPos, partHeaderBlock, 0, partHeaderBlockSize);
            srcPos += partHeaderBlockSize;

            _segments.Add(new Segment { IsoOffset = dstPos, Length = partHeaderBlockSize, Type = SegmentType.Buffer, Buffer = partHeaderBlock });
            dstPos += partHeaderBlockSize;

            // Partition sub-header (0x440 boot header) follows the header block in the source.
            byte[] subHdr = new byte[WiiConsts.BootBinSize];
            srcCopy(srcPos, subHdr, 0, subHdr.Length);
            long subSrc = srcPos + subHdr.Length;

            // Wiped/invalid partition case: boot header id is all-zero. The FS size follows as a
            // single uint32 (hashed size /4). (v1 partitionStreamWrite "\0\0\0\0" branch.)
            bool wipedPartition = subHdr.ReadUInt32B(0) == 0;
            long partHashedSize;
            if (wipedPartition)
            {
                partHashedSize = srcReadU32B(subSrc) * 4L;
                subSrc += 4;
            }
            else
            {
                partHashedSize = subHdr.ReadUInt32B(WiiConsts.NKitHeaderPos + WiiConsts.NKitHdrSrcLenOffset) * 4L;
            }
            long partFsSize = Nanook.NKit.Buffer.HashedLenToFsLen(partHashedSize, WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);

            // Restore the real (hashed) partition data size into the partition header block at
            // 0x2bc. In the NKit source this field holds the shrunk/encoded size; the standard
            // WiiGc.Image reader derives the partition data length from 0x2bc x multiplier, so it
            // must be the real hashed size / 4. Mirrors v1's
            // patchInfo.PartitionHeader.WriteUInt32B(0x2bc, ...). partHeaderBlock is referenced by
            // the Buffer segment already emitted, so patching it here still applies at read time.
            partHeaderBlock.WriteUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset, (uint)(partHashedSize / 4L));

            byte[] partJunkId = subHdr.Read(WiiConsts.DataHdrIdOffset, 4);
            int partDiscNo = subHdr[WiiConsts.DataHdrDiscNoOffset];

            // Clear NKit fields; set encrypted-hash marker the standard image expects.
            Array.Clear(subHdr, WiiConsts.NKitHeaderPos, WiiConsts.NKitHeaderSize);
            subHdr.WriteUInt16B(WiiConsts.DataHdrEncHashOffset, 0x0101);

            long fstOffset = subHdr.ReadUInt32B(WiiConsts.FstPtrOffset) * 4L;
            long mainDolAddr = subHdr.ReadUInt32B(WiiConsts.DolPtrOffset) * 4L;
            int fstSize = (int)(subHdr.ReadUInt32B(WiiConsts.FstSizeOffset) * 4L);

            int hdrToFstSize = (int)(fstOffset - subHdr.Length);
            byte[] pHdrToFst = new byte[hdrToFstSize];
            srcCopy(subSrc, pHdrToFst, 0, hdrToFstSize);
            subSrc += hdrToFstSize;

            byte[] pFst = new byte[fstSize];
            srcCopy(subSrc, pFst, 0, fstSize);
            subSrc += fstSize;

            // Read the hash flags bitmask (which groups have preserved hashes that couldn't be
            // recreated). We defer hash generation, so we don't emit these — but we must consume
            // the trailing preserved-hash DATA after the files+gaps, sized by this bitmask.
            int flagsLen = wiiHashFlagsLength(partFsSize);
            byte[] hashFlags = new byte[flagsLen];
            srcCopy(subSrc, hashFlags, 0, flagsLen);

            subSrc += flagsLen;

            // Build the FS-space DECODE MAP (no expansion). fsPos is the output cursor in the
            // partition's contiguous FS space; runs describe how to generate each range on demand.
            // The leading sub-header + hdrToFst + FST region is captured as a small HeadOverlay
            // (it is tiny and gets patched with corrected offsets); the rest is Data/Junk/Zero/Fill
            // runs referencing the buffered source.
            List<WiiFsRun> runs = new List<WiiFsRun>();
            long fsPos = 0;
            long headLen = subHdr.Length + pHdrToFst.Length + pFst.Length;
            long fstFsPos = subHdr.Length + pHdrToFst.Length;
            fsPos = headLen; // the head region is covered by HeadOverlay, emitted after patching

            Fst parsedFst = Fst.Parse(pFst, 0, null, 4L, out FstParseStatus fstStatus);

            // Hacked/invalid FST (an entry pointing to offset 0, e.g. the FreeLoader disc). There
            // is no usable file list to drive the per-file gap/junk walk. NKit v1 handles this
            // "bad image" case (NkitReaderWii: conFiles == null) by treating the ENTIRE post-FST
            // region as a single gap and decoding it through the gap decoder — NOT a verbatim copy
            // (gap regions are junk/nulls/fill-encoded in the NKit source). Mirror that here: one
            // decodeWiiFsGap over the whole remaining FS region, as the first-and-last file, so the
            // partition still reconstructs (and CRCs) correctly while the FST stays "invalid".
            if (fstStatus == FstParseStatus.InvalidEntry || parsedFst == null || parsedFst.Files == null || parsedFst.Files.Count == 0)
            {
                long badNullsPos = fsPos + WiiConsts.DataNullsCount;
                long badGapLen = partFsSize - fsPos;
                if (badGapLen > 0 && fsPos < partFsSize)
                    subSrc = decodeWiiFsGap(subSrc, runs, ref fsPos, ref badNullsPos, null, true, badGapLen, partFsSize);

                // Pad any residual slack to the exact partition FS size.
                if (fsPos < partFsSize)
                    addFsZero(runs, ref fsPos, partFsSize - fsPos, partFsSize);

                // Capture preserved hashes verbatim (see the valid-FST path). A hacked disc like
                // FreeLoader relies on this: its hashes are not recreatable, so they must be
                // reproduced exactly rather than regenerated.
                subSrc += capturePreservedHashes(subSrc, hashFlags, partFsSize, out byte[] invalidPreservedHashes, out Dictionary<long, long> invalidPreservedMap);

                byte[] invalidHeadOverlay = new byte[headLen];
                Array.Copy(subHdr, 0, invalidHeadOverlay, 0, subHdr.Length);
                Array.Copy(pHdrToFst, 0, invalidHeadOverlay, subHdr.Length, pHdrToFst.Length);
                Array.Copy(pFst, 0, invalidHeadOverlay, fstFsPos, pFst.Length);

                WiiPartitionMap invalidMap = new WiiPartitionMap
                {
                    PartFsSize = partFsSize,
                    JunkId = partJunkId,
                    DiscNo = partDiscNo,
                    JunkStartFsOffset = fstOffset + fstSize + (fstOffset + fstSize == 0 ? 0L : WiiConsts.DataNullsCount),
                    Runs = runs,
                    HeadOverlay = invalidHeadOverlay,
                    PreservedHashes = invalidPreservedHashes,
                    PreservedHashMap = invalidPreservedMap
                };

                long invalidBlockLen = wiiFsLenToBlockLen(partFsSize);
                _segments.Add(new Segment { IsoOffset = dstPos, Length = invalidBlockLen, Type = SegmentType.WiiPartition, Map = invalidMap });
                dstPos += invalidBlockLen;

                partJunkIdOut = partJunkId;
                partJunkStartOut = invalidMap.JunkStartFsOffset;
                return subSrc;
            }

            List<IFsFile> files = parsedFst.Files;

            // Two independent cursors, exactly like v1's partitionStreamWrite:
            //   fsPos  = OUTPUT cursor in FS space (files sit at their FsOffset; gaps fill between)
            //   subSrc = SOURCE cursor into the packed NKit source (files packed back to back,
            //            each followed by its encoded gap words; no output-space padding)
            long nullsPos = fsPos + WiiConsts.DataNullsCount;

            // Issue 3: the hash flags bitmask sits between the FST and the first file in the
            // source, so the encoded first-gap length is inflated by flagsLen. v1 corrects this
            // with `conFiles[0].GapLength -= hashes.FlagsLength`. Mirror that here.
            long firstGapLen = ((FstFile)files[0]).FsOffset - fsPos - flagsLen;
            if (firstGapLen > 0 && fsPos < partFsSize)
                subSrc = decodeWiiFsGap(subSrc, runs, ref fsPos, ref nullsPos, (FstFile)files[0], true, firstGapLen, partFsSize);

            for (int i = 0; i < files.Count; i++)
            {
                FstFile ff = (FstFile)files[i];
                bool isLast = i == files.Count - 1;

                // Place the output cursor at the file's FS offset. The preceding gap should have
                // advanced fsPos to here; fill any residual slack with zero.
                if (fsPos < ff.FsOffset)
                    addFsZero(runs, ref fsPos, ff.FsOffset - fsPos, partFsSize);

                if (ff.FsOffset == mainDolAddr)
                    subHdr.WriteUInt32B(WiiConsts.DolPtrOffset, (uint)(fsPos / 4L));
                pFst.WriteUInt32B(ff.FstPtrOffset, (uint)(fsPos / 4L));

                long fileSize = ff.FsSize;
                // 4-byte-aligned file length (files are stored 4-aligned in the source, and v1
                // computes the post-file gap from the ALIGNED end). Using the unaligned end left a
                // tiny (1-3 byte) residual "gap" that made us read the next file's data as a gap
                // encoding word — corrupting srcPos for the rest of the partition.
                long fileAligned = fileSize + (fileSize % 4 == 0 ? 0 : 4 - (fileSize % 4));
                if (fileSize > 0)
                {
                    // The packed NKit source ALWAYS holds the full 4-aligned file bytes, so the
                    // SOURCE cursor must advance by the full fileAligned (v1: srcPos += size,
                    // unconditional). Only the OUTPUT FS run may be clamped to the partition end
                    // (defensive — don't emit past partFsSize). Clamping the source advance too
                    // (the old Math.Min on subSrc) under-consumed the source whenever a file's
                    // aligned end fell within the final partition group, desyncing subSrc so every
                    // later gap/file read the wrong bytes — corrupting the rebuilt FST on large
                    // (dual-layer) partitions. Keep the two clamps independent.
                    long runLen = Math.Min(fileAligned, partFsSize - fsPos);
                    if (runLen > 0)
                    {
                        addFsRun(runs, ref fsPos, new WiiFsRun
                        {
                            Kind = WiiFsRunKind.Data,
                            FsLength = runLen,
                            SrcOffset = subSrc
                        });
                    }
                    subSrc += fileAligned;   // consume the FULL packed source (not the clamped run)
                    nullsPos = fsPos + WiiConsts.DataNullsCount;
                }

                // Gap after this file, computed from the ALIGNED file end (matches v1's
                // conFiles gap calc). Alignment remainders are NOT gaps and have no encoding.
                long fileEnd = ff.FsOffset + fileAligned;
                long nextFsOffset = isLast ? partFsSize : ((FstFile)files[i + 1]).FsOffset;
                long gapLen = nextFsOffset - fileEnd;

                if (gapLen > 0 && fsPos < partFsSize)
                {
                    // The compacted source reserves EXACTLY gapLen bytes for a non-last file's
                    // post-gap: the gap encoding word(s) followed by any raw-null padding up to the
                    // next file. The main-branch decoder consumes the whole region
                    // (_nkitGap = new byte[inF.PostGapSize]); an AllJunk/AllBlockFilled gap only
                    // reads the 4-8 byte word (junk is regenerated, not stored), so the source
                    // cursor must still advance the FULL gapLen. The streaming decode advanced only
                    // by the words it read, leaving subSrc short whenever the compacted gap exceeded
                    // the encoding size — which desynced every later file (dual-layer FST corruption).
                    // The LAST file is excluded: its trailing "gap" is junk-to-partition-end, which
                    // is regenerated and NOT backed by source bytes.
                    long gapSrcStart = subSrc;
                    subSrc = decodeWiiFsGap(subSrc, runs, ref fsPos, ref nullsPos, ff, isLast, gapLen, partFsSize);
                    if (!isLast && subSrc - gapSrcStart < gapLen)
                        subSrc = gapSrcStart + gapLen;
                }
                else if (ff.FsSize == 0)
                    nullsPos = fsPos + WiiConsts.DataNullsCount;
            }

            // Pad the map with zeros to the exact partition FS size (defensive; the walk should
            // already land on partFsSize).
            if (fsPos < partFsSize)
                addFsZero(runs, ref fsPos, partFsSize - fsPos, partFsSize);

            // Capture the trailing preserved-hash data (after all files+gaps). NKit read reproduces
            // the disc EXACTLY: groups whose hashes could not be recreated at encode time are stored
            // here and copied back into the block-space hash gaps at read time (never regenerated).
            subSrc += capturePreservedHashes(subSrc, hashFlags, partFsSize, out byte[] preservedHashes, out Dictionary<long, long> preservedMap);

            // Build the small head overlay (patched sub-header + hdrToFst + patched FST) that
            // covers FS offset 0. Offsets in subHdr/pFst were patched during the walk.
            byte[] headOverlay = new byte[headLen];
            Array.Copy(subHdr, 0, headOverlay, 0, subHdr.Length);
            Array.Copy(pHdrToFst, 0, headOverlay, subHdr.Length, pHdrToFst.Length);
            Array.Copy(pFst, 0, headOverlay, fstFsPos, pFst.Length);

            WiiPartitionMap map = new WiiPartitionMap
            {
                PartFsSize = partFsSize,
                JunkId = partJunkId,
                DiscNo = partDiscNo,
                JunkStartFsOffset = fstOffset + fstSize + (fstOffset + fstSize == 0 ? 0L : WiiConsts.DataNullsCount),
                Runs = runs,
                HeadOverlay = headOverlay,
                PreservedHashes = preservedHashes,
                PreservedHashMap = preservedMap
            };

            long blockLen = wiiFsLenToBlockLen(partFsSize);
            _segments.Add(new Segment { IsoOffset = dstPos, Length = blockLen, Type = SegmentType.WiiPartition, Map = map });
            dstPos += blockLen;

            partJunkIdOut = partJunkId;
            partJunkStartOut = map.JunkStartFsOffset;
            return subSrc;
        }

        // Append a run at the current fsPos and advance fsPos by its length.
        private void addFsRun(List<WiiFsRun> runs, ref long fsPos, WiiFsRun run)
        {
            if (run.FsLength <= 0) return;
            run.FsOffset = fsPos;
            runs.Add(run);
            fsPos += run.FsLength;
        }

        // Append a zero-fill run and advance fsPos.
        private void addFsZero(List<WiiFsRun> runs, ref long fsPos, long length, long partFsSize)
        {
            if (length <= 0) return;
            runs.Add(new WiiFsRun { FsOffset = fsPos, FsLength = length, Kind = WiiFsRunKind.Zero });
            fsPos += length;
        }

        // Whether the flag bit for the group at hashed offset `off` is set.
        // Mirrors WiiHashStore.IsPreserved: bit index = off / WiiGroupSize, MSB-first per byte.
        private static bool isGroupPreserved(byte[] hashFlags, long off)
        {
            int x = (int)(off / WiiConsts.WiiGroupSize);
            int byt = x / 8;
            if (hashFlags == null || hashFlags.Length <= byt)
                return false;
            int bit = 1 << (7 - (x % 8));
            return (hashFlags[byt] & bit) != 0;
        }

        // Read the preserved-hash blob from the source at `srcOffset` and build the group->byte
        // offset map, mirroring WiiHashStore.ReadPatchData. NKit read reproduces the disc EXACTLY,
        // so groups whose hashes could not be recreated at encode time are stored verbatim and must
        // be copied back into the block-space hash gaps (never regenerated). The blob is packed per
        // preserved group in ascending block-space (hashed) offset order; each group contributes
        // (groupHashedSize / WiiSectorSize * WiiSectorHashSize) bytes. Returns the blob + map, and
        // the number of source bytes consumed (== blob length).
        private long capturePreservedHashes(long srcOffset, byte[] hashFlags, long partitionFsSize,
            out byte[] preserved, out Dictionary<long, long> map)
        {
            long partHashedSize = partitionFsSize / WiiConsts.WiiSectorFsSize * WiiConsts.WiiSectorSize;
            map = new Dictionary<long, long>();
            long hashOff = 0;
            for (long off = 0; off < partHashedSize; off += WiiConsts.WiiGroupSize)
            {
                long sz = Math.Min(partHashedSize - off, WiiConsts.WiiGroupSize); // partial last group
                if (isGroupPreserved(hashFlags, off))
                {
                    map[off] = hashOff; // key = group block-space (hashed) offset = section AreaOffset
                    hashOff += sz / WiiConsts.WiiSectorSize * WiiConsts.WiiSectorHashSize;
                }
            }
            int total = (int)hashOff;
            preserved = new byte[total];
            if (total > 0)
                srcCopy(srcOffset, preserved, 0, total);
            if (map.Count == 0)
            {
                preserved = null;
                map = null;
            }
            return total;
        }

        // Decode a gap into FS-space map RUNS (no expansion). Mirrors parseGcGap's nulls/junk
        // logic exactly, but instead of writing bytes it appends Data/Junk/Zero/Fill runs that
        // describe how to produce the range on demand. Junk runs generate FS-space partition junk
        // at read time; Data runs reference the buffered source. srcPos is a long (Wii sources +
        // offsets can exceed 2 GiB in theory).
        private long decodeWiiFsGap(long srcPos, List<WiiFsRun> runs, ref long fsPos, ref long nullsPos, FstFile file, bool firstOrLastFile, long nkitGapLen, long partFsSize)
        {
            if (nkitGapLen == 0)
            {
                if (file != null && file.FsSize == 0) nullsPos = fsPos + WiiConsts.DataNullsCount;
                return srcPos;
            }
            if (srcPos + 4 > _srcLength) return srcPos;

            long size = srcReadU32B(srcPos); srcPos += 4;
            GapType gt = (GapType)(size & 0b11);
            size &= 0xFFFFFFFC;
            if (size == 0xFFFFFFFC) { size = 0xFFFFFFFCL + srcReadU32B(srcPos); srcPos += 4; }
            if (gt == GapType.JunkFile)
            {
                nullsPos = fsPos + Math.Min(nullsPos - fsPos, 0);
                long nulls = (size & 0xFC) >> 2;
                long junkFileLen = srcReadU32B(srcPos); srcPos += 4;

                long jfAligned = junkFileLen + (junkFileLen % 4 == 0 ? 0 : 4 - (junkFileLen % 4));
                if (jfAligned > 0)
                {
                    addFsZero(runs, ref fsPos, Math.Min(nulls, jfAligned), partFsSize);
                    if (jfAligned - nulls > 0)
                        addFsRun(runs, ref fsPos, new WiiFsRun { Kind = WiiFsRunKind.Junk, FsLength = jfAligned - nulls });
                }
                if (nkitGapLen <= 8) return srcPos;
                size = srcReadU32B(srcPos); srcPos += 4;
                gt = (GapType)(size & 0b11);
                size &= 0xFFFFFFFC;
            }
            else if (file != null && file.FsSize == 0)
                nullsPos = fsPos + WiiConsts.DataNullsCount;

            if (size == 0) return srcPos;

            long maxNulls = Math.Max(0, nullsPos - fsPos);
            long gapNulls = size < maxNulls ? size : (size >= 0x40000 && !firstOrLastFile ? 0 : maxNulls);

            if (gt == GapType.AllJunk)
            {
                addFsZero(runs, ref fsPos, gapNulls, partFsSize);
                if (size - gapNulls > 0)
                    addFsRun(runs, ref fsPos, new WiiFsRun { Kind = WiiFsRunKind.Junk, FsLength = size - gapNulls });
            }
            else if (gt == GapType.AllBlockFilled)
            {
                addFsZero(runs, ref fsPos, size, partFsSize); // scrubbed zeros
            }
            else // Mixed
            {
                long prg = size;
                byte btByte = 0x00;
                GapBlockType bt = GapBlockType.Junk;
                while (prg > 0)
                {
                    if (srcPos + 4 > _srcLength) break;
                    long blk = srcReadU32B(srcPos); srcPos += 4;
                    GapBlockType btType = (GapBlockType)(blk >> 30);
                    bool btRepeat = btType == GapBlockType.Repeat;
                    if (!btRepeat) bt = btType;
                    long cnt = 0x3FFFFFFF & blk;
                    long bytes;

                    if (bt == GapBlockType.NonJunk)
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        long toCopy = Math.Min(bytes, _srcLength - srcPos);
                        if (toCopy > 0)
                        {
                            addFsRun(runs, ref fsPos, new WiiFsRun { Kind = WiiFsRunKind.Data, FsLength = toCopy, SrcOffset = srcPos });
                            srcPos += toCopy;
                        }
                        // If the source ran short, the remainder of `bytes` is left as an implicit
                        // gap; fsPos is advanced below by the full `bytes` via the padding zero.
                        if (bytes - toCopy > 0)
                            addFsZero(runs, ref fsPos, bytes - toCopy, partFsSize);
                    }
                    else if (bt == GapBlockType.ByteFill)
                    {
                        if (!btRepeat) { btByte = (byte)(0xFF & cnt); cnt >>= 8; }
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        if (btByte == 0x00)
                            addFsZero(runs, ref fsPos, bytes, partFsSize);
                        else
                            addFsRun(runs, ref fsPos, new WiiFsRun { Kind = WiiFsRunKind.Fill, FsLength = bytes, FillByte = btByte });
                    }
                    else // Junk
                    {
                        bytes = Math.Min(cnt * GapBlockSize, prg);
                        maxNulls = Math.Max(0, nullsPos - fsPos);
                        long localNulls = prg < maxNulls ? bytes : (bytes >= 0x40000 && !firstOrLastFile ? 0 : Math.Min(maxNulls, bytes));
                        addFsZero(runs, ref fsPos, localNulls, partFsSize);
                        if (bytes - localNulls > 0)
                            addFsRun(runs, ref fsPos, new WiiFsRun { Kind = WiiFsRunKind.Junk, FsLength = bytes - localNulls });
                    }
                    prg -= bytes;
                }
            }
            return srcPos;
        }

        // ── On-demand read of a Wii partition's block-space data ────────────────
        // Produces `count` bytes of the partition starting at block-space offset
        // `blockOffset` (relative to the partition's block data start). Each 0x8000 sector is a
        // 0x400 zero hash gap followed by 0x7c00 of FS data. FS data is generated from the decode
        // map (files from the buffered source, junk generated, zeros/fills), with the small head
        // overlay taking precedence for the FS bytes it covers.
        private void readWiiPartition(WiiPartitionMap map, long blockOffset, byte[] buffer, int bufOffset, int count)
        {
            int produced = 0;
            long blk = blockOffset;

            while (produced < count)
            {
                long sector = blk / WiiConsts.WiiSectorSize;
                int inSector = (int)(blk % WiiConsts.WiiSectorSize);

                if (inSector < WiiConsts.WiiSectorHashSize)
                {
                    // Hash gap (0x400 per sector). Normally zeros — hashes are generated downstream.
                    // But if this sector's GROUP has preserved (non-recreatable) hashes, reproduce
                    // them EXACTLY from the stored blob; the SectionProcessor then skips rebuild for
                    // this group (see processWiiNkitDecoded preserved-group handling).
                    int n = Math.Min(WiiConsts.WiiSectorHashSize - inSector, count - produced);
                    long groupBlockOffset = blk / WiiConsts.WiiGroupSize * WiiConsts.WiiGroupSize;
                    if (map.PreservedHashMap != null && map.PreservedHashMap.TryGetValue(groupBlockOffset, out long groupHashByteOff))
                    {
                        long sectorInGroup = (blk - groupBlockOffset) / WiiConsts.WiiSectorSize;
                        long srcHashOff = groupHashByteOff + (sectorInGroup * WiiConsts.WiiSectorHashSize) + inSector;
                        Array.Copy(map.PreservedHashes, srcHashOff, buffer, bufOffset + produced, n);
                    }
                    else
                    {
                        Array.Clear(buffer, bufOffset + produced, n);
                    }
                    produced += n;
                    blk += n;
                }
                else
                {
                    // FS data region of this sector.
                    int inFs = inSector - WiiConsts.WiiSectorHashSize;              // 0..0x7c00
                    long fsOffset = (sector * WiiConsts.WiiSectorFsSize) + inFs;      // FS-space offset
                    int fsRemainingInSector = WiiConsts.WiiSectorFsSize - inFs;
                    int n = Math.Min(fsRemainingInSector, count - produced);
                    // Clamp to the partition FS size (last sector may be partial).
                    if (fsOffset + n > map.PartFsSize)
                        n = (int)Math.Max(0, map.PartFsSize - fsOffset);
                    if (n == 0)
                    {
                        // Past FS data within a trailing partial sector: emit zeros.
                        int z = Math.Min(fsRemainingInSector, count - produced);
                        Array.Clear(buffer, bufOffset + produced, z);
                        produced += z;
                        blk += z;
                        continue;
                    }

                    produceWiiFs(map, fsOffset, buffer, bufOffset + produced, n);
                    produced += n;
                    blk += n;
                }
            }
        }

        // Produce `count` bytes of contiguous FS-space data at `fsOffset` from the decode map.
        private void produceWiiFs(WiiPartitionMap map, long fsOffset, byte[] buffer, int bufOffset, int count)
        {
            int produced = 0;
            while (produced < count)
            {
                long pos = fsOffset + produced;

                // Head overlay takes precedence for the region it covers.
                if (map.HeadOverlay != null && pos < map.HeadOverlay.Length)
                {
                    int n = (int)Math.Min(map.HeadOverlay.Length - pos, count - produced);
                    Array.Copy(map.HeadOverlay, (int)pos, buffer, bufOffset + produced, n);
                    produced += n;
                    continue;
                }

                WiiFsRun run = findWiiRun(map, pos);
                long inRun = pos - run.FsOffset;
                int avail = (int)Math.Min(run.FsLength - inRun, count - produced);
                if (avail <= 0)
                {
                    // Defensive: shouldn't happen (runs cover the partition). Zero-fill the rest.
                    Array.Clear(buffer, bufOffset + produced, count - produced);
                    return;
                }

                switch (run.Kind)
                {
                    case WiiFsRunKind.Data:
                        copyFromSrcStore(run.SrcOffset + inRun, buffer, bufOffset + produced, avail);
                        break;
                    case WiiFsRunKind.Zero:
                        Array.Clear(buffer, bufOffset + produced, avail);
                        break;
                    case WiiFsRunKind.Fill:
                        for (int i = 0; i < avail; i++)
                            buffer[bufOffset + produced + i] = run.FillByte;
                        break;
                    case WiiFsRunKind.Junk:
                        fillPartitionJunk(map, pos, buffer, bufOffset + produced, avail);
                        break;
                }
                produced += avail;
            }
        }

        // Binary/linear search for the run covering FS offset `pos`, with a sequential cache.
        private WiiFsRun findWiiRun(WiiPartitionMap map, long pos)
        {
            List<WiiFsRun> runs = map.Runs;

            // Fast path: current or next cached run (near-sequential reads).
            int idx = map.LastRunIndex;
            if (idx >= 0 && idx < runs.Count)
            {
                WiiFsRun r = runs[idx];
                if (pos >= r.FsOffset && pos < r.FsOffset + r.FsLength)
                    return r;
                if (idx + 1 < runs.Count)
                {
                    WiiFsRun nx = runs[idx + 1];
                    if (pos >= nx.FsOffset && pos < nx.FsOffset + nx.FsLength)
                    {
                        map.LastRunIndex = idx + 1;
                        return nx;
                    }
                }
            }

            int lo = 0, hi = runs.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                WiiFsRun r = runs[mid];
                if (pos < r.FsOffset) hi = mid - 1;
                else if (pos >= r.FsOffset + r.FsLength) lo = mid + 1;
                else { map.LastRunIndex = mid; return r; }
            }
            // Not found (defensive) — return a synthetic zero run covering the request.
            return new WiiFsRun { FsOffset = pos, FsLength = long.MaxValue, Kind = WiiFsRunKind.Zero };
        }

        // Copy `count` bytes from the buffered source store at `srcOffset` into buffer.
        private void copyFromSrcStore(long srcOffset, byte[] buffer, int bufOffset, int count)
        {
            _srcStore.Position = srcOffset;
            int got = 0;
            while (got < count)
            {
                int r = _srcStore.Read(buffer, bufOffset + got, count - got);
                if (r <= 0) { Array.Clear(buffer, bufOffset + got, count - got); break; }
                got += r;
            }
        }

        // Copy `count` bytes from the reinserted update-partition recovery file at `fileOffset`.
        // The file is the raw on-disc (encrypted+hashed) update partition. Bytes past the file end
        // are zero-filled (the update partition region extends past the file to the data offset).
        private void copyFromUpdateFile(long fileOffset, byte[] buffer, int bufOffset, int count)
        {
            if (!_updatePartitionUsed)
            {
                _updatePartitionUsed = true;
                // Log once, at first read of the reinserted region (not at parse/detection time).
                _recoveryLog?.Invoke($"Inserted update partition [{_updatePartitionDisplayName}]");
            }
            if (_updatePartitionStream == null)
                _updatePartitionStream = System.IO.File.OpenRead(_updatePartitionFile);
            if (fileOffset >= _updatePartitionStream.Length)
            {
                Array.Clear(buffer, bufOffset, count);
                return;
            }
            _updatePartitionStream.Position = fileOffset;
            int got = 0;
            while (got < count)
            {
                int r = _updatePartitionStream.Read(buffer, bufOffset + got, count - got);
                if (r <= 0) { Array.Clear(buffer, bufOffset + got, count - got); break; }
                got += r;
            }
        }

        // Generate FS-space partition junk at FS offset `fsOffset` using the partition's junk id.
        // Uses a dedicated cache (separate from disc junk's _junkBlock) memoised on (map, blockStart)
        // so the expensive NJunk.Fill runs once per 0x40000 block, not once per sub-read slice.
        private byte[] _ptnJunkBlock;
        private WiiPartitionMap _ptnJunkMap;
        private long _ptnJunkBlockStart = -1;
        private void fillPartitionJunk(WiiPartitionMap map, long fsOffset, byte[] buffer, int bufOffset, int count)
        {
            if (_ptnJunkBlock == null)
                _ptnJunkBlock = new byte[NJunk.JunkBlockSize];
            int written = 0;
            while (written < count)
            {
                long blockStart = fsOffset / NJunk.JunkBlockSize * NJunk.JunkBlockSize;
                int inBlock = (int)(fsOffset - blockStart);
                if (!ReferenceEquals(_ptnJunkMap, map) || _ptnJunkBlockStart != blockStart)
                {
                    NJunk.Fill(map.JunkId, map.DiscNo, map.JunkStartFsOffset, map.PartFsSize, blockStart, _ptnJunkBlock);
                    _ptnJunkMap = map;
                    _ptnJunkBlockStart = blockStart;
                }
                int avail = NJunk.JunkBlockSize - inBlock;
                int toCopy = Math.Min(avail, count - written);
                Array.Copy(_ptnJunkBlock, inBlock, buffer, bufOffset + written, toCopy);
                written += toCopy;
                fsOffset += toCopy;
            }
        }
    }
}