using Nanook.NKit.Dats;

namespace Nanook.NKit
{
    internal interface IStepInfo
    {
        string Name { get; }
        TaskType StepType { get; }
        DatItem DatMatch { get; }
        Scan SrcScan { get; }
        IParts SrcParts { get; }
        string Config { get; }
        bool CreateScan { get; }
        string ImageConfig { get; }
        VerifyMethod VerifyMethod { get; }
        ChecksumType[] VerifyChecksums { get; set; }
        bool CreateInChecksum { get; }
        bool CreateOutChecksum { get; }
        bool WriteImage { get; }
        bool DeleteSourceCandidate { get; }

        bool ReqPatch { get; }
        bool ReqChk { get; }
        bool FullScan { get; }
        bool IsLossy { get; }
        bool IsFix { get; }
        bool IsExpand { get; }
        OutputType OutputType { get; }
        bool CanCrc { get; }
        bool CanHash { get; }
    }
}