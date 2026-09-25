using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using System.Collections.Generic;

namespace Nanook.NKit
{

    internal class ConvertWiiGcWbfsStep : WiiGcBlockRemovalBase, IStep
    {
        private IStepContext _context;
        //private IConvertTaskEncoder _cnv;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        private string[] _fmt;

        private byte[] _hdr;
        private const byte _blkShift = 0x15;
        private const byte _blkWbfsShift = 0x9;
        private const int _hdrWbfsSz = 1 << _blkWbfsShift;
        private const int _hdrDiscHdrSz = 0x100;

        //private const int _nkitHdrLen = 0x8 + 0x8 + 0x4 + 0x10 + 0x14 + 0x8; //'NKIT  v1' ImageSize, CRC32, MD5, SHA1, XXHash64


        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk { get; }
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy { get; }
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertWiiGcWbfs;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertWiiGcWbfsStep(IStepContextConstruct context) : base()
        {
            string[] fmt = context.StepConfig.Split(':');

            _outName = context.SourceImageName;
            _outExt = fmt[0];

            // Use configuration service for validation instead of hardcoded logic
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateWbfsFormat(context.StepConfig);
            if (!validationResult.IsValid)
            {
                throw new HandledException($"Convert - {validationResult.ErrorMessage}");
            }

            // Use configuration constants and defaults
            _fmt = new[] {
                ConfigSettingsConstants.FormatWbfs,
                fmt.Length < 2 || string.IsNullOrWhiteSpace(fmt[1]) ? "y" : fmt[1].ToLower(),
            };

            this.ContractIsLossy = _fmt[1] != "y";
            context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} [{(this.ContractIsLossy ? "Lossy" : "Lossless")}]");
            this.ContractReqChk = !this.ContractIsLossy;
            base.BlockSize = 1 << _blkShift; //0x200000

            // Get configuration warnings if any
            IEnumerable<string> warnings = ConfigSettingsFormatValidator.GetConfigurationWarnings(context.SystemType, _fmt[0], null, null);
            foreach (string warning in warnings)
            {
                context.Log?.Info(() => $"Warning: {warning}");
            }

            base.CheckContract(context.StepInfo);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;

            _hdr = new byte[base.BlockSize];
            _hdr.WriteString(0, 4, "WBFS");
            _hdr.Write8(0x08, _blkWbfsShift); //sector size (1 << 9 = 0x200)
            _hdr.Write8(0x09, _blkShift); //wbfs sector size (1 << 0x15 = 0x200000)
            _hdr.Write8(0x0A, 0x01); //version
            _hdr.Write8(0x0C, 0x01);

        }

        public void Patched(ISection section)
        {
        }

        public override void Process(ISection section)
        {
            if (section.ImageOffset == 0)
            {
                _hdr.Write(0x200, section.Encrypted, 0, _hdrDiscHdrSz);
                base.OutStream.NewPart(_outName, _outExt, true);
            }
            base.Process(section);
        }

        public override void ProcessResults() => base.ProcessResults();


        public override byte[] GetHeader() => _hdr;

        public override byte[] FinalHeader(int blocks, Scan scan, bool lossless, Checksums chk)
        {
            int hPos = 0x10000;

            if (lossless)
            {
                NKitHeader hdr = new NKitHeader(2, true, true, true, true, true, HeaderKeyType.None, 0, true, false, false);
                hdr.Size = ImageSize;
                hdr.Checksums.Crc = scan.Crc;
                hdr.Checksums.Md5 = chk.Md5;
                hdr.Checksums.Sha1 = chk.Sha1;
                hdr.Checksums.XxHash = chk.XxHash;
                byte[] nkitHdr = hdr.ToArray();
                _hdr.Write(hPos, nkitHdr, nkitHdr.Length);
                _hdr.Write(hPos + nkitHdr.Length, GapType.Bytes, 0, GapType.Bytes.Length);
            }

            hPos = _hdrDiscHdrSz + _hdrWbfsSz;
            int blockIdx = 1;
            int fullBlocks = (int)(ImageSize / base.BlockSize) + (ImageSize % base.BlockSize != 0 ? 1 : 0);

            for (int i = 0; i < fullBlocks; i++)
            {
                if (!Gaps[i])
                    _hdr.WriteUInt16B(hPos, (ushort)blockIdx++);
                hPos += 2;
            }

            _hdr.WriteUInt32B(0x4, (uint)((blocks + 1) * (base.BlockSize / _hdrWbfsSz))); //+1 for header
            return _hdr;
        }

        public override byte[] PostData(int blocks, Scan scan, bool lossless, Checksums chk) => null;

    }
}