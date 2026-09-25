using Nanook.NKit.Dats;

namespace Nanook.NKit
{
    internal interface IStepContextConstruct
    {
        IImageInfo ImageInfo { get; }
        SystemType SystemType { get; }
        IStepInfo StepInfo { get; }
        IStep Step { get; }
        string SourceImageName { get; }
        byte[] HeaderData { get; }
        string StepConfig { get; }

        ILogScope Log { get; }
        DatManager DatManager { get; }
        long ImageSize { get; }
        IDataProvider Settings { get; }

        void AddSettingsInfo(string name, string value);
        void SkipBlockTaskEnable();
        void SkipToImageOffsetSet(long imageOffset);
    }
}