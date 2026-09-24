using Nanook.NKit.Configuration;
using Nanook.NKit.Iso.Iso9660;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ExtractIsoStep : StepBase, IStep
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
        private Playstation3FixData _ps3Fix;
        private HashSet<string> _candidateFullNames;
        private HashSet<IFsFile> _candidateSet;
        private HashSet<string> _overlapWritePaths;
        private FsType _preferredFsType;

        // Overlap tracking: each overlap file is written through ExtractHandler
        private Dictionary<string, OverlapHandler> _activeOverlaps;
        private List<IFsFile> _overlapCandidates;
        private int _overlapScanIndex;

        private struct OverlapHandler
        {
            public IFsFile File;
            public string ImagePath;
            public long TotalSize;
            public long TotalWritten;
            public long LastDiscEnd;
            public bool IsContaining;
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
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExtractIso;

        public override string ProposedName() => _outName;

        internal ExtractIsoStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);

            // Use configuration provider for extract format validation and parsing
            string extractConfig = context.StepConfig ?? ConfigSettingsDefaults.GetDefaultExtractSearchTerm();

            // Validate the extract configuration format
            ValidationResult validationResult = ConfigSettingsFormatValidator.ValidateExtractFormat(extractConfig);
            if (!validationResult.IsValid)
            {
                throw new HandledException($"Extract configuration error: {validationResult.ErrorMessage}");
            }

            // Parse extract configuration using provider
            ExtractConfiguration config = ConfigSettingsFormatParser.ParseExtractConfiguration(extractConfig);

            // Create FileMask based on parsed configuration
            if (config.IsMaskToRegex)
            {
                _mask = FileMask.CreateImageFsMask(config.Pattern);
            }
            else
            {
                _mask = new FileMask(config.Pattern, !config.IsCaseInsensitive);
            }

            // Log configuration details for debugging
            context.Log?.Detail(() => $"Extract configuration - Forensic:{config.IsForensic}, CaseInsensitive:{config.IsCaseInsensitive}, MaskToRegex:{config.IsMaskToRegex}, Recursive:{config.IsRecursive}, Pattern:'{config.Pattern}'");

            context.AddSettingsInfo("Extract", extractConfig);
            context.AddSettingsInfo("ExtractFlags", $"f:{config.IsForensic}, i:{config.IsCaseInsensitive}, m:{config.IsMaskToRegex}, r:{config.IsRecursive}");

            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            base.Initialise(context);

            _context = context;
            _context.SkipBlockTaskEnable();

            if (_mask?.Mask != null)
                _context.Log.Detail(() => $"Extract mask '{_mask.OriginalMask}' converted to regex '{_mask.Regex}'");

            _ps3Fix = _context.Settings.FixData<Playstation3FixData>();
            _candidates = null;
            _lastAreaOffset = -1;
            _skippedEncFiles = 0;
            _splitProgress = new Dictionary<string, long>();
            _activeOverlaps = new Dictionary<string, OverlapHandler>(StringComparer.OrdinalIgnoreCase);
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
                _candidates = null;

                // Reset overlap state on new area/filesystem
                if (_activeOverlaps != null)
                    _activeOverlaps.Clear();
                _overlapCandidates = null;
                _overlapScanIndex = 0;
            }

            // AreaFileSystem is only published once the area's file system is fully parsed. For
            // ISO9660 this may not happen until the end of the image. Primary is the frozen,
            // ordered snapshot over the same IFsFile instances the live FST holds, so the
            // per-file Links[0].FsType selection below is unchanged by sourcing the list here.
            IFileSystemView fsView = section.AreaFileSystem?.Primary;
            if (fsView != null)
            {
                if (_candidates == null)
                {
                    _candidates = fsView.Files.Where(a => !a.IsSystemFile && a.FsOffset >= section.FsOffset && _mask.IsMatch(a.FullName)).OrderBy(a => a.FsOffset).ToList();

                    // Determine preferred filesystem and filter out lower-priority entries.
                    // Use Links[0] (lowest FsType) to determine each file's primary view.
                    // This matches the original behavior where merged files keep their
                    // first-registered FS as primary.
                    FsType bestFsType = FsType.ElTorito;
                    foreach (IFsFile c in _candidates)
                    {
                        if (c is Nanook.NKit.Iso.Iso9660.FstFile fst && fst.Links.Count > 0)
                        {
                            FsType ft = fst.Links[0].FsType;
                            if (ft > bestFsType) bestFsType = ft;
                            if (bestFsType == FsType.Udf) break;
                        }
                    }
                    if (bestFsType > FsType.Iso9660)
                    {
                        _candidates = _candidates.Where(a =>
                        {
                            if (a is Nanook.NKit.Iso.Iso9660.FstFile fst && fst.Links.Count > 0)
                                return fst.Links[0].FsType >= bestFsType;
                            return true;
                        }).ToList();
                    }
                    _preferredFsType = bestFsType;

                    _candidateFullNames = new HashSet<string>(
                        _candidates.Select(a => a.FullName),
                        StringComparer.OrdinalIgnoreCase);

                    // Build overlap candidates from FidelityFileList
                    BuildOverlapCandidates(section);
                }

                if (_fsTotal == -1)
                    _fsTotal = fsView.Files.Count(a => !a.IsSystemFile);
            }

            saveFileData(section);

            if (!_fsEmptyFoldersCreated && _fsTotal == _fsExtracted)
            {
                createEmptyFolders(_context.WritePath, "", section.AreaFileSystem?.Primary.Root);
                _fsEmptyFoldersCreated = true;
            }
        }

        public void Patched(ISection section)
        {
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            // Close any remaining active overlaps
            if (_activeOverlaps != null)
                _activeOverlaps.Clear();

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
                    if (_candidates != null && _candidates.Count == 0 && _activeOverlaps.Count == 0 && (_overlapCandidates == null || _overlapScanIndex >= _overlapCandidates.Count))
                        break;

                    if (si.File != null && !si.FsFile.IsSystemFile)
                    {
                        if (_mask.IsMatch(si.FsFile.FullName))
                        {
                            IFsFile match = _candidates?.FirstOrDefault(a => a == si.FsFile);
                            if (!section.AreaInfo.IsEncrypted || _context.SourceFile.Key != null)
                            {
                                string path = getPath(section, si.FsFile.Path);
                                string imagePath = Path.Combine(path, si.FsFile.Name);

                                // Skip writeFs for containing files — ProcessOverlaps handles all their data
                                if (_overlapWritePaths == null || !_overlapWritePaths.Contains(imagePath))
                                {
                                    base.ExtractHandler.CreateDirectory(_context.WritePath, path);
                                    writeFs(section, _context.WritePath, imagePath, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize, si.FsFile.FsSize, si.FsFile.SplitParts?.Size ?? si.FsFile.FsSize);
                                }
                            }
                            else if (si.File.OffsetInItem == 0)
                                _skippedEncFiles++;
                            if (match != null && si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                                _candidates.Remove(match);
                        }

                        ProcessOverlaps(section, si);
                    }
                }

                if (_candidates != null)
                {
                    if (_candidates.Count == 0 && _activeOverlaps.Count == 0 && (_overlapCandidates == null || _overlapScanIndex >= _overlapCandidates.Count))
                    {
                        long nextArea = _context.ImageInfo.SourceAreas?.FirstOrDefault(a => a.ImageOffset > section.ImageOffset)?.ImageOffset ?? long.MaxValue;
                        _context.SkipToImageOffsetSet(nextArea);
                    }
                    else if (_activeOverlaps.Count == 0)
                    {
                        long baseOffset = 0;
                        if (section.AreaInfo.FsAddressMode == AddressMode.Area)
                            baseOffset = section.AreaInfo.ImageOffset;

                        // Determine next offset to skip to: the minimum of the next candidate
                        // and the next unscanned overlap candidate. This prevents skipping past
                        // overlap files that sit between the current position and the next candidate.
                        long nextFsOffset = long.MaxValue;
                        if (_candidates.Count > 0)
                            nextFsOffset = _candidates[0].FsOffset;
                        if (_overlapCandidates != null && _overlapScanIndex < _overlapCandidates.Count)
                        {
                            long nextOverlapFsOffset = _overlapCandidates[_overlapScanIndex].FsOffset;
                            if (nextOverlapFsOffset < nextFsOffset)
                                nextFsOffset = nextOverlapFsOffset;
                        }

                        if (nextFsOffset != long.MaxValue)
                        {
                            long newOff = baseOffset + Buffer.FsOffsetToOffset(nextFsOffset, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, false);
                            if (newOff > section.ImageOffset + section.Size)
                                _context.SkipToImageOffsetSet(newOff);
                        }
                    }
                }
            }
        }

        private void ProcessOverlaps(ISection section, SectionItem si)
        {
            if (_overlapCandidates == null || _overlapCandidates.Count == 0)
                return;

            long discStart = si.FsFile.FsOffset + si.File.OffsetInItem;
            long chunkSize = si.File.FsSize;
            long discEnd = discStart + chunkSize;

            // Linear scan: activate new candidates whose range intersects this chunk
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
                        bool isContaining = _overlapWritePaths != null && _overlapWritePaths.Contains(entryImagePath);
                        _activeOverlaps[fullPath] = new OverlapHandler
                        {
                            File = candidate,
                            ImagePath = entryImagePath,
                            TotalSize = candidate.FsSize,
                            TotalWritten = 0,
                            LastDiscEnd = 0,
                            IsContaining = isContaining
                        };
                    }
                    else
                    {
                        // Split file re-entry: update to current extent
                        OverlapHandler oh = _activeOverlaps[fullPath];
                        oh.File = candidate;
                        oh.TotalSize += candidate.FsSize;
                        _activeOverlaps[fullPath] = oh;
                    }
                }

                _overlapScanIndex++;
            }

            // Write data to ALL active overlaps that intersect this chunk
            if (_activeOverlaps.Count > 0)
            {
                List<string> activeKeys = new List<string>(_activeOverlaps.Keys);
                foreach (string key in activeKeys)
                {
                    OverlapHandler state = _activeOverlaps[key];
                    IFsFile file = state.File;

                    long intersectStart = Math.Max(discStart, file.FsOffset);
                    long intersectEnd = Math.Min(discEnd, file.FsOffset + file.FsSize);

                    if (intersectEnd <= intersectStart)
                        continue;

                    // For containing files: prevent duplicate writes when multiple section
                    // items in the same buffer cover overlapping disc ranges
                    if (state.IsContaining && intersectStart < state.LastDiscEnd)
                    {
                        intersectStart = state.LastDiscEnd;
                        if (intersectEnd <= intersectStart)
                            continue;
                    }

                    int offsetInBuffer = (int)(si.File.FsOffset + (intersectStart - discStart));
                    int writeSize = (int)(intersectEnd - intersectStart);

                    // Close any in-progress regular file, then write through the handler.
                    // The handler supports multi-part writes: real handler seeks-to-end,
                    // test handler combines CRCs on close.
                    base.ExtractHandler.CloseFs();
                    base.ExtractHandler.WriteFs(section, _context.WritePath, state.ImagePath,
                        state.TotalWritten, offsetInBuffer, writeSize, state.TotalSize, false);

                    state.TotalWritten += writeSize;
                    state.LastDiscEnd = intersectEnd;
                    _activeOverlaps[key] = state;
                }
            }

            // Close completed overlaps
            if (_activeOverlaps.Count > 0)
            {
                List<string> completed = new List<string>();
                foreach (KeyValuePair<string, OverlapHandler> kvp in _activeOverlaps)
                {
                    if (kvp.Value.TotalWritten >= kvp.Value.TotalSize)
                        completed.Add(kvp.Key);
                }
                foreach (string path in completed)
                {
                    // Ensure the handler flushes this file
                    base.ExtractHandler.CloseFs();
                    _activeOverlaps.Remove(path);
                    _extracted++;
                    _fsExtracted++;
                }
            }
        }

        private void BuildOverlapCandidates(ISection section)
        {
            _activeOverlaps = new Dictionary<string, OverlapHandler>(StringComparer.OrdinalIgnoreCase);
            _overlapScanIndex = 0;
            _overlapWritePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IFileSystemData fsData = ((ISectionProcessor)section).FileSystemData;
            if (fsData?.FidelityFiles == null)
            {
                _overlapCandidates = new List<IFsFile>();
                return;
            }

            HashSet<IFsFile> candidateSet = new HashSet<IFsFile>(_candidates);
            _candidateSet = candidateSet;

            // Identify "containing" candidates: candidates whose extent range encompasses
            // other candidates at different offsets (FsSize extends past the next file's start).
            HashSet<string> containingCandidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _candidates.Count - 1; i++)
            {
                IFsFile c = _candidates[i];
                IFsFile next = _candidates[i + 1];
                if (c.FsOffset + c.FsSize > next.FsOffset)
                    containingCandidateNames.Add(c.FullName);
            }

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            // Add displaced entries from FidelityFileList (files not in OrderedList)
            foreach (IFsFile f in fsData.FidelityFiles.Entries)
            {
                if (f.FsSize == 0 || f.IsSystemFile)
                    continue;
                if (candidateSet.Contains(f))
                    continue;
                if (!_mask.IsMatch(f.FullName))
                    continue;
                if (_preferredFsType > FsType.Iso9660 && f is Nanook.NKit.Iso.Iso9660.FstFile fst && fst.Links.Count > 0)
                {
                    if (fst.Links[0].FsType < _preferredFsType)
                    {
                        // Don't filter out collision entries that represent genuinely different
                        // files (different name AND different size at same offset).
                        // Same-name entries with different sizes are just different FS views of
                        // the same logical file (e.g., ISO9660 vs UDF size discrepancies due to
                        // CDXA sectors) and should be filtered.
                        IFsFile orderedEntry = section.AreaFileSystem?.Primary.Files.FirstOrDefault(c => c.FsOffset == f.FsOffset);
                        if (orderedEntry == null || orderedEntry.FsSize == f.FsSize
                            || string.Equals(orderedEntry.Name, f.Name, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                }
                overlapCandidates.Add(f);
            }

            // Add containing candidates — their section items only cover partial data because
            // the buffer preprocessor skips them once the next file's offset is reached.
            // ProcessOverlaps collects all data within their extent range. writeFs is skipped.
            if (containingCandidateNames.Count > 0)
            {
                foreach (IFsFile c in _candidates)
                {
                    if (containingCandidateNames.Contains(c.FullName))
                    {
                        overlapCandidates.Add(c);
                        string entryPath = getPath(section, c.Path);
                        string imagePath = Path.Combine(entryPath, c.Name);
                        _overlapWritePaths.Add(imagePath);
                    }
                }
            }

            _overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
        }

        private string getPath(ISection section, string path) =>
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

            // For split files, never replace — overlap data may have been written before writeFs
            bool replace = isSplit ? false : newFile;

            if (base.ExtractHandler.WriteFs(section, rootPath, imagePath, pos, fsOffset, sizeFs, fsFullSize, replace))
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