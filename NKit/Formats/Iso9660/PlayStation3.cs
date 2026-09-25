using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class PlayStation3DiscRegion
    {
        public long Offset;
        public long Size;
        public bool Encrypted;
        public override string ToString() => $"{Encrypted} {Offset:X} {Size:X}";
    }

    /// <summary>
    /// Type used to define all known PS3 disc regions
    /// </summary>
    internal enum Region
    {
        // Null region
        NONE = 0x00,

        // Japan and Asia
        Asia = 0x01, // BLAS/BCAS serials
        Japan = Asia, // BLJM/BCJS serials
        Korea = Asia, // BLKS/BCKS serials

        // USA and Canada (NA)
        NorthAmerica = 0x03,
        USA = NorthAmerica,
        Canada = NorthAmerica,

        // Europe, Middle East, and Africa (EMEA)
        Europe = 0x04,
        MiddleEast = Europe,
        Africa = Europe,

        // Australia and New Zealand (Oceania)
        Australia = 0x06, // Some releases use Region.Europe instead
        NewZealand = Australia, // Some releases use Region.Europe instead

        // Brazil and Latin America (LATAM)
        LatinAmerica = 0x09, // e.g. Portuguese + Spanish release
        Brazil = LatinAmerica,
        // Mexico is often Region.NorthAmerica

        // Russia and Eastern Europe
        EasternEurope = 0x0A, // e.g. Russian + Polish language release
        Russia = EasternEurope,
        // Poland is Region.Europe
    }

    internal class PlayStation3
    {
        private const int _secSize = 0x800;
        private byte[] _initialKey;
        public PlayStation3DiscRegion[] Regions;

        public bool IsEncrypted { get; internal set; }
        public bool EncryptionTestSuccess { get; private set; }
        public bool IsEncrypted3k3y { get; private set; }
        public bool Has3k3yHeader { get; private set; }

        public bool HasKey => (this.Key ?? this.Key3k3y) != null;
        public byte[] Key { get; private set; }
        public byte[][] Keys { get; private set; }
        public byte[] Key3k3y { get; private set; }
        public byte[] GetKeyToStore()
        {
            //if the key is different to that from a key file then store it if required
            if (Key != null && (_initialKey == null || !Key.Equals(_initialKey)))
                return Key;
            return null;
        }

        public long[] GetOffsets => Regions.Select(a => a.Offset).ToArray();
        public string Header3k3y { get; private set; }
        public bool KeyIsValid { get; private set; }
        public string KeyTestFileName { get; private set; }
        public long Size { get; }

        public bool Read3k3yKey(byte[] buffer)
        {
            this.Has3k3yHeader = Regex.IsMatch(buffer.ReadString(Consts.Ps33k3yOffsetHeader, 12), "^(En|De)crypted 3K$", RegexOptions.IgnoreCase);
            if (this.Has3k3yHeader)
            {
                this.IsEncrypted3k3y = buffer.ReadString(Consts.Ps33k3yOffsetHeader, 1).ToUpper() != "D";
                using (Aes aes = Aes.Create())
                {
                    aes.Padding = PaddingMode.None;
                    aes.Mode = CipherMode.CBC;
                    aes.Key = Consts.Ps3D1Key;
                    aes.IV = Consts.Ps3D1Iv;
                    this.Key3k3y = new byte[0x10];
                    using (ICryptoTransform ct = aes.CreateEncryptor())
                        ct.TransformBlock(buffer, Consts.Ps33k3yOffsetD1, 0x10, this.Key3k3y, 0);
                }
            }
            return this.Has3k3yHeader;
        }

        internal bool IsEncryptionSupported() => Regions.Length > 1;

        internal bool IsEncryptionSupported(int areaNo)
        {
            if (areaNo >= Regions.Length)
                return false;
            return Regions[areaNo].Encrypted;
        }

        public PlayStation3(byte[] header, byte[] initialKey, long imageSize, byte[][] keys)
        {
            _initialKey = initialKey;
            this.Key = initialKey;
            this.Keys = keys;
            int rgns = ((int)header.ReadUInt32B(0x0) << 1) - 1;

            if (rgns << 2 > _secSize) //invalid
                rgns = 0; //bail out

            Regions = new PlayStation3DiscRegion[Math.Max(0, rgns)]; //regions

            if (this.Regions.Length == 0)
            {
                this.Regions = new PlayStation3DiscRegion[] { new PlayStation3DiscRegion() { Encrypted = false, Offset = 0, Size = imageSize } };
                this.Size = imageSize;
            }
            else
            {
                long offset = 0;
                for (int i = 0; i < Regions.Length; i++)
                {
                    long pos = (header.ReadUInt32B(0xc + (i << 2)) + ((i + 1) % 2)) * (long)_secSize;
                    Regions[i] = new PlayStation3DiscRegion() { Encrypted = i % 2 != 0, Offset = offset, Size = pos - offset };
                    offset = pos;
                }

                PlayStation3DiscRegion last = Regions.Last();
                this.Size = last.Offset + last.Size;
            }
        }

        public long NextArea(long fsOffset, AddressMode mode)
        {
            PlayStation3DiscRegion r = Regions.FirstOrDefault(a => a.Offset >= fsOffset);
            if (r == null)
                return Regions.Last().Offset + Regions.Last().Size;
            return r.Offset;
        }

        /// <summary>Signals + branch of the last <see cref="Test1stEncryptionBlock"/> heuristic run,
        /// for Detail logging by the caller (which owns the Log). Null until first test.</summary>
        internal string LastEncTestDiag { get; private set; }

        internal void Test1stEncryptionBlock(byte[] data, int size, long imageOffset)
        {
            this.IsEncrypted = isEnc(this.Key ?? this.Key3k3y, imageOffset, data, size, out string diag);
            this.LastEncTestDiag = diag;
        }

        internal void Encrypt(long imageOffset, long size, byte[] src, byte[] dst) => encode(imageOffset, size, 0, dst, src, true, null);

        internal void Encrypt(long imageOffset, long size, int offset, byte[] src, byte[] dst) => encode(imageOffset, size, offset, dst, src, true, null);

        internal void Encrypt(long imageOffset, long size, int offset, byte[] src, byte[] dst, byte[] key) => encode(imageOffset, size, offset, dst, src, true, key);

        internal void Decrypt(long imageOffset, long size, byte[] src, byte[] dst) => encode(imageOffset, size, 0, src, dst, false, null);

        internal void Decrypt(long imageOffset, long size, int offset, byte[] src, byte[] dst) => encode(imageOffset, size, offset, src, dst, false, null);

        internal void Decrypt(long imageOffset, long size, int offset, byte[] src, byte[] dst, byte[] key) => encode(imageOffset, size, offset, src, dst, false, key);

        private void encode(long imageOffset, long size, int offset, byte[] enc, byte[] dec, bool encrypt, byte[] key)
        {
            byte[] k = key ?? this.Key ?? this.Key3k3y;
            if (k == null) //if the key is missing then copy the data
            {
                if (encrypt)
                    Array.Copy(dec, enc, size);
                else
                    Array.Copy(enc, dec, size);
                return;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = k;
                byte[] iv = new byte[16];
                int off;

                for (int i = 0; i < size; i += _secSize)
                {
                    off = i + offset;
                    iv.WriteUInt64B(8, (ulong)(imageOffset + off) / (ulong)_secSize);
                    aes.IV = iv;
                    if (encrypt)
                    {
                        using (ICryptoTransform crypto = aes.CreateEncryptor())
                            crypto.TransformBlock(dec, off, _secSize, enc, off);
                    }
                    else
                    {
                        using (ICryptoTransform crypto = aes.CreateDecryptor())
                            crypto.TransformBlock(enc, off, _secSize, dec, off);
                    }
                }
            }
        }

        private static bool isEnc(byte[] key, long imageOffset, byte[] src, int size) => isEnc(key, imageOffset, src, size, out _);

        // Overload that also reports (via 'diag') the signal values and which decision branch fired,
        // so a wrong encrypted/plaintext verdict can be explained from a Detail log. The decision
        // logic is unchanged — 'diag' is pure instrumentation.
        private static bool isEnc(byte[] key, long imageOffset, byte[] src, int size, out string diag)
        {
            if (key == null)
            {
                diag = "no-key -> default encrypted";
                return true; //default, should not be used
            }

            byte[] dec = new byte[size];
            byte[] enc = new byte[size];

            //decrypt
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = key;
                byte[] iv = new byte[16];
                iv.WriteUInt64B(8, (ulong)imageOffset / (ulong)size);
                aes.IV = iv;
                using (ICryptoTransform ct = aes.CreateDecryptor())
                    ct.TransformBlock(src, 0, size, dec, 0);
            }

            //encrypt
            using (Aes aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                aes.Key = key;
                byte[] iv = new byte[16];
                iv.WriteUInt64B(8, (ulong)imageOffset / (ulong)size);
                aes.IV = iv;
                using (ICryptoTransform ct = aes.CreateEncryptor())
                    ct.TransformBlock(src, 0, size, enc, 0);
            }

            ulong[] predefs = new[] { 0x5053334C49434441ul, 0xEB5B94C56177C10Eul, 0x3300963781C5A46Bul, 0x789CAC9B0F5CD2E7ul, 0x97AD7F4F43EB1588ul }; //PS3LIC.. / Blazing Angels 2 - Secret Missions of WWII / Grand Theft Auto - Episodes from Liberty City + GTA IV / Rapala Fishing Frenzy 2009 / Rock Band Track Pack - Classic Rock
            int msbSrc = src.Take(0x10).Count(b => (b & 0x80) == 0);
            int msbEnc = enc.Take(0x10).Count(b => (b & 0x80) == 0);
            int msbDec = dec.Take(0x10).Count(b => (b & 0x80) == 0);
            int consecSrc = testConsecutive(src, size, out int nullsSrc);
            int consecEnc = testConsecutive(enc, size, out int nullsEnc);
            int consecDec = testConsecutive(dec, size, out int nullsDec);
            int scoreSrc = (msbSrc > msbDec && msbSrc > msbEnc ? 1 : 0) + (consecSrc > consecDec && consecSrc > consecEnc ? 1 : 0) + (nullsSrc > nullsDec && nullsSrc > nullsEnc ? 1 : 0);
            int scoreDec = (msbDec > msbSrc && msbDec > msbEnc ? 1 : 0) + (consecDec > consecSrc && consecDec > consecEnc ? 1 : 0) + (nullsDec > nullsSrc && nullsDec > nullsEnc ? 1 : 0);

            string signals = $"off 0x{imageOffset:X} src(msb {msbSrc},consec {consecSrc},nulls {nullsSrc})"
                + $" dec(msb {msbDec},consec {consecDec},nulls {nullsDec}) score src {scoreSrc}/dec {scoreDec}";
            bool decide(out string branch)
            {
                if (predefs.Contains(src.ReadUInt64B(0))) { branch = "predef(src) -> decrypted"; return false; }
                if (predefs.Contains(dec.ReadUInt64B(0))) { branch = "predef(dec) -> encrypted"; return true; }

                if (consecSrc >= 5 || (msbSrc >= 14 && msbSrc > msbDec) || (nullsSrc > 0x30 && nullsSrc > nullsDec)) { branch = "strong src -> decrypted"; return false; }
                if (consecDec >= 5 || (msbDec >= 14 && msbDec > msbSrc) || (nullsDec > 0x30 && nullsDec > nullsSrc)) { branch = "strong dec -> encrypted"; return true; }

                if (consecSrc <= 3 && consecEnc <= 3 && consecSrc > consecDec) { branch = "consec src>dec -> encrypted"; return true; }
                if (consecSrc <= 3 && consecEnc <= 3 && consecDec > consecSrc) { branch = "consec dec>src -> encrypted"; return true; }

                if (scoreSrc > scoreDec) { branch = "score src>dec -> decrypted"; return false; }
                if (scoreDec > scoreSrc) { branch = "score dec>src -> encrypted"; return true; }

                if (msbSrc > msbEnc && msbSrc > msbDec && consecSrc > consecDec) { branch = "msb+consec src -> decrypted"; return false; }
                if (msbDec > msbSrc && msbDec > msbEnc && consecDec > consecSrc) { branch = "msb+consec dec -> encrypted"; return true; }

                if (msbSrc > msbEnc && msbSrc > msbDec) { branch = "msb src -> decrypted"; return false; }
                if (msbDec > msbSrc && msbDec > msbEnc) { branch = "msb dec -> encrypted"; return true; }

                if (consecSrc <= 3 && consecEnc <= 3) { branch = "fallback low-consec -> encrypted"; return true; }

                branch = "fallback -> decrypted";
                return false;
            }

            bool result = decide(out string chosen);
            diag = $"{signals} :: {chosen}";
            return result;
        }

        static int testConsecutive(byte[] data, int size, out int nulls)
        {
            int max = 0;
            int c = 1;
            byte b = data[0];
            nulls = data[0] == 0 ? 1 : 0;
            for (int i = 1; i < size; i++)
            {
                if (data[i] == 0)
                    nulls++;
                if (b == data[i])
                    c++;
                else
                {
                    if (c > max)
                        max = c;
                    b = data[i];
                    c = 1;
                }
            }
            if (c > max)
                return c;
            return max;
        }

        /// <summary>Human-readable trail of the last <see cref="Ps3FileTest"/> key brute-force, for
        /// Detail logging by the caller (which owns the Log). Keys are masked (first 4 ASCII chars +
        /// "...") — never the raw key material. Null until a test with a key search runs.</summary>
        internal string LastKeyTestDiag { get; private set; }

        public void Ps3FileTest(byte[] buffer, long imageOffset, string fileName, int offset)
        {
            this.KeyIsValid = false;
            this.KeyTestFileName = null;
            this.EncryptionTestSuccess = false;
            this.LastKeyTestDiag = null;

            bool isLic = string.Compare(fileName, "lic.dat", true) == 0;
            if (isLic || string.Compare(fileName, "eboot.bin", true) == 0)
            {
                this.KeyTestFileName = fileName;
                if ((isLic && buffer.ReadString(offset, 6) == "PS3LIC") ||
                    (!isLic && (buffer.ReadString(0, 3) == "SCE" || buffer.Read(0, 0x30).Count(a => a == 0x00) >= 0x10)))
                {
                    this.IsEncrypted = false;
                    this.EncryptionTestSuccess = true;
                    this.LastKeyTestDiag = $"{fileName}: plaintext (magic present) -> decrypted, no key search";
                }
                else
                {
                    byte[] wipedKey = new byte[16];

                    // Try current key and 3k3y key first
                    byte[][] tryKeys = new[] { this.Key, this.Key3k3y, wipedKey }.Where(k => k != null).ToArray();
                    byte[][] allTried = tryKeys.Concat(this.Keys ?? Array.Empty<byte[]>()).ToArray();

                    bool found = false;
                    int tried = 0;
                    System.Diagnostics.Stopwatch keySw = System.Diagnostics.Stopwatch.StartNew();
                    foreach (byte[] key in allTried)
                    {
                        tried++;
                        byte[] dec = new byte[0x800];
                        this.Decrypt(imageOffset, 0x800, offset, buffer, dec, key);

                        if (isLic)
                        {
                            if (dec.ReadString(0, 6) == "PS3LIC")
                            {
                                this.Key = key;
                                found = true;
                                break;
                            }
                        }
                        else
                        {
                            if (dec.ReadString(0, 3) == "SCE" || dec.Read(0, 0x30).Count(a => a == 0x00) >= 0x10)
                            {
                                this.Key = key;
                                found = true;
                                break;
                            }
                        }
                    }

                    //if (!found)
                    //    throw new HandledException($"Decryption of {fileName} failed: Invalid key");

                    keySw.Stop();
                    this.IsEncrypted = true;
                    if (found)
                    {
                        this.EncryptionTestSuccess = true;
                        this.KeyIsValid = true;
                        this.LastKeyTestDiag = $"{fileName}: encrypted; matched key {Nanook.NKit.LogScopes.MaskKey(this.Key)} at candidate {tried}/{allTried.Length} in {keySw.ElapsedMilliseconds}ms";
                    }
                    else
                        this.LastKeyTestDiag = $"{fileName}: encrypted; no key matched after {allTried.Length} candidate{(allTried.Length == 1 ? "" : "s")} in {keySw.ElapsedMilliseconds}ms";
                }
            }
        }

        // Parse a PS3_DISC.SFB header into its key/value fields (big-endian offsets/sizes). Used by
        // the PS3 IRD fix (Ps3FixAsIso) and the extract-to-IRD step (FixExtractPs3Step).
        public static Dictionary<string, string> ReadDiscSfb(byte[] b)
        {
            //int version = b.ReadUInt16B(0x4);
            int pos = 0x20;
            string field = b.ReadStringToNull(pos, 0x10);
            Dictionary<string, string> sfbValues = new Dictionary<string, string>();
            while (field != "")
            {
                int o = (int)b.ReadUInt32B(pos + 0x10);
                int sz = (int)b.ReadUInt32B(pos + 0x14);
                sfbValues.Add(field, b.ReadStringToNull(o, sz));
                pos += 0x20;
                field = b.ReadStringToNull(pos);
            }
            return sfbValues;
        }

        // Parse a PARAM.SFO into its key/value fields (little-endian tables). Used by the PS3 IRD
        // fix (Ps3FixAsIso) and the extract-to-IRD step (FixExtractPs3Step).
        public static Dictionary<string, string> ReadParamSfo(byte[] b)
        {
            //int version = (int)b.ReadUInt32L(0x4);
            int keyTableStart = (int)b.ReadUInt32L(0x8);
            int dataTableStart = (int)b.ReadUInt32L(0xc);
            Dictionary<string, string> sfoValues = new Dictionary<string, string>();
            uint paramCount = b.ReadUInt32L(0x10);
            // Parse parameter metadata
            short[] keyOffset = new short[paramCount];
            short[] dataFormat = new short[paramCount];
            int[] dataLength = new int[paramCount];
            int[] dataTotal = new int[paramCount];
            int[] dataOffset = new int[paramCount];
            int pos = 0x14;
            for (int i = 0; i < paramCount; i++)
            {
                keyOffset[i] = (short)b.ReadUInt16L(pos + 0x0);
                dataFormat[i] = (short)b.ReadUInt16L(pos + 0x2);
                dataLength[i] = (int)b.ReadUInt32L(pos + 0x4);
                dataTotal[i] = (int)b.ReadUInt32L(pos + 0x8);
                dataOffset[i] = (int)b.ReadUInt32L(pos + 0xc);
                pos += 0x10;
            }

            for (int i = 0; i < paramCount; i++)
            {
                int pos2 = keyTableStart + keyOffset[i];
                int sz = ((i == paramCount - 1) ? dataTableStart - keyTableStart : keyOffset[i + 1]) - keyOffset[i];
                string k = b.ReadStringToNull(pos2, sz);
                string v;
                pos2 = dataTableStart + dataOffset[i];
                if (dataFormat[i] == 0x0004)
                    v = b.ReadString(pos2, dataLength[i], Encoding.UTF8);
                else if (dataFormat[i] == 0x0404)
                    v = b.ReadUInt32L(pos2).ToString();
                else //if (dataFormat[i] == 0x0204)
                    v = b.ReadStringToNull(Encoding.UTF8, pos2, dataLength[i]);
                sfoValues.Add(k, v);
            }

            return sfoValues;
        }
    }

}