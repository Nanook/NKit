using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IO = System.IO;

namespace Nanook.NKit
{

    internal class WipeWiiGcStep : StepBase, IStep
    {
        private class WipePartitionInfo
        {
            public long ImageOffset;
            public long Size;
            public byte[] Header;
            public byte[] H3;
            public PartitionHeader Certs;
        }

        private IStepContext _context;
        private string _outName;
        private string _outExt;
        private byte[] _tmp;

        private long _junkFsOffset;
        private long _junkLength;
        protected byte[][] _junk;
        private bool _wiiPtn;
        private bool _hasJunk;
        private bool _isWii;
        private byte[] _discId;
        private byte[] _partDiscId;
        private int _discNo;
        //private int _partDiscNo;
        private FileSystemInfo _fsInfo; //incomplete, just set up enough to get junk info

        private ImageHeader _header;
        private WiiSecurity _security;
        private List<WipePartitionInfo> _parts;
        private byte[] _key;

        private byte[] _prtHeader;
        private PartitionType _ptnType;
        private long _prtImageOffset;
        private string _ptnDirName;
        //private OutputType _type;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepWipeWiiGc;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal WipeWiiGcStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            _outName = context.SourceImageName?.Rot13Words();
            _outExt = context.StepConfig;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            _context.SkipBlockTaskEnable();
            _isWii = false;

            base.OutStream.NewPart(_outName, _outExt, true);

            _parts = new List<WipePartitionInfo>();
            if (_context.SystemType == SystemType.Wii)
            {
                _isWii = true;
                _security = new WiiSecurity((int)WiiConsts.WiiGroupSize);
                _key = WiiConsts.NKitWipeTitleKey; //blank key
            }

            _ptnDirName = "";
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            _wiiPtn = section.Type == AreaType.FileSystem && _isWii; //are we a wii partiton

            if (section.Type == AreaType.ImageHeader)
            {
                updatePartitionTable(section.Decrypted);
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size), _isWii); //clone
                string txt = section.ReadBytes(0, 6).ReadString(0, 6).Rot13Words();
                _discId = Encoding.ASCII.GetBytes(txt.Substring(0, 4));
                section.WriteBytes(0, Encoding.ASCII.GetBytes(txt), 0, 6);
                txt = section.ReadBytes(0x20, 0x40).ReadString(0, 0x40).Rot13Words();
                section.WriteBytes(0x20, Encoding.ASCII.GetBytes(txt), 0, 0x40);
                _discNo = _header.DiscNo;
                _junk = null;
            }
            else if (section.Type == AreaType.PartitionHeader)
            {
                _prtImageOffset = section.ImageOffset;
                _parts.Add(new WipePartitionInfo() { ImageOffset = section.ImageOffset, Header = section.Decrypted.Read(0, (int)section.Size) });
                _parts.Last().Certs = new PartitionHeader(WiiConsts.PublicKeyModulus, WiiConsts.PublicKeyModulusRvtR, WiiConsts.PublicKeyExponent, _parts.Last().Header, 0);

                _prtHeader = section.Decrypted.Read(0, (int)section.Size); //clone
                _ptnType = _header.GetPartition(section.ImageOffset).Type;
                if (_ptnType == PartitionType.Game)
                    _ptnDirName = "DATA";
                else if (_ptnType == PartitionType.Update || _ptnType == PartitionType.Channel)
                    _ptnDirName = _ptnType.ToString().ToUpper();
                else if ((int)_ptnType < 0x100)
                    _ptnDirName = $"P{(int)_ptnType}";
                else
                    _ptnDirName = $"P-{SourceFiles.CleanseFileName(Encoding.ASCII.GetString(((uint)_ptnType).ToBytesBE()))}";
            }
            else
            {
                if (!_isWii && section.AreaOffset == 0)
                {
                    string txt = section.ReadBytes(0, 6).ReadString(0, 6).Rot13Words();
                    section.WriteBytes(0, Encoding.ASCII.GetBytes(txt), 0, 6);
                    _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size), _isWii); //clone
                    _discId = Encoding.ASCII.GetBytes(txt.Substring(0, 4));
                    _discNo = _header.DiscNo;
                    IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
                    AreaInfo ai = section.AreaInfo.Clone();
                    ai.SetSecurity(false, ai.IsEncryptionSupported, ai.HasSecurity); //force no encryption
                    buff.ReInitialise(ai, false);
                    buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
                    _fsInfo = new FileSystemInfo(null, section.ImageOffset, _header.Data, (ImageInfo)_context.ImageInfo, _discId, _context.ImageSize, _context.ImageSize);
                    _fsInfo.SetFsInfo(buff, section.ImageOffset); //pass out WipePartition data start
                    _hasJunk = true;
                }
                if (_wiiPtn && section.AreaOffset == 0) //get WipePartition id and patch it
                {
                    string txt = section.ReadBytes(0, 6).ReadString(0, 6).Rot13Words();
                    section.WriteBytes(0, Encoding.ASCII.GetBytes(txt), 0, 6);
                    _partDiscId = Encoding.ASCII.GetBytes(txt.Substring(0, 4));
                    IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
                    AreaInfo ai = section.AreaInfo.Clone();
                    ai.SetSecurity(false, ai.IsEncryptionSupported, ai.HasSecurity); //force no encryption
                    buff.ReInitialise(ai, false);
                    buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
                    _fsInfo = new FileSystemInfo(_header, _prtImageOffset, _prtHeader, (ImageInfo)_context.ImageInfo, _partDiscId, _context.ImageSize, _context.ImageSize);
                    _fsInfo.SetFsInfo(buff, section.ImageOffset - section.AreaOffset); //pass out WipePartition data start
                    _hasJunk = _wiiPtn || (section.Type == AreaType.Other && _fsInfo != null && _fsInfo.Type != PartitionType.Update);
                }
                if (_hasJunk)
                    createJunk(section);
                replaceJunk(section);
                saveFileData(section);
            }

            base.OutStream.Write(section.Encrypted, 0, (int)section.Size); //decrypted if no encryption
        }

        private void updatePartitionTable(byte[] data)
        {
            int offset = WiiConsts.WiiDiscHdrPtnOffset;
            for (int tableIdx = 0; tableIdx < 4; tableIdx++) //up to 4 partitions on the disk
            {
                uint c = data.ReadUInt32B(offset + (tableIdx * 8)); //count of partitions for tableIdx
                if (c != 0)
                {
                    int tableOffset = (int)data.ReadUInt32B(offset + (tableIdx * 8) + 4) * 4; //first WipePartition entry for tableIdx
                    int adjustReadOffset = offset + (tableOffset - WiiConsts.WiiDiscHdrPtnOffset);
                    for (int i = 0; i < c; i++)
                    {
                        long partitionOffset = data.ReadUInt32B(adjustReadOffset + (i * 8)) * 4L;
                        PartitionType partitionType = (PartitionType)data.ReadUInt32B(adjustReadOffset + (i * 8) + 4);
                        if (!Enum.IsDefined(typeof(PartitionType), partitionType))
                        {
                            string id = data.ReadString(adjustReadOffset + (i * 8) + 4, 4).Rot13Words();
                            data.WriteString(adjustReadOffset + (i * 8) + 4, 4, id);
                        }
                    }
                }
            }
        }

        private void createJunk(ISection section) //for wii and gc
        {
            if (_junk == null) //just for first use per WipePartition
            {
                _junk = new byte[(section.Decrypted.Length / NJunk.JunkBlockSize) + (section.Decrypted.Length % NJunk.JunkBlockSize == 0 ? 1 : 2)][]; //1 extra for alignment compensation
                for (int i = 0; i < _junk.Length; i++)
                    _junk[i] = new byte[NJunk.JunkBlockSize];
            }

            long fullSize = _wiiPtn || !_isWii ? _fsInfo.FsSize : _context.ImageInfo.ImageSize;
            _junkLength = _wiiPtn ? section.FsSize : section.Size;
            _junkFsOffset = _wiiPtn ? section.FsOffset : section.ImageOffset;

            long junkDiff = _junkFsOffset % NJunk.JunkBlockSize; //move to the start of a junk block
            if (junkDiff != 0)
            {
                _junkFsOffset -= junkDiff;
                _junkLength += junkDiff;
            }

            junkDiff = (_junkFsOffset + _junkLength) % NJunk.JunkBlockSize; //move to the start of a junk block
            if (junkDiff != 0)
                _junkLength += NJunk.JunkBlockSize - junkDiff;

            if (_isWii && !_wiiPtn && !_hasJunk) //wii disc, but not a WipePartition
                fullSize = 0; //offset < 0xF800000 then junk is zeros

            byte[] junkId = _wiiPtn || !_isWii ? _fsInfo.JunkId : (_fsInfo != null && section.Type == AreaType.Other && _fsInfo.Type == PartitionType.Game ? _fsInfo.JunkId : _discId);
            int discNo = _wiiPtn ? _fsInfo.DiscNo : _discNo;
            long startOffset = _fsInfo?.JunkStartFsOffset ?? 0;

            Parallel.For(0, (int)(_junkLength / NJunk.JunkBlockSize), i =>
                NJunk.Fill(junkId, discNo, startOffset, fullSize, _junkFsOffset + (i * (long)NJunk.JunkBlockSize), _junk[i]));
        }

        private void replaceJunk(ISection section)
        {
            foreach (SectionItem si in section.Items)
            {
                if (si.Gap != null)
                {
                    if (si.Gap.DataType == DataType.NJunk)
                    {
                        if (_hasJunk)
                            junkFill(section, (int)si.Gap.FsOffset, (int)si.Gap.FsSize, si.Gap.DataNulls);
                        else
                            section.Write((int)si.Gap.FsOffset, ByteStream.Zeros, (int)si.Gap.FsSize);
                    }
                    else if (si.Gap.DataType == DataType.Other)
                    {
                        foreach (ISectionData gi in si.GapInfo)
                        {
                            if (gi.DataType == DataType.NJunk)
                            {
                                if (_hasJunk)
                                    junkFill(section, (int)gi.FsOffset, (int)gi.FsSize, gi.DataNulls);
                                else
                                    section.Write((int)si.Gap.FsOffset, ByteStream.Zeros, (int)si.Gap.FsSize);
                            }
                        }
                    }
                }
            }
        }

        private void junkFill(ISection section, int sectionFsOffset, int size, int nulls) //skips hashes
        {
            if (nulls != 0)
            {
                section.Write(sectionFsOffset, ByteStream.Zeros, nulls);
                sectionFsOffset += nulls;
                size -= nulls;
            }

            if (size >= 0)
            {
                int joff = (int)((_wiiPtn ? section.FsOffset : section.ImageOffset) - _junkFsOffset); //align the junk block if it starts before this section
                int junkIdx = (sectionFsOffset + joff) / NJunk.JunkBlockSize;
                int junkOffset = (sectionFsOffset + joff) % NJunk.JunkBlockSize;

                int sz = Math.Min(size, NJunk.JunkBlockSize - junkOffset);
                while (size != 0)
                {
                    section.WriteBytes(sectionFsOffset, _junk[junkIdx], junkOffset, sz);
                    size -= sz;
                    junkIdx++;
                    junkOffset = 0;
                    sectionFsOffset += sz;
                    sz = Math.Min(size, NJunk.JunkBlockSize);
                }
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            foreach (WipePartitionInfo part in _parts)
            {
                base.OutStream.Seek(part.ImageOffset, IO.SeekOrigin.Begin);
                if (part.H3 != null)
                    part.Header.Write(part.Header.Length - part.H3.Length, part.H3, part.H3.Length);
                part.Certs.CertValidator.WipeHeaderChain(part.Header, _key, true);
                base.OutStream.Write(part.Header, 0, part.Header.Length);
            }
            base.ProcessResults();
        }

        private void saveFileData(ISection section)
        {
            if (section.Type == AreaType.FileSystem)
            {
                FileSystemInfo fsInfo = (FileSystemInfo)((SectionProcessor)section).FileSystemData;
                WipePartitionInfo part = _parts.LastOrDefault();
                if (_isWii)
                    part.Size += section.Size;

                if (section.AreaOffset == 0) //start of the fs (FST will have been parsed)
                {
                    if (_isWii && !fsInfo.IsRvtH)
                        part.H3 = (byte[])fsInfo.H3Table.Clone();

                    if (section.FullAreaFileSystem != null)
                    {
                        if (_isWii)
                        {
                            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, WiiConsts.PublicKeyModulusRvtR, WiiConsts.PublicKeyExponent, _prtHeader, 0);
                            bool isRvt = section.Decrypted.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1 && Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetIssuer(section.Decrypted) != WiiConsts.RvtIssuer;
                            bool isRvtH = section.Decrypted.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset) << 2 == 0;

                            byte[] cert = null;
                            using (IO.MemoryStream crtStream = new IO.MemoryStream()) //cert.bin
                            {
                                foreach (SignedData crt in ph.CertValidator.Items)
                                    crtStream.Write(crt.Data, 0, crt.Data.Length);
                                cert = crtStream.ToArray();
                            }

                            var sysFiles = new[]
                            {
                                new { Name = $"header.bin", Path = $"/{_ptnDirName}/disc/", Data = _header.Data.Read(0, 0x100) },
                                new { Name = $"region.bin", Path = $"/{_ptnDirName}/disc/", Data = _header.Data.Read(WiiConsts.WiiDiscHdrRgnOffset, WiiConsts.WiiDiscHdrRgn2Size * 2) },
                                new { Name = $"ticket.bin", Path = $"/{_ptnDirName}/", ph.CertValidator.Ticket.Data },
                                new { Name = $"tmd.bin", Path = $"/{_ptnDirName}/", ph.CertValidator.Tmd.Data },
                                new { Name = $"cert.bin", Path = $"/{_ptnDirName}/", Data = cert },
                                new { Name = $"h3.bin", Path = $"/{_ptnDirName}/", Data = isRvtH ? null : _prtHeader.Read((int)(_prtHeader.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2), WiiConsts.WiiPrtHdrH3Size) },
                            };

                            foreach (var sysFile in sysFiles)
                            {
                                if (sysFile.Data != null)
                                {
                                }
                            }
                        }
                        else //gc
                        {
                        }
                    }
                }

                foreach (SectionItem si in section.Items)
                {
                    if (si.FsFile == null || si.File == null)
                        continue;

                    if (si.FsFile.IsSystemFile)
                    {
                        if (si.FsFile.Name == "__boot.bin")
                        {
                            string txt = section.ReadBytes(0x20, 0x40).ReadString(0, 0x40).Rot13Words();
                            section.WriteBytes(0x20, Encoding.ASCII.GetBytes(txt), 0, 0x40);
                        }
                        else if (si.FsFile.Name == "__apploader.img")
                        {
                            int off = si.File.OffsetInItem == 0 ? 0x1c : 0x0; //keep first 0x1c bytes (size info)
                            section.Write((int)si.File.FsOffset + off, ByteStream.Zeros, (int)si.File.FsSize - off);
                        }
                        else if (si.FsFile.Name == "__main.dol")
                        {
                            int off = si.File.OffsetInItem == 0 ? 0x90 + (0x12 * 0x4) : 0x0; //keep first 0x1c bytes (size info)
                            section.Write((int)si.File.FsOffset + off, ByteStream.Zeros, (int)si.File.FsSize - off);
                        }
                        else if (si.FsFile.Name == "__fst.bin")
                        {
                            if (si.File.OffsetInItem == 0)
                            {
                                _tmp = (byte[])fsInfo.FstBin.Clone();
                                Encoding enc = Encoding.GetEncoding("Shift-JIS");
                                for (int i = (int)_tmp.ReadUInt32B(0x8) * 0xc; i + 1 < _tmp.Length;)
                                {
                                    string fn = _tmp.ReadStringToNull(enc, i, _tmp.Length - i);
                                    if (fn != "opening.bnr")
                                    {
                                        fn = fn.Rot13Words();
                                        _tmp.WriteString(i, fn.Length, fn, enc);
                                    }
                                    while (_tmp[++i] != 0) ;
                                    i++;
                                }
                            }
                            section.WriteBytes((int)si.File.FsOffset, _tmp, (int)si.File.OffsetInItem, (int)si.File.FsSize);
                            if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                                _tmp = null;
                        }
                    }
                    else
                        section.Write((int)si.File.FsOffset, ByteStream.Zeros, (int)si.File.FsSize);

                    //writeFs(section, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize);

                    if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize) //is this the end of the file
                    {
                        //complete
                    }
                }

                bool hasSecurity = section.IsEncrypted;

                if (hasSecurity && _security != null)
                {
                    _security.Populate(_key, section.Encrypted, section.Decrypted, (int)section.Size, true, true, true, section.AreaOffset, part.H3, null, false);
                    _security.MarkDirty();
                    _security.IsValid(true, out _);
                    _security.UpdateH3Entry();
                    _security.Encrypt();
                }
            }
        }

    }
}