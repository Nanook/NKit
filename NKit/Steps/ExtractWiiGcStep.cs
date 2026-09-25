using Nanook.NKit.Configuration;
using Nanook.NKit.Nintendo;
using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;

namespace Nanook.NKit
{

    internal class ExtractWiiGcStep : StepBase, IStep
    {
        private IStepContext _context;
        private string _outName;
        private FileMask _mask;
        private List<IFsFile> _candidates;

        private ImageHeader _header;
        private byte[] _prtHeader;
        private PartitionType _ptnType;
        private long _prtImageOffset;
        private string _ptnDirName;
        private int _extracted;
        private const string _sysDir = "sys";
        private const string _filesDir = "files";

        private bool _forensic;

        // Overlap tracking for shared-offset and range-overlapping files
        private Dictionary<string, OverlapState> _activeOverlaps;
        private List<IFsFile> _overlapCandidates;
        private int _overlapScanIndex;

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
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepExtractWiiGc;

        public override string ProposedName() => _outName;

        internal ExtractWiiGcStep(IStepContextConstruct context)
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

            // Store forensic flag from configuration
            _forensic = config.IsForensic;

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

            if (!_forensic) // No support for filtering in forensic mode
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

            _ptnDirName = "";
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapCandidates = null;
            _overlapScanIndex = 0;
        }

        public override void Process(ISection section)
        {
            base.Process(section);

            if (section.Type == AreaType.ImageHeader)
                _header = new ImageHeader(section.Decrypted.Read(0, (int)section.Size), _context.SystemType == SystemType.Wii); //clone
            else if (section.Type == AreaType.PartitionHeader)
            {
                _prtHeader = section.Decrypted.Read(0, (int)section.Size); //clone
                _ptnType = _header.GetPartition(section.ImageOffset).Type;
                if (_ptnType == PartitionType.Game)
                    _ptnDirName = "DATA";
                else if (_ptnType == PartitionType.Update || _ptnType == PartitionType.Channel)
                    _ptnDirName = _ptnType.ToString().ToUpper();
                else if ((int)_ptnType < 0x100)
                    _ptnDirName = $"P{(int)_ptnType}";
                else
                    _ptnDirName = $"P-{SourceFiles.CleanseFileName(Encoding.ASCII.GetString(((uint)_ptnType).ToBytesBE()))}";
            }
            else
                saveFileData(section);
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

        private void saveFileData(ISection section)
        {
            if (section.Type == AreaType.FileSystem)
            {
                if (section.AreaOffset == 0) //start of the fs (FST will have been parsed)
                {
                    _prtImageOffset = section.ImageOffset;

                    // Reset overlap state on new area/filesystem
                    if (_activeOverlaps != null)
                    {
                        foreach (OverlapState state in _activeOverlaps.Values)
                            state.Stream?.Dispose();
                        _activeOverlaps.Clear();
                    }
                    _overlapCandidates = null;
                    _overlapScanIndex = 0;

                    IFileSystemView fsView = section.AreaFileSystem?.Primary;
                    if (fsView != null)
                    {
                        _candidates = fsView.Files.Where(a => _mask.IsMatch($"/{_ptnDirName}/{(a.IsSystemFile ? _sysDir : _filesDir)}{a.Path}/{(a.IsSystemFile ? a.Name.Substring(2) : a.Name)}")).OrderBy(a => a.FsOffset).ToList();

                        if (_candidates.Count != 0 && _candidates.Count == fsView.FileCount)
                            createEmptyFolders(_context.WritePath, Path.Combine(_ptnDirName, _filesDir), fsView.Root);

                        // Build overlap candidates
                        BuildOverlapCandidates(section);

                        if (_context.SystemType == SystemType.Wii)
                        {
                            PartitionHeader ph = new PartitionHeader(WiiConsts.PublicKeyModulus, WiiConsts.PublicKeyModulusRvtR, WiiConsts.PublicKeyExponent, _prtHeader, 0);
                            bool isRvt = section.Decrypted.Read8(WiiConsts.WiiPrtHdrKoreanOffset) == 1 && Nanook.NKit.Nintendo.WiiGc.FileSystemInfo.GetIssuer(section.Decrypted) != WiiConsts.RvtIssuer;
                            bool isRvtH = section.Decrypted.ReadUInt32B(WiiConsts.WiiPrtHdrPtnSizeOffset) << 2 == 0;

                            byte[] cert = null;
                            using (MemoryStream crtStream = new MemoryStream()) //cert.bin
                            {
                                foreach (SignedData crt in ph.CertValidator.Items)
                                    crtStream.Write(crt.Data, 0, crt.Data.Length);
                                cert = crtStream.ToArray();
                            }

                            var sysFiles = new[]
                            {
                                new { Name = $"header.bin", Path = $"/{_ptnDirName}/disc/", Data = _header.Data.Read(0, 0x100) },
                                new { Name = $"region.bin", Path = $"/{_ptnDirName}/disc/", Data = _header.Data.Read(WiiConsts.WiiDiscHdrRgnOffset, WiiConsts.WiiDiscHdrRgn2Size * 2) },
                                new { Name = $"ticket.bin", Path = $"/{_ptnDirName}/", ph.CertValidator.Ticket.Data },
                                new { Name = $"tmd.bin", Path = $"/{_ptnDirName}/", ph.CertValidator.Tmd.Data },
                                new { Name = $"cert.bin", Path = $"/{_ptnDirName}/", Data = cert },
                                new { Name = $"h3.bin", Path = $"/{_ptnDirName}/", Data = isRvtH ? null : _prtHeader.Read((int)(_prtHeader.ReadUInt32B(WiiConsts.WiiPrtHdrH3PtrOffset) << 2), WiiConsts.WiiPrtHdrH3Size) },
                            };

                            foreach (var sysFile in sysFiles)
                            {
                                if (sysFile.Data != null && _mask.IsMatch(sysFile.Path + sysFile.Name))
                                {
                                    string imagePath = Path.GetDirectoryName(sysFile.Path.Substring(1));
                                    base.ExtractHandler.CreateDirectory(_context.WritePath, imagePath);
                                    base.ExtractHandler.WriteBytes(_context.WritePath, Path.Combine(imagePath, sysFile.Name), sysFile.Data); //header.bin
                                    _extracted++;
                                }
                            }
                        }
                        else //gc
                        {
                        }
                    }
                }

                foreach (SectionItem si in section.Items)
                {
                    if (si.FsFile == null)
                        continue;

                    if (_candidates != null && _candidates.Count == 0 && _activeOverlaps.Count == 0)
                        break;

                    IFsFile match = _candidates?.FirstOrDefault(a => a == si.FsFile);
                    if (match != null)
                    {
                        string path = Path.Combine(_ptnDirName, si.FsFile.IsSystemFile ? _sysDir : _filesDir, si.FsFile.Path.TrimStart('/'));

                        base.ExtractHandler.CreateDirectory(_context.WritePath, path);

                        string imagePath = Path.Combine(path, si.FsFile.IsSystemFile ? si.FsFile.Name.Substring(2) : si.FsFile.Name);

                        if (base.ExtractHandler.WriteFs(section, _context.WritePath, imagePath, si.File.OffsetInItem, (int)si.File.FsOffset, (int)si.File.FsSize, si.FsFile.FsSize, true))
                            _extracted++;

                        ProcessOverlaps(section, si);

                        if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize) //is this the end of the file
                            _candidates.Remove(match);
                    }
                }

                if (_candidates != null)
                {
                    if (_candidates.Count == 0 && _activeOverlaps.Count == 0)
                    {
                        long nextArea = _context.ImageInfo.SourceAreas?.FirstOrDefault(a => a.ImageOffset > section.ImageOffset)?.ImageOffset ?? long.MaxValue;
                        _context.SkipToImageOffsetSet(nextArea); //next area/WipePartition
                    }
                    else if (_activeOverlaps.Count == 0)
                    {
                        long newOff = _prtImageOffset + Buffer.FsOffsetToOffset(_candidates[0].FsOffset, section.AreaInfo.BlockSize, section.AreaInfo.BlockFsOffset, section.AreaInfo.BlockFsSize, false);
                        if (newOff > section.ImageOffset + section.Size)
                            _context.SkipToImageOffsetSet(newOff); //skip within WipePartition
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

            while (_overlapScanIndex < _overlapCandidates.Count)
            {
                IFsFile candidate = _overlapCandidates[_overlapScanIndex];
                if (candidate.FsOffset >= discEnd)
                    break;

                if (candidate.FsOffset + candidate.FsSize > discStart)
                {
                    string entryPath = Path.Combine(_ptnDirName, candidate.IsSystemFile ? _sysDir : _filesDir, candidate.Path.TrimStart('/'));
                    string entryName = candidate.IsSystemFile ? candidate.Name.Substring(2) : candidate.Name;
                    string entryImagePath = Path.Combine(entryPath, entryName);
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
            }
        }

        private void BuildOverlapCandidates(ISection section)
        {
            _activeOverlaps = new Dictionary<string, OverlapState>(StringComparer.OrdinalIgnoreCase);
            _overlapScanIndex = 0;

            // Build a set of FullNames already in the candidate list
            HashSet<string> candidateFullNames = new HashSet<string>(
                _candidates.Select(a => a.FullName),
                StringComparer.OrdinalIgnoreCase);

            // Build a lookup of primary candidate sizes by FsOffset for FstLink merge exclusion
            Dictionary<long, long> primarySizeByOffset = new Dictionary<long, long>();
            foreach (IFsFile c in _candidates)
            {
                if (!primarySizeByOffset.ContainsKey(c.FsOffset))
                    primarySizeByOffset[c.FsOffset] = c.FsSize;
            }

            List<IFsFile> overlapCandidates = new List<IFsFile>();

            // Check FidelityFileList if available
            IFileSystemData fsData = ((ISectionProcessor)section).FileSystemData;
            if (fsData?.FidelityFiles != null)
            {
                foreach (IFsFile f in fsData.FidelityFiles.Entries)
                {
                    if (f.IsSystemFile)
                        continue;

                    string fFullMaskName = $"/{_ptnDirName}/{_filesDir}{f.Path}/{f.Name}";
                    if (!_mask.IsMatch(fFullMaskName))
                        continue;

                    if (candidateFullNames.Contains(f.FullName))
                        continue;

                    // Exclude same-size FstLink merges
                    if (primarySizeByOffset.TryGetValue(f.FsOffset, out long primarySize) && f.FsSize == primarySize)
                        continue;

                    // Handle zero-byte entries immediately
                    if (f.FsSize == 0)
                    {
                        string entryPath = Path.Combine(_ptnDirName, f.IsSystemFile ? _sysDir : _filesDir, f.Path.TrimStart('/'));
                        base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                        string entryImagePath = Path.Combine(entryPath, f.Name);
                        base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                        _extracted++;
                        continue;
                    }

                    overlapCandidates.Add(f);
                }
            }
            else
            {
                // No FidelityFileList — identify overlap candidates from _candidates themselves
                // (files sharing FsOffset with another candidate where the other is larger)
                IEnumerable<IGrouping<long, IFsFile>> offsetGroups = _candidates.GroupBy(a => a.FsOffset).Where(g => g.Count() > 1);
                foreach (IGrouping<long, IFsFile> group in offsetGroups)
                {
                    // The largest file wins the primary slot; smaller files are overlaps
                    long maxSize = group.Max(a => a.FsSize);
                    foreach (IFsFile f in group.Where(a => a.FsSize < maxSize && a.FsSize > 0))
                    {
                        // Handle zero-byte entries immediately (shouldn't appear due to filter, but safety)
                        if (f.FsSize == 0)
                        {
                            string entryPath = Path.Combine(_ptnDirName, f.IsSystemFile ? _sysDir : _filesDir, f.Path.TrimStart('/'));
                            base.ExtractHandler.CreateDirectory(_context.WritePath, entryPath);
                            string entryName = f.IsSystemFile ? f.Name.Substring(2) : f.Name;
                            string entryImagePath = Path.Combine(entryPath, entryName);
                            base.ExtractHandler.WriteBytes(_context.WritePath, entryImagePath, Array.Empty<byte>());
                            _extracted++;
                            continue;
                        }

                        overlapCandidates.Add(f);
                        _candidates.Remove(f); // Remove from primary candidates — will be handled via overlap tracking
                    }
                }
            }

            // Sort by FsOffset for efficient activation scanning
            _overlapCandidates = overlapCandidates.OrderBy(f => f.FsOffset).ToList();
        }

        private void createEmptyFolders(string rootPath, string imagePath, IFsFolder dir)
        {
            if (dir.Files.Count == 0 && dir.Folders.Count == 0)
                base.ExtractHandler.CreateDirectory(rootPath, Path.Combine(imagePath, dir.Path.Substring(1)));

            foreach (IFsFolder d in dir.Folders)
                createEmptyFolders(rootPath, imagePath, d);
        }

    }
}