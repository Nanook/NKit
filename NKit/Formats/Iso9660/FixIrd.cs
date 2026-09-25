using Nanook.GrindCore.GZip;
using System.IO;
using System.Security.Cryptography;
using NGC = Nanook.GrindCore;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class FixIrd : FixFileItem
    {
        public static FixIrd Read(string filename)
        {
            try
            {
                using (FileStream fs = File.OpenRead(filename))
                {
                    long sz = fs.Length;
                    using (GZipStream gz = new GZipStream(fs, NGC.CompressionOptions.DefaultDecompress()))
                    {
                        using (BinaryReader br = new BinaryReader(gz))
                        {
                            if (br.ReadBytes(0x4).ReadString(0x0, 0x4) != "3IRD")
                                return null;

                            FixIrd ird = new FixIrd(filename, sz);
                            ird.Version = br.ReadByte();
                            ird.TitleId = br.ReadBytes(0x9).ReadStringToNull(0);
                            ird.Title = br.ReadString();
                            ird.SysVersion = br.ReadBytes(0x4).ReadStringToNull(0);
                            ird.DiscVersion = br.ReadBytes(0x5).ReadStringToNull(0);
                            ird.AppVersion = br.ReadBytes(0x5).ReadStringToNull(0);
                            return ird;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public FixIrd(string filename, long length) : base(filename, "IRD", length, 0)
        {
        }

        public int Version { get; set; }
        public string TitleId { get; set; }
        public string Title { get; set; }
        public string SysVersion { get; set; }
        public string DiscVersion { get; set; }
        public string AppVersion { get; set; }

        public uint Uid { get; set; } //not for irdkitirds
        public uint ImageCrc { get; set; } // only for irdkit irds
        public bool HasImageCrc { get; set; }
        public byte[] Header { get; set; }
        public byte[] Footer { get; set; }

        public byte[][] RegionHashes { get; set; }
        public long[] FileKeys { get; set; }
        public byte[][] FileHashes { get; set; }

        public int ExtraConfig { get; set; }
        public int Attachments { get; set; }
        public byte[] Pic { get; set; }
        public byte[] D1 { get; set; }
        public byte[] D2 { get; set; }
        public byte[] Key { get; set; }


        public void Populate()
        {
            using (FileStream fs = File.OpenRead(this.Filename))
            {
                using (GZipStream gz = new GZipStream(fs, NGC.CompressionOptions.DefaultDecompress()))
                {
                    using (BinaryReader br = new BinaryReader(gz))
                    {

                        if (br.ReadBytes(0x4).ReadString(0x0, 0x4) != "3IRD")
                            throw new HandledException($"Not a valid IRD: {this.Filename}");

                        //skip already read header stuff
                        br.ReadByte();
                        br.ReadBytes(0x9);
                        br.ReadString();
                        br.ReadBytes(0x4);
                        br.ReadBytes(0x5);
                        br.ReadBytes(0x5);

                        if (this.Version == 7)
                            this.Uid = br.ReadBytes(0x4).ReadUInt32L(0);

                        byte[] tmp = br.ReadBytes((int)br.ReadBytes(0x4).ReadUInt32L(0));
                        using (GZipStream gzH = new GZipStream(new MemoryStream(tmp), NGC.CompressionOptions.DefaultDecompress()))
                        {
                            using (MemoryStream tmpH = new MemoryStream())
                            {
                                gzH.CopyTo(tmpH);
                                this.Header = tmpH.ToArray();
                            }
                        }

                        tmp = br.ReadBytes((int)br.ReadBytes(0x4).ReadUInt32L(0));
                        using (GZipStream gzH = new GZipStream(new MemoryStream(tmp), NGC.CompressionOptions.DefaultDecompress()))
                        {
                            using (MemoryStream tmpH = new MemoryStream())
                            {
                                gzH.CopyTo(tmpH);
                                this.Footer = tmpH.ToArray();
                            }
                        }

                        byte regionCount = br.ReadByte();
                        this.RegionHashes = new byte[regionCount][];
                        for (int i = 0; i < regionCount; i++)
                            this.RegionHashes[i] = br.ReadBytes(0x10);

                        uint fileCount = br.ReadBytes(0x4).ReadUInt32L(0);
                        this.FileKeys = new long[fileCount];
                        this.FileHashes = new byte[fileCount][];
                        for (int i = 0; i < fileCount; i++)
                        {
                            this.FileKeys[i] = (long)br.ReadBytes(0x8).ReadUInt64L(0);
                            this.FileHashes[i] = br.ReadBytes(0x10);
                        }

                        this.ExtraConfig = br.ReadBytes(0x2).ReadUInt16L(0);
                        this.Attachments = br.ReadBytes(0x2).ReadUInt16L(0);

                        if (this.Version >= 9)
                            this.Pic = br.ReadBytes(0x73);

                        this.D1 = br.ReadBytes(0x10);
                        this.D2 = br.ReadBytes(0x10);

                        if (this.Version < 9)
                            this.Pic = br.ReadBytes(0x73);

                        if (this.Version > 7)
                        {
                            this.HasImageCrc = this.ExtraConfig >= 1;
                            if (this.HasImageCrc)
                                this.ImageCrc = br.ReadBytes(0x4).ReadUInt32L(0);
                            else
                                this.Uid = br.ReadBytes(0x4).ReadUInt32L(0);
                        }

                        base.Crc = br.ReadBytes(0x4).ReadUInt32L(0); //ird of file up to this point

                        this.Key = GenerateKey(true, this.D1);
                    }
                }
            }

        }

        public static byte[] GenerateD1(byte[] key)
        {
            byte[] ret = new byte[0x10];
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = Consts.Ps3D1Key;
                aes.IV = Consts.Ps3D1Iv;
                using (ICryptoTransform ct = aes.CreateDecryptor())
                    ct.TransformBlock(key, 0, 0x10, ret, 0);
            }
            return ret;
        }

        public static byte[] GenerateD2(byte[] key)
        {
            byte[] ret = new byte[0x10];
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = Consts.Ps3D2Key;
                aes.IV = Consts.Ps3D2Iv;
                using (ICryptoTransform ct = aes.CreateEncryptor())
                    ct.TransformBlock(key, 0, 0x10, ret, 0);
            }
            return ret;
        }

        public static byte[] GenerateKey(bool isD1, byte[] val)
        {
            byte[] ret = new byte[0x10];
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = isD1 ? Consts.Ps3D1Key : Consts.Ps3D2Key;
                aes.IV = isD1 ? Consts.Ps3D1Iv : Consts.Ps3D2Iv;
                using (ICryptoTransform ct = aes.CreateEncryptor())
                    ct.TransformBlock(val, 0, 0x10, ret, 0);
            }
            return ret;
        }
    }

}