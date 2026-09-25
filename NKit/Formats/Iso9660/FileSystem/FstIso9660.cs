using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FstIso9660 : IFstIso
    {
        //private long _eltoritoOffset;

        private FstContext _ctx;
        public FstIso9660(FstContext context)
        {
            _ctx = context;
        }

        public bool IsUdf => false;
        public long GetSize(FstFile match, byte[] buff, int offset)
        {
            if (match.Type == FsItemType.DirectoryEntry) //grab and return the size from the first block
                return FileSystemDirectoryEntry.GetSize(buff, offset);
            return _ctx.BlockFsSize;
        }

        public void ReadSystemData(FstFile match, IBuffer buffer)
        {
            if (match.Type == FsItemType.PathTable)
                processPathTable(match);
            else if (match.Type == FsItemType.DirectoryEntry)
                processDirectories(match);
            else if (match.Type == FsItemType.BootCatalog)
                processBoot(match);
        }

        public void Setup(IBuffer buffer)
        {
        }

        private void processBoot(FstFile match)
        {
            int outSize;
            int buffSize = _ctx.FsBuffSize;
            FstFile f;

            bool complete = _ctx.Header.ElTorito.EnumEntries(_ctx.FsBuff, 0, buffSize, (int)match.FsSize, (off, sz) =>
                f = _ctx.AddFile((FstFolder)match.Parent, "bootImage", FsType.ElTorito, off, sz, FsItemType.File, false, match.Endian, out int index, out bool existed),
                out outSize);

            if (match.Links.Count == 1 && match.Links[0].FsType == FsType.ElTorito) //update if we haven't seen a file in any filesystem for the boot catalog
                _ctx.UpdateSize(match, outSize); //update to what was read
        }

        private void processPathTable(FstFile match)
        {
            int pos = 0;
            long baseOffset = _ctx.AreaFsOffset;
            long fsOffset = match.FsOffset;
            int blockFsSize = _ctx.BlockFsSize;
            List<FstFolder> dirTables = new List<FstFolder>();
            ImageHeaderPvd pvd = _ctx.Pvd[((FstFolder)match.Parent).FsType];
            dirTables.Add(pvd.RootFolder);
            while (pos < (int)match.FsSize)
            {
                PathTableRecord ptr = PathTableRecord.Parse(fsOffset + pos, blockFsSize, _ctx.FsBuff, pos, match.Endian == FsBlockEndian.Big, baseOffset);
                if (ptr.Size == 0)
                    return;

                int index = _ctx.Ahead.KeyIndex(ptr.ExtentOffset, out bool exists);
                FstFile f;
                if (exists) //Path table already processed for other endian
                {
                    f = _ctx.Ahead[index];
                    if (match.Endian != f.Endian)
                        f.Endian = FsBlockEndian.Both;
                }
                else
                {
                    if (dirTables.Count > ptr.ParentDirectoryNo - 1)
                    {
                        FstFolder fld = new FstFolder(pvd.GetNameString(ptr.DirectoryName, out bool isRomeo), pvd.Type, dirTables[ptr.ParentDirectoryNo - 1]);
                        if (isRomeo)
                            fld.Romeo = isRomeo;
                        if (pos != 0) //root dir
                            dirTables.Add(fld);
                        f = _ctx.AddFile(dirTables.Last(), $"__fst_{ptr.ExtentOffset:X}", fld.FsType, ptr.ExtentOffset, -1, FsItemType.DirectoryEntry, true, match.Endian);
                        if (f.FsOffset == 0)
                            _ctx.AddMissing(f);
                    }
                    else //missing index
                    {
                    }

                }
                pos += ptr.Size;
            }
        }

        private void processDirectories(FstFile match)
        {
            Dictionary<string, FsFileParts> splitFiles = null;

            int pos = 0;
            int block = 0;

            while (pos + 1 < match.FsSize)
            {
                FstFile f = processDirectory(ref pos, match, out int index, out bool existed);
                if (pos == -1)
                    pos = (++block) * _ctx.BlockFsSize; //skip to next block
                else if (f != null)
                {
                    FsFileParts splitFile = null;
                    bool exists = splitFiles?.TryGetValue(f.Name, out splitFile) ?? false;
                    if (f.SplitIndex == -1 || exists)
                    {
                        if (splitFiles == null)
                            splitFiles = new Dictionary<string, FsFileParts> { { f.Name, splitFile = new FsFileParts() } };
                        else if (splitFile == null)
                            splitFiles.Add(f.Name, splitFile = new FsFileParts());
                        splitFile.Parts.Add(new FsFilePart() { FsFile = f });
                    }
                }
            }

            //sum the split files
            if (splitFiles != null)
            {
                foreach (KeyValuePair<string, FsFileParts> kv in splitFiles)
                {
                    int i = 0;
                    foreach (FsFilePart f in kv.Value.Parts)
                    {
                        f.OffsetInFile = kv.Value.Size; //current sum is the offset
                        f.Index = i++;
                        ((FstFile)f.FsFile).SplitIndex = f.Index;
                        ((FstFile)f.FsFile).SplitParts = kv.Value;
                        kv.Value.Size += ((FstFile)f.FsFile).FsSize; //sum
                    }

                }

            }
        }

        private FstFile processDirectory(ref int offset, FstFile match, out int index, out bool existed)
        {
            index = -1;
            existed = false;
            FstFile f = null;

            FileSystemDirectoryEntry fsEntry = FileSystemDirectoryEntry.Parse(match.FsOffset, _ctx.BlockFsSize, _ctx.FsBuff, offset, _ctx.AreaFsOffset);
            if (fsEntry == null)
                offset = -1; //no more entries
            else
            {
                offset += fsEntry.FsSize;

                bool parentPointer = fsEntry.ExtentOffset == match.Parent.Files[0].FsOffset; //parent folder pointer

                if (!parentPointer && fsEntry.ExtentOffset != match.FsOffset && fsEntry.ExtentOffset != 0) //not ourselves
                {
                    // For all entries (files AND subdirectories), call AddFile to
                    // trigger the merge path which maintains VFS folder.Files references.
                    FstFolder fld = (FstFolder)match.Parent;
                    f = _ctx.AddFile(fld, _ctx.Pvd[fld.FsType].GetNameString(fsEntry.Name, out bool isRomeo), fld.FsType, fsEntry.ExtentOffset, fsEntry.DataSize, FsItemType.File, false, match.Endian, out index, out existed);

                    if ((fsEntry.Flags & FileFlags.Directory) != 0)
                    {
                        // For directory entries, update size if still unknown, set RockRidge
                        // name, and clear the returned file reference (caller doesn't track directories)
                        if (f.FsSize == -1)
                            f.FsSize = fsEntry.DataSize;
                        if (fsEntry.RockRidge != null)
                            f.RockRidge = fsEntry.RockRidge;
                        f = null;
                    }
                    else
                    {
                        if (f.Links.Any(a => a.FsType == FsType.ElTorito)) //update any eltorito sizes
                            f.FsSize = fsEntry.DataSize;
                        if (isRomeo)
                            f.Romeo = isRomeo;
                        if (fsEntry.RockRidge != null)
                            f.RockRidge = fsEntry.RockRidge; //set as items from the path table created items (they don't contain the rockridge stuff)
                        if (fsEntry.Cdxa)
                            f.Cdxa = fsEntry.Cdxa;
                        f.SplitIndex = (fsEntry.Flags & FileFlags.MultiExtent) != 0 ? -1 : 0;
                    }
                }
            }
            return f;
        }
    }
}