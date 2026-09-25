using System.Collections.Generic;

namespace Nanook.NKit
{
    internal interface ISectionProcessor : ISection
    {
        void Update();
        void Process();
        void Complete();

        List<MetaData> MissingData { get; }
        IPatchInfo PatchInfo { get; }
        IBuffer Buffer { get; set; }
        IFileSystemData FileSystemData { get; set; }

        void PostProcess();
    }
}