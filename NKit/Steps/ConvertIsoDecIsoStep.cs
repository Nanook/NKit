using Nanook.NKit.Configuration;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nanook.NKit
{
    internal class ConvertIsoDecIsoStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private string _outExt;

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.Image;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepConvertIsoDecIso;

        public override string ProposedName() => $"{_outName}.{_outExt}";

        internal ConvertIsoDecIsoStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Validate that this format is supported for the system
            IReadOnlyList<string> supportedFormats = ConfigSettingsRanges.GetSupportedFormats(context.SystemType);
            if (!supportedFormats.Contains(ConfigSettingsConstants.FormatDecIso, StringComparer.OrdinalIgnoreCase))
            {
                throw new HandledException($"Convert to '{ConfigSettingsConstants.FormatDecIso}' format is not supported for System {context.SystemType}");
            }

            _outName = context.SourceImageName;
            _outExt = "dec.iso"; // DecIso format creates .dec.iso files

            context.AddSettingsInfo("ConvertTo", ConfigSettingsConstants.FormatDecIso);
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.AreaInfo.IsEncrypted && this.Context.Key == null)
                throw new Exception("Encrypted area detected with no Key");

            if (base.OutStream.Position == 0)
            {
                base.OutStream.NewPart(_outName, _outExt, true);
                if (section.Decrypted.ReadString(Consts.Ps33k3yOffsetHeader, 2).ToLower() == "en")
                    section.Decrypted.WriteString(Consts.Ps33k3yOffsetHeader, 2, "De");
            }
            base.OutStream.Write(section.Decrypted, 0, (int)section.Size);
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