using Nanook.NKit;
using Nanook.NKit.Builder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// Builds a Wii disc ISO via <see cref="WiiDiscBuilder"/> + <see cref="WiiPartitionBuilder"/>
    /// from the output of NKit's built-in Extract task.
    ///
    /// USAGE
    /// ──────
    /// 1. Run NKit with task=extract — it writes 100% of disc files to a directory:
    ///      [extractDir]/DATA/sys/   ← boot.bin, bi2.bin, apploader.img, main.dol
    ///      [extractDir]/DATA/files/ ← all game files
    /// 2. Capture ImageHeader and DATA partition header via processor.OnSection.
    /// 3. Call Build(imageHeader, partitionHeader, extractDir) — reads from disk, builds disc.
    ///
    /// The Extract task uses SkipToImageOffsetSet internally to reach every FST entry,
    /// giving 100% file coverage without any NKit injection.
    /// </summary>
    public sealed class WiiDiscRebuildHandler
    {
        // ── Output ──────────────────────────────────────────────────────────────────

        private readonly string _outputPath;
        private FileStream        _outStream;
        public  WiiDiscBuilder    Disc       { get; private set; }
        public  WiiPartitionBuilder Partition { get; private set; }

        // ── Config ────────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, string> _replacements;

        // ── Stats ────────────────────────────────────────────────────────────────────

        public int FilesWritten        { get; private set; }
        public int ReplacementsWritten { get; private set; }

        // ── Log ──────────────────────────────────────────────────────────────────────

        private readonly string _logPath;
        private void L(string msg)
        {
            Console.WriteLine(msg);
            try { File.AppendAllText(_logPath, msg + "\n"); } catch { }
        }

        // ── Constructor ──────────────────────────────────────────────────────────────

        public WiiDiscRebuildHandler(string outputPath, Dictionary<string, string> replacements = null)
        {
            _outputPath   = outputPath;
            _replacements = replacements ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _logPath      = Path.Combine(Path.GetDirectoryName(outputPath), "rebuild_debug.log");
            File.WriteAllText(_logPath,
                $"[rebuild] WiiDiscRebuildHandler started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            L($"[rebuild] output      : {outputPath}");
            L($"[rebuild] replacements: {_replacements.Count}");
        }

        // ── Main entry point ─────────────────────────────────────────────────────────

        /// <summary>
        /// Build the output disc from the NKit-extracted directory.
        /// </summary>
        /// <param name="imageHeader">
        ///   Raw disc header bytes captured from processor.OnSection when
        ///   block.Type == AreaType.ImageHeader.
        /// </param>
        /// <param name="partitionHeader">
        ///   Raw DATA partition header bytes captured from processor.OnSection when
        ///   block.Type == AreaType.PartitionHeader (use the last captured value —
        ///   DATA partition always follows UPDATE in standard Wii disc layout).
        /// </param>
        /// <param name="extractedDir">
        ///   Root directory written by NKit's Extract task.  Must contain DATA/sys/
        ///   and DATA/files/ subdirectories.
        /// </param>
        public void Build(byte[] imageHeader, byte[] partitionHeader, string extractedDir)
        {
            if (imageHeader == null || partitionHeader == null || extractedDir == null)
            {
                L("[rebuild] ERROR: missing imageHeader, partitionHeader, or extractedDir");
                return;
            }

            string sysDir   = Path.Combine(extractedDir, "DATA", "sys");
            string filesDir = Path.Combine(extractedDir, "DATA", "files");

            // ── 1. Read sys files ─────────────────────────────────────────────────────
            byte[] boot   = readSysFile(sysDir, "boot.bin");
            byte[] bi2    = readSysFile(sysDir, "bi2.bin");
            byte[] appldr = readSysFile(sysDir, "apploader.img");
            byte[] dol    = readSysFile(sysDir, "main.dol");

            if (boot == null || bi2 == null || appldr == null || dol == null)
            {
                L($"[rebuild] ERROR: missing sys file(s) in {sysDir}");
                L($"[rebuild]   boot.bin      : {(boot != null ? "ok" : "MISSING")}");
                L($"[rebuild]   bi2.bin       : {(bi2 != null ? "ok" : "MISSING")}");
                L($"[rebuild]   apploader.img : {(appldr != null ? "ok" : "MISSING")}");
                L($"[rebuild]   main.dol      : {(dol != null ? "ok" : "MISSING")}");
                return;
            }

            L($"[rebuild] sys: boot={boot.Length} bi2={bi2.Length} appldr={appldr.Length} dol={dol.Length}");

            // Allow replacement of main.dol (e.g. patched DOL from external tool)
            if (_replacements.TryGetValue("__main.dol", out string dolPath) && File.Exists(dolPath))
            {
                L($"[rebuild] sys replacement: __main.dol → {dolPath}");
                dol = File.ReadAllBytes(dolPath);
            }

            // ── 2. Open output and setup disc ─────────────────────────────────────────
            _outStream = new FileStream(_outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 0x200000);
            Disc = new WiiDiscBuilder(_outStream);
            Disc.SetHeader(imageHeader);
            Disc.ApplyWiiDiscHeaderDefaults();

            // Override disc ID from boot.bin (authoritative for this disc)
            if (boot.Length >= 6)
            {
                string gameId = Encoding.ASCII.GetString(boot, 0, 6).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(gameId))
                    Disc.OverrideDiscId6(gameId);
            }

            // ── 3. Estimate FST size ──────────────────────────────────────────────────
            int  fstFileCount = Directory.Exists(filesDir)
                ? Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories).Length
                : 0;
            long fstNameBytes = Directory.Exists(filesDir)
                ? estimateNameBytes(filesDir)
                : 0;

            // Include any new injection files that don't exist in the extract output
            int injectCount = _replacements.Count(kv =>
                !kv.Key.StartsWith("__") &&
                !File.Exists(Path.Combine(filesDir, kv.Key.Replace('/', Path.DirectorySeparatorChar))));

            long fstReserve = computeFstReserve(fstFileCount + injectCount, fstNameBytes);
            L($"[rebuild] disc='{Disc.DiscId6}'  files={fstFileCount}  inject={injectCount}  fstReserve=0x{fstReserve:X}");

            // ── 4. Add DATA partition ─────────────────────────────────────────────────
            Partition = Disc.AddPartition(Disc.DiscId6, partitionHeader, 0x58000L, PartitionType.Game);
            Partition.SetDataHeader(boot, bi2, appldr, dol, fstReserve);

            // ── 5. Write game files (sorted for deterministic output) ─────────────────
            if (Directory.Exists(filesDir))
            {
                var allFiles = Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(filesDir.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/'))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string discRelPath in allFiles)
                {
                    string key      = discRelPath.ToLowerInvariant();
                    string diskPath = Path.Combine(filesDir, discRelPath.Replace('/', Path.DirectorySeparatorChar));
                    string srcPath  = diskPath;

                    // Apply replacement if one exists for this disc-relative path
                    if (_replacements.TryGetValue(key, out string replPath) && File.Exists(replPath))
                    {
                        srcPath = replPath;
                        ReplacementsWritten++;
                        L($"[rebuild] replace: {discRelPath}  → {replPath}");
                    }

                    string fn = Path.GetFileName(discRelPath);
                    string fp = Path.GetDirectoryName(discRelPath)?.Replace('\\', '/') ?? "";
                    long   sz = new FileInfo(srcPath).Length;

                    using FileStream fs = File.OpenRead(srcPath);
                    Partition.WriteFile(fs, fn, fp, sz);
                    written.Add(key);
                    FilesWritten++;
                }

                // ── 6. Inject files not present in the extracted source ───────────────
                int injected = 0;
                foreach (KeyValuePair<string, string> kv in _replacements)
                {
                    if (kv.Key.StartsWith("__")) continue; // sys-file replacement, handled above
                    if (written.Contains(kv.Key.ToLowerInvariant())) continue;
                    if (!File.Exists(kv.Value)) continue;

                    string fn = Path.GetFileName(kv.Key);
                    string fp = Path.GetDirectoryName(kv.Key)?.Replace('\\', '/') ?? "";
                    long   sz = new FileInfo(kv.Value).Length;
                    using FileStream fs = File.OpenRead(kv.Value);
                    Partition.WriteFile(fs, fn, fp, sz);
                    ReplacementsWritten++;
                    injected++;
                    L($"[rebuild] inject (new): {kv.Key}  ({sz:N0} bytes)");
                }
                if (injected > 0)
                    L($"[rebuild] injected {injected} new file(s) with no source FST entry");
            }

            // ── 7. Complete ───────────────────────────────────────────────────────────
            try
            {
                Disc.Complete();
                L("[rebuild] Disc.Complete() OK");
            }
            catch (Exception ex) { L($"[rebuild] Disc.Complete() FAILED: {ex.Message}"); }

            _outStream.Flush();
            _outStream.Dispose();

            L("");
            L("[rebuild] ════ Summary ════");
            L($"[rebuild]   Files written  : {FilesWritten}");
            L($"[rebuild]   Replacements   : {ReplacementsWritten}");
            L($"[rebuild]   Output         : {_outputPath}");
            L($"[rebuild]   Finished       : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        // ── Private helpers ───────────────────────────────────────────────────────────

        private byte[] readSysFile(string sysDir, string name)
        {
            string path = Path.Combine(sysDir, name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        private static long estimateNameBytes(string filesDir)
        {
            long total = 0;
            foreach (string f in Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories))
                total += Path.GetFileName(f).Length + 1;
            foreach (string d in Directory.GetDirectories(filesDir, "*", SearchOption.AllDirectories))
                total += Path.GetFileName(d).Length + 1;
            return total;
        }

        private static long computeFstReserve(int entries, long nameBytes)
        {
            const long MinReserve = 1 * 1024 * 1024; // 1 MB minimum
            long computed = 4 + (entries * 12L) + nameBytes + 0x8000;
            return ((Math.Max(computed, MinReserve)) + 31) & ~31L;
        }

        // ── Static helpers ────────────────────────────────────────────────────────────

        public static Dictionary<string, string> LoadReplacements(string filePath)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return dict;
            foreach (string line in File.ReadAllLines(filePath))
            {
                int sep = line.IndexOf('|');
                if (sep > 0)
                    dict[line.Substring(0, sep).Trim()] = line.Substring(sep + 1).Trim();
            }
            return dict;
        }
    }
}
