
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_Cdi_Tests : WipedImageTestsBase
    {
        //Retail     / CUE       / 18MiB   / CD-i Fs
        [Fact]
        public void Hygen_PQ_v_Fbppre_Rhebcr_7z_Extract()
        {
            string fileName = @"Hygen PQ-v Fbppre (Rhebcr).7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "Cdi"));
            string outFolderName = $"Cdi_Hygen_PQ_v_Fbppre_Rhebcr_7z_Extract_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Extract", @"ri:.*", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.CDi;

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
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr).7z//Hygen PQ-v Fbppre (Rhebcr).cue", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", f.CleanName);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", f.Name);
            Assert.Equal(SourceImageType.Cdi, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.CDi, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.True(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.False(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(1, f.ImageFiles.Length);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr).bin", f.ImageFiles[0].FileName);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", f.ImageFiles[0].NameOnly);
            Assert.Equal(".bin", f.ImageFiles[0].Extension);
            Assert.Equal(0x11a9f90L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr).7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x21140dL, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.Null(f.Key);
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0xa46ba6d3U, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.Cue, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x71L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal(".cue", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr).cue", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal(".cue", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(1, f.IndexFile.Items.Length);
            Assert.Equal(IndexTrackBasicType.Cdi, f.IndexFile.Items[0].BasicType);
            Assert.Equal(0, f.IndexFile.Items[0].BlockIdx);
            Assert.Equal(0x930, f.IndexFile.Items[0].BlockSize);
            Assert.Equal(7875, f.IndexFile.Items[0].Blocks);
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
            Assert.Equal(0x11a9f90L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(1, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Mode2Raw, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr).bin", f.IndexFile.Items[0].FileName);
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
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", r.FinalName);
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
            Assert.Equal(ContainerType.Cue, t.ContainerType);
            Assert.Equal(SystemType.CDi, t.System);
            Assert.Equal(TaskType.Extract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "Hygen PQ-v Fbppre (Rhebcr)"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("Hygen PQ-v Fbppre (Rhebcr)", t.Name);
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

            string[] extractLines = File.ReadAllLines(Path.Combine(base.GetPath(), "Hygen_PQ_v_Fbppre_Rhebcr_7z_Extract_Tests.txt"));
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