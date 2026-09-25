using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nanook.NKit
{
    internal class ResultOutFile : Part
    {
        public ResultOutFile(Part outFile)
        {
            base.FileName = outFile.FileName;
            base.Checksums = outFile.Checksums;
            base.Size = outFile.Size;
            base.IsIndex = outFile.IsIndex;
            base.IsImageName = outFile.IsImageName;
            base.DeleteExisting = outFile.DeleteExisting;
        }
        public bool DeletedExisting;
        public string FinalName;
    }

    internal class ResultOutFiles : List<ResultOutFile>
    {
        public const string TempChar = "~";
        public string FolderName { get; private set; }
        public string ScanFileName { get; private set; }
        public string KeyFileName { get; private set; }
        public OutputType OutputType => _stepContext.StepInfo.OutputType;

        private readonly string _path;
        private readonly string _tempPath;
        private readonly string _scanPath;
        private readonly string _keyText;
        private readonly string _scanText;
        private readonly string _scanExt;
        private readonly string _finalName;
        private readonly IStepContext _stepContext;

        public ResultOutFiles() : base() { }
        public ResultOutFiles(IEnumerable<ResultOutFile> files) : base(files) { }
        internal ResultOutFiles(string path, string tempPath, string scanPath, string keyText, string scanText, string scanExt, string finalName, IStepContext stepContext) : base()
        {
            _stepContext = stepContext;
            _path = path;
            _tempPath = tempPath;
            _scanPath = scanPath;
            _keyText = keyText;
            _scanText = scanText;
            _scanExt = scanExt;
            _finalName = finalName;

            //ensure no duplicates in outFiles (including folders)
            if (_stepContext.Result.OutFileParts.GroupBy(f => $"{SourceFiles.CleanseFileName(f.FileName)}").Any(g => g.Count() > 1))
                throw new HandledException("Some output items have duplicate names");

            this.AddRange(_stepContext.Result.OutFileParts.Select(a => new ResultOutFile((Part)a)));

            setUniqueFinalNames();
        }

        void setUniqueFinalNames()
        {
            string newPath = _path;
            string newName = _finalName;
            string newExt = "";
            if (_stepContext.StepInfo.OutputType == OutputType.Image || _stepContext.StepInfo.OutputType == OutputType.Scan)
                SourceFiles.GetFileNameParts(Path.Combine(_path, _finalName), out newPath, out newName, out _, out newExt);

            newName = SourceFiles.CleanseFileName(newName);

            bool unique = false;
            int idx = -1;

            this.RemoveAll(a => a.FileName?.EndsWith($"{_scanExt}{TempChar}") ?? false);
            bool firstPass = true;

            while (!unique) //get a unique matching name for scan and image. test existing scan matches if exists
            {
                idx++;
                string idxStr = idx == 0 ? "" : $"_{idx}";
                this.KeyFileName = _keyText == null ? null : Path.Combine(newPath, $"{newName}{idxStr}.{NKitTask._KeyExt}");
                this.ScanFileName = string.IsNullOrEmpty(_scanPath) || _scanText == null ? null : Path.Combine(_scanPath, $"{newName}{idxStr}.{_scanExt}");
                if ((this.KeyFileName != null && File.Exists(this.KeyFileName) && File.ReadAllText(this.KeyFileName) != _keyText)
                    || (this.ScanFileName != null && File.Exists(this.ScanFileName) && File.ReadAllText(this.ScanFileName) != _scanText))
                    continue;

                bool exists = false;
                switch (this.OutputType)
                {
                    case OutputType.Files:
                    case OutputType.Image:
                        foreach (ResultOutFile f in this)
                        {
                            if ((!f.DeleteExisting && !exists) || (f.DeleteExisting && firstPass))
                            {
                                SourceFiles.GetFileNameParts(f.FileName, out string currPath, out string currName, out _, out string currExt);
                                string nm = newName;
                                string ex = newExt;
                                if (this.OutputType == OutputType.Files || string.IsNullOrWhiteSpace(nm)) //OutputType.Files
                                    nm = currName.TrimEnd(TempChar[0]); //filenames with no ext (wii fix partitions)
                                if (string.IsNullOrWhiteSpace(ex))
                                    ex = currExt.TrimEnd(TempChar[0]);

                                if (!f.DeleteExisting || firstPass)
                                {
                                    f.FinalName = Path.Combine(newPath, $"{nm}{(f.DeleteExisting ? "" : idxStr)}{ex}");
                                    if (File.Exists(f.FinalName))
                                    {
                                        if (f.DeleteExisting) //not unique 0 remove temp
                                            f.DeletedExisting = true; //mark for deletion
                                        else
                                            exists = true;
                                    }
                                }
                            }
                        }
                        firstPass = false;
                        break;
                    case OutputType.FolderIndex:
                    case OutputType.FolderFiles:
                        this.FolderName = Path.Combine(newPath, $"{newName}{idxStr}");
                        if (Directory.Exists(this.FolderName))
                            continue;
                        break;
                }
                if (exists)
                    continue;
                unique = true;
            }
        }

        internal void RenameToFinalNames()
        {
            if (_stepContext.StepInfo.WriteImage)
            {
                if (this.OutputType == OutputType.Image || this.OutputType == OutputType.Files)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(this[0].FinalName)); //will create scanout folder if this is a scan
                    foreach (ResultOutFile f in this)
                    {
                        string srcFn = Path.Combine(_tempPath ?? _path, f.FileName);
                        if (f.DeletedExisting)
                            File.Delete(srcFn);
                        else if (f?.FileName != null)
                            File.Move(srcFn, f.FinalName);
                    }
                }
                else if (this.OutputType == OutputType.FolderIndex || this.OutputType == OutputType.FolderFiles)
                {
                    //sometimes the folder is locked by the OS/Virus Scanner ??
                    Exception ex = null;
                    for (int i = 0; i < 6; i++)
                    {
                        try
                        {
                            Directory.Move(_stepContext.WritePath, this.FolderName);
                            ex = null;
                            break;
                        }
                        catch (Exception e)
                        {
                            ex = e;
                            Thread.Sleep(250);
                        }
                    }
                    if (ex != null)
                        throw ex;
                }
            }

            if (this.ScanFileName != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.ScanFileName));
                File.WriteAllText(this.ScanFileName, _scanText);
            }
            if (this.KeyFileName != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.KeyFileName));
                File.WriteAllText(this.KeyFileName, _keyText);
            }
        }

        internal static void DeleteTempFiles(IEnumerable<IStepContext> steps)
        {
            foreach (IStepContext step in steps)
            {
                if ((step.Result?.OutFileParts?.Length ?? 0) != 0 && step.StepInfo.WriteImage)
                {
                    foreach (Part f in step.Result.OutFileParts)
                    {
                        try
                        {
                            string fn = Path.Combine(step.WritePath, f.FileName);
                            if (File.Exists(fn))
                                File.Delete(fn);
                        }
                        catch { }
                    }
                }
            }
        }
    }

}