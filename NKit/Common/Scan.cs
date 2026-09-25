using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    public class Scan
    {
        private ScanArea _currentArea;
        private IFsFolder _virtualFs;
        private bool _vfsCounted;
        private int _vfsFilesCount;
        private int _vfsFoldersCount;

        public uint Crc { get; internal set; }
        public uint CrcDecrypted { get; internal set; }
        public long Size { get; internal set; }
        public SystemType SystemType { get; internal set; }
        public string Name { get; internal set; }

        public IFsFolder VirtualFs
        {
            get
            {
                if (_virtualFs == null)
                {
                    IFsFolder vRoot = new FsFolder() { Files = new List<IFsFile>(), Folders = new List<IFsFolder>(), Name = Path.GetFileNameWithoutExtension(this.Name), Parent = null };
                    foreach (ScanArea sra in this.Areas.Where(a => a.AreaInfo.FsAddressMode != AddressMode.Relative))
                    {
                        IFsFolder mainFsRoot = sra.FsInfo?.FileSystem?.Root;
                        if (mainFsRoot is Iso.Iso9660.FstFolder)
                            vRoot.Files.AddRange(sra.FsInfo.FileSystem.Files[0].Parent.Files);
                        vRoot.Folders.Add(new FsFolder() { Files = mainFsRoot?.Files ?? new List<IFsFile>(), Folders = mainFsRoot?.Folders ?? new List<IFsFolder>(), Parent = vRoot, Name = string.Format("{0} {1}", (sra.AreaInfo.AreaNo + 1).ToString("D2"), sra.AreaInfo.Type.ToString()) });
                    }
                    _virtualFs = vRoot;
                }
                return _virtualFs;
            }
        }

        private static void countScanVfsFilesFolders(IFsFolder folder, ref int filesCount, ref int foldersCount)
        {
            if (folder == null)
                return;
            filesCount += folder.Files.Count;
            foldersCount += folder.Folders.Count;
            foreach (IFsFolder fld in folder.Folders)
                countScanVfsFilesFolders(fld, ref filesCount, ref foldersCount);
        }

        public int VirtualFsTotalFileCount
        {
            get
            {
                if (!_vfsCounted)
                {
                    countScanVfsFilesFolders(this.VirtualFs, ref _vfsFilesCount, ref _vfsFoldersCount);
                    _vfsCounted = true;
                }
                return _vfsFilesCount;
            }
        }

        public int VirtualFsTotalFoldersCount
        {
            get
            {
                if (!_vfsCounted)
                {
                    countScanVfsFilesFolders(this.VirtualFs, ref _vfsFilesCount, ref _vfsFoldersCount);
                    _vfsCounted = true;
                }
                return _vfsFoldersCount;
            }
        }

        internal Properties Properties { get; set; }

        public List<ScanArea> Areas { get; private set; }

        internal Scan(SystemType system, string name)
        {
            this.SystemType = system;
            this.Name = name;
            this.Areas = new List<ScanArea>();
            this.Properties = new Properties("System", "Media", "Type", "Size", "CRC", "DecryptedCRC");
            _virtualFs = null;
        }

        /// <summary>
        /// Resets the internal area accumulation state to ensure clean CRC computation
        /// for a new image. Call this before processing sections for a new image when
        /// the Scan object or its context might carry residual state from a previous image.
        /// This prevents area CRC accumulator bleed between images in aux mode.
        /// </summary>
        internal void ResetAreaAccumulationState() => _currentArea = null;

        /// <summary>
        /// Resets the scan state for processing a new image. Clears the area CRC accumulator
        /// and all previously collected areas/sections so that Crc.Combine in SectionProcessed()
        /// starts from a clean state. This prevents stale CRC values from a previous image
        /// bleeding into the next image's area-level hash computation when the same Scan
        /// object is reused across images (e.g., in batch dedupe with aux mode).
        /// </summary>
        internal void ResetForNewImage(string name)
        {
            _currentArea = null;
            this.Name = name;
            this.Crc = 0;
            this.CrcDecrypted = 0;
            this.Size = 0;
            this.Areas = new List<ScanArea>();
            _virtualFs = null;
            _vfsCounted = false;
            _vfsFilesCount = 0;
            _vfsFoldersCount = 0;
        }

        public IEnumerable<ScanSection> Query(long imageOffset, long size)
        {
            long end = imageOffset + size;
            foreach (ScanArea sra in this.Areas)
            {
                if (sra.ImageOffset >= end)
                    break;
                if (imageOffset >= sra.ImageOffset && imageOffset < sra.ImageOffset + sra.Size)
                {
                    int idx = sra.FindSection(imageOffset - sra.ImageOffset, out bool existed);
                    if (!existed) //if not existed then idx is the index the new item would need to be inserted at - so decrement
                        idx--;

                    while (idx < sra.Sections.Count && sra.Sections[idx].ImageOffset < end)
                    {
                        ScanSection section = sra.Sections[idx++];
                        yield return section;
                    }
                }
            }
        }

        internal void SectionProcessed(ISectionProcessor section)
        {
            if (section.AreaOffset == 0)
            {
                _currentArea = new ScanArea(this)
                {
                    ImageOffset = section.ImageOffset,
                    FsInfo = section.FileSystemData,
                    Type = section.Type,
                    Crc = section.Crc,
                    CrcDecrypted = section.CrcDecrypted,
                    Size = section.Size,
                    AreaInfo = section.AreaInfo
                };
                Areas.Add(_currentArea);
                //System.Diagnostics.Trace.WriteLine("-----------");
                //System.Diagnostics.Trace.WriteLine($"{_currentArea.CrcDecrypted:X8} - {_currentArea.Size:X9}");
            }
            else
            {
                // Guard: _currentArea must have been initialized by a prior section with AreaOffset == 0.
                // If it is null here, it means SectionProcessed was called out of order or the scan
                // state was not properly reset between images.
                if (_currentArea == null)
                    throw new InvalidOperationException(
                        "Scan._currentArea is null when processing a continuation section " +
                        $"(AreaOffset={section.AreaOffset:X}, ImageOffset={section.ImageOffset:X}). " +
                        "This indicates stale or uninitialized scan state. Ensure ResetForNewImage() " +
                        "is called before processing a new image.");

                bool match = _currentArea.CrcDecrypted == _currentArea.Crc && section.Crc == section.CrcDecrypted;
                _currentArea.Crc = ~Nanook.NKit.Crc.Combine(~_currentArea.Crc, ~section.Crc, section.Size);
                if (match)
                    _currentArea.CrcDecrypted = _currentArea.Crc; //don't calculate the same thing again for CrcDecrypted
                else
                    _currentArea.CrcDecrypted = ~Nanook.NKit.Crc.Combine(~_currentArea.CrcDecrypted, ~section.CrcDecrypted, section.Size);
                _currentArea.Size += section.Size;
                //System.Diagnostics.Trace.WriteLine($"{_currentArea.CrcDecrypted:X8} - {_currentArea.Size:X9}");
            }

            section.PatchInfo.PrePatchCrc = section.Crc;
            section.PatchInfo.PrePatchCrcDecrypted = section.CrcDecrypted;
            section.PatchInfo.PrePatchXxHash = section.XxHash;

            ScanSection sec = new ScanSection(_currentArea)
            {
                HashesValid = section.IsValid,
                IsCreatable = section.IsCreatable,
                Crc = section.Crc,
                XxHash = section.XxHash,
                CrcDecrypted = section.CrcDecrypted,
                FsCrc = 0,
                Size = section.Size,
                State = section.State,
                SeekIv = section.SeekIv,
                AreaOffset = section.AreaOffset,
                ImageOffset = section.ImageOffset,
                FsOffset = section.FsOffset,
                FsSize = section.FsSize,
                FileStartIndex = section.FileStartIndex,
                FileEndIndex = section.FileEndIndex,
                PatchInfo = section.PatchInfo.Clone()
            };

            if (sec.PatchInfo.MarkForPatching || sec.PatchInfo.MarkForCalculatedData)
            {
                sec.Data = new byte[section.Decrypted.Length];
                section.Decrypted.CopyTo(sec.Data, 0);
            }

            //copy items to array and set filesystem CRCs
            SectionItems items = new SectionItems();
            //ReadOnlyCollection<IFsFile> fs = section.FileSystemData?.FileSystem?.Files;
            foreach (SectionItem item in section.Items)
            {
                if (item.FsFile != null)
                {
                    IFsFile file = item.FsFile;
                    if (item.File != null)
                    {
                        if (item.File.OffsetInItem == 0) //start of file
                            file.Crc = item.File.Crc;
                        else //combine the section parts to get the full file
                            file.Crc = ~Nanook.NKit.Crc.Combine(~file.Crc, ~item.File.Crc, item.File.FsSize);
                    }
                    if (item.Gap != null)
                    {
                        if (item.Gap.OffsetInItem == 0) //start of file
                            file.GapCrc = item.Gap.Crc;
                        else //combine the section parts to get the full file
                            file.GapCrc = ~Nanook.NKit.Crc.Combine(~file.GapCrc, ~item.Gap.Crc, item.Gap.FsSize);
                    }

                    if (file.SplitParts != null && file.SplitIndex + 1 == file.SplitParts.Parts.Count)
                    {
                        //combine the CRC
                        ((IFsFileXxHashCalc)file.SplitParts).Crc = file.SplitParts.Parts[0].FsFile.Crc;
                        for (int x = 1; x < file.SplitParts.Parts.Count; x++)
                            ((IFsFileXxHashCalc)file.SplitParts).Crc = ~Nanook.NKit.Crc.Combine(~((IFsFileXxHashCalc)file.SplitParts).Crc, ~file.SplitParts.Parts[x].FsFile.Crc, file.SplitParts.Parts[x].FsFile.FsSize);
                    }
                }
                items.Add(item);
            }

            sec.Items = items;

            _currentArea.Sections.Add(sec);

        }

        internal void RecalculatePatchedSectionSingleCrcs(ScanSection section) //file must not have moved. Just content changed
        {
            foreach (SectionItem si in section.Items)
            {
                if (si.FsFile != null)
                {
                    if (si.File != null && si.FsFile.FsSize == si.File.FsSize)
                    {
                        si.FsFile.Crc = si.File.Crc;
                        si.FsFile.XxHash = si.File.XxHash;
                    }
                    if (si.Gap != null && si.FsFile.PostGapSize == si.Gap.FsSize)
                        si.FsFile.GapCrc = si.Gap.Crc;
                }
            }
        }

        internal void RecalculatePatchedSectionCrcs() //file must not have moved. Just content changed
        {
            foreach (ScanArea sra in this.Areas)
            {
                int pfidx = -1; //part file index
                int psidx = -1; //part section index
                bool isFile = false;
                int lastIdx = sra.Sections.Count - 1;
                bool hasPatch = false;

                for (int sidx = 0; sidx <= lastIdx; sidx++)
                {
                    ScanSection s = sra.Sections[sidx];
                    if (sidx == 0)
                    {
                        if (s.FileStartIndex == -1 || s.Items == null || s.Items.Count == 0)
                            break;
                        isFile = s.Items.Last().Gap == null;
                        pfidx = s.FileEndIndex;
                        psidx = sidx;
                        hasPatch = s.PatchInfo.MarkForPatching;
                    }
                    else if (pfidx != s.FileEndIndex || isFile != (s.Items[0].Gap == null || sidx == lastIdx)) //if last file index not the same file/gap changed
                    {
                        int endIdx = s.FileStartIndex == pfidx && isFile == (s.Items[0].File != null) ? sidx : (sidx - 1);
                        if (endIdx == sidx) //was not adjusted
                            hasPatch |= s.PatchInfo.MarkForPatching;
                        //if (hasPatch) //commented as prevented scrubbed crc's being correct
                        calculateFileGapCrc(sra, s, pfidx, psidx, endIdx, isFile);
                        isFile = s.Items.Last().Gap == null;
                        pfidx = s.FileEndIndex;
                        psidx = sidx;
                        hasPatch = s.PatchInfo.MarkForPatching;
                    }
                    else
                        hasPatch |= s.PatchInfo.MarkForPatching;
                }
            }
        }

        private void calculateFileGapCrc(ScanArea sra, ScanSection s, int fileIdx, int startIdx, int endIdx, bool isFile)
        {
            if (startIdx < endIdx)
            {
                IFsFile file = sra.FsInfo?.FileSystem?.Files[fileIdx];

                //recalculate
                if (isFile)
                {
                    for (int i = startIdx; i <= endIdx; i++)
                        file.Crc = i == startIdx ? sra.Sections[i].Items.Last().File.Crc : ~Nanook.NKit.Crc.Combine(~file.Crc, ~sra.Sections[i].Items[0].File.Crc, sra.Sections[i].Items[0].File.FsSize);
                }
                else
                {
                    for (int i = startIdx; i <= endIdx; i++)
                        file.GapCrc = i == startIdx ? sra.Sections[i].Items.Last().Gap.Crc : ~Nanook.NKit.Crc.Combine(~file.GapCrc, ~sra.Sections[i].Items[0].Gap.Crc, sra.Sections[i].Items[0].Gap.FsSize);
                }
            }
        }

        internal void RecalculateAreaCrcs()
        {
            if (Areas.Count > 0)
            {
                this.Crc = Areas[0].Crc;
                this.CrcDecrypted = Areas[0].CrcDecrypted;
                this.Size = Areas[0].Size;

                for (int x = 1; x < Areas.Count; x++)
                {
                    this.Crc = ~Nanook.NKit.Crc.Combine(~Crc, ~Areas[x].Crc, Areas[x].Size);
                    this.CrcDecrypted = ~Nanook.NKit.Crc.Combine(~CrcDecrypted, ~Areas[x].CrcDecrypted, Areas[x].Size);
                    this.Size += Areas[x].Size;
                }
            }
        }

    }

    public class ScanArea
    {
        private OrderedList<ScanSection> _sections;

        public Scan ParentScan { get; private set; }
        public long ImageOffset { get; internal set; }
        public AreaType Type { get; internal set; }
        public uint Crc { get; internal set; }
        public uint CrcDecrypted { get; internal set; }
        public long Size { get; internal set; }
        public IFileSystemData FsInfo { get; internal set; }
        public List<ScanSection> Sections => _sections;

        public AreaInfo AreaInfo { get; internal set; }

        public int FindSection(long areaOffset, out bool existed) => _sections.KeyIndex(areaOffset, out existed);

        internal ScanArea(Scan parent)
        {
            ParentScan = parent;
            _sections = new OrderedList<ScanSection>(a => a.AreaOffset, false);
        }

        internal void CrcRecalculate()
        {
            Crc = Sections[0].Crc;
            CrcDecrypted = Sections[0].CrcDecrypted;
            bool match = true;
            for (int x = 1; x < Sections.Count; x++)
            {
                ScanSection section = Sections[x];
                match = match && section.Crc == section.CrcDecrypted;
                Crc = ~Nanook.NKit.Crc.Combine(~Crc, ~section.Crc, section.Size);
                if (match)
                    CrcDecrypted = Crc;
                else
                    CrcDecrypted = ~Nanook.NKit.Crc.Combine(~CrcDecrypted, ~section.CrcDecrypted, section.Size);
            }

        }
    }

    public class ScanSection
    {
        public ScanArea ParentArea { get; private set; }
        public bool IsCreatable { get; internal set; }
        public bool HashesValid { get; internal set; }
        public uint Crc { get; internal set; }
        public uint CrcDecrypted { get; internal set; }
        public ulong XxHash { get; internal set; }
        internal BitState State { get; set; }
        public byte[] SeekIv { get; internal set; }
        public long Size { get; internal set; }
        public long FsSize { get; internal set; }
        public long ImageOffset { get; internal set; }
        public long AreaOffset { get; internal set; }
        public uint FsCrc { get; internal set; }
        public long FsOffset { get; internal set; }
        public byte[] Data { get; internal set; }
        public int FileStartIndex { get; internal set; }
        public int FileEndIndex { get; internal set; }
        internal IPatchInfo PatchInfo { get; set; }

        internal ScanSection(ScanArea parent)
        {
            ParentArea = parent;
        }

        internal SectionItems Items { get; set; }
        public bool PatchApplied { get; internal set; }

    }
}