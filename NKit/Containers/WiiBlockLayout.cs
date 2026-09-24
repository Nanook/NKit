using Nanook.NKit.Nintendo.WiiGc;
using System;

namespace Nanook.NKit.Container
{
    /// <summary>
    /// Encapsulates the Wii partition block layout: interleaved 0x400 hash areas and 0x7C00
    /// filesystem data areas within each 0x8000 sector.
    ///
    /// Wii partition data is stored as sectors of <see cref="WiiConsts.WiiSectorSize"/> (0x8000).
    /// Each sector begins with a <see cref="WiiConsts.WiiSectorHashSize"/> (0x400) hash area
    /// followed by <see cref="WiiConsts.WiiSectorFsSize"/> (0x7C00) of filesystem data.
    ///
    /// The NKit format (and the decoder) works in contiguous "FS space" where hashes do not
    /// exist. This helper converts between FS space and block (sector) space, and inserts the
    /// 0x400 hash gaps when emitting decoded FS data into a partition's block layout.
    ///
    /// Hash areas themselves are left as zeros here — hash generation and encryption are the
    /// responsibility of the parallel processing stage downstream, not the container.
    ///
    /// This mirrors the math in <see cref="Buffer.OffsetToFsOffset"/> /
    /// <see cref="Buffer.FsOffsetToOffset"/> but is packaged for reuse by the NKit Wii decoder.
    /// </summary>
    internal sealed class WiiBlockLayout
    {
        public int SectorSize { get; }       // 0x8000
        public int HashSize { get; }          // 0x400  (hash area at the start of each sector)
        public int FsSize { get; }            // 0x7C00 (data area following the hash area)

        /// <summary>
        /// Standard Wii partition layout: 0x8000 sector, 0x400 hash, 0x7C00 data.
        /// </summary>
        public static WiiBlockLayout Wii => new WiiBlockLayout(
            WiiConsts.WiiSectorSize, WiiConsts.WiiSectorHashSize, WiiConsts.WiiSectorFsSize);

        /// <summary>
        /// Flat layout (no hashes) — sector == data. Used for GameCube and unhashed regions.
        /// </summary>
        public static WiiBlockLayout Flat => new WiiBlockLayout(
            WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize);

        public WiiBlockLayout(int sectorSize, int hashSize, int fsSize)
        {
            if (fsSize + hashSize > sectorSize)
                throw new ArgumentException("hashSize + fsSize cannot exceed sectorSize");

            SectorSize = sectorSize;
            HashSize = hashSize;
            FsSize = fsSize;
        }

        /// <summary>
        /// True when the layout has no hash gaps (sector == data).
        /// </summary>
        public bool IsFlat => SectorSize == FsSize;

        /// <summary>
        /// Convert a contiguous FS-space offset to the block-space (sector) offset that
        /// includes the interleaved hash areas.
        /// </summary>
        public long FsToBlock(long fsOffset)
        {
            if (IsFlat)
                return fsOffset;

            long rm = fsOffset % FsSize;
            long offset = fsOffset / FsSize * SectorSize;
            if (rm != 0)
                offset += HashSize + rm;
            else
                offset += HashSize; // start of the next sector's data region
            return offset;
        }

        /// <summary>
        /// Convert a block-space (sector) offset to the contiguous FS-space offset,
        /// skipping the hash areas.
        /// </summary>
        public long BlockToFs(long blockOffset)
        {
            if (IsFlat)
                return blockOffset;

            long sector = blockOffset / SectorSize;
            long inSector = blockOffset % SectorSize;
            long fsInSector = Math.Max(0, inSector - HashSize);
            return (sector * FsSize) + fsInSector;
        }

        /// <summary>
        /// Given a number of FS-space bytes, return the number of block-space bytes it
        /// occupies once the hash gaps are inserted, starting from the given FS offset.
        /// </summary>
        public long FsLengthToBlockLength(long fsOffset, long fsLength)
        {
            if (IsFlat || fsLength == 0)
                return fsLength;

            return FsToBlock(fsOffset + fsLength) - FsToBlock(fsOffset);
        }

        /// <summary>
        /// Copy <paramref name="fsLength"/> bytes of contiguous FS data into a partition
        /// block buffer, inserting the 0x400 hash gaps. Hash areas are left untouched
        /// (caller should ensure they are zeroed or handled downstream).
        /// </summary>
        /// <param name="src">Source contiguous FS data.</param>
        /// <param name="srcOffset">Offset into <paramref name="src"/>.</param>
        /// <param name="dst">Destination block-layout buffer.</param>
        /// <param name="dstBlockOffset">Block-space offset within <paramref name="dst"/> to begin writing.</param>
        /// <param name="fsLength">Number of FS bytes to copy.</param>
        public void CopyFsToBlock(byte[] src, int srcOffset, byte[] dst, long dstBlockOffset, int fsLength)
        {
            if (IsFlat)
            {
                Array.Copy(src, srcOffset, dst, dstBlockOffset, fsLength);
                return;
            }

            int remaining = fsLength;
            long dOff = dstBlockOffset;
            int sOff = srcOffset;

            // How many bytes remain in the current sector's data region
            long inSector = dOff % SectorSize;
            int dRmn;
            if (inSector < HashSize)
            {
                // Positioned in (or before) the hash area — advance to the data region
                dOff += HashSize - inSector;
                dRmn = FsSize;
            }
            else
            {
                dRmn = (int)(SectorSize - inSector);
            }

            while (remaining > 0)
            {
                int sz = Math.Min(dRmn, remaining);
                Array.Copy(src, sOff, dst, dOff, sz);
                sOff += sz;
                dOff += sz;
                remaining -= sz;
                dRmn -= sz;
                if (dRmn == 0)
                {
                    dOff += HashSize; // skip the next sector's hash area
                    dRmn = FsSize;
                }
            }
        }

        /// <summary>
        /// Fill <paramref name="fsLength"/> bytes of FS space in a partition block buffer with a
        /// constant byte, inserting the hash gaps. Hash areas are left untouched.
        /// </summary>
        public void FillFsInBlock(byte[] dst, long dstBlockOffset, int fsLength, byte value)
        {
            if (IsFlat)
            {
                for (int i = 0; i < fsLength; i++)
                    dst[dstBlockOffset + i] = value;
                return;
            }

            int remaining = fsLength;
            long dOff = dstBlockOffset;

            long inSector = dOff % SectorSize;
            int dRmn;
            if (inSector < HashSize)
            {
                dOff += HashSize - inSector;
                dRmn = FsSize;
            }
            else
            {
                dRmn = (int)(SectorSize - inSector);
            }

            while (remaining > 0)
            {
                int sz = Math.Min(dRmn, remaining);
                for (int i = 0; i < sz; i++)
                    dst[dOff + i] = value;
                dOff += sz;
                remaining -= sz;
                dRmn -= sz;
                if (dRmn == 0)
                {
                    dOff += HashSize;
                    dRmn = FsSize;
                }
            }
        }
    }
}