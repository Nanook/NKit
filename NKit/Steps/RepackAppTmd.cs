using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    /// <summary>
    /// Takes an existing AppTmd folder (e.g. from a WUX extract) whose .app and .h3
    /// files are already correct, and regenerates title.tmd / title.tik / title.cert
    /// using NKit's own key-derivation and TMD-finalisation code.
    ///
    /// Usage:
    ///   RepackAppTmd.Run("D:\\Temp\\WuxAppTmdFolder", "D:\\Temp\\RepackedOutput");
    ///
    /// The output folder will contain all the original .app/.h3 files plus new
    /// title.tmd / title.tik / title.cert built by our code.  Install that output
    /// on console to verify our tmd/tik/cert generation is correct independently
    /// of the .app content.
    /// </summary>
    public static class RepackAppTmd
    {
        public static void Run(string srcFolder, string outFolder, Dictionary<int, string> swaps = null)
        {
            swaps = swaps ?? new Dictionary<int, string>();
            Directory.CreateDirectory(outFolder);

            // ── 1. Read the existing TMD from the source to extract title metadata ──
            byte[] srcTmd  = File.ReadAllBytes(Path.Combine(srcFolder, "title.tmd"));
            byte[] srcTik  = File.ReadAllBytes(Path.Combine(srcFolder, "title.tik"));
            byte[] srcCert = File.ReadAllBytes(Path.Combine(srcFolder, "title.cert"));

            TmdInfo tmdInfo = new TmdInfo(srcTmd);
            ulong titleId   = tmdInfo.TitleId;
            int   titleVer  = tmdInfo.TitleVersion;

            Console.WriteLine($"TitleId:  {titleId:X16}");
            Console.WriteLine($"Version:  {titleVer}");
            Console.WriteLine($"Contents: {tmdInfo.Content.Length}");

            // ── 2. Derive title key our way ──────────────────────────────────────────
            byte[] rawKey = TmdInfo.GenerateRawKey(titleId.ToString("X16"));
            byte[] encKey = TmdInfo.EncryptKey(rawKey, titleId, WiiUConsts.KeyCommon);

            // ── 3. Build a new TMD matching the source structure ─────────────────────
            byte[] newTmd = TmdInfo.SynthesiseTmd(
                titleId, titleVer, tmdInfo.Content.Length,
                sysVersion: tmdInfo.SysVersion != 0 ? (ulong)tmdInfo.SysVersion : 0x000500101000400AUL,
                groupId:    tmdInfo.GroupId    != 0 ? (ushort)tmdInfo.GroupId
                                                    : (ushort)((titleId >> 8) & 0xFFFF));

            // Copy content records from source TMD (type, index, size)
            // but zero the hashes — FinaliseTmd will recompute them from .h3 files
            for (int i = 0; i < tmdInfo.Content.Length; i++)
            {
                Content c = tmdInfo.Content[i];
                ushort ctype = c.Type.HasFlag(AppContentType.Hashed) ? (ushort)0x2003 : (ushort)0x2001;
                TmdInfo.WriteContentRecord(newTmd, i, (uint)c.ContentId, c.Index, ctype, c.Size);
            }

            SiData newSiData = new SiData(encKey, newTmd, null, null, null);
            newSiData.Complete(new ImageHeader(null));
            var builder = new WiiUAppTmdBuilder(newSiData);

            // ── 4. Feed each .h3 file into the builder so it can finalise the TMD ───
            // WiiUAppTmdBuilder.FinaliseTmd writes SHA1(h3) into the TMD content record.
            // We need to inject the h3 tables directly without re-encrypting the .app files.
            // Use the internal RegisterH3 path via reflection, or recompute SHA1(h3) manually.
            //
            // Since FinaliseTmd reads from _contentHashes which is populated by EncryptHashed,
            // we'll compute SHA1(h3) ourselves and patch the TMD content hash fields directly.
            using SHA1 sha1 = SHA1.Create();

            Console.WriteLine("\nContent hashes from .app/.h3 files:");
            for (int i = 0; i < tmdInfo.Content.Length; i++)
            {
                Content c = tmdInfo.Content[i];
                string appName = $"{c.ContentId:x8}.app";
                string h3Name  = $"{c.ContentId:x8}.h3";
                // Use swapped file if provided, otherwise source folder
                string appPath = swaps.TryGetValue(i, out string swapPath) ? swapPath
                               : Path.Combine(srcFolder, appName);
                string h3Path  = Path.Combine(srcFolder, h3Name); // h3 always from src

                if (!File.Exists(appPath))
                {
                    Console.WriteLine($"  [{i}] {appName} MISSING — skipping");
                    continue;
                }

                if (c.Type.HasFlag(AppContentType.Hashed))
                {
                    if (!File.Exists(h3Path))
                    {
                        Console.WriteLine($"  [{i}] {h3Name} MISSING — skipping");
                        continue;
                    }
                    byte[] h3 = File.ReadAllBytes(h3Path);
                    byte[] h3Hash = sha1.ComputeHash(h3);
                    int recOff = 0xB04 + i * 0x30 + 0x10;
                    Array.Copy(h3Hash, 0, newSiData.FileTmd, recOff, 20);
                    string swapNote = swaps.ContainsKey(i) ? " [SWAPPED]" : "";
                    Console.WriteLine($"  [{i}] {h3Name}  SHA1={BitConverter.ToString(h3Hash).Replace("-","")}{swapNote}");
                }
                else
                {
                    byte[] appBytes = File.ReadAllBytes(appPath);
                    byte[] appHash  = sha1.ComputeHash(appBytes);
                    int recOff = 0xB04 + i * 0x30 + 0x10;
                    Array.Copy(appHash, 0, newSiData.FileTmd, recOff, 20);
                    string swapNote = swaps.ContainsKey(i) ? " [SWAPPED]" : "";
                    Console.WriteLine($"  [{i}] {appName}  SHA1={BitConverter.ToString(appHash).Replace("-","")}{swapNote}");
                }
            }

            // ── 5. Finalise TMD (fills ContentInfo group hashes and ContentInfoHash) ─
            builder.FinaliseTmd();

            // ── 6. Copy .app and .h3 files, write new tmd/tik/cert ──────────────────
            Console.WriteLine($"\nCopying .app and .h3 files to {outFolder}");
            foreach (string f in Directory.GetFiles(srcFolder))
            {
                string name = Path.GetFileName(f);
                if (name == "title.tmd" || name == "title.tik" || name == "title.cert")
                    continue; // we'll write our own
                File.Copy(f, Path.Combine(outFolder, name), overwrite: true);
                Console.WriteLine($"  Copied {name} (from src)");
            }
            // Copy any swapped .app files over the top
            foreach (var (idx, swapPath) in swaps)
            {
                Content c = tmdInfo.Content[idx];
                string destName = $"{c.ContentId:x8}.app";
                File.Copy(swapPath, Path.Combine(outFolder, destName), overwrite: true);
                Console.WriteLine($"  Copied {destName} [SWAPPED from {swapPath}]");
            }

            File.WriteAllBytes(Path.Combine(outFolder, "title.cert"), newSiData.FileCert);
            File.WriteAllBytes(Path.Combine(outFolder, "title.tik"),  newSiData.FileTicket);
            File.WriteAllBytes(Path.Combine(outFolder, "title.tmd"),  newSiData.FileTmd);

            Console.WriteLine($"\nWrote title.cert / title.tik / title.tmd");
            Console.WriteLine($"Output: {outFolder}");

            // ── 7. Compare key fields with source TMD ────────────────────────────────
            Console.WriteLine("\n=== TMD comparison (src vs new) ===");
            byte[] newTmdFinal = newSiData.FileTmd;
            Console.WriteLine($"Size:          src={srcTmd.Length}  new={newTmdFinal.Length}  {(srcTmd.Length==newTmdFinal.Length?"OK":"DIFF")}");

            for (int i = 0; i < tmdInfo.Content.Length; i++)
            {
                int o = 0xB04 + i * 0x30;
                string srcHash = BitConverter.ToString(srcTmd, o+0x10, 20).Replace("-","");
                string newHash = BitConverter.ToString(newTmdFinal, o+0x10, 20).Replace("-","");
                string match   = srcHash == newHash ? "OK" : "DIFF";
                Console.WriteLine($"  [{i}] hash {match}  src={srcHash.Substring(0,16)}..  new={newHash.Substring(0,16)}..");
            }

            // ContentInfoHash at 0x1E4
            string srcCih = BitConverter.ToString(srcTmd, 0x1E4, 32).Replace("-","");
            string newCih = BitConverter.ToString(newTmdFinal, 0x1E4, 32).Replace("-","");
            Console.WriteLine($"  ContentInfoHash: {(srcCih==newCih?"OK":"DIFF")}");
        }
    }
}
