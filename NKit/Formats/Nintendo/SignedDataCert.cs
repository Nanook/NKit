using System.IO;

namespace Nanook.NKit.Nintendo
{
    internal enum SignedStatus { None, Valid, InvalidTicket, InvalidTmd, FakeSigned, InvalidTmdHash, InvalidH3Hash, MissingTicket }

    internal class SignedDataCert : SignedData
    {

        public SignedDataCert(byte[] data) : base(data)
        {
        }

        public SignedDataCert(Stream s) : base()
        {
            long pos = s.Position;

            SignatureType type = (SignatureType)s.ReadBytes(4).ReadUInt32B(0);
            int len = 0x4 + CertValidator.SigLen(type) + 0x3c + 0x40;

            s.Position = pos + len;
            PublicKeyType key = (PublicKeyType)s.ReadBytes(4).ReadUInt32B(0);

            len += 0x4 + 0x40 + 0x4;

            if (key == PublicKeyType.RSA2048)
                len += 0x100 + 0x4 + 0x34;
            else if (key == PublicKeyType.RSA4096)
                len += 0x200 + 0x4 + 0x34;
            else
                len += 0x3c + 0x3c;

            s.Position = pos;
            base.Populate(s.ReadBytes(len));
        }

        public string Name => base.Payload.ReadStringToNull(0x44);
        public uint Id => Payload.ReadUInt32B(0x84);  //follows Name in CertHeader
        public PublicKeyType KeyType => (PublicKeyType)Payload.ReadUInt32B(0x40);

        public byte[] KeyEc => Payload.Read(0x88, 0x3c);
        public int KeyRsaModulusOffset => PayloadOffset + 0x88;
        public int KeyRsaModulusSize => KeyType == PublicKeyType.RSA2048 ? 0x100 : 0x200;
        public byte[] KeyRsaModulus => Payload.Read(0x88, KeyRsaModulusSize);
        public int KeyRsaExponentOffset => PayloadOffset + 0x88 + KeyRsaModulusSize;
        public int KeyRsaExponentSize => 4;
        public byte[] KeyRsaExponent => Payload.Read(0x88 + KeyRsaModulusSize, KeyRsaExponentSize);
        public override string ToString() => string.Format($"SignedDataCert - Type: {Type}, Issuer: {Issuer}, Parent: {ParentName}, Name: {Name}, KeyType: {KeyType}, Offset: {Offset:X}");

    }
}