namespace Nanook.NKit
{
    internal interface IImageContext
    {
        SystemType SystemType { get; }
        void SetSystemType(SystemType systemType);
        IDataProvider Settings { get; }

        // The task being run (Fix/Convert/Scan/...). Used at Open time to decide, e.g., whether a
        // GameCube source should be wrapped with the up-front Fix decorator before the Image reads.
        TaskType TaskType { get; }

        // The type of the CURRENT step (Fix/Verify/Scan/...). Unlike TaskType (which is the overall
        // task and stays "Fix" for every step), this distinguishes the fix step from the verify
        // step that re-reads the fix output. The Fix decorator must only wrap on the actual fix
        // step; the verify step reads the already-corrected output straight through.
        IStepInfo StepInfo { get; }
        IImageHeader Header { get; set; }
        IImageInfo ImageInfo { get; set; }
        ILogScope Log { get; }
        Scan Scan { get; set; }
        SourceFile SourceFile { get; }
        long SkipToImageOffsetGet(long imageOffset);
        SkipType SkipType { get; set; }
    }
}