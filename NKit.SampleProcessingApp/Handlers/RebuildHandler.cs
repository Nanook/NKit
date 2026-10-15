using Nanook.NKit.Builder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// Pure NKit passthrough disc rebuild with optional file replacements.
    ///
    /// GEOMETRY-BASED READING (the key fix)
    /// ──────────────────────────────────────
    /// Iterating block.Items and using item.Size misses ~4,000 files per Wii disc.
    /// After NKit's scrubbing re-analysis pass the internal ISectionData.File is null
    /// for some items, so item.Size == 0 through the public API even though the bytes
    /// ARE in the section buffer.
    ///
    /// Fix: use the frozen FST (built at the first FileSystem section) to compute each
    /// file's position within every subsequent section:
    ///
    ///     offsetInSection = f.FsOffset - block.FsOffset
    ///
    /// then call block.Read(offsetInSection, bytesHere, stream) directly.
    ///
    /// REPLACEMENTS
    /// ─────────────
    /// Pass a Dictionary&lt;string,string&gt; (lowercased disc-path → local file path) to
    /// replace or add files.  Replacement files are written from local disk once;
    /// their section data is skipped.
    /// </summary>
    internal sealed class RebuildHandler : ISectionHandler
    {
        /// <summary>
        /// Loads a replacements manifest written by DiscBuilder.
        /// Format: one entry per line — <c>discpath|localpath</c>
        /// </summary>
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

        public string Name => "rebuild";

        // ── Output ─────────────────────────────────────────────────────────────────

        private readonly string _outputPath;
        private FileStream      _outStream;
        private WiiDiscBuilder  _disc;
        private WiiPartitionBuilder _partition;

        // ── Replacements ────────────────────────────────────────────────────────────

        private readonly Dictionary<string, string> _replacements; // discPath → localPath

        // ── System-file accumulation ────────────────────────────────────────────────

        private Dictionary<string, (MemoryStream Stream, long FsSize)> _pendingSys;
        private Dictionary<string, byte[]> _completedSys;
        private byte[] _partitionHeaderBytes;
        private int    _fstFileCount;
        private long   _fstNameBytes;
        private bool   _partitionStarted;

        // ── FST geometry table ──────────────────────────────────────────────────────

        private List<FstEntry> _fstSorted;   // sorted ascending by FsOffset

        // ── File accumulation ───────────────────────────────────────────────────────

        private readonly Dictionary<long, OpenFile> _active = new Dictionary<long, OpenFile>();
        private readonly HashSet<long>              _done   = new HashSet<long>();
        private readonly HashSet<string>  _writtenReplacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Stats ───────────────────────────────────────────────────────────────────

        private int _sectionsTotal;
        private int _filesWritten;
        private int _filesIncomplete;
        private int _filesZero;
        private int _replacementsWritten;

        // ── Log ─────────────────────────────────────────────────────────────────────

        private StreamWriter _log;

        private void L(string msg)
        {
            Console.WriteLine(msg);
            _log?.WriteLine(msg);
        }

        // ── Constructor ─────────────────────────────────────────────────────────────

        public RebuildHandler(string outputDir, string imageName,
                              Dictionary<string, string> replacements = null)
        {
            Directory.CreateDirectory(outputDir);
            string baseName  = Path.GetFileNameWithoutExtension(imageName);
            _outputPath      = Path.Combine(outputDir, baseName + "_rebuilt.iso");
            string logPath   = Path.Combine(outputDir, "rebuild_debug.log");
            _replacements    = replacements ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _log = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
            L($"[rebuild] output      : {_outputPath}");
            L($"[rebuild] replacements: {_replacements.Count}");
            L($"[rebuild] started     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            L($"[rebuild] mode        : FST-geometry direct reading");
        }

        // ── ISectionHandler ──────────────────────────────────────────────────────────

        public void OnSection(IReadOnlySection block)
        {
            _sectionsTotal++;

            switch (block.Type)
            {
                case AreaType.ImageHeader:
                    {
                        int sz = (int)block.FsSize;
                        byte[] hdr = block.ReadBytes(0, sz);
                        _outStream = new FileStream(_outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 0x200000);
                        _disc = new WiiDiscBuilder(_outStream);
                        _disc.SetHeader(hdr);
                        _disc.ApplyWiiDiscHeaderDefaults();
                        L($"[rebuild] ImageHeader: {sz} bytes  id='{_disc.DiscId6}'");
                    }
                    break;

                case AreaType.PartitionHeader:
                    {
                        int sz = (int)block.FsSize;
                        _partitionHeaderBytes = block.ReadBytes(0, sz);
                        L($"[rebuild] PartitionHeader: {sz} bytes");
                    }
                    break;

                case AreaType.FileSystem:
                    {
                        PartitionType pt = block.AreaFileSystem?.Type ?? PartitionType.Game;
                        if (pt != PartitionType.Game) return;
                        ProcessFsSection(block);
                    }
                    break;
            }
        }

        // ── FileSystem section ────────────────────────────────────────────────────────

        private void ProcessFsSection(IReadOnlySection block)
        {
            bool isFirst = (block.AreaOffset == 0);

            // ── One-time setup at the first FileSystem section ─────────────────────
            if (isFirst)
            {
                _completedSys  = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                _pendingSys    = new Dictionary<string, (MemoryStream, long)>(StringComparer.OrdinalIgnoreCase);
                _fstSorted     = new List<FstEntry>();
                _fstFileCount  = 0;
                _fstNameBytes  = 0;

                IFileSystemView primary = block.AreaFileSystem?.Primary;
                if (primary != null)
                {
                    _fstFileCount = primary.FileCount;
                    int dataCount = 0, zeroCount = 0;

                    for (int i = 0; i < primary.FileCount; i++)
                    {
                        IFsFile f = primary.File(i);
                        _fstNameBytes += (f.Name?.Length ?? 0) + 1;
                        if (f.IsSystemFile) continue;

                        string dp = DiscPath(f);
                        _fstSorted.Add(new FstEntry(f, dp));
                        if (f.FsSize == 0) zeroCount++; else dataCount++;
                    }
                    _fstSorted.Sort((a, b) => a.FsOffset.CompareTo(b.FsOffset));
                    L($"[rebuild] FST: {_fstFileCount} entries — {dataCount} data, {zeroCount} zero-size");
                }

                // Sys-file size registration (for DOL multi-section accumulation)
                foreach (IReadOnlySectionItem item in block.Items)
                {
                    IFsFile f = item.FsFile;
                    if (f?.IsSystemFile != true) continue;
                    if (!_pendingSys.ContainsKey(f.Name))
                        _pendingSys[f.Name] = (new MemoryStream((int)Math.Min(f.FsSize, 64 * 1024 * 1024)), f.FsSize);
                }
            }

            // ── Accumulate sys-file slices (DOL spans multiple sections) ──────────
            if (_pendingSys != null && !_partitionStarted)
            {
                foreach (IReadOnlySectionItem item in block.Items)
                {
                    IFsFile f = item.FsFile;
                    if (f == null || !f.IsSystemFile || item.Size <= 0) continue;
                    if (!_pendingSys.TryGetValue(f.Name, out var acc)) continue;

                    var tmp = new MemoryStream((int)item.Size);
                    block.Read((int)item.FileOffsetInSection, (int)item.Size, tmp);
                    acc.Stream.Write(tmp.ToArray(), 0, (int)item.Size);
                }

                if (_pendingSys.Count > 0 && _pendingSys.All(kv => kv.Value.Stream.Length >= kv.Value.FsSize))
                    StartPartition();
            }

            if (!_partitionStarted) return;

            // ── Write zero-size FST entries (once, before any data files) ──────────
            if (isFirst)
            {
                foreach (var e in _fstSorted.Where(e => e.FsSize == 0))
                {
                    if (_done.Contains(e.FsOffset)) continue;
                    _partition.WriteFile(Stream.Null, e.Name, e.Path, 0L);
                    _done.Add(e.FsOffset);
                    _filesZero++;
                }
                if (_filesZero > 0) L($"[rebuild] Zero-size entries: {_filesZero}");
            }

            // ── Replacement files ──────────────────────────────────────────────────
            // Write replacement files the first time their FST entry is encountered.
            // A replacement file can appear in any section (determined by where its
            // FST entry overlaps in disc-offset space).
            if (_replacements.Count > 0 && _fstSorted != null)
            {
                long sectionStart = block.FsOffset;
                long sectionEnd   = sectionStart + block.FsSize;
                foreach (var e in _fstSorted)
                {
                    if (e.FsOffset >= sectionEnd) break;
                    if (!_replacements.TryGetValue(e.DiscPath, out string localPath)) continue;
                    if (!_writtenReplacements.Add(e.DiscPath)) continue; // already written

                    try
                    {
                        long sz = new FileInfo(localPath).Length;
                        using FileStream rs = File.OpenRead(localPath);
                        _partition.WriteFile(rs, e.Name, e.Path, sz);
                        _done.Add(e.FsOffset); // mark so geometry pass skips it
                        _replacementsWritten++;
                        L($"[rebuild] replacement: {e.DiscPath}  ({sz:N0} bytes)");
                    }
                    catch (Exception ex)
                    {
                        L($"[rebuild] replacement FAILED: {e.DiscPath}  {ex.Message}");
                    }
                }
            }

            // ── Geometry-based source file reading ─────────────────────────────────
            long secStart = block.FsOffset;
            long secEnd   = secStart + block.FsSize;

            foreach (FstEntry e in _fstSorted)
            {
                if (e.FsSize <= 0) continue;

                long fileEnd = e.FsOffset + e.FsSize;
                if (fileEnd  <= secStart) continue;
                if (e.FsOffset >= secEnd) break;

                if (_done.Contains(e.FsOffset)) continue;
                if (_replacements.ContainsKey(e.DiscPath)) continue; // handled above

                long chunkStart      = Math.Max(e.FsOffset, secStart);
                long chunkEnd        = Math.Min(fileEnd, secEnd);
                long bytesHere       = chunkEnd - chunkStart;
                long offsetInSection = chunkStart - secStart;

                if (bytesHere <= 0) continue;

                if (!_active.TryGetValue(e.FsOffset, out OpenFile of))
                {
                    of = new OpenFile
                    {
                        Name   = e.Name,
                        Path   = e.Path,
                        FsSize = e.FsSize,
                        Stream = e.FsSize > 32L * 1024 * 1024
                                 ? (Stream)new FileStream(
                                       Path.Combine(Path.GetTempPath(), $"tghost_rb_{e.FsOffset:X16}.tmp"),
                                       FileMode.Create, FileAccess.ReadWrite, FileShare.None, 0x80000)
                                 : new MemoryStream((int)e.FsSize),
                    };
                    _active[e.FsOffset] = of;
                }

                block.Read((int)offsetInSection, (int)bytesHere, of.Stream);
                of.Written += bytesHere;

                if (of.Written >= e.FsSize)
                {
                    of.Stream.Position = 0;
                    _partition.WriteFile(of.Stream, of.Name, of.Path, of.FsSize);
                    of.Stream.Dispose();
                    DeleteTemp(of.Stream);
                    _active.Remove(e.FsOffset);
                    _done.Add(e.FsOffset);
                    _filesWritten++;
                }
            }
        }

        // ── Partition startup ──────────────────────────────────────────────────────

        private void StartPartition()
        {
            var sys = new Dictionary<string, byte[]>(_completedSys, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _pendingSys)
            {
                if (!sys.TryGetValue(kv.Key, out byte[] ex) || ex.Length < kv.Value.FsSize)
                    sys[kv.Key] = kv.Value.Stream.ToArray()[..(int)kv.Value.FsSize];
                kv.Value.Stream.Dispose();
            }
            _pendingSys = null;

            bool ok = true;
            if (!sys.TryGetValue("__boot.bin",     out byte[] boot))   { L("[rebuild] ERROR: no __boot.bin");   ok = false; }
            if (!sys.TryGetValue("__bi2.bin",       out byte[] bi2))    { L("[rebuild] ERROR: no __bi2.bin");    ok = false; }
            if (!sys.TryGetValue("__apploader.img", out byte[] appldr)) { L("[rebuild] ERROR: no __apploader"); ok = false; }
            if (!sys.TryGetValue("__main.dol",      out byte[] dol))    { L("[rebuild] ERROR: no __main.dol");   ok = false; }
            if (!ok) return;

            if (boot!.Length >= 6)
            {
                string id = System.Text.Encoding.ASCII.GetString(boot, 0, 6).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(id)) _disc.OverrideDiscId6(id);
            }

            long fstSize = EstimateFstSize(_fstFileCount, _fstNameBytes);
            _partition = _disc.AddPartition(_disc.DiscId6, _partitionHeaderBytes, 0x58000L, PartitionType.Game);
            _partition.SetDataHeader(boot, bi2!, appldr!, dol, fstSize);
            _partitionStarted = true;
            L($"[rebuild] Partition started  id='{_disc.DiscId6}'  dol=0x{dol!.Length:X}  fst~0x{fstSize:X}  FstOffset=0x{_partition.FstOffset:X}");
        }

        private static long EstimateFstSize(int entries, long nameBytes)
        {
            long sz = 4 + (entries * 12L) + nameBytes + 0x8000;
            return (sz + 31) & ~31L;
        }

        private static void DeleteTemp(Stream s)
        {
            if (s is FileStream fs)
                try { File.Delete(fs.Name); } catch { }
        }

        private static string DiscPath(IFsFile f)
        {
            string path = (f.Path ?? string.Empty).Trim('/');
            return (path.Length == 0 ? f.Name : path + "/" + f.Name).ToLowerInvariant();
        }

        // ── Completion ──────────────────────────────────────────────────────────────

        public void Completed(NKitTaskResults results)
        {
            foreach (var kv in _active)
            {
                OpenFile of = kv.Value;
                L($"[rebuild] INCOMPLETE 0x{kv.Key:X8} '{of.Path}/{of.Name}' {of.Written}/{of.FsSize}");
                if (_partition != null && of.Written > 0)
                    try { of.Stream.Position = 0; _partition.WriteFile(of.Stream, of.Name, of.Path, of.Written); _filesWritten++; } catch { }
                of.Stream?.Dispose();
                DeleteTemp(of.Stream);
                _filesIncomplete++;
            }
            _active.Clear();

            if (_disc != null && _partition != null)
                try { _disc.Complete(); L("[rebuild] disc.Complete() OK"); }
                catch (Exception ex) { L($"[rebuild] disc.Complete() FAILED: {ex.Message}"); }

            _outStream?.Flush();
            _outStream?.Dispose();

            int dataFiles = _fstSorted?.Count(e => e.FsSize > 0) ?? 0;
            L("");
            L("[rebuild] ════ Summary ════");
            L($"[rebuild]   FST data files     : {dataFiles}");
            L($"[rebuild]   FST zero-size       : {_filesZero}");
            L($"[rebuild]   Files written       : {_filesWritten}");
            L($"[rebuild]   Replacements        : {_replacementsWritten}");
            L($"[rebuild]   Incomplete          : {_filesIncomplete}");
            L($"[rebuild]   Output              : {_outputPath}");
            L($"[rebuild]   Finished            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            _log?.Flush();
            _log?.Dispose();
        }

        // ── Private types ────────────────────────────────────────────────────────────

        private sealed class OpenFile
        {
            public string Name, Path;
            public long   FsSize, Written;
            public Stream Stream;
        }

        private sealed class FstEntry
        {
            public IFsFile FsFile  { get; }
            public string  DiscPath{ get; }
            public string  Name    => FsFile.Name;
            public string  Path    => FsFile.Path;
            public long    FsOffset=> FsFile.FsOffset;
            public long    FsSize  => FsFile.FsSize;
            public FstEntry(IFsFile f, string dp) { FsFile = f; DiscPath = dp; }
        }
    }
}
