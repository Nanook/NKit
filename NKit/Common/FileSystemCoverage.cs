using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Shared coverage loop that drives an <see cref="IFileSystemReader"/> from the
    /// <c>IImage</c> side: it reads the regions the reader still needs (its <see cref="IFileSystemReader.Pending"/>
    /// set) via the <see cref="BufferStream"/> and feeds them back until the reader has resolved
    /// the file system far enough to cover the section about to be emitted (or fully, when
    /// <see cref="IFileSystemReader.RequireFullFileSystemUpFront"/>).
    ///
    /// <para>
    /// This centralises the seek/cache/retain machinery both ISO9660 and XBox use, generalising
    /// WiiGc's <c>readFileSystemUpFront</c> to the pending-region model:
    /// <list type="bullet">
    /// <item>The stream cursor is preserved — regions are read with the negative-count PEEK idiom
    /// and the cursor is restored on return, so the caller's normal sequential read is unaffected.</item>
    /// <item>The cache floor is pinned (<c>Retain</c>) for the duration so an end-of-read
    /// <c>ReleaseTo</c> cannot evict a region the walk or a later backward seek still needs.</item>
    /// <item>Each seek is guarded by <see cref="BufferStream.CanSeekTo"/>: for a seekable source it
    /// is free; for a forward-only source it is allowed only within the cache reach limit, else the
    /// region is skipped (graceful degrade) rather than forcing an unbounded cache fill.</item>
    /// <item>A no-progress guard breaks the loop on a malformed file system (a pass that resolves
    /// nothing), mirroring the guard in <c>readFileSystemUpFront</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class FileSystemCoverage
    {
        // Safety cap on loop iterations for a malformed/adversarial pending set.
        private const int MaxIterations = 0x4000;

        /// <summary>
        /// Ensure <paramref name="reader"/> is resolved through image offset <paramref name="throughImageOffset"/>
        /// (or fully, when <see cref="IFileSystemReader.RequireFullFileSystemUpFront"/>). Reads each
        /// in-range pending region via <paramref name="stream"/> and feeds it to the reader. The
        /// stream cursor is restored and the retain floor cleared on return.
        /// </summary>
        /// <param name="reader">The file-system reader to drive.</param>
        /// <param name="stream">The image stream (Layer-B BufferStream) reads flow through.</param>
        /// <param name="area">The current file-system area (for buffer shaping / encryption).</param>
        /// <param name="throughImageOffset">Resolve pending regions whose ImageOffset is below this;
        /// pass <see cref="long.MaxValue"/> to resolve everything currently pending.</param>
        /// <param name="retainFloor">The lowest image offset the walk may revisit (pinned via Retain).</param>
        /// <param name="makeBuffer">Factory producing an area-shaped buffer for a region read.</param>
        public static void ResolveThrough(
            IFileSystemReader reader,
            BufferStream stream,
            AreaInfo area,
            long throughImageOffset,
            long retainFloor,
            Func<IBuffer> makeBuffer)
        {
            if (reader == null || reader.Complete)
                return;

            bool full = reader.RequireFullFileSystemUpFront || throughImageOffset == long.MaxValue;
            long cursor = stream.Position;
            stream.Retain(retainFloor);
            try
            {
                int guard = 0;
                while (!reader.Complete)
                {
                    IReadOnlyList<FsRegionRequest> pending = reader.Pending;
                    if (pending == null || pending.Count == 0)
                        break;

                    // "Progress" = the pending set actually shrank this pass. Feeding a block that
                    // the reader does not resolve (a malformed FS) is NOT progress, so the loop
                    // stops instead of spinning. Snapshot the pending offsets before the pass.
                    HashSet<long> before = new HashSet<long>();
                    for (int i = 0; i < pending.Count; i++)
                        before.Add(pending[i].ImageOffset);

                    bool anyReachable = false;
                    foreach (FsRegionRequest region in inRange(pending, full, throughImageOffset))
                    {
                        if (!stream.CanSeekTo(region.ImageOffset))
                            continue; // out of reach — graceful degrade (Req 1.4 / 2.5)

                        int want = clampLength(region.Length);
                        if (want <= 0)
                            continue;

                        IBuffer buf = makeBuffer();
                        buf.ReInitialise(area, true); // set the buffer's AreaInfo before Update — parse reads AreaInfo (block sizes, Type)
                        stream.Position = region.ImageOffset;
                        int r = stream.Read(buf.Decrypted, 0, -want); // PEEK: leaves cursor at region start
                        if (r <= 0)
                            continue; // truncated / unreadable region

                        long areaOffset = region.ImageOffset - area.ImageOffset;
                        buf.Update(region.ImageOffset, areaOffset, r, 0, area.IsEncrypted, false);
                        reader.ProcessBlock(buf);
                        anyReachable = true;
                    }

                    if (!anyReachable)
                        break; // nothing in range could be read (all out of reach / truncated)

                    // Did the reader actually resolve something? If the pending set is unchanged
                    // after feeding it, it is not making progress (malformed FS) — stop.
                    bool shrank = reader.Complete;
                    if (!shrank)
                    {
                        IReadOnlyList<FsRegionRequest> after = reader.Pending;
                        if (after == null)
                            shrank = true;
                        else
                        {
                            for (int i = 0; i < after.Count && !shrank; i++)
                                shrank = !before.Contains(after[i].ImageOffset); // a new/changed pending entry = progress
                            if (!shrank)
                                shrank = after.Count < before.Count;
                        }
                    }
                    if (!shrank)
                        break; // no progress — malformed pending set

                    if (++guard > MaxIterations)
                        break; // safety: never spin forever
                }
            }
            finally
            {
                stream.Position = cursor;      // restore for the normal sequential read
                stream.Retain(long.MaxValue);  // clear the floor; normal ReleaseTo eviction resumes
            }
        }

        /// <summary>
        /// Drive a reader whose parser requires CONTIGUOUS, FORWARD, NON-OVERLAPPING block feeds
        /// (ISO9660). The ISO parser stitches directory extents that straddle block boundaries
        /// across successive buffers and, on every block, scans the buffer's whole FS range once for
        /// gap markers (PVD copies, RockRidge/mkisofs signatures) using a monotonic cursor. Feeding
        /// overlapping or backward-seeking blocks (as <see cref="ResolveThrough"/> does per pending
        /// region) makes that gap scan re-run over the same region and manufacture phantom system
        /// entries, corrupting the file tree.
        ///
        /// <para>
        /// So this walks the area as adjacent SectionSize blocks starting at
        /// <paramref name="area"/>.ImageOffset, advancing the cursor by each block's read size and
        /// feeding each region EXACTLY ONCE. It still supports NON-LINEAR directories: after each
        /// block, if the parser is not yet complete and every remaining pending directory lies
        /// beyond the contiguous cursor (a scattered extent), the cursor jumps forward (block
        /// aligned) to the lowest pending offset and resumes contiguous feeding there — never
        /// re-covering an already-fed region. Uses the PEEK idiom and restores the cursor / clears
        /// the retain floor on return, so the caller's normal sequential read (served from the
        /// BufferStream cache, incl. zip/7z) is unaffected.
        /// </para>
        /// </summary>
        public static void ResolveContiguous(
            IFileSystemReader reader,
            BufferStream stream,
            AreaInfo area,
            long imageSize,
            long areaUpperBound,
            int blockLength,
            Func<IBuffer> makeBuffer)
        {
            if (reader == null || reader.Complete)
                return;

            long cursor = stream.Position;
            long areaStart = area.ImageOffset;
            // Never read past the area/track upper bound. A single block read that crossed a track
            // boundary (e.g. a CHD Dreamcast data->audio boundary) would pull audio bytes framed as
            // data through the container's per-read track handling (CDDA endian swap keyed off the
            // read's START position), corrupting both the bytes and the container's running verify
            // hash. Clamping each read to the bound keeps our reads aligned to the same track
            // boundaries the normal sequential pipeline uses.
            if (areaUpperBound <= 0 || areaUpperBound > imageSize)
                areaUpperBound = imageSize;
            stream.Retain(areaStart);
            try
            {
                long covered = areaStart; // next uncovered image offset (block-aligned forward walk)
                int guard = 0;
                while (!reader.Complete)
                {
                    if (covered >= areaUpperBound)
                        break; // reached the area/track boundary — do not read into the next track

                    if (!stream.CanSeekTo(covered))
                        break; // out of cache reach on a forward-only source — graceful degrade

                    int want = (int)System.Math.Min(blockLength, areaUpperBound - covered);
                    if (want <= 0)
                        break;

                    IBuffer buf = makeBuffer();
                    buf.ReInitialise(area, true);
                    stream.Position = covered;
                    int r = stream.Read(buf.Decrypted, 0, -want); // PEEK — cursor unmoved
                    if (r <= 0)
                        break; // truncated / end of source

                    buf.Update(covered, covered - areaStart, r, 0, area.IsEncrypted, false);
                    reader.ProcessBlock(buf);
                    covered += r; // advance past the region we just fed (no overlap)

                    if (reader.Complete)
                        break;

                    // Non-linear support: if the parser still needs directory extents and they ALL
                    // lie beyond what we've covered, skip the (already parsed / file-data) gap and
                    // jump the contiguous cursor forward to the lowest pending offset. If any pending
                    // extent is within the region we just covered it will already have been stitched
                    // in by ProcessBlock, so we simply continue the contiguous walk.
                    IReadOnlyList<FsRegionRequest> pending = reader.Pending;
                    if (pending != null && pending.Count != 0)
                    {
                        long lowest = long.MaxValue;
                        for (int i = 0; i < pending.Count; i++)
                        {
                            if (pending[i].ImageOffset >= covered && pending[i].ImageOffset < lowest)
                                lowest = pending[i].ImageOffset;
                        }
                        // Jump forward only when the NEXT contiguous block wouldn't reach the lowest
                        // pending extent (a genuine scattered directory far ahead). Block-align down
                        // so the fed buffer's framing origin matches the area grid.
                        if (lowest != long.MaxValue && lowest >= covered + blockLength)
                        {
                            long rel = lowest - areaStart;
                            long aligned = rel - (rel % area.BlockSize);
                            covered = areaStart + aligned;
                        }
                    }

                    if (++guard > MaxIterations)
                        break; // safety: never spin forever on a malformed FST
                }
            }
            finally
            {
                stream.Position = cursor;      // restore for the normal sequential read
                stream.Retain(long.MaxValue);  // clear the floor; normal ReleaseTo eviction resumes
            }
        }

        /// <summary>
        /// Feed an explicit TAIL region <c>[tailStart, tailEnd)</c> to the reader's gap scan so
        /// markers that are only identifiable once read (UDF backup anchor / partition mirror, a late
        /// mkisofs signature) are discovered and added to the file system UP FRONT — before the
        /// area's view is published and its sections are dispatched to the parallel stage. Runs after
        /// the FS body parse (so the UDF signature references are captured); the caller supplies the
        /// exact region and an <paramref name="area"/> whose geometry (BlockSize / BaseOffset / block
        /// FS layout) matches the file system that owns the markers, so the gap scan attributes them
        /// to the correct FS offsets.
        ///
        /// <para>
        /// The region is fed as adjacent <paramref name="blockLength"/> blocks (block-aligned to the
        /// area grid). Each seek is guarded by <see cref="BufferStream.CanSeekTo"/>: local/seekable is
        /// free; a forward-only source (large image in an archive) whose tail is beyond the cache
        /// reach limit is skipped (graceful degrade) — exactly as the legacy sequential read would
        /// have missed it. Uses the PEEK idiom and restores the cursor / clears the retain floor on
        /// return, so the caller's normal sequential read (served from the BufferStream cache, incl.
        /// zip/7z) is unaffected.
        /// </para>
        /// </summary>
        public static void ResolveTailMarkers(
            Action<IBuffer> processTailBlock,
            BufferStream stream,
            AreaInfo area,
            long tailStart,
            long tailEnd,
            int blockLength,
            Func<IBuffer> makeBuffer)
        {
            if (processTailBlock == null || blockLength <= 0 || tailEnd <= tailStart)
                return;

            if (tailStart < area.ImageOffset)
                tailStart = area.ImageOffset;
            // Block-align the tail start to the area grid so fed buffers frame like the normal read.
            long rel = tailStart - area.ImageOffset;
            tailStart = area.ImageOffset + (rel - (rel % area.BlockSize));
            if (tailStart >= tailEnd)
                return;

            long cursor = stream.Position;
            stream.Retain(tailStart);
            try
            {
                long covered = tailStart;
                int guard = 0;
                while (covered < tailEnd)
                {
                    if (!stream.CanSeekTo(covered))
                        break; // out of cache reach on a forward-only source — graceful degrade

                    int want = (int)System.Math.Min(blockLength, tailEnd - covered);
                    if (want <= 0)
                        break;

                    IBuffer buf = makeBuffer();
                    buf.ReInitialise(area, true);
                    stream.Position = covered;
                    int r = stream.Read(buf.Decrypted, 0, -want); // PEEK — cursor unmoved
                    if (r <= 0)
                        break; // truncated / end of source

                    buf.Update(covered, covered - area.ImageOffset, r, 0, area.IsEncrypted, false);
                    processTailBlock(buf);
                    covered += r;

                    if (++guard > MaxIterations)
                        break;
                }
            }
            finally
            {
                stream.Position = cursor;      // restore for the normal sequential read
                stream.Retain(long.MaxValue);  // clear the floor; normal ReleaseTo eviction resumes
            }
        }

        // The pending regions to satisfy this pass: all of them when resolving fully, otherwise
        // only those whose start is at/below the coverage target. Materialised into a list so the
        // reader may re-report a changed Pending set between iterations without enumerator issues.
        private static List<FsRegionRequest> inRange(IReadOnlyList<FsRegionRequest> pending, bool full, long throughImageOffset)
        {
            List<FsRegionRequest> result = new List<FsRegionRequest>();
            for (int i = 0; i < pending.Count; i++)
            {
                FsRegionRequest r = pending[i];
                if (full || r.ImageOffset < throughImageOffset)
                    result.Add(r);
            }
            return result;
        }

        private static int clampLength(long length)
        {
            if (length <= 0)
                return 0;
            return length > int.MaxValue ? int.MaxValue : (int)length;
        }
    }
}