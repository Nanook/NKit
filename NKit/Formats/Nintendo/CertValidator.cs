//using Org.BouncyCastle.Crypto.Digests;
//using Org.BouncyCastle.Crypto.Encodings;
//using Org.BouncyCastle.Crypto.Engines;
//using Org.BouncyCastle.Crypto.Parameters;
//using Org.BouncyCastle.Math;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Xml;

namespace Nanook.NKit.Nintendo
{
    internal class CertValidator
    {
        public CertValidator(byte[] publicKeyModulus, byte[] publicKeyModulusRvt, byte[] publicKeyExponent, SignedData ticket, SignedData tmd, List<SignedData> certs, byte[] h3Table)
            : this(publicKeyModulus, publicKeyModulusRvt, publicKeyExponent, ticket, tmd, certs, h3Table, 0)
        {
        }

        public CertValidator(byte[] publicKeyModulus, byte[] publicKeyModulusRvt, byte[] publicKeyExponent, SignedData ticket, SignedData tmd, List<SignedData> certs, byte[] h3Table, int offsetInHeader)
        {
            this.PublicKeyModulus = publicKeyModulus;
            this.PublicKeyModulusRvt = publicKeyModulusRvt;
            this.PublicKeyExponent = publicKeyExponent;
            this.H3Table = h3Table;
            this.Ticket = ticket;
            this.Tmd = tmd;
            Items = certs;
        }

        public byte[] PublicKeyModulus { get; }
        public byte[] PublicKeyModulusRvt { get; }
        public byte[] PublicKeyExponent { get; }
        public byte[] H3Table { get; }

        public SignedData Ticket { get; }
        public SignedData Tmd { get; }

        public List<SignedData> Items { get; }

        public SignedStatus Validate(bool isRetail, bool skipH3Test)
        {
            SignedStatus result = ValidateChain(this.Ticket, isRetail);
            if (result != SignedStatus.Valid && result != SignedStatus.FakeSigned)
                return result;

            SignedStatus tmdRes = ValidateChain(this.Tmd, isRetail);
            if (result != SignedStatus.FakeSigned || tmdRes != SignedStatus.Valid) //don't overwrite a result of fakesigned with valid
                result = tmdRes;
            if (result != SignedStatus.Valid && result != SignedStatus.FakeSigned)
                return result;

            if (!skipH3Test && !ValidH3Hash())
                return SignedStatus.InvalidH3Hash;

            return result;
        }

        internal SignedStatus ValidateChain(SignedData item, bool isRetail)
        {
            SignedData cert = item;
            bool rootChecked = false;
            RSAParameters key;
            SignedDataCert p = null;
            SignedStatus invalidStatus = item == this.Tmd ? SignedStatus.InvalidTmd : SignedStatus.InvalidTicket;
            SignedStatus result = invalidStatus;
            bool isWiped = this.Ticket == null ? false : (this.Ticket.Payload.ReadUInt64B(0x9c) == WiiConsts.NKitWipeIV);

            while (cert != null)
            {
                rootChecked = cert.ParentName == "";

                if (rootChecked)
                {
                    if (isWiped)
                        key = loadKey(true);
                    else
                        key = new RSAParameters() { Modulus = isRetail ? PublicKeyModulus : PublicKeyModulusRvt, Exponent = PublicKeyExponent };
                }
                else
                {
                    p = (SignedDataCert)Items.FirstOrDefault(a => (a as SignedDataCert)?.Name == cert.ParentName);
                    if (p == null)
                    {
                        string nm = cert.ParentName.ToUpper();
                        if (nm == "CP0000000B" || (isWiped && nm == "PC5555555O"))
                            p = new SignedDataCert(WiiUConsts.CertCp0b);
                        else if (nm == "CA00000003" || (isWiped && nm == "PN55555558"))
                            p = new SignedDataCert(WiiUConsts.CertCa03);
                        else if (nm == "XS0000000C" || (isWiped && nm == "KF5555555P"))
                            p = new SignedDataCert(WiiUConsts.CertXs0c);
                        else
                            throw new HandledException($"No cert found for {cert.ParentName}");
                        if (isWiped)
                            ResignCert(p.Data, p, loadKey(p.Type == SignatureType.RSA4096 || p.Type == SignatureType.RSA4096SHA256));
                    }
                    key = new RSAParameters() { Modulus = p.KeyRsaModulus, Exponent = p.KeyRsaExponent };
                }

                SignedStatus r = verify(key.Modulus, key.Exponent, cert.Signature, cert.Payload, cert.ShaType, invalidStatus);
                if (result != SignedStatus.FakeSigned || r != SignedStatus.Valid) //don't overwrite a result of fakesigned with valid
                    result = r;

                if (rootChecked || (result != SignedStatus.Valid && result != SignedStatus.FakeSigned))
                    break;

                cert = p;
            }

            return result;
        }

        private SignedStatus verify(byte[] mod, byte[] exp, byte[] sig, byte[] data, ShaType shaType, SignedStatus invalidStatus)
        {
            byte[] dataHash;
            if (shaType == ShaType.SHA256)
            {
                using (SHA256 sha = SHA256.Create())
                    dataHash = sha.ComputeHash(data);
            }
            else
            {
                using (SHA1 sha = SHA1.Create())
                    dataHash = sha.ComputeHash(data);
            }

            /////////////////////////////////////////////
            // - Bouncy castle version - uncommented using statements and add nuget package
            //     RsaKeyParameters pubKey = new RsaKeyParameters(false, new BigInteger(1, mod), new BigInteger(1, exp));
            //     RsaEngine rsa = new RsaEngine();
            //     rsa.Init(false, pubKey);
            //     byte[] sigHash = rsa.ProcessBlock(sig, 0, sig.Length);
            /////////////////////////////////////////////
            // - MS recommended way. Doesn't expose hash so can't test fake signing. Also needs padding which won't work with RVT TMD and ticket - runs without the rest of this method
            //     RSAParameters key = new RSAParameters() { ShardSize = mod, Exponent = exp };
            //     using (RSACryptoServiceProvider crypt = new RSACryptoServiceProvider())
            //     {
            //         crypt.ImportParameters(key);
            //         return crypt.VerifyData(data, sig, shaType == ShaType.SHA256 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1) ? SignedStatus.Valid : invalidStatus;
            //     }
            /////////////////////////////////////////////
            // - BigInteger version
            byte[] sigHash = FromBigInt(BigInteger.ModPow(ToBigInt(sig), ToBigInt(exp), ToBigInt(mod)));

            /////////////////////////////////////////////

            if (sigHash.Length == 0) //nulled sig results in 1 byte which is 0
                return invalidStatus;

            int off = Math.Max(sigHash.Length - dataHash.Length, 0);
            bool hasNull = false;
            for (int i = 0; i < Math.Min(sigHash.Length, dataHash.Length); i++)
            {
                byte h3Byte = dataHash[i];
                byte sigByte = sigHash[i + off];
                hasNull |= h3Byte == 0 || sigByte == 0;

                if (sigByte != h3Byte)
                    return hasNull ? SignedStatus.FakeSigned : invalidStatus; //only allow FakeSigned if we've seen a null and something doesn't match
            }
            return sigHash.Length >= dataHash.Length ? SignedStatus.Valid : SignedStatus.FakeSigned; //Valid if hashes are the same even with nulls when sigHash >= 20 bytes
        }

        private BigInteger ToBigInt(byte[] data)
        {
            if (BitConverter.IsLittleEndian)
            {
                data = (byte[])data.Clone();
                Array.Reverse(data);
            }

            if ((data.Last() & 0x80) > 0) //make positive
            {
                byte[] temp = new byte[data.Length + 1];
                Array.Copy(data, temp, data.Length);
                data = temp;
            }
            return new BigInteger(data);
        }

        private byte[] FromBigInt(BigInteger value)
        {
            byte[] data = value.ToByteArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return data;
        }

        public bool ValidH3Hash()
        {
            SHA1 sha1 = SHA1.Create();
            try
            {
                return sha1.ComputeHash(this.H3Table).Equals(0, this.Tmd.Payload, 0xb4, 0x14);
            }
            finally
            {
                sha1.Dispose();
            }
        }

        public static int SigLen(SignatureType sigType)
        {
            switch (sigType)
            {
                case SignatureType.RSA4096:
                case SignatureType.RSA4096SHA256:
                    return 0x200;
                case SignatureType.RSA2048:
                case SignatureType.RSA2048SHA256:
                    return 0x100;
                case SignatureType.ECC:
                    return 0x3c;
                default:
                    return 0x100;
            }
        }

        internal static void ResignCert(byte[] headerData, SignedData cert, RSAParameters key)
        {
            if (cert == null)
            {
                cert = new SignedDataCert(headerData);
                key = loadKey(cert.Type == SignatureType.RSA4096 || cert.Type == SignatureType.RSA4096SHA256);
            }

            if (cert is SignedDataCert) //replace public key
            {
                SignedDataCert sd = (SignedDataCert)cert;
                RSAParameters k = loadKey(sd.KeyType == PublicKeyType.RSA4096);
                headerData.Write(cert.Offset + sd.KeyRsaModulusOffset, k.Modulus, k.Modulus.Length);
            }

            using (RSA rsa = RSA.Create())
            {
                string txt = headerData.ReadString(cert.Offset + cert.PayloadOffset, cert is SignedDataCert ? 0x7f : 0x40).Rot13Words();
                headerData.WriteString(cert.Offset + cert.PayloadOffset, txt.Length, txt); //rename cert

                rsa.ImportParameters(key);
                byte[] sig = rsa.SignData(headerData, cert.Offset + cert.PayloadOffset, cert.PayloadSize, cert.ShaType == ShaType.SHA1 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                headerData.Write(cert.Offset + 4, sig); //write legit signature (signed with nkit key)
            }
        }

        internal void WipeHeaderChain(byte[] headerData, byte[] ticketKey, bool isWii)
        {
            //System.Diagnostics.Trace.WriteLine(new RSACryptoServiceProvider(2048).ToXmlString(true)); //create a key

            List<int> wiped = new List<int>();

            foreach (SignedData item in new[] { this.Ticket, this.Tmd })
            {
                SignedData cert = item;
                bool rootChecked = false;
                RSAParameters key;
                SignedDataCert p = null;
                SignedStatus invalidStatus = item == this.Tmd ? SignedStatus.InvalidTmd : SignedStatus.InvalidTicket;
                SignedStatus result = invalidStatus;

                while (cert != null && !wiped.Contains(cert.Offset))
                {
                    if (cert == this.Ticket)
                    {
                        headerData.Write(cert.Offset + WiiConsts.WiiPrtHdrKeyPtrOffset, encryptKey(ticketKey), ticketKey.Length); //legit hash to be signed
                        headerData.WriteUInt64B(cert.Offset + WiiConsts.WiiPrtHdrIVPtrOffset, WiiConsts.NKitWipeIV); //legit hash to be signed
                    }
                    else if (cert == this.Tmd)
                    {
                        string iv = headerData.Read(cert.Offset + cert.PayloadOffset + 0x4c, 8).ToHexString().Rot3Hex();
                        headerData.Write(cert.Offset + cert.PayloadOffset + 0x4c, iv.HexToBytes(), 8); //rename cert
                        if (isWii) //calc and write h3 header data
                        {
                            int h3Offset = (int)headerData.ReadUInt32B(WiiConsts.WiiPrtHdrTmdSizeOffset + 0x10) << 2;
                            using (SHA1 sha1 = SHA1.Create())
                            {
                                byte[] h3Hash = sha1.ComputeHash(headerData, h3Offset, headerData.Length - h3Offset);
                                headerData.Write(cert.Offset + cert.PayloadOffset + 0xb4, h3Hash, h3Hash.Length); //legit hash to be signed
                            }
                        }
                    }

                    rootChecked = cert.ParentName == "";

                    if (rootChecked)
                        key = loadKey(true);
                    else
                    {
                        p = (SignedDataCert)this.Items.FirstOrDefault(a => (a as SignedDataCert)?.Name == cert.ParentName);
                        if (p == null)
                        {
                            string nm = cert.ParentName.ToUpper();
                            if (nm == "CP0000000B")
                                p = new SignedDataCert(WiiUConsts.CertCp0b);
                            else if (nm == "CA00000003")
                                p = new SignedDataCert(WiiUConsts.CertCa03);
                            else if (nm == "XS0000000C")
                                p = new SignedDataCert(WiiUConsts.CertXs0c);
                            else if (nm == "CP0000000B")
                                p = new SignedDataCert(WiiUConsts.CertCp0b);
                            else
                                throw new HandledException($"No cert found for {cert.ParentName}");
                        }
                        key = loadKey(p.KeyType == PublicKeyType.RSA4096);
                    }

                    ResignCert(headerData, cert, key);
                    wiped.Add(cert.Offset);
                    cert = p;
                }
            }

            //replace any extra certs
            foreach (SignedDataCert c in this.Items.Where(a => headerData.ReadString(a.Offset + a.PayloadOffset, 4).StartsWith("Root")))
                ResignCert(headerData, c, loadKey(c.Type == SignatureType.RSA4096 || c.Type == SignatureType.RSA4096SHA256)); //lazy approach, fine as we always use the same pub key
        }

        private static RSAParameters loadKey(bool key4096)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(key4096 ? WiiConsts.PrivateKeyPemNKit4096 : WiiConsts.PrivateKeyPemNKit2048);
            return new RSAParameters()
            {
                Modulus = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("Modulus").InnerText),
                D = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("D").InnerText),
                DP = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("DP").InnerText),
                DQ = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("DQ").InnerText),
                P = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("P").InnerText),
                Q = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("Q").InnerText),
                InverseQ = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("InverseQ").InnerText),
                Exponent = Convert.FromBase64String(doc.DocumentElement.SelectSingleNode("Exponent").InnerText)
            };
        }

        private byte[] encryptKey(byte[] key)
        {
            byte[] encKey = new byte[0x10];
            byte[] iv = new byte[0x10];
            iv.WriteUInt64B(0, WiiConsts.NKitWipeIV);
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Key = WiiConsts.NKitWipeCommonKey;
                aes.IV = iv;
                using (ICryptoTransform cryptor = aes.CreateEncryptor())
                    cryptor.TransformBlock(key, 0, 16, encKey, 0);
            }
            return encKey;
        }
    }
}