using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    // Area/Group traits declared on the base so every generated WipedImage_<System>_Tests partial
    // class inherits them (xUnit applies class-level traits to derived test classes) — no per-file
    // edits across the ~366 generated files. Verified via the `Group=Wiped` trait filter.
    [Trait("Area", "Full")]
    [Trait("Group", "Wiped")]
    public class WipedImageTestsBase
    {
        private string _basePath;

        internal ExtractFileTestItem[] ExtractFileResults { get; set; }
        internal string[] ExtractFileDirectories { get; set; }

        protected string GetPath([CallerFilePath] string path = null) => Path.GetDirectoryName(path);

        protected SystemPresetSettings CreatePresets(string task, string taskOptions, string basePath, string fileName, string outFolderName, string dats, string keys, string fixInfo, string fixFiles)
        {
            string image = Path.Combine(basePath, fileName);
            string outPath = Path.Combine(Environment.CurrentDirectory, outFolderName);
            TaskType taskType = Enum.Parse<TaskType>(task, true);
            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = taskType,
                ScanOut = outPath,
                Extract = taskType == TaskType.Extract ? taskOptions : "",
                Convert = taskType == TaskType.Convert ? taskOptions : "",
                Results = true,
                ResultsOut = Path.Combine(outPath, "Results.txt"),
                LogOut = Path.Combine(outPath, "Log.txt"),
                LogOutLevel = LogLevel.Info,
                FixInfo = fixInfo,
                FixFiles = fixFiles,
                Dat = dats,
                Keys = keys,
                Out = outPath,
                R = false,
                Arc = true,
                V = taskType != TaskType.Convert ? Verify.Y : Verify.N
            };
            presets.In.Add(image);
            return presets;
        }

        internal static string GetFileInfo(string basePath, string fullName)
        {
            XXHash64 xxHash = XXHash64.Create();
            Crc crc = new Crc();
            FileInfo file = new FileInfo(Path.Combine(basePath, fullName));
            uint crcValue;
            using (Stream xxHashStream = new CryptoStream(Stream.Null, xxHash, CryptoStreamMode.Write))
            {
                using (Stream crcStream = new CryptoStream(xxHashStream, crc, CryptoStreamMode.Write))
                {
                    using (FileStream fs = file.OpenRead())
                        fs.CopyTo(crcStream);
                    crcValue = crc.Value;
                }

            }

            return $"0x{xxHash.Hash.ToHexString()}L, 0x{crcValue:x8}U, 0x{file.Length:x}L {file.FullName.Substring(basePath.Length + 1).Replace('\\', '/')}";
        }

        internal static Dictionary<FsType, int> GetIsoFsTypes(ScanArea a)
        {
            Dictionary<FsType, int> typeCounts = new Dictionary<FsType, int>();
            if (a.Type == AreaType.FileSystem && a.FsInfo is Nanook.NKit.Iso.Iso9660.FileSystemInfo)
            {
                foreach (Nanook.NKit.Iso.Iso9660.FstFile f in a.FsInfo.FileSystem.Files)
                {
                    foreach (Nanook.NKit.Iso.Iso9660.FstLink l in f.Links)
                    {
                        if (!typeCounts.ContainsKey(l.FsType))
                            typeCounts.Add(l.FsType, 1);
                        else
                            typeCounts[l.FsType]++;
                    }
                }
            }
            return typeCounts;
        }

        protected NKitTaskResults ProcessImage(SystemPresetSettings presets)
        {
            _basePath = presets.Out;
            ExtractFileTestHandler extractFiles = new ExtractFileTestHandler();

            string outPath = Path.GetDirectoryName(_basePath);
            DirectoryInfo basePath = Directory.CreateDirectory(_basePath);

            if (presets.System == SystemType.Wii && presets.FixFiles != null)
            {
                // NKit now reads archive-backed Wii recovery files (channel/VC + update partitions)
                // directly, so the fix files are used in-place — no need to decompress/stage them.
                presets.OutAsDatMatch = presets.Dat != null;
                presets.V = Verify.N;
            }

            AppSettings settings = new AppSettings(presets);
            StringBuilder logOutput = new StringBuilder();

            CancellationTokenSource cancel = new CancellationTokenSource();
            Action<string, LogLevel> l = (ms, lv) => Trace.Write(ms);
            NKitTask task = null;
            using (Log log = settings.GetLog(l))
            {
                SourceFile file = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, cancel.Token).OrderBy(a => a.Name).FirstOrDefault();
                NKitTaskContext taskContext = new NKitTaskContext(settings, file, l);
                task = new NKitTask(taskContext);
                NKitInput input = new NKitInput(taskContext.Steps[0]);
                Stream readStream = taskContext.Steps[0].SourceFile.OpenFileStream();
                SystemType systemType;
                bool customChkCandidate = taskContext.TaskType == TaskType.Scan || taskContext.TaskType == TaskType.Verify || taskContext.TaskType == TaskType.Expand || taskContext.TaskType == TaskType.Fix;
                input.Open(readStream, customChkCandidate, taskContext.AppSettings.SystemType, taskContext.AppSettings.GetSystem(taskContext.Steps[0].SourceFile.BasePath), out systemType);
                taskContext.Initialise(systemType);

                input.Setup();
                task.Setup(systemType);

                taskContext.CreateSteps(taskContext.Steps[0].ImageInfo.StepImageInfo);

                if (taskContext.Steps.Count != 1)
                    throw new Exception("Test requires one step");

                string[] stepNames = taskContext.Steps.Select(a => a.StepInfo.StepType.ToString()).ToArray();
                task.StepsInit(taskContext);
                task.LogParams(taskContext.DatManager, taskContext.Settings);

                NKitStepContext stepContext = taskContext.Steps[0];
                NKitStep step = new NKitStep(input, stepContext);
                if (stepContext.StepInfo.IsFix)
                    stepContext.ImageInfo.Mode = ReadMode.Fix; //must be applied after Image setup but before reading - tricky setting to calculate and apply
                task.StepStart(stepContext);
                stepContext.Step.Initialise(stepContext);
                if (taskContext.TaskType == TaskType.Convert || taskContext.TaskType == TaskType.Expand || taskContext.TaskType == TaskType.FixExtract || taskContext.TaskType == TaskType.Fix)
                    ((StepBase)stepContext.Step).EnableTestMode(true);
                ((StepBase)stepContext.Step).ExtractHandler = extractFiles; //override for testing

                foreach (ISection b in step.ReadSections()) { }
                ;
                foreach (ISection b in step.Patch()) { }
                ;
                step.Complete();
                stepContext.Complete();
                input.Close();

                task.StepComplete(stepContext);
                task.StepsComplete();
                task.Complete(true, null);
            }

            this.ExtractFileDirectories = extractFiles.Directories.ToArray();
            this.ExtractFileResults = extractFiles.Files.Values.ToArray();
            return task.Results;
        }

        protected void Complete() => Directory.Delete(_basePath, true);
    }
}