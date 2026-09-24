using Nanook.NKit.Nintendo.WiiGc;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class SiData
    {
        public int AppIndex { get; internal set; }
        public ulong TitleId { get; internal set; }
        public byte[] FileCert { get; internal set; }
        public byte[] FileCetk { get; internal set; }
        public byte[] FileTicket { get; internal set; }
        public byte[] FileTmd { get; internal set; }
        public byte[] KeyTitle { get; internal set; }
        public byte[] IvTitle { get; internal set; }
        public TmdInfo TmdInfo { get; internal set; }
        public Content[] Contents => this.TmdInfo?.Content;
        public SignedStatus SignedStatus { get; internal set; }
        internal bool IsComplete { get; set; }
        public bool GeneratedTicket { get; internal set; }
        public bool GeneratedCert { get; internal set; }

        public byte[] Key { get; internal set; }

        internal SiData()
        {
        }
        internal SiData(byte[] key, IndexFile idx, byte[] fst)
        {
            byte[] tik = idx.Additional.FirstOrDefault(a => a.Extension.ToLower() == ".tik" || a.FileName.ToLower().StartsWith("tik"))?.Data;
            byte[] crt = idx.Additional.FirstOrDefault(a => a.Extension.ToLower() == ".cert" || a.FileName.ToLower().StartsWith("cetk"))?.Data;
            setup(key, idx.Data, tik, crt, fst);
        }

        internal SiData(byte[] key, byte[] tmd, byte[] tik, byte[] cert, byte[] fst)
        {
            setup(key, tmd, tik, cert, fst);
        }

        private void setup(byte[] key, byte[] tmd, byte[] tik, byte[] cert, byte[] fst)
        {
            this.FileTmd = tmd;
            this.FileCert = cert;
            this.FileTicket = tik;
            this.Key = key;

            if (this.FileTmd != null && this.FileTmd.Read8(0x180) == 0) //version 0 - vWii
            {
                if (this.FileCert != null && this.FileTicket == null && this.FileCert.ReadUInt32B(0) == 0x10001 && this.FileCert.ReadString(WiiUConsts.TicketIssuerOffset, 0x1a) == WiiUConsts.TicketIssuerVWii) //cert with ticket
                {
                    this.FileCetk = this.FileCert; // preserve original combined cetk
                    this.FileTicket = this.FileCert.Read(0, 0x2A4); //copy ticket
                    this.FileCert = this.FileCert.Read(this.FileTicket.Length, this.FileCert.Length - this.FileTicket.Length);
                }
            }
            else //WiiU
            {
                TmdInfo tmpTmdInfo = new TmdInfo(this.FileTmd);
                string issuer = tmpTmdInfo.IsWiped ? WiiUConsts.TicketIssuer.Rot13Words() : WiiUConsts.TicketIssuer;

                if (this.FileCert != null && this.FileTicket == null && this.FileCert.ReadUInt32B(0) == 0x10004 && this.FileCert.ReadString(WiiUConsts.TicketIssuerOffset, 0x1a) == issuer) //cert with ticket
                {
                    this.FileCetk = this.FileCert; // preserve original combined cetk
                    this.FileTicket = this.FileCert.Read(0, 0x350); //copy ticket
                    this.FileCert = this.FileCert.Read(this.FileTicket.Length, this.FileCert.Length - this.FileTicket.Length);
                }

                byte[] commonKey = tmpTmdInfo.IsWiped ? WiiGc.WiiConsts.NKitWipeCommonKey : WiiUConsts.KeyCommon;

                if (this.FileCert == null)
                {
                    byte[][] certs = [(byte[])WiiUConsts.CertCa03.Clone(), (byte[])WiiUConsts.CertCp0b.Clone(), (byte[])WiiUConsts.CertXs0c.Clone()];
                    if (tmpTmdInfo.IsWiped)
                    {
                        foreach (byte[] crt in certs)
                            CertValidator.ResignCert(crt, null, new RSAParameters());
                    }
                    this.FileCert = certs[0].Concat(certs[1]).Concat(certs[2]).ToArray();
                    this.GeneratedCert = true;
                }

                if (this.FileTicket == null)
                {
                    this.Key ??= TmdInfo.GenerateEncryptedKey(tmpTmdInfo.TitleId.ToString("X16"), fst, commonKey);
                    if (this.Key != null)
                    {
                        this.FileTicket = TmdInfo.CreateTicket(tmpTmdInfo.TitleId, this.Key, issuer);
                        this.GeneratedTicket = true;
                    }
                }
                else if (this.Key == null)
                    this.Key = TmdInfo.GetTicketEncryptedKey(this.FileTicket);
            }
        }

        internal void Complete(ImageHeader header)
        {
            this.IsComplete = true;
            this.TmdInfo = new TmdInfo(this.FileTmd);
            if (this.FileTicket != null)
            {
                this.IvTitle = this.FileTicket.Read(WiiUConsts.TicketTitleIdOffset, 0x10);
                this.IvTitle.WriteUInt64B(8, 0);
            }
            this.TitleId = this.TmdInfo.TitleId;

            if (this.TmdInfo.Version == 0)
            {
                header.KeyCommon = WiiUConsts.KeyCommonVWii;
                header.KeyCommonDev = new byte[0x10];
            }

            if (this.FileTicket != null)
            {
                byte[] key = this.TmdInfo.IsRetail ? header.KeyCommon : header.KeyCommonDev;
                if (this.TmdInfo.IsWiped)
                    key = WiiConsts.NKitWipeCommonKey;
                this.KeyTitle = WiiUSecurity.DecryptHashless(this.FileTicket, null, WiiUConsts.TicketKeyOffset, 0x10, key, this.IvTitle);
            }

            this.Validate();
            PartitionInfo part = header.Partitions?.FirstOrDefault(a => a.WiiUTitleId == this.TitleId);
            if (part != null)
                part.WiiUSiData = this;
        }

        internal CertValidator CreateCertValidator()
        {
            TmdInfo ti = this.TmdInfo;
            SignedData ticket = new SignedData(this.FileTicket);
            SignedData tmd = new SignedData(this.FileTmd.Read(0, ti.TmdHeaderSize));
            List<SignedData> certs = new List<SignedData>();
            using (MemoryStream ms = new MemoryStream(this.FileCert))
            {
                long pos;
                while ((pos = ms.Position) < ms.Length) //split the certs
                    certs.Add(new SignedDataCert(ms) { Offset = (int)pos });
            }
            if (ti.CertData != null) //CDN certs TMDs have certs appended
            {
                using (MemoryStream ms = new MemoryStream(ti.CertData))
                {
                    while (ms.Position < ms.Length) //extra tmd certs - split the certs
                        certs.Add(new SignedDataCert(ms));
                }
            }

            return new CertValidator(
                Nanook.NKit.Nintendo.WiiGc.WiiConsts.PublicKeyModulus,
                Nanook.NKit.Nintendo.WiiGc.WiiConsts.PublicKeyModulusRvtR,
                Nanook.NKit.Nintendo.WiiGc.WiiConsts.PublicKeyExponent,
                ticket, tmd, certs, null);
        }

        internal void Validate()
        {
            if (this.FileTicket == null)
            {
                this.SignedStatus = SignedStatus.MissingTicket;
                return;
            }

            TmdInfo ti = this.TmdInfo;
            CertValidator cv = CreateCertValidator();

            SignedStatus s = cv.Validate(this.TmdInfo?.IsRetail ?? true, true); //validate tik and tmd
            if (s == SignedStatus.Valid) //if valid test the tmd content hashes
            {
                using (SHA256 sha = SHA256.Create())
                {
                    if (!ti.Hash.Equals(0, sha.ComputeHash(this.FileTmd, ti.TmdHeaderSize, ti.TmdContentOffset - ti.TmdHeaderSize), 0, ti.Hash.Length))
                        s = SignedStatus.InvalidTmdHash;
                    else if (ti.ContentGroups.Any(g => !g.Hash.Equals(0, sha.ComputeHash(this.FileTmd, g.ContentOffset, g.ContentSize), 0, g.Hash.Length)))
                        s = SignedStatus.InvalidTmdHash;
                }
            }
            this.SignedStatus = s;
        }

    }

}