using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;

namespace NKit.Tests
{
    public class ProcessingTestBase
    {
        protected static TheoryData<ProcessingTestItem> CreateData(string filename, string outputBasePath = null)
        {
            TheoryData<ProcessingTestItem> data = new TheoryData<ProcessingTestItem>();
            if (!File.Exists(filename))
            {
                data.Add(new ProcessingTestItem() { Index = 0, TestName = $"Skipped" });
                return data;
            }

            //load the csv
            // inputBasePath = the Tests/ directory itself (data paths use ../ to reference files
            // in the parent NKitExternalTestFiles/ directory — "1 level below working folder").
            // outputBasePath = where test output dirs are created (may differ when inputs are read-only).
            string inputBasePath = new FileInfo(filename).DirectoryName;
            string basePath = outputBasePath ?? new FileInfo(filename).Directory.Parent.FullName;
            string testName = Path.GetFileNameWithoutExtension(filename);
            List<string[]> values = new List<string[]>();
            string[] columnNames = null;
            foreach (string l in File.ReadAllLines(filename).Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (columnNames == null)
                    columnNames = l.Split('\t');
                else
                    values.Add(l.Split('\t'));
            }

            for (int i = 0; i < values.Count; i++)
            {
                int c = 0;
                data.Add(new ProcessingTestItem()
                {
                    Index = i + 1,
                    Values = values[i].ToDictionary(a => columnNames[c++]),
                    BasePath = basePath,
                    InputBasePath = inputBasePath,
                    TestName = testName
                });
            }
            return data;
        }

        public ProcessingTestBase()
        {
        }

        private string join(DirectoryInfo basePath, string name) => Path.GetFullPath(Path.Combine(basePath.FullName, name));

        private string cleansePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            return new DirectoryInfo(path).FullName.Replace('\\', '/').TrimEnd('/'); //DirectoryInfo resolves ..
        }

        private T getSetting<T>(ProcessingTestItem data, string name, T defaultValue, bool isPath, DirectoryInfo basePath)
        {
            string value;
            if (data.Values.TryGetValue($"p_{name}", out value))
            {
                if (value == "")
                    return defaultValue;
                else if (typeof(T) == typeof(bool))
                    return (T)Convert.ChangeType(value == "y", typeof(T));
                else if (typeof(T).IsEnum)
                    return (T)Enum.Parse(typeof(T), value, true);
                else
                {
                    if (value.ToLower() == "<blank>")
                        value = "";
                    if (isPath)
                    {
                        int midx = value.LastIndexOf("//");
                        if (midx != -1 && midx != 0) //can't start with // (might be unc) 
                            value = string.Concat(join(basePath, value.Substring(0, midx)), value.Substring(midx)); //ensure we stay in the test folder
                        else
                            value = join(basePath, value); //ensure we stay in the test folder
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
            }
            else
                return defaultValue;
        }

        private void setup(string setupString, DirectoryInfo basePath, DirectoryInfo inputBasePath = null)
        {
            inputBasePath ??= basePath;
            string filesTxtName = "files.txt";

            foreach (string v in setupString.Split('|'))
            {
                if (v.StartsWith("i:"))
                {
                    string[] itms = v.Split(':', 3); //0=i, 1=name, 2=format
                    string pth = join(basePath, itms[1]);
                    string pthIso = Path.ChangeExtension(join(basePath, itms[1]), ".iso");
                    string ext = Path.GetExtension(pth).ToLower();
                    bool isoExists = File.Exists(pthIso);
                    bool notIso = ext.ToLower() != ".iso";

                    string convert = itms.Length < 3 ? ext.Trim('.') : itms[2];
                    string imgNameOnly = Path.GetFileName(itms[1]).ToLower();

                    if (ext == ".iso" || !isoExists)
                    {
                        if (imgNameOnly.StartsWith("gcbasic"))
                            TestImageBuilder.CreateImageGcBasic(pthIso); //supports leading path and diff post name e.g. img/gcbasic_X.iso
                        else if (imgNameOnly.StartsWith("wiibasic"))
                            TestImageBuilder.CreateImageWiiBasic(pthIso);
                    }
                    if (notIso)
                    {
                        TestImageBuilder.ConvertImage(pthIso, convert);
                        if (!isoExists)
                            File.Delete(pthIso);
                    }
                }
                else if (v.StartsWith("d:"))
                {
                    string[] itms = v.Split(':');
                    if (itms.Length > 2)
                    {
                        string imgNameOnly = Path.GetFileName(itms[2]).ToLower();
                        string pth = join(basePath, itms[1]);
                        if (imgNameOnly.StartsWith("gcbasic", StringComparison.OrdinalIgnoreCase))
                            TestImageBuilder.CreateDatGcBasic(pth, Path.GetFileNameWithoutExtension(itms[2]), itms.Length > 3 ? itms[3] : "");
                    }
                }
                else if (v.StartsWith("s:"))
                {
                    string[] itms = v.Split(':');
                    if (itms.Length > 2)
                    {
                        string imgNameOnly = Path.GetFileName(itms[2]).ToLower();
                        string pth = join(basePath, itms[1]);
                        if (imgNameOnly.StartsWith("gcbasic", StringComparison.OrdinalIgnoreCase))
                            CreateScan(join(basePath, itms[2]), pth);
                    }
                }
                else if (v.StartsWith("f:"))
                {
                    string[] itms = v.Split(':');
                    if (itms.Length > 1)
                    {
                        string pth = join(inputBasePath, itms[1]);
                        File.Copy(pth, Path.Combine(basePath.FullName, filesTxtName));
                    }
                }
            }
        }

        // Returns inputBasePath for paths starting with ../ (external fixtures), outputBasePath otherwise (created by setup()).
        private static DirectoryInfo rawBase(ProcessingTestItem data, string key, DirectoryInfo inputBasePath, DirectoryInfo outputBasePath)
        {
            string v;
            return data.Values.TryGetValue($"p_{key}", out v) && v?.StartsWith("..") == true ? inputBasePath : outputBasePath;
        }

        private SystemPresetSettings getSettings(ProcessingTestItem data, DirectoryInfo inputBasePath, DirectoryInfo outputBasePath = null)
        {
            outputBasePath ??= inputBasePath;
            SystemPresetSettings settings = new SystemPresetSettings();
            // p_In / p_In2: bare filenames (created by setup()) resolve from outputBasePath;
            // paths starting with ../ reference external fixtures and resolve from inputBasePath.
            string rawIn1 = data.Values.TryGetValue("p_In", out string rv1) ? rv1 : null;
            string rawIn2 = data.Values.TryGetValue("p_In2", out string rv2) ? rv2 : null;
            DirectoryInfo in1Base = rawIn1?.StartsWith("..") == true ? inputBasePath : outputBasePath;
            DirectoryInfo in2Base = rawIn2?.StartsWith("..") == true ? inputBasePath : outputBasePath;
            string in1 = getSetting(data, "In", (string)null, true, in1Base);
            string in2 = getSetting(data, "In2", (string)null, true, in2Base);
            if (in1 != null)
                settings.In.Add(in1);
            if (in2 != null)
                settings.In.Add(in2);

            settings.Arc = getSetting(data, "Arc", settings.Arc, false, inputBasePath);
            settings.BaseInPath = getSetting(data, "BaseInPath", settings.BaseInPath, true, inputBasePath);
            settings.ConsoleLevel = getSetting(data, "ConsoleLevel", settings.ConsoleLevel, false, inputBasePath);
            settings.Convert = getSetting(data, "Convert", settings.Convert, false, inputBasePath);
            settings.Dat = getSetting(data, "Dat", settings.Dat, true, rawBase(data, "Dat", inputBasePath, outputBasePath));
            settings.DeleteProcessed = getSetting(data, "DeleteProcessed", settings.DeleteProcessed, false, inputBasePath);
            settings.Extract = getSetting(data, "Extract", settings.Extract, false, inputBasePath);
            settings.FixFiles = getSetting(data, "FixFiles", settings.FixFiles, true, rawBase(data, "FixFiles", inputBasePath, outputBasePath));
            settings.FixInfo = getSetting(data, "FixInfo", settings.FixInfo, true, rawBase(data, "FixInfo", inputBasePath, outputBasePath));
            settings.Keys = getSetting(data, "Keys", settings.Keys, true, rawBase(data, "Keys", inputBasePath, outputBasePath));
            // Output paths resolve relative to outputBasePath (writable), not inputBasePath (may be read-only)
            settings.LogOut = getSetting(data, "LogOut", settings.LogOut, true, outputBasePath);
            settings.LogOutLevel = getSetting(data, "LogOutLevel", settings.LogOutLevel, false, inputBasePath);
            settings.Out = getSetting(data, "Out", settings.Out, true, outputBasePath);
            settings.OutAsDatMatch = getSetting(data, "OutAsDatMatch", settings.OutAsDatMatch, false, inputBasePath);
            settings.R = getSetting(data, "R", settings.R, false, inputBasePath);
            settings.Results = getSetting(data, "Results", settings.Results, false, inputBasePath);
            settings.ResultsOut = getSetting(data, "ResultsOut", settings.ResultsOut, true, outputBasePath);
            settings.ScanIn = getSetting(data, "ScanIn", settings.ScanIn, true, rawBase(data, "ScanIn", inputBasePath, outputBasePath));
            settings.ScanOut = getSetting(data, "ScanOut", settings.ScanOut, true, outputBasePath);
            settings.SkipIfCompleted = getSetting(data, "SkipIfCompleted", settings.SkipIfCompleted, false, inputBasePath);
            settings.System = getSetting(data, "System", settings.System, false, inputBasePath);
            settings.Task = getSetting(data, "Task", settings.Task, false, inputBasePath);
            settings.Tmp = getSetting(data, "Tmp", settings.Tmp, true, outputBasePath);
            settings.V = getSetting(data, "V", settings.V, false, inputBasePath);
            return settings;
        }

        private void validateResults(ProcessingTestItem data, NKitTaskResults results, SystemPresetSettings presets, DirectoryInfo basePath, DirectoryInfo inputBasePath = null)
        {
            inputBasePath ??= basePath;

            // Diagnostic: print all result fields for test index 1 so we can compare actual vs expected
            if (data.Index == 1)
            {
                Console.WriteLine($"\n=== DIAGNOSTIC: Test {data.Index} '{data.TestName}' ===");
                Console.WriteLine($"  InputBasePath : {inputBasePath.FullName}");
                Console.WriteLine($"  OutputBasePath: {basePath.FullName}");
                Console.WriteLine($"  Source.Name   : {results.Source?.Name}");
                Console.WriteLine($"  ErrorMsg      : {results.ErrorMsg}");
                Console.WriteLine($"  ImageSkipped  : {results.ImageSkipped}");
                Console.WriteLine("  --- Actual results ---");
                foreach (KeyValuePair<string, string> kv in results.ToDictionary())
                {
                    string expectedKey = $"r_{kv.Key}";
                    string expected = data.Values.TryGetValue(expectedKey, out string ev) ? ev : "(no expectation)";
                    bool match = string.Equals(kv.Value, expected, StringComparison.OrdinalIgnoreCase)
                              || expected == "<ValidNumber>" && long.TryParse(kv.Value, out _)
                              || expected == "<ValidDateTime>" && DateTime.TryParse(kv.Value, out _)
                              || expected.StartsWith("<rx") || expected == "<Ignore>"
                              || expected == "<SrcPath>" || expected == "<SrcParent>"
                              || expected == "<OutPath>" || expected == "<OutPathDir>"
                              || expected == "<ScanOutPath>" || expected == "<NotEmpty>";
                    string status = match ? "OK  " : "FAIL";
                    Console.WriteLine($"  [{status}] {kv.Key,-28} actual='{kv.Value}' expected='{expected}'");
                }
                Console.WriteLine("=== END DIAGNOSTIC ===\n");
            }
            Regex rx = new Regex(@"^\<rx(.*):(.*)\>$", RegexOptions.IgnoreCase);
            Match mrx;

            //test the results that would be output to the results file
            foreach (KeyValuePair<string, string> actual in results.ToDictionary())
            {
                string expected = data.Values[$"r_{actual.Key}"];
                string lExpected = expected.ToLower();
                string finalExpected = "";
                string finalActual = actual.Value;
                bool isRegex = false;
                bool isValid;

                if (lExpected == "<ignore>")
                {
                    finalExpected = expected;
                    isValid = true;
                }
                else if (lExpected == "<validdatetime>")
                {
                    finalExpected = expected;
                    isValid = DateTime.TryParse(finalActual, out _);
                }
                else if (lExpected == "<validnumber>")
                {
                    finalExpected = expected;
                    isValid = long.TryParse(finalActual, out _);
                }
                else if (lExpected == "<notempty>")
                {
                    finalExpected = expected;
                    isValid = !string.IsNullOrWhiteSpace(finalActual);
                }
                else if ((mrx = rx.Match(expected)).Success)
                {
                    isRegex = true;
                    RegexOptions opt = mrx.Groups[1].Value.ToLower().Contains('i') ? RegexOptions.IgnoreCase : (RegexOptions)0;
                    finalExpected = mrx.Groups[2].Value;
                    if (mrx.Groups[1].Value.ToLower().Contains('p')) //is path
                    {
                        finalActual = cleansePath(finalActual);
                        finalExpected = Regex.Replace(finalExpected, @"<srcpath>", Regex.Escape(cleansePath(inputBasePath.FullName) ?? ""), opt);
                        finalExpected = Regex.Replace(finalExpected, @"<outpath>", Regex.Escape(cleansePath(presets.Out) ?? ""), opt);
                        finalExpected = Regex.Replace(finalExpected, @"<scanoutpath>", Regex.Escape(cleansePath(presets.ScanOut) ?? ""), opt);
                    }
                    finalExpected = Regex.Replace(finalExpected, @"<srcnameonly>", Regex.Escape(results.Source.Name) ?? "", opt);
                    isValid = Regex.Match(finalActual ?? "", finalExpected, opt).Success;
                }
                else if (lExpected == "<srcpath>")
                {
                    finalExpected = cleansePath(inputBasePath.FullName);
                    finalActual = cleansePath(actual.Value);
                    isValid = finalExpected == finalActual;
                }
                else if (lExpected == "<srcparent>")
                {
                    finalExpected = cleansePath(inputBasePath.Parent.FullName);
                    finalActual = cleansePath(actual.Value);
                    isValid = finalExpected == finalActual;
                }
                else if (lExpected == "<outpath>")
                {
                    finalExpected = cleansePath(presets.Out);
                    finalActual = cleansePath(actual.Value);
                    isValid = finalExpected == finalActual;
                }
                else if (lExpected == "<outpathdir>")
                {
                    finalExpected = cleansePath(Path.Combine(presets.Out, results.Source.Name));
                    finalActual = cleansePath(actual.Value);
                    isValid = finalExpected == finalActual;
                }
                else if (lExpected == "<scanoutpath>")
                {
                    finalExpected = cleansePath(presets.ScanOut);
                    finalActual = cleansePath(actual.Value);
                    isValid = finalExpected == finalActual;
                }
                else if (lExpected == "<srcnameonly>")
                {
                    finalExpected = results.Source.Name;
                    isValid = finalExpected == finalActual;
                }
                else
                {
                    finalExpected = expected;
                    isValid = finalExpected == finalActual;
                }

                string message = $"{actual.Key} : Expected{(isRegex ? "(RX)" : "")} '{finalExpected}' : Actual '{actual.Value}'";
                Debug.WriteLine(message);
                Assert.True(isValid, message);

            }
        }

        internal static void CreateScan(string image, string pth)
        {
            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = TaskType.Scan,
                Out = pth
            };
            presets.In.Add(image);
            AppSettings settings = new AppSettings(presets);
            NKitTaskResults results;
            StringBuilder logOutput = new StringBuilder();

            CancellationTokenSource cancel = new CancellationTokenSource();

            using (Log log = settings.GetLog((ms, lv) => Console.Write(ms)))
            {
                List<SourceFile> images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null).ToList();
                SourceFile img = images.FirstOrDefault();
                NKitProcessor p = new NKitProcessor(settings, img, (ms, lv) => Trace.Write(logOutput.Append(ms)));

                // look at results class to see if it was successful
                results = p.Process(cancel.Token);
            }
        }


        public virtual void Tests(ProcessingTestItem data)
        {
            if (data.Index == 0)
                return;

            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(data.BasePath, $"{data.TestName}_{data.Index:D3}_{Guid.NewGuid():N}"));

            setup(data.Values["Setup"], basePath, new DirectoryInfo(data.InputBasePath));
            SystemPresetSettings presets = this.getSettings(data, new DirectoryInfo(data.InputBasePath), basePath);
            AppSettings settings = new AppSettings(presets);
            NKitTaskResults results;
            StringBuilder logOutput = new StringBuilder();

            CancellationTokenSource cancel = new CancellationTokenSource();

            using (Log log = settings.GetLog((ms, lv) => Console.Write(ms)))
            {
                List<SourceFile> images = SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null).OrderBy(a => a.Name).ToList();
                SourceFile img = images.FirstOrDefault();
                NKitProcessor p = new NKitProcessor(settings, img, (ms, lv) => Trace.Write(logOutput.Append(ms)));

                // look at results class to see if it was successful
                results = p.Process(cancel.Token);
            }

            validateResults(data, results, presets, basePath,
                data.Values.TryGetValue("p_In", out string rvIn) && rvIn?.StartsWith("..") == true
                    ? new DirectoryInfo(data.InputBasePath)
                    : basePath);

            //results have been verified. Test file system

            if (!string.IsNullOrEmpty(presets.Tmp)) //if tmp is set ensure all temp files wrote there
                Assert.True(results.StepFiles.All(a => a.FileName.EndsWith("~"))); //no temp folder any more  //.Select(b => b.FileName).Where(a => !string.IsNullOrWhiteSpace(a)).All(b => Regex.IsMatch(cleansePath(b), $"^{Regex.Escape(cleansePath(presets.Tmp))}", RegexOptions.IgnoreCase)));

            if (!results.ImageSkipped && string.IsNullOrEmpty(results.ErrorMsg)) //skipped
            {
                //check results exist
                switch (results.StepFiles.OutputType)
                {
                    case OutputType.Image:
                    case OutputType.Files:
                        foreach (ResultOutFile fs in results.StepFiles)
                        {
                            Assert.False(fs.FinalName?.EndsWith('~') ?? false, $"No Result file names should end with '~' - {fs.FinalName}");
                            Assert.True(File.Exists(fs.FinalName), $"File '{fs.FinalName}' does not exist");
                        }
                        break;
                    case OutputType.Scan:
                        Assert.False(results.OutScanFilePath?.EndsWith('~') ?? false, $"No Result scan file names should end with '~' - {results.OutScanFilePath}");
                        Assert.True(File.Exists(results.OutScanFilePath), $"Scan file '{results.OutScanFilePath}' does not exist");
                        break;
                    case OutputType.FolderIndex:
                    case OutputType.FolderFiles:
                        Assert.True(results.StepFiles.FolderName != null, "Directory name not set");
                        Assert.False(results.StepFiles.FolderName.EndsWith('~'), $"No Result directory name should end with '~' - '{results.StepFiles.FolderName}'");
                        Assert.True(Directory.Exists(results.StepFiles.FolderName), $"Directory '{results.StepFiles.FolderName}' does not exist");
                        break;
                    case OutputType.None:
                        break;
                }

                //test temp files do not exist
                foreach (NKitStepContext step in results.StepResults)
                {
                    switch (step.StepInfo.OutputType)
                    {
                        case OutputType.Image:
                        case OutputType.Files:
                        case OutputType.Scan:
                            foreach (Part fs in step.Result.OutFileParts)
                            {
                                if (fs.FileName?.EndsWith('~') ?? false) //never for dedupe
                                    Assert.False(File.Exists(fs.FileName), $"Temp File '{fs.FileName}' should not exist");
                            }
                            break;
                        case OutputType.FolderIndex:
                        case OutputType.FolderFiles:
                            if (step.WritePath?.EndsWith('~') ?? false) //never for dedupe
                                Assert.False(Directory.Exists(step.WritePath), $"Temp Directory '{step.WritePath}' should not exist");
                            break;
                        case OutputType.None:
                            break;
                    }
                }
            }

            //test results out
            if (!string.IsNullOrEmpty(presets.ResultsOut))
            {
                if (presets.Results)
                    Assert.True(File.Exists(presets.ResultsOut), $"Results File '{presets.ResultsOut}' does not exist");
                else
                    Assert.False(File.Exists(presets.ResultsOut), $"Results File '{presets.ResultsOut}' should not exist");
            }

            //usage output when no task or in params is not part of the library. It's the launch app's responsibility
            //test log out
            if (presets.ConsoleLevel == LogLevel.None)
                Assert.True(logOutput.Length == 0, $"Console output logged when ConsoleLevel set to {presets.ConsoleLevel}");
            else
                Assert.True(logOutput.Length != 0, $"No Console output logged when ConsoleLevel set to {presets.ConsoleLevel}");

            if (!string.IsNullOrEmpty(presets.LogOut))
            {
                if (presets.LogOutLevel == LogLevel.None)
                    Assert.False(File.Exists(cleansePath(presets.LogOut)), $"LogOut File '{presets.LogOut}' should not exist");
                else
                {
                    Assert.True(File.Exists(presets.LogOut), $"LogOut File '{presets.LogOut}' does not exist");
                    Assert.True(new FileInfo(presets.LogOut).Length != 0, $"LogOut File '{presets.LogOut}' does not contain data");
                }
            }

            //delete source file on process
            if (presets.DeleteProcessed && !results.Source.IsArchived && (results.Task == TaskType.Convert || results.Task == TaskType.Expand))
            {
                if (results.VerifyResult == VerifyResult.VerifySuccess && !presets.SkipIfCompleted)
                {
                    Assert.True(results.Source.IsDeleted, "Source File IsDeleted bool is not true");
                    foreach (SourceFileItem fn in results.Source.ImageFiles)
                        Assert.False(File.Exists(Path.Combine(fn.Path, fn.FileName)), $"Source File '{Path.Combine(fn.Path, fn.FileName)}' should not exist");
                    if (results.Source.IndexFile != null)
                        Assert.False(File.Exists(Path.Combine(results.Source.IndexFile.Path, results.Source.IndexFile.FileName)), $"Source Index File '{Path.Combine(results.Source.IndexFile.Path, results.Source.IndexFile.FileName)}' should not exist");
                }
                else
                {
                    Assert.False(results.Source.IsDeleted, "Source File IsDeleted bool is not false");
                    foreach (SourceFileItem fn in results.Source.ImageFiles)
                        Assert.True(File.Exists(Path.Combine(fn.Path, fn.FileName)), $"Source File '{Path.Combine(fn.Path, fn.FileName)}' does not exist");
                    if (results.Source.IndexFile != null)
                        Assert.True(File.Exists(Path.Combine(results.Source.IndexFile.Path, results.Source.IndexFile.FileName)), $"Source Index File '{Path.Combine(results.Source.IndexFile.Path, results.Source.IndexFile.FileName)}' does not exist");
                }
            }

            string crcsFileName = Path.Combine(basePath.FullName, "files.txt");
            if (File.Exists(crcsFileName)) //test extracted files
                testFiles(crcsFileName, results, false);

            basePath.Delete(true);
        }

        private void testFiles(string crcsFileName, NKitTaskResults results, bool createMode)
        {
            Dictionary<string, uint?> fileCrcs = new Dictionary<string, uint?>();

            if (!createMode)
            {
                Assert.True(File.Exists(crcsFileName));
                foreach (string x in File.ReadAllLines(crcsFileName))
                {
                    string[] sp = x.Split('\t'); //skip crcs that are "?" - we don't care
                    fileCrcs.Add(sp[0], sp[1] == "?" ? null : uint.Parse(sp[1], System.Globalization.NumberStyles.HexNumber));
                }
            }
            int filesCount = 0;
            int l = results.OutPath.Length + 1;
            foreach (string fn in Directory.GetFiles(results.OutPath, "*", SearchOption.AllDirectories))
            {
                uint actualCrc;
                uint? expectedCrc;
                string key = fn.Substring(l).Replace('\\', '/');
                if (createMode)
                    fileCrcs.Add(key, 0);

                Assert.True(fileCrcs.TryGetValue(key, out expectedCrc), $"File '{key}' not found");

                if (expectedCrc != null)
                {
                    using (Crc crc = new Crc())
                    {
                        using (Stream fs = File.OpenRead(fn))
                            actualCrc = crc.ComputeHash(fs).ReadUInt32B(0);
                    }

                    if (createMode)
                    {
                        expectedCrc = actualCrc;
                        File.AppendAllText(crcsFileName, $"{key}\t{actualCrc:x8}\n");
                    }

                    Assert.Equal(expectedCrc, actualCrc);
                }
                filesCount++;
            }
            Assert.Equal(fileCrcs.Count, filesCount);
        }
    }
}