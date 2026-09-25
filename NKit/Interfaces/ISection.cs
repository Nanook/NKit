using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit
{
    public enum CompletionStatus { Processing, Complete, ToBePatched }
    internal interface ISection
    {
        long ImageOffset { get; }
        long Size { get; }
        byte[] Decrypted { get; }
        byte[] Encrypted { get; }
        long FsOffset { get; }
        long AreaOffset { get; }
        long FsSize { get; }
        uint Crc { get; }
        uint CrcDecrypted { get; }
        ulong XxHash { get; }
        AreaType Type { get; }
        int FileStartIndex { get; }
        int FileEndIndex { get; }
        IFileSystem FullAreaFileSystem { get; } //only set when area fs has all directory entries (so it's CsqThread safe as readonly)
        IAreaFileSystemView AreaFileSystem { get; } //frozen, immutable view of the area's file system (Primary/System/per-FS). Null until the fs is fully parsed. Preferred over FullAreaFileSystem for Out consumers.
        SectionItems Items { get; }
        IEnumerable<NonCreatableData> NonCreatableItems { get; }
        bool IsValid { get; }
        bool IsCreatable { get; }
        bool IsEncrypted { get; }
        CompletionStatus Status { get; }
        BitState State { get; }
        byte[] SeekIv { get; }

        AreaInfo AreaInfo { get; }

        void Write(int fsOffset, Stream fromStream, int size);
        void WriteBytes(int fsOffset, byte[] bytes, int offset, int size);
        void Read(int fsOffset, int size, Stream toStream);
        byte[] ReadBytes(int fsOffset, int size);
    }
}