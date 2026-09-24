
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //Retail     / ISO       / 4.38GiB / Wiped WBFS image containing only the Game partitions (No update or channel)
        [Fact]
        public void Znevb_Xneg_Jvv_HFN_Ra_Se_Rf__GameOnly_zip_Fix()
        {
            string fileName = @"Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.zip";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_Znevb_Xneg_Jvv_HFN_Ra_Se_Rf__GameOnly_zip_Fix_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"../../../../../WipedImages/_dats/wii.dat";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"../../../../../WipedImages/_fix/wii_files";

            SystemPresetSettings presets = base.CreatePresets("Fix", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.Wii;

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
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.zip//Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.iso", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly", f.CleanName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly", f.Name);
            Assert.Equal(SourceImageType.Iso, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.iso", f.ImageFiles[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly", f.ImageFiles[0].NameOnly);
            Assert.Equal(".iso", f.ImageFiles[0].Extension);
            Assert.Equal(0x118240000L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.Zip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.zip", f.ArchiveFiles[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".zip", f.ArchiveFiles[0].Extension);
            Assert.Equal(0xfadd4L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.Null(f.IndexFile);

            ////////////////////////////////////////
            // Step Info
            ////////////////////////////////////////
            Assert.NotNull(i);
            Assert.True(i.CanCrc);
            Assert.False(i.CanHash);
            Assert.False(i.CreateInChecksum);
            Assert.False(i.CreateOutChecksum);
            Assert.False(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.False(i.FullScan);
            Assert.True(i.IsExpand);
            Assert.True(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Fix-WiiGc", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Fix, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("iso", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x118240000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.iso", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x122df8000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C78CB2FD", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.iso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x9b65dcb5U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x118240000L, r.ResultSize.Value);
            Assert.Null(r.Scan);
            Assert.NotNull(r.StepInfo);
            Assert.Equal(VerifyResult.Unverified, r.VerifyResult);
            Assert.Equal("NoVerify", r.VerifyType);

            ////////////////////////////////////////
            // StepResult DatItem
            ////////////////////////////////////////
            Assert.NotNull(r.MatchedDatItem);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso", r.MatchedDatItem.FileName);
            Assert.Equal(".iso", r.MatchedDatItem.FileNameExt);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", r.MatchedDatItem.Name);
            Assert.Equal(0x1, r.MatchedDatItem.Length);
            Assert.Equal(1, r.MatchedDatItem.Bins.Length);
            Assert.Equal(1, r.MatchedDatItem.Parts.Length);
            Assert.False(r.MatchedDatItem.IsMultiPart);
            Assert.NotNull(r.MatchedDatItem);
            Assert.Equal(1, r.MatchedDatItem.Length);
            Assert.Equal(0x118240000L, r.MatchedDatItem[0].Size);
            Assert.NotNull(r.MatchedDatItem[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:9B65DCB5, Md5:666C3E5020DF6576B8C61E8EAE86B50D, Sha1:479AE1EBC2282BF4386C353BFED7AA60045713B1", r.MatchedDatItem[0].Checksums.ToString(true, true));
            Assert.NotNull(r.MatchedDatItem[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso", r.MatchedDatItem[0].FileName);

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.Iso, t.ContainerType);
            Assert.Equal(SystemType.Wii, t.System);
            Assert.Equal(TaskType.Fix, t.Task);
            Assert.Equal(0x118240000L, t.Size);
            Assert.Equal(0x9b65dcb5U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.NotNull(t.DatMatch);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.Null(t.OutScanFilePath);
            Assert.True(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x122df8000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C78CB2FD", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)_GameOnly.iso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            base.Complete();
        }
    }
}