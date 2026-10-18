using System;
using System.Collections.Generic;
using System.IO;

namespace Nanook.NKit.SampleProcessingApp.Handlers
{
    /// <summary>
    /// FILE EXTRACT handler — mirrors the DedupeStep extraction pattern.
    ///
    /// Key insight from DedupeStep.saveFileData:
    ///   - Stream key is f.FsOffset (the file's disc position), NOT path+name.
    ///   - Create/replace stream when si.File.OffsetInItem == 0  (file start).
    ///   - Append to existing stream when OffsetInItem > 0          (continuation).
    ///   - Complete when OffsetInItem + si.File.FsSize == f.FsSize.
    ///
    /// Public API mapping:
    ///   si.File.OffsetInItem → inferred: if key NOT in _open, this is a new start.
    ///   si.File.FsOffset     → item.FileOffsetInSection  (section-relative)
    ///   si.File.FsSize       → item.Size                 (bytes in this section)
    ///   f.FsSize             → item.FsFile.FsSize         (total file size)
    ///
    /// Why FsOffset as key (not path+name)?
    ///   Some FST entries share the same lowercase path+name but live at different
    ///   disc offsets. Using FsOffset guarantees each physical file gets its own stream.
    /// </summary>
    internal sealed class ExtractHandler : ISectionHandler
    {
        private readonly string _outputRoot;

        // Keyed by FsOffset (disc offset) — same as DedupeStep._activeFileStreams.
        private readonly Dictionary<long, OpenFile> _activeStreams =
            new Dictionary<long, OpenFile>();
        // Completed offsets — skip gap-reference items for already-finished files.
        private readonly HashSet<long> _doneOffsets = new HashSet<long>();
        // Map FsOffset → output path for FST entries (populated at area start).
        private readonly Dictionary<long, string> _fsOffsetToPath =
            new Dictionary<long, string>();

        private int _extracted, _incomplete, _zeroCreated, _fromItems;

        public ExtractHandler(string outputDirectory, string imageName)
        {
            string baseName = Path.GetFileNameWithoutExtension(imageName);
            this._outputRoot = Path.Combine(outputDirectory, baseName + "_files");
            Directory.CreateDirectory(this._outputRoot);
        }

        public string Name => "extract";

        public void OnSection(IReadOnlySection block)
        {
            if (block.Type != AreaType.FileSystem) return;
            if (block.AreaFileSystem?.Type != PartitionType.Game) return;

            // ── One-time FST scan: build FsOffset→path map, pre-create zero-size files ─
            if (block.AreaOffset == 0)
            {
                IFileSystemView primary = block.AreaFileSystem?.Primary;
                if (primary != null)
                {
                    int zero = 0, data = 0;
                    for (int i = 0; i < primary.FileCount; i++)
                    {
                        IFsFile f = primary.File(i);
                        if (f.IsSystemFile) continue;
                        Directory.CreateDirectory(this.MapDirectory(f));

                        if (f.FsSize == 0)
                        {
                            zero++;
                            // Pre-create zero-size files (item.Size always 0 for them)
                            if (!_doneOffsets.Contains(f.FsOffset))
                            {
                                string p = this.MapFilePath(f);
                                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                                try { File.WriteAllBytes(p, Array.Empty<byte>()); } catch { }
                                _doneOffsets.Add(f.FsOffset);
                                _extracted++; _zeroCreated++;
                            }
                        }
                        else
                        {
                            data++;
                            // Register FsOffset → output path mapping.
                            // If two entries share the same FsOffset, last writer wins
                            // (same bytes on disc, same content — either path is valid).
                            if (!_fsOffsetToPath.ContainsKey(f.FsOffset))
                                _fsOffsetToPath[f.FsOffset] = this.MapFilePath(f);
                        }
                    }
                    Console.WriteLine($"  [extract] FST: {primary.FileCount} entries " +
                                      $"({data} data, {zero} zero-size)");
                    Console.WriteLine($"  [extract] Pre-created {_zeroCreated} zero-size files");
                }
            }

            // ── Accumulate items — mirrors DedupeStep.saveFileData ───────────────────
            foreach (IReadOnlySectionItem item in block.Items)
            {
                IFsFile fsFile = item.FsFile;
                if (fsFile == null || fsFile.IsSystemFile || item.Size <= 0) continue;

                long fsOffset = fsFile.FsOffset; // disc position = stream key

                if (_doneOffsets.Contains(fsOffset)) continue; // already complete

                // Open stream when OffsetInItem == 0 (file start).
                // We infer OffsetInItem == 0 by: key not yet in _activeStreams.
                if (!_activeStreams.TryGetValue(fsOffset, out OpenFile of))
                {
                    // Use registered path from FST, fall back to deriving from this item.
                    if (!_fsOffsetToPath.TryGetValue(fsOffset, out string outPath))
                        outPath = this.MapFilePath(fsFile);

                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    if (Directory.Exists(outPath)) { _doneOffsets.Add(fsOffset); continue; }
                    try
                    {
                        of = new OpenFile
                        {
                            Stream   = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                                                      FileShare.None, 0x80000),
                            Written  = 0,
                            FullSize = fsFile.FsSize,
                        };
                        _activeStreams[fsOffset] = of;
                    }
                    catch { _doneOffsets.Add(fsOffset); continue; }
                }

                // Write — sequential append (stream position is at end of last write)
                block.Read((int)item.FileOffsetInSection, (int)item.Size, of.Stream);
                of.Written += item.Size;

                // Complete when OffsetInItem + FsSize == FsFile.FsSize
                // = of.Written >= of.FullSize
                if (of.Written >= of.FullSize)
                {
                    of.Stream.Flush(); of.Stream.Dispose();
                    _activeStreams.Remove(fsOffset);
                    _doneOffsets.Add(fsOffset);
                    _extracted++; _fromItems++;
                }
            }
        }

        public void Completed(NKitTaskResults results)
        {
            foreach (var kvp in _activeStreams)
            {
                kvp.Value.Stream?.Flush(); kvp.Value.Stream?.Dispose();
                _extracted++; _incomplete++;
                Console.WriteLine($"  [extract] INCOMPLETE: FsOffset=0x{kvp.Key:X}  " +
                                  $"{kvp.Value.Written}/{kvp.Value.FullSize}");
            }
            _activeStreams.Clear();

            Console.WriteLine($"  [extract] {_extracted} file(s) written under {_outputRoot}");
            Console.WriteLine($"  [extract]   from items:   {_fromItems}");
            Console.WriteLine($"  [extract]   zero-size:    {_zeroCreated}");
            Console.WriteLine($"  [extract]   incomplete:   {_incomplete}");
        }

        private string MapFilePath(IFsFile f)
        {
            string rel = (f.Path ?? "").TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string n = f.IsSystemFile && f.Name?.Length > 2 ? f.Name.Substring(2) : f.Name;
            return Path.Combine(string.IsNullOrEmpty(rel) ? _outputRoot : Path.Combine(_outputRoot, rel), n ?? "unnamed");
        }
        private string MapDirectory(IFsFile f)
        {
            string rel = (f.Path ?? "").TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            return string.IsNullOrEmpty(rel) ? _outputRoot : Path.Combine(_outputRoot, rel);
        }
        private sealed class OpenFile { public FileStream Stream; public long Written, FullSize; }
    }
}
