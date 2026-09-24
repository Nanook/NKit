using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiU
{
    internal class TmdInfo
    {
        private static string[] _Data;

        static TmdInfo()
        {
            _Data = Encoding.ASCII.GetString(WiiUConsts.Data).Split('|');
        }

        public TmdInfo(byte[] data)
        {
            this.SigType = (int)data.ReadUInt32B(0x0);
            this.Issuer = data.ReadString(WiiUConsts.TicketIssuerOffset, 0x1a);
            this.IsWiped = this.Issuer.StartsWith("Root".Rot13Words());
            this.IsRetail = this.Issuer != (this.IsWiped ? WiiUConsts.CommonDevIssuer.Rot13Words() : WiiUConsts.CommonDevIssuer);
            this.Version = data.Read8(0x180);

            if (this.Version == 1) //the version supported by jwud/jnus and cdecrypt
            {
                this.CaCrlVersion = data.Read8(0x181);
                this.SignerCrlVersion = data.Read8(0x182);
                this.SysVersion = (int)data.ReadUInt64B(0x184);
                this.TitleId = data.ReadUInt64B(0x18c);
                this.TitleType = data.ReadUInt16B(0x194);
                this.GroupId = data.ReadUInt16B(0x198);
                this.AccessRights = data.ReadUInt32B(0x1d8);
                this.TitleVersion = (int)data.ReadUInt16B(0x1dc);
                this.TotalContents = (int)data.ReadUInt16B(0x1de);
                this.BootContent = data.ReadUInt16B(0x1e0);
                this.Hash = data.Read(0x1e4, 0x20);
            }
            else if (this.Version == 0) //vWii - minimum support to allow it to error out later
            {
                this.TotalContents = (int)data.ReadUInt16B(0x1de);
                this.TitleId = data.ReadUInt64B(0x18c);
            }
            else
            {
                throw new HandledException($"TMD format version {this.Version} is not supported");
            }

            List<Content> c = new List<Content>();
            int offset = this.TmdContentOffset;
            for (int i = 0; i < this.TotalContents; i++)
            {
                long id = (int)data.ReadUInt32B(offset);
                int index = (int)data.ReadUInt16B(offset + 0x04);
                int type = data.ReadUInt16B(offset + 0x06);
                long size = (long)data.ReadUInt64B(offset + 0x08);
                c.Add(new Content(id, index, (AppContentType)type, size, data.Read(offset + 0x10, 20)));
                offset += TmdContentItemLength;
            }
            this.Content = c.ToArray();

            if (offset < data.Length)
                CertData = data.Read(offset, data.Length - offset);

            List<ContentGroup> grps = new List<ContentGroup>();
            int idx = 0;
            offset = TmdHeaderSize;
            for (int i = 0; i < 40; i++) //up to 40 entries
            {
                int count = offset + TmdContentItemLength >= data.Length ? 0 : Math.Min(c.Count - idx, (int)data.ReadUInt32B(offset));
                if (count <= 0)
                    break;
                grps.Add(new ContentGroup(data.Read(offset + 0x4, 20), c.Skip(idx).Take(count).ToArray(), offset, TmdContentOffset + (idx * TmdContentItemLength), count * TmdContentItemLength));
                idx += count;
                offset += count += TmdContentItemLength;
            }
            ContentGroups = grps.ToArray();
        }

        public ContentGroup[] ContentGroups { get; private set; }
        public Content[] Content { get; private set; }
        public byte[] CertData { get; private set; }
        public int TotalContents { get; private set; }
        public int TmdHeaderSize => 0x204;
        public int TmdContentOffset => this.Version == 0 ? 0x1e4 : 0xb04;
        public int TmdContentItemLength => this.Version == 0 ? 0x24 : 0x30;
        public byte[] Hash { get; private set; }
        public byte Version { get; private set; }
        public byte CaCrlVersion { get; private set; }
        public byte SignerCrlVersion { get; private set; }
        public ulong TitleId { get; private set; }
        public int TitleVersion { get; private set; }
        public int MinorVersion { get; private set; }
        public long SysVersion { get; private set; }
        public long SigType { get; private set; }
        public int TitleType { get; private set; }
        public string Issuer { get; private set; }
        public bool IsWiped { get; private set; }
        public int GroupId { get; private set; }
        public long AccessRights { get; private set; }
        public int BootContent { get; private set; }
        public int ContentsCount { get; private set; }
        public bool IsRetail { get; }

        public static byte[] CreateTicket(ulong titleId, byte[] key, string issuer)
        {
            byte[] tik = new byte[0x350];
            tik.WriteUInt16B(0x0, 0x1);
            tik.WriteUInt16B(0x2, 0x4);
            for (int i = 0x4; i < 0x104; i += 8)
                tik.WriteUInt64B(i, 0xD15EA5ED15ABE11Aul);
            tik.WriteString(WiiUConsts.TicketIssuerOffset, WiiUConsts.TicketIssuer.Length, issuer);
            for (int i = 0x180; i < 0x1BC; i += 4)
                tik.WriteUInt32B(i, 0xFEEDFACEu);
            tik.Write8(0x1bc, 1);

            tik.WriteUInt64B(WiiUConsts.TicketTitleIdOffset, titleId);
            byte[] iv = new byte[0x10];
            iv.WriteUInt64B(0, titleId);
            key = (byte[])key.Clone(); //don't mofify the existing key
            tik.Write(WiiUConsts.TicketKeyOffset, key);

            tik.WriteUInt16B(0x220, 0x1);
            tik.WriteUInt16B(0x2A4, 0x1);
            tik.WriteUInt16B(0x2A6, 0x14);
            tik.WriteUInt16B(0x2AA, 0xAC);
            tik.WriteUInt16B(0x2AE, 0x14);
            tik.WriteUInt16B(0x2B0, 0x1);
            tik.WriteUInt16B(0x2B2, 0x14);
            tik.WriteUInt16B(0x2BA, 0x28);
            tik.WriteUInt16B(0x2BE, 0x1);
            tik.WriteUInt16B(0x2C2, 0x84);
            tik.WriteUInt16B(0x2C6, 0x84);
            tik.WriteUInt16B(0x2C8, 0x3);
            for (int i = 0x2D0; i < 0x2F0; i += 4)
                tik.WriteUInt32B(i, 0xFFFFFFFFu);

            return tik;
        }

        public static byte[] GetTicketEncryptedKey(byte[] ticket)
        {
            byte[] iv = new byte[0x10];
            iv.WriteUInt64B(0, ticket.ReadUInt64B(WiiUConsts.TicketTitleIdOffset)); //titleId
            return ticket.Read(WiiUConsts.TicketKeyOffset, 0x10);
        }

        public static byte[] DecryptKey(byte[] key, ulong titleId, byte[] commonKey)
        {
            byte[] iv = new byte[0x10];
            iv.WriteUInt64B(0, titleId);
            return WiiUSecurity.DecryptHashless(key, null, 0, key.Length, commonKey, iv);
        }

        public static byte[] EncryptKey(byte[] key, ulong titleId, byte[] commonKey)
        {
            byte[] iv = new byte[0x10];
            iv.WriteUInt64B(0, titleId);
            return WiiUSecurity.EncryptHashless(key, new byte[key.Length], 0, key.Length, commonKey, iv);
        }

        public static byte[] GenerateEncryptedKey(string titleId, byte[] fst, byte[] commonKey)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] dec = null;
                for (int i = 1; i < _Data.Length; i++)
                {
                    byte[] key = i < _Data.Length - 2 ? genKey(md5, _Data[i], titleId) : _Data[i].HexToBytes();
                    dec = WiiUSecurity.DecryptHashless(fst, null, 0, 0x10, key, new byte[16]); //blank IV
                    if (dec.ReadString(0, 3) == "FST")
                        return EncryptKey(key, ulong.Parse(titleId, System.Globalization.NumberStyles.HexNumber), commonKey);
                }
                //try the Wipe key
                dec = WiiUSecurity.DecryptHashless(fst, null, 0, 0x10, WiiGc.WiiConsts.NKitWipeTitleKey, new byte[16]); //blank IV
                if (dec.ReadString(0, 3) == "FST")
                    return EncryptKey(WiiGc.WiiConsts.NKitWipeTitleKey, ulong.Parse(titleId, System.Globalization.NumberStyles.HexNumber), WiiGc.WiiConsts.NKitWipeCommonKey);

                return null;
            }
        }

        private static byte[] genKey(MD5 md5, string pwd, string titleId)
        {
            byte[] secret = md5.ComputeHash((_Data[0] + titleId.Substring(2, titleId.Length - 2)).HexToBytes());
            int iterations = 20;

#if NET10_0_OR_GREATER
            // Use the static Pbkdf2 method on newer .NET to avoid Rfc2898DeriveBytes instance warnings
            byte[] passwordBytes = Encoding.UTF8.GetBytes(pwd);
            return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, secret, iterations, HashAlgorithmName.SHA1, 16);
#else
            using (Rfc2898DeriveBytes x = new Rfc2898DeriveBytes(pwd, secret, iterations, HashAlgorithmName.SHA1))
                return x.GetBytes(16);
#endif
        }

    }
}