using System.Collections.Generic;

namespace Nanook.NKit
{
    public interface IFileSystem
    {
        List<IFsFile> Files { get; }
        IFsFolder Root { get; }

        List<IFsFile> CloneFiles();
    }
}