using Nanook.NKit;
using Nanook.NKit.Dats;
using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NKit.Tests
{
    internal class TaskStepResult
    {
        public int Index;
        public string Name;
        public string ClassName;
        public string VerifyType;
        public bool IsMain;
        public bool IsVerify;
    }

    internal class TaskStepVerifySettings
    {
        public string PrmV;
        public bool InNKitScan;
        public bool Dats;
        public bool DatItem;
    }

    public class TaskStepsShared
    {
        internal static TaskStepVerifySettings[] VerifyCombos()
        {
            return new[]
            {
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = false, Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = false, Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = false, Dats = true,  DatItem = true,  },
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = true,  Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = true,  Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "n", InNKitScan = true,  Dats = true,  DatItem = true,  },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = false, Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = false, Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = false, Dats = true,  DatItem = true,  },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = true,  Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = true,  Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "y", InNKitScan = true,  Dats = true,  DatItem = true,  },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = false, Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = false, Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = false, Dats = true,  DatItem = true,  },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = true,  Dats = false, DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = true,  Dats = true,  DatItem = false, },
                new TaskStepVerifySettings() { PrmV = "datLookup", InNKitScan = true,  Dats = true,  DatItem = true,  }
            };
        }
        //hard coded checksum combos. These are detected when processing in the real code
        internal static NKitTaskContext Process(string taskType, string system, string srcType, string srcInfo, string configString, bool reqPatch, string prmV, IParts parts, bool inScan, bool dats, bool datItem, string cfg)
        {
            bool srcCrcHash = parts[0] != null && (parts[0].Checksums.HasCrc || parts[0].Checksums.HasHash);
            DatItem di = !datItem ? null : new DatItem("dat.dat", new DatItemPart[] { new DatItemPart("", 0, null, null, 0) });
            Scan scan = !inScan ? null : new Scan(SystemType.NotSet, "");

            SystemPresetSettings presets = new SystemPresetSettings()
            {
                Task = (TaskType)Enum.Parse(typeof(TaskType), taskType, true),
                System = (SystemType)Enum.Parse(typeof(SystemType), system, true),
                V = (Verify)Enum.Parse(typeof(Verify), prmV, true),
                Convert = taskType == "convert" ? configString : "",
                Extract = taskType == "extract" ? configString : "",
                Out = ""
            };

            bool isGdRom = cfg == null ? false : Regex.IsMatch(cfg, "^(chd)?gdrom", RegexOptions.IgnoreCase);
            bool forceIdx = srcInfo.Contains("idx");
            bool isDataStore = srcInfo.Contains("ds");

            AppSettings settings = new AppSettings(presets);
            SourceFile file = CreateSourceFile(srcType, isGdRom || forceIdx, isDataStore); //will need to cater for multi track images also
            NKitTaskContext task = new NKitTaskContext(settings, file, null);
            task.Steps[0].ImageInfo = new ImageInfo() { IsFolderIndex = file.IndexFile != null };
            task.Initialise(presets.System);
            string config = task.CalculateConfig(configString, task.SystemType == SystemType.Dreamcast && isGdRom);
            string srcFormat = file.IndexFile != null ? "folderindex" : "image";
            IStepsImageInfo imgInfo = new FakeImageInfo() { ReqPatch = reqPatch, Checksums = parts[0]?.Checksums ?? new Checksums(), IsIndex = file.IndexFile != null };
            task.CreateSteps(imgInfo, srcFormat, di, scan, config, configString, dats);
            return task;
        }

        internal static TaskStepResult[] GetResults(string resultString)
        {
            List<TaskStepResult> res = new List<TaskStepResult>();
            MatchCollection mc = Regex.Matches(resultString, @"([^|]*)(?:\((?:(M),?)?(?:(V):(.*?),?)?(?:C:(.*?),?)?\))");
            int i = 0;
            foreach (Match m in mc)
            {
                res.Add(new TaskStepResult()
                {
                    Index = i++,
                    Name = m.Groups[1].Value,
                    IsMain = m.Groups[2].Value != "",
                    IsVerify = m.Groups[3].Value != "",
                    VerifyType = m.Groups[4].Value,
                    ClassName = m.Groups[5].Value
                });
            }
            return res.ToArray();
        }

        internal static IParts CreateInChecksums(string srcFormat, bool nkitHeader)
        {
            Checksums chk = new Checksums();
            switch (srcFormat)
            {
                case ".nkit.iso":
                case ".nkit.gcz":
                    chk.Crc = 1;
                    break;
                case ".iso.dec":
                case ".jso":
                    chk.Md5 = new byte[0x10];
                    break;
                case ".rvz":
                case ".cso":
                case ".zso":
                case ".wbfs":
                case ".ciso":
                    if (nkitHeader)
                    {
                        chk.Crc = 1;
                        chk.Md5 = new byte[0x10];
                        chk.Sha1 = new byte[0x14];
                        chk.XxHash = 1;
                    }
                    break;
            }
            return new Parts(chk);
        }

        internal static SourceFile CreateSourceFile(string srcFormat, bool forceIndex, bool isDataStore = false)
        {
            SourceFile file;

            if (forceIndex || (new string[] { ".cue", ".tmd", ".gdi" }).Contains(srcFormat))
            {
                file = new SourceFile();
                file.IndexFile = IndexFile.Parse("", "", Encoding.UTF8.GetBytes("FILE \"File.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n"));
            }
            else if (srcFormat == ".sfb")
            {
                file = new SourceFile();
                file.ImageFiles = new[] { new SourceFileItem("", "PS3_DISC.SFB", ".SFB", "", 0, 0, 0, false, false) };
            }
            else
                file = new SourceFile();

            if (isDataStore)
            {
                // Model a DataStore (.nkds) source: the archive is the .nkds database and it
                // wraps an image whose shape (folderindex via IndexFile above, or a plain
                // image) already reflects the stored ImageFormat. A real DataStore source
                // always carries an ImageFiles entry (the wrapped image) alongside the
                // ArchiveFiles (.nkds db); Initialised() dereferences ImageFiles, so it must
                // be non-null. For the folderindex (CUE/GDI) case the IndexFile above is the
                // shape and ImageFiles is empty; for the image case we supply an .iso entry.
                const string nkds = ".nkds"; // DataStore.DatabaseFileExtension
                file.ArchiveFiles = new[] { new SourceFileItem("", "Store" + nkds, nkds, nkds, 0, 0, 0, false, false) };
                file.IsArchive = true;
                if (file.IndexFile != null)
                    file.ImageFiles = new SourceFileItem[0];
                else
                    file.ImageFiles = new[] { new SourceFileItem("", "Image.iso", ".iso", ".iso", 0, 0, 0, false, false) };
                file.Initialised();
            }

            return file;
        }
    }

    internal class FakeImageInfo : IStepsImageInfo
    {
        public bool ReqPatch { get; set; }
        public Checksums CustomChecksums { get; set; }
        public Checksums Checksums { get; set; }
        public bool HasCrc { get; set; }
        public bool HasHash { get; set; }
        public long Size { get; set; }
        public bool IsIndex { get; set; }
    }
}