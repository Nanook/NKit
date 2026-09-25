using NKitDataStore;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nanook.NKit
{

    public class NKitProcessor
    {
        public event EventHandler<ProgressEventArgs> ProgressEvent;

        /// <summary>Raised periodically during processing with pipeline telemetry (stage load, workers, throughput).</summary>
        public event EventHandler<EngineStats> StatsEvent;

        private NKitTaskContext _taskContext;
        private float _progressDiff = 0.005f;

        internal void RaiseStats(EngineStats stats)
        {
            if (stats != null)
                StatsEvent?.Invoke(this, stats);
        }

        public int Step { get; private set; }
        public int StepTotal { get; private set; }

        public float Progress { get; private set; }

        public float ProgressTotal { get; private set; }

        public bool ResultsWriteError { get; private set; }

        /// <summary>
        /// Optional per-section callback for EMBEDDING consumers. When set, every processed section
        /// block is delivered here IN IMAGE ORDER as a restricted, read-only <see cref="IReadOnlySection"/>
        /// view — letting a consumer read the processed data (extract, convert, stream) without NKit
        /// writing to disk. The callback does not replace the configured task's own output; it is an
        /// additional observation hook over the same ordered block feed the output steps consume.
        /// See NKitVault/10 Refactor/Public API and Embedding Model.md.
        /// </summary>
        public Action<IReadOnlySection> OnSection { get; set; }

        /// <summary>
        /// 1-based index of this image within the caller's batch (the CLI/UI loops the scanned
        /// images; NKit processes one at a time). Set before <see cref="Process(CancellationToken)"/>.
        /// When both this and <see cref="ImageTotal"/> are &gt; 0 they are carried on the image
        /// Title line as an <c>X/Y</c> counter. Default 0 = unknown (no counter shown).
        /// </summary>
        public int ImageIndex
        {
            get => _taskContext.ImageIndex;
            set => _taskContext.ImageIndex = value;
        }

        /// <summary>Total images in the caller's batch, or 0 when unknown. The Y of <c>X/Y</c>.</summary>
        public int ImageTotal
        {
            get => _taskContext.ImageTotal;
            set => _taskContext.ImageTotal = value;
        }

        public NKitProcessor(AppSettings settings, SourceFile file, Action<string, LogLevel> consoleLog, bool dynamicConsole = false)
        {
            ResultsWriteError = false;
            if (file == null)
                throw new ArgumentException("file must not be null");
            _taskContext = new NKitTaskContext(settings, file, consoleLog, dynamicConsole);
        }

        public NKitTaskResults Process(CancellationToken cancel)
        {
            _taskContext.CancelToken = cancel;
            return this.Process();
        }

        //Main processing entry point. It all starts here
        public NKitTaskResults Process()
        {
            // Early interception for synthetic folder sources
            SourceFile sourceFile = _taskContext.Steps[0].SourceFile;
            if (sourceFile.IsSyntheticFolder)
            {
                return processSyntheticFolder();
            }

            // For TmdApp sources with some missing content, skip only the absent files
            // and continue processing with what is present on disk.
            if (sourceFile.HasMissingTmdContent)
            {
                SourceFileTrack[] pruned = sourceFile.PruneMissingTmdContent();
                if (pruned.Length > 0)
                    _taskContext.Log.Info(() => $"Skipping {pruned.Length} missing content file(s) in '{sourceFile.Name}': {string.Join(", ", pruned.Select(t => t.FileName))}");
                if (sourceFile.IndexFile?.Items.Length == 0 || sourceFile.ImageFiles.Length == 0)
                {
                    NKitTask skipTask = new NKitTask(_taskContext);
                    skipTask.CompleteSkipped(SystemType.NotSet);
                    return skipTask.Results;
                }
            }
            else if (sourceFile.Status == SourceFileResult.MissingFile)
            {
                NKitTask skipTask = new NKitTask(_taskContext);
                skipTask.CompleteSkipped(SystemType.NotSet);
                return skipTask.Results;
            }

            NKitTask task = new NKitTask(_taskContext); //initialises Results also
            NKitInput input = null;
            Stream readStream = null;
            bool isSetup = false;

            try
            {

                //open the src image to get the initial details
                input = new NKitInput(_taskContext.Steps[0]); //reads source format and marks missing/removed blocks of data
                readStream = _taskContext.Steps[0].SourceFile.OpenFileStream(_taskContext.Log);
                SystemType systemType;

                bool customChkCandidate = _taskContext.TaskType == TaskType.Scan || _taskContext.TaskType == TaskType.Verify || _taskContext.TaskType == TaskType.Expand; //need to add check for V=y when we have caching reader

                //read the image before the steps start to determine what we have
                if (input.Open(readStream, customChkCandidate, _taskContext.AppSettings.SystemType, _taskContext.AppSettings.GetSystem(_taskContext.Steps[0].SourceFile.BasePath), out systemType)) //scan results will be null on scan, populated on 2nd use
                {
                    _taskContext.Initialise(systemType);

                    input.Setup();
                    bool skip = task.Setup(systemType);
                    if (skip)
                        return task.Results;

                    _taskContext.CreateSteps(_taskContext.Steps[0].ImageInfo.StepImageInfo); //creates the tasks with nulled context, outfiles etc

                    if (_taskContext.Steps[0].StepInfo.Name.Contains("NotSupported"))
                    {
                        SourceFile sf = _taskContext.Steps[0].SourceFile;
                        if (_taskContext.TaskType == TaskType.Convert)
                            throw new HandledException($"Task: [{_taskContext.TaskType}] not supported for [{_taskContext.SystemType}:{sf.ImageType}] => [{_taskContext.Settings.Convert}]!");
                        else
                            throw new HandledException($"Task: [{_taskContext.TaskType}] not supported for [{_taskContext.SystemType}:{sf.ImageType}]!");
                    }

                    isSetup = true;

                    string[] stepNames = _taskContext.Steps.Select(a => a.StepInfo.StepType.ToString()).ToArray();
                    task.StepsInit(_taskContext);
                    task.LogParams(_taskContext.DatManager, _taskContext.Settings);

                    foreach (NKitStepContext step in _taskContext.Steps)
                    {
                        if (_taskContext.CancelToken?.IsCancellationRequested ?? false)
                            throw new HandledException("Processing Cancelled");

                        try
                        {
                            if (!task.StepStart(step))
                                break;

                            if (step.Index != 0) //set up new task and relay file info
                            {
                                SystemType system = systemType;
                                input = new NKitInput(step); //reads source format and marks missing/removed blocks of data
                                readStream = step.SourceFile.OpenFileStream(_taskContext.Log);
                                input.Open(readStream, false, system, system, out _); //scan results will be null on scan, populated on 2nd use
                                input.Setup();
                            }

                            if (step.StepInfo.IsFix)
                                step.ImageInfo.Mode = ReadMode.Fix; //must be applied after Image setup but before reading - tricky setting to calculate and apply

                            step.Step.Initialise(step);
                            process(input, step.Index, stepNames);
                        }
                        catch
                        {
                            step.Step.ProcessResultsAsExceptioned(); //finalise the results
                            throw;
                        }
                        finally
                        {
                            try { task.StepComplete(step); } catch { }
                            // Deterministically release the decoder chain + Layer-B cache manager.
                            // Close() is idempotent (already called on the success path in process()),
                            // and runs before readStream (Layer-A raw source) is closed so any final
                            // decoder read still has its source. Covers the exception path too.
                            try { input?.Close(); } catch { }
                            try { readStream.Close(); readStream = null; } catch { }
                            try { (step.Step as IDisposable)?.Dispose(); } catch { }
                        }
                    }
                    task.StepsComplete();
                    _taskContext.Log.Info(() => Log.Divider);
                    ResultsWriteError = !task.Complete(true, null);
                }
                else //filtered out
                {
                    _taskContext.Log.Info(() => Log.Divider);
                    task.CompleteSkipped(systemType);
                    _taskContext.Log.Info(() => Log.Divider);
                }
            }
            catch (Exception ex)
            {
                ResultsWriteError = !task.Complete(false, isSetup ? ex.Message : ((_taskContext?.Steps[0]?.SourceFile?.FriendlyFullPath ?? "") + " : " + ex.Message));
            }
            finally
            {
                // Catch an input that failed before/at Open (never reached the per-step finally).
                // Close() is idempotent so this is safe even when a step already closed it.
                try { input?.Close(); } catch { }
                try { if (readStream != null) readStream.Close(); } catch { }
                ResultOutFiles.DeleteTempFiles(_taskContext.Steps);

                input = null;

                if (_taskContext != null)
                    _taskContext.Log = null; _taskContext.HostLog = null; //don't dispose this
                _taskContext = null;

                //force a tidy up
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.WaitForFullGCComplete();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.WaitForFullGCComplete();
                GC.Collect();
            }

            return task.Results;
        }

        private void process(NKitInput input, int stepIdx, string[] steps)
        {
            long prg = 0;
            this.Step = stepIdx;
            this.StepTotal = steps.Length;
            NKitStepContext stepContext = _taskContext.Steps[stepIdx];
            NKitStep step = input == null ? null : new NKitStep(input, stepContext, this.RaiseStats); //let errors raise up

            DateTime _startDate = DateTime.Now;
            TimeSpan ts;

            float lastPcnt = 0;
            float pcnt = 0;

            // Throughput sampling: bytes moved over the wall-clock window since the last sample.
            DateTime sampleTime = _startDate;
            long samplePrg = 0;
            double mibPerSec = 0;

            raiseProgressEvent(stepIdx, steps, 0, 0, 0, 0);
            try
            {
                _taskContext.HostLog.Progress(stepIdx, steps, 0f, lastPcnt, null);

                if (step != null) //verify is last task and not required
                {
                    //if verify and verifier isComplete then skip
                    long sz = stepContext.StepInfo.IsFix ? stepContext.ImageInfo.FixSize : input.Image.Size;

                    foreach (ISection b in step.ReadSections())
                    {
                        if (stepContext.SkipType == SkipType.End || (_taskContext.CancelToken?.IsCancellationRequested ?? false))
                            step.Abort();

                        // Embedding hook: deliver each ordered block to a consumer via a restricted
                        // read-only view (never the raw ISection/ISectionProcessor). No-op when unset.
                        if (this.OnSection != null)
                            this.OnSection(new ReadOnlySectionView(b));

                        prg = b.ImageOffset;
                        pcnt = (float)(prg / (double)Math.Max(sz, prg)); //Max for safety

                        // Sample MiB/s over at least a 0.25s window so the rate is readable.
                        DateTime now = DateTime.Now;
                        double secs = (now - sampleTime).TotalSeconds;
                        if (secs >= 0.25)
                        {
                            long moved = prg - samplePrg;
                            if (moved > 0)
                                mibPerSec = moved / (1024.0 * 1024.0) / secs;
                            sampleTime = now;
                            samplePrg = prg;
                        }

                        raiseProgressEvent(stepIdx, steps, pcnt, prg, sz, mibPerSec);

                        if (this.Progress > 0f && this.Progress < 1f)
                        {
                            _taskContext.HostLog.Progress(stepIdx, steps, this.Progress, lastPcnt, null);
                            lastPcnt = this.Progress;
                        }
                    }
                    ts = DateTime.Now - _startDate;

                    if (stepContext.SkipType == SkipType.None)
                    {
                        foreach (ISection b in step.Patch())
                        {
                            if (_taskContext.CancelToken?.IsCancellationRequested ?? false)
                                break;
                        }
                    }

                    if (_taskContext.CancelToken?.IsCancellationRequested ?? false)
                        throw new HandledException("Processing Cancelled");

                    step.Complete();
                    input.Close();
                }
                else
                    ts = DateTime.Now - _startDate;

                string completeMessage;
                if (stepContext.SkipType != SkipType.None)
                    completeMessage = string.Format(" ~{0,2}m {1,2:D2}s  [Partial Read]", ((int)ts.TotalMinutes).ToString(), ts.Seconds.ToString());
                else
                {
                    uint crc = (stepContext.Result.ResultCrc ?? 0) == 0 ? (stepContext.Scan?.Crc ?? 0) : stepContext.Result.ResultCrc.Value;
                    completeMessage = string.Format(" ~{0,2}m {1,2:D2}s  [{2}CRC {3}]", ((int)ts.TotalMinutes).ToString(), ts.Seconds.ToString(), (stepContext.ImageInfo.Tracks?.Length ?? 0) <= 1 ? "" : "Combined ", crc.ToString("X8"));
                }
                // Raise the final event WITH the completion detail so the dynamic console can show
                // the CRC in place of the live stats, then emit the legacy dots line for non-dynamic.
                raiseProgressEvent(stepIdx, steps, 1, completeMessage: completeMessage);
                _taskContext.HostLog.Progress(stepIdx, steps, 1f, lastPcnt, completeMessage);
                step = null;
                return;
            }
            catch (Exception)
            {
                ts = DateTime.Now - _startDate;
                string completeMessage = string.Format(" ~{0,2}m {1,2:D2}s  [Failed!]", ((int)ts.TotalMinutes).ToString(), ts.Seconds.ToString());
                raiseProgressEvent(stepIdx, steps, 1, completeMessage: completeMessage);
                _taskContext.HostLog.Progress(stepIdx, steps, 1f, lastPcnt, completeMessage);
                _taskContext.Log.Info(() => Log.Divider);
                throw;
            }
        }

        private void raiseProgressEvent(int step, string[] steps, float progress, long bytesProcessed = 0, long totalBytes = 0, double mibPerSec = 0, string completeMessage = null)
        {
            float total = ((float)step + progress) / (float)steps.Length;

            if (progress != 0f && progress != 1f && total - this.ProgressTotal <= _progressDiff) //allow dupe values for start and end
                return; //don't raise the event too often

            this.Progress = progress;
            this.ProgressTotal = total;
            if (ProgressEvent != null)
            {
                ProgressEvent(this, new ProgressEventArgs()
                {
                    Progress = progress,
                    ProgressTotal = total,
                    IsStart = step == 0 && progress == 0f,
                    IsComplete = step == steps.Length - 1 && progress == 1f,
                    Step = step,
                    StepTotal = steps.Length,
                    Steps = steps,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    MiBPerSec = mibPerSec,
                    CompleteMessage = completeMessage
                });
            }
        }

        /// <summary>
        /// Handles synthetic folder SourceFiles by delegating to FolderImageProcessor.
        /// Does NOT enter the normal NKitTask/step pipeline.
        /// </summary>
        private NKitTaskResults processSyntheticFolder()
        {
            SourceFile sf = _taskContext.Steps[0].SourceFile;
            FolderGroupInfo group = sf.SyntheticFolderGroup;

            // Resolve datastore path from settings
            // Use group.SourceFolder instead of sf.BasePath — synthetic SourceFiles
            // have null ImageFiles/IndexFile so BasePath would throw
            string dedupeDirectory = _taskContext.AppSettings
                .GetOutFilesPath(null, group.SourceFolder, group.SystemType, TaskType.Dedupe);
            string setName = _taskContext.AppSettings[group.SystemType]?.DedupeConfig?.SetName
                ?? group.SystemType.ToString();

            // Resolve to actual directory if path ends with .nkds
            if (dedupeDirectory.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                string fullPath = Path.GetFullPath(dedupeDirectory);
                setName = Path.GetFileNameWithoutExtension(fullPath);
                dedupeDirectory = Path.GetDirectoryName(fullPath) ?? dedupeDirectory;
            }

            FolderImageProcessor processor = new FolderImageProcessor();
            FolderProcessResult result = processor.Process(
                sf, dedupeDirectory, setName,
                (msg, lvl) => _taskContext?.HostLog?.Write(lvl, () => msg));

            return NKitTaskResults.CreateSyntheticResult(result, group.BaseName);
        }

    }
}