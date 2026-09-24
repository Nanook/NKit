using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ExtractXBoxStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private FileMask _mask;
        private List<IFsFile> _candidates;

        private long _lastAreaOffset;
        private Dictionary<string, long> _splitProgress;

        private int _extracted;
        private int _fsExtracted;
        private int _fsTotal;
        private bool _fsEmptyFoldersCreated;
        private int _skippedEncFiles;
        private bool _isIso9660;
        //private Playstation3FixData _ps3Fix;

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
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExtractXBox;

        public override string ProposedName() => _outName;

        internal ExtractXBoxStep(IStepContextConstruct context)
        {
            Match m = Regex.Match(context.StepConfig ?? "ri:.*", "^([a-z]*):(.*)$");
            bool matchCase = false;
            if (m.Success)
            {
                matchCase = !m.Groups[1].Value.Contains('i');
                if (m.Groups[1].Value.Contains('m')) //convert mask to a regex
                    _mask = FileMask.CreateImageFsMask(m.Groups[2].Value);
                else
                    _mask = new FileMask(m.Groups[2].Value, matchCase);
            }
            else
                _mask = new FileMask(".*", false);

            context.AddSettingsInfo("Extract", context.StepConfig);

            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            _context.SkipBlockTaskEnable();

            if (_mask?.Mask != null)
                _context.Log.Detail(() => $"Extract mask '{_mask.OriginalMask}' converted to regex '{_mask.Regex}'");

            //_ps3Fix = _context.Settings.FixData<Playstation3FixData>();
            _candidates = null;
            _lastAreaOffset = -1;
            _skippedEncFiles = 0;
            _splitProgress = new Dictionary<string, long>();
            _isIso9660 = _context.ImageInfo.SourceAreas.Length == 2;
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapCandidates = null;
            _overlapScanIndex = 0;
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            //don't do when not new 0 base FS
            if (section.AreaInfo.BaseOffset == 0 && section.AreaInfo.ImageOffset != _lastAreaOffset)
                _lastAreaOffset = section.AreaOffset;

            if (section.AreaOffset == 0 && section.AreaInfo.BaseOffset == 0)
            {
                _fsExtracted = 0;
                _fsTotal = -1;
                _fsEmptyFoldersCreated = false;
                //if (section.AreaInfo.BaseOffset == 0) //new fs
                _candidates = null;

                // Reset overlap state on new area/filesystem
                if (_activeOverlaps != null)
                {
                    foreach (OverlapState state in _activeOverlaps.Values)
                        state.Stream?.Dispose();
                    _activeOverlaps.Clear();
                }
                _overlapCandidates = null;
                _overlapScanIndex = 0;
            }

            // AreaFileSystem is only published once the area's file system is fully parsed. For
            // ISO9660 this may not happen until the end of the image. The XDVDFS game partition is
            // identified by the view's Kind (== XDvdFs) — no cast to the internal Microsoft.XBox.Fst.
            IAreaFileSystemView areaFs = section.AreaFileSystem;
            bool isXdvdFs = areaFs != null && areaFs.Kind == FileSystemKind.XDvdFs;
            if (!_isIso9660 && (section.Type == AreaType.Other || (areaFs != null && !isXdvdFs)))
            {
                long nextArea = _context.ImageInfo.SourceAreas?.FirstOrDefault(a => a.ImageOffset > section.ImageOffset)?.ImageOffset ?? long.MaxValue;
                // _context.SkipToImageOffsetSet(nextArea); //seek is not flexible enough to skip small areas - fix in rework
                return;
            }
            else if (areaFs != null && (_isIso9660 || isXdvdFs))
            {
                IFileSystemView fsView = areaFs.Primary;
                if (_candidates == null)
                {
                    _candidates = fsView.Files.Where(a => !a.IsSystemFile && a.FsOffset >= section.FsOffset && _mask.IsMatch(a.FullName)).OrderBy(a => a.FsOffset).ToList();

                    // Build a set of FullNames already in the candidate list so overlap writes
                    // don't attempt to re-extract files already being streamed via their own extents.
                    _candidateFullNames = new HashSet<string>(
                        _candidates.Select(a => a.FullName),
                        StringComparer.OrdinalIgnoreCase);

                    // Build overlap candidates
                    BuildOverlapCandidates(section);
                }

                if (_fsTotal == -1)
                    _fsTotal = fsView.Files.Count(a => !a.IsSystemFile);
            }

            saveFileData(section); //don't save encrypted files

            if (!_fsEmptyFoldersCreated && _fsTotal == _fsExtracted)
            {
                createEmptyFolders(_context.WritePath, "", areaFs?.Primary.Root);
                _fsEmptyFoldersCreated = true;
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();
            //_context.ScanInvalidated = true; //don't save scans

            // Close any remaining active overlaps (shouldn't normally happen, but safety net)
            if (_activeOverlaps != null)
            {
                foreach (OverlapState state in _activeOverlaps.Values)
                    state.Stream?.Dispose();
                _activeOverlaps.Clear();
            }

            if (_skippedEncFiles != 0)
                _context.Log.Info(() => $"Skipped {_skippedEncFiles} Encrypted file{_skippedEncFiles.s()} as no key was found");
            _context.Log.Info(() => $"Extracted {_extracted} file{_extracted.s()}");
            base.ProcessResults();
        }

        private void createEmptyFolders(string rootPath, string imagePath, IFsFolder dir)
        {
            if (dir == null)
                return;

            if (!string.IsNullOrEmpty(dir.Name) && dir.Files.Count(a => !a.IsSystemFile) == 0 && dir.Folders.Count == 0)
                base.ExtractHandler.CreateDirectory(rootPath, Path.Combine(imagePath, dir.Path.Substring(1)));

            foreach (IFsFolder d in dir.Folders)
                createEmptyFolders(rootPath, imagePath, d);
        }

        private void saveFileData(ISection section)
        {
            if (section.Type == AreaType.FileSystem)
            {
                foreach (SectionItem si in section.Items)
                {
                    if (_candidates != null && _candidates.Count == 0 && _activeOverlaps.Count == 0)
                        break;

                    if (si.File != null && !si.FsFile.IsSystemFile)
                    {
                        if (_mask.IsMatch(si.FsFile.FullName))
                        {
                            IFsFile match = _candidates?.FirstOrDefault(a => a == si.FsFile);
                            if (!section.AreaInfo.IsEncrypted || _context.SourceFile.Key != null) //don't save encrypted files when no key
                            {
                                string path = getPath(section, si.FsFile.Path);

                                base.ExtractHandler.CreateDirectory(_context.WritePath, path);

                                string imagePath = Path.Combine(path, si.FsFile.Name);

                                writeFs(section, _context.WritePath, imagePath, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize, si.FsFile.FsSize, si.FsFile.SplitParts?.Size ?? si.FsFile.FsSize);

                                ProcessOverlaps(section, si);
                            }
                            else if (si.File.OffsetInItem == 0)
                                _skippedEncFiles++;
                            if (match != null && si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize) //is this the end of the file
                                _candidates.Remove(match);
                        }
                    }
                }

                if (_candidates != null)
                {
                    if (_candidates.Count == 0 && _activeOverlaps.Count == 0)
                    {
                        long nextArea = _context.ImageInfo.SourceAreas?.FirstOrDefault(a => a.ImageOffset > section.ImageOffset)?.ImageOffset ?? long.MaxValue;
                        _context.SkipToImageOffsetSet(nextArea); //end disc reading
                    }
                    else if (_activeOverlaps.Count == 0)
                    {
                        long newOff = Buffer.FsOffsetToOffset(_candidates[0].FsOffset, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, false);
                        if (newOff > section.ImageOffset + section.Size)
                            _context.SkipToImageOffsetSet(newOff);
                    }
                }
            }
            else if (section.Type != AreaType.Other)
            {

            }
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
                    string entryPath = getPath(section, candidate.Path);
                    string entryImagePath = Path.Combine(entryPath, candidate.Name);
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

        private void BuildOverlapCandidates(ISection section)
        {
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapScanIndex = 0;

            // Try to source from FidelityFileList if available
            IFileSystemData fsData = ((ISectionProcessor)section).FileSystemData;
            if (fsData?.FidelityFiles != null)
            {
                // Build a lookup of primary candidate sizes by FsOffset for FstLink merge exclusion
                Dictionary<long, long> primarySizeByOffset = new Dictionary<long, long>();
                foreach (IFsFile c in _candidates)
                {
                    if (!primarySizeByOffset.ContainsKey(c.FsOffset))
                        primarySizeByOffset[c.FsOffset] = c.FsSize;
                }

                List<IFsFile> overlapCandidates = new List<IFsFile>();

                foreach (IFsFile f in fsData.FidelityFiles.Entries)
                {
                    // Skip system files
                    if (f.IsSystemFile)
                        continue;

                    // Skip files that don't match the mask
                    if (!_mask.IsMatch(f.FullName))
                        continue;

                    // Skip files already in the primary candidate list
                    if (_candidateFullNames.Contains(f.FullName))
                        continue;

                    // Exclude same-size FstLink merges (same offset, same size = alias, not a separate file)
                    if (primarySizeByOffset.TryGetValue(f.FsOffset, out long primarySize) && f.FsSize == primarySize)
                        continue;

                    // Handle zero-byte entries immediately: create empty file, don't add to overlap candidates
                    if (f.FsSize == 0)
                    {
                        string entryPath = getPath(section, f.Path);
                        base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                        string entryImagePath = Path.Combine(entryPath, f.Name);
                        base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                        _extracted++;
                        _fsExtracted++;
                        continue;
                    }

                    overlapCandidates.Add(f);
                }

                // Sort by FsOffset for efficient activation scanning
                _overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
            }
            else
            {
                // No fidelity list available — identify overlaps from _candidates
                // Files sharing FsOffset with a larger file (excluding same-size FstLink merges)
                IEnumerable<IGrouping<long, IFsFile>> offsetGroups = _candidates.GroupBy(f => f.FsOffset).Where(g => g.Count() > 1);
                List<IFsFile> overlapCandidates = new List<IFsFile>();

                foreach (IGrouping<long, IFsFile> group in offsetGroups)
                {
                    long maxSize = group.Max(f => f.FsSize);
                    foreach (IFsFile f in group)
                    {
                        // Skip the largest (it's the primary in OrderedList)
                        if (f.FsSize == maxSize)
                            continue;

                        // Handle zero-byte entries immediately
                        if (f.FsSize == 0)
                        {
                            string entryPath = getPath(section, f.Path);
                            base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                            string entryImagePath = Path.Combine(entryPath, f.Name);
                            base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                            _extracted++;
                            _fsExtracted++;
                            continue;
                        }

                        overlapCandidates.Add(f);
                        // Remove from _candidates since they'll be handled via overlap tracking
                        _candidates.Remove(f);
                    }
                }

                // Sort by FsOffset for efficient activation scanning
                _overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
            }
        }

        private string getPath(ISection section, string path) =>
            //string extra = string.Format("{0}_{1}", section.AreaInfo.ImageOffset.ToString("X9"), section.AreaInfo.Type.ToString());
            path.Trim('\\', '/');

        private void writeFs(ISection section, string rootPath, string imagePath, long pos, int fsOffset, int sizeFs, long fsFullSize, long splitFullSize)
        {
            bool isSplit = splitFullSize != fsFullSize;
            bool newFile = pos == 0;

            if (isSplit)
            {
                long l = 0;
                if (!_splitProgress.TryGetValue(imagePath, out l))
                    _splitProgress.Add(imagePath, 0);
                else
                    newFile = false;
                pos = l;
            }

            if (base.ExtractHandler.WriteFs(section, rootPath, imagePath, pos, fsOffset, sizeFs, fsFullSize, newFile))
            {
                if (isSplit && !newFile)
                    _fsTotal--;  //part > 1 - deduct the file
                else
                {
                    _extracted++;
                    _fsExtracted++;
                }
            }

            if (isSplit)
            {
                if (pos + sizeFs >= splitFullSize)
                    _splitProgress.Remove(imagePath); //complete
                else
                    _splitProgress[imagePath] = pos + sizeFs;
            }
        }
    }
}