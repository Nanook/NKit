using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{

    public interface IFsItem
    {
        string Name { get; }
        IFsFolder Parent { get; }
        string Path { get; }
        string ToString();
    }

    public interface IFsFolder : IFsItem
    {
        List<IFsFile> Files { get; }
        List<IFsFolder> Folders { get; }
    }

    public interface IFsFileParts
    {
        List<IFsFilePart> Parts { get; }
        long Size { get; }
        ulong XxHash { get; }
        uint Crc { get; }

    }

    public interface IFsFilePart
    {
        int Index { get; }
        long OffsetInFile { get; }
        IFsFile FsFile { get; }
    }

    public interface IFsFile : IFsItem
    {
        bool IsMissing { get; }
        bool IsLastFile { get; }
        int SplitIndex { get; }
        IFsFileParts SplitParts { get; }
        string FullName { get; }
        long FsSize { get; }
        ulong XxHash { get; set; }
        uint Crc { get; set; }
        uint GapCrc { get; set; }
        bool IsSystemFile { get; }
        long FsOffset { get; }
        long PostGapSize { get; }
        long PostGapFsOffset { get; }

        IFsFile Clone();
    }

    public interface IFsFileXxHashCalc
    {
        XXHash64 Object { get; set; }
        Stream Stream { get; set; }
        ulong XxHash { get; set; }
        uint Crc { get; set; }
    }
}