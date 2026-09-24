
using Nanook.NKit;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //Retail     / RVZ       / 4.38GiB / Channel after game partition
        [Fact]
        public void Znevb_Xneg_Jvv_HFN_Ra_Se_Rf_rvz_Expand()
        {
            string fileName = @"Znevb Xneg Jvv (HFN) (Ra,Se,Rf).rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_Znevb_Xneg_Jvv_HFN_Ra_Se_Rf_rvz_Expand_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Expand", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            //presets.System = SystemType.Wii;

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
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", f.CleanName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", f.Name);
            Assert.Equal(SourceImageType.Rvz, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).rvz", f.ImageFiles[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x4952fcL, f.ImageFiles[0].Size);
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
            Assert.Equal(0x118240000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C08A2E20, Md5:8389506087DA2B12BFCE9A0477DB6833, Sha1:DF3A0ADB31B118182782482FC662579D0686D246, XxHash:DCCEB1FDE2A05C7E", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso", r.FinalName);
            Assert.Equal("XxHash|Crc32", string.Join('|', r.ChkCompared.Select(a => a.ToString())));
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x118240000L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C08A2E20, Md5:8389506087DA2B12BFCE9A0477DB6833, Sha1:DF3A0ADB31B118182782482FC662579D0686D246, XxHash:DCCEB1FDE2A05C7E", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x118240000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C08A2E20", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xc08a2e20U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x118240000L, r.ResultSize.Value);
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
            Assert.Equal(SystemType.Wii, t.System);
            Assert.Equal(TaskType.Expand, t.Task);
            Assert.Equal(0x118240000L, t.Size);
            Assert.Equal(0xc08a2e20U, t.CRC);
            Assert.Equal(0xb70163e4U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.VerifySuccess, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("InChecksums [XxHash+Crc32]", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("DCCEB1FDE2A05C7E", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Znevb Xneg Jvv (HFN) (Ra,Se,Rf).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x118240000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C08A2E20", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf).iso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Znevb Xneg Jvv (HFN) (Ra,Se,Rf)", t.Scan.Name);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.Equal(0xc08a2e20U, t.Scan.Crc);
            Assert.Equal(0xb70163e4U, t.Scan.CrcDecrypted);
            Assert.Equal(0x118240000L, t.Scan.Size);
            Assert.Equal(4150, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(141, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Wii", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("Retail", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("118240000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("C08A2E20", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("B70163E4", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(9, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("EZPR56", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Usa", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("ZnevbXnegJvv", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal(AreaType.ImageHeader, t.Scan.Areas[0].Type);
            Assert.Equal(0x95322fceU, t.Scan.Areas[0].Crc);
            Assert.Equal(0x95322fceU, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x50000L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Update", t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("830F0E5C5185E0ECA3366E2797AD964E16EACB07", t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[1].Type);
            Assert.Equal(0x0b1d77bcU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x0b1d77bcU, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("ERYFNO", t.Scan.Areas[2].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("Fnzcyr Tnzr Anzr", t.Scan.Areas[2].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[2].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("09E59FD9", t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("ERYF", t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00003E398", t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("00A5E8000", t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0x1031effeU, t.Scan.Areas[2].Crc);
            Assert.Equal(0xe936a7e5U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0xab48000L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[3].Type);
            Assert.Equal(0x36f38d3aU, t.Scan.Areas[3].Crc);
            Assert.Equal(0x36f38d3aU, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x4c48000L, t.Scan.Areas[3].Size);
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Game", t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("01FBAFC09720308B39A73467ABCA9CEC89B51DBD", t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[4].Type);
            Assert.Equal(0xf64d6becU, t.Scan.Areas[4].Crc);
            Assert.Equal(0xf64d6becU, t.Scan.Areas[4].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[4].Size);
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("EZPR56", t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("ZnevbXnegJvv", t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("E5F09DC5", t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("EZPR", t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("0002EC968", t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("0FDBA8000", t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[5].Type);
            Assert.Equal(0x035e59f3U, t.Scan.Areas[5].Crc);
            Assert.Equal(0x95b860efU, t.Scan.Areas[5].CrcDecrypted);
            Assert.Equal(0x105ea0000L, t.Scan.Areas[5].Size);
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Channel", t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("94484BC92AAAC50C27D7CEFAC5184E2CC108161D", t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[6].Type);
            Assert.Equal(0xf3b2fd78U, t.Scan.Areas[6].Crc);
            Assert.Equal(0xf3b2fd78U, t.Scan.Areas[6].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[6].Size);
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("_VAFMM", t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("Vafgnyyre Cnegvgvba", t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("76F86827", t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("_VAF", t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00019575C", t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("001C20000", t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[7].Type);
            Assert.Equal(0x59d6677cU, t.Scan.Areas[7].Crc);
            Assert.Equal(0x22d253edU, t.Scan.Areas[7].CrcDecrypted);
            Assert.Equal(0x1d10000L, t.Scan.Areas[7].Size);
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("EZPR", t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[8].Type);
            Assert.Equal(0x5160d588U, t.Scan.Areas[8].Crc);
            Assert.Equal(0x5160d588U, t.Scan.Areas[8].CrcDecrypted);
            Assert.Equal(0xe50000L, t.Scan.Areas[8].Size);

            base.Complete();
        }
    }
}