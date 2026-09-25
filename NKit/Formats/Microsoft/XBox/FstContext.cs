namespace Nanook.NKit.Microsoft.XBox
{
    internal class FstContext
    {
        private OrderedList<IFsFile> _files;
        private OrderedList<FstFile> _missing;
        private OrderedList<FstFile> _ahead;
        private FidelityFileList _fidelityFiles;
        private AreaInfo _areaInfo;
        private XDvdFsHeader _header;
        private RangeResult _rr;
        private IBuffer _curr;
        private byte[] _fsBuff;
        private int _fsBuffUsed;

        public byte[] FsBuff => _fsBuffUsed == 0 ? null : _fsBuff;
        public int FsBuffSize => _fsBuffUsed;
        public int FsBuffClear() => _fsBuffUsed = 0;
        public XDvdFsHeader Header => _header;
        public OrderedList<IFsFile> FileSystem => _files;
        public OrderedList<FstFile> Ahead => _ahead;
        public FidelityFileList FidelityFiles => _fidelityFiles;

        public FstContext(XDvdFsHeader header, AreaInfo areaInfo, IFixData fixData)
        {
            _fsBuff = new byte[0x400 * 0x400]; //1MiB
            _fsBuffUsed = 0;
            _rr = new RangeResult();
            _header = header;
            _areaInfo = areaInfo;
            FixData = fixData;
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
        public FstFile Current(IBuffer buffer /*, Func<FstFile, byte[], int, long> getSize*/)
        {
            _curr = buffer;
            FstFile f = _ahead.Count == 0 ? null : _ahead[0];
            if (f != null)
            {
                long fOffset = FsOffToOff(f.FsOffset);
                if (fOffset < buffer.ImageOffset + buffer.Size)
                {
                    //if (f.FsSize == -1)
                    //{
                    //    f.FsSize = getSize == null ? BlockFsSize : getSize(f, CiBuffer.Decrypted, (int)(fOffset - CiBuffer.AreaOffset));
                    //    setFileAnalysis(_files.KeyIndex(f.FsOffset, out _));
                    //}
                    buffer.TestFsRange(f.FsOffset, f.FsSize, _rr);
                    if (_rr.IsMatch)
                    {
                        if (_rr.RangeOffset == 0)
                            _fsBuffUsed = 0;
                        if (f.FsSize > _fsBuff.Length)
                            _fsBuff = new byte[f.FsSize + 0x400];
                        buffer.ReadFs(_rr.BufferOffset, _fsBuff, _fsBuffUsed, 0x8000, 0, 0x8000, _rr.Size);
                        _fsBuffUsed += _rr.Size;

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

        internal void UpdateSize(FstFile file, long fsSize) => setFileAnalysis(_files.KeyIndex(file.FsOffset, out _));//if (file.FsSize != fsSize)//{//    file.FsSize = fsSize;//    Current(_curr, null); //refresh//}

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
            Add(file.FsOffset, file, out int index, out _);
            _files.Remove(file);
            _ahead.Remove(file);
            _missing.Add(file);
            return file;
        }

        public FstFile Add(long fsOffset, FstFile file, out int index, out bool existed)
        {
            existed = false;
            index = _files.KeyIndex(fsOffset, out bool exists);

            if (!exists)
            {
                _files.Insert(index, file);
                setFileAnalysis(index);
                if (_curr == null || fsOffset >= _curr.AreaOffset)
                    _ahead.InsertIfMissing(file, out bool _);
            }
            else
                file = (FstFile)_files[index];

            return file;
        }

        public FstFile AddFile(FstFolder folder, string name, FsItemType fsType, long fsOffset, long size) => AddFile(folder, name, fsType, fsOffset, size, false, out _, out _);

        public FstFile AddFile(FstFolder folder, string name, FsItemType fsType, long fsOffset, long size, bool isSystem) => AddFile(folder, name, fsType, fsOffset, size, isSystem, out _, out _);

        public FstFile AddFile(FstFolder folder, string name, FsItemType fsType, long fsOffset, long size, bool isSystem, out int index, out bool existed)
        {
            existed = false;
            index = _files.KeyIndex(fsOffset, out bool exists);
            FstFile f;

            if (!exists)
            {
                f = new FstFile(folder, name, fsOffset, size) { IsSystemFile = isSystem, Type = fsType };
                _files.Insert(index, f);
                setFileAnalysis(index);
                if (_curr == null || fsOffset >= _curr.AreaOffset)
                    _ahead.InsertIfMissing(f, out bool _);
                _fidelityFiles.Add(f);
            }
            else
            {
                f = (FstFile)_files[index];
                // Different size → separate fidelity entry
                if (f.FsSize != size)
                {
                    FstFile collision = new FstFile(folder, name, fsOffset, size)
                    { IsSystemFile = isSystem };
                    _fidelityFiles.Add(collision);
                }
                // Same size → reference already in fidelity collection
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
                    f.PostGapSize = _header.FsSize - f.PostGapFsOffset;
            }
        }

    }

}