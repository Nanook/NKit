using System.Collections.Generic;

namespace Nanook.NKit
{
    public interface IPart
    {
        string FileName { get; }
        Checksums Checksums { get; }
        long Size { get; }
        byte[] this[ChecksumType index] { get; }
    }

    public interface IParts : IEnumerable<IPart>
    {
        IPart this[int index] { get; }
        int Length { get; }
        ChecksumType? GetHashType(bool incXxHash, out bool hasCrc);
        bool IsMultiPart { get; }
    }

    public interface IPartsGlobalHash
    {
        bool HasGlobalHashes { get; }
        uint GlobalCrc { get; }
        ulong GlobalXxHash { get; }
        byte[] GlobalMd5 { get; }
        byte[] GlobalSha1 { get; }
    }
}