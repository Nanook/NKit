using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{
    internal enum FsItemType
    {
        RootFs, CustomArea, Pvd, PathTable, DirectoryEntry, File, BootCatalog, UdfVrs, UdfAvdp, UdfPartition, UdfFileSet, UdfFileEntry
    }
    internal enum FsBlockEndian { Both, Big, Little }

    internal class Fst : IFileSystem
    {
        private FstContext _ctx;
        private IFstIso _iso;
        private FstIso9660 _iso9660;
        private FstIsoUdf _isoUdf;
        private ImageHeader _header;
        private int _lastFile;
        private bool _pvdsSet;
        private Dictionary<FsType, ImageHeaderPvd> _pvd;

        public bool AllFolderRecordsParsed { get; set; }

        public Fst(ImageHeader header, long physicalVolumeSize)
        {
            _pvd = header.Pvds;
            _ctx = header.FstContext;
            _ctx.PhysicalVolumeSize = physicalVolumeSize;
            _iso9660 = new FstIso9660(_ctx);
            _isoUdf = new FstIsoUdf(_ctx);
            _header = header;
            this.AllFolderRecordsParsed = false;

            if (_header.Data.Length != 0)
                _ctx.AddFile(_pvd[FsType.System].RootFolder, "__customHeader", FsType.System, 0, 0x8000, FsItemType.CustomArea, true);

            _pvdsSet = false;
        }

        public List<IFsFile> Files => _ctx.FileSystem;

        // UDF present in this file system. The tail markers (backup AVDP / partition mirror) only
        // exist for UDF volumes, so the tail scan is skipped entirely when this is false.
        internal bool HasUdf => _pvd != null && _pvd.ContainsKey(FsType.Udf);

        public IFsFolder Root { get; private set; }

        // Exposes the parse context so the up-front coverage reader (Iso9660FileSystemReader) can
        // read the pending directory extents (Ahead) and translate their fs offsets to image
        // offsets (FsOffToOff). Read-only use; the reader never mutates the context directly.
        internal FstContext Context => _ctx;

        public List<IFsFile> CloneFiles()
        {
            if (this.Files == null)
                return null;
            return new List<IFsFile>(this.Files.Select(a => a.Clone()).ToList());
        }

        private FstFile setFs(IBuffer buffer)
        {
            if (!_pvdsSet && _header.Pvds.Count != 0)
            {
                _iso9660.Setup(buffer);
                _isoUdf.Setup(buffer);

                if (_header.MainFsType != FsType.Other)
                    this.Root = _header.MainPvd.RootFolder;

                _pvdsSet = true;
            }
            else if (!_isoUdf.VolumeSetupComplete)
                _isoUdf.Setup(buffer);

            FstFile f = _ctx.Current(buffer, (sf, buff, off) =>
            {
                setIsoObj(sf);
                return _iso.GetSize(sf, buff, off);
            });
            setIsoObj(f);
            return f;
        }

        private void setIsoObj(FstFile f)
        {
            if (f != null)
            {
                if (f.Links[0].FsType == FsType.Udf || f.Type >= FsItemType.UdfAvdp)
                    _iso = _isoUdf;
                else
                    _iso = _iso9660;
            }
        }

        public void SetFsData(IBuffer buffer)
        {
            FstFile match;
            while ((match = setFs(buffer)) != null)
            {
                _iso.ReadSystemData(match, buffer); //uses 9660/udf
                _ctx.FsBuffClear(); // prevent accidental usage of old CiBuffer
                if (!_ctx.Match.RangeComplete)
                    break; //not got the full thing
            }

            //process files in this block also check unused blocks and look for known unmarked blocks (mkisofs / rockridge info etc
            processFileGaps(buffer);

            if (_ctx.Ahead.Count == 0)
                this.AllFolderRecordsParsed = true;

            return;
        }


        // Scan a fed buffer (typically the volume TAIL) sector-by-sector for the known system-marker
        // byte signatures and insert any matches into the FST — WITHOUT the file-relative gap-walk
        // navigation of processFileGaps. The gap-walk positions itself with _lastFile and bounds each
        // scan to the space BETWEEN two known files; at the volume tail the last real file's post-gap
        // "covers" the trailing region, so the gap-walk never scans it. The UDF backup structures
        // (AVDP backup / partition mirror) live in the last ~0x8000 of the image regardless of the
        // file layout, so this scans every block of the fed buffer directly. Idempotent: markers
        // already present (matched by FsOffset) are skipped.
        public void DiscoverTailMarkers(IBuffer buffer)
        {
            if (_ctx.FileSystem.Count == 0)
                return;

            long areaFsBase = Buffer.OffsetToFsOffset(buffer.AreaInfo.BaseOffset, buffer.AreaInfo.BlockSize, buffer.AreaInfo.BlockFsOffset, buffer.AreaInfo.BlockFsSize);
            long buffoff = buffer.FsOffset + areaFsBase;
            long buffend = buffoff + buffer.FsSize;
            for (long gappos = buffoff; gappos < buffend; gappos += buffer.BlockFsSize)
            {
                if (scanMarker(buffer, gappos, buffoff, out string name, out FsType fsType, out int sz))
                {
                    int gapIdx = _ctx.FileSystem.KeyIndex(gappos, out bool gapExists);
                    if (!gapExists)
                    {
                        FstFile f = new FstFile(_ctx.Pvd[fsType].RootFolder, string.Format(name, gappos.ToString("X")), fsType, gappos, sz, FsItemType.File) { IsSystemFile = true };
                        _ctx.UpdateGapToFile(f, out _, out _);
                    }
                }
            }
        }

        // Test one sector for a known system-marker signature. Extracted from processFileGaps so the
        // tail scan and the gap-walk share identical detection.
        private bool scanMarker(IBuffer buffer, long gappos, long buffoff, out string name, out FsType fsType, out int sz)
        {
            sz = _ctx.BlockFsSize;
            fsType = FsType.System;
            name = null;
            byte[] test = buffer.ReadFsBytes((int)(gappos - buffoff), 0x20);

            if (test[6] == 0x1 && (test[0] == 0xff || test[0] == 0x01) && test.ReadString(1, 5) == "CD001")
            {
                fsType = FsType.System;
                name = test[0] == 0xff ? "__pvdEnd_{0}" : "__pvd_{0}";
            }
            else if (_isoUdf?.VolPointerData != null && test.Equals(0, _isoUdf.VolPointerData, 0, 4))
            {
                if (test.Equals(0xa, _isoUdf.VolPointerData, 0xa, 2) && test.Equals(0x10, _isoUdf.VolPointerData, 0x10, test.Length - 0x10))
                {
                    fsType = FsType.Udf;
                    name = "__ufdAvdpBackup_{0}";
                    sz = _isoUdf.VolPointerData.Length;
                }
            }
            else if (_isoUdf?.PartitionData != null && test.Equals(0, _isoUdf.PartitionData, 0, 4))
            {
                byte[] test2 = buffer.ReadFsBytes((int)(gappos - buffoff), _isoUdf.PartitionData.Length);
                if (test2.Equals(0xa, _isoUdf.PartitionData, 0xa, 2) && test2.Equals(0x1c, _isoUdf.PartitionData, 0x1c, test2.Length - 0x1c))
                {
                    fsType = FsType.Udf;
                    name = $"__ufdPartitionMirror_{_isoUdf.PartitionType}_{{0}}";
                }
            }
            else
            {
                string tmp = test.ReadString(0, 0x18);
                fsType = FsType.System;
                if (tmp.StartsWith("MKI "))
                    name = "__mkIsoFs_{0}";
                else if (tmp.StartsWith("ER") && tmp.Contains("RRIP"))
                    name = "__rockRidge_{0}";
                else if (tmp.StartsWith(Microsoft.XBox.Consts.VolumeId))
                    name = "__volume_{0}";
                else if (tmp.StartsWith("XBOX_DVD_LAYOUT_TOOL_SIG"))
                    name = "__xbox_hdr_{0}";
            }
            return name != null;
        }

        private void processFileGaps(IBuffer buffer)
        {

            if (_ctx.FileSystem.Count == 0)
                return;

            long areaFsBase = Buffer.OffsetToFsOffset(buffer.AreaInfo.BaseOffset, buffer.AreaInfo.BlockSize, buffer.AreaInfo.BlockFsOffset, buffer.AreaInfo.BlockFsSize);
            long areaFsOffset = areaFsBase + Buffer.OffsetToFsOffset(buffer.AreaOffset, buffer.AreaInfo.BlockSize, buffer.AreaInfo.BlockFsOffset, buffer.AreaInfo.BlockFsSize);
            long buffoff = buffer.FsOffset + areaFsBase;
            FstFile f = null;

            if (buffoff == 0 && _ctx.FileSystem[0].FsOffset >= buffer.BlockSize)
                _lastFile = -1;
            else
            {
                f = (FstFile)_ctx.FileSystem[Math.Min(_lastFile, _ctx.FileSystem.Count - 1)];

                while (_lastFile > 0 && f.PostGapFsOffset > areaFsOffset && f.FsOffset > areaFsOffset)
                    f = (FstFile)_ctx.FileSystem[--_lastFile];
                while (_lastFile + 1 < _ctx.FileSystem.Count && _ctx.FileSystem[_lastFile + 1].PostGapFsOffset < areaFsOffset)
                    f = (FstFile)_ctx.FileSystem[++_lastFile];
            }

            long gappos = 0;
            long buffend = buffoff + buffer.FsSize;
            while (gappos < buffend && (f == null || f.PostGapFsOffset < buffend))
            {
                gappos = f == null ? 0 : Math.Max(buffoff, f.PostGapFsOffset);
                gappos += gappos % _ctx.BlockFsSize == 0 ? 0 : _ctx.BlockFsSize - (gappos % _ctx.BlockFsSize);
                long gapend = Math.Min(buffend, _lastFile + 1 >= _ctx.FileSystem.Count ? long.MaxValue : _ctx.FileSystem[_lastFile + 1].FsOffset);

                FsType fsType = FsType.System;

                while (gappos < gapend)
                {
                    int sz = _ctx.BlockFsSize;
                    byte[] test = buffer.ReadFsBytes((int)(gappos - buffoff), 0x20);
                    string name = null;

                    if (test[6] == 0x1 && (test[0] == 0xff || test[0] == 0x01) && test.ReadString(1, 5) == "CD001")
                    {
                        fsType = FsType.System;
                        if (test[0] == 0xff)
                            name = "__pvdEnd_{0}";
                        else
                            name = "__pvd_{0}";
                    }
                    else if (_isoUdf?.VolPointerData != null && test.Equals(0, _isoUdf.VolPointerData, 0, 4))
                    {
                        if (test.Equals(0xa, _isoUdf.VolPointerData, 0xa, 2) && test.Equals(0x10, _isoUdf.VolPointerData, 0x10, test.Length - 0x10))
                        {
                            fsType = FsType.Udf;
                            name = "__ufdAvdpBackup_{0}";
                            sz = _isoUdf.VolPointerData.Length;
                        }
                    }
                    else if (_isoUdf?.PartitionData != null && test.Equals(0, _isoUdf.PartitionData, 0, 4))
                    {
                        fsType = FsType.Udf;
                        byte[] test2 = buffer.ReadFsBytes((int)(gappos - buffoff), _isoUdf.PartitionData.Length);
                        if (test2.Equals(0xa, _isoUdf.PartitionData, 0xa, 2) && test2.Equals(0x1c, _isoUdf.PartitionData, 0x1c, test2.Length - 0x1c))
                            name = $"__ufdPartitionMirror_{_isoUdf.PartitionType}_{{0}}";
                    }
                    else
                    {
                        string tmp = test.ReadString(0, 0x18);
                        fsType = FsType.System;
                        if (tmp.StartsWith("MKI "))
                            name = "__mkIsoFs_{0}";
                        else if (tmp.StartsWith("ER") && tmp.Contains("RRIP"))
                            name = "__rockRidge_{0}";
                        else if (tmp.StartsWith(Microsoft.XBox.Consts.VolumeId)) //reduce how much string conversion is done for gaps
                            name = "__volume_{0}";
                        else if (tmp.StartsWith("XBOX_DVD_LAYOUT_TOOL_SIG"))
                            name = "__xbox_hdr_{0}";
                    }

                    if (name != null)
                    {
                        // Only create gap entry if not already in the OrderedList
                        int gapIdx = _ctx.FileSystem.KeyIndex(gappos, out bool gapExists);
                        if (!gapExists)
                        {
                            f = new FstFile(_ctx.Pvd[fsType].RootFolder, string.Format(name, gappos.ToString("X")), fsType, gappos, sz, FsItemType.File) { IsSystemFile = true };
                            if (_lastFile >= 0) //not if inserting at the start
                                _ctx.UpdateGapToFile(f, out _, out bool exists);
                        }
                    }
                    gappos += buffer.BlockFsSize;
                }

                if (gappos < buffend && _lastFile < _ctx.FileSystem.Count)
                    f = (FstFile)_ctx.FileSystem[++_lastFile];
            }
        }

    }

}