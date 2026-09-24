using Nanook.NKit.Configuration;
using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nanook.NKit
{
    internal class NKitStepContext : IStepContext, IImageContext
    {
        private NKitTaskContext _parent;
        private BlockSkip _blockSkip;


        internal NKitStepContext(NKitTaskContext parent, int index)
        {
            _parent = parent;
            this.Index = index;
            this.CustomSettingsInfo = new Dictionary<string, string>();
        }

        internal void Initialise(SourceFile sourceFile) => this.SourceFile = sourceFile;

        internal void SetNextVerifySourceParts(IParts parts)
        {
            if (parts == null)
                return;

            NKitStepContext nextVerify = getNextVerifyStep();

            if (nextVerify?.StepInfo is Steps.Shared.StepInfo stepInfo)
                stepInfo.SrcParts = parts;
        }

        internal void SetNextVerifySourceFile(SourceFile sourceFile)
        {
            if (sourceFile == null)
                return;

            NKitStepContext nextVerify = getNextVerifyStep();
            if (nextVerify != null)
                nextVerify.SourceFile = sourceFile;
        }

        private NKitStepContext getNextVerifyStep() => _parent.Steps
            .Skip(this.Index + 1)
            .FirstOrDefault(a => a.StepInfo?.VerifyMethod != VerifyMethod.NoVerify);

        internal void Setup(SystemType systemType, IStepInfo info, string stepConfig)
        {
            this.SystemType = systemType;
            this.StepConfig = stepConfig;
            this.StepInfo = info;
            this.Result = new NKitStepResult() { StepInfo = info };
            switch (info.Name)
            {
                case "Fix-WiiGc":
                    this.Step = new FixWiiGcStep(this);
                    break;
                case "FixExtract-WiiGc":
                    this.Step = new FixExtractWiiGcStep(this);
                    break;
                case "Fix-Ps3":
                    if (info.ImageConfig == "sfb")
                        this.Step = new FixPs3IrdStep(this);
                    else
                        throw new HandledException($"Step {info.Name} / {info.ImageConfig} is not recognised");
                    break;
                case "FixExtract-Ps3":
                    this.Step = new FixExtractPs3Step(this);
                    break;
                case "Convert-WiiU-Wux":
                    this.Step = new ConvertWiiUWuxStep(this);
                    break;
                case "Convert-Iso-CsoZso":
                    this.Step = new ConvertIsoCsoZsoStep(this);
                    break;
                case "Convert-Iso-DecIso":
                    this.Step = new ConvertIsoDecIsoStep(this);
                    break;
                case "Convert-Iso-CueToc":
                case "Expand-Iso-CueToc":
                    this.Step = new ConvertIsoCueTocStep(this);
                    break;
                case "Convert-WiiU-AppTmd":
                    this.Step = new ConvertWiiUAppTmdStep(this);
                    break;
                case "Expand-WiiU-AppTmd":
                    this.Step = new ExpandWiiUAppTmdStep(this);
                    break;
                case "Convert-WiiGc-Lossless":
                    if (info.ImageConfig == "ciso[nkit]")
                        this.Step = new ConvertWiiGcCisoStep(this);
                    else if (info.ImageConfig == "wbfs[nkit]")
                        this.Step = new ConvertWiiGcWbfsStep(this);
                    else if (info.ImageConfig == "rvz[nkit]")
                        this.Step = new ConvertWiiGcRvzStep(this);
                    else
                        throw new HandledException($"Step {info.Name} / {info.ImageConfig} is not recognised");
                    break;
                case "Convert-WiiGc-Lossy":
                    if (info.ImageConfig == ConfigSettingsConstants.FormatCiso)
                        this.Step = new ConvertWiiGcCisoStep(this);
                    else if (info.ImageConfig == ConfigSettingsConstants.FormatWbfs)
                        this.Step = new ConvertWiiGcWbfsStep(this);
                    else
                        throw new HandledException($"Step {info.Name} / {info.ImageConfig} is not recognised");
                    break;
                case "Convert-XBox-Xiso":
                    this.Step = new ConvertXBoxStep(this);
                    break;
                case "Convert-GdRom-CueGdi":
                case "Expand-GdRom-CueGdi":
                    this.Step = new ConvertIsoCueGdiFromGdStep(this);
                    break;
                case "Expand-Image-Patch":
                    this.Step = new ScanPatchStep(this);
                    break;
                case "Convert-Image":
                case "Expand-Image":
                case "Scan-Image":
                case "Verify-Image":
                    this.Step = new ScanStep(this);
                    break;
                case "Expand-XBox":
                    this.Step = new ExpandXBoxStep(this);
                    break;
                case "Verify-IsoGdChd":
                    this.Step = new ScanIsoGdStep(this);
                    break;
                case "Extract-WiiGc":
                    if (stepConfig == ConfigSettingsConstants.ExtractFlagForensic)
                        this.Step = new ExtractForensicStep(this);
                    else
                        this.Step = new ExtractWiiGcStep(this);
                    break;
                case "Extract-WiiU":
                    if (stepConfig == ConfigSettingsConstants.ExtractFlagForensic)
                        this.Step = new ExtractForensicStep(this);
                    else
                        this.Step = new ExtractWiiUStep(this);
                    break;
                case "Extract-Iso":
                    if (stepConfig == ConfigSettingsConstants.ExtractFlagForensic)
                        this.Step = new ExtractForensicStep(this);
                    else
                        this.Step = new ExtractIsoStep(this);
                    break;
                case "Extract-XBox":
                    if (stepConfig == ConfigSettingsConstants.ExtractFlagForensic)
                        this.Step = new ExtractForensicStep(this);
                    else
                        this.Step = new ExtractXBoxStep(this);
                    break;
                case "Dedupe-Image":
                    this.Step = new DedupeStep(this);
                    break;
                case "NotSet-NotSupported":
                    this.Step = null;
                    break;
                case "Wipe-Iso":
                case "Wipe-IsoCueTocGdi":
                    this.Step = new WipeIsoStep(this);
                    break;
                case "Wipe-IsoGdChd":
                    this.Step = new WipeIsoGdChdStep(this);
                    break;
                case "Wipe-WiiGc":
                    this.Step = new WipeWiiGcStep(this);
                    break;
                case "Wipe-WiiU":
                case "Wipe-WiiU-AppTmd":
                    this.Step = new WipeWiiUStep(this);
                    break;
                default:
                    throw new HandledException($"Step {info.Name} is not recognised");
            }
        }

        public void Complete()
        {
            this.Step = null; //free resource.

            //force a tidy up
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.WaitForFullGCComplete();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.WaitForFullGCComplete();
            GC.Collect();
        }

        public CancellationToken? CancelToken => _parent.CancelToken;

        public Dictionary<string, string> CustomSettingsInfo { get; }
        public int Index { get; set; }


        public AppSettings AppSettings => _parent.AppSettings;
        public TaskType TaskType => _parent.AppSettings.TaskType;
        public IDataProvider Settings
        {
            get
            {
                SystemSettings s = _parent.AppSettings[this.SystemType];
                if (s != null)
                {
                    // Ensure the per-system settings are initialised so paths (fixFiles/fixInfo/dat)
                    // and Lookup are resolved. This may be accessed before NKitTaskContext.Initialise
                    // (e.g. during image construction for Wii update-partition reinsertion), so
                    // initialise on demand when the system is known. Idempotent — Initialise just
                    // re-resolves the paths and rebuilds Lookup.
                    if (s.Lookup == null && this.SystemType != SystemType.NotSet)
                        s.Initialise(this.SystemType, _parent.AppSettings.TaskType, this.Log, this.DatManager);

                    if (s.Lookup == null)
                    {
                        string sourceBasePath = _parent.Steps.FirstOrDefault()?.SourceFile?.BasePath;
                        string dedupePath = _parent.AppSettings.GetOutFilesPath(null, sourceBasePath, this.SystemType, _parent.AppSettings.TaskType);
                        return new SettingsDataProvider(this.SystemType, this.Log, null, s.FixInfo, s.FixFiles, null, this.DatManager) { DedupePath = dedupePath };
                    }
                    else
                        return s.Lookup;
                }
                return null;
            }
        }

        public IImageHeader Header { get; set; }

        public SkipType SkipType { get; set; } //invalidates scan and resets address checking

        // Set by the Dedupe step when FinalizeImage detected the image already exists in the set
        // (identical name + checksums) and rolled it back. Surfaced on NKitTaskResults so the UI
        // can report "Already Exists" rather than leaving the row stuck in processing.
        internal bool ImageAlreadyExists { get; set; }

        public long SkipToImageOffsetGet(long imageOffset) //exposed on IImageContext
        {
            if (_blockSkip != null)
                return _blockSkip.ImageGetSkipRequest(imageOffset);
            return 0;
        }

        ///////////////////////////////////////////////////////////
        //IImageContext
        public void SetSystemType(SystemType systemType) => this.SystemType = systemType;

        ///////////////////////////////////////////////////////////
        //IStepContextConstruct - used to construct the task
        public SystemType SystemType { get; private set; }
        public IStepInfo StepInfo { get; private set; }
        public IStep Step { get; private set; }
        public string SourceImageName => _parent.SourceImageName;
        public byte[] HeaderData => this.Header.Data;
        public string StepConfig { get; private set; }
        public ILogScope Log => _parent.Log;
        public DatManager DatManager => _parent.AppSettings.DatManager;
        public long ImageSize => this.ImageInfo?.ImageSize ?? 0;

        //public bool ScanInvalidated { get; set; }
        public void AddSettingsInfo(string name, string value) => this.CustomSettingsInfo.Add(name, value);
        public void SkipBlockTaskEnable() => _blockSkip = new BlockSkip(); //exposed on ITaskContext
        public void SkipToImageOffsetSet(long imageOffset) //exposed on ITaskContext
        {
            if (_blockSkip != null)
                _blockSkip.TaskSetSkipRequest(imageOffset);
        }

        ///////////////////////////////////////////////////////////
        // IStepContext + IStepContextConstruct
        public SourceFile SourceFile { get; private set; }
        public byte[] Key => this.SourceFile?.Key;
        public IImageInfo ImageInfo { get; set; }
        public Scan Scan { get; set; }
        public NKitStepResult Result { get; set; }
        public string WritePath { get; internal set; }

        ///////////////////////////////////////////////////////////

        //coordinates block skipping between the image and task class (they're on different threads)
        //the task requests an offset to skip to. The Image must safely skip there (reading any required disc parts e.g. WipePartition headers etc)
        private class BlockSkip
        {
            private long _skipToImageOffset;
            private ReaderWriterLockSlim _lock;

            public BlockSkip()
            {
                _lock = new ReaderWriterLockSlim();
            }

            //called infrequently by task class (Write lock)
            public void TaskSetSkipRequest(long toImageOffset) //should be infrequent so lock here
            {
                _lock.EnterWriteLock();
                _skipToImageOffset = toImageOffset;
                _lock.ExitWriteLock();
            }

            //called every read from Image class. Only if Block skip was enabled by task (Read Lock)
            public long ImageGetSkipRequest(long currentImageOffset)
            {
                _lock.EnterReadLock();
                try
                {
                    if (currentImageOffset < _skipToImageOffset)
                        return _skipToImageOffset;
                    else
                        return 0;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }

        }

    }
}