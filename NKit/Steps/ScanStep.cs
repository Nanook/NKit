using Nanook.NKit.Steps.Shared;
using System;

namespace Nanook.NKit
{
    internal class ScanStep : StepBase, IStep
    {
        private IStepContext _context;
        //private string _outFilename;
        private string _outName;
        private string _outExt;
        private OutputType _type;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => _type;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepScan;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ScanStep(IStepContextConstruct context)
        {
            if (context.StepInfo.StepType == TaskType.Scan)
                _type = OutputType.Scan;
            else if (context.StepInfo.StepType == TaskType.Verify)
                _type = OutputType.None;
            else
                _type = OutputType.Image;

            base.CheckContract(context.StepInfo);

            _outName = context.SourceImageName;
            _outExt = context.StepConfig ?? "iso";

            // Add scan operation info
            context.AddSettingsInfo("ScanTo", _outExt);

            // System/task info will be reported elsewhere; do not emit a fixed "Scan operation for ..." log here.
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

            // must have a key, if was converting to deciso then would be other step type
            if (section.AreaOffset == 0 && _context.SystemType == SystemType.PS3 && _context.StepInfo.StepType == TaskType.Convert && section.AreaInfo.IsEncryptionSupported && _context.Key == null)
                throw new Exception("Encrypted area detected with no key");

            if (base.InChkStream != null && section.AreaOffset == 0 && section.ImageOffset != 0 && Context.ImageInfo.IsFolderIndex) // per part
                base.InChkStream.NewPart(null, null, false);
            base.Process(section);

            //#if DEBUG
            //            if (section.AreaInfo.BlockFsSize != section.AreaInfo.BlockSize)
            //            {
            //                //wipe wii hashes
            //                for (int i = 0; i < section.Size; i += section.AreaInfo.BlockSize)
            //                    Array.Clear(section.Decrypted, i, section.AreaInfo.BlockFsOffset);
            //            }
            //            if (section.AreaInfo.IsEncrypted)
            //                Array.Copy(section.Decrypted, section.Encrypted, (int)section.Size);
            //#endif
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