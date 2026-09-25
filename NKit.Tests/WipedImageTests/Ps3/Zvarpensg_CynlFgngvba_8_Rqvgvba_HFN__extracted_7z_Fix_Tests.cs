
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Ps3_Tests : WipedImageTestsBase
    {
        //Retail     / ISO       / 367MiB  / Regular, small Image
        [Fact]
        public void Zvarpensg_CynlFgngvba_8_Rqvgvba_HFN__extracted_7z_Fix()
        {
            string fileName = @"Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Ps3"));
            string outFolderName = $"Ps3_Zvarpensg_CynlFgngvba_8_Rqvgvba_HFN__extracted_7z_Fix_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"../../../../../WipedImages/_keys";
            string fixInfo = @"../../../../../WipedImages/_fix/fix_ps3.yaml";
            string fixFiles = @"../../../../../WipedImages/_fix/ps3_files";

            SystemPresetSettings presets = base.CreatePresets("Fix", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.PS3;

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
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.7z//PS3_DISC.SFB", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted", f.CleanName);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted", f.Name);
            Assert.Equal(SourceImageType.Sfb, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.PS3, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.False(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("PS3_DISC.SFB", f.ImageFiles[0].FileName);
            Assert.Equal("PS3_DISC.SFB", f.ImageFiles[0].NameOnly);
            Assert.Equal(".SFB", f.ImageFiles[0].Extension);
            Assert.Equal(0x600L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x1a037L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.NotNull(f.Key);
            Assert.Equal("00000000000000000000000000000000", f.Key.ToHexString());
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
            Assert.True(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Fix-Ps3", i.Name);
            Assert.Equal(OutputType.Image, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Fix, i.StepType);
            Assert.Equal(VerifyMethod.InChecksums, i.VerifyMethod);
            Assert.Equal("Crc32", string.Join('|', i.VerifyChecksums.Select(a => a.ToString())));
            Assert.NotNull(i.Config);
            Assert.Equal("iso", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("sfb", i.ImageConfig);
            Assert.NotNull(i.SrcParts);
            Assert.Equal(1, i.SrcParts.Length);
            Assert.Equal(0x0L, i.SrcParts[0].Size);
            Assert.NotNull(i.SrcParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:DE29F0D7", i.SrcParts[0].Checksums.ToString(true, true));
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
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.iso", r.FinalName);
            Assert.Equal("Crc32", string.Join('|', r.ChkCompared.Select(a => a.ToString())));
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x16e80000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:95928AAC, Md5:C033E3C1FEC815E79E08CA5D54DEB4B7", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.iso~", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x95928aacU, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x16e80000L, r.ResultSize.Value);
            Assert.NotNull(r.Scan);
            Assert.NotNull(r.StepInfo);
            Assert.Equal(VerifyResult.VerifyFailed, r.VerifyResult);
            Assert.Equal("InChecksums [Crc32]", r.VerifyType);

            ////////////////////////////////////////
            // StepResult DatItem
            ////////////////////////////////////////
            Assert.Null(r.MatchedDatItem);

            ////////////////////////////////////////
            // Task Result
            ////////////////////////////////////////
            Assert.Equal(ContainerType.Ps3Jb, t.ContainerType);
            Assert.Equal(SystemType.PS3, t.System);
            Assert.Equal(TaskType.Fix, t.Task);
            Assert.Equal(0x16e80000L, t.Size);
            Assert.Equal(0x95928aacU, t.CRC);
            Assert.Equal(0x3cc83683U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.VerifyFailed, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.Equal(basePath.TrimEnd('\\', '/'), t.OutPath.TrimEnd('\\', '/'));
            Assert.NotNull(t.Name);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted", t.Name);
            Assert.NotNull(t.VerifyType);
            Assert.Equal("InChecksums [Crc32]", t.VerifyType);
            Assert.NotNull(t.VerifyChecksum);
            Assert.Equal("", t.VerifyChecksum);
            Assert.Null(t.DatMatch);
            Assert.Null(t.ErrorMsg);
            Assert.NotNull(t.OutFileName);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.iso", t.OutFileName);
            Assert.Null(t.OutKeyFilePath);
            Assert.NotNull(t.OutScanFilePath);
            Assert.Equal(Path.Combine(basePath, "Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.NotNull(t.Key);
            Assert.Equal("00000000000000000000000000000000", t.Key.ToHexString());
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x16e80000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:95928AAC, Md5:C033E3C1FEC815E79E08CA5D54DEB4B7", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted.iso~", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Zvarpensg - CynlFgngvba 8 Rqvgvba (HFN)_extracted", t.Scan.Name);
            Assert.Equal(SystemType.PS3, f.SystemType);
            Assert.Equal(0x95928aacU, t.Scan.Crc);
            Assert.Equal(0x3cc83683U, t.Scan.CrcDecrypted);
            Assert.Equal(0x16e80000L, t.Scan.Size);
            Assert.Equal(770, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(65, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("PS3", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("016E80000", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("95928AAC", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("3CC83683", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(3, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
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
            Assert.Equal("000009000", t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("5AE55B10", t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("8A5C0A710CE3AD5C", t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("00002DD00", t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0xccd6f26dU, t.Scan.Areas[0].Crc);
            Assert.Equal(0xccd6f26dU, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x460000L, t.Scan.Areas[0].Size);
            Dictionary<FsType, int> t_ScanTypes0 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[0]);
            Assert.Equal(4, t_ScanTypes0.Count);
            Assert.Equal(2, t_ScanTypes0[FsType.System]);
            Assert.Equal(350, t_ScanTypes0[FsType.Iso9660]);
            Assert.Equal(381, t_ScanTypes0[FsType.Joliet]);
            Assert.Equal(768, t_ScanTypes0[FsType.Udf]);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000460000", t.Scan.Areas[1].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[1].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[1].Type);
            Assert.Equal(0xce14ccfcU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x82bca929U, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x6a10000L, t.Scan.Areas[1].Size);
            Dictionary<FsType, int> t_ScanTypes1 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[1]);
            Assert.Equal(4, t_ScanTypes1.Count);
            Assert.Equal(2, t_ScanTypes1[FsType.System]);
            Assert.Equal(350, t_ScanTypes1[FsType.Iso9660]);
            Assert.Equal(381, t_ScanTypes1[FsType.Joliet]);
            Assert.Equal(768, t_ScanTypes1[FsType.Udf]);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2048", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[2].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("006E70000", t.Scan.Areas[2].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[2].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
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
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0xff737999U, t.Scan.Areas[2].Crc);
            Assert.Equal(0xff737999U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x10010000L, t.Scan.Areas[2].Size);
            Dictionary<FsType, int> t_ScanTypes2 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[2]);
            Assert.Equal(4, t_ScanTypes2.Count);
            Assert.Equal(2, t_ScanTypes2[FsType.System]);
            Assert.Equal(350, t_ScanTypes2[FsType.Iso9660]);
            Assert.Equal(381, t_ScanTypes2[FsType.Joliet]);
            Assert.Equal(768, t_ScanTypes2[FsType.Udf]);

            base.Complete();
        }
    }
}