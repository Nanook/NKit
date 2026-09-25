
using Nanook.NKit;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Gamecube_Tests : WipedImageTestsBase
    {
        //Demo       / RVZ       / 1.36GiB / No Magic ID
        [Fact]
        public void Crnpu_f_Pnfgyr_Tnzrphor_Grpu_Qrzb_rvz_Expand()
        {
            string fileName = @"Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Gamecube"));
            string outFolderName = $"Gamecube_Crnpu_f_Pnfgyr_Tnzrphor_Grpu_Qrzb_rvz_Expand_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Expand", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb", f.CleanName);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb", f.Name);
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
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.rvz", f.ImageFiles[0].FileName);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x21804L, f.ImageFiles[0].Size);
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
            Assert.True(i.CanCrc);
            Assert.True(i.CanHash);
            Assert.True(i.CreateInChecksum);
            Assert.False(i.CreateOutChecksum);
            Assert.True(i.CreateScan);
            Assert.True(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.True(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Expand-Image", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Expand, i.StepType);
            Assert.Equal(VerifyMethod.InChecksums, i.VerifyMethod);
            Assert.Equal("XxHash|Crc32", string.Join('|', i.VerifyChecksums.Select(a => a.ToString())));
            Assert.NotNull(i.Config);
            Assert.Equal("iso", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x57058000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:AC45D745, Md5:2B59140E20C6F6CBDC7A082582D4167E, Sha1:6D682E343161E41084B6C287DA5165DCD385471F, XxHash:016B054E00AF2D96", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.iso", r.FinalName);
            Assert.Equal("XxHash|Crc32", string.Join('|', r.ChkCompared.Select(a => a.ToString())));
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x57058000L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:AC45D745, Md5:2B59140E20C6F6CBDC7A082582D4167E, Sha1:6D682E343161E41084B6C287DA5165DCD385471F, XxHash:016B054E00AF2D96", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x57058000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:AC45D745", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.iso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xac45d745U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x57058000L, r.ResultSize.Value);
            Assert.NotNull(r.Scan);
            Assert.NotNull(r.StepInfo);
            Assert.Equal(VerifyResult.VerifySuccess, r.VerifyResult);
            Assert.Equal("InChecksums [XxHash+Crc32]", r.VerifyType);

            ////////////////////////////////////////
            // StepResult DatItem
            ////////////////////////////////////////
            Assert.Null(r.MatchedDatItem);

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.Rvz, t.ContainerType);
            Assert.Equal(SystemType.GameCube, t.System);
            Assert.Equal(TaskType.Expand, t.Task);
            Assert.Equal(0x57058000L, t.Size);
            Assert.Equal(0xac45d745U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.VerifySuccess, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("InChecksums [XxHash+Crc32]", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("016B054E00AF2D96", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.iso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x57058000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:AC45D745", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb.iso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Crnpu'f_Pnfgyr_Tnzrphor_Grpu_Qrzb", t.Scan.Name);
            Assert.Equal(SystemType.GameCube, f.SystemType);
            Assert.Equal(0xac45d745U, t.Scan.Crc);
            Assert.Equal(0xac45d745U, t.Scan.CrcDecrypted);
            Assert.Equal(0x57058000L, t.Scan.Size);
            Assert.Equal(333, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(3, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("GameCube", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("057058000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("AC45D745", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(1, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("55[0x0]R56", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Japan", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("AQQRZB", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("C5FB49F4", t.Scan.Areas[0].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("55[0x0]R", t.Scan.Areas[0].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00008A523", t.Scan.Areas[0].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0xac45d745U, t.Scan.Areas[0].Crc);
            Assert.Equal(0xac45d745U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x57058000L, t.Scan.Areas[0].Size);

            base.Complete();
        }
    }
}