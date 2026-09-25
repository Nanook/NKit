using System.Security.Cryptography;

namespace Nanook.NKit.Nintendo
{
    internal class WiiSecuritySector
    {
        public WiiSecuritySector(int index, int blockSize, int hashSize)
        {
            Size = blockSize;
            Index = index;
            Offset = index * blockSize;
            FsOffset = (index * blockSize) + hashSize;
            Aes = Aes.Create();
            Iv = new byte[16];
            Aes.Mode = CipherMode.CBC;
            Aes.Padding = PaddingMode.None;
            Sha = SHA1.Create();
        }

        public readonly int Index;
        public readonly int Offset;
        public readonly int FsOffset;
        public readonly Aes Aes;
        public byte[] Iv;
        public int Size;

        public bool IsUsed;
        public SHA1 Sha { get; }
        public void Populate()
        {
        }
        public override string ToString() => string.Format("Index:{0}, Offset:{1}, {2}, {3}", Index.ToString("X8"), Offset.ToString("X8"), IsUsed ? "Used" : "NotNused");
    }
}