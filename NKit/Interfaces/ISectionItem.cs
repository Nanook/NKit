using System.Collections.Generic;

namespace Nanook.NKit
{
    internal interface ISectionItem
    {
        long ImageOffset { get; }
        long AreaOffset { get; }
        long AreaBase { get; }
        IFsFile FsFile { get; }
        ISectionData File { get; }
        ISectionData Gap { get; }
        List<ISectionData> GapInfo { get; }


        int FileIndex { get; }
        List<string> FileSystems { get; }
    }
}