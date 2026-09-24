
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_WiiU_Tests : WipedImageTestsBase
    {
        //CAT-I      / WUX       / 23.4GiB / Decrypted SI partition, CommonDev key for game ticket
        [Fact]
        public void JvvH_PNG_V_wux_ConvertApp()
        {
            string fileName = @"JvvH_PNG-V.wux";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "WiiU"));
            string outFolderName = $"WiiU_JvvH_PNG_V_wux_ConvertApp_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"../../../../../WipedImages/_keys";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"app", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.WiiU;

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
            Assert.Equal("JvvH_PNG-V.wux", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("JvvH_PNG-V", f.CleanName);
            Assert.Equal("JvvH_PNG-V", f.Name);
            Assert.Equal(SourceImageType.Wux, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.WiiU, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("JvvH_PNG-V.wux", f.ImageFiles[0].FileName);
            Assert.Equal("JvvH_PNG-V", f.ImageFiles[0].NameOnly);
            Assert.Equal(".wux", f.ImageFiles[0].Extension);
            Assert.Equal(0x3f8000L, f.ImageFiles[0].Size);
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
            Assert.False(i.FullScan);
            Assert.False(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.True(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Convert-WiiU-AppTmd", i.Name);
            Assert.Equal(OutputType.FolderIndex, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("apptmd", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("apptmd", i.ImageConfig);
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
            Assert.Equal("JvvH_PNG-V [555055556CD37D55]", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(23, r.OutFileParts.Length);
            Assert.Equal(0x8000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:EB9679B7", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("00000000.app", r.OutFileParts[0].FileName);
            Assert.Equal(0x110000L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("00000001.app", r.OutFileParts[1].FileName);
            Assert.Equal(0x110000L, r.OutFileParts[2].Size);
            Assert.NotNull(r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[2].FileName);
            Assert.Equal("00000002.app", r.OutFileParts[2].FileName);
            Assert.Equal(0x110000L, r.OutFileParts[3].Size);
            Assert.NotNull(r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[3].FileName);
            Assert.Equal("00000003.app", r.OutFileParts[3].FileName);
            Assert.Equal(0xb30000L, r.OutFileParts[4].Size);
            Assert.NotNull(r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D0990ECE", r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[4].FileName);
            Assert.Equal("00000004.app", r.OutFileParts[4].FileName);
            Assert.Equal(0x110000L, r.OutFileParts[5].Size);
            Assert.NotNull(r.OutFileParts[5].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", r.OutFileParts[5].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[5].FileName);
            Assert.Equal("00000005.app", r.OutFileParts[5].FileName);
            Assert.Equal(0x110000L, r.OutFileParts[6].Size);
            Assert.NotNull(r.OutFileParts[6].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", r.OutFileParts[6].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[6].FileName);
            Assert.Equal("00000006.app", r.OutFileParts[6].FileName);
            Assert.Equal(0x2090000L, r.OutFileParts[7].Size);
            Assert.NotNull(r.OutFileParts[7].Checksums.ToString(true, true));
            Assert.Equal("Crc32:715ECC8B", r.OutFileParts[7].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[7].FileName);
            Assert.Equal("00000007.app", r.OutFileParts[7].FileName);
            Assert.Equal(0x18000L, r.OutFileParts[8].Size);
            Assert.NotNull(r.OutFileParts[8].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D9900D16", r.OutFileParts[8].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[8].FileName);
            Assert.Equal("00000008.app", r.OutFileParts[8].FileName);
            Assert.Equal(0x7050000L, r.OutFileParts[9].Size);
            Assert.NotNull(r.OutFileParts[9].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2E2DE1B4", r.OutFileParts[9].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[9].FileName);
            Assert.Equal("00000009.app", r.OutFileParts[9].FileName);
            Assert.Equal(0x1fb60000L, r.OutFileParts[10].Size);
            Assert.NotNull(r.OutFileParts[10].Checksums.ToString(true, true));
            Assert.Equal("Crc32:499DD9B3", r.OutFileParts[10].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[10].FileName);
            Assert.Equal("0000000a.app", r.OutFileParts[10].FileName);
            Assert.Equal(0xd00L, r.OutFileParts[11].Size);
            Assert.NotNull(r.OutFileParts[11].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CD9FA207", r.OutFileParts[11].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[11].FileName);
            Assert.Equal("title.cert", r.OutFileParts[11].FileName);
            Assert.Equal(0x350L, r.OutFileParts[12].Size);
            Assert.NotNull(r.OutFileParts[12].Checksums.ToString(true, true));
            Assert.Equal("Crc32:EA8D94BC", r.OutFileParts[12].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[12].FileName);
            Assert.Equal("title.tik", r.OutFileParts[12].FileName);
            Assert.Equal(0xd14L, r.OutFileParts[13].Size);
            Assert.NotNull(r.OutFileParts[13].Checksums.ToString(true, true));
            Assert.Equal("Crc32:8571565A", r.OutFileParts[13].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[13].FileName);
            Assert.Equal("title.tmd", r.OutFileParts[13].FileName);
            Assert.Equal(0x14L, r.OutFileParts[14].Size);
            Assert.NotNull(r.OutFileParts[14].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", r.OutFileParts[14].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[14].FileName);
            Assert.Equal("00000001.h3", r.OutFileParts[14].FileName);
            Assert.Equal(0x14L, r.OutFileParts[15].Size);
            Assert.NotNull(r.OutFileParts[15].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", r.OutFileParts[15].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[15].FileName);
            Assert.Equal("00000002.h3", r.OutFileParts[15].FileName);
            Assert.Equal(0x14L, r.OutFileParts[16].Size);
            Assert.NotNull(r.OutFileParts[16].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", r.OutFileParts[16].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[16].FileName);
            Assert.Equal("00000003.h3", r.OutFileParts[16].FileName);
            Assert.Equal(0x14L, r.OutFileParts[17].Size);
            Assert.NotNull(r.OutFileParts[17].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C890A483", r.OutFileParts[17].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[17].FileName);
            Assert.Equal("00000004.h3", r.OutFileParts[17].FileName);
            Assert.Equal(0x14L, r.OutFileParts[18].Size);
            Assert.NotNull(r.OutFileParts[18].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", r.OutFileParts[18].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[18].FileName);
            Assert.Equal("00000005.h3", r.OutFileParts[18].FileName);
            Assert.Equal(0x14L, r.OutFileParts[19].Size);
            Assert.NotNull(r.OutFileParts[19].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", r.OutFileParts[19].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[19].FileName);
            Assert.Equal("00000006.h3", r.OutFileParts[19].FileName);
            Assert.Equal(0x14L, r.OutFileParts[20].Size);
            Assert.NotNull(r.OutFileParts[20].Checksums.ToString(true, true));
            Assert.Equal("Crc32:19789F45", r.OutFileParts[20].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[20].FileName);
            Assert.Equal("00000007.h3", r.OutFileParts[20].FileName);
            Assert.Equal(0x14L, r.OutFileParts[21].Size);
            Assert.NotNull(r.OutFileParts[21].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D556CD08", r.OutFileParts[21].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[21].FileName);
            Assert.Equal("00000009.h3", r.OutFileParts[21].FileName);
            Assert.Equal(0x28L, r.OutFileParts[22].Size);
            Assert.NotNull(r.OutFileParts[22].Checksums.ToString(true, true));
            Assert.Equal("Crc32:1067EADC", r.OutFileParts[22].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[22].FileName);
            Assert.Equal("0000000a.h3", r.OutFileParts[22].FileName);
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
            Assert.Equal(ContainerType.Wux, t.ContainerType);
            Assert.Equal(SystemType.WiiU, t.System);
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "JvvH_PNG-V [555055556CD37D55]"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("JvvH_PNG-V", t.Name);
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
            Assert.Equal(23, t.StepFiles.Count);
            Assert.Equal(0x8000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.False(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:EB9679B7", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("00000000.app", t.StepFiles[0].FileName);
            Assert.Equal(0x110000L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.False(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("00000001.app", t.StepFiles[1].FileName);
            Assert.Equal(0x110000L, t.StepFiles[2].Size);
            Assert.False(t.StepFiles[2].IsIndex);
            Assert.False(t.StepFiles[2].IsImageName);
            Assert.NotNull(t.StepFiles[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", t.StepFiles[2].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[2].FileName);
            Assert.Equal("00000002.app", t.StepFiles[2].FileName);
            Assert.Equal(0x110000L, t.StepFiles[3].Size);
            Assert.False(t.StepFiles[3].IsIndex);
            Assert.False(t.StepFiles[3].IsImageName);
            Assert.NotNull(t.StepFiles[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", t.StepFiles[3].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[3].FileName);
            Assert.Equal("00000003.app", t.StepFiles[3].FileName);
            Assert.Equal(0xb30000L, t.StepFiles[4].Size);
            Assert.False(t.StepFiles[4].IsIndex);
            Assert.False(t.StepFiles[4].IsImageName);
            Assert.NotNull(t.StepFiles[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D0990ECE", t.StepFiles[4].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[4].FileName);
            Assert.Equal("00000004.app", t.StepFiles[4].FileName);
            Assert.Equal(0x110000L, t.StepFiles[5].Size);
            Assert.False(t.StepFiles[5].IsIndex);
            Assert.False(t.StepFiles[5].IsImageName);
            Assert.NotNull(t.StepFiles[5].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", t.StepFiles[5].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[5].FileName);
            Assert.Equal("00000005.app", t.StepFiles[5].FileName);
            Assert.Equal(0x110000L, t.StepFiles[6].Size);
            Assert.False(t.StepFiles[6].IsIndex);
            Assert.False(t.StepFiles[6].IsImageName);
            Assert.NotNull(t.StepFiles[6].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2F443395", t.StepFiles[6].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[6].FileName);
            Assert.Equal("00000006.app", t.StepFiles[6].FileName);
            Assert.Equal(0x2090000L, t.StepFiles[7].Size);
            Assert.False(t.StepFiles[7].IsIndex);
            Assert.False(t.StepFiles[7].IsImageName);
            Assert.NotNull(t.StepFiles[7].Checksums.ToString(true, true));
            Assert.Equal("Crc32:715ECC8B", t.StepFiles[7].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[7].FileName);
            Assert.Equal("00000007.app", t.StepFiles[7].FileName);
            Assert.Equal(0x18000L, t.StepFiles[8].Size);
            Assert.False(t.StepFiles[8].IsIndex);
            Assert.False(t.StepFiles[8].IsImageName);
            Assert.NotNull(t.StepFiles[8].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D9900D16", t.StepFiles[8].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[8].FileName);
            Assert.Equal("00000008.app", t.StepFiles[8].FileName);
            Assert.Equal(0x7050000L, t.StepFiles[9].Size);
            Assert.False(t.StepFiles[9].IsIndex);
            Assert.False(t.StepFiles[9].IsImageName);
            Assert.NotNull(t.StepFiles[9].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2E2DE1B4", t.StepFiles[9].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[9].FileName);
            Assert.Equal("00000009.app", t.StepFiles[9].FileName);
            Assert.Equal(0x1fb60000L, t.StepFiles[10].Size);
            Assert.False(t.StepFiles[10].IsIndex);
            Assert.False(t.StepFiles[10].IsImageName);
            Assert.NotNull(t.StepFiles[10].Checksums.ToString(true, true));
            Assert.Equal("Crc32:499DD9B3", t.StepFiles[10].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[10].FileName);
            Assert.Equal("0000000a.app", t.StepFiles[10].FileName);
            Assert.Equal(0xd00L, t.StepFiles[11].Size);
            Assert.False(t.StepFiles[11].IsIndex);
            Assert.False(t.StepFiles[11].IsImageName);
            Assert.NotNull(t.StepFiles[11].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CD9FA207", t.StepFiles[11].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[11].FileName);
            Assert.Equal("title.cert", t.StepFiles[11].FileName);
            Assert.Equal(0x350L, t.StepFiles[12].Size);
            Assert.False(t.StepFiles[12].IsIndex);
            Assert.False(t.StepFiles[12].IsImageName);
            Assert.NotNull(t.StepFiles[12].Checksums.ToString(true, true));
            Assert.Equal("Crc32:EA8D94BC", t.StepFiles[12].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[12].FileName);
            Assert.Equal("title.tik", t.StepFiles[12].FileName);
            Assert.Equal(0xd14L, t.StepFiles[13].Size);
            Assert.True(t.StepFiles[13].IsIndex);
            Assert.False(t.StepFiles[13].IsImageName);
            Assert.NotNull(t.StepFiles[13].Checksums.ToString(true, true));
            Assert.Equal("Crc32:8571565A", t.StepFiles[13].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[13].FileName);
            Assert.Equal("title.tmd", t.StepFiles[13].FileName);
            Assert.Equal(0x14L, t.StepFiles[14].Size);
            Assert.False(t.StepFiles[14].IsIndex);
            Assert.False(t.StepFiles[14].IsImageName);
            Assert.NotNull(t.StepFiles[14].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", t.StepFiles[14].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[14].FileName);
            Assert.Equal("00000001.h3", t.StepFiles[14].FileName);
            Assert.Equal(0x14L, t.StepFiles[15].Size);
            Assert.False(t.StepFiles[15].IsIndex);
            Assert.False(t.StepFiles[15].IsImageName);
            Assert.NotNull(t.StepFiles[15].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", t.StepFiles[15].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[15].FileName);
            Assert.Equal("00000002.h3", t.StepFiles[15].FileName);
            Assert.Equal(0x14L, t.StepFiles[16].Size);
            Assert.False(t.StepFiles[16].IsIndex);
            Assert.False(t.StepFiles[16].IsImageName);
            Assert.NotNull(t.StepFiles[16].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", t.StepFiles[16].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[16].FileName);
            Assert.Equal("00000003.h3", t.StepFiles[16].FileName);
            Assert.Equal(0x14L, t.StepFiles[17].Size);
            Assert.False(t.StepFiles[17].IsIndex);
            Assert.False(t.StepFiles[17].IsImageName);
            Assert.NotNull(t.StepFiles[17].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C890A483", t.StepFiles[17].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[17].FileName);
            Assert.Equal("00000004.h3", t.StepFiles[17].FileName);
            Assert.Equal(0x14L, t.StepFiles[18].Size);
            Assert.False(t.StepFiles[18].IsIndex);
            Assert.False(t.StepFiles[18].IsImageName);
            Assert.NotNull(t.StepFiles[18].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", t.StepFiles[18].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[18].FileName);
            Assert.Equal("00000005.h3", t.StepFiles[18].FileName);
            Assert.Equal(0x14L, t.StepFiles[19].Size);
            Assert.False(t.StepFiles[19].IsIndex);
            Assert.False(t.StepFiles[19].IsImageName);
            Assert.NotNull(t.StepFiles[19].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0E869777", t.StepFiles[19].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[19].FileName);
            Assert.Equal("00000006.h3", t.StepFiles[19].FileName);
            Assert.Equal(0x14L, t.StepFiles[20].Size);
            Assert.False(t.StepFiles[20].IsIndex);
            Assert.False(t.StepFiles[20].IsImageName);
            Assert.NotNull(t.StepFiles[20].Checksums.ToString(true, true));
            Assert.Equal("Crc32:19789F45", t.StepFiles[20].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[20].FileName);
            Assert.Equal("00000007.h3", t.StepFiles[20].FileName);
            Assert.Equal(0x14L, t.StepFiles[21].Size);
            Assert.False(t.StepFiles[21].IsIndex);
            Assert.False(t.StepFiles[21].IsImageName);
            Assert.NotNull(t.StepFiles[21].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D556CD08", t.StepFiles[21].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[21].FileName);
            Assert.Equal("00000009.h3", t.StepFiles[21].FileName);
            Assert.Equal(0x28L, t.StepFiles[22].Size);
            Assert.False(t.StepFiles[22].IsIndex);
            Assert.False(t.StepFiles[22].IsImageName);
            Assert.NotNull(t.StepFiles[22].Checksums.ToString(true, true));
            Assert.Equal("Crc32:1067EADC", t.StepFiles[22].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[22].FileName);
            Assert.Equal("0000000a.h3", t.StepFiles[22].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            base.Complete();
        }
    }
}