using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class VrsList : List<UdfDescriptorTag>
    {
        public int ListBlock;
        public int ListSize;
    }
    internal class IsoFixIgnoreFiles
    {
        public uint PvdCrc;
        public string Path;
    }
    internal class FstIsoUdf : IFstIso
    {
        private FstContext _ctx;
        private Regex _regexNameOnly;
        private UdfAnchorVolumeDescriptorPointer _volPointer;
        private UdfVolume _vol;
        private Dictionary<int, VrsList> _vrsList; //read volume recognition sequence

        internal byte[] VolPointerData { get; set; }
        internal byte[] PartitionData { get; set; }
        internal string PartitionType { get; set; }

        public bool VolumeSetupComplete { get; set; }

        public FstIsoUdf(FstContext context)
        {
            _ctx = context;
            this.VolumeSetupComplete = false;
            _regexNameOnly = new Regex("^__file_[0-9a-z]+_(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        public bool IsUdf => true;

        public long GetSize(FstFile match, byte[] buff, int offset) => _ctx.BlockFsSize;

        public void ReadSystemData(FstFile match, IBuffer buffer)
        {
            switch (match.Type)
            {
                case FsItemType.UdfPartition:
                    processPartition(match, buffer.AreaInfo.FsOffset);
                    break;
                case FsItemType.UdfFileSet:
                    processFileSet(match, buffer.AreaInfo.FsOffset);
                    break;
                case FsItemType.UdfFileEntry:
                    processFileEntry(match, buffer.AreaInfo.FsOffset);
                    break;
                case FsItemType.DirectoryEntry:
                    processDirectories(match, buffer.AreaInfo.FsOffset);
                    break;
            }
        }

        public void Setup(IBuffer buffer)
        {
            if (!_ctx.Pvd.ContainsKey(FsType.Udf))
            {
                this.VolumeSetupComplete = true; //so setup is called again
                return;
            }
            if (!this.VolumeSetupComplete)
            {
                if (_volPointer == null)
                {
                    ImageHeaderPvd pvd = _ctx.Pvd[FsType.Udf];
                    int pntOff = (0x100 * buffer.BlockSize) + buffer.BlockFsOffset - (int)buffer.AreaInfo.FsOffset;
                    int pntFsOff = (int)Buffer.OffsetToFsOffset(pntOff, buffer.BlockSize, buffer.BlockFsOffset, buffer.BlockFsSize);
                    _volPointer = UdfAnchorVolumeDescriptorPointer.Parse(buffer.Decrypted, pntOff);
                    this.VolPointerData = buffer.Decrypted.Read(pntOff, _volPointer.Size);
                    _vrsList = new Dictionary<int, VrsList>(); //read volume recognition sequence
                    _vrsList.Add((int)_volPointer.MainDescriptorSequence.Location, new VrsList() { ListBlock = (int)_volPointer.MainDescriptorSequence.Location, ListSize = (int)_volPointer.MainDescriptorSequence.Size });
                    _vrsList.Add((int)_volPointer.ReserveDescriptorSequence.Location, new VrsList() { ListBlock = (int)_volPointer.ReserveDescriptorSequence.Location, ListSize = (int)_volPointer.ReserveDescriptorSequence.Size });

                    _ctx.AddFile(pvd.RootFolder, $"__ufdAvdp_{pntFsOff:X}", FsType.Udf, pntFsOff, _volPointer.Size, FsItemType.UdfAvdp, true);
                }
                this.VolumeSetupComplete = processVolRecSeq(buffer);
            }
        }

        private bool processVolRecSeq(IBuffer buffer)
        {
            if (_volPointer != null && _vrsList != null)
            {
                List<VrsList> newVrs;
                do
                {
                    newVrs = null;
                    foreach (KeyValuePair<int, VrsList> kv in _vrsList)
                        newVrs = readVrsList(buffer, kv.Value, newVrs);

                    if (newVrs != null) //add any new items
                    {
                        foreach (VrsList v in newVrs)
                        {
                            if (!_vrsList.ContainsKey(v.ListBlock)) //backup has same items (skip them)
                                _vrsList.Add(v.ListBlock, v);
                        }
                    }
                } while (newVrs != null); //loop if new were added

                if (!_vrsList.All(a => (a.Value.LastOrDefault()?.TagId ?? UdfTagId.None) == UdfTagId.TerminatingDescriptor))
                    return false; //not yet complete

                ImageHeaderPvd pvd = _ctx.Pvd[FsType.Udf];

                bool hasReserve = false;
                foreach (KeyValuePair<int, VrsList> kv in _vrsList)
                {
                    long off = (kv.Key * buffer.BlockFsSize) - buffer.AreaInfo.FsOffset;
                    long sz = kv.Value.Count * buffer.BlockFsSize;
                    string vrsType = "Unknown";
                    if (kv.Value[0].TagLocation == _volPointer.MainDescriptorSequence.Location)
                        vrsType = "Main";
                    else if (kv.Value[0].TagLocation == _volPointer.ReserveDescriptorSequence.Location)
                    {
                        vrsType = "Reserve";
                        hasReserve = true;
                    }
                    else if (kv.Value[0].TagId == UdfTagId.LogicalVolumeIntegrityDescriptor)
                        vrsType = "Integrity";
                    _ctx.AddFile(pvd.RootFolder, $"__ufdVrs_{vrsType}_{off:X}", FsType.Udf, off, sz, FsItemType.UdfVrs, true);
                }

                List<UdfVolume> vols = _vrsList.Select(kv => new UdfVolume(pvd, kv.Value, _volPointer, buffer.AreaInfo.FsOffset)).ToList();
                _vol = vols.FirstOrDefault(a => a.IsMain) ?? vols[0];
                this.PartitionType = _vol.IsMain ? "Main" : (hasReserve ? "Reserve" : "Unknown");

                if (_vol.Maps.Any(a => a is UdfMetadataPartitionMap))
                    _ctx.AddFile(pvd.RootFolder, $"__ufdPartition_{PartitionType}_{_vol.PartitionFsOffset:X}", FsType.Udf, _vol.PartitionFsOffset, _vol.Size, FsItemType.UdfPartition, true);
                else if (_vol.Maps.Any(a => a is UdfVirtualPartitionMap))
                    throw new Exception($"Virtual Partition not supported, raise this issue");
                else if (_vol.Maps.Any(a => a is UdfSparablePartitionMap))
                    throw new Exception($"Sparable Partition not supported, raise this issue");
                else
                {
                    _vol.FsOffset = _vol.PartitionFsOffset;
                    _ctx.AddFile(pvd.RootFolder, $"__udfFileSet_{_vol.FsOffset:X}", FsType.Udf, _vol.FsOffset, _vol.Size, FsItemType.UdfFileSet, true);
                }
                _vrsList = null; //end the processing
                return true;
            }
            return false;
        }

        private List<VrsList> readVrsList(IBuffer buffer, VrsList vrs, List<VrsList> newVrs)
        {
            if (vrs.LastOrDefault()?.TagId != UdfTagId.TerminatingDescriptor)
            {
                if (readVrsItem(buffer, vrs))
                {
                    UdfLogicalVolumeDescriptor volDesc = (UdfLogicalVolumeDescriptor)vrs.FirstOrDefault(a => a.TagId == UdfTagId.LogicalVolumeDescriptor);
                    if (volDesc != null && volDesc.IntegritySequenceExtent.Location != 0)
                        (newVrs ??= new List<VrsList>()).Add(new VrsList() { ListBlock = (int)volDesc.IntegritySequenceExtent.Location, ListSize = (int)volDesc.IntegritySequenceExtent.Size });
                }
            }

            return newVrs;
        }

        private bool readVrsItem(IBuffer buffer, VrsList vrs)
        {
            int block = vrs.ListBlock + vrs.Count;
            for (int b = block; b < vrs.ListBlock + vrs.ListSize; b++)
            {
                long off = (b * (long)buffer.BlockSize) + (long)buffer.BlockFsOffset - buffer.AreaInfo.FsOffset;
                if (off < buffer.AreaOffset || off >= buffer.AreaOffset + buffer.Size)
                    break;
                UdfDescriptorTag tag = UdfDescriptorTag.Parse(buffer.Decrypted, (int)(off - buffer.AreaOffset));
                if (tag != null)
                {
                    vrs.Add(tag);
                    if (tag.TagId == UdfTagId.TerminatingDescriptor)
                        return true;
                }
                else
                    break;
            }
            return false;
        }

        private void processPartition(FstFile match, long baseOffset)
        {
            UdfDescriptorTag tag = UdfDescriptorTag.Parse(_ctx.FsBuff, 0);

            if (tag.TagId == UdfTagId.FileEntry)
            {
                UdfFileEntry file = UdfFileEntry.Parse(_ctx.FsBuff, 0);
                this.PartitionData = _ctx.FsBuff.Read(0, file.Size);

                int pos = 0;
                while (pos < file.AllocationDescriptorsLength)
                {
                    UdfShortAllocationDescriptor d = UdfShortAllocationDescriptor.Parse(file.AllocationDescriptors, pos);

                    long fsOff = match.FsOffset + (d.ExtentLocation * (long)_vol.Volume.LogicalBlockSize) - baseOffset;
                    if (pos == 0) //root folder
                        _vol.FsOffset = fsOff;

                    string name = match.Type == FsItemType.UdfPartition ? $"__udfFileSet_{fsOff:X}" : "Unknown";
                    _ctx.AddFile((FstFolder)match.Parent, name, ((FstFolder)match.Parent).FsType, fsOff, _vol.Volume.LogicalBlockSize, FsItemType.UdfFileSet, true);

                    pos += d.Size;
                }
            }
            else if (tag.TagId == UdfTagId.ExtendedFileEntry)
            {
                //UdfExtendedFileEntry file = UdfExtendedFileEntry.Parse(data, offset);
            }
        }

        private void processFileSet(FstFile match, long baseOffset)
        {
            UdfDescriptorTag tag = UdfDescriptorTag.Parse(_ctx.FsBuff, 0);
            if (tag.TagId == UdfTagId.TerminatingDescriptor)
            {
                match.Links[0].EncodedChildName = $"__udfFileSetEnd_{match.FsOffset:X}"; //there will be only 1
                return;
            }

            UdfFileSetDescriptor fsd = UdfFileSetDescriptor.Parse(_ctx.FsBuff, 0);
            UdfLongAllocationDescriptor root = fsd.RootDirectoryIcb;
            long fsOff = match.FsOffset + (root.ExtentLocation.LogicalBlock * (long)_vol.Volume.LogicalBlockSize);
            _vol.FileDescriptorFsOffset = fsOff;

            _ctx.AddFile((FstFolder)match.Parent, $"__fst_{fsOff:X}", FsType.Udf, fsOff, root.ExtentLength, FsItemType.UdfFileEntry, true);

            if (fsd.DescriptorTag.TagId == UdfTagId.FileSetDescriptor)
            {
                fsOff = match.FsOffset + match.FsSize;
                _ctx.AddFile((FstFolder)match.Parent, $"__udfFileSet_{fsOff:X}", FsType.Udf, fsOff, root.ExtentLength, FsItemType.UdfFileSet, true);
            }
        }

        private void processFileEntry(FstFile match, long baseOffset)
        {
            UdfFileEntry fileEntry = null;
            UdfAllocationExtentDescriptor tag = null;
            FsFileParts splitParts = null;
            long firstOffset = -1; //hack used to detimine when a file is split. Test the split offset. If < the file start pos then it's pointer to the next split table block

            if (match.TempStash != null) //extended block
            {
                tag = UdfAllocationExtentDescriptor.Parse(_ctx.FsBuff, 0);
                fileEntry = null;
                splitParts = (FsFileParts)match.TempStash.SplitParts;
                firstOffset = splitParts.Parts[0].FsFile.FsOffset;
            }
            else
                fileEntry = UdfFileEntry.Parse(_ctx.FsBuff, 0);
            //UdfFile file = new UdfFile(_vol, fileEntry, _vol.Volume.LogicalBlockSize);
            if (fileEntry != null && fileEntry.InformationControlBlock.FileType == UdfFileType.Directory)
            {
                int pos = 0;
                while (pos < fileEntry.AllocationDescriptorsLength)
                {
                    int sz = entryFsOffsetSize(fileEntry, _vol.PartitionOffsets, pos, out long fsOff, out long fsSize);
                    //if (fsSize == 0)
                    //    fsSize = 0x800;
                    _ctx.AddFile((FstFolder)match.Parent, $"__dir_{fsOff:X}", ((FstFolder)match.Parent).FsType, fsOff, fsSize, FsItemType.DirectoryEntry, true);

                    pos += sz;
                }
            }
            else //File
            {
                int descLen = tag?.AllocationDescriptorsLength ?? fileEntry.AllocationDescriptors.Length;
                int pos = 0;
                bool splitEnd = true;
                string name = _regexNameOnly.Replace(match.TempStash == null ? match.Name : match.TempStash.Name, "$1");
                while (pos < descLen)
                {
                    int sz;
                    long fsOff;
                    long fsSize;
                    if (tag != null)
                        sz = entryFsOffsetSize(tag, _vol.PartitionOffsets, pos, out fsOff, out fsSize);
                    else
                        sz = entryFsOffsetSize(fileEntry, _vol.PartitionOffsets, pos, out fsOff, out fsSize);

                    if (pos == 0 || fsOff >= firstOffset) //file data
                    {
                        bool parentPointer = fsOff == match.Parent.Files[0].FsOffset; //parent folder pointer
                        FstFile f;
                        if (!parentPointer && fsSize != 0) //not ourselves
                        {
                            f = _ctx.AddFile((FstFolder)match.Parent, name, ((FstFolder)match.Parent).FsType, fsOff, fsSize, FsItemType.File);
                            if (f != null)
                            {
                                if (sz < descLen && splitParts == null && match.SplitParts == null)
                                {
                                    match.SplitParts = splitParts = new FsFileParts();
                                    firstOffset = f.FsOffset;
                                }

                                if (splitParts != null)
                                    splitParts.Parts.Add(new FsFilePart() { FsFile = f });
                            }
                        }
                    }
                    else //extended entry
                    {
                        FstFile ext = _ctx.AddFile((FstFolder)match.Parent, $"__fileext_{fsOff:X}_{name}", ((FstFolder)match.Parent).FsType, fsOff, _vol.Volume.LogicalBlockSize, FsItemType.UdfFileEntry, true);
                        ext.TempStash = match.TempStash ?? match; //add the original entry
                        splitEnd = false;
                    }
                    pos += sz;
                }

                //sum the split files
                if (splitParts != null && splitEnd)
                {
                    int i = 0;
                    foreach (FsFilePart f in splitParts.Parts)
                    {
                        f.OffsetInFile = splitParts.Size; //current sum is the offset
                        f.Index = i++;
                        ((FstFile)f.FsFile).SplitIndex = f.Index;
                        ((FstFile)f.FsFile).SplitParts = splitParts;
                        splitParts.Size += ((FstFile)f.FsFile).FsSize; //sum
                    }
                }
            }
        }

        private int entryFsOffsetSize(UdfAllocationExtentDescriptor ext, long[] baseOffsets, int pos, out long fsOff, out long fsSize)
        {
            UdfLongAllocationDescriptor d = UdfLongAllocationDescriptor.Parse(ext.AllocationDescriptors, pos);
            fsOff = baseOffsets[d.ExtentLocation.Partition] + (d.ExtentLocation.LogicalBlock * (long)_vol.Volume.LogicalBlockSize);
            fsSize = d.ExtentLength;
            return d.Size;
        }

        private int entryFsOffsetSize(UdfFileEntry fileEntry, long[] baseOffsets, int pos, out long fsOff, out long fsSize)
        {
            if (fileEntry.InformationControlBlock.AllocationType == UdfAllocationType.LongDescriptors)
            {
                UdfLongAllocationDescriptor d = UdfLongAllocationDescriptor.Parse(fileEntry.AllocationDescriptors, pos);
                fsOff = baseOffsets[d.ExtentLocation.Partition] + (d.ExtentLocation.LogicalBlock * (long)_vol.Volume.LogicalBlockSize);
                fsSize = d.ExtentLength;
                return d.Size;
            }
            else
            {
                UdfShortAllocationDescriptor d = UdfShortAllocationDescriptor.Parse(fileEntry.AllocationDescriptors, pos);
                fsOff = baseOffsets[1] + (d.ExtentLocation * (long)_vol.Volume.LogicalBlockSize);
                fsSize = d.ExtentLength;
                return d.Size;
            }
        }

        private void processDirectories(FstFile match, long baseOffset)
        {
            Dictionary<string, List<FstFile>> splitFiles = new Dictionary<string, List<FstFile>>();

            int pos = 0;
            int block = 0;

            while (pos + 1 < match.FsSize)
            {
                FstFile f = processDirectory(ref pos, match, out int index, out bool existed);
                if (pos == -1)
                    pos = (++block) * _ctx.BlockFsSize; //skip to next block
                else if (f != null)
                {
                    List<FstFile> sf = null;
                    if (f.SplitIndex == -1 || (splitFiles.Count != 0 && splitFiles.TryGetValue(f.Name, out sf))) //-1 is new split file
                    {
                        if (sf == null && !splitFiles.TryGetValue(f.Name, out sf))
                            splitFiles.Add(f.Name, sf = new List<FstFile>());
                        sf.Add(f);
                    }
                }
            }
        }

        private FstFile processDirectory(ref int offset, FstFile match, out int index, out bool existed)
        {
            index = -1;
            existed = false;
            FstFile f = null;

            UdfFileId fsEntry = UdfFileId.Parse(_ctx.FsBuff, offset);
            offset += fsEntry.Size + (fsEntry.Size % 4 == 0 ? 0 : (4 - (fsEntry.Size % 4)));
            if ((fsEntry.FileCharacteristics & UdfFileCharacteristic.Parent) == 0)
            {
                long fsOffset = _vol.FsOffset + (fsEntry.FileLocation.ExtentLocation.LogicalBlock * (long)_vol.Volume.LogicalBlockSize);
                bool parentPointer = fsOffset == match.Parent.Files[0].FsOffset; // parent folder pointer

                if (!parentPointer && fsEntry.FileLocation.ExtentLength != 0) //not ourselves
                {
                    if (fsEntry.FileCharacteristics == UdfFileCharacteristic.Directory)
                    {
                        FstFolder fld = new FstFolder(fsEntry.Name, ((FstFolder)match.Parent).FsType, (FstFolder)match.Parent);
                        _ctx.AddFile(fld, $"__fst_{fsOffset:X}", fld.FsType, fsOffset, _vol.Volume.LogicalBlockSize, FsItemType.UdfFileEntry, true);
                    }
                    else
                        _ctx.AddFile((FstFolder)match.Parent, $"__file_{fsOffset:X}_{fsEntry.Name}", ((FstFolder)match.Parent).FsType, fsOffset, _vol.Volume.LogicalBlockSize, FsItemType.UdfFileEntry, true);
                }
            }
            return f;
        }
    }
}