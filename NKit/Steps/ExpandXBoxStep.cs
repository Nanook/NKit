using Nanook.NKit.Steps.Shared;

namespace Nanook.NKit
{
    internal class ExpandXBoxStep : StepBase, IStep
    {
        private IStepContext _context;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        private OutputType _type;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => _type;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExpandXBox;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ExpandXBoxStep(IStepContextConstruct context)
        {
            // Validate that expand operation is appropriate for this system
            if (context.SystemType != SystemType.XBox && context.SystemType != SystemType.XBox360)
            {
                context.Log?.Info(() => $"Warning: Expand operation being applied to non-XBox system: {context.SystemType}");
            }

            if (context.StepInfo.StepType == TaskType.Scan)
                _type = OutputType.Scan;
            else if (context.StepInfo.StepType == TaskType.Verify)
                _type = OutputType.None;
            else
                _type = OutputType.Image;

            base.CheckContract(context.StepInfo);

            _outName = context.SourceImageName;
            _outExt = context.StepConfig;

            context.AddSettingsInfo("ExpandTo", _outExt ?? "iso");
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
        }

        public override void Process(ISection section)
        {
            if (section.ImageOffset == 0)
                base.OutStream.NewPart(_outName, _outExt, true);
            if (base.InChkStream != null && section.AreaOffset == 0 && section.ImageOffset != 0 && Context.ImageInfo.IsFolderIndex) // per part
                base.InChkStream.NewPart(null, null, false);
            base.Process(section);
            base.OutStream.Write(section.Encrypted, 0, (int)section.Size);
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            base.ProcessResults();
        }

        public void Patched(ISection section)
        {
        }

    }
}