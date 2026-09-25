
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //Retail     / RVZ       / 4.38GiB / Win Partition, Disc 2
        [Fact]
        public void Qentba_Dhrfg_K_Nyy_va_Bar_Cnpxntr_Wncna_Qvfp_7_rvz_ConvertWbfsy()
        {
            string fileName = @"Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_Qentba_Dhrfg_K_Nyy_va_Bar_Cnpxntr_Wncna_Qvfp_7_rvz_ConvertWbfsy_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"wbfs:y", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7)", f.CleanName);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7)", f.Name);
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
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).rvz", f.ImageFiles[0].FileName);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x26b8fcL, f.ImageFiles[0].Size);
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
            Assert.Equal("wbfs[nkit]", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x118240000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:DF3D801E, Md5:A8B340208AABB5B099E3221A2F754868, Sha1:842681425D6D9CB382B81C6AB22E08174FB79C6A, XxHash:26C009A49025113A", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).wbfs", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x118240000L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:DF3D801E, Md5:A8B340208AABB5B099E3221A2F754868, Sha1:842681425D6D9CB382B81C6AB22E08174FB79C6A, XxHash:26C009A49025113A", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0xdb800000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E54EBA7A", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).wbfs~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xdf3d801eU, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x118240000L, r.ResultSize.Value);
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
            Assert.Equal(SystemType.Wii, t.System);
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x118240000L, t.Size);
            Assert.Equal(0xdf3d801eU, t.CRC);
            Assert.Equal(0x78228ac7U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).wbfs", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0xdb800000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E54EBA7A", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7).wbfs~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Qentba Dhrfg K - Nyy va Bar Cnpxntr (Wncna) (Qvfp 7)", t.Scan.Name);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.Equal(0xdf3d801eU, t.Scan.Crc);
            Assert.Equal(0x78228ac7U, t.Scan.CrcDecrypted);
            Assert.Equal(0x118240000L, t.Scan.Size);
            Assert.Equal(360, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(57, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Wii", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("Retail", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("118240000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("DF3D801E", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("78228AC7", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(9, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("F1GWTQ", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Japan", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("QentbaDhrfg K 6&7 NyyVaBar", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal(AreaType.ImageHeader, t.Scan.Areas[0].Type);
            Assert.Equal(0xd1125f44U, t.Scan.Areas[0].Crc);
            Assert.Equal(0xd1125f44U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x50000L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Update", t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("6E61C0C6426BE91AD47EEA3DB391FA75D59B3EF6", t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[1].Type);
            Assert.Equal(0x37733d1cU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x37733d1cU, t.Scan.Areas[1].CrcDecrypted);
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
            Assert.Equal("8A728763", t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("ERYF", t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00003E6B8", t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("00AFF0000", t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0xc3c0b92fU, t.Scan.Areas[2].Crc);
            Assert.Equal(0x3e361e15U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0xb5a0000L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[3].Type);
            Assert.Equal(0x7d4128c4U, t.Scan.Areas[3].Crc);
            Assert.Equal(0x7d4128c4U, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x41f0000L, t.Scan.Areas[3].Size);
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("GameData", t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("C9976CB8D5BB073B83D75665ACCADFE00A88AF23", t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[4].Type);
            Assert.Equal(0x3bc93d38U, t.Scan.Areas[4].Crc);
            Assert.Equal(0x3bc93d38U, t.Scan.Areas[4].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[4].Size);
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("_JVAMM", t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("JSFVafgnyyre Cnegvgvba", t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("3CD63854", t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("_JVA", t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("000172B1C", t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("0C8E50000", t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[5].Type);
            Assert.Equal(0x3d7adca6U, t.Scan.Areas[5].Crc);
            Assert.Equal(0xfb9831f5U, t.Scan.Areas[5].CrcDecrypted);
            Assert.Equal(0xcf600000L, t.Scan.Areas[5].Size);
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Game", t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("215DD9561AA20BB4E5D0D631110F95EF70A5F08C", t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[6].Type);
            Assert.Equal(0x094e1d1eU, t.Scan.Areas[6].Crc);
            Assert.Equal(0x094e1d1eU, t.Scan.Areas[6].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[6].Size);
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("F1GWTQ", t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("rknzcyr", t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("28D678D4", t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("F1GW", t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("000172AC8", t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("000768000", t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[7].Type);
            Assert.Equal(0xa80c1cb4U, t.Scan.Areas[7].Crc);
            Assert.Equal(0x5672b4bdU, t.Scan.Areas[7].CrcDecrypted);
            Assert.Equal(0x7a8000L, t.Scan.Areas[7].Size);
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("F1GW", t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[8].Type);
            Assert.Equal(0xa5d9da35U, t.Scan.Areas[8].Crc);
            Assert.Equal(0xa5d9da35U, t.Scan.Areas[8].CrcDecrypted);
            Assert.Equal(0x38c58000L, t.Scan.Areas[8].Size);

            base.Complete();
        }
    }
}