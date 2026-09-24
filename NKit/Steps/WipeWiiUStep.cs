using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IO = System.IO;

namespace Nanook.NKit
{

    internal class WipeWiiUStep : StepBase, IStep
    {
        private class Gap
        {
            public long SrcImageOffset;
            public long SrcSize;
            public long DstImageOffset;
            public long DstSize;
        }

        private class Si
        {
            public ulong TitleId;
            public byte[] SiBuffer;
            public SiData SiData;
            public FileSystemInfo SiFsInfo;
            public long SiImageOffset;
        }

        private class WipePartition
        {
            public long ImageOffset;
            public byte[] Header;
            public int Index;
            public ulong TitleId;
            public Si Si;
            public FileSystemInfo FsInfo;
            public Dictionary<long, byte[]> ContentH3 = new Dictionary<long, byte[]>();
        }
        private class sectionCache
        {
            public long ImageOffset;
            public long Size;
            public byte[] Data;
        }

        private class h2Set
        {
            public sectionCache[] H2Items = new sectionCache[WiiUConsts.H2Count];

            public int Count = 0;
            public long FullH2Size;
            public long FullSize;
        }

        private class siPartition
        {
            //public byte[] Data;
            public Dictionary<string, long> Files = new Dictionary<string, long>();
        }

        private IStepContext _context;
        private string _outName;
        private string _outExt;
        // True when the SOURCE is already a wiped image. Wipe's ROT13/Rot3 name obfuscation is its own
        // inverse, so applying it again to an already-wiped image would REVERSE it (un-obfuscate the
        // names/IDs). When the source is already wiped we re-wipe content/keys idempotently but leave
        // the already-obfuscated names/IDs untouched.
        private bool _alreadyWiped;

        private byte[] _enc;
        private ImageHeader _header;
        private WiiUSecurity _security;
        private FileSystemInfo _fsInfo;
        private List<Si> _sis;
        private List<WipePartition> _ptns;
        private List<Gap> _gaps;
        private h2Set _h2Set;

        private PartitionInfo _ptn;
        private ContentHeader _cntHeader;
        private OutputType _type;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => _type;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepWipeWiiU;

        public override string ProposedName()
        {
            if (_type == OutputType.FolderIndex)
                return _outName;
            return $"{_outName}.{_outExt}";
        }

        internal WipeWiiUStep(IStepContextConstruct context)
        {
            if (context.StepInfo.OutputType == OutputType.FolderIndex)
                _type = OutputType.FolderIndex;
            else
                _type = OutputType.Image;
            base.CheckContract(context.StepInfo);

            _outName = context.SourceImageName?.Rot13Words();
            _outExt = context.StepConfig;
            _sis = new List<Si>();
            _ptns = new List<WipePartition>();
            _gaps = new List<Gap>();
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            //_context.SkipBlockTaskEnable();

            // Detect an already-wiped source: the reader adopts the NKit wipe disc key for a wiped disc.
            // If so, the name/ID obfuscation is already applied and must NOT be applied again.
            _alreadyWiped = _context.SourceFile?.Key != null
                && _context.SourceFile.Key.Length == Nintendo.WiiGc.WiiConsts.NKitWipeDiscKey.Length
                && _context.SourceFile.Key.Equals(0, Nintendo.WiiGc.WiiConsts.NKitWipeDiscKey, 0, Nintendo.WiiGc.WiiConsts.NKitWipeDiscKey.Length);
            if (_alreadyWiped)
                _outName = _context.SourceImageName; // already obfuscated; do NOT re-ROT13

            _h2Set = new h2Set();

            if (_context.SourceFile.ImageType == SourceImageType.TmdApp)
            {
                if (_context.SourceFile.IndexFile.Items.Any(a => a.FileIsMissing))
                    throw new Exception("Cannot convert from App/CDN when files are missing");
            }
            else
                base.OutStream.NewPart(_outName, _outExt, true); //image

        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type == AreaType.RawKeyMissing)
                throw new Exception("Missing Key required to Wipe files");

            if (_context.SourceFile.ImageType == SourceImageType.TmdApp && section.ImageOffset == 0)
            {
                if (section.Type == AreaType.FstBlock) //source is app files
                {
                    _header = new ImageHeader(null);
                    if (_context.SourceFile.IndexFile.WiiUFstMismatch)
                        throw new Exception("Convert from App/CDN Error - TMD items do not match FST items");
                    _security = new WiiUSecurity(_header);

                    IndexFile idx = _context.SourceFile.IndexFile;
                    byte[] iv = new byte[0x10];
                    iv.WriteUInt64B(0, Nintendo.WiiGc.WiiConsts.NKitWipeIV);
                    SiData si = new SiData(Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey, idx, section.Encrypted) { AppIndex = 0, IvTitle = iv };
                    _header.SiData.Add(si);

                    si.Complete(_header);
                    _header.Update(0, si);

                    _fsInfo = new Nanook.NKit.Nintendo.WiiU.FileSystemInfo(0, _context.ImageSize, _header, _header.SiData[0], (int)section.Size);
                    _fsInfo.ProcessBlock(0, section.Decrypted, (int)section.Size, true, _context.SourceFile.IndexFile);

                    _ptn = new PartitionInfo(PartitionType.Game, 0, 0, 0);
                    _ptn.Id = si.TmdInfo.TitleId.ToString("X16");
                    _ptn.IsMainContent = true;

                    _sis.Add(new Si() { TitleId = si.TitleId, SiData = si, SiBuffer = new byte[0x20000] });
                    _ptns.Add(new WipePartition()
                    {
                        ImageOffset = section.ImageOffset,
                        Header = section.Decrypted.Read(0, (int)section.Size),
                        TitleId = si.TitleId,
                        Si = _sis[0],
                        FsInfo = _fsInfo
                    });

                    //write fst as app
                    byte[] key = _fsInfo.Type == PartitionType.Game ? Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey : _header.Key;
                    WiiUSecurity.EncryptFst(section.Encrypted, section.Decrypted, (int)section.Size, key);
                    using (IO.Stream fst = new IO.FileStream(IO.Path.Combine(_context.WritePath, $"{_fsInfo.SiData.Contents[0].ContentId:x8}.app"), IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None))
                        fst.Write(section.Encrypted, 0, (int)section.Size);
                }
            }
            else if (section.Type == AreaType.ImageHeader)
            {
                if (!_alreadyWiped) // ROT13 is its own inverse; skip if source is already wiped
                {
                    string id = section.Decrypted.ReadString(4, 0x20).Rot13Words();
                    section.Decrypted.WriteString(4, id.Length, id);
                }
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size))
                {
                    Key = Nintendo.WiiGc.WiiConsts.NKitWipeDiscKey,
                    KeyCommon = Nintendo.WiiGc.WiiConsts.NKitWipeCommonKey,
                    KeyCommonDev = Nintendo.WiiGc.WiiConsts.NKitWipeCommonKey
                };
                _security = new WiiUSecurity(_header);
            }
            else if (section.Type == AreaType.PartitionTable)
            {
                _header.Update(section.Decrypted.Read(0, (int)section.Size)); //clone
                int idx = 0;
                foreach (PartitionInfo pi in _header.Partitions)
                {
                    _ptns.Add(new WipePartition()
                    {
                        ImageOffset = pi.ImageOffset,
                        Index = idx++,
                        TitleId = pi.WiiUTitleId
                    });
                }

                if (!_alreadyWiped) // Rot3Hex is its own inverse; skip if source is already wiped
                {
                    int volumes = (int)section.Decrypted.ReadUInt32B(WiiUConsts.DiscContentVolumeCountOffset);
                    for (int i = 0; i < volumes; i++)
                    {
                        int off = WiiUConsts.DiscContentVolumesOffset + (i * WiiUConsts.DiscContentVolumeSize) + WiiUConsts.DiscContentPartitionVolumeOffset;
                        string id = section.Decrypted.ReadString(off, 2);
                        // The 2-char type prefix (SI/UP/GM/GI) is intrinsic and kept; the trailing volume
                        // ID is Rot3Hex'd. SI has no trailing ID to obfuscate. UP (Update) DOES carry a
                        // trailing volume ID and must be obfuscated like the GM/GI game volumes (it was
                        // previously skipped, leaving the Update volume name un-rotated).
                        if (id != "SI")
                        {
                            string volumeId = section.Decrypted.ReadStringToNull(off + 2).Rot3Hex();
                            section.Decrypted.WriteString(off + 2, volumeId.Length, volumeId);
                        }
                    }
                }
                if (section.AreaInfo.IsEncrypted) //dev/cat-I si WipePartition is not encrypted
                    WiiUSecurity.EncryptPartitionTable(section.Encrypted, section.Decrypted, (int)section.Size, _header.Key);
            }
            else if (section.Type == AreaType.PartitionHeader)
            {
                _ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset); // && (a.Type == PartitionType.Si || (a.Type == PartitionType.Game && a.Id.StartsWith("GM00050000"))));
                _ptns.FirstOrDefault(a => a.ImageOffset == section.ImageOffset).Header = section.Decrypted.Read(0, (int)section.Size);
                if (_ptn != null && _ptn.ImageOffset == section.ImageOffset) //if not our WipePartition then skip will forward
                    _fsInfo = new Nanook.NKit.Nintendo.WiiU.FileSystemInfo(section.ImageOffset, _context.ImageSize, _header, section.Decrypted);
            }
            else if (section.Type == AreaType.FstBlock)
            {
                _cntHeader = null;
                _fsInfo.ProcessBlock(section.ImageOffset, section.Decrypted, (int)section.Size, true, null);
                if (!_alreadyWiped) // FST filename ROT13 is its own inverse; skip if already wiped
                    processFst(section);
                byte[] key = _fsInfo.Type == PartitionType.Game ? Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey : _header.Key;
                if (section.AreaInfo.IsEncrypted) //dev/cat-I si WipePartition is not encrypted
                    WiiUSecurity.EncryptFst(section.Encrypted, section.Decrypted, (int)section.Size, key);

                if (_fsInfo.SiData != null)
                {
                    WipePartition pt = _ptns.FirstOrDefault(a => a.TitleId == _fsInfo.SiData.TitleId);
                    pt.Si = _sis.FirstOrDefault(a => a.TitleId == pt.TitleId);
                    pt.FsInfo = _fsInfo;
                }
            }
            else if (section.Type == AreaType.Other)
            {
                if (section.AreaOffset == 0)
                    _gaps[_gaps.Count - 1].DstImageOffset = section.ImageOffset;
                _gaps[_gaps.Count - 1].DstSize += (long)section.Size;
            }
            else if (_ptn != null && section.Type == AreaType.FileSystem)
            {
                if (section.AreaOffset == 0)
                {
                    if (_gaps.Count == 0 || _gaps[_gaps.Count - 1].DstImageOffset != 0)
                        _gaps.Add(new Gap() { SrcImageOffset = section.ImageOffset });
                    else if (_gaps[_gaps.Count - 1].DstImageOffset == 0)
                        _gaps[_gaps.Count - 1] = new Gap() { SrcImageOffset = section.ImageOffset };

                    _cntHeader = _fsInfo.FstBlock.GetContentHeader(section.ImageOffset);
                    if (_cntHeader != null && _cntHeader.HasHashes && _h2Set.H2Items[0] == null) //one time setup
                    {
                        _h2Set.FullH2Size = _cntHeader.BlockSize * WiiUConsts.H0Count * WiiUConsts.H1Count;
                        _h2Set.FullSize = _h2Set.FullH2Size * WiiUConsts.H2Count;
                        _enc = new byte[_h2Set.FullH2Size];
                        for (int i = 0; i < _h2Set.H2Items.Length; i++)
                            _h2Set.H2Items[i] = new sectionCache() { Data = new byte[_h2Set.FullH2Size] };
                    }
                }
                _gaps[_gaps.Count - 1].SrcSize += (long)section.Size;

                if (_ptn.Type == PartitionType.Si)
                    processSiData(section);

                // Non-Game HASHLESS partitions (Update/GameUpdate) are read back per-sector with the
                // disc key. The per-file zero loop below only clears NAMED file extents in
                // section.Decrypted, leaving the inter-file GAPS as real content — and for hashless
                // content section.Decrypted is never re-encrypted, so section.Encrypted (original source
                // ciphertext) is emitted verbatim and the gaps scan as "Other" not "Nulls".
                // For these partitions wipe the WHOLE decrypted region (gaps included), preserving only
                // the intrinsic title.tik/cert/tmd, then re-encrypt with the wipe disc key (below).
                bool nonGameHashless = section.IsEncrypted && _ptn.Type != PartitionType.Si
                    && _ptn.Type != PartitionType.Game && !(_cntHeader?.HasHashes ?? false);

                if (nonGameHashless)
                {
                    // Update/GameUpdate title.tik/cert/tmd are NOT intrinsic (only the SI partition's
                    // title.* are) — wipe the WHOLE decrypted region including them and the inter-file
                    // gaps, then re-encrypt (below) so the section AND the RepeatedApp Other regions that
                    // mirror it read back as Nulls.
                    Array.Clear(section.Decrypted, 0, (int)section.Size);
                }
                else
                {
                    foreach (SectionItem si in section.Items)
                    {
                        if (si.FsFile == null || si.File == null)
                            continue;

                        if (si.FsFile.IsSystemFile)
                        {
                        }
                        else if (si.FsFile.Name != "title.tik" && si.FsFile.Name != "title.cert" && si.FsFile.Name != "title.tmd")
                            section.Write((int)si.File.FsOffset, ByteStream.Zeros, (int)si.File.FsSize);
                    }
                }
            }

            bool hasSecurity = section.IsEncrypted && _cntHeader != null && _cntHeader.HasHashes && section.Type != AreaType.Other;

            // Re-encrypt zeroed Update/GameUpdate hashless content with the wipe disc key so read-back
            // yields Nulls — the whole FileSystem region (wiped above) AND its RepeatedApp Other regions
            // are wiped in place, and the gap-copy for those gaps is suppressed (WipedInPlace).
            // SI partition is NOT handled here: its whole FileSystem area (title.* + bulk) is rewritten
            // from SiBuffer in ProcessResults (bulk zeroed there, title.* re-signed), and its RepeatedApp
            // Other regions are then replicated from that now-null SI source by the gap-copy.
            // Game (hashed) partitions go through the hasSecurity/h2 path instead.
            // Re-encrypt the zeroed Update/GameUpdate FileSystem region with the wipe disc key so it
            // reads back as Nulls. The RepeatedApp "Other" area that follows is NOT wiped here — it is a
            // huge region only partially emitted as sections; instead the ProcessResults gap-copy
            // replicates this now-null FileSystem source across the whole Other area. Because the source
            // is exactly one content (SizePaddedToBlock) and the reader resets the RepeatedApp IV every
            // content, the copied null-ciphertext realigns and decrypts to Nulls.
            PartitionType ptnType = _ptn?.Type ?? PartitionType.Other;
            bool reencryptHashless = section.IsEncrypted && !(_cntHeader?.HasHashes ?? false) && _security != null
                && ptnType != PartitionType.Game && ptnType != PartitionType.Si
                && section.Type == AreaType.FileSystem;
            if (reencryptHashless)
            {
                PartitionType ptype = _fsInfo?.Type ?? _header.GetNextPartition(section.ImageOffset).Type;
                _security.Populate(_cntHeader, _fsInfo?.SiData, ptype, section.Type, section.Encrypted, section.Decrypted, (int)section.Size, false, false, section.AreaOffset);
                _security.Encrypt();
            }

            bool h2Update = false;
            WipePartition ptn = null;

            if (hasSecurity && _security != null)
            {
                ptn = _ptns.First(a => a.ImageOffset == _ptn.ImageOffset);
                int h2Index = (int)(section.AreaOffset % _h2Set.FullSize / _h2Set.FullH2Size);
                PartitionType ptype = _fsInfo?.Type ?? _header.GetNextPartition(section.ImageOffset).Type;

                SiData key = new SiData() { KeyTitle = Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey };
                //recalculated H0 / H1
                _security.Populate(_cntHeader, key, ptype, section.Type, section.Encrypted, section.Decrypted, (int)section.Size, false, true, section.AreaOffset);

                _h2Set.H2Items[h2Index].ImageOffset = section.ImageOffset;
                _h2Set.H2Items[h2Index].Size = section.Size;
                Array.Copy(section.Decrypted, _h2Set.H2Items[h2Index].Data, (int)section.Size);
                _h2Set.Count = h2Index + 1;
                h2Update = true;
            }

            if (_type == OutputType.FolderIndex && _cntHeader != null)
            {
                if (section.AreaOffset == 0)
                {
                    if (_cntHeader.HasHashes && _cntHeader.H3HashCount == 0) //create hashes if no h3 files (index mode)
                    {
                        _cntHeader.H3Offset = 0x40 + (ptn.ContentH3.Count * 0x14);
                        _cntHeader.H3HashCount = (int)(_cntHeader.Size / _h2Set.FullSize) + ((_cntHeader.Size % _h2Set.FullSize) == 0 ? 0 : 1);
                        _cntHeader.H3Hashes = new byte[0x14 * _cntHeader.H3HashCount];
                    }
                    base.OutStream.NewPart($"{_cntHeader.Content.ContentId:x8}", "app", false);
                }
            }

            if (h2Update)
            {
                bool isLastPart = section.AreaOffset + section.Size == _cntHeader.SizePaddedToBlock;
                if (isLastPart || _h2Set.Count == WiiUConsts.H2Count)
                    updateH2Entries(ptn, section.Type, isLastPart);
            }
            else
            {
                base.OutStream.Write(section.Encrypted, 0, (int)section.Size); //decrypted if no encryption
            }
        }



        private void updateH2Entries(WipePartition ptn, AreaType type, bool isLastPart)
        {
            //write out all data with a complete H2 hash set
            using (SHA1 sha = SHA1.Create())
            {
                for (int i = 0; i < _h2Set.Count; i++) // get a full h2 hash array
                {
                    sectionCache sc = _h2Set.H2Items[i];
                    Array.Copy(sha.ComputeHash(sc.Data, WiiUConsts.H1Offset, WiiUConsts.H1Len), 0, _h2Set.H2Items[0].Data, WiiUConsts.H2Offset + (i * 0x14), 0x14); //get each section's h2
                }
            }

            for (int i = 0; i < _h2Set.Count; i++) // get a full h2 hash array
            {
                sectionCache sc = _h2Set.H2Items[i];
                for (int h = 0; h < sc.Data.Length; h += _cntHeader.BlockSize)
                    Array.Copy(_h2Set.H2Items[0].Data, WiiUConsts.H2Offset, sc.Data, h + WiiUConsts.H2Offset, WiiUConsts.H2Len); //copy h2 to all blocks
            }

            //update the h3 entries in the WipePartition header for this h2 set
            if (_cntHeader.H3HashCount != 0)
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    int h3Index = (int)((_h2Set.H2Items[0].ImageOffset - _cntHeader.ImageOffset) / _h2Set.FullSize);
                    byte[] sha = sha1.ComputeHash(_h2Set.H2Items[0].Data, WiiUConsts.H2Offset, WiiUConsts.H2Len);
                    ptn.Header.Write(0x40 + _cntHeader.H3Offset + (h3Index * 0x14), sha); //replace h3 item
                    if (isLastPart)
                    {
                        byte[] h3set = ptn.Header.Read(0x40 + _cntHeader.H3Offset, _cntHeader.H3HashCount * 0x14);
                        ptn.ContentH3.Add(_cntHeader.ImageOffset, sha1.ComputeHash(h3set));
                        if (_type == OutputType.FolderIndex)
                            base.OutStream.WriteAdditionalFile(h3set, 0, h3set.Length, $"{_cntHeader.Content.ContentId:x8}.h3", false, false);
                    }
                }
            }

            PartitionType ptype = _fsInfo?.Type ?? _header.GetNextPartition(_h2Set.H2Items[0].ImageOffset).Type;
            for (int i = 0; i < _h2Set.Count; i++)
            {
                sectionCache sc = _h2Set.H2Items[i];
                byte[] iv = new byte[0x10];
                iv.WriteUInt64B(0, Nintendo.WiiGc.WiiConsts.NKitWipeIV);
                SiData si2 = new SiData() { Key = Nintendo.WiiGc.WiiConsts.NKitWipeCommonKey, KeyTitle = Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey, IvTitle = iv };
                _security.Populate(_cntHeader, si2, ptype, type, _enc, sc.Data, (int)sc.Size, false, false, sc.ImageOffset - _cntHeader.ImageOffset);
                bool isValid = _security.IsValid(false, out _);
                _security.Encrypt();

                base.OutStream.Write(_enc, 0, (int)sc.Size); //decrypted if no encryption
            }
        }

        private void processSiData(ISection section)
        {
            //hack in to IBuffer to use the same code Image uses to read SI data
            IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
            buff.ReInitialise(section.AreaInfo, false);
            buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
            Image.SetSiInfoInImageHeader(buff, _cntHeader, _header, _fsInfo, true);

            if (section.AreaOffset == 0)
            {
                SiData si = _header.SiData.FirstOrDefault(a => a.AppIndex == _cntHeader.Index);
                _sis.Add(new Si()
                {
                    SiData = si,
                    TitleId = si.TitleId,
                    SiFsInfo = _fsInfo,
                    SiBuffer = section.Decrypted.Read(0, (int)section.Size),
                    SiImageOffset = section.ImageOffset
                });
            }
        }

        private void processFst(ISection section)
        {
            byte[] buff = section.Decrypted;
            int off = _fsInfo.FstBlock.FstFileOffset;
            int sz = _fsInfo.FstBlock.FstFileSize - off;
            Encoding enc = Encoding.GetEncoding("Shift-JIS");

            // Structural SI FST entries that must NOT be obfuscated: the intrinsic title files and the
            // content-index directories. The content-index dir names map to the content/app indices, so
            // derive them from the FST rather than hardcoding a fixed pair (a hardcoded "02"/"03" wiped
            // one index but not the other when a disc's SI used different indices, e.g. 03/04 -> 03 kept
            // but 04 rotated to 59).
            HashSet<string> keepNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "title.tik", "title.cert", "title.tmd"
            };
            if (_fsInfo?.FstBlock?.ContentHeaders != null)
            {
                foreach (ContentHeader ch in _fsInfo.FstBlock.ContentHeaders)
                {
                    keepNames.Add(ch.Index.ToString("D2"));
                    keepNames.Add(ch.Index.ToString("x2"));
                    keepNames.Add(ch.Index.ToString("X2"));
                }
            }

            for (int i = ((int)buff.ReadUInt32B(off + 0x8) * 0x10) + off + 1; i + 1 < sz && buff[i] != 0;)
            {
                string fn = buff.ReadStringToNull(enc, i, buff.Length - i);
                if (!keepNames.Contains(fn))
                {
                    fn = fn.Rot13Words();
                    buff.WriteString(i, fn.Length, fn, enc);
                }
                while (buff[++i] != 0) ;
                i++;
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            foreach (WipePartition p in _ptns)
            {
                if (p.Si?.SiData != null)
                {
                    if (_type == OutputType.Image)
                    {
                        base.OutStream.Seek(p.ImageOffset, IO.SeekOrigin.Begin);
                        base.OutStream.Write(p.Header, 0, p.Header.Length);
                        p.Si.SiData.Key = Nintendo.WiiGc.WiiConsts.NKitWipeDiscKey;
                    }

                    p.Si.SiData.KeyTitle = Nintendo.WiiGc.WiiConsts.NKitWipeTitleKey;
                    p.Si.SiData.IvTitle = new byte[0x10];

                    if (p.ContentH3.Count != 0)
                    {
                        //rehash the tmd tables
                        SiData si = p.Si.SiData;
                        foreach (KeyValuePair<long, byte[]> h3 in p.ContentH3)
                        {
                            ContentHeader cnt = p.FsInfo.FstBlock.GetContentHeader(h3.Key);
                            si.FileTmd.Write(si.TmdInfo.TmdContentOffset + (si.TmdInfo.TmdContentItemLength * cnt.Index) + 0x10, h3.Value, 0x14);
                        }
                        using (SHA256 sha = SHA256.Create())
                        {
                            foreach (ContentGroup cg in si.TmdInfo.ContentGroups)
                                si.FileTmd.Write(cg.GroupOffset + 0x4, sha.ComputeHash(si.FileTmd, cg.ContentOffset, cg.ContentSize), 0x20);
                            si.FileTmd.Write(0x1e4, sha.ComputeHash(si.FileTmd, si.TmdInfo.TmdHeaderSize, si.TmdInfo.TmdContentOffset - si.TmdInfo.TmdHeaderSize), 0x20);
                        }
                        //if (p.Si.SiData.TitleId != 0) //not update
                        //{
                        //    byte[] titleId = si.FileTmd.Read(0x18c, 0x10);
                        //    si.FileTmd.Write(0x18c, titleId.ToHexString().Rot3Hex().HexToBytes());
                        //}
                    }
                    resign(p, p.Si.SiData.KeyTitle);

                    if (_type == OutputType.Image)
                    {
                        _security.Populate(null, p.Si.SiData, PartitionType.Si, AreaType.FileSystem, p.Si.SiBuffer, p.Si.SiBuffer, p.Si.SiBuffer.Length, false, false, 0);

                        if (p.Si.SiData.TmdInfo.IsRetail)
                            _security.Encrypt();

                        base.OutStream.Seek(p.Si.SiImageOffset, IO.SeekOrigin.Begin);
                        base.OutStream.Write(p.Si.SiBuffer, 0, p.Si.SiBuffer.Length);
                    }
                }
            }

            base.ProcessResults();

            // ChecksummedFiles[0].FileName is a bare filename; resolve it against the step's
            // WritePath (temp dir) so the gap-copy pass opens the actual temp image, not a
            // CWD-relative path.
            string outImagePath = IO.Path.Combine(base.Context.WritePath, base.OutStream.ChecksummedFiles[0].FileName);
            using (IO.FileStream rd = IO.File.Open(outImagePath, IO.FileMode.Open, IO.FileAccess.ReadWrite, IO.FileShare.ReadWrite))
            {
                using (IO.FileStream wt = IO.File.Open(outImagePath, IO.FileMode.Open, IO.FileAccess.ReadWrite, IO.FileShare.ReadWrite))
                {
                    foreach (Gap g in _gaps.Where(a => a.DstImageOffset != 0))
                    {
                        long sz = 0;
                        wt.Position = g.DstImageOffset;
                        while (sz != g.DstSize)
                        {
                            rd.Position = g.SrcImageOffset;
                            long c = Math.Min(g.DstSize - sz, g.SrcSize);
                            rd.Copy(wt, c);
                            sz += c;
                        }
                    }
                }
            }
        }

        private void resign(WipePartition p, byte[] key)
        {
            SiData si = p.Si.SiData;
            //resign the cert chain
            CertValidator cv = si.CreateCertValidator();

            if (_type == OutputType.Image)
            {
                //write the files back to the SI WipePartition and encrypt it
                IFsFile tikFile = p.Si.SiFsInfo.FileSystem.Files.FirstOrDefault(a => a.Name == "title.tik");
                IFsFile tmdFile = p.Si.SiFsInfo.FileSystem.Files.FirstOrDefault(a => a.Name == "title.tmd");
                IFsFile certFile = p.Si.SiFsInfo.FileSystem.Files.FirstOrDefault(a => a.Name == "title.cert");

                cv.Ticket.Offset = (int)tikFile.FsOffset;
                cv.Tmd.Offset = (int)tmdFile.FsOffset;
                foreach (SignedData sd in cv.Items)
                    sd.Offset += (int)certFile.FsOffset;
            }
            else //folderindex - make fake si CiBuffer to reuse certchain wipe
            {
                cv.Ticket.Offset = 0;
                cv.Tmd.Offset = 0x8000;
                foreach (SignedData sd in cv.Items)
                    sd.Offset += 0x10000;
            }

            //assemble CiBuffer for certvalidator
            Array.Copy(si.FileTicket, 0, p.Si.SiBuffer, cv.Ticket.Offset, si.FileTicket.Length);
            Array.Copy(si.FileTmd, 0, p.Si.SiBuffer, cv.Tmd.Offset, si.FileTmd.Length);
            Array.Copy(si.FileCert, 0, p.Si.SiBuffer, cv.Items[0].Offset, si.FileCert.Length);

            cv.WipeHeaderChain(p.Si.SiBuffer, key, false); //switch to nkit key

            if (_type == OutputType.FolderIndex)
            {
                base.OutStream.WriteAdditionalFile(p.Si.SiBuffer, cv.Ticket.Offset, si.FileTicket.Length, "title.tik", false, false);
                base.OutStream.WriteAdditionalFile(p.Si.SiBuffer, cv.Tmd.Offset, si.FileTmd.Length, "title.tmd", false, false);
                base.OutStream.WriteAdditionalFile(p.Si.SiBuffer, cv.Items[0].Offset, si.FileCert.Length, "title.cert", false, false);
            }
        }
    }
}