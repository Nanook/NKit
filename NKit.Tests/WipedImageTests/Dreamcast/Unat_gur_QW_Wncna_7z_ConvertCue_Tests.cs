
using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Dreamcast_Tests : WipedImageTestsBase
    {
        //MILCD      / CHD       / 680MiB  / MIL CD (Audio... Data)
        [Fact]
        public void Unat_gur_QW_Wncna_7z_ConvertCue()
        {
            string fileName = @"Unat gur QW (Wncna).7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Dreamcast"));
            string outFolderName = $"Dreamcast_Unat_gur_QW_Wncna_7z_ConvertCue_{Guid.NewGuid():N}";
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
            Assert.Equal("Unat gur QW (Wncna).7z//Unat gur QW (Wncna).cue +4", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Unat gur QW (Wncna)", f.CleanName);
            Assert.Equal("Unat gur QW (Wncna)", f.Name);
            Assert.Equal(SourceImageType.Cue, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.True(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.True(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(4, f.ImageFiles.Length);
            Assert.Equal("Unat gur QW (Wncna) (Track 1).bin", f.ImageFiles[0].FileName);
            Assert.Equal("Unat gur QW (Wncna)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[0].Extension);
            Assert.Equal(0x31f5000L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal("Unat gur QW (Wncna) (Track 2).bin", f.ImageFiles[1].FileName);
            Assert.Equal("Unat gur QW (Wncna)", f.ImageFiles[1].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[1].Extension);
            Assert.Equal(0x42583e0L, f.ImageFiles[1].Size);
            Assert.True(f.ImageFiles[1].IsArchived);
            Assert.Equal("Unat gur QW (Wncna) (Track 3).bin", f.ImageFiles[2].FileName);
            Assert.Equal("Unat gur QW (Wncna)", f.ImageFiles[2].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[2].Extension);
            Assert.Equal(0x5499cf0L, f.ImageFiles[2].Size);
            Assert.True(f.ImageFiles[2].IsArchived);
            Assert.Equal("Unat gur QW (Wncna) (Track 4).bin", f.ImageFiles[3].FileName);
            Assert.Equal("Unat gur QW (Wncna)", f.ImageFiles[3].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[3].Extension);
            Assert.Equal(0x1df4cc90L, f.ImageFiles[3].Size);
            Assert.True(f.ImageFiles[3].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Unat gur QW (Wncna).7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("Unat gur QW (Wncna)", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x67246L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0x8de33068U, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.Cue, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x217L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal(".cue", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("Unat gur QW (Wncna).cue", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("Unat gur QW (Wncna)", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal(".cue", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(4, f.IndexFile.Items.Length);
            Assert.Equal(IndexTrackBasicType.Audio, f.IndexFile.Items[0].BasicType);
            Assert.Equal(0, f.IndexFile.Items[0].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[0].BlockSize);
            Assert.Equal(22272, f.IndexFile.Items[0].Blocks);
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
            Assert.Equal(0x31f5000L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(1, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Audio, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 1).bin", f.IndexFile.Items[0].FileName);
            Assert.Equal(IndexTrackBasicType.Audio, f.IndexFile.Items[1].BasicType);
            Assert.Equal(0, f.IndexFile.Items[1].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[1].BlockSize);
            Assert.Equal(29578, f.IndexFile.Items[1].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[1].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[1].ChdSubType);
            Assert.False(f.IndexFile.Items[1].FileIsMissing);
            Assert.Equal(0x31f5000L, f.IndexFile.Items[1].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[1].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[1].PreGapType);
            Assert.Equal(1, f.IndexFile.Items[1].Session);
            Assert.Equal(0x42583e0L, f.IndexFile.Items[1].Size);
            Assert.Equal(0x0, f.IndexFile.Items[1].SubSize);
            Assert.Equal(2, f.IndexFile.Items[1].TrackIndex);
            Assert.Equal(IndexTrackType.Audio, f.IndexFile.Items[1].TrackType);
            Assert.Null(f.IndexFile.Items[1].ChdTag);
            Assert.Null(f.IndexFile.Items[1].Comment);
            Assert.NotNull(f.IndexFile.Items[1].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 2).bin", f.IndexFile.Items[1].FileName);
            Assert.Equal(IndexTrackBasicType.Audio, f.IndexFile.Items[2].BasicType);
            Assert.Equal(0, f.IndexFile.Items[2].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[2].BlockSize);
            Assert.Equal(37717, f.IndexFile.Items[2].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[2].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[2].ChdSubType);
            Assert.False(f.IndexFile.Items[2].FileIsMissing);
            Assert.Equal(0x744d3e0L, f.IndexFile.Items[2].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[2].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[2].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[2].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[2].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[2].PreGapType);
            Assert.Equal(1, f.IndexFile.Items[2].Session);
            Assert.Equal(0x5499cf0L, f.IndexFile.Items[2].Size);
            Assert.Equal(0x0, f.IndexFile.Items[2].SubSize);
            Assert.Equal(3, f.IndexFile.Items[2].TrackIndex);
            Assert.Equal(IndexTrackType.Audio, f.IndexFile.Items[2].TrackType);
            Assert.Null(f.IndexFile.Items[2].ChdTag);
            Assert.Null(f.IndexFile.Items[2].Comment);
            Assert.NotNull(f.IndexFile.Items[2].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 3).bin", f.IndexFile.Items[2].FileName);
            Assert.Equal(IndexTrackBasicType.Mode2, f.IndexFile.Items[3].BasicType);
            Assert.Equal(0, f.IndexFile.Items[3].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[3].BlockSize);
            Assert.Equal(213683, f.IndexFile.Items[3].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[3].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[3].ChdSubType);
            Assert.False(f.IndexFile.Items[3].FileIsMissing);
            Assert.Equal(0xc8e70d0L, f.IndexFile.Items[3].ImageOffset);
            Assert.Equal(0xe279250L, f.IndexFile.Items[3].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[3].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[3].PreGapType);
            Assert.Equal(2, f.IndexFile.Items[3].Session);
            Assert.Equal(0x1df4cc90L, f.IndexFile.Items[3].Size);
            Assert.Equal(0x0, f.IndexFile.Items[3].SubSize);
            Assert.Equal(4, f.IndexFile.Items[3].TrackIndex);
            Assert.Equal(IndexTrackType.Mode2Raw, f.IndexFile.Items[3].TrackType);
            Assert.Null(f.IndexFile.Items[3].ChdTag);
            Assert.Null(f.IndexFile.Items[3].Comment);
            Assert.NotNull(f.IndexFile.Items[3].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 4).bin", f.IndexFile.Items[3].FileName);
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
            Assert.Equal("Convert-Iso-CueToc", i.Name);
            Assert.Equal(OutputType.FolderIndex, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Convert, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("cue/toc", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("cue", i.ImageConfig);
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
            Assert.Equal("Unat gur QW (Wncna)", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(5, r.OutFileParts.Length);
            Assert.Equal(0x31f5000L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:89392F2C", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 1).bin", r.OutFileParts[0].FileName);
            Assert.Equal(0x42583e0L, r.OutFileParts[1].Size);
            Assert.NotNull(r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:8D49C0EF", r.OutFileParts[1].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[1].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 2).bin", r.OutFileParts[1].FileName);
            Assert.Equal(0x5499cf0L, r.OutFileParts[2].Size);
            Assert.NotNull(r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:5FC34E41", r.OutFileParts[2].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[2].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 3).bin", r.OutFileParts[2].FileName);
            Assert.Equal(0x1df4cc90L, r.OutFileParts[3].Size);
            Assert.NotNull(r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CA83D01D", r.OutFileParts[3].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[3].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 4).bin", r.OutFileParts[3].FileName);
            Assert.Equal(0x16dL, r.OutFileParts[4].Size);
            Assert.NotNull(r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D5D9FA96", r.OutFileParts[4].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[4].FileName);
            Assert.Equal("Unat gur QW (Wncna).cue", r.OutFileParts[4].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0x8ff6cf7dU, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x2a833d60L, r.ResultSize.Value);
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
            Assert.Equal(TaskType.Convert, t.Task);
            Assert.Equal(0x2a833d60L, t.Size);
            Assert.Equal(0x8ff6cf7dU, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "Unat gur QW (Wncna)"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Unat gur QW (Wncna)", t.Name);
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
            Assert.Equal(Path.Combine(basePath, "Unat gur QW (Wncna).nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(5, t.StepFiles.Count);
            Assert.Equal(0x31f5000L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("Crc32:89392F2C", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 1).bin", t.StepFiles[0].FileName);
            Assert.Equal(0x42583e0L, t.StepFiles[1].Size);
            Assert.False(t.StepFiles[1].IsIndex);
            Assert.True(t.StepFiles[1].IsImageName);
            Assert.NotNull(t.StepFiles[1].Checksums.ToString(true, true));
            Assert.Equal("Crc32:8D49C0EF", t.StepFiles[1].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[1].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 2).bin", t.StepFiles[1].FileName);
            Assert.Equal(0x5499cf0L, t.StepFiles[2].Size);
            Assert.False(t.StepFiles[2].IsIndex);
            Assert.True(t.StepFiles[2].IsImageName);
            Assert.NotNull(t.StepFiles[2].Checksums.ToString(true, true));
            Assert.Equal("Crc32:5FC34E41", t.StepFiles[2].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[2].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 3).bin", t.StepFiles[2].FileName);
            Assert.Equal(0x1df4cc90L, t.StepFiles[3].Size);
            Assert.False(t.StepFiles[3].IsIndex);
            Assert.True(t.StepFiles[3].IsImageName);
            Assert.NotNull(t.StepFiles[3].Checksums.ToString(true, true));
            Assert.Equal("Crc32:CA83D01D", t.StepFiles[3].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[3].FileName);
            Assert.Equal("Unat gur QW (Wncna) (Track 4).bin", t.StepFiles[3].FileName);
            Assert.Equal(0x16dL, t.StepFiles[4].Size);
            Assert.True(t.StepFiles[4].IsIndex);
            Assert.True(t.StepFiles[4].IsImageName);
            Assert.NotNull(t.StepFiles[4].Checksums.ToString(true, true));
            Assert.Equal("Crc32:D5D9FA96", t.StepFiles[4].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[4].FileName);
            Assert.Equal("Unat gur QW (Wncna).cue", t.StepFiles[4].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("Unat gur QW (Wncna)", t.Scan.Name);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.Equal(0x8ff6cf7dU, t.Scan.Crc);
            Assert.Equal(0x8ff6cf7dU, t.Scan.CrcDecrypted);
            Assert.Equal(0x2a833d60L, t.Scan.Size);
            Assert.Equal(70, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(9, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("Dreamcast", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("Disc", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("02A833D60", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("8FF6CF7D", t.Scan.Properties["CRC"].ToXmlValue());
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
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal("00:04:56.960", t.Scan.Areas[0].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal(AreaType.Audio, t.Scan.Areas[0].Type);
            Assert.Equal(0x89392f2cU, t.Scan.Areas[0].Crc);
            Assert.Equal(0x89392f2cU, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x31f5000L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("0", t.Scan.Areas[1].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal("00:06:34.373", t.Scan.Areas[1].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal(AreaType.Audio, t.Scan.Areas[1].Type);
            Assert.Equal(0x8d49c0efU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x8d49c0efU, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x42583e0L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[2].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[2].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal("00:08:22.893", t.Scan.Areas[2].AreaInfo.Properties["Duration"].ToXmlValue());
            Assert.Equal(AreaType.Audio, t.Scan.Areas[2].Type);
            Assert.Equal(0x5fc34e41U, t.Scan.Areas[2].Crc);
            Assert.Equal(0x5fc34e41U, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0x5499cf0L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[3].AreaInfo.Properties["Session"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[3].AreaInfo.Properties["Track"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["Region"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("2352", t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[3].AreaInfo.Properties["Mode1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.Equal("213658", t.Scan.Areas[3].AreaInfo.Properties["Mode2Form1"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.Equal("24", t.Scan.Areas[3].AreaInfo.Properties["Mode2Form2"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.Equal("000000000", t.Scan.Areas[3].AreaInfo.Properties["AreaOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.Equal("00E279250", t.Scan.Areas[3].AreaInfo.Properties["SessionOffsetBase"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["HeaderXxHash"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.Equal("0000342B3", t.Scan.Areas[3].AreaInfo.Properties["PvdSectorCount"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.Equal("00E279250", t.Scan.Areas[3].AreaInfo.Properties["PhysicalOffset"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["ThreeKey"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["DecryptionValid"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[3].Type);
            Assert.Equal(0xca83d01dU, t.Scan.Areas[3].Crc);
            Assert.Equal(0xca83d01dU, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x1df4cc90L, t.Scan.Areas[3].Size);
            Dictionary<FsType, int> t_ScanTypes3 = WipedImageTestsBase.GetIsoFsTypes(t.Scan.Areas[3]);
            Assert.Equal(2, t_ScanTypes3.Count);
            Assert.Equal(2, t_ScanTypes3[FsType.System]);
            Assert.Equal(62, t_ScanTypes3[FsType.Iso9660]);

            base.Complete();
        }
    }
}