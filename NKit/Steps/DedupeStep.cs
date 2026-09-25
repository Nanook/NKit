using Nanook.NKit.Configuration;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    internal class DedupeStep : StepBase, IStep, IDisposable
    {
        private IStepContext _context;
        private string _outName;
        private string _dataStorePath;
        private bool _hasNonPatchedData;
        private string _splitSetName; // Per-game split store name for Xbox/Xbox360 dual mode

        // System formatter (required) - owns its own datastore/image writer
        private IDataStoreSystemFormatter _dataStoreFormatter;
        private XXHash64 _fullImageHasher;

        private Dictionary<long, Stream> _activeFileStreams; // FsOffset -> Stream for files being written across multiple Process() calls
        private Dictionary<long, FileWriteContext> _fileContexts; // Track file metadata

        private class FileWriteContext
        {
            public long FsOffset { get; set; }
            public ulong ExpectedXxHash { get; set; }
            public long BytesWritten { get; set; }
            public long ImageOffsetStart { get; set; }
        }

        internal override bool ContractReqPatch => false;
        internal override bool ContractReqChk => true;
        internal override bool ContractFullScan => true;
        internal override bool ContractIsLossy => false;
        internal override bool ContractIsExpand => true;
        internal override bool ContractIsFix => false;
        internal override OutputType ContractOutputType => OutputType.FileStore;
        internal override bool ContractCanCrc => true;
        internal override bool ContractCanHash => true;
        internal override string ComponentTag => Nanook.NKit.LogScopes.StepDedupe;

        public override string ProposedName() => _outName;

        private void cleanupFormatterState()
        {
            if (_activeFileStreams != null)
            {
                foreach (KeyValuePair<long, Stream> kvp in _activeFileStreams.ToList())
                    try { kvp.Value?.Dispose(); } catch { }

                _activeFileStreams.Clear();
            }

            _fileContexts?.Clear();

            try { _fullImageHasher?.Dispose(); } catch { }
            _fullImageHasher = null;

            try { _dataStoreFormatter?.Dispose(); } catch { }
            _dataStoreFormatter = null;

            // Shutdown pooled readers so OS handles are released. Runs on both success and abort paths.
            try { ImageReaderPool.Shutdown(); } catch { }
            try { GC.Collect(); GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(50); } catch { }
        }

        public void Dispose() => cleanupFormatterState();

        internal DedupeStep(IStepContextConstruct context)
        {
            base.CheckContract(context.StepInfo);
            _outName = context.SourceImageName;
        }

        public override void Initialise(IStepContext context)
        {
            cleanupFormatterState();
            _splitSetName = null; // Reset per-image split store name for Xbox/Xbox360 dual mode

            // Force-close any pooled readers that may have survived from a
            // previous image's dedupe step before we try to open the datastore again.
            try { ImageReaderPool.Shutdown(); } catch { }

            base.Initialise(context);

            _context = context;

            // Ensure the Scan object's area accumulation state is clean for this image.
            // When aux mode is active and multiple images are processed against the same
            // DataStore, the Scan's _currentArea and accumulated CRC state must start fresh
            // to prevent area CRC accumulator bleed from a previous image's computation.
            // Although the Scan is typically created fresh per image, this explicit reset
            // guarantees SectionProcessed() starts with clean state regardless of context reuse.
            _context.Scan?.ResetAreaAccumulationState();

            _fullImageHasher = XXHash64.Create();

            // Initialize helper dictionaries
            _activeFileStreams = new Dictionary<long, Stream>();
            _fileContexts = new Dictionary<long, FileWriteContext>();

            // Get the parsed dedupe configuration from system settings
            DedupeConfiguration dedupeConfig = (_context as NKitStepContext)?.AppSettings[_context.SystemType]?.DedupeConfig
                ?? new DedupeConfiguration();

            string dedupeTargetPath = _context.Settings.DedupePath;
            string dedupeDirectory = dedupeTargetPath;
            string configuredSetName = dedupeConfig.SetName;
            string setName;

            if (!string.IsNullOrWhiteSpace(dedupeTargetPath) && dedupeTargetPath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                dedupeDirectory = Path.GetDirectoryName(Path.GetFullPath(dedupeTargetPath)) ?? dedupeTargetPath;
                setName = string.IsNullOrWhiteSpace(configuredSetName)
                    ? Path.GetFileNameWithoutExtension(dedupeTargetPath)
                    : configuredSetName.Trim();
            }
            else
            {
                setName = (configuredSetName ?? _context.SystemType.ToString().ToLower()).Trim();
            }

            _dataStorePath = !string.IsNullOrWhiteSpace(dedupeTargetPath) && dedupeTargetPath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(dedupeTargetPath)
                : Path.Combine(dedupeDirectory, setName + DataStore.DatabaseFileExtension);

            long shardSize = dedupeConfig.ShardSize;
            int blockSize = dedupeConfig.BlockSize;

            // Only set block size when the datastore does not already exist for this system
            int blockSizeToUse = 0;
            int primaryBlockSize = 0;
            try
            {
                using (DataStore ds = new DataStore(dedupeDirectory))
                {
                    SetInfo info = ds.GetSetInfo(setName);
                    if (info == null)
                        blockSizeToUse = blockSize; // may be 0 which means use default
                    else
                        primaryBlockSize = info.BlockSize;
                }
            }
            catch { /* ignore - fall back to default behaviour */ }

            // Enforce aux block size when primary is new and an aux store exists
            if (primaryBlockSize == 0)
            {
                int? auxBlockSize = DataStore.GetAuxBlockSize(dedupeDirectory);
                if (auxBlockSize != null && auxBlockSize.Value != blockSizeToUse)
                {
                    if (blockSizeToUse != 0)
                    {
                        string msg = $"ERROR: Block size {blockSizeToUse} conflicts with aux store block size {auxBlockSize.Value}. Forcing block size to {auxBlockSize.Value}.";
                        Trace.TraceError(msg);
                        Console.Error.WriteLine(msg);
                    }
                    blockSizeToUse = auxBlockSize.Value;
                }
            }

            // Aux discovery — convention-based, no explicit path needed.
            // Check for {setName}.aux.nkds in the same directory as the primary set.
            // When the primary set already exists, validate block sizes match.
            // When the primary set is new, just check if the aux set exists (the formatter
            // will create the primary with a matching block size).
            string auxSetName = null;
            try
            {
                if (primaryBlockSize > 0)
                {
                    // First check if aux exists at all
                    string candidateAux = DataStore.ResolveAuxSetName(_dataStorePath);
                    if (candidateAux != null)
                    {
                        // Validate block sizes match — hard error if they don't
                        auxSetName = DataStore.ResolveAuxSetNameWithBlockSizeValidation(_dataStorePath, primaryBlockSize);
                        if (auxSetName == null)
                            throw new HandledException($"Aux store block size does not match primary store block size ({primaryBlockSize}). All stores in the same directory must use the same block size.");
                    }
                }
                else
                {
                    // Primary doesn't exist yet — still discover aux by convention.
                    // Block size validation will happen implicitly: the formatter creates
                    // the primary set with the configured block size, and the aux set was
                    // already created by the user with a matching block size.
                    auxSetName = DataStore.ResolveAuxSetName(_dataStorePath);
                }
            }
            catch (HandledException) { throw; }
            catch { /* ignore - fall back to primary-only mode */ }

            // Xbox/Xbox360 dual mode: ALWAYS set up both shared aux and split stores when AutoCreateAux is enabled,
            // regardless of whether a pre-existing aux was discovered. Xbox/Xbox360 uses system-type-derived
            // aux naming (not folder-name) and always needs a per-game split store.
            if (dedupeConfig.AutoCreateAux && (_context.SystemType == SystemType.XBox || _context.SystemType == SystemType.XBox360))
            {
                int auxBlockSize = primaryBlockSize > 0 ? primaryBlockSize : blockSizeToUse;

                // Xbox/Xbox360 dual mode: create BOTH a shared aux store AND a per-game split store.
                // Any user-supplied Aux_Filename is ignored for Xbox/Xbox360.
                // Any pre-discovered folder-name-based aux is overridden with system-type-based naming.

                // 1. Shared aux store: "{systemType}.aux" (e.g., "xbox360.aux") with standard sharding (50GB)
                string systemTypeName = _context.SystemType.ToString().ToLower(); // e.g. "xbox360"
                string sharedAuxName = systemTypeName + DataStore._AuxSetSuffix; // e.g. "xbox360.aux"

                // Check if a shared aux store already exists (case-insensitive match for system-type pattern)
                string existingAux = DataStore.ResolveAuxSetName(_dataStorePath);
                if (existingAux != null)
                {
                    // A shared aux store already exists — reuse it regardless of case/naming.
                    // Xbox always uses one shared aux per directory; don't create duplicates.
                    auxSetName = existingAux;
                }
                else
                {
                    // Create the system-type-based shared aux (ignore any pre-existing folder-name aux)
                    // Validate filename length
                    string auxFileNameFull = sharedAuxName + DataStore.DatabaseFileExtension; // "xbox360.aux.nkds"
                    if (auxFileNameFull.Length > 255)
                        throw new HandledException($"Shared aux store filename '{auxFileNameFull}' exceeds 255 characters.");

                    try
                    {
                        using (DataStore ds = new DataStore(dedupeDirectory))
                        {
                            if (ds.GetSetInfo(sharedAuxName) == null)
                                ds.CreateSet(sharedAuxName, 50L * 1024 * 1024 * 1024, auxBlockSize); // standard sharding (50GB)
                        }
                        auxSetName = sharedAuxName;
                    }
                    catch (InvalidOperationException)
                    {
                        // Shared aux set was created concurrently — reuse without modification
                        auxSetName = sharedAuxName;
                    }
                    catch (Exception ex)
                    {
                        throw new HandledException($"Failed to create shared aux store '{sharedAuxName}': {ex.Message}");
                    }
                }

                // 2. Per-game split store: "{gameName}.split" with embedded mode (shardSize=0)
                // Use the source image name (game name) for the split store, not the primary set name.
                // The split is per-game, so each game gets its own isolated filler store.
                // Fall back to setName if _outName isn't available (e.g. UI pre-creation).
                string gameBaseName = !string.IsNullOrEmpty(_outName)
                    ? Path.GetFileNameWithoutExtension(_outName)
                    : setName;
                string splitName = gameBaseName + DataStore._SplitSetSuffix; // e.g. "batte.split"
                string splitSetName;

                // Idempotent reuse: check if the split store already exists before creating
                string existingSplit = DataStore.ResolveSplitSetName(dedupeDirectory, gameBaseName);
                if (existingSplit != null)
                {
                    // Split store already exists — reuse without modification
                    splitSetName = existingSplit;
                }
                else
                {
                    // Validate filename length
                    string splitFileName = splitName + DataStore.DatabaseFileExtension; // "gamename.split.nkds"
                    if (splitFileName.Length > 255)
                        throw new HandledException($"Split store filename '{splitFileName}' exceeds 255 characters.");

                    try
                    {
                        using (DataStore ds = new DataStore(dedupeDirectory))
                            ds.CreateSet(splitName, 0, auxBlockSize); // shardSize=0 → embedded mode
                        splitSetName = splitName;
                    }
                    catch (InvalidOperationException)
                    {
                        // Split set was created concurrently — reuse without modification
                        splitSetName = splitName;
                    }
                    catch (Exception ex)
                    {
                        throw new HandledException($"Failed to create split store '{splitName}': {ex.Message}");
                    }
                }

                // Store the split set name for passing to the formatter later
                _splitSetName = splitSetName;
            }
            // Auto-create aux store for non-Xbox systems if configured and none exists
            else if (dedupeConfig.AutoCreateAux && auxSetName == null)
            {
                int auxBlockSize = primaryBlockSize > 0 ? primaryBlockSize : blockSizeToUse;

                switch (_context.SystemType)
                {
                    case SystemType.Wii:
                    case SystemType.WiiU:
                        // Shared aux store: use user-supplied Aux_Filename if provided,
                        // otherwise derive from the containing folder name.
                        string auxBaseName;
                        if (!string.IsNullOrWhiteSpace(dedupeConfig.AuxFilename))
                            auxBaseName = dedupeConfig.AuxFilename.Trim();
                        else
                            auxBaseName = ResolveFolderName(dedupeDirectory);
                        string auxName = auxBaseName + DataStore._AuxSetSuffix; // e.g. "Wii.aux" or "MyCollection.aux"

                        // Validate filename length
                        string auxFileNameStr = auxName + DataStore.DatabaseFileExtension; // "Wii.aux.nkds"
                        if (auxFileNameStr.Length > 255)
                            throw new HandledException($"Aux store filename '{auxFileNameStr}' exceeds 255 characters.");

                        try
                        {
                            using (DataStore ds = new DataStore(dedupeDirectory))
                                ds.CreateSet(auxName, 50L * 1024 * 1024 * 1024, auxBlockSize);
                            auxSetName = auxName;
                        }
                        catch (InvalidOperationException)
                        {
                            // Aux set already exists — reuse without modification
                            auxSetName = auxName;
                        }
                        catch (Exception ex)
                        {
                            throw new HandledException($"Failed to create aux store '{auxName}': {ex.Message}");
                        }
                        break;

                    default:
                        // Other systems (GameCube, PS1, PS2, Dreamcast, etc.) — no aux store created
                        break;
                }
            }

            // create formatter which manages its own datastore / image writer
            switch (_context.SystemType)
            {
                case SystemType.GameCube:
                    _dataStoreFormatter = new DataStoreGameCubeFormatter(dedupeDirectory, _outName, shardSize, _context, blockSizeToUse, setName);
                    break;
                case SystemType.Wii:
                    _dataStoreFormatter = new DataStoreWiiFormatter(dedupeDirectory, _outName, shardSize, _context, blockSizeToUse, setName, auxSetName);
                    break;
                case SystemType.WiiU:
                    _dataStoreFormatter = new DataStoreWiiUFormatter(dedupeDirectory, _outName, shardSize, _context, blockSizeToUse, setName, auxSetName);
                    break;
                case SystemType.XBox:
                case SystemType.XBox360:
                    _dataStoreFormatter = new DataStoreXboxFormatter(dedupeDirectory, _outName, shardSize, _context, blockSizeToUse, setName, auxSetName, _splitSetName);
                    break;
                case SystemType.PS1:
                case SystemType.PS2:
                case SystemType.PS3:
                case SystemType.PSP:
                case SystemType.SegaCD:
                case SystemType.Saturn:
                case SystemType.Dreamcast:
                case SystemType.CDi:
                case SystemType.Default:
                case SystemType.PcEngine:
                    _dataStoreFormatter = new DataStoreIso9660Formatter(dedupeDirectory, _outName, shardSize, _context, blockSizeToUse, setName);
                    break;
                default:
                    throw new NotSupportedException($"No IDataStoreSystemFormatter available for system type '{_context.SystemType}'. Dedupe requires a formatter for this system.");
            }
        }

        public override void Process(ISection section)
        {
            // RawKeyMissing means either (a) no key at all, or (b) the key exists but the
            // CDN content is a raw system binary (not a WiiU FST container). In case (b) we
            // still have the key set on SourceFile.Key and can store the encrypted bytes
            // verbatim. Only throw when the key is genuinely absent.
            if (section.Type == AreaType.RawKeyMissing)
            {
                if (_context.SourceFile?.Key == null)
                {
                    try { cleanupFormatterState(); } catch { }
                    throw new Exception("Missing Key required to dedupe image");
                }
                // Raw CDN binary: hash and store as-is, no FST deduplication.
                if (section.Encrypted != null)
                    _fullImageHasher.TransformBlock(section.Encrypted, 0, (int)section.Size, null, 0);
                _dataStoreFormatter.ProcessSection(section);
                base.Process(section);
                return;
            }

            if (section.Encrypted != null)
                _fullImageHasher.TransformBlock(section.Encrypted, 0, (int)section.Size, null, 0);

            _dataStoreFormatter.ProcessSection(section);
            base.Process(section);
            saveFileData(section, false);
        }

        public void Patched(ISection section)
        {
        }

        // Called by ProcessResultsAsExceptioned() on the abort/error path.
        // Guarantees cleanupFormatterState() runs so the SQLite write-lock
        // (SemaphoreSlim) is always released even when processing throws.
        public override void processResults()
        {
            try
            {
                base.processResults();
            }
            finally
            {
                cleanupFormatterState();
            }
        }

        public override void ProcessResults()
        {
            base.ProcessingComplete();

            try
            {
                // Close any remaining active streams
                foreach (KeyValuePair<long, Stream> kvp in _activeFileStreams.ToList())
                {
                    kvp.Value?.Dispose();
                    _activeFileStreams.Remove(kvp.Key);
                }

                // Let formatter create areas
                Scan scan = _context?.Scan;
                if (scan != null)
                    _dataStoreFormatter.CreateAreas(scan.Areas);

                // Persist filesystem.yaml into the datastore (always)
                if (scan != null)
                {
                    try
                    {
                        _dataStoreFormatter.BuildFileSystemYaml(scan);
                    }
                    catch (Exception ex)
                    {
                        _context.Log.Error(() => $"Failed to write filesystem.yaml: {ex.Message}");
                    }
                }

                // Finalize image via formatter
                // TODO: Change this so it's calculated by the basestep and make it configurable to be each file or whole image
                _fullImageHasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                ulong xxHash = _fullImageHasher.HashUInt64;

                Checksums dataStoreChecksums = new Checksums()
                {
                    Size = _context.Scan.Size,
                    Crc = _context.Scan.Crc
                };
                if (xxHash != 0)
                    dataStoreChecksums.XxHash = xxHash;

                _dataStoreFormatter.FinalizeImage(_context.Scan.Size, _context.Scan.Crc, xxHash);

                if (_dataStoreFormatter.AlreadyExists)
                {
                    // The image was already in the set (same name + checksums) so FinalizeImage rolled
                    // it back rather than storing a duplicate. Do NOT wire up the post-step verify — the
                    // image no longer exists in the store, so verifying it would read a zero/absent image
                    // (the historic "CRC 0" / stuck-row symptom). Flag it so the result reports AlreadyExists.
                    if (_context is NKitStepContext sc)
                        sc.ImageAlreadyExists = true;
                    _context.Log.Info(() => $"Image '{_context?.SourceFile?.Name}' already exists in the set - not added (rolled back).");
                }
                else
                {
                    // Pass the original input filename (if available) so the DataStore proxy
                    // source can disambiguate images by contained files (e.g., tmd.x).
                    // Prefer the index file name (e.g., tmd.x) when available; otherwise fall back to the first image part name.
                    string originalFile = _context?.SourceFile?.IndexFile?.NameOnly
                        ?? _context?.SourceFile?.ImageFiles?.FirstOrDefault()?.NameOnly;
                    (_context as NKitStepContext)?.SetNextVerifySourceFile(NKitTask.createDataStoreSourceFile(_dataStorePath, _dataStoreFormatter.ImageFileName, originalFile));
                    (_context as NKitStepContext)?.SetNextVerifySourceParts(new Parts(dataStoreChecksums));
                }

                base.ProcessResults();
            }
            catch (Exception ex)
            {
                _context.Log.Error(() => $"Error finalizing DataStore image: {ex.Message}");
            }
            finally
            {
                cleanupFormatterState();
            }
        }

        private void saveFileData(ISection section, bool patch)
        {
            // Save files using system formatter
            if (section.Type != AreaType.FileSystem)
                return;

            DataStride stride = null;
            if (section.AreaInfo.BlockSize != section.AreaInfo.BlockFsSize)
                stride = new DataStride() { SourceBlockSize = section.AreaInfo.BlockSize, DataOffset = section.AreaInfo.BlockFsOffset, DataLength = section.AreaInfo.BlockFsSize };

            ////////////////////////////
            // Store the files
            foreach (ISectionItem si in section.Items)
            {
                IFsFile f = si.FsFile;

                if (si.File == null || !_dataStoreFormatter.ShouldPreserveFile(section, f)) // Skip if no file or should not be preserved (junk files, zero-length files)
                    continue;

                // Start of new file
                if (si.File.OffsetInItem == 0)
                {
                    // Close any existing stream for this offset (shouldn't happen, but be safe)
                    if (_activeFileStreams.ContainsKey(f.FsOffset))
                    {
                        _context.Log.Info(() => $"Aborting incomplete file at offset {f.FsOffset}");
                        _activeFileStreams[f.FsOffset]?.Dispose();
                        _activeFileStreams.Remove(f.FsOffset);
                        _fileContexts.Remove(f.FsOffset);
                    }

                    _hasNonPatchedData = !((ISectionProcessor)section).PatchInfo.MarkForPatching || patch;

                    if (_hasNonPatchedData)
                    {
                        try
                        {
                            BlockType blockType = f.IsSystemFile ? BlockType.FileSystem : BlockType.File;

                            // Determine partition base image offset and stride (if any)
                            long partitionImageOffset = section.AreaInfo?.ImageOffset ?? section.ImageOffset;

                            // Compute absolute image offset and open stream via formatter
                            long imageOffsetStart = _dataStoreFormatter.ToImageOffsetFromFsOffsets(partitionImageOffset, f.FsOffset);

                            Stream fileStream = _dataStoreFormatter.BeginFileWrite(imageOffsetStart, blockType, stride, partitionImageOffset);

                            _activeFileStreams[f.FsOffset] = fileStream;
                            _fileContexts[f.FsOffset] = new FileWriteContext
                            {
                                FsOffset = f.FsOffset,
                                ExpectedXxHash = f.XxHash,
                                BytesWritten = 0,
                                ImageOffsetStart = imageOffsetStart
                            };
                        }
                        catch (Exception ex)
                        {
                            _context.Log.Error(() => $"Failed to create stream for file {f.FullName}: {ex.Message}");
                            throw;
                        }
                    }
                }

                _hasNonPatchedData |= !((ISectionProcessor)section).PatchInfo.MarkForPatching || patch;

                // Write file data if we have an active stream
                if (_hasNonPatchedData && _activeFileStreams.ContainsKey(f.FsOffset))
                {
                    try
                    {
                        Stream fileStream = _activeFileStreams[f.FsOffset];
                        FileWriteContext context = _fileContexts[f.FsOffset];

                        long beforePos = fileStream.Position;
                        section.Read((int)si.File.FsOffset, (int)si.File.FsSize, fileStream);
                        long bytesWritten = fileStream.Position - beforePos;

                        context.BytesWritten += bytesWritten;

                    }
                    catch (Exception ex)
                    {
                        _context.Log.Error(() => $"Failed to write data for file {f.FullName}: {ex.Message}");
                        throw;
                    }
                }

                // Complete file when all data has been written
                if (si.File.OffsetInItem + si.File.FsSize == si.FsFile.FsSize)
                {
                    if (!_hasNonPatchedData)
                    {
                        // Patched file - abort
                        if (_activeFileStreams.ContainsKey(f.FsOffset))
                        {
                            _activeFileStreams[f.FsOffset]?.Dispose();
                            _activeFileStreams.Remove(f.FsOffset);
                            _fileContexts.Remove(f.FsOffset);
                        }
                    }
                    else if (_activeFileStreams.ContainsKey(f.FsOffset))
                    {
                        try
                        {
                            Stream fileStream = _activeFileStreams[f.FsOffset];
                            FileWriteContext context = _fileContexts[f.FsOffset];

                            _dataStoreFormatter.FinalizeFileWrite(context.ImageOffsetStart, fileStream);

                            _activeFileStreams.Remove(f.FsOffset);
                            _fileContexts.Remove(f.FsOffset);

                            // Optional: Verify XXHash if needed - not implemented here
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }
            }

            // Persist block padding once per section — section contains all block padding encoded to be stored at the section position.
            if (section.Status == CompletionStatus.Complete) //only when not to be patched or has being patched
                _dataStoreFormatter.FinaliseSectionAndPersistBlockPadding(section.ImageOffset, section, section.Type == AreaType.FileSystem, stride);
        }

        /// <summary>
        /// Extracts the leaf directory name from a base directory path.
        /// Used as the default aux set name for Wii/WiiU when no explicit Aux_Filename is provided.
        /// </summary>
        /// <param name="baseDirectory">The base directory path (e.g., "D:\Games\Wii\" or "D:\Games\Wii")</param>
        /// <returns>The leaf folder name (e.g., "Wii")</returns>
        /// <exception cref="HandledException">Thrown when the path has no folder name (e.g., root paths like "D:\" or "/")</exception>
        private static string ResolveFolderName(string baseDirectory)
        {
            // Path.GetFileName on a directory path returns the leaf folder name
            // e.g., "D:\Games\Wii\" → "Wii", "D:\Games\Wii" → "Wii"
            string name = Path.GetFileName(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                throw new HandledException("Cannot derive aux filename: base directory has no folder name");
            return name;
        }
    }
}