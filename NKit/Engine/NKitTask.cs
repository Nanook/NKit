using Nanook.NKit.Configuration;
using Nanook.NKit.Container;
using Nanook.NKit.Dats;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    internal class NKitTask
    {
        public const string ScanExt = "nkit.yaml";
        internal const string _KeyExt = "dkey";

        public NKitTaskResults Results { get; private set; }
        public SystemType SystemType { get; private set; }
        private SourceFile _sourceFile; //the original source file
        private NKitTaskContext _context;
        private bool _deleteSourceFileSet;
        private bool _deleteSourceFileCandidate;
        private string _skipName;
        private DateTime _started;
        private NKitVerify _verify;
        private bool _skipImage;
        public string TempPath { get; private set; }
        public string ScanPath { get; private set; }
        public string OutPath { get; private set; }
        public string VerifyType => _verify.VerifyType;

        public NKitTask(NKitTaskContext context)
        {
            _started = DateTime.Now;
            _context = context;
        }

        public bool Setup(SystemType systemType)
        {
            NKitStepContext step0 = _context.Steps[0];
            this.SystemType = systemType;

            _verify = new NKitVerify(step0.DatManager, _context.Log);
            _deleteSourceFileSet = false;
            _deleteSourceFileCandidate = false;
            _sourceFile = step0.SourceFile;
            _sourceFile.SystemType = systemType;

            //Do Out first as temp used Out if blank - error message is better being triggered from Out first
            this.OutPath = _context.AppSettings.GetOutFilesPath(null, _sourceFile.BasePath, this.SystemType, _context.TaskType);
            this.ScanPath = _context.AppSettings.GetScanOutFilesPath(_sourceFile.BasePath, this.SystemType);
            this.TempPath = _context.AppSettings.ResolveAndCreateTempPath(this.SystemType, _sourceFile.BasePath);

            _skipImage = _context.AppSettings.SystemType != SystemType.NotSet && systemType != _context.AppSettings.SystemType; //exit if not the type set in the config

            if (_skipImage)
            {
                logImageHeader(this.SystemType, _sourceFile);

                //skiped
                this.Results = NKitTaskResults.Create(false, true, true, _context.Steps, _started, null, null, _skipName, null);
                _context.Log.Info(() => this.Results.ErrorMsg);
            }

            return _skipImage;
        }

        public void StepsInit(NKitTaskContext taskContext)
        {
            //this.Results.Steps = totalSteps;
            _context = taskContext;

            DatManager dm = _context.DatManager;
            if (_context.Settings.OutAsDatMatch && dm.ItemsCount() == 0 && (_context.TaskType == TaskType.Convert || _context.TaskType == TaskType.Fix || _context.TaskType == TaskType.Expand))
                throw new HandledException($"OutAsDatMatch is enabled, but no Dat was found for '{this.SystemType}' system");
            if (_context.Settings.V == Verify.DatLookup && dm.ItemsCount() == 0)
                throw new HandledException($"Verify is set to DatLookup, but no Dat was found for '{this.SystemType}' system");

            //find last write
            NKitStepContext s = taskContext.Steps.LastOrDefault(a => a.StepInfo.WriteImage);

            //check if proposed filename exists
            if (_context.Settings.SkipIfCompleted)
            {
                if (_context.Settings.OutAsDatMatch)
                    _context.Log.Info(() => "SkipIfCompleted ignored as OutAsDatMatch is enabled");
                else if (s != null)
                {
                    string fn = Path.Combine(this.OutPath, s.Step.ProposedName());
                    if (s.StepInfo.WriteImage && fn != null) //null is not supported
                    {
                        if (s.StepInfo.OutputType == OutputType.Image)
                            _skipImage = File.Exists(fn);
                        else if (s.StepInfo.OutputType == OutputType.FolderIndex || s.StepInfo.OutputType == OutputType.FolderFiles)
                            _skipImage = Directory.Exists(fn);

                        if (_skipImage)
                            _skipName = fn;
                    }
                }
            }
            _verify.Initialise(_context.Steps.Select(a => a.StepInfo), taskContext.DatManager.ItemsCount() != 0);
        }

        private SourceFile createNewSourceFile(NKitStepContext step)
        {
            SourceFile sf;
            if (step.SourceFile != null)
                sf = step.SourceFile; //already set or transposed from previous step
            else if (_context.TaskType == TaskType.Dedupe && step.StepInfo.StepType != TaskType.Dedupe)
            {
                NKitStepContext lastStep = _context.Steps[step.Index - 1];
                string configuredSetName = _context.Settings.DedupeConfig?.SetName;
                string setName = string.IsNullOrWhiteSpace(configuredSetName)
                    ? this.SystemType.ToString()
                    : configuredSetName.Trim();
                string dataStorePath = this.OutPath;
                if (!dataStorePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                    dataStorePath = Path.Combine(dataStorePath, setName + DataStore.DatabaseFileExtension);

                // Prefer the index filename (e.g. tmd.x) when available for accurate datastore lookup
                string orig = lastStep?.SourceFile?.IndexFile?.FileName ?? lastStep?.SourceFile?.ImageFiles?.FirstOrDefault()?.FileName;
                sf = createDataStoreSourceFile(dataStorePath, DataStoreAsIso.GetImageFileName(_context.SourceImageName, ImageFormat.Iso), orig);
                sf.Key = lastStep.Key;
                sf.IndexFile = lastStep?.SourceFile?.IndexFile;
            }
            else
            {
                NKitStepContext lastStep = _context.Steps[step.Index - 1];
                sf = SourceFile.CreateFromTemp(lastStep.Result.OutPath, lastStep.Result.OutFileParts);
                sf.Key = lastStep.Key;
            }
            sf.Initialised();
            return sf;
        }

        internal static SourceFile createDataStoreSourceFile(string dataStorePath, string imageFileName, string originalImageFileName = null)
        {
            string fullDataStorePath = Path.GetFullPath(dataStorePath);
            string dataStoreFileName = Path.GetFileName(fullDataStorePath);
            string dataStoreDirectory = Path.GetDirectoryName(fullDataStorePath) ?? string.Empty;
            string imageExtension = SourceFiles.GetKnownFileExtension(imageFileName);

            SourceFile sf = new SourceFile
            {
                ArchiveFiles = new[]
                {
                    new SourceFileItem(dataStoreDirectory, dataStoreFileName, DataStore.DatabaseFileExtension, DataStore.DatabaseFileExtension, 0,
                        File.Exists(fullDataStorePath) ? new FileInfo(fullDataStorePath).Length : 0, 0, false, false)
                },
                ImageFiles = new[]
                {
                    new SourceFileItem(string.Empty, imageFileName, imageExtension, imageExtension, 0, 0, 0, true, false)
                },
                IsArchive = false,
            };

            if (!string.IsNullOrEmpty(originalImageFileName))
                sf.OriginalFileName = originalImageFileName;

            return sf;
        }

        public bool StepStart(NKitStepContext step)
        {
            if (step.StepInfo.OutputType == OutputType.Image || step.StepInfo.OutputType == OutputType.Files)
                step.WritePath = this.TempPath;
            else if (step.StepInfo.OutputType == OutputType.FolderIndex || step.StepInfo.OutputType == OutputType.FolderFiles)
                step.WritePath = Path.Combine(this.OutPath, SourceFiles.GetUniqueFoldername(this.OutPath, step.Step.ProposedName(), "", true));
            else if (step.StepInfo.OutputType == OutputType.FileStore)
                step.WritePath = this.OutPath;
            if (step.StepInfo.WriteImage)
            {
                // For FileStore (dedupe), OutPath may be a .nkds file — create its parent directory instead
                string dirToCreate = step.WritePath;
                if (step.StepInfo.OutputType == OutputType.FileStore
                    && dirToCreate.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    dirToCreate = Path.GetDirectoryName(Path.GetFullPath(dirToCreate)) ?? dirToCreate;
                }
                Directory.CreateDirectory(dirToCreate);
            }

            //set up the step
            step.Initialise(createNewSourceFile(step));

            if (step.Index == 0)
            {
                if (_skipImage)
                    return false;
            }

            if (!_deleteSourceFileCandidate)
                _deleteSourceFileCandidate = _context.Settings.DeleteProcessed && !_sourceFile.IsArchived && _context.VerifyEnabled && step.StepInfo.DeleteSourceCandidate;

            ((SettingsDataProvider)_context.Settings.Lookup).DedupePath = this.OutPath;
            return true;
        }

        public void StepComplete(NKitStepContext step)
        {
            step.Complete(); //free resource

            _verify.Verify(step.Result, _context.Steps.Select(a => a.Result), _context.Steps[0].ImageSize, _skipImage);
        }

        public void StepsComplete()
        {
            string missing;

            if (_sourceFile.IndexFile != null)
            {
                if (!string.IsNullOrEmpty(missing = string.Join("|", _sourceFile.IndexFile.Items.Where(a => a.FileIsMissing).Select(a => a.FileName))))
                    _context.Log.Info(() => $"Index file '{Path.GetFileName(_sourceFile.IndexFile.FileName)}' references missing files '{missing}'");
                if (_context.Steps.Any(a => _sourceFile.IndexFile.WiiUFstMismatch))
                    _context.Log.Info(() => $"Index file '{Path.GetFileName(_sourceFile.IndexFile.FileName)}' and FST block have mismatched entries");
            }

            NKitStepResult[] results = _skipImage
                ? new[] { _context.Steps[0].Result }
                : _context.Steps.Select(a => a.Result).ToArray();
        }


        public bool Complete(bool success, string exceptionMessage)
        {
            if (!string.IsNullOrEmpty(exceptionMessage))
            {
                if (_context.Steps[0].Result == null)
                    _context.Steps[0].Result = new NKitStepResult() { VerifyResult = VerifyResult.Error, StepInfo = _context.Steps[0].StepInfo };
            }

            NKitStepResult[] results = _skipImage ? new[] { _context.Steps[0].Result } : _context.Steps.Select(a => a.Result).ToArray();
            NKitStepContext main = NKitTaskResults.GetCompletionStep(_context.TaskType, _context.Steps);
            NKitStepResult verifyResult = NKitTaskResults.GetVerifyResult(results, main?.Result?.MatchedDatItem, out _, out _);
            if (success && main == null)
                success = false;

            DatItem datMatch = verifyResult.MatchedDatItem; //verify may be set and MatchedItem is null of no match found.
            Scan scan = NKitTaskResults.GetScan(results);

            if (scan == null && success)
            {
                if (_context.TaskType == TaskType.Extract)
                    _context.Log.Info(() => "Scan not saved for Extract task");
                else
                    _context.Log.Info(() => "Scan not saved due the image being fixed or parts being skipped");
            }

            byte[] key = null;
            ResultOutFiles resultFiles = null;

            if (success && !_skipImage)
            {
                string ext = main.Result.StepInfo.OutputType == OutputType.Image ? SourceFiles.GetKnownFileExtension(main.Result.FinalName) : "";
                string newName = (!_context.Settings.OutAsDatMatch || datMatch?.FileName == null) ? null : datMatch.FileName.Substring(0, datMatch.FileName.Length - datMatch.FileNameExt.Length) + ext;
                //store the header key if it's different to one provided by a key file. Needs better implementation than this
                if (main.SystemType == SystemType.WiiU)
                    key = ((Nanook.NKit.Nintendo.WiiU.ImageHeader)main.Header).GetKeyToStore();
                else if (main.SystemType == SystemType.PS3)
                    key = ((Nanook.NKit.Iso.Iso9660.ImageHeader)main.Header).Ps3.GetKeyToStore();

                //finalise the output files/folders
                string finalName = newName ?? main.Result.FinalName; //final name assigned by main step
                string scanText = scan == null ? null : ScanParserYaml.Serialize(scan, _context.Settings.ScanFormat);
                string scanExt = ScanParserYaml.ScanExtension(_context.Settings.ScanFormat);
                string keyText = null; //setting this causes a key file to be written out
                if (key != null && main.SystemType == SystemType.PS3 && ((Nanook.NKit.Iso.Iso9660.ImageHeader)main.Header).Ps3.Key == ((Nanook.NKit.Iso.Iso9660.ImageHeader)main.Header).Ps3.Key3k3y) //only save 3k3y keys currently
                    keyText = key.ToHexString();
                resultFiles = new ResultOutFiles(this.OutPath, this.TempPath, !_context.ScanEnabled ? null : this.ScanPath, keyText, scanText, scanExt, finalName, main); //finalise and return unique name used on disk
                resultFiles.RenameToFinalNames();
                ResultOutFiles.DeleteTempFiles(_context.Steps); //delete all existing temp files - make a method, call on exit or fail
            }

            //Delete the source file(s)
            if (_sourceFile != null && _deleteSourceFileCandidate && verifyResult.VerifyResult == VerifyResult.VerifySuccess)
                _sourceFile.Delete();

            //Finalise the out files name
            this.Results = NKitTaskResults.Create(success, _skipImage, false, _context.Steps, _started, key, exceptionMessage, _skipName, resultFiles);

            if (success)
                logOutResults(this.Results);

            bool writeSuccess = writeResultsLine(this.Results);
            if (exceptionMessage != null)
                _context.Log.Error(() => $"FAILED  : {exceptionMessage}");

            return writeSuccess;
        }

        public void LogParams(DatManager datManager, SystemSettings settings)
        {
            NKitStepContext step0 = _context.Steps[0];
            logImageHeader(this.SystemType, _sourceFile);

            Dictionary<string, string> prms = new Dictionary<string, string>();
            string path = _sourceFile.FriendlyFullPath;
            if (_sourceFile.IsArchived)
            {
                if (SourceFiles.TrySplitArchivePath(path, out string archivePath, out string innerPath))
                {
                    prms.Add(_sourceFile.IsDataStore ? "InDataStore" : "InArchive", archivePath + (_sourceFile.ArchiveFiles.Length == 1 ? "" : $" +{_sourceFile.ArchiveFiles.Length - 1}"));
                    prms.Add("InFile", innerPath);
                }
                else
                    prms.Add("InFile", path);
            }
            else
                prms.Add("InFile", path);
            if (TempPath != null)
                prms.Add("TempPath", TempPath);
            if (OutPath != null)
                prms.Add("OutPath", OutPath);
            if (_context.ScanEnabled && ScanPath != null)
                prms.Add("ScanPath", ScanPath);

            if (datManager != null)
                prms.Add("Dats", datManager.ToString());
            // If this is a dedupe operation or source is a datastore, include dedupe params
            try
            {
                string dedupePath = settings?.Dedupe ?? settings?.DedupeConfig?.SetName; // best effort
                // Prefer explicit DedupePath from settings provider lookup
                if (settings?.Lookup is SettingsDataProvider sdp && !string.IsNullOrWhiteSpace(sdp.DedupePath))
                    dedupePath = sdp.DedupePath;

                if (!string.IsNullOrWhiteSpace(dedupePath) || _sourceFile.IsDataStore)
                {
                    // Attempt to resolve dedupe configuration
                    DedupeConfiguration dedupeCfg = _context.AppSettings[this.SystemType]?.DedupeConfig;
                    string configuredSetName = dedupeCfg?.SetName;
                    string setName = string.IsNullOrWhiteSpace(configuredSetName) ? this.SystemType.ToString() : configuredSetName.Trim();
                    string dataStorePath = dedupePath ?? this.OutPath;
                    if (!string.IsNullOrWhiteSpace(dataStorePath) && !dataStorePath.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                        dataStorePath = Path.Combine(dataStorePath, setName + DataStore.DatabaseFileExtension);

                    if (!string.IsNullOrWhiteSpace(dataStorePath))
                    {
                        if (_sourceFile.IsDataStore || _context.TaskType == TaskType.Dedupe)
                        {
                            (long ShardSize, int BlockSize)? sizes = DataStore.GetSetSizes(dataStorePath);
                            if (sizes != null)
                                prms.Set("DataStore", $"{dataStorePath} [Shard: {FormatSize(sizes.Value.ShardSize)}, Block: {FormatSize(sizes.Value.BlockSize)}]");
                            else
                            {
                                // Set doesn't exist yet — show config defaults
                                long cfgShard = dedupeCfg?.ShardSize ?? Configuration.DedupeConfiguration.DefaultShardSize;
                                int cfgBlock = dedupeCfg?.BlockSize > 0 ? dedupeCfg.BlockSize : 0x10000;
                                prms.Set("DataStore", $"{dataStorePath} [Shard: {FormatSize(cfgShard)}, Block: {FormatSize(cfgBlock)}] (new)");
                            }

                            try
                            {
                                string auxName = DataStore.ResolveAuxSetName(dataStorePath);
                                if (auxName != null)
                                {
                                    string auxDir = Path.GetDirectoryName(Path.GetFullPath(dataStorePath)) ?? "";
                                    string auxPath = Path.Combine(auxDir, auxName + DataStore.DatabaseFileExtension);
                                    (long ShardSize, int BlockSize)? auxSizes = DataStore.GetSetSizes(auxPath);
                                    if (auxSizes != null)
                                        prms.Set("DataStoreAux", $"{auxPath} [Shard: {FormatSize(auxSizes.Value.ShardSize)}, Block: {FormatSize(auxSizes.Value.BlockSize)}]");
                                    else
                                        prms.Set("DataStoreAux", auxPath);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(settings.FixFiles))
                prms.Add("FixFiles", settings.FixFiles);
            if (this.SystemType == SystemType.PS3)
            {
                Nanook.NKit.Iso.Iso9660.ImageHeader hdr = (Nanook.NKit.Iso.Iso9660.ImageHeader)step0.Header;
                if (hdr.Ps3.Key != null) //looked up key from keys files
                    prms.Add("Initial Key", hdr.Ps3.Key.ToHexString());
                if (hdr.Ps3.Has3k3yHeader)
                    prms.Add("3k3y Key", hdr.Ps3.Key3k3y.ToHexString());
                if (hdr.Ps3.Key == null && hdr.Ps3.Key3k3y == null && step0.Settings.AllKeys.Length == 0)
                    prms.Add("Key", "No Keys");
                else if (step0.Settings.AllKeys.Length != 0)
                    prms.Add("Lookup Keys", step0.Settings.AllKeys.Length.ToString());
            }
            else if (this.SystemType == SystemType.Wii)
            {
                Nanook.NKit.Nintendo.WiiGc.ImageInfo info = (Nanook.NKit.Nintendo.WiiGc.ImageInfo)step0.ImageInfo;
                if (info.IsNkit && info.IsNkitUpdateRemoved && info.NKitUpdatePartition == null)
                    prms.Add("UpdateFile", $"MISSING! Update partition '*_{info.NKitUpdatePartitionCrc:X8}' - using filler. See wiki for more info");
                else if (info.IsNkit && info.IsNkitUpdateRemoved)
                    prms.Add("UpdateFile", $"Update partition '{Path.GetFileName(info.NKitUpdatePartition.Filename)}'");
            }
            else if (this.SystemType == SystemType.WiiU)
            {
                if (_sourceFile.Key != null)
                    prms.Add("Initial Key", _sourceFile.Key.ToHexString());
                if (_sourceFile.Key == null && step0.Settings.AllKeys?.Length == 0)
                    prms.Add("Key", "No Keys");
                else if (step0.Settings.AllKeys.Length != 0)
                    prms.Add("Lookup Keys", step0.Settings.AllKeys.Length.ToString());
            }


            //collate the params
            foreach (KeyValuePair<string, string> p in _context.Steps.SelectMany(a => a.CustomSettingsInfo))
                prms.Set(p.Key, p.Value); //get the last
            Nanook.NKit.Nintendo.WiiGc.ImageInfo wiiGcInfo = step0.ImageInfo as Nanook.NKit.Nintendo.WiiGc.ImageInfo;
            bool isNkitSrc = (wiiGcInfo?.IsNkit ?? false) || (wiiGcInfo?.IsNkitDecoded ?? false);
            // For a decoded NKit source report the true underlying container (Gcz/Iso); otherwise the container type.
            ContainerType srcContainer = (wiiGcInfo?.IsNkitDecoded ?? false) ? wiiGcInfo.NKitSourceContainer : step0.ImageInfo.ContainerType;
            prms.Set("SrcType", (isNkitSrc ? "NKit." : "") + srcContainer.ToString());

            if (step0.StepInfo.SrcParts != null) //Src checksums only support 1 part - currently no requirement for more
            {
                foreach (KeyValuePair<ChecksumType, byte[]> kv in step0.StepInfo.SrcParts[0].Checksums.ToDictionary())
                    prms.Set($"Src{kv.Key}", kv.Value.ToHexString());
            }
            if (step0.StepInfo.DatMatch != null)
            {
                Checksums dip = step0.StepInfo.DatMatch.Bins.First().Checksums;
                prms.Set($"DatMd5", step0.StepInfo.DatMatch.IsMultiPart ? $"{dip.Md5.ToHexString()} +{step0.StepInfo.DatMatch.Bins.Length - 1}" : dip.Md5.ToHexString());
                prms.Set($"DatSHA1", step0.StepInfo.DatMatch.IsMultiPart ? $"{dip.Sha1.ToHexString()} +{step0.StepInfo.DatMatch.Bins.Length - 1}" : dip.Sha1.ToHexString());
                prms.Set($"DatCRC", step0.StepInfo.DatMatch.IsMultiPart ? $"{dip.Crc.ToString("X8")} +{step0.StepInfo.DatMatch.Bins.Length - 1}" : dip.Crc.ToString("X8"));
            }
            if (step0.StepInfo.SrcScan != null)
                prms.Set($"SrcScan", step0.StepInfo.SrcScan.Name);
            prms.Set("Verify", _context.Settings.V != Verify.N ? $"Y ({this.VerifyType})" : "N");
            int pad = prms.Keys.Max(a => a.Length);
            foreach (KeyValuePair<string, string> inP in prms)
                _context.Log.InfoInParam(() => $"{inP.Key}{new string(' ', pad + 1 - inP.Key.Length)}: {inP.Value}");
            _context.Log.Info(() => Log.Divider);

            if (_skipImage)
                _context.Log.Info(() => $"SKIPPED: Output already exists - {_skipName}");
        }

        private void logImageHeader(SystemType systemType, SourceFile sf)
        {
            //log the params. The Section/Divider rules are left unstamped (host passes them
            //through unchanged); only the title line carries the [Title] prefix so the host can
            //classify/colour it. The inner "[Task/System]  Name" format is preserved exactly so a
            //host can sub-colour the title parts. When the host supplied an X/Y image counter it is
            //appended as "  [X/Y]" (0/0 = unknown, omitted).
            _context.Log.Info(() => Log.Section);
            _context.Log.InfoTitle(() =>
            {
                string counter = (_context.ImageIndex > 0 && _context.ImageTotal > 0)
                    ? $"  [{_context.ImageIndex}/{_context.ImageTotal}]"
                    : "";
                return $"[{_context.AppSettings.TaskType}/{systemType}]  {sf.Name}{counter}";
            });
            _context.Log.Info(() => Log.Divider);
        }

        private void logOutResults(NKitTaskResults results)
        {
            // Output result summary lines carry the [OutParam] prefix so the host can classify and
            // colour them (stripped at Info verbosity, left in place at Detail/Debug).
            if (results.DatMatch != null)
                _context.Log.InfoOutParam(() => $"DatName   : {results.DatMatch}");
            if (results.OutPath != null && results.OutFileName != null)
            {
                string[] fl = results.OutFileName.Split('|');
                for (int i = 0; i < fl.Length; i++)
                {
                    string[] prts = fl[i].Split(':');
                    _context.Log.InfoOutParam(() => $"OutFile   : {Path.Combine(results.OutPath, prts[0])}{(prts.Length <= 1 ? "" : $"[{prts[1]}]")}");
                }
            }
            else if (results.OutPath != null)
                _context.Log.InfoOutParam(() => $"OutPath   : {results.OutPath}");

            if (results.OutScanFilePath != null)
                _context.Log.InfoOutParam(() => $"OutScan   : {results.OutScanFilePath}");
            if (results.OutKeyFilePath != null)
                _context.Log.InfoOutParam(() => $"OutKeyPath: {results.OutKeyFilePath}");
            if (results.Key != null)
                _context.Log.InfoOutParam(() => $"OutKey    : {results.Key.ToHexString()}");

            if (_context.Settings.V != Verify.N)
                _context.Log.InfoOutParam(() => $"Verify    : {results.VerifyResult} ({results.VerifyType})");
            if (results.Source.IsDeleted && results.Source.IndexFile != null)
                _context.Log.InfoOutParam(() => $"Deleted   : " + Path.Combine(results.Source.IndexFile.Path, results.Source.IndexFile.FileName) + $" +{results.Source.ImageFiles.Length}");
            else if (results.Source.IsDeleted)
                _context.Log.InfoOutParam(() => $"Deleted   : " + Path.Combine(results.Source.BasePath, results.Source.ImageFiles[0].FileName) + (results.Source.ImageFiles.Length == 1 ? "" : $" +{results.Source.ImageFiles.Length - 1}"));
            else if (_deleteSourceFileSet)
                _context.Log.InfoOutParam(() => $"NoDelete  : Conditions not met - Convert/Expand/Fix single file with Verify Success");

        }

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "0";
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024}KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024 * 1024)}MB";
            return $"{bytes / (1024L * 1024 * 1024)}GB";
        }

        private bool writeResultsLine(NKitTaskResults results)
        {
            if (results != null && (_context.Settings?.Results ?? false))
            {
                try
                {
                    string path = _context.Settings.ResultsOut;
                    if (path == null)
                        return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (!File.Exists(path))
                        File.WriteAllText(path, results.Header('\t') + Environment.NewLine);
                    File.AppendAllText(path, results.ToString('\t') + Environment.NewLine);
                    _context.Log.InfoOutParam(() => $"Result    : {path}");
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }

        internal void CompleteSkipped(SystemType detectedSystemType)
        {
            this.SystemType = detectedSystemType;
            _skipImage = true;
            this.Results = NKitTaskResults.CreateSkipped(_context.Steps[0], detectedSystemType);
            _context.Log.Info(() => $"Filter System [{this.SystemType}] : Skipped [{detectedSystemType}] {_context.SourceImageName}");
        }
    }
}