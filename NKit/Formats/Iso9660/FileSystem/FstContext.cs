using System;
using System.Collections.Generic;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FstContext
    {
        private OrderedList<IFsFile> _files;
        private FidelityFileList _fidelityFiles;
        private OrderedList<FstFile> _missing;
        private OrderedList<FstFile> _ahead;
        private AreaInfo _areaInfo;
        private ImageHeader _header;
        private Dictionary<FsType, ImageHeaderPvd> _pvd;
        private RangeResult _rr;
        private IBuffer _curr;
        private byte[] _fsBuff;
        private int _fsBuffUsed;

        public byte[] FsBuff => _fsBuffUsed == 0 ? null : _fsBuff;
        public int FsBuffSize => _fsBuffUsed;
        public int FsBuffClear() => _fsBuffUsed = 0;
        public ImageHeader Header => _header;
        public Dictionary<FsType, ImageHeaderPvd> Pvd => _pvd;
        public OrderedList<IFsFile> FileSystem => _files;
        public OrderedList<FstFile> Ahead => _ahead;
        public FidelityFileList FidelityFiles => _fidelityFiles;
        public long PhysicalVolumeSize { get; internal set; }

        public FstContext(ImageHeader header, AreaInfo areaInfo, IFixData fixData)
        {
            _fsBuff = new byte[0x400 * 0x400]; //1MiB
            _fsBuffUsed = 0;
            _rr = new RangeResult();
            _header = header;
            _areaInfo = areaInfo;
            FixData = fixData;
            _pvd = new Dictionary<FsType, ImageHeaderPvd>();
            _pvd.Add(FsType.System, ImageHeaderPvd.CreateSystem());
            _files = new OrderedList<IFsFile>(a => a.FsOffset, false);
            _ahead = new OrderedList<FstFile>(a => a.FsOffset, false);
            _missing = new OrderedList<FstFile>(a => a.FsOffset, false);
            _fidelityFiles = new FidelityFileList();
        }

        public RangeResult Match => _rr;
        public long AreaFsOffset => _areaInfo.FsOffset;
        public long BaseOffset => _areaInfo.BaseOffset;
        public int BlockFsSize => _areaInfo.BlockFsSize;

        public IFixData FixData { get; internal set; }

        public long FsOffToOff(long offset) => Buffer.FsOffsetToOffset(offset, _areaInfo.BlockSize, _areaInfo.BlockFsOffset, _areaInfo.BlockFsSize, false);
        public FstFile Current(IBuffer buffer, Func<FstFile, byte[], int, long> getSize)
        {
            _curr = buffer;
            FstFile f = _ahead.Count == 0 ? null : _ahead[0];
            if (f != null)
            {
                long fOffset = FsOffToOff(f.FsOffset);
                if (fOffset < buffer.ImageOffset + buffer.Size)
                {
                    if (f.FsSize == -1)
                    {
                        f.FsSize = getSize == null ? BlockFsSize : getSize(f, buffer.Decrypted, (int)(fOffset - buffer.AreaOffset));
                        setFileAnalysis(_files.KeyIndex(f.FsOffset, out _));
                    }
                    buffer.TestFsRange(f.FsOffset, f.FsSize, _rr);
                    if (_rr.IsMatch)
                    {
                        if (f.Type != FsItemType.File)
                        {
                            if (_rr.RangeOffset == 0)
                                _fsBuffUsed = 0;
                            if (f.FsSize > _fsBuff.Length)
                                _fsBuff = new byte[f.FsSize + 0x400];
                            buffer.ReadFs(_rr.BufferOffset, _fsBuff, _fsBuffUsed, 0x8000, 0, 0x8000, _rr.Size);
                            _fsBuffUsed += _rr.Size;
                        }
                        if (_rr.RangeComplete)
                        {
                            _ahead.RemoveAt(0);
                            return f;
                        }
                        else
                        {
                        }
                    }
                }
            }
            return null;
        }

        public int Index(long fsOffset) => _files.KeyIndex(fsOffset, out _);
        public int Index(long fsOffset, out bool existed) => _files.KeyIndex(fsOffset, out existed);
        public int AheadIndex(long fsOffset) => _ahead.KeyIndex(fsOffset, out _);
        public int AheadIndex(long fsOffset, out bool existed) => _ahead.KeyIndex(fsOffset, out existed);

        internal void UpdateSize(FstFile file, long fsSize)
        {
            setFileAnalysis(_files.KeyIndex(file.FsOffset, out _));
            if (file.FsSize != fsSize)
            {
                file.FsSize = fsSize;
                Current(_curr, null); //refresh
            }
        }

        internal void UpdateGapToFile(FstFile file, out int index, out bool exists)
        {
            index = _files.InsertIfMissing(file, out exists);
            if (!exists)
            {
                setFileAnalysis(index);
                setFileAnalysis(index - 1);
            }
        }

        public FstFile AddMissing(FstFile file)
        {
            Add(file.FsOffset, file.Links, lnks => file, out int index, out _);
            _files.Remove(file);
            _ahead.Remove(file);
            _missing.Add(file);
            return file;
        }

        public FstFile Add(long fsOffset, IEnumerable<FstLink> links, Func<IEnumerable<FstLink>, FstFile> createFile, out int index, out bool existed)
        {
            existed = false;
            index = _files.KeyIndex(fsOffset, out bool exists);
            FstFile f;
            if (!exists)
            {
                _files.Insert(index, f = createFile(links));
                setFileAnalysis(index);
                _ahead.InsertIfMissing(f, out bool _);
            }
            else
                FstLink.Merge(links, (f = (FstFile)_files[index]).Links);
            return f;
        }

        public FstFile AddFile(FstFolder folder, string name, FsType fsType, long fsOffset, long size, FsItemType type) => AddFile(folder, name, fsType, fsOffset, size, type, false, FsBlockEndian.Both, out _, out _);

        public FstFile AddFile(FstFolder folder, string name, FsType fsType, long fsOffset, long size, FsItemType type, bool isSystem, FsBlockEndian endian) => AddFile(folder, name, fsType, fsOffset, size, type, isSystem, endian, out _, out _);

        public FstFile AddFile(FstFolder folder, string name, FsType fsType, long fsOffset, long size, FsItemType type, bool isSystem) => AddFile(folder, name, fsType, fsOffset, size, type, isSystem, FsBlockEndian.Both, out _, out _);

        public FstFile AddFile(FstFolder folder, string name, FsType fsType, long fsOffset, long size, FsItemType type, bool isSystem, FsBlockEndian endian, out int index, out bool existed)
        {
            existed = false;
            index = _files.KeyIndex(fsOffset, out bool exists);
            FstFile f;
            if (!exists)
            {
                f = new FstFile(folder, name, fsType, fsOffset, size, type) { Endian = endian, IsSystemFile = isSystem };
                _files.Insert(index, f);
                setFileAnalysis(index);
                if (f.Type != FsItemType.File)
                    _ahead.InsertIfMissing(f, out bool _);
                _fidelityFiles.Add(f);
            }
            else
            {
                f = (FstFile)_files[index];
                // Check if a larger incoming size would overlap the next file in the OrderedList.
                // If so, it's a genuine shared-extent/overlap (SSIF-like) that needs fidelity tracking.
                bool largerOverlapsNext = false;
                if (size > f.FsSize && index + 1 < _files.Count)
                    largerOverlapsNext = (fsOffset + size) > _files[index + 1].FsOffset;

                // Case-insensitive name match = same logical file from different FS view
                // (e.g., ISO9660 uppercase vs UDF/Joliet mixed case). Always merge these
                // regardless of size differences (CDXA sectors cause size discrepancies).
                // However, do NOT override largerOverlapsNext — if the larger size genuinely
                // overlaps the next file (e.g., PS1 CDXA extended data), it must go to collision.
                bool isSameLogicalFile = string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);

                if ((f.FsSize == size || f.Type != type || f.Name == name || f.IsSystemFile || isSystem
                    || size == 0 || f.FsSize == 0 || size > f.FsSize || isSameLogicalFile)
                    && !largerOverlapsNext)
                {
                    // Merge: just update links. This covers same-size aliases, different types,
                    // same-name multi-fs views, system entries, zero-length placeholders, AND
                    // cases where a larger size is reported from a different filesystem view
                    // (e.g., Joliet vs ISO9660 size discrepancy due to CDXA sectors).
                    // The first-registered size is kept (original behavior).
                    f.Links.InsertIfMissing(new FstLink(folder, name, fsType, size), out _);
                    // Add to parent's Files list to maintain VFS references across filesystem views.
                    if (f.Parent != null)
                        ((FstFolder)f.Parent).Files.Add(f);
                }
                else // collision — genuinely different file at same offset (smaller size or overlapping larger size)
                {
                    // Maintain VFS reference for the existing file (matches original unconditional merge behavior).
                    if (f.Parent != null)
                        ((FstFolder)f.Parent).Files.Add(f);

                    // Same offset + smaller size = genuinely different file → add to fidelity only
                    // For SSIF/m2ts shared extents (FsItemType.File, non-system, different name):
                    // use null parent to avoid VFS count inflation.
                    // For all other cases: use real parent to preserve the folder tree.
                    FstFolder parent = (type == FsItemType.File && !isSystem) ? null : folder;
                    FstFile collision = new FstFile(parent, name, fsType, fsOffset, size, type)
                    { Endian = endian, IsSystemFile = isSystem };
                    if (parent == null)
                    {
                        // Manually set parent link without adding to folder.Files
                        collision.Links.Clear();
                        collision.Links.Add(new FstLink(folder, name, fsType));
                    }
                    _fidelityFiles.Add(collision);
                    // Return the collision for regular files so split-parts tracking references
                    // the correct entry. For system/directory entries, return the winner since
                    // callers use the return value for structural navigation.
                    if (type == FsItemType.File)
                        f = collision;
                }
            }

            return f;
        }

        private void setFileAnalysis(int idx)
        {
            FstFile f = (FstFile)_files[idx];
            if (idx == _files.Count - 1)
            {
                if (idx > 0)
                    ((FstFile)_files[_files.Count - 2]).IsLastFile = false;
                f.IsLastFile = true;
            }

            if (idx > 0) //set gap from previous file
            {
                FstFile f2 = (FstFile)_files[idx - 1];
                if (f2.FsSize != -1)
                {
                    f2.PostGapFsOffset = f2.FsOffset + f2.FsSize;
                    f2.PostGapSize = f.FsOffset - f2.PostGapFsOffset;
                }
            }

            if (f.FsSize != -1)
            {
                f.PostGapFsOffset = f.FsOffset + f.FsSize;
                if (idx + 1 < _files.Count)
                {
                    FstFile f2 = (FstFile)_files[idx + 1];
                    f.PostGapSize = f2.FsOffset - f.PostGapFsOffset;
                }
                else
                    f.PostGapSize = this.PhysicalVolumeSize - f.PostGapFsOffset;
            }
        }

    }

}