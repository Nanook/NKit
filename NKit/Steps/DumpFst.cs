using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nanook.NKit
{
    /// <summary>
    /// Decrypts and dumps the FST binary from a 00000000.app so two FSTs
    /// can be compared directly.  Title key is derived via NKit's GenerateRawKey.
    /// </summary>
    public static class DumpFst
    {
        public static void Run(string appPath, string tikPath, string outTxtPath)
        {
            byte[] tik = File.ReadAllBytes(tikPath);
            byte[] enc = File.ReadAllBytes(appPath);

            // Read title ID from ticket at 0x1DC
            ulong titleId = 0;
            for (int i = 0; i < 8; i++) titleId = (titleId << 8) | tik[0x1DC + i];

            // Extract the encrypted title key directly from the ticket (works for both
            // real disc tickets and NKit synthetic tickets).
            byte[] encTitleKey = TmdInfo.GetTicketEncryptedKey(tik);

            if (encTitleKey == null)
            {
                // Fallback to synthetic derivation
                Console.WriteLine("Ticket key extraction failed — using GenerateRawKey fallback");
                byte[] rawKey2 = TmdInfo.GenerateRawKey(titleId.ToString("X16"));
                encTitleKey = TmdInfo.EncryptKey(rawKey2, titleId, WiiUConsts.KeyCommon);
            }

            // Decrypt the title key using the WiiU common key
            byte[] encKey = TmdInfo.DecryptKey(encTitleKey, titleId, WiiUConsts.KeyCommon);

            Console.WriteLine($"TitleId:  {titleId:X16}");
            Console.WriteLine($"TitleKey (enc): {Hex(encTitleKey)}");
            Console.WriteLine($"TitleKey (raw): {Hex(encKey)}");

            // ── Decrypt (hashless AES-CBC, IV = content-index as BE u16, rest zeros) ─
            // Use WiiUSecurity.DecryptFst which applies the correct IV scheme.
            byte[] dec = new byte[enc.Length];
            WiiUSecurity.DecryptFst(enc, dec, enc.Length, encKey);

            string rawPath = outTxtPath + ".bin";
            File.WriteAllBytes(rawPath, dec);
            Console.WriteLine($"Decrypted FST: {rawPath}");

            // ── Dump ─────────────────────────────────────────────────────────────
            var sb = new StringBuilder();
            DumpFstBinary(dec, sb);
            File.WriteAllText(outTxtPath, sb.ToString());
            Console.WriteLine($"FST dump:      {outTxtPath}");
        }

        static void DumpFstBinary(byte[] d, StringBuilder sb)
        {
            uint magic      = R32(d, 0x00);
            uint multiplier = R32(d, 0x04);
            uint nContents  = R32(d, 0x08);
            byte hashDis    = d[0x0C];

            sb.AppendLine("=== FST Header ===");
            sb.AppendLine($"  Magic:        0x{magic:X8}  ({(magic == 0x46535400 ? "FST\\0 OK" : "BAD")})");
            sb.AppendLine($"  Multiplier:   {multiplier}");
            sb.AppendLine($"  ContentCount: {nContents}");
            sb.AppendLine($"  HashDisabled: {hashDis}");
            sb.AppendLine();

            int fstRecLen = 0x20;
            sb.AppendLine($"=== Content Records ({nContents}) ===");
            sb.AppendLine($"  {"Idx",-4} {"RawOff",-10} {"RawSz",-10} {"TitleId",-18} {"GroupId",-8} {"Type"}");
            for (int i = 0; i < (int)nContents; i++)
            {
                int o     = (i + 1) * fstRecLen;
                uint rawOff = R32(d, o + 0x00);
                uint rawSz  = R32(d, o + 0x04);
                ulong tid   = R64(d, o + 0x08);
                uint grp    = R32(d, o + 0x10);
                byte type   = d[o + 0x14];
                sb.AppendLine($"  [{i,-3}] 0x{rawOff:X8}  0x{rawSz:X8}  {tid:X16}  0x{grp:X4}    {type} ({(AppType)type})");
            }
            sb.AppendLine();

            // File/directory entries follow content records
            int entryBase = (int)((nContents + 1) * fstRecLen);
            if (entryBase + 0x10 > d.Length) { sb.AppendLine("ERROR: entryBase beyond data"); return; }

            uint nEntries = R32(d, entryBase + 0x08); // root val3 = total entry count
            if (nEntries > 100000) { sb.AppendLine($"ERROR: unreasonable entry count {nEntries}"); return; }

            int namesBase = entryBase + (int)(nEntries * 0x10);

            sb.AppendLine($"=== File Entries ({nEntries}) ===");
            sb.AppendLine($"  {"Idx",-5} {"T",-5} {"Sect",-5} {"RawOff/End",-14} {"Size",-12} {"Path"}");

            var stack = new Stack<(string path, uint endIdx)>();
            stack.Push(("", uint.MaxValue));
            string curPath = "";

            for (uint i = 0; i < nEntries; i++)
            {
                int o      = entryBase + (int)(i * 0x10);
                uint val1  = R32(d, o + 0x00);
                uint val2  = R32(d, o + 0x04);
                uint val3  = R32(d, o + 0x08);
                ushort perm = R16(d, o + 0x0C);
                ushort sect = R16(d, o + 0x0E);

                bool isDir  = (val1 >> 24) == 0x01;
                int nameOff = (int)(val1 & 0x00FFFFFF);
                string name = ReadStr(d, namesBase + nameOff);

                while (stack.Count > 1 && i >= stack.Peek().endIdx)
                {
                    stack.Pop();
                    curPath = stack.Count > 1 ? stack.Peek().path : "";
                }

                string full = i == 0 ? "/" : (curPath.Length == 0 ? name : $"{curPath}/{name}");

                if (isDir && i > 0)
                {
                    stack.Push((full, val3));
                    curPath = full;
                }

                if (isDir)
                    sb.AppendLine($"  [{i,-4}] DIR   s={sect,-4} end={val3,-10}               {full}/");
                else
                {
                    long byteOff = (long)val2 * multiplier;
                    sb.AppendLine($"  [{i,-4}] FILE  s={sect,-4} off=0x{byteOff:X8}(raw={val2})  sz=0x{val3:X8}  {full}");
                }
            }
        }

        static uint   R32(byte[] d, int o) => ((uint)d[o]<<24)|((uint)d[o+1]<<16)|((uint)d[o+2]<<8)|d[o+3];
        static ulong  R64(byte[] d, int o) => ((ulong)R32(d,o)<<32)|R32(d,o+4);
        static ushort R16(byte[] d, int o) => (ushort)((d[o]<<8)|d[o+1]);
        static string Hex(byte[] b)        => BitConverter.ToString(b).Replace("-","");
        static string ReadStr(byte[] d, int o)
        {
            if (o >= d.Length) return "?";
            int e = o; while (e < d.Length && d[e] != 0) e++;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding("Shift-JIS").GetString(d, o, e - o);
        }
    }
}
