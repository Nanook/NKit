
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Dreamcast_Tests : WipedImageTestsBase
    {
        //Unlicenced / CHD       / 679MiB  / First track marked as data with no FS, 2nd is Dreamcast - 2 sessions
        [Fact]
        public void ZC8_QP_Hfr_ZC8_Nhqvb_PQ_f_ba_Lbhe_Qernzpnfg_Rhebcr_Hay_7z_Expand()
        {
            string fileName = @"ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Dreamcast"));
            string outFolderName = $"Dreamcast_ZC8_QP_Hfr_ZC8_Nhqvb_PQ_f_ba_Lbhe_Qernzpnfg_Rhebcr_Hay_7z_Expand_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Expand", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).7z//ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).cue +2", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.CleanName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.Name);
            Assert.Equal(SourceImageType.IsoMode2, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.True(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.True(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(2, f.ImageFiles.Length);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 1).bin", f.ImageFiles[0].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[0].Extension);
            Assert.Equal(0x16c7fb20L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 2).bin", f.ImageFiles[1].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.ImageFiles[1].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[1].Extension);
            Assert.Equal(0x13ae4d90L, f.ImageFiles[1].Size);
            Assert.True(f.ImageFiles[1].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x14b7e714L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0x2764e09bU, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.Cue, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x130L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal(".cue", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).cue", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal(".cue", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(2, f.IndexFile.Items.Length);
            Assert.Equal(IndexTrackBasicType.Mode2, f.IndexFile.Items[0].BasicType);
            Assert.Equal(0, f.IndexFile.Items[0].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[0].BlockSize);
            Assert.Equal(162502, f.IndexFile.Items[0].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[0].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[0].ChdSubType);
            Assert.False(f.IndexFile.Items[0].FileIsMissing);
            Assert.Equal(0x0L, f.IndexFile.Items[0].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[0].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[0].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[0].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[0].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[0].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[0].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[0].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[0].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[0].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[0].PreGapType);
            Assert.Equal(1, f.IndexFile.Items[0].Session);
            Assert.Equal(0x16c7fb20L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(1, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Mode2Raw, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 1).bin", f.IndexFile.Items[0].FileName);
            Assert.Equal(IndexTrackBasicType.Mode2, f.IndexFile.Items[1].BasicType);
            Assert.Equal(0, f.IndexFile.Items[1].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[1].BlockSize);
            Assert.Equal(140387, f.IndexFile.Items[1].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[1].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[1].ChdSubType);
            Assert.False(f.IndexFile.Items[1].FileIsMissing);
            Assert.Equal(0x16c7fb20L, f.IndexFile.Items[1].ImageOffset);
            Assert.Equal(0x18611ca0L, f.IndexFile.Items[1].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[1].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[1].PreGapType);
            Assert.Equal(2, f.IndexFile.Items[1].Session);
            Assert.Equal(0x13ae4d90L, f.IndexFile.Items[1].Size);
            Assert.Equal(0x0, f.IndexFile.Items[1].SubSize);
            Assert.Equal(2, f.IndexFile.Items[1].TrackIndex);
            Assert.Equal(IndexTrackType.Mode2Raw, f.IndexFile.Items[1].TrackType);
            Assert.Null(f.IndexFile.Items[1].ChdTag);
            Assert.Null(f.IndexFile.Items[1].Comment);
            Assert.NotNull(f.IndexFile.Items[1].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 2).bin", f.IndexFile.Items[1].FileName);
            Assert.Equal(0, f.IndexFile.Additional.Count);

            ////////////////////////////////////////
            // Step Info
            ////////////////////////////////////////
            Assert.NotNull(i);
            Assert.True(i.CanCrc);
            Assert.True(i.CanHash);
            Assert.False(i.CreateInChecksum);
            Assert.False(i.CreateOutChecksum);
            Assert.True(i.CreateScan);
            Assert.False(i.DeleteSourceCandidate);
            Assert.True(i.FullScan);
            Assert.True(i.IsExpand);
            Assert.False(i.IsFix);
            Assert.False(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Expand-Iso-CueToc", i.Name);
            Assert.Equal(OutputType.FolderIndex, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Expand, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("cue/toc", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("", i.ImageConfig);
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
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(3, r.OutFileParts.Length);
            Assert.Equal(0x16c7fb20L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FE289E1E", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 1).bin", r.OutFileParts[0].FileName);
            Assert.Equal(0x13ae4d90L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:5C3F3410", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 2).bin", r.OutFileParts[1].FileName);
            Assert.Equal(0x110L, r.OutFileParts[2].Size);
            Assert.NotNull(r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E1B8357B", r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[2].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).cue", r.OutFileParts[2].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xd3cc6406U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x2a7648b0L, r.ResultSize.Value);
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
            Assert.Equal(ContainerType.Cue, t.ContainerType);
            Assert.Equal(SystemType.Dreamcast, t.System);
            Assert.Equal(TaskType.Expand, t.Task);
            Assert.Equal(0x2a7648b0L, t.Size);
            Assert.Equal(0xd3cc6406U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", t.Name);
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
            Assert.Equal(Path.Combine(basePath, "ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(3, t.StepFiles.Count);
            Assert.Equal(0x16c7fb20L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:FE289E1E", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 1).bin", t.StepFiles[0].FileName);
            Assert.Equal(0x13ae4d90L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.True(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:5C3F3410", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay) (Track 2).bin", t.StepFiles[1].FileName);
            Assert.Equal(0x110L, t.StepFiles[2].Size);
            Assert.True(t.StepFiles[2].IsIndex);
            Assert.True(t.StepFiles[2].IsImageName);
            Assert.NotNull(t.StepFiles[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:E1B8357B", t.StepFiles[2].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[2].FileName);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay).cue", t.StepFiles[2].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("ZC8 QP - Hfr ZC8 Nhqvb PQ'f ba Lbhe Qernzpnfg (Rhebcr) (Hay)", t.Scan.Name);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.Equal(0xd3cc6406U, t.Scan.Crc);
            Assert.Equal(0xd3cc6406U, t.Scan.CrcDecrypted);
            Assert.Equal(0x2a7648b0L, t.Scan.Size);
            Assert.Equal(7, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(3, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Dreamcast", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("02A7648B0", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("D3CC6406", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(2, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[0].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[0].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("162500", t.Scan.Areas[0].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[0].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[0].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[0].Type);
            Assert.Equal(0xfe289e1eU, t.Scan.Areas[0].Crc);
            Assert.Equal(0xfe289e1eU, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x16c7fb20L, t.Scan.Areas[0].Size);
            Dictionary<FsType, int> t_ScanTypes0 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[0]);
            Assert.Equal(1, t_ScanTypes0.Count);
            Assert.Equal(1, t_ScanTypes0[FsType.System]);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("140386", t.Scan.Areas[1].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[1].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("018611CA0", t.Scan.Areas[1].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("000022461", t.Scan.Areas[1].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("018611CA0", t.Scan.Areas[1].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[1].Type);
            Assert.Equal(0x5c3f3410U, t.Scan.Areas[1].Crc);
            Assert.Equal(0x5c3f3410U, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x13ae4d90L, t.Scan.Areas[1].Size);
            Dictionary<FsType, int> t_ScanTypes1 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[1]);
            Assert.Equal(2, t_ScanTypes1.Count);
            Assert.Equal(3, t_ScanTypes1[FsType.System]);
            Assert.Equal(6, t_ScanTypes1[FsType.Iso9660]);

            base.Complete();
        }
    }
}