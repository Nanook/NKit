using Nanook.NKit.Iso.Iso9660;

namespace Nanook.NKit
{
    internal interface IFstIso
    {
        void Setup(IBuffer buffer);
        bool IsUdf { get; }
        void ReadSystemData(FstFile match, IBuffer buffer);
        long GetSize(FstFile match, byte[] buff, int offset);
    }
}