using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Steps.Shared
{

    internal abstract class StepBase
    {
        private SourceFileTrack[] _tracks;
        private bool _split;
        private bool _resultsProcessed;

        internal IExtractFileHandler ExtractHandler { get; set; }

        internal protected IStepContext Context { get; private set; }

        internal abstract bool ContractReqPatch { get; }
        internal abstract bool ContractReqChk { get; }
        internal abstract bool ContractFullScan { get; }
        internal abstract bool ContractIsLossy { get; }
        internal abstract bool ContractIsExpand { get; }
        internal abstract bool ContractIsFix { get; }
        internal abstract OutputType ContractOutputType { get; }
        internal abstract bool ContractCanCrc { get; }
        internal abstract bool ContractCanHash { get; }

        /// <summary>
        /// This step's component tag (a <c>Nanook.NKit.LogScopes.Step*</c> constant), prepended to
        /// the one-line completion summary so a log filtered on e.g. <c>[FixWiiGc]</c> shows only
        /// that step's lines. Rendered under the <c>[Out]</c> scope: <c>[Out] [FixWiiGc] …</c>.
        /// </summary>
        internal abstract string ComponentTag { get; }

        protected ChecksumStream OutStream { get; private set; }
        protected ChecksumStream InChkStream { get; private set; }

        public abstract string ProposedName();

        protected void SetFinalName(string name) => Context.Result.FinalName = name;

        internal void EnableTestMode(bool crc) => this.OutStream.EnableTestMode(crc);

        protected virtual void CheckContract(IStepInfo stepInfo)
        {
            if (stepInfo.ReqPatch != ContractReqPatch)
                throw new HandledException($"Step Contract Fail - ReqPatch is {stepInfo.ReqPatch} and must be {ContractReqPatch}");
            if (stepInfo.ReqChk != ContractReqChk)
                throw new HandledException($"Step Contract Fail - ReqChk is {stepInfo.ReqChk} and must be {ContractReqChk}");
            if (stepInfo.FullScan != ContractFullScan)
                throw new HandledException($"Step Contract Fail - FullScan is {stepInfo.FullScan} and must be {ContractFullScan}");
            if (stepInfo.IsLossy != ContractIsLossy)
                throw new HandledException($"Step Contract Fail - IsLossy is {stepInfo.IsLossy} and must be {ContractIsLossy}");
            if (stepInfo.IsExpand != ContractIsExpand)
                throw new HandledException($"Step Contract Fail - IsExpand is {stepInfo.IsExpand} and must be {ContractIsExpand}");
            if (stepInfo.IsFix != ContractIsFix)
                throw new HandledException($"Step Contract Fail - IsFix is {stepInfo.IsFix} and must be {ContractIsFix}");
            if (stepInfo.OutputType != ContractOutputType)
                throw new HandledException($"Step Contract Fail - OutputType is {stepInfo.OutputType} and must be {ContractOutputType}");
            if (stepInfo.CanCrc != ContractCanCrc)
                throw new HandledException($"Step Contract Fail - CanCrc is {stepInfo.CanCrc} and must be {ContractCanCrc}");
            if (stepInfo.CanHash != ContractCanHash)
                throw new HandledException($"Step Contract Fail - CanHash is {stepInfo.CanHash} and must be {ContractCanHash}");
        }

        public virtual void Initialise(IStepContext context)
        {
            _resultsProcessed = false;
            this.Context = context;
            IStepInfo si = Context.StepInfo;
            List<ChecksumType> checksums;
            if (si.StepType == TaskType.Extract)
                ExtractHandler = new ExtractFileHandler();

            _tracks = this.Context.ImageInfo.Tracks?.Select(a => a.Clone()).ToArray();
            _split = (_tracks?.Length ?? 1) > 1; //output is split

            if (si.ReqChk)
            {
                if (si.StepType == TaskType.Dedupe)
                    checksums = new List<ChecksumType> { ChecksumType.XxHash };
                else
                    checksums = new List<ChecksumType> { ChecksumType.Crc32, ChecksumType.Md5, ChecksumType.Sha1, ChecksumType.XxHash };
                if (si.FullScan)
                    checksums.Remove(ChecksumType.Crc32); //remove crc
                InChkStream = new ChecksumStream(checksums, false, null, false);
            }
            else if (si.CreateInChecksum)
            {
                if (Context.ImageInfo.StepImageInfo.CustomChecksums == null || si.VerifyMethod == VerifyMethod.DatLookup)
                {
                    checksums = si.VerifyChecksums == null ? new List<ChecksumType>() : new List<ChecksumType>(si.VerifyChecksums);

                    if (si.FullScan || si.IsFix) //calc the CRC if not creating a scan, or in fix mode (scan will be bad)
                        checksums.Remove(ChecksumType.Crc32); //remove crc

                    // Ensure XXHash is included for DataStore verification when the
                    // image consists of multiple parts. Single-part images previously
                    // verified correctly without a streamed XXHash, so only request
                    // XXHash for multi-part inputs where a global hash is needed.
                    if (si.VerifyMethod == VerifyMethod.DataStore && _tracks != null && _tracks.Length > 1 && !checksums.Contains(ChecksumType.XxHash))
                        checksums.Add(ChecksumType.XxHash);

                    InChkStream = new ChecksumStream(checksums, false, null, false);
                }
            }

            //Calc outbound checksums
            checksums = new List<ChecksumType>();
            if (si.CreateOutChecksum && si.CanHash)
            {
                // Prefer the appropriate hash type depending on verify method.
                // For DataStore verification we want XXHash as the primary hash.
                if (si.VerifyMethod == VerifyMethod.DataStore)
                {
                    checksums.Add(ChecksumType.XxHash);
                    if (this.ContractReqChk)
                    {
                        checksums.Add(ChecksumType.Md5);
                        checksums.Add(ChecksumType.Sha1);
                    }
                }
                else
                {
                    //this needs to check if the hashes are in the dat. Fine for redump and tosec for now
                    if ((checksums.Count == 0 && (si.VerifyMethod == VerifyMethod.DatLookup || si.VerifyMethod == VerifyMethod.DatMatch || si.VerifyMethod == VerifyMethod.InChecksums)) || this.ContractReqChk)
                        checksums.Add(ChecksumType.Md5);
                    if ((checksums.Count == 0 && (si.VerifyMethod == VerifyMethod.DatLookup || si.VerifyMethod == VerifyMethod.DatMatch || si.VerifyMethod == VerifyMethod.InChecksums)) || this.ContractReqChk)
                        checksums.Add(ChecksumType.Sha1);
                    if ((checksums.Count == 0 && (si.VerifyMethod == VerifyMethod.DatLookup || si.VerifyMethod == VerifyMethod.DatMatch || si.VerifyMethod == VerifyMethod.InChecksums)) || this.ContractReqChk)
                        checksums.Add(ChecksumType.XxHash);
                }
            }
            if (si.CreateOutChecksum && si.CanCrc)
                checksums.Add(ChecksumType.Crc32);

            if (si.VerifyMethod != VerifyMethod.NoVerify)
            {
                //set up verify
            }

            bool temp = this.Context.StepInfo.OutputType == OutputType.Image || this.Context.StepInfo.OutputType == OutputType.Files;
            this.OutStream = new ChecksumStream(checksums, si.WriteImage, this.Context.WritePath, temp);
        }

        public virtual void Process(ISection section)
        {
            if (this.InChkStream != null)
            {
                if (_tracks == null)
                    this.InChkStream.Write(section.Encrypted, 0, (int)section.Size);
                else
                {
                    int sz = (int)section.Size;
                    int files = this.InChkStream.ChecksummedFiles.Count;
                    SourceFileTrack t = _tracks[files];
                    int total = 0;

                    while (total != sz)
                    {
                        if (_split && t.Size == this.InChkStream.Position)
                        {
                            bool cached = false;

                            t = _tracks[files + 1];
                            this.InChkStream.NewPart(Path.GetFileNameWithoutExtension(t.FileName), Path.GetExtension(t.FileName), true);
                            if (cached)
                                return; //don't write next track
                        }

                        int write = !_split ? sz : (int)Math.Min(sz - total, t.Size - this.InChkStream.Position);
                        this.InChkStream.Write(section.Encrypted, total, write); //decrypted if no encryption
                        total += write;
                    }
                }
            }
        }

        public virtual void ProcessingComplete()
        {
            if (this.ExtractHandler != null)
                this.ExtractHandler.CloseFs();

            if (Context.StepInfo.CreateInChecksum)
            {
                if (InChkStream != null)
                    InChkStream.Close();

                if (Context.ImageInfo.StepImageInfo.CustomChecksums != null &&
                    (Context.StepInfo.VerifyMethod == VerifyMethod.InChecksums || Context.StepInfo.VerifyMethod == VerifyMethod.DataStore))
                    Context.Result.InFileParts = new Parts(Context.ImageInfo.StepImageInfo.CustomChecksums);
                else if (InChkStream != null)
                {
                    //set crc info from scan (In crcing is turned off when Full Scan is on to save cpu)
                    if (Context.StepInfo.FullScan)
                    {
                        IParts parts = null;
                        if (Context.Scan.Areas.Count == InChkStream.ChecksummedFiles.Count)
                        {
                            for (int i = 0; i < Context.Scan.Areas.Count; i++)
                            {
                                InChkStream.ChecksummedFiles[i].Checksums.Crc = Context.Scan.Areas[i].Crc;
                                InChkStream.ChecksummedFiles[i].Checksums.Size = Context.Scan.Areas[i].Size;
                            }
                        }
                        else if (InChkStream.ChecksummedFiles.Count == 0) //patched so no hash, but have crc
                            parts = new Parts(new Checksums() { Crc = Context.Scan.Crc, Size = Context.Scan.Size });
                        else if (InChkStream.ChecksummedFiles.Count == 1) //parts aren't tracks, get crc
                        {
                            InChkStream.ChecksummedFiles[0].Checksums.Crc = Context.Scan.Crc;
                            InChkStream.ChecksummedFiles[0].Checksums.Size = Context.Scan.Size;
                        }

                        if (parts == null)
                            parts = InChkStream.GetAllParts();
                        Context.Result.InFileParts = parts;
                    }
                    else
                        Context.Result.InFileParts = InChkStream.GetAllParts();
                }
            }
        }

        public void ProcessResultsAsExceptioned()
        {
            try
            {
                processResults();
            }
            catch { }
        }

        public virtual void processResults()
        {
            if (_resultsProcessed)
                return;

            _resultsProcessed = true;
            OutStream.Close();

            if (Context.Result.FinalName == null)
                Context.Result.FinalName = this.ProposedName() ?? ""; // lifecycle object will finalise the name for main step

            if (Context.StepInfo.FullScan)
                Context.Result.Scan = Context.Scan; //set

            //if (Context.StepInfo.CreateOutChecksum)
            Context.Result.OutFileParts = this.OutStream.GetAllParts();
            Context.Result.OutPath = this.OutStream.BasePath;

            //check the file/folder outputs are valid
            if (Context.StepInfo.OutputType == OutputType.Image)
            {
                if (OutStream.ChecksummedFiles.Count != 1)
                    throw new HandledException($"Output type 'Image' expects 1 file");
            }
            else if (Context.StepInfo.OutputType == OutputType.FolderIndex && Context.StepInfo.StepType != TaskType.Scan && Context.StepInfo.StepType != TaskType.Verify)
            {
                if (Context.Result.OutFileParts.Count(a => ((Part)a).IsIndex) != 1)
                    throw new HandledException($"Output type 'FolderIndex' expects 1 index file");
            }

            logCompletionSummary();
        }

        // [Out] [<Step>] Detail: one identity/result line per step run (emitted once, from the
        // single processResults finalise point — NOT the per-section Process hot path). Lets a log
        // filtered on the step's tag show which step ran and what it produced. Gated on Detail so
        // there is no cost (and no string built) in Info mode.
        private void logCompletionSummary()
        {
            ILogScope outScope = Context?.Log?.ScopeFor(Nanook.NKit.LogScopes.Out);
            if (outScope == null || !outScope.IsEnabled(LogLevel.Detail))
                return;

            IStepInfo si = Context.StepInfo;
            bool writes = si.OutputType != OutputType.None && si.OutputType != OutputType.Scan;

            // What the step is configured to do (reliable at this point). The final CRC and
            // VerifyResult are computed LATER by NKitVerify at task level (see the task-level
            // [CRC …] / [Results] lines), so they are deliberately NOT shown here — this is the
            // step's own identity/config line, not the verify outcome.
            string extra = "";
            if (writes) // name/parts only mean something when the step actually writes output
            {
                int outParts = Context.Result?.OutFileParts?.Count() ?? 0;
                string name = Context.Result?.FinalName;
                if (string.IsNullOrEmpty(name))
                    name = this.ProposedName();
                extra = $" parts {outParts}" + (string.IsNullOrEmpty(name) ? "" : $" name '{name}'");
            }

            outScope.Log(LogLevel.Detail,
                $"{Nanook.NKit.LogScopes.Tag(this.ComponentTag)}{si.Name} write:{(si.WriteImage ? "y" : "n")} out {si.OutputType} vfy {si.VerifyMethod}{extra}");
        }

        public virtual void ProcessResults() => processResults();

    }
}