
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Wii_Tests : WipedImageTestsBase
    {
        //Retail     / RVZ       / 8GiB    / Retail with Virtual Console partitions
        [Fact]
        public void Fhcre_Fznfu_Oebf_Oenjy_Rhebcr_Ra_Se_Qr_Rf_Vg_rvz_FixExtract()
        {
            string fileName = @"Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg).rvz";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Wii"));
            string outFolderName = $"Wii_Fhcre_Fznfu_Oebf_Oenjy_Rhebcr_Ra_Se_Qr_Rf_Vg_rvz_FixExtract_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("FixExtract", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg).rvz", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.CleanName);
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.Name);
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
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg).rvz", f.ImageFiles[0].FileName);
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".rvz", f.ImageFiles[0].Extension);
            Assert.Equal(0x2bbad0L, f.ImageFiles[0].Size);
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
            Assert.False(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.False(i.FullScan);
            Assert.False(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("FixExtract-WiiGc", i.Name);
            Assert.Equal(OutputType.Files, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.FixExtract, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("files", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x1fb4e0000L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:61284D76, Md5:17F2192BB27DDE87CDB3F3D60C5696CF, Sha1:D455AFBFAE33426F0DFB70ED8A271BCFBE42DB6A, XxHash:ADE77CDF70F0A9CD", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(14, r.OutFileParts.Length);
            Assert.Equal(0xae58000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:3AA21E52", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("CD0411364EDB14768FDD6FB03A4C6AC9914CC7EA_N_5C918C8D~", r.OutFileParts[0].FileName);
            Assert.Equal(0xaa0000L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:BCFAF55E", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("EFOC560000_01_UN3C_N_BCFAF55E~", r.OutFileParts[1].FileName);
            Assert.Equal(0xab0000L, r.OutFileParts[2].Size);
            Assert.NotNull(r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:04DD2742", r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[2].FileName);
            Assert.Equal("EFOC560000_02_UN4C_N_04DD2742~", r.OutFileParts[2].FileName);
            Assert.Equal(0xac8000L, r.OutFileParts[3].Size);
            Assert.NotNull(r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:4AD95C9F", r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[3].FileName);
            Assert.Equal("EFOC560000_03_UONC_N_84CF2D96~", r.OutFileParts[3].FileName);
            Assert.Equal(0xb70000L, r.OutFileParts[4].Size);
            Assert.NotNull(r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CCB6F869", r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[4].FileName);
            Assert.Equal("EFOC560000_04_UOOS_N_CCB6F869~", r.OutFileParts[4].FileName);
            Assert.Equal(0xb70000L, r.OutFileParts[5].Size);
            Assert.NotNull(r.OutFileParts[5].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E07DFF7A", r.OutFileParts[5].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[5].FileName);
            Assert.Equal("EFOC560000_05_UOOC_N_E07DFF7A~", r.OutFileParts[5].FileName);
            Assert.Equal(0xac8000L, r.OutFileParts[6].Size);
            Assert.NotNull(r.OutFileParts[6].Checksums.ToString(true, true));
            Assert.Equal("Crc32:B38D79EB", r.OutFileParts[6].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[6].FileName);
            Assert.Equal("EFOC560000_06_UOPC_N_A3F05B91~", r.OutFileParts[6].FileName);
            Assert.Equal(0xaa8000L, r.OutFileParts[7].Size);
            Assert.NotNull(r.OutFileParts[7].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C43739BC", r.OutFileParts[7].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[7].FileName);
            Assert.Equal("EFOC560000_07_UOQC_N_3908E1F1~", r.OutFileParts[7].FileName);
            Assert.Equal(0xaf0000L, r.OutFileParts[8].Size);
            Assert.NotNull(r.OutFileParts[8].Checksums.ToString(true, true));
            Assert.Equal("Crc32:97967C1A", r.OutFileParts[8].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[8].FileName);
            Assert.Equal("EFOC560000_08_UORC_N_97967C1A~", r.OutFileParts[8].FileName);
            Assert.Equal(0xbe8000L, r.OutFileParts[9].Size);
            Assert.NotNull(r.OutFileParts[9].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C7BBF7B4", r.OutFileParts[9].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[9].FileName);
            Assert.Equal("EFOC560000_09_UOSC_N_CB5EB6F2~", r.OutFileParts[9].FileName);
            Assert.Equal(0xbe0000L, r.OutFileParts[10].Size);
            Assert.NotNull(r.OutFileParts[10].Checksums.ToString(true, true));
            Assert.Equal("Crc32:098BD6A3", r.OutFileParts[10].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[10].FileName);
            Assert.Equal("EFOC560000_10_UOTC_N_098BD6A3~", r.OutFileParts[10].FileName);
            Assert.Equal(0x1000000L, r.OutFileParts[11].Size);
            Assert.NotNull(r.OutFileParts[11].Checksums.ToString(true, true));
            Assert.Equal("Crc32:10E2064C", r.OutFileParts[11].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[11].FileName);
            Assert.Equal("EFOC560000_11_UOVC_N_10E2064C~", r.OutFileParts[11].FileName);
            Assert.Equal(0x38a8000L, r.OutFileParts[12].Size);
            Assert.NotNull(r.OutFileParts[12].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E0244EA7", r.OutFileParts[12].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[12].FileName);
            Assert.Equal("EFOC560000_12_UOXC_N_2ECD169E~", r.OutFileParts[12].FileName);
            Assert.Equal(0x2420000L, r.OutFileParts[13].Size);
            Assert.NotNull(r.OutFileParts[13].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E2C73901", r.OutFileParts[13].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[13].FileName);
            Assert.Equal("EFOC560000_13_UOYC_N_0818AB99~", r.OutFileParts[13].FileName);
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
            Assert.Equal(ContainerType.Rvz, t.ContainerType);
            Assert.Equal(SystemType.Wii, t.System);
            Assert.Equal(TaskType.FixExtract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Fhcre Fznfu Oebf. Oenjy (Rhebcr) (Ra,Se,Qr,Rf,Vg)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("NoVerify", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("CD0411364EDB14768FDD6FB03A4C6AC9914CC7EA_N_5C918C8D|EFOC560000_01_UN3C_N_BCFAF55E|EFOC560000_02_UN4C_N_04DD2742|EFOC560000_03_UONC_N_84CF2D96|EFOC560000_04_UOOS_N_CCB6F869|EFOC560000_05_UOOC_N_E07DFF7A|EFOC560000_06_UOPC_N_A3F05B91|EFOC560000_07_UOQC_N_3908E1F1|EFOC560000_08_UORC_N_97967C1A|EFOC560000_09_UOSC_N_CB5EB6F2|EFOC560000_10_UOTC_N_098BD6A3|EFOC560000_11_UOVC_N_10E2064C|EFOC560000_12_UOXC_N_2ECD169E|EFOC560000_13_UOYC_N_0818AB99", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.Null(t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(14, t.StepFiles.Count);
            Assert.Equal(0xae58000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.False(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:3AA21E52", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("CD0411364EDB14768FDD6FB03A4C6AC9914CC7EA_N_5C918C8D~", t.StepFiles[0].FileName);
            Assert.Equal(0xaa0000L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.False(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:BCFAF55E", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("EFOC560000_01_UN3C_N_BCFAF55E~", t.StepFiles[1].FileName);
            Assert.Equal(0xab0000L, t.StepFiles[2].Size);
            Assert.False(t.StepFiles[2].IsIndex);
            Assert.False(t.StepFiles[2].IsImageName);
            Assert.NotNull(t.StepFiles[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:04DD2742", t.StepFiles[2].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[2].FileName);
            Assert.Equal("EFOC560000_02_UN4C_N_04DD2742~", t.StepFiles[2].FileName);
            Assert.Equal(0xac8000L, t.StepFiles[3].Size);
            Assert.False(t.StepFiles[3].IsIndex);
            Assert.False(t.StepFiles[3].IsImageName);
            Assert.NotNull(t.StepFiles[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:4AD95C9F", t.StepFiles[3].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[3].FileName);
            Assert.Equal("EFOC560000_03_UONC_N_84CF2D96~", t.StepFiles[3].FileName);
            Assert.Equal(0xb70000L, t.StepFiles[4].Size);
            Assert.False(t.StepFiles[4].IsIndex);
            Assert.False(t.StepFiles[4].IsImageName);
            Assert.NotNull(t.StepFiles[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CCB6F869", t.StepFiles[4].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[4].FileName);
            Assert.Equal("EFOC560000_04_UOOS_N_CCB6F869~", t.StepFiles[4].FileName);
            Assert.Equal(0xb70000L, t.StepFiles[5].Size);
            Assert.False(t.StepFiles[5].IsIndex);
            Assert.False(t.StepFiles[5].IsImageName);
            Assert.NotNull(t.StepFiles[5].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E07DFF7A", t.StepFiles[5].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[5].FileName);
            Assert.Equal("EFOC560000_05_UOOC_N_E07DFF7A~", t.StepFiles[5].FileName);
            Assert.Equal(0xac8000L, t.StepFiles[6].Size);
            Assert.False(t.StepFiles[6].IsIndex);
            Assert.False(t.StepFiles[6].IsImageName);
            Assert.NotNull(t.StepFiles[6].Checksums.ToString(true, true));
            Assert.Equal("Crc32:B38D79EB", t.StepFiles[6].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[6].FileName);
            Assert.Equal("EFOC560000_06_UOPC_N_A3F05B91~", t.StepFiles[6].FileName);
            Assert.Equal(0xaa8000L, t.StepFiles[7].Size);
            Assert.False(t.StepFiles[7].IsIndex);
            Assert.False(t.StepFiles[7].IsImageName);
            Assert.NotNull(t.StepFiles[7].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C43739BC", t.StepFiles[7].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[7].FileName);
            Assert.Equal("EFOC560000_07_UOQC_N_3908E1F1~", t.StepFiles[7].FileName);
            Assert.Equal(0xaf0000L, t.StepFiles[8].Size);
            Assert.False(t.StepFiles[8].IsIndex);
            Assert.False(t.StepFiles[8].IsImageName);
            Assert.NotNull(t.StepFiles[8].Checksums.ToString(true, true));
            Assert.Equal("Crc32:97967C1A", t.StepFiles[8].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[8].FileName);
            Assert.Equal("EFOC560000_08_UORC_N_97967C1A~", t.StepFiles[8].FileName);
            Assert.Equal(0xbe8000L, t.StepFiles[9].Size);
            Assert.False(t.StepFiles[9].IsIndex);
            Assert.False(t.StepFiles[9].IsImageName);
            Assert.NotNull(t.StepFiles[9].Checksums.ToString(true, true));
            Assert.Equal("Crc32:C7BBF7B4", t.StepFiles[9].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[9].FileName);
            Assert.Equal("EFOC560000_09_UOSC_N_CB5EB6F2~", t.StepFiles[9].FileName);
            Assert.Equal(0xbe0000L, t.StepFiles[10].Size);
            Assert.False(t.StepFiles[10].IsIndex);
            Assert.False(t.StepFiles[10].IsImageName);
            Assert.NotNull(t.StepFiles[10].Checksums.ToString(true, true));
            Assert.Equal("Crc32:098BD6A3", t.StepFiles[10].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[10].FileName);
            Assert.Equal("EFOC560000_10_UOTC_N_098BD6A3~", t.StepFiles[10].FileName);
            Assert.Equal(0x1000000L, t.StepFiles[11].Size);
            Assert.False(t.StepFiles[11].IsIndex);
            Assert.False(t.StepFiles[11].IsImageName);
            Assert.NotNull(t.StepFiles[11].Checksums.ToString(true, true));
            Assert.Equal("Crc32:10E2064C", t.StepFiles[11].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[11].FileName);
            Assert.Equal("EFOC560000_11_UOVC_N_10E2064C~", t.StepFiles[11].FileName);
            Assert.Equal(0x38a8000L, t.StepFiles[12].Size);
            Assert.False(t.StepFiles[12].IsIndex);
            Assert.False(t.StepFiles[12].IsImageName);
            Assert.NotNull(t.StepFiles[12].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E0244EA7", t.StepFiles[12].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[12].FileName);
            Assert.Equal("EFOC560000_12_UOXC_N_2ECD169E~", t.StepFiles[12].FileName);
            Assert.Equal(0x2420000L, t.StepFiles[13].Size);
            Assert.False(t.StepFiles[13].IsIndex);
            Assert.False(t.StepFiles[13].IsImageName);
            Assert.NotNull(t.StepFiles[13].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E2C73901", t.StepFiles[13].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[13].FileName);
            Assert.Equal("EFOC560000_13_UOYC_N_0818AB99~", t.StepFiles[13].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            base.Complete();
        }
    }
}