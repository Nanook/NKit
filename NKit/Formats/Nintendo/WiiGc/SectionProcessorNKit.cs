using System;
using System.Collections.Generic;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class SectionProcessorNKit
    {
        internal static void FixMissingWiiFsScrubbing(IBuffer buf, List<MetaData> missingData, FileSystemInfo fsInfo)
        {
            foreach (MetaData md in missingData)
            {
                if (md.Type == MetaDataType.Fill)
                {
                    int nkitDiff = (int)(md.FsOffset % WiiConsts.WiiSectorFsSize);

                    if (nkitDiff > 0 && nkitDiff < 0x100)
                    {
                        //test the data not included in the fill to see if it's a match
                        byte[] filler = md.BlockByte == 0xff ? fsInfo.DecryptedBlockFilledFF : fsInfo.DecryptedBlockFilled00;
                        bool isScrub = true;
                        for (int i = 0, s = 0; i < nkitDiff; i++, s++)
                        {
                            if (s >= filler.Length)
                                s = 0;
                            if (buf.Decrypted[md.Offset - nkitDiff + i] != filler[s])
                            {
                                isScrub = false;
                                break;
                            }
                        }

                        if (isScrub) //pull the offset back to start of the block / include the hashes
                        {
                            md.FsOffset -= nkitDiff;
                            md.FsSize += nkitDiff;
                            md.Offset -= nkitDiff + WiiConsts.WiiSectorBlockSize;
                            md.Size += nkitDiff + WiiConsts.WiiSectorBlockSize;
                        }
                    }
                }
            }

        }
        internal static void FixNulls(IBuffer b, FileSystemInfo fsInfo, SectionItems items, Func<int, int, int, int, int> junkFileTest)
        {
            int idx = b.FileStartIndex;

            if (idx == -1)
                return;

            int nkitNullsFix = 0;
            RangeResult rr = new RangeResult();
            int mdIdx = 0;
            bool lastFileWasJunk = false;

            FstFile f = (FstFile)fsInfo.FileSystem.Files[idx];

            int siIdx = -1;
            foreach (SectionItem si in items)
            {
                siIdx++;

                if (si.File != null) //NKit junk file, leading nulls creation. Junk will have been inserted by fillMissingData()
                {
                    idx = si.FileIndex;
                    f = (FstFile)fsInfo.FileSystem.Files[idx];

                    while (mdIdx < b.MissingData.Count && (b.MissingData[mdIdx].Type == MetaDataType.Data || b.MissingData[mdIdx].FsOffset + b.MissingData[mdIdx].FsSize <= si.File.FsOffset)) //skip data blocks and blocks we're past
                        mdIdx++;

                    int junkNulls = Math.Max(0, (int)(f.Analysis.JunkNulls - si.File.OffsetInItem));

                    if (lastFileWasJunk && idx > 0 && !((FstFile)fsInfo.FileSystem.Files[idx - 1]).IsMissing)
                        junkNulls = nkitNullsFix; //not a fool proof fix, but fixes the NKIT format non identified junk file in XGIII / HauntedHouse PAL / Gremlins / Zumba Fitness USA

                    if (junkNulls != 0 && f.IsMissing)
                        writeNulls(ref mdIdx, b, (int)si.File.FsOffset, junkNulls, rr);


                    //if junk file then test the end of the last file if gap < 0x1c
                    lastFileWasJunk = f.IsMissing;
                    if (!lastFileWasJunk && siIdx < items.Count - 1 && items[siIdx + 1].File != null && ((FstFile)fsInfo.FileSystem.Files[items[siIdx + 1].FileIndex]).IsMissing)
                        lastFileWasJunk = -1 != junkFileTest(mdIdx, junkNulls, (int)si.File.FsOffset, (int)si.File.FsSize);

                    if (!f.IsMissing && nkitNullsFix == WiiConsts.DataNullsCount && f.Name == "zzzdummy.rbb")
                    {
                        lastFileWasJunk = true; //XGIII PAL - without it Wii Catz, Dogz etc fail - couldn't work around this one file
                        nkitNullsFix -= (int)si.File.FsSize;
                    }
                    else if (!lastFileWasJunk) //was it set to junk
                        nkitNullsFix = f.Analysis.MaxNullsSize; //0 if big gap or no gap (no gap as next file might be junk file, if not then it's reset to 0x1c)
                    else //!f.IsNKitJunkFile = caught the nkit v1 bug - junk file not identified as junk and messes up nulls calcs. This is a basic hack that seems to work for current images
                        nkitNullsFix = (int)Math.Max(0, nkitNullsFix - (!f.IsMissing ? f.FsSize : junkNulls)); //XGIII PAL requires => !f.IsNKitJunkFile ? f.FsSize
                }

                if (si.Gap != null)
                {
                    while (mdIdx < b.MissingData.Count && (b.MissingData[mdIdx].Type == MetaDataType.Data || b.MissingData[mdIdx].FsOffset + b.MissingData[mdIdx].FsSize <= si.Gap.FsOffset)) //skip data blocks and blocks we're past
                        mdIdx++;

                    int junkNulls = (int)Math.Min(Math.Min(si.Gap.FsSize, f.Analysis.ExpectedNulls), Math.Max(f.Analysis.ExpectedNulls - si.Gap.OffsetInItem, 0L));

                    if (lastFileWasJunk && junkNulls != 0 && !f.IsMissing)
                        junkNulls = 0; //not a fool proof fix, but fixes the NKIT format non identified junk file in XGIII / HauntedHouse PAL / Gremlins / Zumba Fitness USA

                    if (junkNulls != 0)
                        writeNulls(ref mdIdx, b, (int)si.Gap.FsOffset, junkNulls, rr);

                    nkitNullsFix = junkNulls != 0 ? nkitNullsFix - junkNulls : 0;
                }
            }
        }

        private static void writeNulls(ref int mdIdx, IBuffer b, int sectionOffset, int size, RangeResult rr)
        {
            if (mdIdx == b.MissingData.Count)
                return;

            while (b.MissingData[mdIdx].FsOffset + b.MissingData[mdIdx].FsSize <= sectionOffset)
            {
                mdIdx++;
                if (mdIdx == b.MissingData.Count)
                    return;
            }

            b.TestRangeBounds(sectionOffset, size, b.MissingData[mdIdx].FsOffset, b.MissingData[mdIdx].FsOffset + b.MissingData[mdIdx].FsSize, rr);
            while (rr.IsMatch && mdIdx < b.MissingData.Count)
            {
                if (b.MissingData[mdIdx].Type == MetaDataType.NJunk || b.MissingData[mdIdx].Type == MetaDataType.NJunkFile)
                    b.ClearFs((int)b.MissingData[mdIdx].FsOffset + rr.BufferOffset, rr.Size);

                if (rr.RangeComplete)
                    break;

                mdIdx++;
                if (mdIdx == b.MissingData.Count)
                    return;

                b.TestRangeBounds(sectionOffset, size, b.MissingData[mdIdx].FsOffset, b.MissingData[mdIdx].FsOffset + b.MissingData[mdIdx].FsSize, rr);
            }
        }

        internal static bool RequiresPatch(IBuffer b, FileSystemInfo fsInfo)
        {
            bool patch = b.Type == AreaType.ImageHeader ||
                          b.Type == AreaType.PartitionHeader ||
                           (b.Type == AreaType.FileSystem &&
                            b.FsOffset < fsInfo.FstData.Length &&
                            !fsInfo.InvalidPartitionData
                         );
            return patch;
        }
    }
}