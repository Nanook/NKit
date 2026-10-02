using Nanook.NKit.Steps.Shared;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    internal class ExtractWiiUStep : WiiUFileExtractBase
    {
        internal override bool ContractReqPatch  => false;
        internal override bool ContractReqChk   => false;
        internal override bool ContractFullScan  => false;
        internal override bool ContractIsLossy   => true;
        internal override bool ContractIsExpand  => false;
        internal override bool ContractIsFix     => false;
        internal override OutputType ContractOutputType => OutputType.FolderFiles;
        internal override bool ContractCanCrc    => false;
        internal override bool ContractCanHash   => false;
        internal override string ComponentTag    => LogScopes.StepExtractWiiU;

        internal ExtractWiiUStep(IStepContextConstruct context)
            : base(buildMask(context.StepConfig, out bool forensic), context.SourceImageName)
        {
            base.CheckContract(context.StepInfo);
            if (!forensic)
                context.AddSettingsInfo("Extract", context.StepConfig);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static FileMask buildMask(string stepConfig, out bool forensic)
        {
            Match m = Regex.Match(stepConfig ?? "ri:.*", "^([a-z]*):(.*)$");
            forensic = false;
            bool matchCase = false;

            if (m.Success)
            {
                matchCase = !m.Groups[1].Value.Contains('i');
                forensic  =  m.Groups[1].Value.Contains('f');
                if (m.Groups[1].Value.Contains('m'))
                    return FileMask.CreateImageFsMask(m.Groups[2].Value);
                else
                    return new FileMask(m.Groups[2].Value, matchCase);
            }
            return new FileMask(".*", false);
        }
    }
}
