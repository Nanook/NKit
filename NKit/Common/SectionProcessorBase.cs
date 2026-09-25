using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    internal class SectionProcessorBase
    {
        private IBuffer _buffer;
        //private bool _reprocessedData;
        public const int AnalyseBlockSize = 0x400;
        public long ImageOffset => _buffer.ImageOffset;
        public long Size => _buffer.Size;
        public byte[] Decrypted => _buffer.Decrypted;
        public byte[] Encrypted => _buffer.Encrypted;
        public long FsOffset => _buffer.FsOffset;
        public long AreaOffset => _buffer.AreaOffset;
        public AreaInfo AreaInfo => _buffer.AreaInfo;
        public long FsSize => _buffer.FsSize;
        public uint Crc { get; protected set; }
        public uint CrcDecrypted { get; protected set; }  // only if section is encrypted
        public AreaType Type => _buffer.Type;
        public int FileStartIndex => _buffer.FileStartIndex;
        public int FileEndIndex => _buffer.FileEndIndex;
        public SectionItems Items { get; protected set; }
        public bool IsValid { get; protected set; }
        public bool IsCreatable { get; protected set; }
        public bool IsEncrypted => _buffer.IsEncrypted;
        public List<MetaData> MissingData => _buffer.MissingData;
        public CompletionStatus Status { get; protected set; }

        public IPatchInfo PatchInfo => _buffer.PatchInfo;
        public IBuffer Buffer { get => _buffer; set => _buffer = value; }
        public IFileSystemData FileSystemData { get; set; }

        public IFileSystem FullAreaFileSystem => (this.FileSystemData?.AllFoldersParsed ?? false) ? this.FileSystemData.FileSystem : null;  //directoryies scanned this is readonly so is CsqThread safe

        // Frozen, immutable snapshot of the area's file system for Out consumers (same AllFoldersParsed
        // gate as FullAreaFileSystem, but the AreaView's Primary/System/per-FS projections are a
        // published snapshot that never shifts index). Preferred over FullAreaFileSystem.
        public IAreaFileSystemView AreaFileSystem => this.FileSystemData?.AreaView;

        public ulong XxHash { get; protected set; }
        public BitState State { get; protected set; }
        public byte[] SeekIv { get; protected set; }

        public IImageInfo ImageInfo { get; protected set; }

        // Component tag for the per-section Trace line, so each format is identifiable like the
        // readers ([WiiGc]/[ISO9660]/[WiiU]/[XDVDFS]). Overridden per processor; defaults to the
        // generic [Section] tag.
        protected virtual string SectionTag => Nanook.NKit.LogScopes.Section;

        // Whether THIS section carries verifiable integrity data whose validity (hashOk/creatable)
        // is meaningful — Wii/WiiU partition hashes, or an ISO9660 CD sector mode with Reed-Solomon
        // EDC/ECC (Mode1 / Mode2 Form1). NOT true for GameCube, PS3 (plain AES, no hashes), audio,
        // etc. Encryption-supported is NOT the same thing (PS3 is encrypted but unverifiable), so
        // the per-section log only shows hashOk/creatable when this is true. Default: false.
        protected virtual bool SectionHasVerifiableHashes => false;


        public SectionProcessorBase()
        {
            State = new BitState();
        }


        public virtual void Complete()
        {
            if (((Buffer)this.Buffer).IsEncrypted)
            {
                Crc = Nanook.NKit.Crc.Compute(this.Encrypted, 0, (int)Size);
                CrcDecrypted = Nanook.NKit.Crc.Compute(this.Decrypted, 0, (int)Size);
            }
            else
            {
                Crc = Nanook.NKit.Crc.Compute(this.Decrypted, 0, (int)Size);
                CrcDecrypted = Crc;
            }

            Status = this.PatchInfo.MarkForPatching || this.PatchInfo.MarkForCalculatedData ? CompletionStatus.ToBePatched : CompletionStatus.Complete;

            logSection();
        }

        // Trace (debug level): one line per section summarising what this processor did — area type,
        // offset/size, encryption state, hash validity, scrub/patch/fix markers, and the checksums
        // known at completion. Tagged per format via SectionTag ([WiiGc]/[ISO9660]/[WiiU]/[XDVDFS]),
        // like the IImage readers. Trace so it never floods Detail; only visible at debug level.
        private void logSection()
        {
            ILogScope log = ImageInfo?.SectionLog;
            if (log == null || !log.IsEnabled(LogLevel.Trace))
                return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
            sb.Append(Nanook.NKit.LogScopes.Tag(this.SectionTag));
            sb.Append($"section [{this.Type}] off 0x{this.ImageOffset:X} size 0x{this.Size:X}");
            sb.Append($" enc:{(this.IsEncrypted ? "y" : "n")}");
            // Only meaningful for sections with verifiable integrity data (Wii/WiiU hashes, ISO
            // EDC/ECC) — omitted for PS3/GameCube/audio where IsValid/IsCreatable are not derived.
            if (this.SectionHasVerifiableHashes)
                sb.Append($" hashOk:{(this.IsValid ? "y" : "n")} creatable:{(this.IsCreatable ? "y" : "n")}");
            if (this.PatchInfo != null)
            {
                if (this.PatchInfo.ScrubbingChanged) sb.Append(" scrubbed");
                if (this.PatchInfo.MarkForPatching) sb.Append(" patch");
                if (this.PatchInfo.MarkForCalculatedData) sb.Append(" calc");
            }
            // State bitmask (e.g. Wii per-block scrubbing flags) — only when non-empty. Rendered
            // the SAME way the scan output does (BitState.ToString() = hex bytes), so the log and
            // the scan file agree. The bits ARE the information; ASCII of a flag mask is just dots.
            if (this.State != null && !this.State.IsClear())
                sb.Append($" state:{this.State}");
            sb.Append($" status:{this.Status}");
            sb.Append($" crc {this.Crc:X8}");
            if (this.IsEncrypted && this.CrcDecrypted != this.Crc)
                sb.Append($" crcDec {this.CrcDecrypted:X8}");
            if (this.XxHash != 0)
                sb.Append($" xxh {this.XxHash:X16}");

            log.Log(LogLevel.Trace, sb.ToString());
        }


        protected void CreateSectionItems(long areaBase)
        {
            long areaFsBase = Nanook.NKit.Buffer.OffsetToFsOffset(areaBase, _buffer.BlockSize, _buffer.BlockFsOffset, _buffer.BlockFsSize);
            long areaFs = Nanook.NKit.Buffer.OffsetToFsOffset(this.AreaOffset, _buffer.BlockSize, _buffer.BlockFsOffset, _buffer.BlockFsSize);

            RangeResult rr = new RangeResult();

            Items = new SectionItems();

            if (FileStartIndex != -1)
            {
                for (int idx = FileStartIndex; idx <= FileEndIndex; idx++)
                {
                    IFsFile f = this.FileSystemData.FileSystem.Files[idx];

                    SectionItem si = new SectionItem(ImageOffset, AreaOffset, areaFsBase, f);
                    _buffer.TestFsRange(f.FsOffset - areaFsBase, f.FsSize, rr);
                    if (rr.IsMatch)
                    {
                        //file
                        si.FileIndex = idx;
                        si.File = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                    }

                    if ((!rr.IsMatch || rr.RangeComplete) && f.PostGapSize != 0)
                    {
                        //gap
                        _buffer.TestFsRange(f.PostGapFsOffset - areaFsBase, f.PostGapSize, rr);

                        if (rr.IsMatch && rr.Size != 0)
                            si.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                    }

                    Items.Add(si);
                }

                // Trailing gap: if the last file's post-gap ends before the section's FS boundary,
                // add a gap item for the remaining space
                IFsFile lastFile = this.FileSystemData.FileSystem.Files[FileEndIndex];
                long lastFileEnd = lastFile.PostGapSize != 0
                    ? lastFile.PostGapFsOffset + lastFile.PostGapSize
                    : lastFile.FsOffset + lastFile.FsSize;
                long sectionFsEnd = areaFs + this.FsSize;
                long trailingGapSize = sectionFsEnd - lastFileEnd;
                if (trailingGapSize > 0)
                {
                    SectionItem gp = new SectionItem(ImageOffset, AreaOffset, areaFsBase, lastFile);
                    _buffer.TestFsRange(lastFileEnd - areaFsBase, trailingGapSize, rr);
                    if (rr.IsMatch && rr.Size != 0)
                        gp.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                    Items.Add(gp);
                }
            }
        }



        protected void ProcessData(bool recover, bool analyse, long areaBase, Action<SectionItem> section) //recover or convert
        {
            //_reprocessedData = false;
            long areaFsBase = Nanook.NKit.Buffer.OffsetToFsOffset(areaBase, _buffer.BlockSize, _buffer.BlockFsOffset, _buffer.BlockFsSize);
            long areaFs = Nanook.NKit.Buffer.OffsetToFsOffset(this.AreaOffset, _buffer.BlockSize, _buffer.BlockFsOffset, _buffer.BlockFsSize);

            RangeResult rr = new RangeResult();

            Items = new SectionItems();

            if (FileStartIndex != -1)
            {
                for (int idx = FileStartIndex; idx <= FileEndIndex; idx++)
                {
                    IFsFile f = this.FileSystemData.FileSystem.Files[idx];

                    if (idx == FileStartIndex && areaFs < f.FsOffset) //first file in this section is not at the start of the fs range
                    {
                        long gapFsOffset = areaFs > areaFsBase ? areaFs : areaFsBase;
                        long gapSize = f.FsOffset - gapFsOffset;
                        if (gapSize > 0)
                        {
                            // Use the previous file in the list as the gap's name reference
                            // (the last file from the previous content area whose post-gap
                            // data occupies this space)
                            IFsFile prevFile = FileStartIndex > 0 ? this.FileSystemData.FileSystem.Files[FileStartIndex - 1] : null;
                            SectionItem gp = new SectionItem(ImageOffset, AreaOffset, areaFsBase, prevFile);
                            _buffer.TestFsRange(gapFsOffset - areaFsBase, gapSize, rr);
                            if (rr.IsMatch && rr.Size != 0)
                                gp.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                            Items.Add(gp);
                        }
                    }

                    SectionItem si = new SectionItem(ImageOffset, AreaOffset, areaFsBase, f);
                    long testOffset = f.FsOffset - areaFsBase;
                    _buffer.TestFsRange(testOffset, f.FsSize, rr);
                    if (rr.IsMatch)
                    {
                        //file
                        si.FileIndex = idx;
                        si.File = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                    }

                    if ((!rr.IsMatch || rr.RangeComplete) && f.PostGapSize != 0)
                    {
                        //gap
                        _buffer.TestFsRange(f.PostGapFsOffset - areaFsBase, f.PostGapSize, rr);

                        if (rr.IsMatch && rr.Size != 0)
                            si.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };

                    }
                    Items.Add(si);

                    section(si); //caller
                }

                // Trailing gap: if the last file's post-gap ends before the section's FS boundary,
                // add a gap item for the remaining space (mirror of the leading gap fix above)
                IFsFile lastFile = this.FileSystemData.FileSystem.Files[FileEndIndex];
                long lastFileEnd = lastFile.PostGapSize != 0
                    ? lastFile.PostGapFsOffset + lastFile.PostGapSize
                    : lastFile.FsOffset + lastFile.FsSize;
                long sectionFsEnd = areaFs + this.FsSize;
                long trailingGapSize = sectionFsEnd - lastFileEnd;
                if (trailingGapSize > 0)
                {
                    SectionItem gp = new SectionItem(ImageOffset, AreaOffset, areaFsBase, lastFile);
                    _buffer.TestFsRange(lastFileEnd - areaFsBase, trailingGapSize, rr);
                    if (rr.IsMatch && rr.Size != 0)
                        gp.Gap = new SectionData(this.ImageOffset, this.AreaInfo, rr.BufferOffset, rr.Size) { OffsetInItem = rr.RangeOffset };
                    Items.Add(gp);
                    section(gp); //caller - analyse trailing gap data
                }
            }
        }
        protected void AnalysedItem(SectionItem si, bool isFile, int fsOffset, int fsSize, DataType type, int nulls, byte fillByte)
        {
            if (isFile)
            {
                SectionData file = (SectionData)si.File;
                if (fsOffset == file.FsOffset) //first part
                {
                    file.DataType = type;
                    if (type == DataType.NJunk)
                        file.DataNulls = nulls;
                }
                else if (file.DataType != type)
                    file.DataType = DataType.Data;
            }
            else
            {
                ((SectionData)si.Gap).DataNulls += nulls;

                //merge
                if (si.GapInfo.Count != 0)
                {
                    SectionData last = (SectionData)si.GapInfo.Last();
                    if (last.DataType == type && last.FillByte == fillByte && last.FsOffset + last.FsSize == fsOffset) //joins on (but not for data - don't want to merge data arrays together)
                    {
                        last.DataNulls += nulls;
                        last.FsSize += fsSize;
                        return;
                    }
                }

                //add
                si.GapInfo.Add(new SectionData(this.ImageOffset, this.AreaInfo, fsOffset, fsSize) { DataNulls = nulls, DataType = type, FillByte = fillByte });
            }
        }

        public void AnalyseDataWithMeta(ref int mdIdx, bool isFile, int sectionFsOffset, int size, Action<int, int, DataType, int, byte> analysed)
        {
            DataType type = DataType.Data;
            byte scrubByte = 0x00;

            this.ProcessBlocksWithMeta(ref mdIdx, sectionFsOffset, size, (off, fsOff, sz, mdIdx2) =>
            {
                if (mdIdx2 != -1) //use missing data
                    analysed(fsOff, sz, MissingData[mdIdx2].Type.ToDataType(), 0, MissingData[mdIdx2].BlockByte);
                else
                {
                    if (this.IsBlockFilled(this.Decrypted, off, sz, ref scrubByte)) //check for blank decrypted data
                        type = DataType.Fill;
                    else
                        type = isFile ? DataType.Data : DataType.Other;
                    analysed(fsOff, sz, type, 0, scrubByte); //if type is the same byt the scrubByte differs then use the last scrubbyte
                }
                return true;
            });
        }

        protected void ProcessBlocksWithMeta(ref int mdIdx, int sectionOffset, int size, Func<int, int, int, int, bool> process)
        {
            int sectorSize = SectionProcessorBase.AnalyseBlockSize; //0x400 aligned blocks

            int mdi = mdIdx;

            _buffer.ProcessFsData(sectionOffset, size, (data, off, fsOff, sz) =>
            {
                int s = Math.Min(sz, sectorSize - ((off + sectorSize) % sectorSize));
                int end = off + sz;

                while (s != 0)
                {
                    while (mdi < MissingData.Count && (MissingData[mdi].Type == MetaDataType.Data || MissingData[mdi].FsOffset + MissingData[mdi].FsSize <= fsOff)) //skip data blocks and blocks we're past
                        mdi++;

                    bool isMissingData = mdi < MissingData.Count && fsOff >= MissingData[mdi].FsOffset && fsOff + s <= MissingData[mdi].FsOffset + MissingData[mdi].FsSize; //only if full process block is within a missing section

                    if (!process(off, fsOff, s, isMissingData ? mdi : -1))
                        return false;
                    off += s;
                    fsOff += s;
                    s = Math.Min(sectorSize, end - off);
                }
                return true;
            });

            mdIdx = mdi;
        }

        protected void ProcessBlocks(int sectionOffset, int size, Func<int, int, int, bool> process) //skips hashes
        {
            int sectorSize = SectionProcessorBase.AnalyseBlockSize; //0x400 aligned blocks

            _buffer.ProcessFsData(sectionOffset, size, (data, off, fsOff, sz) =>
            {
                int s = Math.Min(sz, sectorSize - ((off + sectorSize) % sectorSize));
                int end = off + sz;

                while (s != 0)
                {
                    if (!process(off, fsOff, s))
                        return false;
                    off += s;
                    fsOff += s;
                    s = Math.Min(sectorSize, end - off);
                }
                return true;
            });
        }

        protected bool IsBlockFilled(byte[] buff, int offset, int size, ref byte fillByte) //only use within aligned 0x400 blocks
        {
            bool success;
            int bTst = offset;
            int bEnd = offset + size;
            byte b = buff[bTst];

            while (bTst < bEnd)
            {
                if (buff[bTst] != b)
                    break;
                bTst++;
            }
            success = bTst == bEnd;

            if (success)
                fillByte = b;

            return success;
        }

        public virtual void Update()
        {
            Crc = 0;
            CrcDecrypted = 0;
            XxHash = 0;
            Items = null;
            State = new BitState();
            SeekIv = null;
            Status = CompletionStatus.Processing;
        }

        protected void XxHashParallel()
        {
            IFileSystemData fsInfo = this.FileSystemData;
            if (fsInfo != null && this.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in this.Items)
                {
                    if (si.File != null)
                    {
                        IFsFileXxHashCalc xxHash = si.FsFile as IFsFileXxHashCalc;
                        if (xxHash != null && si.File.OffsetInItem == 0) //only process files we have the start of (safe for parallel)
                        {
                            xxHash.Object = XXHash64.Create();
                            xxHash.Stream = new CryptoStream(Stream.Null, xxHash.Object, CryptoStreamMode.Write);
                            bool isComplete = si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize;
                            ulong xxh;
                            if ((xxh = xxHashProcess(si, xxHash, isComplete)) != 0)
                                si.FsFile.XxHash = xxh;

                            //xxhash the full split file
                            if (si.FsFile.SplitParts != null && si.FsFile.SplitIndex == 0)
                            {
                                IFsFileXxHashCalc xxHashSplit = (IFsFileXxHashCalc)si.FsFile.SplitParts;
                                xxHashSplit.Object = XXHash64.Create();
                                xxHashSplit.Stream = new CryptoStream(Stream.Null, xxHashSplit.Object, CryptoStreamMode.Write);
                                xxHashProcess(si, xxHashSplit, isComplete && si.FsFile.SplitIndex == si.FsFile.SplitParts.Parts.Count - 1); //last part is complete
                            }
                        }
                    }
                }
            }
            this.XxHash = XXHash64.Compute(this.Decrypted, 0, (int)this.Size);
        }

        protected void XxHashLinear()
        {
            IFileSystemData fsInfo = this.FileSystemData;
            if (fsInfo != null && this.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in this.Items)
                {
                    if (si.File != null)
                    {
                        ulong xxh;
                        IFsFileXxHashCalc xxHash = si.FsFile as IFsFileXxHashCalc;
                        bool isComplete = si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize;
                        if (xxHash != null && si.File.OffsetInItem != 0) //process all parts of files that aren't the start of them
                        {
                            if ((xxh = xxHashProcess(si, xxHash, si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)) != 0)
                                si.FsFile.XxHash = xxh;
                        }

                        //xxhash the full split file
                        if (si.FsFile.SplitParts != null && !(si.File.OffsetInItem == 0 && si.FsFile.SplitIndex == 0)) //start of full split file
                        {
                            IFsFileXxHashCalc xxHashParts = (IFsFileXxHashCalc)si.FsFile.SplitParts;
                            if ((xxh = xxHashProcess(si, xxHashParts, isComplete && si.FsFile.SplitIndex == si.FsFile.SplitParts.Parts.Count - 1)) != 0)
                                xxHashParts.XxHash = xxh;
                        }
                    }
                }
            }
        }

        private ulong xxHashProcess(SectionItem si, IFsFileXxHashCalc xxHash, bool isComplete)
        {
            if (xxHash.Stream != null)
            {
                this.Buffer.ProcessFsData((int)si.File.FsOffset, (int)si.File.FsSize, (data, off, fsOff, sz) => { xxHash.Stream.Write(data, off, sz); return true; });

                //only close the file if we have all of the file
                if (isComplete)
                {
                    xxHash.Stream.Close();
                    ulong hash = xxHash.Object.HashUInt64;
                    xxHash.Stream = null;
                    xxHash.Object = null;
                    return hash;
                }
            }
            return 0;
        }

        protected void ParallelChecksumAndCleanse()
        {
            foreach (SectionItem si in Items)
            {
                if (si.File != null)
                    ((SectionData)si.File).Crc = _buffer.CrcFsData((int)si.File.FsOffset, (int)si.File.FsSize);

                if (si.Gap != null)
                {
                    SectionData gap = (SectionData)si.Gap;
                    if (si.GapInfo.Count == 1)
                    {
                        gap.DataType = si.GapInfo[0].DataType;
                        gap.DataNulls = si.GapInfo[0].DataNulls;
                        gap.FillByte = si.GapInfo[0].FillByte;
                        si.GapInfo.Clear(); //remove the gap info
                    }

                    gap.Crc = _buffer.CrcFsData((int)gap.FsOffset, (int)gap.FsSize);

                    if (si.GapInfo.Count == 0)
                    {
                        if (gap.DataType == DataType.Other)
                            gap.XxHash = this.Buffer.XxHashFsData((int)gap.FsOffset, (int)gap.FsSize);
                    }
                    else // > 1 as =1 was removed
                    {
                        gap.DataType = DataType.Other;
                        foreach (SectionData gd in si.GapInfo.Where(a => a.DataType == DataType.Other))
                        {
                            gd.Crc = this.Buffer.CrcFsData((int)gd.FsOffset, (int)gd.FsSize);
                            gd.XxHash = this.Buffer.XxHashFsData((int)gd.FsOffset, (int)gd.FsSize);
                        }
                    }

                }
            }
        }

        public virtual void PostProcess()
        {

        }
    }

}