using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    internal class FixExtractWiiGcStep : StepBase, IStep
    {
        private IStepContext _context;
        //private Regex _regex;
        //private bool _isDisposed;
        private MemoryStream _tempStream;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Files;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepFixExtractWiiGc;

        public override string ProposedName() => null; //leave null for OutputType.Files with multiple files

        internal FixExtractWiiGcStep(IStepContextConstruct context)
        {
            _writing = false;
            base.CheckContract(context.StepInfo);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            //_regex = new Regex(".*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _context.SkipBlockTaskEnable();
        }

        private byte[] _gcAppLoader;
        private byte[] _gcFst;
        private int _gcFstOffset;
        private int _gcFixFstOffset;
        private string _gcId8;
        private uint _sysCrc;

        private void gcExtract(ISection section)
        {
            if (section.ImageOffset == 0)
            {
                ImageHeader hdr = new ImageHeader(section.Decrypted, false);
                if (hdr.IsDatel)
                    throw new HandledException("Datel image detected, FixExtract aborted");


                _gcId8 = string.Concat(hdr.Id6, section.Decrypted.Read(WiiConsts.DataHdrDiscNoOffset, 1).ToHexString(), section.Decrypted.Read(WiiConsts.DataHdrRevisionOffset, 1).ToHexString());
                _sysCrc = 0;
                int size = (int)(section.Items.FirstOrDefault(a => a.FsFile?.Name == "__apploader.img")?.FsFile.FsSize ?? 0);
                _gcAppLoader = section.Decrypted.Read(WiiConsts.AppLoaderOffset, size);
                _gcFixFstOffset = _gcFstOffset = 0x10 + WiiConsts.DataHdrTitleSize;
                _gcFst = new byte[(int)(_gcFixFstOffset + (int)section.Decrypted.ReadUInt32B(WiiConsts.FstSizeOffset))];

                _gcFst.WriteUInt32B(0x00, section.Decrypted.ReadUInt32B(WiiConsts.DolPtrOffset)); //dol
                _gcFst.WriteUInt32B(0x04, section.Decrypted.ReadUInt32B(WiiConsts.FstPtrOffset)); //fstAddr
                _gcFst.WriteUInt32B(0x08, section.Decrypted.ReadUInt32B(WiiConsts.FstSizeMaxOffset)); //maxfst
                _gcFst.WriteUInt32B(0x0c, section.Decrypted.ReadUInt32B(WiiConsts.Bi2BinOffset + 0x18)); //region
                _gcFst.Write(0x10, section.Decrypted.Read(WiiConsts.DataHdrTitleOffset, WiiConsts.DataHdrTitleSize)); //title
            }

            if (_gcFstOffset < _gcFst.Length)
            {
                ISectionItem fstItem = section.Items.FirstOrDefault(a => a.File != null && a.FsFile?.Name == "__fst.bin");
                if (fstItem != null)
                {
                    Array.Copy(section.Decrypted, fstItem.File.FsOffset, _gcFst, _gcFstOffset, fstItem.File.FsSize);
                    _gcFstOffset += (int)fstItem.File.FsSize;

                    if (_gcFstOffset >= _gcFst.Length)
                    {
                        _sysCrc = ~Crc.Combine(~_sysCrc, ~Crc.Compute(section.Decrypted, 0, (int)(fstItem.File.FsOffset + fstItem.File.FsSize)), (int)(fstItem.File.FsOffset + fstItem.File.FsSize));

                        uint appLdrCrc = Crc.Compute(_gcAppLoader);

                        string fn = string.Format("fst[{0}][{1}][{2}][{3}]", SourceFiles.CleanseFileName(_gcId8), appLdrCrc.ToString("X8"), Crc.Compute(_gcFst, _gcFixFstOffset, _gcFst.Length - _gcFixFstOffset).ToString("X8"), _sysCrc.ToString("X8"));
                        base.OutStream.NewPart(fn, "bin", false, true);
                        base.OutStream.Write(_gcFst, 0, _gcFst.Length);

                        fn = string.Format("appldr[{0}][{1}]", _gcAppLoader.ReadString(0, 10).Replace("/", ""), appLdrCrc.ToString("X8"));
                        base.OutStream.NewPart(fn, "bin", false, true);
                        base.OutStream.Write(_gcAppLoader, 0, _gcAppLoader.Length);

                        _context.SkipToImageOffsetSet(long.MaxValue); //cause image reader to stop
                    }
                }

                if (_gcFstOffset < _gcFst.Length)
                    _sysCrc = ~Crc.Combine(~_sysCrc, ~section.Crc, (int)section.Size); //combine full crc as the fst ends after this block
            }
        }

        private ImageHeader _header;
        private string _contentSha1;
        private string _tempName;
        private bool _writing;
        private uint _tempCrc;
        private PartitionType _currentPartitionType;
        private bool _currentPartitionStoreOther;
        private long _skippedPadding;
        private int _channelNo;
        private string _prtId8;
        private bool _isKorean;


        private void wiiExtractFinalise()
        {
            try
            {
                if (_writing)
                {
                    //_tempStream.Close();
                    string tmp = _tempName;
                    if (_currentPartitionType == PartitionType.Update)
                        base.OutStream.NewPart($"{_contentSha1}_{(_isKorean ? "K" : "N")}_{_tempCrc:X8}", "", false, true);
                    else
                        base.OutStream.NewPart($"{_header.Id8}_{++_channelNo:D2}_{_prtId8}_{(_isKorean ? "K" : "N")}_{_tempCrc:X8}", "", false, true);
                    _tempStream.Position = 0;
                    _tempStream.CopyTo(base.OutStream);
                    _tempStream.Close();
                    _writing = false;
                }
            }
            finally
            {
                _contentSha1 = null;
                _tempName = null;
                _tempCrc = 0;
                _skippedPadding = 0;
                _currentPartitionType = (PartitionType)int.MaxValue;
            }
        }


        private void wiiExtract(ISection section)
        {
            if (section.Type == AreaType.ImageHeader)
            {
                _header = new ImageHeader((byte[])section.Decrypted.Clone(), true);

                if (_header.IsDatel)
                    throw new HandledException("Datel image detected, FixExtract aborted");

                wiiExtractFinalise(); //use to initialisecurr
            }

            if (section.Type == AreaType.PartitionHeader)
            {
                wiiExtractFinalise();
                _currentPartitionType = _header.GetPartition(section.ImageOffset).Type;
                PartitionInfo pi = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset && (a.Type == PartitionType.Update || a.Type == PartitionType.Channel || !Enum.IsDefined(typeof(PartitionType), a.Type)));
                _currentPartitionStoreOther = _currentPartitionType == PartitionType.Update;
                //if (!_currentPartitionStoreOther && !Enum.IsDefined(typeof(PartitionType), _currentPartitionType)) //is a channel
                //    _currentPartitionStoreOther = section.ImageOffset != Header.Partitions.LastOrDefault()?.ImageOffset; //save gaps when not last VC 
                if (pi == null)
                {
                    _context.SkipToImageOffsetSet(long.MaxValue); //end
                    return;
                }
                else if (pi.ImageOffset > section.ImageOffset)
                {
                    _context.SkipToImageOffsetSet(pi.ImageOffset);
                    return;
                }
            }

            if (_currentPartitionType == PartitionType.Update || _currentPartitionType == PartitionType.Channel || !Enum.IsDefined(typeof(PartitionType), _currentPartitionType)) //VC
            {
                if (section.Type == AreaType.PartitionHeader)
                {
                    if (section.AreaOffset == 0)
                    {
                        int tmdOffset = (int)section.Decrypted.ReadUInt32B(WiiConsts.WiiPrtHdrTmdPtrOffset) * 4;
                        _isKorean = section.Decrypted.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1 && Nintendo.WiiGc.FileSystemInfo.GetIssuer(section.Decrypted) != WiiConsts.RvtIssuer;
                        _contentSha1 = section.Decrypted.Read(tmdOffset + 0x1e4 + 0x10, 20).ToHexString();
                        _tempStream = new MemoryStream();
                        _writing = true;
                    }
                    _tempStream.Write(section.Encrypted, 0, (int)section.Size);
                }
                else if (section.Type == AreaType.FileSystem)
                {
                    _tempStream.Write(section.Encrypted, 0, (int)section.Size);
                    if (section.AreaOffset == 0)
                        _prtId8 = SourceFiles.CleanseFileName(section.Decrypted.ReadString(WiiConsts.WiiSectorHashSize, 4));
                }
                else if (section.Type == AreaType.Other) //filler
                {
                    if (_currentPartitionStoreOther)
                    {
                        int p = 0;
                        int s;
                        while (p < section.Size)
                        {
                            s = Math.Min(WiiConsts.WiiSectorSize, (int)section.Size - p);
                            //don't write nulls for update WipePartition, if data is found flush out the skipped nulls
                            if (_currentPartitionType == PartitionType.Update && section.Encrypted.Equals(p, s, 0))
                                _skippedPadding += s;
                            else
                            {
                                if (_skippedPadding != 0)
                                {
                                    ByteStream.Zeros.Copy(_tempStream, _skippedPadding);
                                    _skippedPadding = 0;
                                }
                                _tempStream.Write(section.Encrypted, p, s);
                            }
                            p += s;
                        }
                    }
                }
                _tempCrc = ~Crc.Combine(~_tempCrc, ~section.Crc, (int)section.Size);
            }
            else if (section.Type == AreaType.PartitionHeader) //skip past
            {
                PartitionInfo[] more = _header.Partitions.Where(a => a.ImageOffset > section.ImageOffset && (a.Type == PartitionType.Update || a.Type == PartitionType.Channel || !Enum.IsDefined(typeof(PartitionType), a.Type))).OrderBy(a => a.ImageOffset).ToArray();
                if (more.Length != 0)
                    _context.SkipToImageOffsetSet(more[0].ImageOffset);
                else //no more
                    _context.SkipToImageOffsetSet(long.MaxValue);
            }
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (_context.SystemType == SystemType.GameCube)
                gcExtract(section);
            else if (_context.SystemType == SystemType.Wii)
                wiiExtract(section);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            wiiExtractFinalise();

            base.ProcessResults();
        }

        public void Patched(ISection section)
        {
        }
    }
}