
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //Retail     / RVZ       / 4.38GiB / Channel between Update and Data
        [Fact]
        public void Jvv_Svg_Cyhf_Xbern_rvz_ConvertCison()
        {
            string fileName = @"Jvv Svg Cyhf (Xbern).rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_Jvv_Svg_Cyhf_Xbern_rvz_ConvertCison_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Convert", @"ciso:n", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("Jvv Svg Cyhf (Xbern).rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Jvv Svg Cyhf (Xbern)", f.CleanName);
            Assert.Equal("Jvv Svg Cyhf (Xbern)", f.Name);
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
            Assert.Equal("Jvv Svg Cyhf (Xbern).rvz", f.ImageFiles[0].FileName);
            Assert.Equal("Jvv Svg Cyhf (Xbern)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x9e4fb8L, f.ImageFiles[0].Size);
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
            Assert.Equal("ciso", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x118240000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:2AD2950F, Md5:E415A0C0E577BD892B020545C5C20079, Sha1:35233958F9D5A0E82E347056333F1A108A51BCA7, XxHash:95944C948B4332B1", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Jvv Svg Cyhf (Xbern).ciso", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x2fa08000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0DD71DAB", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Jvv Svg Cyhf (Xbern).ciso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x2ad2950fU, r.ResultCrc.Value);
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
            Assert.Equal(0x2ad2950fU, t.CRC);
            Assert.Equal(0xcd0cef5eU, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Jvv Svg Cyhf (Xbern)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Jvv Svg Cyhf (Xbern).ciso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Jvv Svg Cyhf (Xbern).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x2fa08000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:0DD71DAB", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Jvv Svg Cyhf (Xbern).ciso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Jvv Svg Cyhf (Xbern)", t.Scan.Name);
            Assert.Equal(SystemType.Wii, f.SystemType);
            Assert.Equal(0x2ad2950fU, t.Scan.Crc);
            Assert.Equal(0xcd0cef5eU, t.Scan.CrcDecrypted);
            Assert.Equal(0x118240000L, t.Scan.Size);
            Assert.Equal(1191, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(834, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Wii", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("Retail", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("118240000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("2AD2950F", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("CD0CEF5E", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(9, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("ESCX56", t.Scan.Areas[0].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("Korea", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("JvvSvgCyhf(XE:C)", t.Scan.Areas[0].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[0].AreaInfo.Properties["Partitions"].ToXmlValue());
            Assert.Equal(AreaType.ImageHeader, t.Scan.Areas[0].Type);
            Assert.Equal(0xece6b7a3U, t.Scan.Areas[0].Crc);
            Assert.Equal(0xece6b7a3U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x50000L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Update", t.Scan.Areas[1].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("D1428D4DDBB25A56CEFB251EA92D36235B7FADC7", t.Scan.Areas[1].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[1].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[1].Type);
            Assert.Equal(0x189df758U, t.Scan.Areas[1].Crc);
            Assert.Equal(0x189df758U, t.Scan.Areas[1].CrcDecrypted);
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
            Assert.Equal("43F9EF7A", t.Scan.Areas[2].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("ERYF", t.Scan.Areas[2].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("00003E748", t.Scan.Areas[2].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("006058000", t.Scan.Areas[2].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0xab385d95U, t.Scan.Areas[2].Crc);
            Assert.Equal(0x8417b04dU, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x6378000L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[3].Type);
            Assert.Equal(0x26b236a6U, t.Scan.Areas[3].Crc);
            Assert.Equal(0x26b236a6U, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x9418000L, t.Scan.Areas[3].Size);
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[4].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Channel", t.Scan.Areas[4].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("4BF28EE428CF38F80FB6A8E1E422493D4CD7C679", t.Scan.Areas[4].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[4].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[4].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[4].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[4].Type);
            Assert.Equal(0xbf18c71fU, t.Scan.Areas[4].Crc);
            Assert.Equal(0xbf18c71fU, t.Scan.Areas[4].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[4].Size);
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[5].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("_VAFMM", t.Scan.Areas[5].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[5].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[5].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("Vafgnyyre Cnegvgvba", t.Scan.Areas[5].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[5].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[5].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[5].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("700C36ED", t.Scan.Areas[5].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("_VAF", t.Scan.Areas[5].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("0001979C0", t.Scan.Areas[5].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("0017A8000", t.Scan.Areas[5].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[5].Type);
            Assert.Equal(0xe897448dU, t.Scan.Areas[5].Crc);
            Assert.Equal(0xd1675456U, t.Scan.Areas[5].CrcDecrypted);
            Assert.Equal(0x1870000L, t.Scan.Areas[5].Size);
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[6].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.Equal("Game", t.Scan.Areas[6].AreaInfo.Properties["PartitionType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.Equal("5888BAF2DE3F157702280E1C9AE3BE69380E54F0", t.Scan.Areas[6].AreaInfo.Properties["ContentSha"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[6].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[6].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[6].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal(AreaType.PartitionHeader, t.Scan.Areas[6].Type);
            Assert.Equal(0xdc75fd10U, t.Scan.Areas[6].Crc);
            Assert.Equal(0xdc75fd10U, t.Scan.Areas[6].CrcDecrypted);
            Assert.Equal(0x20000L, t.Scan.Areas[6].Size);
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[7].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.Equal("ESCX56", t.Scan.Areas[7].AreaInfo.Properties["ID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[7].AreaInfo.Properties["DiscNo"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[7].AreaInfo.Properties["Revision"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.Equal("JvvSvgCyhf(XE:C)", t.Scan.Areas[7].AreaInfo.Properties["Title"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[7].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[7].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[7].AreaInfo.Properties["HasFileSystem"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.Equal("B82B0F6D", t.Scan.Areas[7].AreaInfo.Properties["SystemDataCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("ESCX", t.Scan.Areas[7].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.Equal("0007B1814", t.Scan.Areas[7].AreaInfo.Properties["JunkLeadingNulls"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal("0FE020000", t.Scan.Areas[7].AreaInfo.Properties["JunkEndNullsOffset"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[7].Type);
            Assert.Equal(0x3deb0533U, t.Scan.Areas[7].Crc);
            Assert.Equal(0x8eeead4eU, t.Scan.Areas[7].CrcDecrypted);
            Assert.Equal(0x106340000L, t.Scan.Areas[7].Size);
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[8].AreaInfo.Properties["UpdatePartitionRemoved"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[8].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal("ESCX", t.Scan.Areas[8].AreaInfo.Properties["JunkID"].ToXmlValue());
            Assert.Equal(AreaType.Other, t.Scan.Areas[8].Type);
            Assert.Equal(0x118ddafcU, t.Scan.Areas[8].Crc);
            Assert.Equal(0x118ddafcU, t.Scan.Areas[8].CrcDecrypted);
            Assert.Equal(0xe50000L, t.Scan.Areas[8].Size);

            base.Complete();
        }
    }
}