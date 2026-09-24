using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit
{
    public class NKitTaskResults
    {
        public SourceFile Source { get; internal set; }
        public Scan Scan { get; internal set; }

        internal ResultOutFiles StepFiles { get; set; }
        internal List<NKitStepContext> StepResults { get; private set; }
        public DateTime DateStamp { get; internal set; }
        public string InFilePath { get; internal set; }
        public string Name { get; internal set; }
        public TaskType Task { get; internal set; }
        public SystemType System { get; internal set; }
        public ContainerType ContainerType { get; internal set; }
        public string DatMatch { get; internal set; }
        public string OutPath { get; internal set; }
        public string OutFileName { get; internal set; }
        public string OutScanFilePath { get; internal set; }
        public string OutKeyFilePath { get; internal set; }
        public int Steps { get; internal set; }
        public string VerifyChecksum { get; internal set; }
        public string VerifyType { get; internal set; } //scan compare / sourcecrc compare etc
        public VerifyResult VerifyResult { get; internal set; }
        public int Duration { get; internal set; }
        public long Size { get; internal set; }
        public uint CRC { get; internal set; }
        public uint DecryptedCrc { get; internal set; }
        public bool SupportsEncryption { get; internal set; }
        public bool HasEncryption { get; internal set; }
        public byte[] Key { get; internal set; }
        public bool ImageSkipped { get; internal set; }
        public bool ImageAlreadyExists { get; internal set; }
        public string ErrorMsg { get; internal set; }

        public string[] HeaderArray()
        {
            return new[]
            {
                "DateStamp",
                "InFilepath",
                "Name",
                "Task",
                "Steps",
                "System",
                "ContainerType",
                "Duration",
                "Size",
                "CRC",
                "DecryptedCrc",
                "VerifyChecksum",
                "VerifyType", //scan compare / sourcecrc compare etc
                "VerifyResult",
                "DatMatch",
                "OutPath",
                "OutFileName",
                "OutScanFilePath",
                "OutKeyFilePath",
                "SupportsEncryption",
                "HasEncryption",
                "Key",
                "Skipped",
                "ErrorMsg"
            };
        }
        public string[] ToArray()
        {
            return new[]
            {
                DateStamp.ToString(),
                InFilePath ?? "",
                Name ?? "",
                Task.ToString(),
                Steps.ToString(),
                System.ToString(),
                ContainerType.ToString(),
                Duration.ToString(),
                Size.ToString(),
                CRC.ToString("X8"),
                DecryptedCrc.ToString("X8"),
                VerifyChecksum ?? "",
                VerifyType, //scan compare / sourcecrc compare etc
                VerifyResult.ToString(),
                DatMatch ?? "",
                OutPath ?? "",
                OutFileName ?? "",
                OutScanFilePath ?? "",
                OutKeyFilePath ?? "",
                SupportsEncryption.ToString(),
                HasEncryption.ToString(),
                Key == null ? "" : Key.ToHexString(),
                ImageSkipped.ToString(),
                ErrorMsg ?? ""
            };
        }

        internal static NKitTaskResults CreateSkipped(NKitStepContext first, SystemType detectedSystemType)
        {
            NKitTaskResults r = new NKitTaskResults();

            r.System = detectedSystemType;
            r.Task = first.AppSettings.TaskType;
            r.Source = first.SourceFile;
            r.ContainerType = ContainerType.Unknown;
            r.InFilePath = first.SourceFile.BasePath;
            r.Name = first.SourceFile.Name;
            r.VerifyResult = VerifyResult.Unverified;
            r.VerifyType = "Skipped";
            r.ImageSkipped = true;
            return r;
        }

        /// <summary>
        /// Creates a minimal NKitTaskResults wrapping a FolderProcessResult for synthetic folder sources.
        /// </summary>
        internal static NKitTaskResults CreateSyntheticResult(FolderProcessResult result, string baseName)
        {
            NKitTaskResults r = new NKitTaskResults();
            bool success = result == FolderProcessResult.Created || result == FolderProcessResult.UpToDate;

            r.Name = baseName;
            r.Task = TaskType.Dedupe;
            r.System = SystemType.WiiU;
            r.ContainerType = ContainerType.Unknown;
            r.DateStamp = DateTime.Now;
            r.VerifyResult = success ? VerifyResult.Unverified : VerifyResult.Error;
            r.VerifyType = "Synthetic";
            r.ImageSkipped = result == FolderProcessResult.NoChildren;
            r.ErrorMsg = result == FolderProcessResult.Error ? $"Folder processing failed for '{baseName}'" : null;
            return r;
        }

        internal static NKitTaskResults Create(bool success, bool imageSkipped, bool badSys, List<NKitStepContext> stepResults, DateTime started, byte[] key, string exception, string skipName, ResultOutFiles resultFiles)
        {
            NKitTaskResults r = new NKitTaskResults();
            TaskType task = stepResults[0].AppSettings.TaskType;
            r.ImageSkipped = imageSkipped;
            // A dedupe add that found the image already in the set rolled it back; surface that so
            // callers (the UI) can report AlreadyExists instead of a stuck/failed row.
            r.ImageAlreadyExists = stepResults.Any(s => s.ImageAlreadyExists);

            r.VerifyResult = VerifyResult.Unverified;
            r.VerifyType = VerifyMethod.NoVerify.ToString();

            r.System = stepResults[0].SystemType;
            r.Task = task;
            // For a decoded NKit source (NKitAsIso) the stream Format is always Iso, but the true
            // underlying container is carried on NKitSourceContainer (Gcz for .nkit.gcz, Iso for
            // .nkit.iso). Report that, matching the SrcType logic in NKitTask.LogParams — otherwise
            // fixing/converting a .nkit.gcz reports Iso instead of Gcz.
            Nanook.NKit.Nintendo.WiiGc.ImageInfo wiiGcInfo0 = stepResults[0].ImageInfo as Nanook.NKit.Nintendo.WiiGc.ImageInfo;
            r.ContainerType = (wiiGcInfo0?.IsNkitDecoded ?? false)
                ? wiiGcInfo0.NKitSourceContainer
                : (stepResults[0].ImageInfo?.ContainerType ?? ContainerType.Unknown);

            r.Source = stepResults[0].SourceFile;
            r.InFilePath = r.Source.BasePath;
            r.Name = r.Source.Name;

            r.Steps = stepResults.Count;

            if (imageSkipped && badSys)
            {
                r.ErrorMsg = $"Skipping - System filter set to {stepResults[0].SystemType}";
                r.VerifyResult = VerifyResult.Unverified;
                return r;
            }

            NKitStepResult[] results = imageSkipped ? new[] { stepResults[0].Result } : stepResults.Select(a => a.Result).ToArray();

            r.Scan = GetScan(results);

            NKitStepResult verifyResult = GetVerifyResult(results, null, out IParts parts, out ChecksumType? hashType);
            r.VerifyType = verifyResult.VerifyType;
            r.VerifyResult = verifyResult.VerifyResult;
            r.VerifyChecksum = hashType == null || (parts?.Length ?? 0) != 1 ? "" : parts[0].Checksums[hashType.Value].ToHexString();
            r.CRC = imageSkipped ? 0 : (verifyResult.ResultCrc ?? verifyResult.Scan?.Crc ?? 0);
            r.Size = verifyResult.ResultSize ?? verifyResult.Scan?.Size ?? 0;

            results = stepResults.Select(a => a.Result).ToArray();
            NKitStepContext main = stepResults.LastOrDefault(a => task == TaskType.Scan || task == TaskType.Verify || (a.StepInfo?.WriteImage ?? false));
            NKitStepResult verify = GetVerifyResult(results, main?.Result?.MatchedDatItem, out _, out _);

            if (success)
            {
                r.Key = key ?? r.Source.Key;
                DatItem datMatch = verify.MatchedDatItem; //verify may be set and MatchedItem is null of no match found.
                //if (_state.Verifier.VerifyResult == VerifyResult.Unverified || _state.Verifier.VerifyResult == VerifyResult.VerifySuccess)
                //    datMatch = (_state.Verifier.DatItem ?? main.DatMatch);

                r.DatMatch = datMatch?.Name;

                r.SupportsEncryption = main.ImageInfo.SourceSupportsEncryption;
                r.HasEncryption = stepResults[0].ImageInfo.SourceHasEncryption; //source encryption

                if (skipName != null)
                {
                    r.OutPath = Path.GetDirectoryName(skipName);
                    r.OutFileName = Path.GetFileName(skipName);
                }
                else
                {
                    //OutputFile idx = main.OutFiles.FirstOrDefault(a => a.Type == OutputFileType.Index);
                    //string[] nm = idx != null ? new string[] { idx.FinalFullName } : main.OutFiles.Where(a => a.Type == OutputFileType.File || a.Type == OutputFileType.FixFile).Select(a => a.FinalFullName).ToArray();
                    ResultOutFile idx = main.StepInfo.OutputType == OutputType.FolderIndex ? resultFiles.FirstOrDefault(a => a?.IsIndex ?? false) : null;
                    if (resultFiles.OutputType == OutputType.Scan || resultFiles.OutputType == OutputType.None)
                    {
                        r.OutPath = "";
                        r.OutFileName = "";
                    }
                    else if (resultFiles.OutputType == OutputType.FolderIndex || resultFiles.OutputType == OutputType.FolderFiles)
                    {
                        r.OutPath = resultFiles.FolderName;
                        r.OutFileName = "";
                    }
                    else if (resultFiles.OutputType == OutputType.FileStore)
                    {
                        r.OutPath = main.WritePath;
                        r.OutFileName = "";
                    }
                    else if (resultFiles.Count != 0)
                    {
                        r.OutPath = main.StepInfo.WriteImage ? Path.GetDirectoryName(resultFiles[0].FinalName) : null;
                        r.OutFileName = string.Join("|", resultFiles.Select(a => Path.GetFileName(a.FinalName + (a.DeletedExisting ? " [Dupe]" : ""))));
                    }
                    else if (r.OutPath == null)
                        r.OutPath = resultFiles.FolderName;
                    //r.OutPath = main.OutFiles.FirstOrDefault(a => a.Type == OutputFileType.Folder)?.FinalFullName;
                }

                r.OutScanFilePath = resultFiles?.ScanFileName;
                r.OutKeyFilePath = resultFiles?.KeyFileName;
                r.DateStamp = DateTime.Now;
                r.Duration = (int)(DateTime.Now - started).TotalSeconds;
                r.DecryptedCrc = r.SupportsEncryption && r.Scan != null ? r.Scan.CrcDecrypted : 0; //only set CrcDecrypted when encryption is supported. Else Crc is the main CRC (there is no DecryptedCrc)
            }
            else
            {
                if (main == null && stepResults.Count > 0)
                    main = stepResults[0];
                //r.Steps = 0;
                r.CRC = 0;
                r.Size = 0; //main.ImageSize;
                r.DateStamp = DateTime.Now;
                r.Duration = (int)(DateTime.Now - started).TotalSeconds;
                r.VerifyResult = VerifyResult.Error;
                r.ErrorMsg = exception;
            }
            r.StepFiles = resultFiles;
            r.StepResults = stepResults;
            return r;
        }

        /// <summary>
        /// Release the heavy per-image object graph this result roots but does not expose: the
        /// step-context list (which transitively holds ImageInfo, AreaView/FST, Header and the
        /// section buffers referenced through it). Everything the result surfaces is already
        /// distilled into scalar fields plus <see cref="Scan"/> and <see cref="Source"/>, which are
        /// KEPT. Call this once a host has finished inspecting the freshly-produced result and is
        /// about to retain it long-term (e.g. the UI keeping one result per row across a large
        /// multi-image run) so each processed image's graph is not accumulated. Immediate consumers
        /// (tests, report writers) that read StepResults right after processing must do so before
        /// calling this. Idempotent.
        /// </summary>
        public void ReleaseHeavyReferences() => StepResults = null;

        /// <summary>
        /// Release EVERYTHING heavy this result roots, including <see cref="Scan"/> and
        /// <see cref="Source"/> — not just the step-context graph freed by
        /// <see cref="ReleaseHeavyReferences"/>. For a Wii/GC image the retained <see cref="Scan"/>
        /// transitively roots the parsed filesystem (<c>FileSystemInfo</c>/partition map) and every
        /// <c>ScanArea</c>/<c>ScanSection</c> + section buffers, which is tens of MiB PER IMAGE.
        /// A host that keeps one result per row for the whole session (the UI) and only ever reads
        /// the SCALAR verify outcome (VerifyResult/VerifyType/CRC/ErrorMsg) does not need Scan or
        /// Source at all; retaining them across many rows is the dominant source of the UI memory
        /// climb. Call this ONLY after all scalar fields have been read off the result. After this
        /// the result still carries its scalar summary, but Scan/Source/StepResults are gone.
        /// Idempotent.
        /// </summary>
        public void ReleaseAllReferences()
        {
            StepResults = null;
            Scan = null;
            Source = null;
            // StepFiles (ResultOutFiles) holds a reference to the NKitStepContext (via its _stepContext
            // field, used during rename/OutputType). That step context re-roots the ENTIRE per-image
            // graph — ImageInfo, the SectionProcessors and their Buffers/SegmentedMemoryStream, and the
            // Scan with all its ScanSection/SectionData/SectionItem objects. So nulling Scan/Source above
            // does nothing while StepFiles is kept: a heap snapshot shows ResultOutFiles with an
            // inclusive size of ~6 MB EACH, accumulating one per retained UI row. Everything the result
            // surfaces from StepFiles (OutPath/OutFileName/OutScanFilePath/OutKeyFilePath) is already
            // captured into scalar fields during Create and rename has already run, so drop it too.
            StepFiles = null;
        }

        private string escape(string value, char separator)
        {
            value ??= "";
            if (value.Contains(separator))
            {
                if (value.Contains('"'))
                    value = $"\"{value.Replace("\"", "\"\"")}\"";
                else
                    value = $"\"{value}\"";
            }
            return value;
        }

        public string Header(char separator) => string.Join(separator.ToString(), this.HeaderArray().Select(a => escape(a, separator)).ToArray());

        public string ToString(char separator) => string.Join(separator.ToString(), this.ToArray().Select(a => escape(a, separator)).ToArray());

        public Dictionary<string, string> ToDictionary()
        {
            string[] hdr = this.HeaderArray();
            int i = 0;
            return this.ToArray().ToDictionary(a => hdr[i++]);
        }

        internal static Scan GetScan(NKitStepResult[] results)
        {
            //Get the last scan, stop when hitting !FullImage
            for (int i = results.Length - 1; i >= 0; i--)
            {
                NKitStepResult r = results[i];
                if (r.StepInfo != null)
                {
                    if (!r.StepInfo.FullScan)
                        return null;
                    if (r.Scan != null)
                        return r.Scan;
                }
            }
            return null;
        }

        internal static NKitStepResult GetVerifyResult(NKitStepResult[] results, DatItem mainDatItem, out IParts parts, out ChecksumType? hashType)
        {
            //the final step that verified
            NKitStepResult verifyResult = results?.LastOrDefault(a => a?.StepInfo?.VerifyMethod != VerifyMethod.NoVerify);
            hashType = null;
            parts = null;

            if (verifyResult != null && verifyResult.StepInfo != null)
            {
                parts = NKitVerify.GetStepChecksums(verifyResult, verifyResult == results.Last());
                if ((parts?.Length ?? 0) != 0 && verifyResult.ChkCompared != null)
                {
                    Checksums chk = parts[0].Checksums;
                    if (chk.HasHash && verifyResult.ChkCompared.Contains(ChecksumType.Md5))
                        hashType = ChecksumType.Md5;
                    else if (chk.HasHash && verifyResult.ChkCompared.Contains(ChecksumType.Sha1))
                        hashType = ChecksumType.Sha1;
                    else if (chk.HasHash && verifyResult.ChkCompared.Contains(ChecksumType.XxHash))
                        hashType = ChecksumType.XxHash;
                }
                return verifyResult;
            }
            else
            {
                NKitStepResult res = results.LastOrDefault();
                return new NKitStepResult()
                {
                    ResultCrc = res?.ResultCrc ?? 0,
                    ResultSize = res?.ResultSize ?? 0,
                    MatchedDatItem = mainDatItem,
                    VerifyType = VerifyMethod.NoVerify.ToString(),
                    VerifyResult = VerifyResult.Unverified
                };
            }
        }

        internal static NKitStepContext GetCompletionStep(TaskType task, List<NKitStepContext> steps) =>
            //the final step that ran and output a file(s)
            steps.LastOrDefault(a => task == TaskType.Scan || task == TaskType.Verify || (a.StepInfo?.WriteImage ?? false));
    }
}