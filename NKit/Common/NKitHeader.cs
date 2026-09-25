using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    public enum HeaderKeyType { None, Aes, AesWithIv }
    [Flags]
    public enum HeaderFlags
    {
        Size = 0b000000001,
        Crc32 = 0b000000010,
        Md5 = 0b000000100,
        Sha1 = 0b000001000,
        Xxhash64 = 0b000010000,
        Key = 0b000100000,
        Encrypted = 0b001000000,
        ExtraData = 0b010000000,
        IndexFile = 0b100000000,
        Version1 = Crc32 | Md5 | Sha1 | Xxhash64
    }

    internal class NKitHeader
    {
        private bool _isEncrypted;
        public int Version { get; }
        public bool HasSize { get; }
        public bool HasCrc32 { get; }
        public bool HasMd5 { get; }
        public bool HasSha1 { get; }
        public bool HasXxhash64 { get; }
        public HeaderKeyType KeyType { get; }
        public bool HasExtraData { get; } //not supported yet
        public bool HasIndexFile { get; } //not supported yet
        public Checksums Checksums { get; set; }
        public byte[] Key { get; set; }
        public long Size { get; set; }


        public HeaderFlags Flags { get; private set; }
        public int Length { get; private set; }

        public bool IsEncrypted
        {
            get => _isEncrypted;
            set
            {
                _isEncrypted = value;
                if (_isEncrypted)
                    this.Flags |= HeaderFlags.ExtraData;
                else
                    this.Flags &= ~HeaderFlags.Encrypted;
            }
        }

        public static int GetHeaderLen(byte[] first14Bytes, int offset) => GetHeaderLen(first14Bytes, offset, out _);

        public static int GetHeaderLen(byte[] first14Bytes, int offset, out int version)
        {
            version = 0;
            string hdr = Encoding.ASCII.GetString(first14Bytes, offset, 8);
            Match m = Regex.Match(hdr, "^NKIT *v([0-9]+)$");
            if (m.Success)
            {
                version = int.Parse(m.Groups[1].Value);
                if (version == 1)
                    return calcHeaderSize(version, HeaderFlags.Version1, 0);
                else
                    return first14Bytes.ReadUInt16B(offset + 0x8);
            }
            return 0;
        }

        public byte[] ToArray()
        {
            byte[] header = new byte[calcHeaderSize(this.Version, this.Flags, this.Key == null ? 0 : this.Key.Length)];

            int pos = 0;
            header.WriteString(pos, 8, $"NKIT  v{this.Version}");
            pos += 8;

            if (this.Version >= 2)
            {
                header.WriteUInt16B(pos, (ushort)header.Length);
                pos += 2;
                header.WriteUInt16B(pos, (ushort)this.Flags);
                pos += 2;
                if ((this.Flags & HeaderFlags.Size) != 0)
                {
                    header.WriteUInt64B(pos, (ulong)this.Size); //long image size
                    pos += 8; //long image size
                }
            }

            if ((this.Flags & HeaderFlags.Crc32) != 0)
            {
                header.WriteUInt32B(pos, this.Checksums.Crc);
                pos += 0x4; //Has Crc32
            }
            if ((this.Flags & HeaderFlags.Md5) != 0)
            {
                header.Write(pos, this.Checksums.Md5, 0x10);
                pos += 0x10; //Has MD5
            }
            if ((this.Flags & HeaderFlags.Sha1) != 0)
            {
                header.Write(pos, this.Checksums.Sha1, 0x14);
                pos += 0x14; //Has SHA1
            }
            if ((this.Flags & HeaderFlags.Xxhash64) != 0)
            {
                header.WriteUInt64B(pos, this.Checksums.XxHash);
                pos += 0x8; //Has XXHASH64
            }

            if (this.Version >= 2)
            {
                if ((this.Flags & HeaderFlags.Key) != 0)
                {
                    if (this.Key == null)
                        this.Key = new byte[0];

                    header.Write8(pos, (byte)this.Key.Length); //Has Key + 1 byte for key len and 1 for type
                    pos += 1;
                    header.Write8(pos, (byte)this.KeyType);
                    pos += 1;
                    header.Write(pos, this.Key, this.Key.Length);
                    pos += this.Key.Length;
                }
            }

            return header;
        }

        public NKitHeader(byte[] header, int offset)
        {
            int version;
            this.Checksums = new Checksums();
            this.Length = GetHeaderLen(header, offset, out version);
            if (this.Length != 0)
            {
                if (header.Length - offset < this.Length)
                    throw new HandledException("NKit Header length is too short");

                int pos = offset + 0xa;
                HeaderFlags flags = (HeaderFlags)header.ReadUInt16B(pos);
                pos += 2;
                this.Flags = flags;

                if ((flags & HeaderFlags.Size) != 0)
                {
                    this.Size = (long)header.ReadUInt64B(pos); //long image size
                    this.HasSize = true;
                    pos += 8; //long image size
                }
                if ((flags & HeaderFlags.Crc32) != 0)
                {
                    this.HasCrc32 = true;
                    this.Checksums.Crc = header.ReadUInt32B(pos);
                    pos += 0x4; //Has Crc32
                }
                if ((flags & HeaderFlags.Md5) != 0)
                {
                    this.HasMd5 = true;
                    this.Checksums.Md5 = header.Read(pos, 0x10);
                    pos += 0x10; //Has MD5
                }
                if ((flags & HeaderFlags.Sha1) != 0)
                {
                    this.HasSha1 = true;
                    this.Checksums.Sha1 = header.Read(pos, 0x14);
                    pos += 0x14; //Has SHA1
                }
                if ((flags & HeaderFlags.Xxhash64) != 0)
                {
                    this.HasXxhash64 = true;
                    this.Checksums.XxHash = header.ReadUInt64B(pos);
                    pos += 0x8; //Has XXHASH64
                }
                if ((flags & HeaderFlags.Key) != 0)
                {
                    int keylen = header.Read8(pos); //Has Key + 1 byte for key len and 1 for type
                    pos += 1;
                    this.KeyType = (HeaderKeyType)header.Read8(pos);
                    pos += 1;
                    this.Key = header.Read(pos, keylen);
                    pos += keylen;
                }
            }
        }

        public NKitHeader(int version, bool hasSize, bool hasCrc32, bool hasMd5, bool hasSha1, bool hasXxhash64, HeaderKeyType keyType, int keyLen, bool isEncrypted, bool hasExtraData, bool hasIndexFile)
        {
            this.Checksums = new Checksums();
            Version = version;
            HasSize = hasSize;
            HasCrc32 = hasCrc32;
            HasMd5 = hasMd5;
            HasSha1 = hasSha1;
            HasXxhash64 = hasXxhash64;
            IsEncrypted = isEncrypted;
            KeyType = keyType;
            HasExtraData = hasExtraData;
            HasIndexFile = hasIndexFile;

            HeaderFlags flags = 0;
            if (hasSize)
                flags |= HeaderFlags.Size;
            if (hasCrc32)
                flags |= HeaderFlags.Crc32;
            if (hasMd5)
                flags |= HeaderFlags.Md5;
            if (hasSha1)
                flags |= HeaderFlags.Sha1;
            if (hasXxhash64)
                flags |= HeaderFlags.Xxhash64;
            if (keyType != HeaderKeyType.None)
                flags |= HeaderFlags.Key;
            if (isEncrypted)
                flags |= HeaderFlags.Encrypted;
            if (hasExtraData)
                flags |= HeaderFlags.ExtraData;
            if (hasIndexFile)
                flags |= HeaderFlags.IndexFile;
            this.Flags = flags;
            this.Length = calcHeaderSize(this.Version, this.Flags, keyLen);
        }

        private static int calcHeaderSize(int version, HeaderFlags flags, int keyLen)
        {
            int hdrSize = 0x8; //NKitHdr+ver

            if (version >= 2)
                hdrSize += 0x2 + 0x2; //HdrSize + bits
            if ((flags & HeaderFlags.Size) != 0)
                hdrSize += 0x8; //long image size
            if ((flags & HeaderFlags.Crc32) != 0)
                hdrSize += 0x4; //Has Crc32
            if ((flags & HeaderFlags.Md5) != 0)
                hdrSize += 0x10; //Has MD5
            if ((flags & HeaderFlags.Sha1) != 0)
                hdrSize += 0x14; //Has SHA1
            if ((flags & HeaderFlags.Xxhash64) != 0)
                hdrSize += 0x8; //Has XXHASH64
            if ((flags & HeaderFlags.Key) != 0)
                hdrSize += keyLen + 2; //Has Key + 1 byte for key len and 1 for type
            return hdrSize;
        }

    }
}