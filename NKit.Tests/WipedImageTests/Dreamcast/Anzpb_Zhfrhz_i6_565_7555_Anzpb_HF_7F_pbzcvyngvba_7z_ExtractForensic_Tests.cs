
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Dreamcast_Tests : WipedImageTestsBase
    {
        //Retail     / GDI       / 1.10GiB / Type3 Split (Data Audio Data Audio Data)
        [Fact]
        public void Anzpb_Zhfrhz_i6_565_7555_Anzpb_HF_7F_pbzcvyngvba_7z_ExtractForensic()
        {
            string fileName = @"Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Dreamcast"));
            string outFolderName = $"Dreamcast_Anzpb_Zhfrhz_i6_565_7555_Anzpb_HF_7F_pbzcvyngvba_7z_ExtractForensic_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Extract", @"f", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].7z//Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].gdi +5", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", f.CleanName);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", f.Name);
            Assert.Equal(SourceImageType.Gdi, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.Dreamcast, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.True(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.True(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(5, f.ImageFiles.Length);
            Assert.Equal("track01.bin", f.ImageFiles[0].FileName);
            Assert.Equal("track", f.ImageFiles[0].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[0].Extension);
            Assert.Equal(0x15bfa0L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal("track02.raw", f.ImageFiles[1].FileName);
            Assert.Equal("track", f.ImageFiles[1].NameOnly);
            Assert.Equal(".raw", f.ImageFiles[1].Extension);
            Assert.Equal(0x12e0a0L, f.ImageFiles[1].Size);
            Assert.True(f.ImageFiles[1].IsArchived);
            Assert.Equal("track03.bin", f.ImageFiles[2].FileName);
            Assert.Equal("track", f.ImageFiles[2].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[2].Extension);
            Assert.Equal(0x462a2c30L, f.ImageFiles[2].Size);
            Assert.True(f.ImageFiles[2].IsArchived);
            Assert.Equal("track04.raw", f.ImageFiles[3].FileName);
            Assert.Equal("track", f.ImageFiles[3].NameOnly);
            Assert.Equal(".raw", f.ImageFiles[3].Extension);
            Assert.Equal(0x102f90L, f.ImageFiles[3].Size);
            Assert.True(f.ImageFiles[3].IsArchived);
            Assert.Equal("track05.bin", f.ImageFiles[4].FileName);
            Assert.Equal("track", f.ImageFiles[4].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[4].Extension);
            Assert.Equal(0x682620L, f.ImageFiles[4].Size);
            Assert.True(f.ImageFiles[4].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x14d7e06L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0x5ee57b9eU, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.Gdi, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x95L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal(".gdi", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba].gdi", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal(".gdi", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(5, f.IndexFile.Items.Length);
            Assert.Equal(IndexTrackBasicType.Mode1, f.IndexFile.Items[0].BasicType);
            Assert.Equal(0, f.IndexFile.Items[0].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[0].BlockSize);
            Assert.Equal(606, f.IndexFile.Items[0].Blocks);
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
            Assert.Equal(0, f.IndexFile.Items[0].Session);
            Assert.Equal(0x15bfa0L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(1, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Mode1Raw, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("track01.bin", f.IndexFile.Items[0].FileName);
            Assert.Equal(IndexTrackBasicType.Audio, f.IndexFile.Items[1].BasicType);
            Assert.Equal(0, f.IndexFile.Items[1].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[1].BlockSize);
            Assert.Equal(526, f.IndexFile.Items[1].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[1].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[1].ChdSubType);
            Assert.False(f.IndexFile.Items[1].FileIsMissing);
            Assert.Equal(0x15bfa0L, f.IndexFile.Items[1].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].PhysicalOffset);
            Assert.Equal(0x1b21c0L, f.IndexFile.Items[1].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[1].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[1].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[1].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[1].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[1].Session);
            Assert.Equal(0x12e0a0L, f.IndexFile.Items[1].Size);
            Assert.Equal(0x0, f.IndexFile.Items[1].SubSize);
            Assert.Equal(2, f.IndexFile.Items[1].TrackIndex);
            Assert.Equal(IndexTrackType.Audio, f.IndexFile.Items[1].TrackType);
            Assert.Null(f.IndexFile.Items[1].ChdTag);
            Assert.Null(f.IndexFile.Items[1].Comment);
            Assert.NotNull(f.IndexFile.Items[1].FileName);
            Assert.Equal("track02.raw", f.IndexFile.Items[1].FileName);
            Assert.Equal(IndexTrackBasicType.Mode1, f.IndexFile.Items[2].BasicType);
            Assert.Equal(0, f.IndexFile.Items[2].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[2].BlockSize);
            Assert.Equal(500497, f.IndexFile.Items[2].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[2].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[2].ChdSubType);
            Assert.False(f.IndexFile.Items[2].FileIsMissing);
            Assert.Equal(0x28a040L, f.IndexFile.Items[2].ImageOffset);
            Assert.Equal(0x64efd80L, f.IndexFile.Items[2].PhysicalOffset);
            Assert.Equal(0x64efd80L, f.IndexFile.Items[2].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[2].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[2].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[2].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[2].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[2].Session);
            Assert.Equal(0x462a2c30L, f.IndexFile.Items[2].Size);
            Assert.Equal(0x0, f.IndexFile.Items[2].SubSize);
            Assert.Equal(3, f.IndexFile.Items[2].TrackIndex);
            Assert.Equal(IndexTrackType.Mode1Raw, f.IndexFile.Items[2].TrackType);
            Assert.Null(f.IndexFile.Items[2].ChdTag);
            Assert.Null(f.IndexFile.Items[2].Comment);
            Assert.NotNull(f.IndexFile.Items[2].FileName);
            Assert.Equal("track03.bin", f.IndexFile.Items[2].FileName);
            Assert.Equal(IndexTrackBasicType.Audio, f.IndexFile.Items[3].BasicType);
            Assert.Equal(0, f.IndexFile.Items[3].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[3].BlockSize);
            Assert.Equal(451, f.IndexFile.Items[3].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[3].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[3].ChdSubType);
            Assert.False(f.IndexFile.Items[3].FileIsMissing);
            Assert.Equal(0x4652cc70L, f.IndexFile.Items[3].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].PhysicalOffset);
            Assert.Equal(0x4c7e8bd0L, f.IndexFile.Items[3].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[3].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[3].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[3].Session);
            Assert.Equal(0x102f90L, f.IndexFile.Items[3].Size);
            Assert.Equal(0x0, f.IndexFile.Items[3].SubSize);
            Assert.Equal(4, f.IndexFile.Items[3].TrackIndex);
            Assert.Equal(IndexTrackType.Audio, f.IndexFile.Items[3].TrackType);
            Assert.Null(f.IndexFile.Items[3].ChdTag);
            Assert.Null(f.IndexFile.Items[3].Comment);
            Assert.NotNull(f.IndexFile.Items[3].FileName);
            Assert.Equal("track04.raw", f.IndexFile.Items[3].FileName);
            Assert.Equal(IndexTrackBasicType.Mode1, f.IndexFile.Items[4].BasicType);
            Assert.Equal(0, f.IndexFile.Items[4].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[4].BlockSize);
            Assert.Equal(2902, f.IndexFile.Items[4].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[4].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[4].ChdSubType);
            Assert.False(f.IndexFile.Items[4].FileIsMissing);
            Assert.Equal(0x4662fc00L, f.IndexFile.Items[4].ImageOffset);
            Assert.Equal(0x4c941d80L, f.IndexFile.Items[4].PhysicalOffset);
            Assert.Equal(0x4c941d80L, f.IndexFile.Items[4].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[4].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[4].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[4].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[4].Session);
            Assert.Equal(0x682620L, f.IndexFile.Items[4].Size);
            Assert.Equal(0x0, f.IndexFile.Items[4].SubSize);
            Assert.Equal(5, f.IndexFile.Items[4].TrackIndex);
            Assert.Equal(IndexTrackType.Mode1Raw, f.IndexFile.Items[4].TrackType);
            Assert.Null(f.IndexFile.Items[4].ChdTag);
            Assert.Null(f.IndexFile.Items[4].Comment);
            Assert.NotNull(f.IndexFile.Items[4].FileName);
            Assert.Equal("track05.bin", f.IndexFile.Items[4].FileName);
            Assert.Equal(0, f.IndexFile.Additional.Count);

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
            Assert.True(i.IsLossy);
            Assert.True(i.WriteImage);
            Assert.Equal("Extract-Iso", i.Name);
            Assert.Equal(OutputType.FolderFiles, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Extract, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("folderfiles", i.Config);
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
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(0, r.OutFileParts.Length);
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
            Assert.Equal(ContainerType.Gdi, t.ContainerType);
            Assert.Equal(SystemType.Dreamcast, t.System);
            Assert.Equal(TaskType.Extract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Anzpb Zhfrhz i6.565 (7555)(Anzpb)(HF)[!][7F][pbzcvyngvba]", t.Name);
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
            Assert.False(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.Null(t.Key);
            Assert.NotNull(t.StepFiles);
            Assert.Equal(0, t.StepFiles.Count);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            ////////////////////////////////////////
            // Extracted Tracks
            ////////////////////////////////////////

            string[] extractLines = File.ReadAllLines(Path.Combine(base.GetPath(), "Anzpb_Zhfrhz_i6_565_7555_Anzpb_HF_7F_pbzcvyngvba_7z_ExtractForensic_Tests.txt"));
            Assert.Equal(extractLines.Length, base.ExtractFileResults.Length + ExtractFileDirectories.Length);
            int l = 0;
            for (int c = 0; c < base.ExtractFileResults.Length; c++)
                Assert.Equal(extractLines[l++], $"{base.ExtractFileResults[c].Crc:x8}\t{base.ExtractFileResults[c].Size:x}\t/{base.ExtractFileResults[c].FileName.Replace('\\', '/')}");
            for (int c = 0; c < base.ExtractFileDirectories.Length; c++)
                Assert.Equal(extractLines[l++], base.ExtractFileDirectories[c].Replace('\\', '/'));


            base.Complete();
        }
    }
}