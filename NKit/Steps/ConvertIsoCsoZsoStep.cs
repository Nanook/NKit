using Nanook.GrindCore;
using Nanook.GrindCore.DeflateZLib;
using Nanook.GrindCore.Lz4;
using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using System;
using System.Linq;

namespace Nanook.NKit
{

    internal class ConvertIsoCsoZsoStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;
        //private bool _isDisposed;
        private string[] _fmt;

        private const int _hdrSize = 0x18;
        private int _blkSz;
        private DeflateBlock _deflate;
        private Lz4Block _lz4;
        private CompressionVersion _deflateVersion;

        private ContainerType _containerType;
        private int _version;
        private CompressionType _levelDeflate;
        private CompressionType _levelLz4;
        private long _size;
        private long _originalSize;
        private int _parallelism;
        private byte[] _hdr;
        private bool _useDecrypted;
        private int _shift;
        private int _align;
        private bool _isCso2;
        private bool _isZso;
        private CircularSequenceQueue<CiBuffer> _threads;
        private NKitHeader _nkitHdr;
        private int _nkitHdrPos;
        private byte[] _buff;
        private int _buffSz;

        private enum compType { Lz4 = 0, Cso, CsoFiltered, CsoHuffman, CsoRle }

        private class CiBuffer
        {
            public CiBuffer(int maxSize, bool doCso, bool doLz4, bool bruteForceCso)
            {
                MaxSize = maxSize;
                Buffer = new byte[maxSize + Math.Max(0x400, maxSize << 1)];
                CompBuff = new byte[][]
                {
                    doLz4 ? new byte[Buffer.Length] : null,
                    doCso ? new byte[Buffer.Length] : null,
                    doCso && bruteForceCso ? new byte[Buffer.Length] : null,
                    doCso && bruteForceCso ? new byte[Buffer.Length] : null,
                    doCso && bruteForceCso ? new byte[Buffer.Length] : null
                };
                CompSize = new int[CompBuff.Length];
            }
            public int MaxSize;
            public int BlockIndex;
            public int Offset;
            public byte[] Buffer;
            public byte[][] CompBuff;
            public int[] CompSize;
            public compType BestIdx;
        }

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => true;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertIsoCsoZso;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertIsoCsoZsoStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Use configuration service to validate the entire format string first
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateFormatString(context.SystemType, context.StepConfig);
            if (!validationResult.IsValid)
                throw new HandledException($"Convert - {validationResult.ErrorMessage}");

            // Parse format configuration using unified configuration parser - no manual parsing needed
            object formatConfig = ConfigSettingsFormatParser.ParseFormatConfiguration(context.StepConfig, context.SystemType);

            if (formatConfig is not CsoFormatConfiguration csoConfig)
                throw new HandledException($"Convert format configuration type '{formatConfig.GetType().Name}' is not supported by this step. Expected CSO/ZSO format.");

            // Set basic properties using parsed configuration and helper properties
            _containerType = csoConfig.IsZso ? ContainerType.Zso : ContainerType.Cso;
            _outName = context.SourceImageName;
            _outExt = _containerType.ToString().ToLower();
            _version = csoConfig.Version;
            _isZso = csoConfig.IsZso;
            _isCso2 = csoConfig.IsCso2;

            // Apply parsed configuration - no more manual parsing needed
            _levelDeflate = (CompressionType)csoConfig.DeflateLevel;
            _levelLz4 = (CompressionType)csoConfig.Lz4Level;
            _blkSz = csoConfig.BlockSizeBytes;
            _parallelism = csoConfig.Parallelism;

            // Build the format array for compatibility with existing code
            _fmt = new[] {
                csoConfig.ContainerTypeString,
                csoConfig.DeflateLevel.ToString(),
                ConfigSettingsFormatParser.ParseBlockSizeToString(csoConfig.BlockSizeBytes),
                csoConfig.Parallelism.ToString()
            };

            // Initialize compression blocks - only create what's needed for the container type
            _deflateVersion = CompressionVersion.ZLib(ZLibVersion.v1_3_1); //pin to this version
            if (_containerType == ContainerType.Cso || _isCso2)
                _deflate = new DeflateBlock(new CompressionOptions() { Type = _levelDeflate, Version = _deflateVersion, BlockSize = _blkSz });
            if (_isZso || _isCso2)
                _lz4 = new Lz4Block(new CompressionOptions() { Type = _levelLz4, BlockSize = _blkSz });

            // Log configuration warnings from centralized parser
            foreach (string warning in csoConfig.Warnings)
            {
                context.Log?.Info(() => $"Warning: {warning}");
            }

            context.AddSettingsInfo("ConvertTo", $"{_outExt} (Level:{(_isZso ? csoConfig.Lz4Level : csoConfig.DeflateLevel)}, BlockSize:{ConfigSettingsFormatParser.ParseBlockSizeToString(csoConfig.BlockSizeBytes)}, Parallelism:{csoConfig.Parallelism})");
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            SourceImageType sourceType = _context.SourceFile.ImageType;


            if (_context.SystemType == SystemType.WiiU && sourceType == SourceImageType.TmdApp)
            {
                if (_context.SourceFile.IndexFile.Items.Any(a => a.FileIsMissing))
                    throw new Exception("Cannot convert from App/CDN when files are missing");
            }

            _size = _context.ImageSize;

            bool isEncrypted = this.Context.SystemType == SystemType.PS3;
            _useDecrypted = isEncrypted && this.Context.Key != null;
            _nkitHdr = new NKitHeader(2, false, true, true, true, true, this.Context.Key == null ? HeaderKeyType.None : HeaderKeyType.Aes, this.Context.Key?.Length ?? 0, isEncrypted, false, false);
            _nkitHdr.Key = this.Context.Key;
            _nkitHdr.Size = _size;
            _buffSz = 0;
            _buff = new byte[_blkSz];

            int blocks = (int)(_size / _blkSz) + (_size % _blkSz != 0 ? 1 : 0); //support the last block not aligning to the block size
            int hdrSize = _hdrSize + ((blocks + 1) << 2); //add one for the end file header item

            //add 4 bytes gap to the header
            hdrSize += 0x4; //4 nulls then nkit header
            _nkitHdrPos = hdrSize;

            _originalSize = _size;
            _shift = 0;
            _align = 1;
            while (_size > 0x7fffffffL)
            {
                _shift++;
                _align *= 2;
                _size >>= 1;
            }

            hdrSize += _nkitHdr.Length;
            int rmn = hdrSize % _align;
            if (rmn != 0)
                hdrSize += _align - rmn;
            _hdr = new byte[hdrSize];
            CiBuffer[] buffs = new CiBuffer[_parallelism + 2]; //plus 2, 1 for filling, 1 for writing
            for (int i = 0; i < buffs.Length; i++)
                buffs[i] = new CiBuffer(_blkSz, _containerType == ContainerType.Cso, _isZso || _isCso2, false);
            _threads = new CircularSequenceQueue<CiBuffer>(buffs, b => blockCompress(b), b => blockWrite(b));


            _hdr.WriteString(0, 4, _containerType == ContainerType.Cso ? "CISO" : "ZISO");
            _hdr.WriteUInt32L(0x04, _hdrSize);
            _hdr.WriteUInt64L(0x08, (ulong)_originalSize);
            _hdr.WriteUInt32L(0x10, (uint)_blkSz);
            _hdr.Write8(0x14, (byte)_version);
            _hdr.Write8(0x15, (byte)_shift);
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.AreaInfo.IsEncrypted && this.Context.Key == null)
                throw new Exception("Encrypted area detected with no Key");

            if (section.ImageOffset == 0)
            {
                base.OutStream.NewPart(_outName, _outExt, true);
                base.OutStream.Write(_hdr, 0, _hdr.Length);
                base.OutStream.CrcSplit();
            }

            byte[] buff = _useDecrypted ? section.Decrypted : section.Encrypted;

            int idx = (int)(section.ImageOffset / _blkSz);
            int sz = (int)section.Size + _buffSz;

            int rmn = sz % _blkSz;

            for (int i = 0; i < sz - rmn; i += _blkSz)
            {
                blockInit(_threads.FillItem, buff, i, idx++);
                _threads.ItemComplete();
            }
            if (rmn != 0)
            {
                if (_buffSz != 0 && rmn - _buffSz == section.Size)
                    Array.Copy(buff, 0, _buff, _buffSz, section.Size);
                else
                    Array.Copy(buff, sz - rmn - _buffSz, _buff, 0, rmn);
                _buffSz = rmn;
            }
            else
                _buffSz = 0;

            if (_buffSz != 0 && section.ImageOffset + section.Size == _originalSize) //is this the last block and isn't complete?
            {
                Array.Clear(_buff, _buffSz, _blkSz - _buffSz); //clear the end of the CiBuffer
                blockInit(_threads.FillItem, _buff, 0, idx++);
                _threads.ItemComplete();
            }
        }

        private void blockInit(CiBuffer b, byte[] buffer, int offset, int blockIndex)
        {
            b.BestIdx = _isZso ? compType.Lz4 : compType.Cso;
            for (int i = 0; i < b.CompSize.Length; i++)
                b.CompSize[i] = 0;
            b.Offset = offset;
            int writeOff = 0;
            int sz;
            if (offset < _buffSz) //within the bounds of buffered data
            {
                Array.Copy(_buff, offset, b.Buffer, 0, _buffSz);
                offset += _buffSz;
                sz = _blkSz - _buffSz;
                writeOff = _buffSz;
            }
            else
                sz = Math.Min(_blkSz, buffer.Length - (offset - _buffSz));

            if (sz != 0)
                Array.Copy(buffer, offset - _buffSz, b.Buffer, writeOff, sz);
            b.BlockIndex = blockIndex;
        }
        private void blockCompress(CiBuffer b)
        {
            if (b.CompBuff[(int)compType.Lz4] != null)
            {
                int l4sz = b.CompBuff[(int)compType.Lz4].Length;
                _lz4.Compress(b.Buffer, 0, _blkSz, b.CompBuff[(int)compType.Lz4], 0, ref l4sz);
                b.CompSize[(int)compType.Lz4] = l4sz;
            }


            if (b.CompBuff[(int)compType.Cso] != null)
            {
                int sz = b.CompBuff[(int)compType.Cso].Length;
                _deflate.Compress(b.Buffer, 0, _blkSz, b.CompBuff[(int)compType.Cso], 0, ref sz);
                b.CompSize[(int)compType.Cso] = sz;
            }

            int best = int.MaxValue;
            for (int i = 0; i < b.CompSize.Length; i++)
            {
                if (b.CompBuff[i] != null && b.CompSize[i] < best)
                {
                    best = b.CompSize[i];
                    b.BestIdx = (compType)i;
                }
            }
        }

        private void blockWrite(CiBuffer b)
        {
            long pos = base.OutStream.Position;
            byte[] comp = b.CompBuff[(int)b.BestIdx];
            int compSz = b.CompSize[(int)b.BestIdx];
            int pad;

            if (_align != 0 && (pad = compSz % _align) != 0) //sets pad
            {
                pad = _align - pad;
                compSz += pad;
            }
            else
                pad = 0;

            bool isComp = compSz < _blkSz; //a final truncated block will always be smaller (should be padded with nulls before compress)
            uint offset = (uint)(pos >> _shift);
            if (!isComp)
            {

                base.OutStream.Write(b.Buffer, 0, _blkSz); //store full size even if truncated last block (end is nulled)
                if (!_isCso2)
                    offset |= 0x80000000;
            }
            else
            {
                if (pad != 0)
                    Array.Clear(comp, compSz - pad, pad);

                if (_isCso2 && b.BestIdx == compType.Lz4)
                    offset |= 0x80000000;

                base.OutStream.Write(comp, 0, compSz);
            }

            _hdr.WriteUInt32L(_hdrSize + (b.BlockIndex << 2), offset);
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            _threads.Complete();
            int idx = (int)(_originalSize / _blkSz) + (_originalSize % _blkSz == 0 ? 0 : 1);
            _hdr.WriteUInt32L(_hdrSize + (idx << 2), (uint)(base.OutStream.Position >> _shift));

            _nkitHdr.Checksums = this.Context.Result.InFileParts[0].Checksums.Clone();

            byte[] nkitHdr = _nkitHdr.ToArray();
            _hdr.Write(_nkitHdrPos, nkitHdr);

            base.OutStream.CrcPatch(0, s =>
            {
                base.OutStream.Write(_hdr, 0, _hdr.Length);
            });
            base.ProcessResults();
        }
    }
}