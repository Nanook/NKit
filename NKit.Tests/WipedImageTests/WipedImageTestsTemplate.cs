using Nanook.NKit;
using Nanook.NKit.Dats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NKit.Tests.Full.Wiped
{
    internal class WipedImageTestsTemplate
    {
        public static string CreateTest(NKitTaskResults t, string testName, string taskOptions, string sys, string inPath, string basePath, string fileName, string name, string info, string extractFileName, string dats, string keys, string fixInfo, string fixFiles)
        {
            if (name[0] >= '0' && name[0] <= '9')
                name = "_" + name;
            StringBuilder _sb = new StringBuilder();
            Action<string> X = new Action<string>(s => _sb.Append(s));

            X($@"
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{{
    // Area=""Full""/Group=""Wiped"" are inherited from WipedImageTestsBase; only the per-System trait
    // is emitted per generated class so tests can be filtered by console system too.
    [Trait(""System"", ""{sys}"")]
    public partial class WipedImage_{sys}_Tests : WipedImageTestsBase
    {{
        //{info}
        [Fact]
        public void {name}_{testName}()
        {{
            string fileName = @""{fileName}"";
            string inPath = Path.GetFullPath(Path.Combine(@""{inPath}"", ""{sys}""));
            string outFolderName = $""{sys}_{name}_{testName}_{{Guid.NewGuid():N}}"";
            string basePath = Directory.CreateDirectory(Path.Combine(""."", outFolderName)).FullName;
            string dats = @""{dats}"";
            string keys = @""{keys}"";
            string fixInfo = @""{fixInfo}"";
            string fixFiles = @""{fixFiles}"";

            SystemPresetSettings presets = base.CreatePresets(""{t.Task}"", @""{taskOptions}"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.{Enum.Parse<SystemType>(sys, true)};");
            X($@"

            NKitTaskResults t = base.ProcessImage(presets);
            NKitStepResult r = t.StepResults[0].Result;
            SourceFile f = t.Source;
            IStepInfo i = r.StepInfo;
            Scan s = t.Scan;");

            sourceFile(X, t.Source);
            stepInfo(X, t.StepResults[0].Result.StepInfo);
            scan(X, t.StepResults[0].Result.StepInfo?.SrcScan, "i.SrcScan", "Source");
            datItem(X, t.StepResults[0].Result.StepInfo?.DatMatch, "i.DatMatch", "Source");
            stepResult(X, t.StepResults[0].Result);
            datItem(X, t.StepResults[0].Result.MatchedDatItem, "r.MatchedDatItem", "StepResult");
            results(X, t, basePath);
            scan(X, t.Scan, "t.Scan", "Result");
            if (extractFileName != null)
                checksumFiles(X, extractFileName);

            X($@"

            base.Complete();
        }}
    }}
}}
");
            return _sb.ToString();
        }

        private static void checksumFiles(Action<string> X, string extractFileName)
        {
            X($@"

            ////////////////////////////////////////
            // Extracted Tracks
            ////////////////////////////////////////

            string[] extractLines = File.ReadAllLines(Path.Combine(base.GetPath(), ""{Path.GetFileName(extractFileName)}""));
            Assert.Equal(extractLines.Length, base.ExtractFileResults.Length + ExtractFileDirectories.Length);
            int l = 0;
            for (int c = 0; c < base.ExtractFileResults.Length; c++)
                Assert.Equal(extractLines[l++], $""{{base.ExtractFileResults[c].Crc:x8}}\t{{base.ExtractFileResults[c].Size:x}}\t/{{base.ExtractFileResults[c].FileName.Replace('\\', '/')}}"");
            for (int c = 0; c < base.ExtractFileDirectories.Length; c++)
                Assert.Equal(extractLines[l++], base.ExtractFileDirectories[c].Replace('\\', '/'));
            ");
        }

        private static void results(Action<string> X, NKitTaskResults t, string basePath)
        {
            X($@"

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.{t.ContainerType}, t.ContainerType);
            Assert.Equal(SystemType.{t.System}, t.System);
            Assert.Equal(TaskType.{t.Task}, t.Task);
            Assert.Equal(0x{t.Size:x}L, t.Size);
            Assert.Equal(0x{t.CRC:x8}U, t.CRC);
            Assert.Equal(0x{t.DecryptedCrc:x8}U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.{t.VerifyResult}, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));");
            if (string.IsNullOrEmpty(t.OutPath))
                testString(X, t.OutPath, "t.OutPath"); //scan
            else if (t.OutPath.TrimEnd('\\', '/') == basePath.TrimEnd('\\', '/'))
            {
                X($@"
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));");
            }
            else
                testStringWithPath(X, t.OutPath, "t.OutPath", "basePath"); //extract
            testString(X, t.Name, "t.Name");
            testString(X, t.VerifyType, "t.VerifyType");
            testString(X, t.VerifyChecksum, "t.VerifyChecksum");
            testString(X, t.DatMatch, "t.DatMatch");
            testString(X, t.ErrorMsg, "t.ErrorMsg");
            testString(X, t.OutFileName, "t.OutFileName");
            testStringWithPath(X, t.OutKeyFilePath, "t.OutKeyFilePath", "basePath");
            testStringWithPath(X, t.OutScanFilePath, "t.OutScanFilePath", "basePath");
            X($@"
            Assert.{t.HasEncryption}(t.HasEncryption);
            Assert.{t.SupportsEncryption}(t.SupportsEncryption);
            Assert.{t.ImageSkipped}(t.ImageSkipped);
            Assert.{(t.Key == null ? "" : "Not")}Null(t.Key);");
            if (t.Key != null)
            {
                X($@"
            Assert.Equal(""{t.Key.ToHexString()}"", t.Key.ToHexString());");
            }
            resultFiles(X, t.StepFiles, "t.StepFiles", "basePath");
        }

        private static void scan(Action<string> X, Scan s, string name, string title)
        {
            X($@"

            ////////////////////////////////////////
            // {title} Scan
            ////////////////////////////////////////
            Assert.{(s == null ? "" : "Not")}Null({name});");
            if (s == null)
                return;
            //int filesCount = 0;
            // int foldersCount = 0;
            testString(X, s.Name, $@"{name}.Name");
            X($@"
            Assert.Equal(SystemType.{s.SystemType}, f.SystemType);
            Assert.Equal(0x{s.Crc:x8}U, {name}.Crc);
            Assert.Equal(0x{s.CrcDecrypted:x8}U, {name}.CrcDecrypted);
            Assert.Equal(0x{s.Size:x}L, {name}.Size);
            Assert.Equal({s.VirtualFsTotalFileCount}, {name}.VirtualFsTotalFileCount);
            Assert.Equal({s.VirtualFsTotalFoldersCount}, {name}.VirtualFsTotalFoldersCount);");
            foreach (string k in s.Properties.Keys)
                testString(X, s.Properties[k].ToXmlValue(), $@"{name}.Properties[""{k}""].ToXmlValue()");
            X($@"
            Assert.Equal({s.Areas.Count}, {name}.Areas.Count);");
            foreach (ScanArea a in s.Areas)
            {
                // An area's Properties can legitimately be null (an area type not populated by the
                // reader's SetProperties, e.g. a key-missing/default area) — the real serializer
                // (ScanParser) guards this the same way, so mirror it here rather than NRE.
                if (a.AreaInfo?.Properties != null)
                    foreach (string k in a.AreaInfo.Properties.Keys)
                        testString(X, a.AreaInfo.Properties[k].ToXmlValue(), $@"{name}.Areas[{a.AreaInfo.AreaNo}].AreaInfo.Properties[""{k}""].ToXmlValue()");
                X($@"
            Assert.Equal(AreaType.{a.Type}, {name}.Areas[{a.AreaInfo.AreaNo}].Type);
            Assert.Equal(0x{a.Crc:x8}U, {name}.Areas[{a.AreaInfo.AreaNo}].Crc);
            Assert.Equal(0x{a.CrcDecrypted:x8}U, {name}.Areas[{a.AreaInfo.AreaNo}].CrcDecrypted);
            Assert.Equal(0x{a.Size:x}L, {name}.Areas[{a.AreaInfo.AreaNo}].Size);");
                Dictionary<FsType, int> fsTypeCounts = WipedImageTestsBase.GetIsoFsTypes(a);
                if (fsTypeCounts.Count != 0)
                {
                    string nm = $"{name.Replace(".", "_")}Types{a.AreaInfo.AreaNo}";
                    X($@"
            Dictionary<FsType, int> {nm} = WipedImageTestsBase.GetIsoFsTypes({name}.Areas[{a.AreaInfo.AreaNo}]);
            Assert.Equal({fsTypeCounts.Count}, {nm}.Count);");
                    foreach (KeyValuePair<FsType, int> kv in WipedImageTestsBase.GetIsoFsTypes(a))
                    {
                        X($@"
            Assert.Equal({kv.Value}, {nm}[FsType.{kv.Key}]);");
                    }
                }
            }

        }

        private static void datItem(Action<string> X, DatItem di, string name, string title)
        {
            X($@"

            ////////////////////////////////////////
            // {title} DatItem
            ////////////////////////////////////////
            Assert.{(di == null ? "" : "Not")}Null({name});");
            if (di == null)
                return;
            X($@"
            Assert.Equal(""{di.FileName}"", {name}.FileName);
            Assert.Equal(""{di.FileNameExt}"", {name}.FileNameExt);
            Assert.Equal(""{di.Name}"", {name}.Name);
            Assert.Equal(0x{di.Length:x}, {name}.Length);
            Assert.Equal({di.Bins.Length}, {name}.Bins.Length);
            Assert.Equal({di.Parts.Length}, {name}.Parts.Length);
            Assert.{di.IsMultiPart} ({name}.IsMultiPart); ");
            fileParts(X, di, name, "");
        }

        private static void stepResult(Action<string> X, NKitStepResult r)
        {
            X($@"

            ////////////////////////////////////////
            // Step Result
            ////////////////////////////////////////
            Assert.{(r == null ? "" : "Not")}Null(r);");
            if (r == null)
                return;
            X($@"
            Assert.Equal(""{r.FinalName}"", r.FinalName);
            ");
            if (r.ChkCompared != null)
                X($@"Assert.Equal(""{string.Join('|', r.ChkCompared.Select(a => a.ToString()))}"", string.Join('|', r.ChkCompared.Select(a => a.ToString())));");
            else
                X($@"Assert.Null(r.ChkCompared);");
            fileParts(X, r.InFileParts, "r.InFileParts", "inPath");
            fileParts(X, r.OutFileParts, "r.OutFileParts", "basePath");
            X($@"
            Assert.{(r.ResultCrc == null ? "" : "Not")}Null(r.ResultCrc);");
            if (r.ResultCrc != null)
            {
                X($@"
            Assert.Equal(0x{r.ResultCrc:x8}U, r.ResultCrc.Value);");
            }
            X($@"
            Assert.{(r.ResultSize == null ? "" : "Not")}Null(r.ResultSize);");
            if (r.ResultCrc != null)
            {
                X($@"
            Assert.Equal(0x{r.ResultSize:x}L, r.ResultSize.Value);");
            }
            X($@"
            Assert.{(r.Scan == null ? "" : "Not")}Null(r.Scan);
            Assert.{(r.StepInfo == null ? "" : "Not")}Null(r.StepInfo);
            Assert.Equal(VerifyResult.{r.VerifyResult}, r.VerifyResult);
            Assert.Equal(""{r.VerifyType}"", r.VerifyType);");
        }

        private static void stepInfo(Action<string> X, IStepInfo i)
        {
            X($@"

            ////////////////////////////////////////
            // Step Info
            ////////////////////////////////////////
            Assert.{(i == null ? "" : "Not")}Null(i);");
            if (i == null)
                return;
            X($@"
            Assert.{i.CanCrc}(i.CanCrc);
            Assert.{i.CanHash}(i.CanHash);
            Assert.{i.CreateInChecksum}(i.CreateInChecksum);
            Assert.{i.CreateOutChecksum}(i.CreateOutChecksum);
            Assert.{i.CreateScan}(i.CreateScan);
            Assert.{i.DeleteSourceCandidate}(i.DeleteSourceCandidate);
            Assert.{i.FullScan}(i.FullScan);
            Assert.{i.IsExpand}(i.IsExpand);
            Assert.{i.IsFix}(i.IsFix);
            Assert.{i.IsLossy}(i.IsLossy);
            Assert.{i.WriteImage}(i.WriteImage);
            Assert.Equal(""{i.Name}"", i.Name);
            Assert.Equal(OutputType.{i.OutputType}, i.OutputType);
            Assert.{i.ReqChk}(i.ReqChk);
            Assert.{i.ReqPatch}(i.ReqPatch);
            Assert.Equal(TaskType.{i.StepType}, i.StepType);
            Assert.Equal(VerifyMethod.{i.VerifyMethod}, i.VerifyMethod);
            ");
            if (i.VerifyChecksums != null)
                X($@"Assert.Equal(""{string.Join('|', i.VerifyChecksums.Select(a => a.ToString()))}"", string.Join('|', i.VerifyChecksums.Select(a => a.ToString())));");
            else
                X($@"Assert.Null(i.VerifyChecksums);");
            testString(X, i.Config, "i.Config");
            testString(X, i.ImageConfig, "i.ImageConfig");
            fileParts(X, i.SrcParts, "i.SrcParts", "inPath");
        }

        private static void sourceFile(Action<string> X, SourceFile f)
        {
            X($@"

            ////////////////////////////////////////
            // Source File
            ////////////////////////////////////////
            Assert.{(f == null ? "" : "Not")}Null(f);");
            if (f == null)
                return;
            X($@"
            Assert.Equal(inPath.TrimEnd('\\', '/'), f.BasePath.TrimEnd('\\', '/'));");
            testString(X, f.FriendlyFullPath.Replace(f.BasePath, ""), @"f.FriendlyFullPath.Replace(f.BasePath, """")");
            X($@"
            Assert.Equal(""{f.CleanName}"", f.CleanName);
            Assert.Equal(""{f.Name}"", f.Name);
            Assert.Equal(SourceImageType.{f.ImageType}, f.ImageType);
            Assert.Equal(SourceFileResult.{f.Status}, f.Status);
            Assert.Equal(SystemType.{f.SystemType}, f.SystemType);
            Assert.{f.IsArchive}(f.IsArchive);
            Assert.{f.IsArchived}(f.IsArchived);
            Assert.{f.IsDeleted}(f.IsDeleted);
            Assert.{f.IsFolderMode}(f.IsFolderMode);
            Assert.{f.IsSplitArchive}(f.IsSplitArchive);
            Assert.{f.IsSplitImage}(f.IsSplitImage);
            Assert.Equal(0x{f.Length:x}L, f.Length);
            Assert.Equal({f.ImageFiles.Length}, f.ImageFiles.Length);");
            for (int i = 0; i < f.ImageFiles.Length; i++)
            {
                X($@"
            Assert.Equal(""{f.ImageFiles[i].FileName}"", f.ImageFiles[{i}].FileName);
            Assert.Equal(""{f.ImageFiles[i].NameOnly}"", f.ImageFiles[{i}].NameOnly);
            Assert.Equal(""{f.ImageFiles[i].Extension}"", f.ImageFiles[{i}].Extension);
            Assert.Equal(0x{f.ImageFiles[i].Size:x}L, f.ImageFiles[{i}].Size);
            Assert.{f.ImageFiles[i].IsArchived}(f.ImageFiles[{i}].IsArchived);");
            }
            X($@"
            Assert.Equal(SourceArchiveType.{f.ArchiveType}, f.ArchiveType);
            Assert.{(f.ArchiveFiles == null ? "" : "Not")}Null(f.ArchiveFiles);");
            if (f.ArchiveFiles != null)
            {
                X($@"
            Assert.Equal({f.ArchiveFiles.Length}, f.ArchiveFiles.Length);");
                for (int i = 0; i < f.ArchiveFiles.Length; i++)
                {
                    X($@"
            Assert.Equal(""{f.ArchiveFiles[i].FileName}"", f.ArchiveFiles[{i}].FileName);
            Assert.Equal(""{f.ArchiveFiles[i].NameOnly}"", f.ArchiveFiles[{i}].NameOnly);
            Assert.Equal(""{f.ArchiveFiles[i].Extension}"", f.ArchiveFiles[{i}].Extension);
            Assert.Equal(0x{f.ArchiveFiles[i].Size:x}L, f.ArchiveFiles[{i}].Size);
            Assert.{f.ArchiveFiles[i].IsArchived}(f.ArchiveFiles[{i}].IsArchived);");
                }
            }
            X($@"
            Assert.{(f.Key == null ? "" : "Not")}Null(f.Key);");
            if (f.Key != null)
            {
                X($@"
            Assert.Equal(""{f.Key.ToHexString()}"", f.Key.ToHexString());");
            }
            indexFile(X, f.IndexFile, "f.IndexFile");
        }

        private static void indexFile(Action<string> X, IndexFile x, string name)
        {
            X($@"
            // {name} IndexFile Test
            Assert.{(x == null ? "" : "Not")}Null({name});");
            if (x == null)
                return;
            X($@"
            Assert.Equal(0x{x.Crc:x}U, {name}.Crc);
            Assert.Equal(IndexFileType.{x.FileType}, {name}.FileType);
            Assert.{x.IsArchived}({name}.IsArchived);
            Assert.{x.IsTemp}({name}.IsTemp);
            Assert.Equal(0x{x.Offset:x}L, {name}.Offset);
            Assert.Equal(0x{x.Size:x}L, {name}.Size);
            Assert.{x.WiiUFstMismatch}({name}.WiiUFstMismatch);");
            testString(X, x.Extension, $"{name}.Extension");
            testString(X, x.FileName, $"{name}.FileName");
            testString(X, x.NameOnly, $"{name}.NameOnly");
            testString(X, x.Postfix, $"{name}.Postfix");
            if (string.IsNullOrEmpty(x.Path))
                testString(X, x.Path, $"{name}.Path");
            else
            {
                X($@"
            Assert.Equal(inPath.TrimEnd('\\', '/'), {name}.Path.TrimEnd('\\', '/')); ");
            }
            X($@"
            Assert.Equal({x.Items.Length}, {name}.Items.Length);");
            for (int c = 0; c < x.Items.Length; c++)
            {
                X($@"
            Assert.Equal(IndexTrackBasicType.{x.Items[c].BasicType}, {name}.Items[{c}].BasicType);
            Assert.Equal({x.Items[c].BlockIdx}, {name}.Items[{c}].BlockIdx);
            Assert.Equal(0x{x.Items[c].BlockSize:x}, {name}.Items[{c}].BlockSize);
            Assert.Equal({x.Items[c].Blocks}, {name}.Items[{c}].Blocks);
            Assert.Equal(MediaType.{x.Items[c].ChdMediaType}, {name}.Items[{c}].ChdMediaType);
            Assert.Equal(CdSubType.{x.Items[c].ChdSubType}, {name}.Items[{c}].ChdSubType);
            Assert.{x.Items[c].FileIsMissing}({name}.Items[{c}].FileIsMissing);
            Assert.Equal(0x{x.Items[c].ImageOffset:x}L, {name}.Items[{c}].ImageOffset);
            Assert.Equal(0x{x.Items[c].PhysicalOffset:x}L, {name}.Items[{c}].PhysicalOffset);
            Assert.Equal(0x{x.Items[c].LogicalOffset:x}L, {name}.Items[{c}].LogicalOffset);
            Assert.Equal(0x{x.Items[c].LogicalSize:x}L, {name}.Items[{c}].LogicalSize);
            Assert.Equal(0x{x.Items[c].Pad:x}, {name}.Items[{c}].Pad);
            Assert.Equal(0x{x.Items[c].PadSize:x}, {name}.Items[{c}].PadSize);
            Assert.Equal(0x{x.Items[c].PostGap:x}, {name}.Items[{c}].PostGap);
            Assert.Equal(0x{x.Items[c].PreGap:x}, {name}.Items[{c}].PreGap);
            Assert.Equal(0x{x.Items[c].PreGapDataSize:x}, {name}.Items[{c}].PreGapDataSize);
            Assert.Equal(0x{x.Items[c].PreGapSubSize:x}, {name}.Items[{c}].PreGapSubSize);
            Assert.Equal(IndexTrackType.{x.Items[c].PreGapType}, {name}.Items[{c}].PreGapType);
            Assert.Equal({x.Items[c].Session}, {name}.Items[{c}].Session);
            Assert.Equal(0x{x.Items[c].Size:x}L, {name}.Items[{c}].Size);
            Assert.Equal(0x{x.Items[c].SubSize:x}, {name}.Items[{c}].SubSize);
            Assert.Equal({x.Items[c].TrackIndex}, {name}.Items[{c}].TrackIndex);
            Assert.Equal(IndexTrackType.{x.Items[c].TrackType}, {name}.Items[{c}].TrackType);");
                testString(X, x.Items[c].ChdTag, $"{name}.Items[{c}].ChdTag");
                testString(X, x.Items[c].Comment, $"{name}.Items[{c}].Comment");
                testString(X, x.Items[c].FileName, $"{name}.Items[{c}].FileName");
            }
            X($@"
            Assert.Equal({x.Additional.Count}, {name}.Additional.Count);");
            for (int c = 0; c < x.Additional.Count; c++)
            {
                X($@"
            Assert.Equal(0x{x.Additional[c].Size:x}L, {name}.Additional[{c}].Size);");
                testString(X, x.Additional[c].FileName, $"{name}.Additional[{c}].FileName");
            }
        }

        private static void resultFiles(Action<string> X, ResultOutFiles files, string name, string pathName)
        {
            X($@"
            Assert.{(files == null ? "" : "Not")}Null({name});");
            if (files == null)
                return;
            X($@"
            Assert.Equal({files.Count}, {name}.Count);");
            for (int c = 0; c < files.Count; c++)
            {
                X($@"
            Assert.Equal(0x{files[c].Size:x}L, {name}[{c}].Size);
            Assert.{files[c].IsIndex}({name}[{c}].IsIndex);
            Assert.{files[c].IsImageName}({name}[{c}].IsImageName);");
                testString(X, files[c].Checksums.ToString(true, true), $"{name}[{c}].Checksums.ToString(true, true)");
                testString(X, files[c].FileName, $"{name}[{c}].FileName");
            }
        }

        private static void fileParts(Action<string> X, IParts parts, string name, string pathName)
        {
            X($@"
            Assert.{(parts == null ? "" : "Not")}Null({name});");
            if (parts == null)
                return;
            X($@"
            Assert.Equal({parts.Length}, {name}.Length);");
            for (int c = 0; c < parts.Length; c++)
            {
                X($@"
            Assert.Equal(0x{parts[c].Size:x}L, {name}[{c}].Size);");
                testString(X, parts[c].Checksums.ToString(true, true), $"{name}[{c}].Checksums.ToString(true, true)");
                testString(X, parts[c].FileName, $"{name}[{c}].FileName");
            }
        }

        private static void testStringWithPath(Action<string> X, string value, string name, string pathName)
        {
            X($@"
            Assert.{(value == null ? "" : "Not")}Null({name});");
            if (value == null)
                return;
            if (Path.GetDirectoryName(value) != "")
            {
                X($@"
            Assert.Equal(Path.Combine({pathName}, ""{Path.GetFileName(value)}""), {name});");
            }
            else
            {
                X($@"
            Assert.Equal(""{value}"", {name});");
            }
        }

        private static void testString(Action<string> X, string value, string name)
        {
            X($@"
            Assert.{(value == null ? "" : "Not")}Null({name});");
            if (value == null)
                return;
            X($@"
            Assert.Equal(""{value}"", {name});");
        }
    }
}