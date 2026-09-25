
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_XBox360_Tests : WipedImageTestsBase
    {
        //Retail     / ISO       / 7.20GiB / Data in Video Part 2, extra pvd in xdvdfs
        [Fact]
        public void Obeqreynaqf_7_Nqq_Ba_Pbagrag_Cnpx_Rhebcr_chd_Scan()
        {
            string fileName = @"Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "XBox360"));
            string outFolderName = $"XBox360_Obeqreynaqf_7_Nqq_Ba_Pbagrag_Cnpx_Rhebcr_chd_Scan_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Scan", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.XBox360;

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
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.CleanName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.Name);
            Assert.Equal(SourceImageType.Chd, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.XBox360, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.False(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).chd", f.ImageFiles[0].FileName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".chd", f.ImageFiles[0].Extension);
            Assert.Equal(0x6ca1L, f.ImageFiles[0].Size);
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
            Assert.False(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.True(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.False(i.WriteImage);
            Assert.Equal("Scan-Image", i.Name);
            Assert.Equal(OutputType.Scan, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Scan, i.StepType);
            Assert.Equal(VerifyMethod.InChecksums, i.VerifyMethod);
            Assert.Equal("Sha1", string.Join('|', i.VerifyChecksums.Select(a => a.ToString())));
            Assert.NotNull(i.Config);
            Assert.Equal("scan", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("scan", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x0L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Sha1:E211AC871CC8106C3D3E103D703D2DC379D99244", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).nkit.yaml", r.FinalName);
            Assert.Equal("Sha1", string.Join('|', r.ChkCompared.Select(a => a.ToString())));
            Assert.NotNull(r.InFileParts);
            Assert.Equal(1, r.InFileParts.Length);
            Assert.Equal(0x0L, r.InFileParts[0].Size);
            Assert.NotNull(r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Sha1:E211AC871CC8106C3D3E103D703D2DC379D99244", r.InFileParts[0].Checksums.ToString(true, true));
            Assert.Null(r.InFileParts[0].FileName);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x1d3390000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x00000000U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x0L, r.ResultSize.Value);
            Assert.NotNull(r.Scan);
            Assert.NotNull(r.StepInfo);
            Assert.Equal(VerifyResult.VerifySuccess, r.VerifyResult);
            Assert.Equal("InChecksums [Sha1]", r.VerifyType);

            ////////////////////////////////////////
            // StepResult DatItem
            ////////////////////////////////////////
            Assert.Null(r.MatchedDatItem);

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.Chd, t.ContainerType);
            Assert.Equal(SystemType.XBox360, t.System);
            Assert.Equal(TaskType.Scan, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x70fbcf18U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.VerifySuccess, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal("", t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("InChecksums [Sha1]", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("E211AC871CC8106C3D3E103D703D2DC379D99244", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x1d3390000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Obeqreynaqf 7 - Nqq-Ba Pbagrag Cnpx (Rhebcr)", t.Scan.Name);
            Assert.Equal(SystemType.XBox360, f.SystemType);
            Assert.Equal(0x70fbcf18U, t.Scan.Crc);
            Assert.Equal(0x70fbcf18U, t.Scan.CrcDecrypted);
            Assert.Equal(0x1d3390000L, t.Scan.Size);
            Assert.Equal(346, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(13, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("XBox360", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("1D3390000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("70FBCF18", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("70FBCF18", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(5, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.Equal("Iso9660", t.Scan.Areas[0].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("000008800", t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("7C29CDA3", t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("67816277D720E131", t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("000011448", t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal("2012-11-18T16:00:00", t.Scan.Areas[0].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0xb58b313bU, t.Scan.Areas[0].Crc);
            Assert.Equal(0xb58b313bU, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x8a40000L, t.Scan.Areas[0].Size);
            Dictionary<FsType, int> t_ScanTypes0 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[0]);
            Assert.Equal(3, t_ScanTypes0.Count);
            Assert.Equal(2, t_ScanTypes0[FsType.System]);
            Assert.Equal(157, t_ScanTypes0[FsType.Iso9660]);
            Assert.Equal(318, t_ScanTypes0[FsType.Udf]);
            Assert.Equal(AreaType.Other, t.Scan.Areas[1].Type);
            Assert.Equal(0x3fc71e7dU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x3fc71e7dU, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x7350000L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.Equal("XDvdFs", t.Scan.Areas[2].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[2].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal("2013-03-08T21:26:21", t.Scan.Areas[2].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0x308cc3d3U, t.Scan.Areas[2].Crc);
            Assert.Equal(0x308cc3d3U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x1b3880000L, t.Scan.Areas[2].Size);
            Assert.Equal(AreaType.Other, t.Scan.Areas[3].Type);
            Assert.Equal(0x3fc71e7dU, t.Scan.Areas[3].Crc);
            Assert.Equal(0x3fc71e7dU, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x7350000L, t.Scan.Areas[3].Size);
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.Equal("Iso9660", t.Scan.Areas[4].AreaInfo.Properties["FsType"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["Version"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[4].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("008A40000", t.Scan.Areas[4].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[4].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[4].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[4].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal("2012-11-18T16:00:00", t.Scan.Areas[4].AreaInfo.Properties["HeaderDate"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[4].Type);
            Assert.Equal(0x6947fda0U, t.Scan.Areas[4].Crc);
            Assert.Equal(0x6947fda0U, t.Scan.Areas[4].CrcDecrypted);
            Assert.Equal(0x8a30000L, t.Scan.Areas[4].Size);
            Dictionary<FsType, int> t_ScanTypes4 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[4]);
            Assert.Equal(3, t_ScanTypes4.Count);
            Assert.Equal(2, t_ScanTypes4[FsType.System]);
            Assert.Equal(157, t_ScanTypes4[FsType.Iso9660]);
            Assert.Equal(318, t_ScanTypes4[FsType.Udf]);

            base.Complete();
        }
    }
}