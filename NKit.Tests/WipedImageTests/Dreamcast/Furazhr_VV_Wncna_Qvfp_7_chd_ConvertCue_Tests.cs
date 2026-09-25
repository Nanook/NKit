
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Dreamcast_Tests : WipedImageTestsBase
    {
        //Retail     / CHD       / 1.10GiB / Type3 (Data Audio Data Data))
        [Fact]
        public void Furazhr_VV_Wncna_Qvfp_7_chd_ConvertCue()
        {
            string fileName = @"Furazhr VV (Wncna) (Qvfp 7).chd";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Dreamcast"));
            string outFolderName = $"Dreamcast_Furazhr_VV_Wncna_Qvfp_7_chd_ConvertCue_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"cue", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.Dreamcast;

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
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7).chd", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", f.CleanName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", f.Name);
            Assert.Equal(SourceImageType.Chd, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7).chd", f.ImageFiles[0].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".chd", f.ImageFiles[0].Extension);
            Assert.Equal(0x93dd94L, f.ImageFiles[0].Size);
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
            Assert.False(i.CreateInChecksum);
            Assert.True(i.CreateOutChecksum);
            Assert.True(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.True(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Convert-GdRom-CueGdi", i.Name);
            Assert.Equal(OutputType.FolderIndex, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("gdromcue/gdromgdi", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("GdRomcue", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x0L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Sha1:C4AF8F30F108C406559D2E3728740BC4A8A56872", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(5, r.OutFileParts.Length);
            Assert.Equal(0x1d9280L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:28106B37", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 1).bin", r.OutFileParts[0].FileName);
            Assert.Equal(0x1c6350L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:DF4ACEE3", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 2).bin", r.OutFileParts[1].FileName);
            Assert.Equal(0x1baddc0L, r.OutFileParts[2].Size);
            Assert.NotNull(r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:3BC53385", r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[2].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 3).bin", r.OutFileParts[2].FileName);
            Assert.Equal(0x44f26860L, r.OutFileParts[3].Size);
            Assert.NotNull(r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FEE07BBD", r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[3].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 4).bin", r.OutFileParts[3].FileName);
            Assert.Equal(0x1f5L, r.OutFileParts[4].Size);
            Assert.NotNull(r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:7C6D76D3", r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[4].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7).cue", r.OutFileParts[4].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x963c346fU, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x4cfc43a0L, r.ResultSize.Value);
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
            Assert.Equal(ContainerType.Chd, t.ContainerType);
            Assert.Equal(SystemType.Dreamcast, t.System);
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x4cfc43a0L, t.Size);
            Assert.Equal(0x963c346fU, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "Furazhr VV (Wncna) (Qvfp 7)"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Furazhr VV (Wncna) (Qvfp 7).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(5, t.StepFiles.Count);
            Assert.Equal(0x1d9280L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:28106B37", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 1).bin", t.StepFiles[0].FileName);
            Assert.Equal(0x1c6350L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.True(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:DF4ACEE3", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 2).bin", t.StepFiles[1].FileName);
            Assert.Equal(0x1baddc0L, t.StepFiles[2].Size);
            Assert.False(t.StepFiles[2].IsIndex);
            Assert.True(t.StepFiles[2].IsImageName);
            Assert.NotNull(t.StepFiles[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:3BC53385", t.StepFiles[2].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[2].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 3).bin", t.StepFiles[2].FileName);
            Assert.Equal(0x44f26860L, t.StepFiles[3].Size);
            Assert.False(t.StepFiles[3].IsIndex);
            Assert.True(t.StepFiles[3].IsImageName);
            Assert.NotNull(t.StepFiles[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FEE07BBD", t.StepFiles[3].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[3].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7) (Track 4).bin", t.StepFiles[3].FileName);
            Assert.Equal(0x1f5L, t.StepFiles[4].Size);
            Assert.True(t.StepFiles[4].IsIndex);
            Assert.True(t.StepFiles[4].IsImageName);
            Assert.NotNull(t.StepFiles[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:7C6D76D3", t.StepFiles[4].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[4].FileName);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7).cue", t.StepFiles[4].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Furazhr VV (Wncna) (Qvfp 7)", t.Scan.Name);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.Equal(0x963c346fU, t.Scan.Crc);
            Assert.Equal(0x963c346fU, t.Scan.CrcDecrypted);
            Assert.Equal(0x4cfc43a0L, t.Scan.Size);
            Assert.Equal(3917, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(88, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Dreamcast", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("04CFC43A0", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("963C346F", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(4, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("974", t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
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
            Assert.Equal("3E96A34F", t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("DAA483E39E8F62C6", t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("00000064F", t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
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
            Assert.Equal(0xd23c5715U, t.Scan.Areas[0].Crc);
            Assert.Equal(0xd23c5715U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x22f4a0L, t.Scan.Areas[0].Size);
            Dictionary<FsType, int> t_ScanTypes0 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[0]);
            Assert.Equal(2, t_ScanTypes0.Count);
            Assert.Equal(2, t_ScanTypes0[FsType.System]);
            Assert.Equal(66, t_ScanTypes0[FsType.Iso9660]);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal("00:09:47.013", t.Scan.Areas[1].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal(AreaType.Audio, t.Scan.Areas[1].Type);
            Assert.Equal(0x6102f0c0U, t.Scan.Areas[1].Crc);
            Assert.Equal(0x6102f0c0U, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x62c08e0L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("12565", t.Scan.Areas[2].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("0064EFD80", t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("00007B156", t.Scan.Areas[2].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0xe08387bbU, t.Scan.Areas[2].Crc);
            Assert.Equal(0xe08387bbU, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x1c2f0f0L, t.Scan.Areas[2].Size);
            Dictionary<FsType, int> t_ScanTypes2 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[2]);
            Assert.Equal(2, t_ScanTypes2.Count);
            Assert.Equal(2, t_ScanTypes2[FsType.System]);
            Assert.Equal(3681, t_ScanTypes2[FsType.Iso9660]);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[3].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[3].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("491585", t.Scan.Areas[3].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[3].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[3].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("001C2F0F0", t.Scan.Areas[3].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("0064EFD80", t.Scan.Areas[3].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[3].Type);
            Assert.Equal(0xc08c9eddU, t.Scan.Areas[3].Crc);
            Assert.Equal(0xc08c9eddU, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x44ea5530L, t.Scan.Areas[3].Size);
            Dictionary<FsType, int> t_ScanTypes3 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[3]);
            Assert.Equal(2, t_ScanTypes3.Count);
            Assert.Equal(2, t_ScanTypes3[FsType.System]);
            Assert.Equal(3681, t_ScanTypes3[FsType.Iso9660]);

            base.Complete();
        }
    }
}