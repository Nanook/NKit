
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //RVT-H      / gcm (7z)  / 4.38GiB / RVT-H Manually Scrubbed
        [Fact]
        public void EIY_QVNT_Ire9_9_655W56_WCA_EIG_U_VBF9_7z_ConvertRvz()
        {
            string fileName = @"EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_EIY_QVNT_Ire9_9_655W56_WCA_EIG_U_VBF9_7z_ConvertRvz_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"rvz", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).7z//EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).iso", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", f.CleanName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", f.Name);
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
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).iso", f.ImageFiles[0].FileName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".iso", f.ImageFiles[0].Extension);
            Assert.Equal(0x118940000L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x4e954eL, f.ArchiveFiles[0].Size);
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
            Assert.Equal("Convert-WiiGc-Lossless", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.True(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("rvz[nkit]/wbfs[nkit]/ciso[nkit]", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("rvz[nkit]", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x118940000L, i.SrcParts[0].Size);
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
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).rvz", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x118940000L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:78020AFD, Md5:C8A8A5B12B0A229E2CC88F2523DAB86F, Sha1:B4CBD5A0653539BAD3435F0FFF6D55EC3D6DBE6F, XxHash:42B864CD2D459E42", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x10d5e0L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:1FA1ADA5", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).rvz~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x78020afdU, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x118940000L, r.ResultSize.Value);
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
            Assert.Equal(SystemType.Wii, t.System);
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x118940000L, t.Size);
            Assert.Equal(0x78020afdU, t.CRC);
            Assert.Equal(0x78020afdU, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).rvz", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x10d5e0L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:1FA1ADA5", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9).rvz~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("EIY_QVNT_Ire9_9 (655W56) (WCA) EIG-U (VBF9)", t.Scan.Name);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.Equal(0x78020afdU, t.Scan.Crc);
            Assert.Equal(0x78020afdU, t.Scan.CrcDecrypted);
            Assert.Equal(0x118940000L, t.Scan.Size);
            Assert.Equal(1354, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(783, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Wii", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("RVT-H", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("118940000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("78020AFD", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("78020AFD", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(5, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("655W56", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Japan", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("EIY_QVNT_Ire9_9", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal(AreaType.ImageHeader, t.Scan.Areas[0].Type);
            Assert.Equal(0x84ead9f4U, t.Scan.Areas[0].Crc);
            Assert.Equal(0x84ead9f4U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x50000L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Update", t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("DCECD53A6425C869B24A7B77B0E085A653560CE2", t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[1].Type);
            Assert.Equal(0x5611b18aU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x5611b18aU, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x8000L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("[0x0][0x0][0x0][0x0][0x0][0x0]", t.Scan.Areas[2].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("4", t.Scan.Areas[2].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("false", t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000000", t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("false", t.Scan.Areas[2].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0xf55e8da7U, t.Scan.Areas[2].Crc);
            Assert.Equal(0xf55e8da7U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x1e0000L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Game", t.Scan.Areas[3].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("89C13D7C20FCB366CFC09326BBEEAB26A62A809A", t.Scan.Areas[3].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[3].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[3].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[3].Type);
            Assert.Equal(0xe284a64eU, t.Scan.Areas[3].Crc);
            Assert.Equal(0xe284a64eU, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x8000L, t.Scan.Areas[3].Size);
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("655W56", t.Scan.Areas[4].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[4].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[4].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("EIY_QVNT_Ire9_9", t.Scan.Areas[4].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("false", t.Scan.Areas[4].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[4].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000000", t.Scan.Areas[4].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[4].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("9E8C5174", t.Scan.Areas[4].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("655W", t.Scan.Areas[4].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00007EAE8", t.Scan.Areas[4].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("118700000", t.Scan.Areas[4].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[4].Type);
            Assert.Equal(0x056ebaa4U, t.Scan.Areas[4].Crc);
            Assert.Equal(0x056ebaa4U, t.Scan.Areas[4].CrcDecrypted);
            Assert.Equal(0x118700000L, t.Scan.Areas[4].Size);

            base.Complete();
        }
    }
}