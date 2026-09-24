using Nanook.NKit.Dats;

namespace Nanook.NKit
{
    internal class NKitStepResult
    {
        public IStepInfo StepInfo { get; internal set; }
        public Scan Scan { get; internal set; }
        public IParts InFileParts { get; internal set; }
        public IParts OutFileParts { get; internal set; }
        public string OutPath { get; internal set; }
        public DatItem MatchedDatItem { get; internal set; }
        public VerifyResult VerifyResult { get; internal set; }
        public ChecksumType[] ChkCompared { get; internal set; }
        public string VerifyType { get; internal set; }
        public uint? ResultCrc { get; internal set; } //Combined InCrc for most steps. Calculated for Fix
        public long? ResultSize { get; internal set; } //Summed InSize for most steps. Calculated for Fix
        public string FinalName { get; internal set; }
    }
}