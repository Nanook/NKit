using Nanook.NKit.Dats;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    internal class FixPs3IrdStep : StepBase, IStep
    {
        private IStepContext _context;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        //private OutputType _type;
        //private DatManager _datManager;
        //private Playstation3FixData _fixData;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => true;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepFixPs3Ird;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal FixPs3IrdStep(IStepContextConstruct context)
        {
            //if (context.StepInfo.StepType == TaskType.Scan)
            //    _type = OutputType.Scan;
            //else if (context.StepInfo.StepType == TaskType.Verify)
            //    _type = OutputType.None;
            //else
            //    _type = OutputType.Image;
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
            _outExt = context.StepConfig;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;

            //_datManager = _context.DatManager;
            //_fixData = _context.Settings.FixData<Playstation3FixData>();
        }

        public override void Process(ISection section)
        {
            if (base.InChkStream != null && section.AreaOffset == 0 && section.ImageOffset != 0 && Context.ImageInfo.IsFolderIndex) // per part
                base.InChkStream.NewPart(null, null, false);
            base.Process(section);
            if (base.OutStream.Position == 0)
                base.OutStream.NewPart(_outName, _outExt, true);
            base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            IrdFileResults results = ((ImageInfo)_context.ImageInfo).IrdResults;
            uint? irdCrc = _context.ImageInfo.StepImageInfo.HasCrc ? _context.ImageInfo.StepImageInfo.Checksums.Crc : null;

            Scan scan = this.Context.Scan;
            uint crc = this.Context.Scan.Crc;
            long size = this.Context.Scan.Size;

            if (irdCrc.HasValue)
                _context.Log.Info(() => string.Format("Ird: Image {0} [Ird Crc {1}]", irdCrc.Value == crc ? "matches" : "does not match", irdCrc.Value.ToString("X8")));
            List<IrdFileResult> failedFiles = results.Where(a => a.IsMissing || !a.SizeIsValid || !a.IsMd5Valid).ToList();
            _context.Log.Info(() => string.Format("Ird: [Files {0}] - [Failed {1}]", results.Count, failedFiles.Count));
            foreach (IrdFileResult fail in failedFiles)
                _context.Log.Info(() => string.Format("IrdFile: {0} [Size {1}] [Md5 {2}] {3}", fail.IsMissing ? "Missing" : (!fail.SizeIsValid ? "BadSize" : "BadMd5 "), fail.File.FsSize.ToString().PadRight(10), fail.Md5.ToHexString(), fail.File.FullName));
            foreach (IrdFileResult itm in results.Where(a => a.FixFileName != null))
                _context.Log.Info(() => string.Format("FixFile: [Size {0}] [Md5 {1}] {2}", itm.File.FsSize.ToString().PadRight(10), itm.Md5.ToHexString(), itm.FixFileName));

            DatItem match = _context.DatManager?.FindImageMatch(crc, size);

            if (match != null)
            {
                this.Context.Result.ResultCrc = match.Bins[0].Checksums.Crc; //invalidates the scan
                this.Context.Result.ResultSize = match.Size; //invalidates the scan
                this.Context.Result.MatchedDatItem = match;
            }
            else
                this.Context.Result.ResultCrc = scan.Crc; //invalidates the scan
            this.Context.Result.ResultSize = scan.Size; //invalidates the scan

            base.ProcessResults();
        }

        public void Patched(ISection section)
        {
        }

    }
}