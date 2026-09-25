namespace Nanook.NKit
{

    internal interface IStepContext : IStepContextConstruct
    {
        SourceFile SourceFile { get; }

        byte[] Key { get; }

        Scan Scan { get; }
        NKitStepResult Result { get; }
        string WritePath { get; }
        int Index { get; }
    }
}