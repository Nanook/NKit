
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_WiiU_Tests : WipedImageTestsBase
    {
        //Retail     / APP       / 60MiB   / CDN tmd, no tik, no cert, no h3 files
        [Fact]
        public void JvvH_Pqa_SMrebK_HFN_7z_ExtractForensic()
        {
            string fileName = @"JvvH_Pqa_SMrebK_HFN.7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "WiiU"));
            string outFolderName = $"WiiU_JvvH_Pqa_SMrebK_HFN_7z_ExtractForensic_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"../../../../../WipedImages/_keys";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Extract", @"f", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
            presets.System = SystemType.WiiU;

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
            Assert.Equal("JvvH_Pqa_SMrebK_HFN.7z//tmd.16 +10", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("JvvH_Pqa_SMrebK_HFN", f.CleanName);
            Assert.Equal("JvvH_Pqa_SMrebK_HFN", f.Name);
            Assert.Equal(SourceImageType.TmdApp, f.ImageType);
            Assert.Equal(SourceFileResult.Valid, f.Status);
            Assert.Equal(SystemType.WiiU, f.SystemType);
            Assert.False(f.IsArchive);
            Assert.True(f.IsArchived);
            Assert.False(f.IsDeleted);
            Assert.True(f.IsFolderMode);
            Assert.False(f.IsSplitArchive);
            Assert.True(f.IsSplitImage);
            Assert.Equal(0x0L, f.Length);
            Assert.Equal(10, f.ImageFiles.Length);
            Assert.Equal("0000000a", f.ImageFiles[0].FileName);
            Assert.Equal("0000000a", f.ImageFiles[0].NameOnly);
            Assert.Equal("", f.ImageFiles[0].Extension);
            Assert.Equal(0x8000L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal("0000000b", f.ImageFiles[1].FileName);
            Assert.Equal("0000000b", f.ImageFiles[1].NameOnly);
            Assert.Equal("", f.ImageFiles[1].Extension);
            Assert.Equal(0x8000L, f.ImageFiles[1].Size);
            Assert.True(f.ImageFiles[1].IsArchived);
            Assert.Equal("00000002", f.ImageFiles[2].FileName);
            Assert.Equal("00000002", f.ImageFiles[2].NameOnly);
            Assert.Equal("", f.ImageFiles[2].Extension);
            Assert.Equal(0x8000L, f.ImageFiles[2].Size);
            Assert.True(f.ImageFiles[2].IsArchived);
            Assert.Equal("0000000c", f.ImageFiles[3].FileName);
            Assert.Equal("0000000c", f.ImageFiles[3].NameOnly);
            Assert.Equal("", f.ImageFiles[3].Extension);
            Assert.Equal(0x30000L, f.ImageFiles[3].Size);
            Assert.True(f.ImageFiles[3].IsArchived);
            Assert.Equal("00000004", f.ImageFiles[4].FileName);
            Assert.Equal("00000004", f.ImageFiles[4].NameOnly);
            Assert.Equal("", f.ImageFiles[4].Extension);
            Assert.Equal(0xb30000L, f.ImageFiles[4].Size);
            Assert.True(f.ImageFiles[4].IsArchived);
            Assert.Equal("00000005", f.ImageFiles[5].FileName);
            Assert.Equal("00000005", f.ImageFiles[5].NameOnly);
            Assert.Equal("", f.ImageFiles[5].Extension);
            Assert.Equal(0x110000L, f.ImageFiles[5].Size);
            Assert.True(f.ImageFiles[5].IsArchived);
            Assert.Equal("00000006", f.ImageFiles[6].FileName);
            Assert.Equal("00000006", f.ImageFiles[6].NameOnly);
            Assert.Equal("", f.ImageFiles[6].Extension);
            Assert.Equal(0x110000L, f.ImageFiles[6].Size);
            Assert.True(f.ImageFiles[6].IsArchived);
            Assert.Equal("00000007", f.ImageFiles[7].FileName);
            Assert.Equal("00000007", f.ImageFiles[7].NameOnly);
            Assert.Equal("", f.ImageFiles[7].Extension);
            Assert.Equal(0x1250000L, f.ImageFiles[7].Size);
            Assert.True(f.ImageFiles[7].IsArchived);
            Assert.Equal("0000000d", f.ImageFiles[8].FileName);
            Assert.Equal("0000000d", f.ImageFiles[8].NameOnly);
            Assert.Equal("", f.ImageFiles[8].Extension);
            Assert.Equal(0x180000L, f.ImageFiles[8].Size);
            Assert.True(f.ImageFiles[8].IsArchived);
            Assert.Equal("0000000e", f.ImageFiles[9].FileName);
            Assert.Equal("0000000e", f.ImageFiles[9].NameOnly);
            Assert.Equal("", f.ImageFiles[9].Extension);
            Assert.Equal(0x1aa0000L, f.ImageFiles[9].Size);
            Assert.True(f.ImageFiles[9].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("JvvH_Pqa_SMrebK_HFN.7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("JvvH_Pqa_SMrebK_HFN", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x20ca13L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.NotNull(f.Key);
            Assert.Equal("7711F11B47F42D15B2CAB8845C574CED", f.Key.ToHexString());
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0xbf5aa595U, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.TmdApp, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x13e4L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal("", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("tmd.16", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("tmd.16", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal("", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(10, f.IndexFile.Items.Length);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[0].BasicType);
            Assert.Equal(0, f.IndexFile.Items[0].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[0].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[0].Blocks);
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
            Assert.Equal(0x8000L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(0, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("0000000a", f.IndexFile.Items[0].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[1].BasicType);
            Assert.Equal(0, f.IndexFile.Items[1].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[1].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[1].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[1].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[1].ChdSubType);
            Assert.False(f.IndexFile.Items[1].FileIsMissing);
            Assert.Equal(0x8000L, f.IndexFile.Items[1].ImageOffset);
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
            Assert.Equal(0, f.IndexFile.Items[1].Session);
            Assert.Equal(0x8000L, f.IndexFile.Items[1].Size);
            Assert.Equal(0x0, f.IndexFile.Items[1].SubSize);
            Assert.Equal(1, f.IndexFile.Items[1].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[1].TrackType);
            Assert.Null(f.IndexFile.Items[1].ChdTag);
            Assert.Null(f.IndexFile.Items[1].Comment);
            Assert.NotNull(f.IndexFile.Items[1].FileName);
            Assert.Equal("0000000b", f.IndexFile.Items[1].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[2].BasicType);
            Assert.Equal(0, f.IndexFile.Items[2].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[2].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[2].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[2].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[2].ChdSubType);
            Assert.False(f.IndexFile.Items[2].FileIsMissing);
            Assert.Equal(0x10000L, f.IndexFile.Items[2].ImageOffset);
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
            Assert.Equal(0, f.IndexFile.Items[2].Session);
            Assert.Equal(0x8000L, f.IndexFile.Items[2].Size);
            Assert.Equal(0x0, f.IndexFile.Items[2].SubSize);
            Assert.Equal(2, f.IndexFile.Items[2].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[2].TrackType);
            Assert.Null(f.IndexFile.Items[2].ChdTag);
            Assert.Null(f.IndexFile.Items[2].Comment);
            Assert.NotNull(f.IndexFile.Items[2].FileName);
            Assert.Equal("00000002", f.IndexFile.Items[2].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[3].BasicType);
            Assert.Equal(0, f.IndexFile.Items[3].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[3].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[3].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[3].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[3].ChdSubType);
            Assert.False(f.IndexFile.Items[3].FileIsMissing);
            Assert.Equal(0x18000L, f.IndexFile.Items[3].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[3].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[3].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[3].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[3].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[3].Session);
            Assert.Equal(0x30000L, f.IndexFile.Items[3].Size);
            Assert.Equal(0x0, f.IndexFile.Items[3].SubSize);
            Assert.Equal(3, f.IndexFile.Items[3].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[3].TrackType);
            Assert.Null(f.IndexFile.Items[3].ChdTag);
            Assert.Null(f.IndexFile.Items[3].Comment);
            Assert.NotNull(f.IndexFile.Items[3].FileName);
            Assert.Equal("0000000c", f.IndexFile.Items[3].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[4].BasicType);
            Assert.Equal(0, f.IndexFile.Items[4].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[4].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[4].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[4].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[4].ChdSubType);
            Assert.False(f.IndexFile.Items[4].FileIsMissing);
            Assert.Equal(0x48000L, f.IndexFile.Items[4].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[4].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[4].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[4].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[4].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[4].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[4].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[4].Session);
            Assert.Equal(0xb30000L, f.IndexFile.Items[4].Size);
            Assert.Equal(0x0, f.IndexFile.Items[4].SubSize);
            Assert.Equal(4, f.IndexFile.Items[4].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[4].TrackType);
            Assert.Null(f.IndexFile.Items[4].ChdTag);
            Assert.Null(f.IndexFile.Items[4].Comment);
            Assert.NotNull(f.IndexFile.Items[4].FileName);
            Assert.Equal("00000004", f.IndexFile.Items[4].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[5].BasicType);
            Assert.Equal(0, f.IndexFile.Items[5].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[5].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[5].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[5].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[5].ChdSubType);
            Assert.False(f.IndexFile.Items[5].FileIsMissing);
            Assert.Equal(0xb78000L, f.IndexFile.Items[5].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[5].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[5].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[5].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[5].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[5].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[5].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[5].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[5].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[5].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[5].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[5].Session);
            Assert.Equal(0x110000L, f.IndexFile.Items[5].Size);
            Assert.Equal(0x0, f.IndexFile.Items[5].SubSize);
            Assert.Equal(5, f.IndexFile.Items[5].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[5].TrackType);
            Assert.Null(f.IndexFile.Items[5].ChdTag);
            Assert.Null(f.IndexFile.Items[5].Comment);
            Assert.NotNull(f.IndexFile.Items[5].FileName);
            Assert.Equal("00000005", f.IndexFile.Items[5].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[6].BasicType);
            Assert.Equal(0, f.IndexFile.Items[6].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[6].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[6].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[6].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[6].ChdSubType);
            Assert.False(f.IndexFile.Items[6].FileIsMissing);
            Assert.Equal(0xc88000L, f.IndexFile.Items[6].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[6].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[6].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[6].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[6].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[6].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[6].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[6].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[6].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[6].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[6].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[6].Session);
            Assert.Equal(0x110000L, f.IndexFile.Items[6].Size);
            Assert.Equal(0x0, f.IndexFile.Items[6].SubSize);
            Assert.Equal(6, f.IndexFile.Items[6].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[6].TrackType);
            Assert.Null(f.IndexFile.Items[6].ChdTag);
            Assert.Null(f.IndexFile.Items[6].Comment);
            Assert.NotNull(f.IndexFile.Items[6].FileName);
            Assert.Equal("00000006", f.IndexFile.Items[6].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[7].BasicType);
            Assert.Equal(0, f.IndexFile.Items[7].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[7].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[7].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[7].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[7].ChdSubType);
            Assert.False(f.IndexFile.Items[7].FileIsMissing);
            Assert.Equal(0xd98000L, f.IndexFile.Items[7].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[7].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[7].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[7].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[7].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[7].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[7].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[7].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[7].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[7].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[7].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[7].Session);
            Assert.Equal(0x1250000L, f.IndexFile.Items[7].Size);
            Assert.Equal(0x0, f.IndexFile.Items[7].SubSize);
            Assert.Equal(7, f.IndexFile.Items[7].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[7].TrackType);
            Assert.Null(f.IndexFile.Items[7].ChdTag);
            Assert.Null(f.IndexFile.Items[7].Comment);
            Assert.NotNull(f.IndexFile.Items[7].FileName);
            Assert.Equal("00000007", f.IndexFile.Items[7].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[8].BasicType);
            Assert.Equal(0, f.IndexFile.Items[8].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[8].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[8].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[8].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[8].ChdSubType);
            Assert.False(f.IndexFile.Items[8].FileIsMissing);
            Assert.Equal(0x1fe8000L, f.IndexFile.Items[8].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[8].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[8].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[8].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[8].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[8].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[8].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[8].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[8].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[8].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[8].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[8].Session);
            Assert.Equal(0x180000L, f.IndexFile.Items[8].Size);
            Assert.Equal(0x0, f.IndexFile.Items[8].SubSize);
            Assert.Equal(8, f.IndexFile.Items[8].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[8].TrackType);
            Assert.Null(f.IndexFile.Items[8].ChdTag);
            Assert.Null(f.IndexFile.Items[8].Comment);
            Assert.NotNull(f.IndexFile.Items[8].FileName);
            Assert.Equal("0000000d", f.IndexFile.Items[8].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[9].BasicType);
            Assert.Equal(0, f.IndexFile.Items[9].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[9].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[9].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[9].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[9].ChdSubType);
            Assert.False(f.IndexFile.Items[9].FileIsMissing);
            Assert.Equal(0x2168000L, f.IndexFile.Items[9].ImageOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[9].PhysicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[9].LogicalOffset);
            Assert.Equal(0x0L, f.IndexFile.Items[9].LogicalSize);
            Assert.Equal(0x0, f.IndexFile.Items[9].Pad);
            Assert.Equal(0x0, f.IndexFile.Items[9].PadSize);
            Assert.Equal(0x0, f.IndexFile.Items[9].PostGap);
            Assert.Equal(0x0, f.IndexFile.Items[9].PreGap);
            Assert.Equal(0x0, f.IndexFile.Items[9].PreGapDataSize);
            Assert.Equal(0x0, f.IndexFile.Items[9].PreGapSubSize);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[9].PreGapType);
            Assert.Equal(0, f.IndexFile.Items[9].Session);
            Assert.Equal(0x1aa0000L, f.IndexFile.Items[9].Size);
            Assert.Equal(0x0, f.IndexFile.Items[9].SubSize);
            Assert.Equal(9, f.IndexFile.Items[9].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[9].TrackType);
            Assert.Null(f.IndexFile.Items[9].ChdTag);
            Assert.Null(f.IndexFile.Items[9].Comment);
            Assert.NotNull(f.IndexFile.Items[9].FileName);
            Assert.Equal("0000000e", f.IndexFile.Items[9].FileName);
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
            Assert.Equal("Extract-WiiU", i.Name);
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
            Assert.Equal("JvvH_Pqa_SMrebK_HFN", r.FinalName);
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
            Assert.Equal(ContainerType.TmdApp, t.ContainerType);
            Assert.Equal(SystemType.WiiU, t.System);
            Assert.Equal(TaskType.Extract, t.Task);
            Assert.Equal(0x0L, t.Size);
            Assert.Equal(0x00000000U, t.CRC);
            Assert.Equal(0x00000000U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal(Path.Combine(basePath, "JvvH_Pqa_SMrebK_HFN"), t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("JvvH_Pqa_SMrebK_HFN", t.Name);
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
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.NotNull(t.Key);
            Assert.Equal("7711F11B47F42D15B2CAB8845C574CED", t.Key.ToHexString());
            Assert.NotNull(t.StepFiles);
            Assert.Equal(0, t.StepFiles.Count);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.Null(t.Scan);

            ////////////////////////////////////////
            // Extracted Tracks
            ////////////////////////////////////////

            string[] extractLines = File.ReadAllLines(Path.Combine(base.GetPath(), "JvvH_Pqa_SMrebK_HFN_7z_ExtractForensic_Tests.txt"));
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