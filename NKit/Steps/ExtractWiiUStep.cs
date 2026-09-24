using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiU;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ExtractWiiUStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private FileMask _mask;
        private List<IFsFile> _candidates;

        private string _ptnPath;
        private PartitionInfo _ptn;
        private ImageHeader _header;
        private FileSystemInfo _fsInfo;
        private ContentHeader _cntHeader;
        private int _extracted;
        private int _fsExtracted;

        private bool _forensic;

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

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => false;
        internal override bool ContractFullScan => false;
        internal override bool ContractIsLossy => true;
        internal override bool ContractIsExpand => false;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FolderFiles;
        internal override bool ContractCanCrc => false;
        internal override bool ContractCanHash => false;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExtractWiiU;

        public override string ProposedName() => _outName;

        internal ExtractWiiUStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            Match m = Regex.Match(context.StepConfig ?? "ri:.*", "^([a-z]*):(.*)$");
            bool matchCase = false;
            _forensic = false;
            if (m.Success)
            {
                matchCase = !m.Groups[1].Value.Contains('i');
                _forensic = m.Groups[1].Value.Contains('f');
                if (m.Groups[1].Value.Contains('m')) //convert mask to a regex
                    _mask = FileMask.CreateImageFsMask(m.Groups[2].Value);
                else
                    _mask = new FileMask(m.Groups[2].Value, matchCase);
            }
            else
                _mask = new FileMask(".*", false);

            if (!_forensic) //no support for filtering atm
                context.AddSettingsInfo("Extract", context.StepConfig);

            _outName = context.SourceImageName;
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

        private void setFinalName(string name, string titleId, string version)
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
                if (section.Type == AreaType.FstBlock) //source is app files
                {
                    _header = new ImageHeader(null);

                    //FileItem fi;
                    IndexFile idx = _context.SourceFile.IndexFile;
                    SiData si = new SiData(null, idx, section.Encrypted) { AppIndex = 0 };
                    _header.SiData.Add(si);

                    si.Complete(_header);
                    _header.Update(0, si);

                    _fsInfo = new FileSystemInfo(0, _context.ImageSize, _header, _header.SiData[0], (int)section.Size);
                    _fsInfo.ProcessBlock(0, section.Decrypted, (int)section.Size, true, _context.SourceFile.IndexFile);
                    setCandidates(section, false);

                    _ptn = new PartitionInfo(PartitionType.Game, 0, 0, 0);
                    setFinalName(_context.SourceFile.Name, si.TmdInfo.TitleId.ToString("X16"), si.TmdInfo.TitleVersion.ToString());

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
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size)); //clone
            else if (section.Type == AreaType.PartitionTable)
            {
                _header.Update(section.Decrypted.Read(0, (int)section.Size)); //clone
                setFinalName(_context.SourceFile.Name, null, null);
            }
            else if (section.Type == AreaType.PartitionHeader)
            {
                _ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset); // && (a.Type == PartitionType.Si || (a.Type == PartitionType.Game && a.Id.StartsWith("GM00050000"))));
                if (_ptn != null && _ptn.ImageOffset == section.ImageOffset) //if not our WipePartition then skip will forward
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

                        if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize) //is this the end of the file
                            _candidates.Remove(match);
                    }
                }
            }

            skipToNext(section);
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            // Close any remaining active overlaps (safety net)
            if (_activeOverlaps != null)
            {
                foreach (OverlapState state in _activeOverlaps.Values)
                    state.Stream?.Dispose();
                _activeOverlaps.Clear();
            }

            _context.Log.Info(() => $"Extracted {_extracted} file{_extracted.s()}");
            base.ProcessResults();
        }

        private void setCandidates(ISection section, bool addPtn)
        {
            // Frozen, fully-parsed file list from the area view. WiiU always builds the full FS
            // up front, so Primary is populated here. The FileSystemInfo (_fsInfo) rebuild that
            // drives ContentHeaders / SI security stays as-is; only the file LIST moves to the view.
            IFileSystemView fsView = section.AreaFileSystem?.Primary;
            if (fsView != null)
            {
                string ptn = addPtn ? $"/{_ptn.Id}" : "";
                _candidates = fsView.Files.Where(a => _mask.IsMatch(ptn + a.FullName)).OrderBy(a => ((FstFile)a).WiiUAppIndex).ThenBy(a => a.FsOffset).ToList();

                // Build a set of FullNames already in the candidate list
                _candidateFullNames = new HashSet<string>(
                    _candidates.Select(a => a.FullName),
                    StringComparer.OrdinalIgnoreCase);

                // Reset overlap state on new filesystem
                if (_activeOverlaps != null)
                {
                    foreach (OverlapState state in _activeOverlaps.Values)
                        state.Stream?.Dispose();
                    _activeOverlaps.Clear();
                }
                _overlapScanIndex = 0;

                // Build overlap candidates
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
                    //fallthrough to skip to next WipePartition
                }
                else if (_activeOverlaps.Count > 0)
                {
                    return; // don't skip while overlaps are active
                }
                else if (((FstFile)_candidates[0]).WiiUAppIndex > _cntHeader.Index) //skip to later content
                {
                    _context.SkipToImageOffsetSet(_fsInfo.FstBlock.ContentHeaders[((FstFile)_candidates[0]).WiiUAppIndex].ImageOffset);
                    return;
                }
                else if (((FstFile)_candidates[0]).WiiUAppIndex == _cntHeader.Index) //skip within same content
                {
                    long newOff = _fsInfo.FstBlock.ContentHeaders[((FstFile)_candidates[0]).WiiUAppIndex].ImageOffset + Buffer.FsOffsetToOffset(_candidates[0].FsOffset, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, false);
                    if (newOff > section.ImageOffset + section.Size)
                    {
                        _context.SkipToImageOffsetSet(newOff);
                        return;
                    }
                    else
                        return; //no skip
                }
            }

            if (_ptn != null && _ptn.ImageOffset > section.ImageOffset)
                _context.SkipToImageOffsetSet(_ptn.ImageOffset);
            else
            {
                PartitionInfo ptn = _header.Partitions.FirstOrDefault(a => a.ImageOffset >= section.ImageOffset); // && (a.Type == PartitionType.Si || (a.Type == PartitionType.Game && a.Id.StartsWith("GM00050000"))));
                if (ptn != null)
                    _context.SkipToImageOffsetSet(ptn.ImageOffset);
                else if (_ptn != null && _ptn.ImageOffset != 0) //don't skip to end for app extracts
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
            //hack in to IBuffer to use the same code Image uses to read SI data
            IBuffer buff = new Buffer(section.AreaInfo.IsEncryptionSupported, section.Decrypted);
            buff.ReInitialise(section.AreaInfo, false);
            buff.Update(section.ImageOffset, section.AreaOffset, (int)section.Size, -1, false, false);
            Image.SetSiInfoInImageHeader(buff, _cntHeader, _header, _fsInfo, true);
        }

        private void BuildOverlapCandidates(ISection section)
        {
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapScanIndex = 0;

            // Build a lookup of primary candidate sizes by FsOffset for FstLink merge exclusion
            Dictionary<long, long> primarySizeByOffset = new Dictionary<long, long>();
            foreach (IFsFile c in _candidates)
            {
                if (!primarySizeByOffset.ContainsKey(c.FsOffset))
                    primarySizeByOffset[c.FsOffset] = c.FsSize;
            }

            // Build a set of output paths that regular candidates will write to,
            // to prevent overlap candidates from conflicting with them.
            HashSet<string> candidateOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IFsFile c in _candidates)
            {
                string cPath = getPath(section, "", c.Path);
                string cImagePath = Path.Combine(cPath, c.IsSystemFile ? c.Name.Substring(2) : c.Name);
                candidateOutputPaths.Add(cImagePath);
            }

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            // Try FidelityFileList first, fall back to identifying overlaps from _candidates
            IFileSystemData fsData = ((ISectionProcessor)section).FileSystemData;
            IEnumerable<IFsFile> sourceFiles = fsData?.FidelityFiles?.Entries;

            if (sourceFiles != null)
            {
                foreach (IFsFile f in sourceFiles)
                {
                    if (f.IsSystemFile)
                        continue;

                    if (!_mask.IsMatch(f.FullName))
                        continue;

                    if (_candidateFullNames.Contains(f.FullName))
                        continue;

                    // Exclude entries whose output path conflicts with a regular candidate
                    string fPath = getPath(section, "", f.Path);
                    string fImagePath = Path.Combine(fPath, f.IsSystemFile ? f.Name.Substring(2) : f.Name);
                    if (candidateOutputPaths.Contains(fImagePath))
                        continue;

                    // Exclude same-size FstLink merges (same offset, same size = alias)
                    if (primarySizeByOffset.TryGetValue(f.FsOffset, out long primarySize) && f.FsSize == primarySize)
                        continue;

                    // Handle zero-byte entries immediately
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
                // No FidelityFileList — identify overlaps from _candidates themselves
                // Files at the same offset where only the largest wins the primary slot
                IEnumerable<IGrouping<long, IFsFile>> offsetGroups = _candidates.GroupBy(f => f.FsOffset).Where(g => g.Count() > 1);
                foreach (IGrouping<long, IFsFile> group in offsetGroups)
                {
                    // The primary candidate is the first one (largest at that offset in the OrderedList)
                    IFsFile primary = group.First();
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

            // Sort by FsOffset for efficient activation scanning
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
                if (candidate.FsOffset >= discEnd)
                    break;

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

            List<string> activeKeys = new List<string>(_activeOverlaps.Keys);
            foreach (string key in activeKeys)
            {
                OverlapState state = _activeOverlaps[key];
                IFsFile file = state.File;

                long intersectStart = Math.Max(discStart, file.FsOffset);
                long intersectEnd = Math.Min(discEnd, file.FsOffset + file.FsSize);

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

            List<string> completed = new List<string>();
            foreach (KeyValuePair<string, OverlapState> kvp in _activeOverlaps)
            {
                if (kvp.Value.TotalWritten >= kvp.Value.TotalSize)
                    completed.Add(kvp.Key);
            }
            foreach (string path in completed)
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