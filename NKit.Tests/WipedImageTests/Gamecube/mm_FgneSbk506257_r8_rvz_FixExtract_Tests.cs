
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Gamecube_Tests : WipedImageTestsBase
    {
        //Demo       / RVZ       / 803MiB  / Image size not a multiple of 4, ends with a 1 byte file
        [Fact]
        public void mm_FgneSbk506257_r8_rvz_FixExtract()
        {
            string fileName = @"mm_FgneSbk506257_r8.rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Gamecube"));
            string outFolderName = $"Gamecube_mm_FgneSbk506257_r8_rvz_FixExtract_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("FixExtract", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.GameCube;

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
            Assert.Equal("mm_FgneSbk506257_r8.rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("mm_FgneSbk506257_r8", f.CleanName);
            Assert.Equal("mm_FgneSbk506257_r8", f.Name);
            Assert.Equal(SourceImageType.Rvz, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.GameCube, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("mm_FgneSbk506257_r8.rvz", f.ImageFiles[0].FileName);
            Assert.Equal("mm_FgneSbk506257_r8", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x13dda4L, f.ImageFiles[0].Size);
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
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("FixExtract-WiiGc", i.Name);
            Assert.Equal(OutputType.Files, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.FixExtract, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("files", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x32330b69L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:3B5BE5FB, Md5:0EAF0ADD429529709F2D2A384A2E1F31, Sha1:C47AD842E837C43AB3A5675FF3AC247DB5114E92, XxHash:73420D5FA8EFED15", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(2, r.OutFileParts.Length);
            Assert.Equal(0x11fbbL, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:207E881E", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("fst[TFNW560000][FCB79F1B][086CD268][0F85C649].bin~", r.OutFileParts[0].FileName);
            Assert.Equal(0x1b4e4L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FCB79F1B", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("appldr[20011217][FCB79F1B].bin~", r.OutFileParts[1].FileName);
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
            Assert.Equal(ContainerType.Rvz, t.ContainerType);
            Assert.Equal(SystemType.GameCube, t.System);
            Assert.Equal(TaskType.FixExtract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("mm_FgneSbk506257_r8", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("fst[TFNW560000][FCB79F1B][086CD268][0F85C649].bin|appldr[20011217][FCB79F1B].bin", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.Null(t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(2, t.StepFiles.Count);
            Assert.Equal(0x11fbbL, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.False(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:207E881E", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("fst[TFNW560000][FCB79F1B][086CD268][0F85C649].bin~", t.StepFiles[0].FileName);
            Assert.Equal(0x1b4e4L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.False(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FCB79F1B", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("appldr[20011217][FCB79F1B].bin~", t.StepFiles[1].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            base.Complete();
        }
    }
}