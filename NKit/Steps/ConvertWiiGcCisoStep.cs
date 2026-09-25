using Nanook.NKit.Configuration;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System.Collections.Generic;

namespace Nanook.NKit
{

    internal class ConvertWiiGcCisoStep : WiiGcBlockRemovalBase, IStep
    {
        private IStepContext _context;
        //private IConvertTaskEncoder _cnv;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        private string[] _fmt;

        private byte[] _hdr;
        private const int _HdrSz = WiiConsts.WiiSectorSize;
        private const int _HdrDiscHdrSz = 0x8;

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
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertWiiGcCiso;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertWiiGcCisoStep(IStepContextConstruct context) : base()
        {
            string[] fmt = context.StepConfig.Split(':');

            _outName = context.SourceImageName;
            _outExt = fmt[0];

            // Use configuration service for validation instead of hardcoded logic
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateCisoFormat(context.StepConfig);
            if (!validationResult.IsValid)
            {
                throw new HandledException($"Convert - {validationResult.ErrorMessage}");
            }

            // Use configuration constants and defaults
            _fmt = new[] {
                ConfigSettingsConstants.FormatCiso,
                fmt.Length < 2 || string.IsNullOrWhiteSpace(fmt[1]) ? "y" : fmt[1].ToLower(),
            };

            this.ContractIsLossy = _fmt[1] != "y";
            context.AddSettingsInfo("ConvertTo", $"{_fmt[0]} [{(this.ContractIsLossy ? "Lossy" : "Lossless")}]");

            this.ContractReqChk = !this.ContractIsLossy;
            base.BlockSize = (int)WiiConsts.WiiGroupSize;

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

            _hdr = new byte[_HdrSz];

            _hdr.WriteString(0, 4, "CISO");
            _hdr.WriteUInt32L(0x04, (uint)base.BlockSize);
        }

        public void Patched(ISection section)
        {
        }

        public override void Process(ISection section)
        {
            if (section.ImageOffset == 0)
                base.OutStream.NewPart(_outName, _outExt, true);

            base.Process(section);
        }

        public override void ProcessResults() => base.ProcessResults();


        public override byte[] GetHeader() => _hdr;

        public override byte[] FinalHeader(int blocks, Scan scan, bool lossless, Checksums chk)
        {
            int hPos = _HdrDiscHdrSz;
            int fullBlocks = (int)(ImageSize / base.BlockSize) + (ImageSize % base.BlockSize != 0 ? 1 : 0);

            for (int i = 0; i < fullBlocks; i++)
            {
                if (!Gaps[i])
                    _hdr.Write8(hPos, 1);
                hPos++;
            }

            return _hdr;
        }

        public override byte[] PostData(int blocks, Scan scan, bool lossless, Checksums chk)
        {
            if (lossless)
            {
                NKitHeader hdr = new NKitHeader(2, true, true, true, true, true, HeaderKeyType.None, 0, true, false, false);
                hdr.Size = ImageSize;
                hdr.Checksums.Crc = scan.Crc;
                hdr.Checksums.Md5 = chk.Md5;
                hdr.Checksums.Sha1 = chk.Sha1;
                hdr.Checksums.XxHash = chk.XxHash;
                byte[] nkitHdr = hdr.ToArray();
                byte[] fullHdr = new byte[nkitHdr.Length + GapType.Bytes.Length];
                fullHdr.Write(0, nkitHdr, nkitHdr.Length);
                fullHdr.Write(nkitHdr.Length, GapType.Bytes, 0, GapType.Bytes.Length);
                return fullHdr;
            }
            return null;
        }

    }
}