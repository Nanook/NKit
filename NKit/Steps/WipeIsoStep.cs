using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{

    internal class WipeIsoStep : StepBase, IStep
    {
        const ushort _Terminator = 0xffff;

        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private bool _split;
        private OutputType _type;
        private ContainerType _container;
        private int _pathNext;
        private int _pathNmOff;
        private int _pathNmLen;
        private byte[] _tag = new byte[0x800];
        private int _tagPos = 0;
        private int _tagSz = 0;
        private const int _MinSz = 0x27;
        private PlayStation3 _ps3;
        private string[] _ps3Ignore;
        private bool _isXBox;

        private long _lastAreaOffset;

        private SourceFileTrack[] _tracks;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => _type;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepWipeIso;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        private string replace(string name) => Regex.Replace(name, Regex.Escape(_context.SourceImageName), _outName, RegexOptions.IgnoreCase);

        internal WipeIsoStep(IStepContextConstruct context)
        {
            _isXBox = context.SystemType == SystemType.XBox || context.SystemType == SystemType.XBox360;
            _type = (!_isXBox && context.ImageInfo.IsFolderIndex) ? OutputType.FolderIndex : OutputType.Image;
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName?.Rot13Words();
            _outExt = _type == OutputType.Image ? context.StepConfig : "";
            _ps3Ignore = new string[] { "lic.dat", "eboot.bin", "ps3_disc.sfb", "param.sfo", "ps3updat.pup", "ps3_update", "ps3_game" };
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            if (_context.SystemType == SystemType.PS3)
                _ps3 = new PlayStation3(_context.HeaderData, new byte[16], _context.ImageSize, _context.Settings.AllKeys); // Encoding.ASCII.GetBytes("NKITNKITNKITNKIT")); //blank key
            _lastAreaOffset = -1;
            _tracks = _context.ImageInfo.Tracks?.Select(a => a.Clone()).ToArray();
            if (_tracks != null)
            {
                foreach (SourceFileTrack t in _tracks)
                    t.FileName = replace(t.FileName);
            }
            _split = (_tracks?.Length ?? 1) > 1; //output is split
            _container = _context.ImageInfo.ContainerType == ContainerType.Chd ? ContainerType.Cue : _context.ImageInfo.ContainerType;

            if (_context.ImageInfo.IsFolderIndex)
                this.OutStream.NewPart(Path.GetFileNameWithoutExtension(_tracks?[0].FileName), Path.GetExtension(_tracks?[0].FileName), true);
            else
                base.OutStream.NewPart(_outName, _outExt, true);
        }

        public override void Process(ISection section)
        {
            if (base.InChkStream != null && section.AreaOffset == 0 && section.ImageOffset != 0 && Context.ImageInfo.IsFolderIndex) // per part
                base.InChkStream.NewPart(null, null, false);
            base.Process(section);

            //don't do when not new 0 base FS
            if (section.AreaInfo.BaseOffset == 0 && section.AreaInfo.ImageOffset != _lastAreaOffset)
                _lastAreaOffset = section.AreaOffset;

            if (section.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in section.Items)
                {
                    int keep = 0;
                    if (si.File != null)
                    {
                        if (!si.FsFile.IsSystemFile)
                        {
                            if (_context.SystemType == SystemType.PS3 && si.File.OffsetInItem == 0)
                            {
                                if (String.Compare(si.FsFile.Name, "lic.dat", true) == 0)
                                    keep = section.ReadBytes((int)si.File.Offset, 6).ReadString(0, 6) == "PS3LIC" ? 6 : 0;
                                else if (String.Compare(si.FsFile.Name, "eboot.bin", true) == 0)
                                    keep = section.ReadBytes((int)si.File.Offset, 3).ReadString(0, 3) == "SCE" ? 3 : 0;
                                else if (String.Compare(si.FsFile.Name, "ps3_disc.sfb", true) == 0)
                                {
                                    keep = section.ReadBytes((int)si.File.Offset, 4).ReadString(0, 4) == ".SFB" ? (int)si.File.FsSize : 0;
                                    wipeDiscSfbValues(section.Decrypted, (int)si.File.Offset);
                                }
                                else if (String.Compare(si.FsFile.Name, "param.sfo", true) == 0)
                                {
                                    keep = section.ReadBytes((int)si.File.Offset, 4).ReadString(0, 4) == "\0PSF" ? (int)si.File.FsSize : 0;
                                    wipeParamSfoValues(section.Decrypted, (int)si.File.Offset);
                                }
                                else if (String.Compare(si.FsFile.Name, "ps3updat.pup", true) == 0)
                                    keep = section.ReadBytes((int)si.File.Offset, 5).ReadString(0, 5) == "SCEUF" ? 3 : 0;
                                section.Write((int)si.File.FsOffset + keep, ByteStream.Zeros, (int)si.File.FsSize - keep);
                            }

                            if (!section.IsEncrypted)
                                section.Write((int)si.File.FsOffset + keep, ByteStream.Zeros, (int)si.File.FsSize - keep);
                            else if (section.IsEncrypted) //ps3
                                Array.Clear(section.Encrypted, (int)si.File.FsOffset, (int)si.File.FsSize);
                        }
                        else if (_isXBox && si.FsFile is Microsoft.XBox.FstFile)
                            processSystemXBoxFile(section, si);
                        else if (si.FsFile.FsOffset == 0 && ((FstFile)si.FsFile).Type == FsItemType.CustomArea)
                            processCustomHeader(section, si);
                        else
                            processSystemFile(section, si);
                    }
                    if (si.Gap != null)
                    {
                        if (_isXBox)
                            Array.Clear(section.Decrypted, (int)si.Gap.FsOffset, (int)si.Gap.FsSize);

                        //don't clear gaps
                        //if (section.IsEncrypted)
                        //    Array.Clear(section.Encrypted, (int)si.Gap.FsOffset, (int)si.Gap.FsSize);
                        //else
                        //    Array.Clear(section.Decrypted, (int)si.Gap.FsOffset, (int)si.Gap.FsSize);
                    }
                    if (keep != 0 && section.IsEncrypted) //copy encrypted bytes over
                        _ps3.Encrypt(section.ImageOffset + si.File.Offset, 0x800, (int)si.File.Offset, section.Decrypted, section.Encrypted);
                }

                if (_context.SystemType == SystemType.PS3 && section.AreaOffset == 0 && section.AreaInfo.AreaNo == 1)
                    _ps3.Encrypt(section.ImageOffset, 0x800, 0, section.Decrypted, section.Encrypted);

                int blockSize = section.AreaInfo.BlockSize;
                for (int i = 0; i < section.Size; i += blockSize)
                {
                    Ecm.GetModeInfo(section.Decrypted, i, blockSize, out bool mode1, out bool mode2Form1, out bool mode2Form2);
                    if (mode2Form2)
                        Array.Clear(section.Decrypted, i + section.AreaInfo.BlockFsOffset + section.AreaInfo.BlockFsSize, section.AreaInfo.BlockSize - (section.AreaInfo.BlockFsOffset + section.AreaInfo.BlockFsSize) - 4);
                    Ecm.ReconstructEcc(section.Decrypted, i, mode1, mode2Form1, mode2Form2);
                }
            }
            else if (section.Type == AreaType.Audio)
                section.Write(0, ByteStream.Zeros, (int)section.Size);

            writeData(section.Encrypted, 0, (int)section.Size); //decrypted if no encryption
        }

        private void processCustomHeader(ISection section, SectionItem si)
        {
            if (_context.SystemType == SystemType.PS2)
            {
                section.Write(0, ByteStream.Fives, (int)si.FsFile.FsSize);
                byte[] hdr = "43A445A546662727E708A8E9C90BAB6B4C6D4D4D6DACEA08C503".HexToBytes();
                byte xor = 0x55;
                for (int i = 0; i < hdr.Length; i++)
                    hdr[i] ^= xor;
                section.WriteBytes(0x130, hdr, 0, hdr.Length);
            }
            else
            {
                int keep = 0;
                if (_context.SystemType == SystemType.PS3)
                    keep = 0x100;
                section.Write(keep, ByteStream.Zeros, (int)(si.FsFile.FsSize - keep));

                if (_context.SystemType == SystemType.PS1)
                    section.WriteBytes(0x2020, Encoding.ASCII.GetBytes(@"Sony Computer Entertainment"), 0, 0x1b);
                else if (_context.SystemType == SystemType.PS3)
                    section.WriteBytes(0x800, Encoding.ASCII.GetBytes(@"PlayStation3"), 0, 0xc);
                else if (_context.SystemType == SystemType.SegaCD)
                    section.WriteBytes(0x0, Encoding.ASCII.GetBytes(@"SEGADISCSYSTEM"), 0, 0xe);
                else if (_context.SystemType == SystemType.Saturn)
                    section.WriteBytes(0x0, Encoding.ASCII.GetBytes(@"SEGA SEGASATURN"), 0, 0xf);
                else if (_context.SystemType == SystemType.Dreamcast)
                {
                    section.Write(0, ByteStream.Zeros, 0x100);
                    section.WriteBytes(0x0, Encoding.ASCII.GetBytes(@"SEGA SEGAKATANA"), 0, 0xf);
                }
            }
        }

        private void processSystemXBoxFile(ISection section, SectionItem si)
        {
            Microsoft.XBox.FstFile fs = (Microsoft.XBox.FstFile)si.FsFile;

            ISectionData f = si.File;

            for (int i = 0; i < f.FsSize; i += section.AreaInfo.BlockSize)
            {
                switch (fs.Type)
                {
                    case Microsoft.XBox.FsItemType.Volume:
                        break;
                    case Microsoft.XBox.FsItemType.RootFs:
                    case Microsoft.XBox.FsItemType.DirectoryEntry:
                        rot13XBoxDirEntries(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, (Microsoft.XBox.FstFile)si.FsFile);
                        break;
                }
            }
        }

        private void processSystemFile(ISection section, SectionItem si)
        {
            FstFile fs = (FstFile)si.FsFile;
            ISectionData f = si.File;

            for (int i = 0; i < f.FsSize; i += section.AreaInfo.BlockSize)
            {
                switch (fs.Type)
                {
                    case FsItemType.Pvd:
                        rot13PathPvd(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, fs);
                        break;
                    case FsItemType.PathTable:
                        if (f.OffsetInItem + i == 0)
                            _pathNext = 0;
                        rot13PathTable(section.Decrypted, i + (int)f.Offset, (int)Math.Min(section.AreaInfo.BlockSize, f.FsSize - i), fs);
                        break;
                    case FsItemType.DirectoryEntry:
                        if (((FstFolder)fs.Parent).FsType != FsType.Udf)
                            rot13DirEntries(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, fs);
                        else
                            rot13UdfDirEntries(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, fs);
                        break;
                    case FsItemType.UdfVrs:
                        rot13UdfVrs(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, fs);
                        break;
                    case FsItemType.UdfFileSet:
                        rot13UdfPartition(section.Decrypted, i + (int)f.Offset, section.AreaInfo.BlockSize, fs);
                        break;
                }
            }
        }

        private void rot13PathPvd(byte[] buff, int offset, int length, FstFile fs)
        {
            if (buff.Read8(offset + 0x0000) == 0xff)
            {
            }
            else if (buff.ReadString(offset + 0x7, 0x20).TrimEnd('\0') == "EL TORITO SPECIFICATION")
            {
            }
            else
            {
                int keep = 0;
                if (_context.SystemType == SystemType.PSP)
                    keep = 8;
                int off = offset;
                rot13Rename(buff, fs, off + 0x8 + keep, 0x20 - keep);
                rot13Rename(buff, fs, off + 0x28, 0x20);
                off += 0x9c;
                int rootDirLen = buff.Read8(off);
                int rootDirNameLen = buff.Read8(off + 0x20);
                if (rootDirNameLen > 1) //null terminated
                    rot13Rename(buff, fs, off + 0x21, rootDirNameLen);
                off += rootDirLen;
                rot13Rename(buff, fs, off, 0x80);
                rot13Rename(buff, fs, off + 0x80, 0x80);
                rot13Rename(buff, fs, off + 0x100, 0x80);
                rot13Rename(buff, fs, off + 0x180, 0x80);
                rot13Rename(buff, fs, off + 0x200, 0x25);
                rot13Rename(buff, fs, off + 0x225, 0x25);
                rot13Rename(buff, fs, off + 0x24a, 0x25);
                Array.Clear(buff, off + 0x2b5, length - (off - offset + 0x2b5));
            }
        }

        private void rot13PathTable(byte[] buff, int offset, int length, FstFile fs)
        {
            int off = 0;
            int o = offset;
            int nl;
            int sz;

            if (_pathNext != 0)
            {
                rot13Rename(buff, fs, offset + _pathNmOff, _pathNmLen);
                off = _pathNext;
                o += _pathNext;
                _pathNext = _pathNmOff = _pathNmLen = 0;
            }

            while ((nl = buff.Read8(o + 0x00)) != 0 && off < length)
            {
                sz = 8 + nl + (nl % 2 == 1 ? 1 : 0);
                off += sz;
                _pathNext = Math.Max(off - length, 0);
                if (_pathNext != 0)
                {
                    int diff = o + 8 - (offset + length);
                    _pathNmLen = nl + diff;
                    _pathNmOff = Math.Max(0, o + 8 - (offset + length));
                    if (diff < 0)
                        rot13Rename(buff, fs, o + 8, -diff);
                    break;
                }
                rot13Rename(buff, fs, o + 8, nl);
                o = offset + off;
            }
        }

        private void rot13XBoxDirEntries(byte[] buff, int offset, int size, Microsoft.XBox.FstFile fs)
        {
            ushort leftOffset = 0;
            int off = offset;

            while (off - offset < size)
            {
                if (off - offset >= size || (leftOffset = buff.ReadUInt16L(off)) == _Terminator)
                    break;

                byte nameLen = buff.Read8(off + 0xd);
                rot13Rename(buff, off + 0xe, nameLen);

                off += 0xe + nameLen;
                off += off % 4 == 0 ? 0 : (4 - (off % 4));
            }

        }

        private void rot13DirEntries(byte[] buff, int offset, int length, FstFile fs)
        {
            int off = 0;
            int o = offset;
            int sz;
            while ((sz = buff.Read8(o + 0x00)) != 0 && off < length)
            {
                int nl = buff.Read8(o + 0x20);
                if (nl != 0)
                {
                    rot13Rename(buff, fs, o + 0x21, nl);

                    int rrOff = 0x21 + nl + ((o + 0x21 + nl) % 2);
                    if (rrOff < length)
                    {
                        while (sz - rrOff >= 3)
                        {
                            int rsz = (int)buff[rrOff + o + 2];
                            if (rsz == 0)
                                break;
                            if (buff.ReadString(rrOff + o, 2) == "NM" && buff[rrOff + o + 4] == 0) //rockridge
                                rot13Rename(buff, fs, rrOff + o + 5, rsz - 5);
                            rrOff += rsz;
                        }
                    }
                }
                off += sz;
                o = offset + off;
            }
        }

        private void rot13UdfDirEntries(byte[] buff, int offset, int length, FstFile fs)
        {
            int off = 0;
            int fullOff = offset;
            int nl;

            if (_pathNext != 0)
            {
                rot13Rename(buff, fs, offset + _pathNmOff, _pathNmLen);
                off = _pathNext;
                fullOff += _pathNext;
                _pathNext = 0;
            }

            while (off < length)
            {
                int txtOff;
                if (_tagPos != 0)
                {
                    if (_tagPos < _MinSz) // get the rest of the 0x27 bytes
                        Array.Copy(buff, fullOff, _tag, _tagPos, _MinSz - _tagPos);
                    int impLen = _tag.ReadUInt16L(0x24); //UdfFileId
                    _tagSz = _MinSz + impLen;
                    if (impLen != 0) //get extra if Implementation was used
                        Array.Copy(buff, fullOff, _tag, _MinSz, impLen);
                    _tagSz += _MinSz + impLen;
                    nl = Math.Max(_tag.Read8(0x13) - 1, 0);
                    txtOff = _MinSz + impLen - _tagPos;
                    _tagPos = 0;
                }
                else if (off + _MinSz >= length)
                {
                    if (buff.ReadUInt16L(fullOff) == 0)
                        return;
                    _tagPos = length - off;
                    Array.Copy(buff, fullOff, _tag, 0, _tagPos);
                    break;
                }
                else
                {
                    if (buff.ReadUInt16L(fullOff) == 0)
                        return;
                    int impLen = buff.ReadUInt16L(fullOff + 0x24); //UdfFileId
                    _tagSz = _MinSz + impLen;
                    if (off + _tagSz >= length) //not enough bytes
                    {
                        _tagPos = length - off;
                        Array.Copy(buff, fullOff, _tag, 0, _tagPos);
                        break;
                    }
                    nl = Math.Max(buff.Read8(fullOff + 0x13) - 1, 0);
                    txtOff = _tagSz;
                }
                off += txtOff + nl + ((txtOff + nl) % 4 == 0 ? 0 : (4 - ((txtOff + nl) % 4)));
                if (nl != 0)
                {
                    _pathNext = Math.Max(off - length, 0);
                    if (_pathNext != 0)
                    {
                        int diff = fullOff + txtOff - (offset + length);
                        _pathNmLen = nl + diff;
                        _pathNmOff = Math.Max(0, fullOff + txtOff - (offset + length));
                        if (diff < 0)
                            rot13Rename(buff, fs, fullOff + txtOff, -diff);
                        break;
                    }
                    rot13Rename(buff, fs, fullOff + txtOff, nl);
                }
                fullOff = offset + off;
            }
        }

        private void rot13Rename(byte[] buff, FstFile fs, int o, int l)
        {
            Encoding enc = Encoding.Default;
            if (fs.Links.Any(a => a.FsType == FsType.Iso9660))
                enc = Encoding.ASCII;
            else if (fs.Links.Any(a => a.FsType == FsType.Joliet))
                enc = Encoding.BigEndianUnicode;
            else if (fs.Links.Any(a => a.FsType == FsType.Udf))
                enc = buff[o - 1] == 16 ? Encoding.BigEndianUnicode : Encoding.ASCII;
            string nm = buff.ReadString(o, l, enc);
            if (nm[0] != '\u0000' && nm[0] != '\u0001' && nm[0] != '\ufffd')
            {
                if (nm.Length > 2 && nm[nm.Length - 2] == ';')
                    nm = nm.Substring(0, nm.Length - 2);
                if (_context.SystemType != SystemType.PS3 || !_ps3Ignore.Any(a => string.Compare(a, nm, true) == 0))
                {
                    nm = nm.Rot13Words();
                    buff.WriteString(o, nm.Length, nm, enc);
                }
            }
        }

        private void rot13Rename(byte[] buff, int o, int l)
        {
            string nm = buff.ReadString(o, l);
            nm = nm.Rot13Words();
            buff.WriteString(o, nm.Length, nm);
        }

        private void rot13UdfVrs(byte[] buff, int offset, int length, FstFile fs)
        {
            UdfDescriptorTag rec = UdfDescriptorTag.Parse(buff, offset);
            if (rec.TagId == UdfTagId.PrimaryVolumeDescriptor)
            {
                rot13Rename(buff, fs, offset + 0x19, 0x1d);
                rot13Rename(buff, fs, offset + 0x49, 0x7d);
            }
            else if (rec.TagId == UdfTagId.ImplementationUseVolumeDescriptor)
            {
                int ioff = offset + 0x10 + 0x20 + 0x4; //inline implementation use offset
                rot13Rename(buff, fs, ioff + 0x41, 0x7d);
            }
            else if (rec.TagId == UdfTagId.LogicalVolumeDescriptor)
            {
                rot13Rename(buff, fs, offset + 0x55, 0x7d);
            }
        }

        private void rot13UdfPartition(byte[] buff, int offset, int length, FstFile fs)
        {
            rot13Rename(buff, fs, offset + 0x71, 0x7d);
            rot13Rename(buff, fs, offset + 0x131, 0x1d);
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            //_context.ScanInvalidated = true; //don't save scans

            if (_context.ImageInfo.IsFolderIndex)
            {
                string name = Path.GetFileName(_outName);
                string fn = $"{name}.{_context.ImageInfo.ContainerType.ToString().ToLower()}";

                string file;
                if (this.Context.ImageInfo.ContainerType == ContainerType.Chd || this.Context.SourceFile.IndexFile == null)
                {
                    List<string> names = _tracks.Select(a => Path.GetFileName(a.FileName)).ToList();
                    if (_container == ContainerType.Toc)
                        file = IndexFile.ToToc(names, _context.ImageInfo.CdDiscType ?? CdType.Cdrom, _tracks);
                    else if (_container == ContainerType.Gdi)
                        file = IndexFile.ToGdi(names, _tracks);
                    else
                        file = IndexFile.ToCue(names, _split, _tracks);
                }
                else
                {
                    fn = Path.GetFileName(replace(_context.SourceFile.IndexFile.FileName));
                    file = Encoding.UTF8.GetString(_context.SourceFile.IndexFile.Data);
                    file = file.Replace(_context.SourceImageName, _outName);
                }
                base.OutStream.WriteAdditionalFile(Encoding.UTF8.GetBytes(file), 0, -1, fn, true, true);
            }

            base.ProcessResults();
        }

        private void writeData(byte[] buff, int offset, int sz)
        {
            int files = this.OutStream.ChecksummedFiles.Count;
            SourceFileTrack t = !_split ? null : _tracks[files];
            int total = 0;

            while (total != sz)
            {
                if (_split && t.Size == base.OutStream.Position)
                {
                    bool cached = false;

                    t = _tracks[files + 1];
                    this.OutStream.NewPart(Path.GetFileNameWithoutExtension(t.FileName), Path.GetExtension(t.FileName), true);
                    if (cached)
                        return; //don't write next track
                }

                int write = !_split ? sz : (int)Math.Min(sz - total, t.Size - base.OutStream.Position);
                base.OutStream.Write(buff, offset + total, write); //decrypted if no encryption
                total += write;
            }
        }

        private void wipeDiscSfbValues(byte[] data, int offset)
        {
            if (data.ReadString(offset + 0x0, 0x4) != ".SFB")
                return;

            //int version = data.ReadUInt16B(0x4);
            int pos = offset + 0x20;
            string field = data.ReadStringToNull(pos, 0x10);
            while (field != "")
            {
                //if (field != "VERSION")
                //{
                int o = offset + (int)data.ReadUInt32B(pos + 0x10);
                int sz = (int)data.ReadUInt32B(pos + 0x14);
                data.WriteString(o, sz, data.ReadString(o, sz).Rot13Words());
                pos += 0x20;
                field = data.ReadStringToNull(pos);
                //}
            }
        }

        private void wipeParamSfoValues(byte[] data, int offset)
        {
            if (data.ReadString(offset + 0x0, 0x4) != "\0PSF")
                return;

            //int version = (int)data.ReadUInt32L(0x4);
            int keyTableStart = (int)data.ReadUInt32L(offset + 0x8);
            int dataTableStart = (int)data.ReadUInt32L(offset + 0xc);
            uint paramCount = data.ReadUInt32L(offset + 0x10);
            // Parse parameter metadata
            short[] keyOffset = new short[paramCount];
            short[] dataFormat = new short[paramCount];
            int[] dataLength = new int[paramCount];
            int[] dataTotal = new int[paramCount];
            int[] dataOffset = new int[paramCount];
            int pos = offset + 0x14;
            for (int i = 0; i < paramCount; i++)
            {
                keyOffset[i] = (short)data.ReadUInt16L(pos + 0x0);
                dataFormat[i] = (short)data.ReadUInt16L(pos + 0x2);
                dataLength[i] = (int)data.ReadUInt32L(pos + 0x4);
                dataTotal[i] = (int)data.ReadUInt32L(pos + 0x8);
                dataOffset[i] = (int)data.ReadUInt32L(pos + 0xc);
                pos += 0x10;
            }

            for (int i = 0; i < paramCount; i++)
            {
                int pos2 = offset + keyTableStart + keyOffset[i];
                int sz = ((i == paramCount - 1) ? dataTableStart - keyTableStart : keyOffset[i + 1]) - keyOffset[i];
                string k = data.ReadStringToNull(pos2, sz);
                pos2 = offset + dataTableStart + dataOffset[i];
                if (dataFormat[i] == 0x0004)
                    data.WriteString(pos2, dataLength[i], data.ReadString(pos2, dataLength[i], Encoding.UTF8).Rot13Words(), Encoding.UTF8);
                else if (dataFormat[i] == 0x0404)
                {
                    //data.ReadUInt32L(pos2).ToString();
                }
                else //if (dataFormat[i] == 0x0204)
                    data.WriteString(pos2, dataLength[i], data.ReadString(pos2, dataLength[i], Encoding.UTF8).Rot13Words(), Encoding.UTF8);
            }

        }

    }
}