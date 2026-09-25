namespace Nanook.NKit.Nintendo
{
    internal enum SignatureType { RSA4096 = 0x00010000, RSA2048 = 0x00010001, ECC = 0x00010002, RSA4096SHA256 = 0x00010003, RSA2048SHA256 = 0x00010004 };
    internal enum PublicKeyType { RSA4096 = 0, RSA2048 = 1 };
    internal enum ShaType { SHA1 = 0, SHA256 = 1 }

    internal class SignedData
    {
        public SignedData()
        {
        }

        public SignedData(byte[] data)
        {
            Populate(data);
        }

        public void Populate(byte[] data) => this.Data = data;

        public int Offset { get; internal set; }
        public byte[] Data { get; private set; }
        public byte[] Payload => Data.Read(PayloadOffset, PayloadSize);

        public SignatureType Type => (SignatureType)Data.ReadUInt32B(0);
        public ShaType ShaType => (Type == SignatureType.RSA4096SHA256 || Type == SignatureType.RSA2048SHA256) ? ShaType.SHA256 : ShaType.SHA1;
        public int SignatureLength => CertValidator.SigLen(Type);
        public int PayloadOffset => 0x4 + SignatureLength + 0x3c;
        public int PayloadSize => Data.Length - PayloadOffset;

        public byte[] Signature => Data.Read(4, SignatureLength);
        public string Issuer => Data.ReadStringToNull(PayloadOffset);
        public string ParentName
        {
            get
            {
                string issuer = this.Issuer;
                int l = issuer.LastIndexOf('-');
                return l < 0 || l >= issuer.Length ? "" : issuer.Substring(l + 1);
            }
        }
        public override string ToString() => string.Format($"SignedData - Type: {Type}, Issuer: {Issuer}, Parent: {ParentName}, Offset: {Offset:X}");
    }
}