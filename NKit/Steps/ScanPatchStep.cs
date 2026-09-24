using Nanook.NKit.Steps.Shared;
using System.IO;

namespace Nanook.NKit
{
    internal class ScanPatchStep : StepBase, IStep
    {
        private IStepContext _context;
        //private string _outFilename;
        //private bool _isDisposed;
        private string _outName;
        private string _outExt;

        internal override bool ContractReqPatch => true;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepScanPatch;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ScanPatchStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            _outName = context.SourceImageName;
            _outExt = context.StepConfig;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            base.OutStream.NewPart(_outName, _outExt, true);
        }

        public override void Process(ISection section)
        {
            if (section.AreaOffset == 0 && section.ImageOffset != 0 && Context.ImageInfo.IsFolderIndex) // per part
                base.InChkStream.NewPart(null, null, false);
            base.Process(section);
            base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            base.OutStream.Close();
            base.ProcessResults();
        }

        public void Patched(ISection section)
        {
            base.OutStream.Seek(section.ImageOffset, SeekOrigin.Begin);
            base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }
    }
}