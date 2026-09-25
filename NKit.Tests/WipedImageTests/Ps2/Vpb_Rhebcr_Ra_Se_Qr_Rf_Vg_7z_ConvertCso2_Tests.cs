
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Ps2_Tests : WipedImageTestsBase
    {
        //Retail     / ISO       / 866MiB  / UDF NSR02, DVD with UDF and ISO9660 (Unicode UDF)
        [Fact]
        public void Vpb_Rhebcr_Ra_Se_Qr_Rf_Vg_7z_ConvertCso2()
        {
            string fileName = @"Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Ps2"));
            string outFolderName = $"Ps2_Vpb_Rhebcr_Ra_Se_Qr_Rf_Vg_7z_ConvertCso2_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"cso2", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.PS2;

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
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).7z//Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).iso", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.CleanName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.Name);
            Assert.Equal(SourceImageType.Iso, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.PS2, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).iso", f.ImageFiles[0].FileName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".iso", f.ImageFiles[0].Extension);
            Assert.Equal(0x361a8000L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x7799L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.Null(f.IndexFile);

            ////////////////////////////////////////
            // Step Info
            ////////////////////////////////////////
            Assert.NotNull(i);
            Assert.False(i.CanCrc);
            Assert.False(i.CanHash);
            Assert.True(i.CreateInChecksum);
            Assert.False(i.CreateOutChecksum);
            Assert.True(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.False(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Convert-Iso-CsoZso", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.True(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("cso/zso", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("cso2", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x0L, i.SrcParts[0].Size);
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
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).cso", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x361a8000L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:16422783, Md5:9AA22C3BB3E32B35DFB6EFFBF06DA6B1, Sha1:A55EB1373D4A6A8A5637A7C187387F604CA2DA4F, XxHash:6B21B875DC78BBF3", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0xa928ddL, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:A3782500", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).cso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x16422783U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x361a8000L, r.ResultSize.Value);
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
            Assert.Equal(ContainerType.Iso, t.ContainerType);
            Assert.Equal(SystemType.PS2, t.System);
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x361a8000L, t.Size);
            Assert.Equal(0x16422783U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).cso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0xa928ddL, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:A3782500", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg).cso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Vpb (Rhebcr) (Ra,Se,Qr,Rf,Vg)", t.Scan.Name);
            Assert.Equal(SystemType.PS2, f.SystemType);
            Assert.Equal(0x16422783U, t.Scan.Crc);
            Assert.Equal(0x16422783U, t.Scan.CrcDecrypted);
            Assert.Equal(0x361a8000L, t.Scan.Size);
            Assert.Equal(52, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(2, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("PS2", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("0361A8000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("16422783", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(1, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("000008800", t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("5180EE77", t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("18D8DA391B3C49C9", t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("00006C350", t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0x16422783U, t.Scan.Areas[0].Crc);
            Assert.Equal(0x16422783U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x361a8000L, t.Scan.Areas[0].Size);
            Dictionary<FsType, int> t_ScanTypes0 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[0]);
            Assert.Equal(3, t_ScanTypes0.Count);
            Assert.Equal(2, t_ScanTypes0[FsType.System]);
            Assert.Equal(25, t_ScanTypes0[FsType.Iso9660]);
            Assert.Equal(50, t_ScanTypes0[FsType.Udf]);

            base.Complete();
        }
    }
}