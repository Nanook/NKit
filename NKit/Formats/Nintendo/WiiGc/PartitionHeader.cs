//using Org.BouncyCastle.Crypto.Digests;
//using Org.BouncyCastle.Crypto.Encodings;
//using Org.BouncyCastle.Crypto.Engines;
//using Org.BouncyCastle.Crypto.Parameters;
//using Org.BouncyCastle.Math;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.Nintendo.WiiGc
{
    internal class PartitionHeader
    {
        public PartitionHeader(byte[] publicKeyModulus, byte[] publicKeyModulusRvt, byte[] publicKeyExponent, byte[] headerData, int offset)
        {
            List<SignedData> Items = new List<SignedData>();

            int TicketSize = 0x2a4;
            int TicketOffset = offset + 0x0;

            int off = offset + TicketSize;

            int TmdSize = (int)headerData.ReadUInt32B(off);
            int TmdOffset = (int)headerData.ReadUInt32B(off + 0x4) << 2;
            int ChainSize = (int)headerData.ReadUInt32B(off + 0x8);
            int ChainOffset = (int)headerData.ReadUInt32B(off + 0xc) << 2;
            int H3Offset = (int)headerData.ReadUInt32B(off + 0x10) << 2;
            int Size = (int)headerData.ReadUInt32B(off + 0x14) << 2;
            int H3Size = Size - H3Offset;
            PartitionDataSize = ((long)headerData.ReadUInt32B(off + 0x18)) << 2;

            Data = headerData.Read(offset, Size);

            using (MemoryStream ms = new MemoryStream(Data))
            {
                ms.Seek(ChainOffset, SeekOrigin.Begin);
                int o;
                while ((o = (int)ms.Position) < ChainOffset + ChainSize)
                    Items.Add(new SignedDataCert(ms) { Offset = o });
            }

            this.CertValidator = new CertValidator(
                publicKeyModulus,
                publicKeyModulusRvt,
                publicKeyExponent,
                new SignedData(Data.Read(TicketOffset, TicketSize)) { Offset = TicketOffset },
                new SignedData(Data.Read(TmdOffset, TmdSize)) { Offset = TmdOffset },
                Items,
                Data.Read(H3Offset, H3Size)
            );
        }

        public CertValidator CertValidator { get; }

        public byte[] Data { get; }
        public long PartitionDataSize { get; }

    }
}