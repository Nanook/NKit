using Nanook.NKit.Dats;

namespace Nanook.NKit.Steps.Shared
{
    internal class StepInfo : IStepInfo
    {
        public string Name { get; set; }
        public TaskType StepType { get; set; }
        public DatItem DatMatch { get; set; }
        public Scan SrcScan { get; set; }
        public IParts SrcParts { get; set; }

        public string Config { get; set; }
        public bool CreateScan { get; set; }
        public VerifyMethod VerifyMethod { get; set; }
        public ChecksumType[] VerifyChecksums { get; set; }
        public bool CreateInChecksum { get; set; }
        public bool CreateOutChecksum { get; set; }
        public bool WriteImage { get; set; }
        public bool DeleteSourceCandidate { get; set; }

        public bool ReqPatch { get; set; }

        public bool ReqChk { get; set; }

        public bool FullScan { get; set; }

        public bool IsLossy { get; set; }
        public bool IsFix { get; set; }

        public bool IsExpand { get; set; }

        public OutputType OutputType { get; set; }

        public bool CanCrc { get; set; }

        public bool CanHash { get; set; }
        public string ImageConfig { get; set; }
    }
}