using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Microsoft.XBox
{
    internal enum FsItemType
    {
        Volume, RootFs, DirectoryEntry, File
    }

    internal class Fst : IFileSystem
    {
        const ushort terminator = 0xffff;
        const int XISO_ATTRIBUTE_DIR = 0x10;
        private FstContext _ctx;
        private XDvdFsHeader _header;
        private int _lastFile;

        public bool AllFolderRecordsParsed { get; set; }

        internal Fst(XDvdFsHeader header)
        {
            _ctx = header.FstContext;
            _header = header;
            _lastFile = 0;

            this.Root = new FstFolder("", null);
            _ctx.AddFile((FstFolder)this.Root, $"__volume_{_header.Volume.FsOffet:x}", FsItemType.Volume, _header.Volume.FsOffet, _header.Volume.FsSize, true);
            _ctx.AddFile((FstFolder)this.Root, $"__fst_{_header.Volume.RootFsOffset:x}", FsItemType.RootFs, _header.Volume.RootFsOffset, _header.Volume.RootFsSize, true);

            if (Files.Count != 0)
                ((FstFile)Files.Last()).IsLastFile = true;
        }


        public static List<IFsFile> CloneFiles(IEnumerable<IFsFile> files)
        {
            if (files == null)
                return null;

            return new List<IFsFile>(files.Select(a => a.Clone()).ToList());
        }

        public List<IFsFile> CloneFiles() => CloneFiles(Files);

        public List<IFsFile> Files => _ctx.FileSystem;
        public IFsFolder Root { get; private set; }

        private void processFileGaps(IBuffer buffer)
        {
            if (_ctx.FileSystem.Count == 0)
                return;

            long areaFsOffset = Buffer.OffsetToFsOffset(buffer.AreaOffset, buffer.AreaInfo.BlockSize, buffer.AreaInfo.BlockFsOffset, buffer.AreaInfo.BlockFsSize);
            FstFile f = null;

            if (buffer.FsOffset == 0 && _ctx.FileSystem[0].FsOffset >= buffer.BlockSize)
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
            long buffend = buffer.FsOffset + buffer.FsSize;
            while (gappos < buffend && (f == null || f.PostGapFsOffset < buffend))
            {
                gappos = f == null ? 0 : Math.Max(buffer.FsOffset, f.PostGapFsOffset);
                gappos += gappos % _ctx.BlockFsSize == 0 ? 0 : _ctx.BlockFsSize - (gappos % _ctx.BlockFsSize);
                long gapend = Math.Min(buffend, _lastFile + 1 >= _ctx.FileSystem.Count ? long.MaxValue : _ctx.FileSystem[_lastFile + 1].FsOffset);

                while (gappos < gapend)
                {
                    byte[] test = buffer.ReadFsBytes((int)(gappos - buffer.FsOffset), 0x18);
                    string name = null;
                    if (test[6] == 0x1 && (test[0] == 0xff || test[0] == 0x01) && test.ReadString(1, 5) == "CD001")
                    {
                        if (test[0] == 0xff)
                            name = "__pvdEnd_{0}";
                        else
                            name = "__pvd_{0}";
                    }
                    else if (test[0] == Consts.VolumeId[0] || test[0] == 'X') //reduce how much string conversion is done for gaps
                    {
                        string tmp = test.ReadString(0, test.Length);
                        if (tmp.StartsWith(Consts.VolumeId))
                            name = "__volume_{0}";
                        else if (tmp.StartsWith("XBOX_DVD_LAYOUT_TOOL_SIG"))
                            name = "__xbox_hdr_{0}";
                    }

                    if (name != null)
                    {
                        f = new FstFile((FstFolder)_ctx.FileSystem[0].Parent, string.Format(name, gappos.ToString("X")), gappos, _ctx.BlockFsSize) { IsSystemFile = true };
                        if (_lastFile >= 0) //not if inserting at the start
                            _ctx.UpdateGapToFile(f, out _, out bool exists);
                    }
                    gappos += buffer.BlockFsSize;
                }

                if (gappos < buffend && _lastFile < _ctx.FileSystem.Count)
                    f = (FstFile)_ctx.FileSystem[++_lastFile];
            }
        }

        public void SetFsData(IBuffer buffer)
        {
            if (!this.AllFolderRecordsParsed)
            {
                FstFile match;
                while ((match = _ctx.Current(buffer)) != null)
                {
                    if (match.Type == FsItemType.DirectoryEntry) //Volume and RootFs already processed, ignore files
                    {
                        //if (_ctx.Match.RangeComplete)
                        //{
                        this.Add((FstFolder)match.Parent, _ctx.FsBuff, 0, match.FsSize, buffer.AreaInfo.BlockSize);
                        _ctx.FsBuffClear(); // prevent accidental usage of old CiBuffer
                                            //}
                                            // else //if (!_ctx.Match.RangeComplete)
                                            //     break; //not got the full thing
                    }
                }

            }
            //process files in this block also check unused blocks and look for known unmarked blocks (mkisofs / rockridge info etc
            processFileGaps(buffer);

            if (!this.AllFolderRecordsParsed && _ctx.Ahead.Count == 0)
                this.AllFolderRecordsParsed = true;

            return;
        }

        public void Add(FstFolder parent, byte[] data, int offset, long size, int blockSize)
        {
            ushort leftOffset = 0;
            int off = offset;

            while (off - offset < size)
            {
                while (off - offset < size && (leftOffset = data.ReadUInt16L(off)) == terminator)
                {
                    off += 2;
                    off += off % blockSize == 0 ? 0 : (blockSize - (off % blockSize));
                }
                if (off - offset >= size)
                    break;

                byte nameLen = 0;
                if (off - offset + 0xe >= size || off - offset + 0xe + (nameLen = data.Read8(off - offset + 0xd)) > size)
                    break;

                byte attr = data.Read8(off + 0xc);
                string name = data.ReadString(off + 0xe, nameLen);
                if (!string.IsNullOrEmpty(name))
                {
                    long fsOffset = data.ReadUInt32L(off + 0x4) * blockSize;
                    long fsSize = data.ReadUInt32L(off + 0x8);

                    FstFolder p = parent;
                    bool isDir = (attr & XISO_ATTRIBUTE_DIR) != 0;

                    if (isDir)
                    {
                        p = new FstFolder(name, parent);
                        name = $"__fst_{fsOffset:X}_{p.Name}";
                    }
                    FstFile node = _ctx.AddFile(p, name, isDir ? FsItemType.DirectoryEntry : FsItemType.File, fsOffset, fsSize, isDir);

                    node.LeftSector = leftOffset;
                    node.RightSector = data.ReadUInt16L(off + 0x2);
                    node.Attributes = attr;
                }
                off += 0xe + nameLen;
                off += off % 4 == 0 ? 0 : (4 - (off % 4));
            }
        }
    }
}