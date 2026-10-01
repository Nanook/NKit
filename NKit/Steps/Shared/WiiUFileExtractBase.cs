using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.Linq;
using Path = System.IO.Path;

namespace Nanook.NKit.Steps.Shared
{
    /// <summary>
    /// Shared WiiU file-extraction engine used by both <see cref="ExtractWiiUStep"/>
    /// and <see cref="ConvertWiiULoadiineStep"/>.
    ///
    /// Handles all WiiU image parsing (partition table, FST, TmdApp) and file writing
    /// via <see cref="StepBase.ExtractHandler"/>.  Subclasses supply:
    ///   • The file-match mask (extract: from StepConfig; convert: match-all ".*")
    ///   • Their own contract properties (IsLossy, OutputType, ComponentTag, …)
    ///   • Optional <see cref="OnTitleIdentified"/> override for WUA archive title-folder setup
    /// </summary>
    internal abstract class WiiUFileExtractBase : StepBase, IStep
    {
        private IStepContext _context;
        private readonly FileMask _mask;
        private List<IFsFile> _candidates;

        private string _ptnPath;
        private PartitionInfo _ptn;
        private ImageHeader _header;
        private FileSystemInfo _fsInfo;
        private ContentHeader _cntHeader;
        private int _extracted;
        private int _fsExtracted;

        // Overlap tracking for shared-offset and range-overlapping files
        private Dictionary<string, OverlapState> _activeOverlaps;
        private List<IFsFile> _overlapCandidates;
        private int _overlapScanIndex;
        private HashSet<string> _candidateFullNames;

        private struct OverlapState
        {
            public System.IO.FileStream Stream;
            public IFsFile File;
            public long Written;
            public long TotalSize;
            public long TotalWritten;
        }

        public override string ProposedName() => _outName;
        private readonly string _outName;

        /// <summary>
        /// Initialises the shared extraction engine.
        /// </summary>
        /// <param name="mask">File-match mask — extract steps pass a user-configured mask;
        /// convert steps pass a match-all mask (<c>new FileMask(".*", false)</c>).</param>
        /// <param name="outName">Proposed output name (source image name).</param>
        protected WiiUFileExtractBase(FileMask mask, string outName)
        {
            _mask    = mask;
            _outName = outName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            _context.SkipBlockTaskEnable();
            _ptnPath = "";

            if (_mask?.Mask != null)
                _context.Log.Detail(() => $"Extract mask '{_mask.OriginalMask}' converted to regex '{_mask.Regex}'");

            _extracted = 0;
            _fsExtracted = 0;
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapCandidates = null;
            _overlapScanIndex = 0;
        }

        protected void SetFinalNameFromTitleId(string name, string titleId, string version)
        {
            if (titleId != null && !name.ToLower().Contains(titleId.ToLower()))
                name += $" [{titleId}]";

            string v = version == null ? "" : $"[rev{version}]";

            if (!name.ToLower().Contains(v.ToLower()))
                name += v;

            base.SetFinalName(name);
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type == AreaType.RawKeyMissing)
                throw new Exception("Missing Key required to Extract files");

            if (_context.SourceFile.ImageType == SourceImageType.TmdApp && section.ImageOffset == 0)
            {
                if (section.Type == AreaType.FstBlock)
                {
                    _header = new ImageHeader(null);

                    IndexFile idx = _context.SourceFile.IndexFile;
                    SiData si = new SiData(null, idx, section.Encrypted) { AppIndex = 0 };
                    _header.SiData.Add(si);

                    si.Complete(_header);
                    _header.Update(0, si);

                    _fsInfo = new FileSystemInfo(0, _context.ImageSize, _header, _header.SiData[0], (int)section.Size);
                    _fsInfo.ProcessBlock(0, section.Decrypted, (int)section.Size, true, _context.SourceFile.IndexFile);
                    setCandidates(section, false);

                    _ptn = new PartitionInfo(PartitionType.Game, 0, 0, 0);
                    SetFinalNameFromTitleId(_context.SourceFile.Name, si.TmdInfo.TitleId.ToString("X16"), si.TmdInfo.TitleVersion.ToString());
                    OnTitleIdentified(si.TmdInfo.TitleId.ToString("X16"), si.TmdInfo.TitleVersion.ToString());

                    var sysFiles = new[]
                    {
                        new { Name = $"title.tik", Data = si.FileTicket },
                        new { Name = $"title.tmd", Data = si.FileTmd },
                        new { Name = $"title.cert", Data = si.FileCert }
                    };

                    foreach (var sysFile in sysFiles)
                    {
                        if (sysFile.Data != null && _mask.IsMatch($"/{sysFile.Name}"))
                        {
                            base.ExtractHandler.WriteBytes(_context.WritePath, sysFile.Name, sysFile.Data);
                            _extracted++;
                        }
                    }
                }
            }
            else if (section.Type == AreaType.ImageHeader)
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size));
            else if (section.Type == AreaType.PartitionTable)
            {
                _header.Update(section.Decrypted.Read(0, (int)section.Size));
                SetFinalNameFromTitleId(_context.SourceFile.Name, null, null);
            }
            else if (section.Type == AreaType.PartitionHeader)
            {
                _ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset);
                if (_ptn != null && _ptn.ImageOffset == section.ImageOffset)
                {
                    _fsInfo = new FileSystemInfo(section.ImageOffset, _context.ImageSize, _header, section.Decrypted);
                    _ptnPath = _ptn.Id;
                }
            }
            else if (section.Type == AreaType.FstBlock)
            {
                _fsInfo.ProcessBlock(section.ImageOffset, section.Decrypted, (int)section.Size, true, null);
                setCandidates(section, true);
            }
            else if (_ptn != null && section.Type == AreaType.FileSystem)
            {
                _cntHeader = _fsInfo.FstBlock.GetContentHeader(section.ImageOffset);

                if (_ptn.Type == PartitionType.Si)
                    saveFileData(section);

                foreach (SectionItem si in section.Items)
                {
                    if (_candidates != null && _candidates.Count == 0 && _activeOverlaps.Count == 0)
                        break;

                    IFsFile match = _candidates?.FirstOrDefault(a => a == si.FsFile);
                    if (match != null)
                    {
                        string path = getPath(section, "", si.FsFile.Path);

                        base.ExtractHandler.CreateDirectory(_context.WritePath, path);

                        string imagePath = Path.Combine(path, si.FsFile.IsSystemFile ? si.FsFile.Name.Substring(2) : si.FsFile.Name);

                        base.ExtractHandler.WriteFs(section, _context.WritePath, imagePath, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize, si.FsFile.FsSize, true);

                        ProcessOverlaps(section, si);

                        if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                        {
                            _candidates.Remove(match);
                            _extracted++;
                            _fsExtracted++;
                        }
                    }
                }
            }

            skipToNext(section);
        }

        public void Patched(ISection section) { }

        /// <summary>
        /// Called when the game title is first identified (title ID and version are known).
        /// Subclasses can override — e.g. to set the WUA archive title folder name.
        /// </summary>
        protected virtual void OnTitleIdentified(string titleId, string version) { }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            if (_activeOverlaps != null)
            {
                foreach (OverlapState state in _activeOverlaps.Values)
                    state.Stream?.Dispose();
                _activeOverlaps.Clear();
            }

            Context.Result.ProgressSummary = $"Extracted {_extracted} file{_extracted.s()}";
            base.ProcessResults();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void setCandidates(ISection section, bool addPtn)
        {
            IFileSystemView fsView = section.AreaFileSystem?.Primary;
            if (fsView != null)
            {
                string ptn = addPtn ? $"/{_ptn.Id}" : "";
                _candidates = fsView.Files.Where(a => _mask.IsMatch(ptn + a.FullName)).OrderBy(a => ((FstFile)a).WiiUAppIndex).ThenBy(a => a.FsOffset).ToList();

                _candidateFullNames = new HashSet<string>(
                    _candidates.Select(a => a.FullName),
                    StringComparer.OrdinalIgnoreCase);

                if (_activeOverlaps != null)
                {
                    foreach (OverlapState state in _activeOverlaps.Values)
                        state.Stream?.Dispose();
                    _activeOverlaps.Clear();
                }
                _overlapScanIndex = 0;

                BuildOverlapCandidates(section);

                if (_candidates.Count != 0 && _candidates.Count == fsView.FileCount)
                    createEmptyFolders(_context.WritePath, _ptnPath, fsView.Root);
            }
        }

        private void skipToNext(ISection section)
        {
            if (_ptn == null || _ptn.ImageOffset == section.ImageOffset || section.Type == AreaType.FstBlock)
                return;

            if (_candidates != null)
            {
                if (_candidates.Count == 0 && _activeOverlaps.Count == 0)
                {
                    // fallthrough to skip to next partition
                }
                else if (_activeOverlaps.Count > 0)
                {
                    return;
                }
                else if (((FstFile)_candidates[0]).WiiUAppIndex > _cntHeader.Index)
                {
                    _context.SkipToImageOffsetSet(_fsInfo.FstBlock.ContentHeaders[((FstFile)_candidates[0]).WiiUAppIndex].ImageOffset);
                    return;
                }
                else if (((FstFile)_candidates[0]).WiiUAppIndex == _cntHeader.Index)
                {
                    long newOff = _fsInfo.FstBlock.ContentHeaders[((FstFile)_candidates[0]).WiiUAppIndex].ImageOffset + Buffer.FsOffsetToOffset(_candidates[0].FsOffset, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, false);
                    if (newOff > section.ImageOffset + section.Size)
                    {
                        _context.SkipToImageOffsetSet(newOff);
                        return;
                    }
                    else
                        return;
                }
            }

            if (_ptn != null && _ptn.ImageOffset > section.ImageOffset)
                _context.SkipToImageOffsetSet(_ptn.ImageOffset);
            else
            {
                PartitionInfo ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset);
                if (ptn != null)
                    _context.SkipToImageOffsetSet(ptn.ImageOffset);
                else if (_ptn != null && _ptn.ImageOffset != 0)
                    _context.SkipToImageOffsetSet(long.MaxValue);
            }
        }

        private void createEmptyFolders(string rootPath, string imagePath, IFsFolder dir)
        {
            if (dir.Files.Count == 0 && dir.Folders.Count == 0)
                base.ExtractHandler.CreateDirectory(rootPath, Path.Combine(imagePath, dir.Path.Substring(1)));

            foreach (IFsFolder d in dir.Folders)
                createEmptyFolders(rootPath, imagePath, d);
        }

        private void saveFileData(ISection section)
        {
            IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
            buff.ReInitialise(section.AreaInfo, false);
            buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
            Image.SetSiInfoInImageHeader(buff, _cntHeader, _header, _fsInfo, true);
        }

        private void BuildOverlapCandidates(ISection section)
        {
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapScanIndex = 0;

            Dictionary<long, long> primarySizeByOffset = new Dictionary<long, long>();
            foreach (IFsFile c in _candidates)
            {
                if (!primarySizeByOffset.ContainsKey(c.FsOffset))
                    primarySizeByOffset[c.FsOffset] = c.FsSize;
            }

            HashSet<string> candidateOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IFsFile c in _candidates)
            {
                string cPath = getPath(section, "", c.Path);
                string cImagePath = Path.Combine(cPath, c.IsSystemFile ? c.Name.Substring(2) : c.Name);
                candidateOutputPaths.Add(cImagePath);
            }

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            IFileSystemData fsData = ((ISectionProcessor)section).FileSystemData;
            IEnumerable<IFsFile> sourceFiles = fsData?.FidelityFiles?.Entries;

            if (sourceFiles != null)
            {
                foreach (IFsFile f in sourceFiles)
                {
                    if (f.IsSystemFile) continue;
                    if (!_mask.IsMatch(f.FullName)) continue;
                    if (_candidateFullNames.Contains(f.FullName)) continue;

                    string fPath = getPath(section, "", f.Path);
                    string fImagePath = Path.Combine(fPath, f.IsSystemFile ? f.Name.Substring(2) : f.Name);
                    if (candidateOutputPaths.Contains(fImagePath)) continue;

                    if (primarySizeByOffset.TryGetValue(f.FsOffset, out long primarySize) && f.FsSize == primarySize)
                        continue;

                    if (f.FsSize == 0)
                    {
                        string entryPath = getPath(section, "", f.Path);
                        base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                        string entryImagePath = Path.Combine(entryPath, f.IsSystemFile ? f.Name.Substring(2) : f.Name);
                        base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                        _extracted++;
                        _fsExtracted++;
                        continue;
                    }

                    overlapCandidates.Add(f);
                }
            }
            else
            {
                IEnumerable<IGrouping<long, IFsFile>> offsetGroups = _candidates.GroupBy(f => f.FsOffset).Where(g => g.Count() > 1);
                foreach (IGrouping<long, IFsFile> group in offsetGroups)
                {
                    foreach (IFsFile other in group.Skip(1))
                    {
                        if (other.FsSize == 0)
                        {
                            string entryPath = getPath(section, "", other.Path);
                            base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                            string entryImagePath = Path.Combine(entryPath, other.IsSystemFile ? other.Name.Substring(2) : other.Name);
                            base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                            _extracted++;
                            _fsExtracted++;
                        }
                        else if (!candidateOutputPaths.Contains(Path.Combine(getPath(section, "", other.Path), other.IsSystemFile ? other.Name.Substring(2) : other.Name)))
                        {
                            overlapCandidates.Add(other);
                        }
                    }
                }
            }

            _overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
        }

        private void ProcessOverlaps(ISection section, SectionItem si)
        {
            if (_overlapCandidates == null || _overlapCandidates.Count == 0)
                return;

            long discStart = si.FsFile.FsOffset + si.File.OffsetInItem;
            long chunkSize = si.File.FsSize;
            long discEnd = discStart + chunkSize;

            while (_overlapScanIndex < _overlapCandidates.Count)
            {
                IFsFile candidate = _overlapCandidates[_overlapScanIndex];
                if (candidate.FsOffset >= discEnd) break;

                if (candidate.FsOffset + candidate.FsSize > discStart)
                {
                    string entryPath = getPath(section, "", candidate.Path);
                    string entryImagePath = Path.Combine(entryPath, candidate.IsSystemFile ? candidate.Name.Substring(2) : candidate.Name);
                    string fullPath = Path.Combine(_context.WritePath, entryImagePath);

                    if (!_activeOverlaps.ContainsKey(fullPath))
                    {
                        base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                        string dir = System.IO.Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir))
                            System.IO.Directory.CreateDirectory(dir);
                        System.IO.FileStream stream = new System.IO.FileStream(fullPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 0x200000);
                        long totalSize = candidate.SplitParts?.Size ?? candidate.FsSize;
                        _activeOverlaps[fullPath] = new OverlapState { Stream = stream, File = candidate, Written = 0, TotalSize = totalSize, TotalWritten = 0 };
                    }
                    else
                    {
                        OverlapState state = _activeOverlaps[fullPath];
                        state.File = candidate;
                        state.Written = 0;
                        _activeOverlaps[fullPath] = state;
                    }
                }

                if (candidate.FsOffset < discEnd)
                    _overlapScanIndex++;
                else
                    break;
            }

            foreach (string key in new List<string>(_activeOverlaps.Keys))
            {
                OverlapState state = _activeOverlaps[key];
                IFsFile file = state.File;

                long intersectStart = Math.Max(discStart, file.FsOffset);
                long intersectEnd   = Math.Min(discEnd, file.FsOffset + file.FsSize);

                if (intersectEnd > intersectStart)
                {
                    int offsetInSection = (int)(si.File.FsOffset + (intersectStart - discStart));
                    int writeSize = (int)(intersectEnd - intersectStart);
                    section.Read(offsetInSection, writeSize, state.Stream);
                    state.Written += writeSize;
                    state.TotalWritten += writeSize;
                    _activeOverlaps[key] = state;
                }
            }

            foreach (string path in new List<string>(_activeOverlaps.Keys.Where(k => _activeOverlaps[k].TotalWritten >= _activeOverlaps[k].TotalSize)))
            {
                _activeOverlaps[path].Stream.Close();
                _activeOverlaps.Remove(path);
                _extracted++;
                _fsExtracted++;
            }
        }

        private string getPath(ISection section, string type, string path) => Path.Combine(_ptnPath, type, path.Trim('\\', '/'));
    }
}
