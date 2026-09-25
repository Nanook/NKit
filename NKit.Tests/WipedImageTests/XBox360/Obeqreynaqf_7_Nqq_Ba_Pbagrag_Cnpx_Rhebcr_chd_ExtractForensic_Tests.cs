
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_XBox360_Tests : WipedImageTestsBase
    {
        //Retail     / ISO       / 7.20GiB / Data in Video Part 2, extra pvd in xdvdfs
        [Fact]
        public void Obeqreynaqf_7_Nqq_Ba_Pbagrag_Cnpx_Rhebcr_chd_ExtractForensic()
        {
            string fileName = @"Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "XBox360"));
            string outFolderName = $"XBox360_Obeqreynaqf_7_Nqq_Ba_Pbagrag_Cnpx_Rhebcr_chd_ExtractForensic_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Extract", @"f", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.XBox360;

            NKitTaskResults t = base.ProcessImage(presets);
            NKitStepResult r = t.StepResults[0].Result;
            SourceFile f = t.Source;
            IStepInfo i = r.StepInfo;
            Scan s = t.Scan;

            ////////////////////////////////////////
            // Source File
            ////////////////////////////////////////
            Assert.NotNull(f);
            Assert.Equal(inPath.TrimEnd('\\', '/'), f.BasePath.TrimEnd('\\', '/'));
            Assert.NotNull(f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.CleanName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.Name);
            Assert.Equal(SourceImageType.Chd, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.XBox360, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd", f.ImageFiles[0].FileName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".chd", f.ImageFiles[0].Extension);
            Assert.Equal(0x6ca1L, f.ImageFiles[0].Size);
            Assert.False(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.None, f.ArchiveType);
            Assert.Null(f.ArchiveFiles);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.Null(f.IndexFile);

            ////////////////////////////////////////
            // Step Info
            ////////////////////////////////////////
            Assert.NotNull(i);
            Assert.False(i.CanCrc);
            Assert.False(i.CanHash);
            Assert.False(i.CreateInChecksum);
            Assert.False(i.CreateOutChecksum);
            Assert.False(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.False(i.FullScan);
            Assert.False(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.True(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Extract-XBox", i.Name);
            Assert.Equal(OutputType.FolderFiles, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Extract, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("folderfiles", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x0L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Sha1:E211AC871CC8106C3D3E103D703D2DC379D99244", i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Null(i.SrcParts[0].FileName);

            ////////////////////////////////////////
            // Source Scan
            ////////////////////////////////////////
            Assert.Null(i.SrcScan);

            ////////////////////////////////////////
            // Source DatItem
            ////////////////////////////////////////
            Assert.Null(i.DatMatch);

            ////////////////////////////////////////
            // Step Result
            ////////////////////////////////////////
            Assert.NotNull(r);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(0, r.OutFileParts.Length);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x00000000U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x0L, r.ResultSize.Value);
            Assert.Null(r.Scan);
            Assert.NotNull(r.StepInfo);
            Assert.Equal(VerifyResult.Unverified, r.VerifyResult);
            Assert.Equal("NoVerify", r.VerifyType);

            ////////////////////////////////////////
            // StepResult DatItem
            ////////////////////////////////////////
            Assert.Null(r.MatchedDatItem);

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.Chd, t.ContainerType);
            Assert.Equal(SystemType.XBox360, t.System);
            Assert.Equal(TaskType.Extract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.Null(t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(0, t.StepFiles.Count);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            ////////////////////////////////////////
            // Extracted Tracks
            ////////////////////////////////////////

            string[] extractLines = File.ReadAllLines(Path.Combine(base.GetPath(), "Obeqreynaqf_7_Nqq_Ba_Pbagrag_Cnpx_Rhebcr_chd_ExtractForensic_Tests.txt"));
            Assert.Equal(extractLines.Length, base.ExtractFileResults.Length + ExtractFileDirectories.Length);
            int l = 0;
            for (int c = 0; c < base.ExtractFileResults.Length; c++)
                Assert.Equal(extractLines[l++], $"{base.ExtractFileResults[c].Crc:x8}\t{base.ExtractFileResults[c].Size:x}\t/{base.ExtractFileResults[c].FileName.Replace('\\', '/')}");
            for (int c = 0; c < base.ExtractFileDirectories.Length; c++)
                Assert.Equal(extractLines[l++], base.ExtractFileDirectories[c].Replace('\\', '/'));


            base.Complete();
        }
    }
}