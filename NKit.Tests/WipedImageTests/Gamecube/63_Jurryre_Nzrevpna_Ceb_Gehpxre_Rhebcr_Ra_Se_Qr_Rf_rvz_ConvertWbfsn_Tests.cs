
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Gamecube_Tests : WipedImageTestsBase
    {
        //Retail     / RVZ       / 1.36GiB / Fst in 2nd section, Files at the start of the image
        [Fact]
        public void _63_Jurryre_Nzrevpna_Ceb_Gehpxre_Rhebcr_Ra_Se_Qr_Rf_rvz_ConvertWbfsn()
        {
            string fileName = @"63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Gamecube"));
            string outFolderName = $"Gamecube__63_Jurryre_Nzrevpna_Ceb_Gehpxre_Rhebcr_Ra_Se_Qr_Rf_rvz_ConvertWbfsn_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"wbfs:n", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf)", f.CleanName);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf)", f.Name);
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
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).rvz", f.ImageFiles[0].FileName);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x299244L, f.ImageFiles[0].Size);
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
            Assert.True(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.False(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.True(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Convert-WiiGc-Lossy", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("wbfs/ciso", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("wbfs", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x57058000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:B667CF77, Md5:CCF0BFF8E679F293C579C8B68AB42039, Sha1:62B31A087D24CBB6779E1A03BB08722329E2E473, XxHash:A181B6E708BB4E3A", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).wbfs", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x11200000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:A8CA91D5", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).wbfs~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xb667cf77U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x57058000L, r.ResultSize.Value);
            Assert.NotNull(r.Scan);
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
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x57058000L, t.Size);
            Assert.Equal(0xb667cf77U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).wbfs", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x11200000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:A8CA91D5", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf).wbfs~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("63 Jurryre - Nzrevpna Ceb Gehpxre (Rhebcr) (Ra,Se,Qr,Rf)", t.Scan.Name);
            Assert.Equal(SystemType.GameCube, f.SystemType);
            Assert.Equal(0xb667cf77U, t.Scan.Crc);
            Assert.Equal(0xb667cf77U, t.Scan.CrcDecrypted);
            Assert.Equal(0x57058000L, t.Scan.Size);
            Assert.Equal(359, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(38, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("GameCube", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("057058000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("B667CF77", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(1, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("TJRC3C", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Pal", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("63 Jurryre", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("F75134CB", t.Scan.Areas[0].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("TJRC", t.Scan.Areas[0].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("0003AC118", t.Scan.Areas[0].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0xb667cf77U, t.Scan.Areas[0].Crc);
            Assert.Equal(0xb667cf77U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x57058000L, t.Scan.Areas[0].Size);

            base.Complete();
        }
    }
}