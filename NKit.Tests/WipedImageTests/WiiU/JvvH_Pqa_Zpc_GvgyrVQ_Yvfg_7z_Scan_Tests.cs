
using Nanook.NKit;
using System;
using System.IO;
using Xunit;

namespace NKit.Tests.Full.Wiped
{
    public partial class WipedImage_WiiU_Tests : WipedImageTestsBase
    {
        //Update     / APP       / 284KiB  / tmd.6 Update tmd+cetk files. Multi versions in archive
        [Fact]
        public void JvvH_Pqa_Zpc_GvgyrVQ_Yvfg_7z_Scan()
        {
            string fileName = @"JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.7z";
            string inPath = Path.GetFullPath(Path.Combine(@"../../../../../WipedImages", "WiiU"));
            string outFolderName = $"WiiU_JvvH_Pqa_Zpc_GvgyrVQ_Yvfg_7z_Scan_{Guid.NewGuid():N}";
            string basePath = Directory.CreateDirectory(Path.Combine(".", outFolderName)).FullName;
            string dats = @"";
            string keys = @"../../../../../WipedImages/_keys";
            string fixInfo = @"";
            string fixFiles = @"";

            SystemPresetSettings presets = base.CreatePresets("Scan", @"", inPath, fileName, outFolderName, dats, keys, fixInfo, fixFiles);
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
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.7z//tmd.6 +4", f.FriendlyFullPath.Replace(f.BasePath, ""));
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", f.CleanName);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", f.Name);
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
            Assert.Equal(4, f.ImageFiles.Length);
            Assert.Equal("00000009", f.ImageFiles[0].FileName);
            Assert.Equal("00000009", f.ImageFiles[0].NameOnly);
            Assert.Equal("", f.ImageFiles[0].Extension);
            Assert.Equal(0x160L, f.ImageFiles[0].Size);
            Assert.True(f.ImageFiles[0].IsArchived);
            Assert.Equal("0000000a", f.ImageFiles[1].FileName);
            Assert.Equal("0000000a", f.ImageFiles[1].NameOnly);
            Assert.Equal("", f.ImageFiles[1].Extension);
            Assert.Equal(0x290L, f.ImageFiles[1].Size);
            Assert.True(f.ImageFiles[1].IsArchived);
            Assert.Equal("00000002", f.ImageFiles[2].FileName);
            Assert.Equal("00000002", f.ImageFiles[2].NameOnly);
            Assert.Equal("", f.ImageFiles[2].Extension);
            Assert.Equal(0xef0L, f.ImageFiles[2].Size);
            Assert.True(f.ImageFiles[2].IsArchived);
            Assert.Equal("0000000b", f.ImageFiles[3].FileName);
            Assert.Equal("0000000b", f.ImageFiles[3].NameOnly);
            Assert.Equal("", f.ImageFiles[3].Extension);
            Assert.Equal(0x10000L, f.ImageFiles[3].Size);
            Assert.True(f.ImageFiles[3].IsArchived);
            Assert.Equal(SourceArchiveType.SevenZip, f.ArchiveType);
            Assert.NotNull(f.ArchiveFiles);
            Assert.Equal(1, f.ArchiveFiles.Length);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.7z", f.ArchiveFiles[0].FileName);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", f.ArchiveFiles[0].NameOnly);
            Assert.Equal(".7z", f.ArchiveFiles[0].Extension);
            Assert.Equal(0x12c86L, f.ArchiveFiles[0].Size);
            Assert.False(f.ArchiveFiles[0].IsArchived);
            Assert.NotNull(f.Key);
            Assert.Equal("75B928E955968EB352E6B19A9B2E4EBE", f.Key.ToHexString());
            // f.IndexFile IndexFile Test
            Assert.NotNull(f.IndexFile);
            Assert.Equal(0x45f6fd99U, f.IndexFile.Crc);
            Assert.Equal(IndexFileType.TmdApp, f.IndexFile.FileType);
            Assert.False(f.IndexFile.IsArchived);
            Assert.False(f.IndexFile.IsTemp);
            Assert.Equal(0x0L, f.IndexFile.Offset);
            Assert.Equal(0x12c4L, f.IndexFile.Size);
            Assert.False(f.IndexFile.WiiUFstMismatch);
            Assert.NotNull(f.IndexFile.Extension);
            Assert.Equal("", f.IndexFile.Extension);
            Assert.NotNull(f.IndexFile.FileName);
            Assert.Equal("tmd.6", f.IndexFile.FileName);
            Assert.NotNull(f.IndexFile.NameOnly);
            Assert.Equal("tmd.6", f.IndexFile.NameOnly);
            Assert.NotNull(f.IndexFile.Postfix);
            Assert.Equal("", f.IndexFile.Postfix);
            Assert.NotNull(f.IndexFile.Path);
            Assert.Equal("", f.IndexFile.Path);
            Assert.Equal(4, f.IndexFile.Items.Length);
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
            Assert.Equal(0x160L, f.IndexFile.Items[0].Size);
            Assert.Equal(0x0, f.IndexFile.Items[0].SubSize);
            Assert.Equal(0, f.IndexFile.Items[0].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[0].TrackType);
            Assert.Null(f.IndexFile.Items[0].ChdTag);
            Assert.Null(f.IndexFile.Items[0].Comment);
            Assert.NotNull(f.IndexFile.Items[0].FileName);
            Assert.Equal("00000009", f.IndexFile.Items[0].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[1].BasicType);
            Assert.Equal(0, f.IndexFile.Items[1].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[1].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[1].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[1].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[1].ChdSubType);
            Assert.False(f.IndexFile.Items[1].FileIsMissing);
            Assert.Equal(0x160L, f.IndexFile.Items[1].ImageOffset);
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
            Assert.Equal(0x290L, f.IndexFile.Items[1].Size);
            Assert.Equal(0x0, f.IndexFile.Items[1].SubSize);
            Assert.Equal(1, f.IndexFile.Items[1].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[1].TrackType);
            Assert.Null(f.IndexFile.Items[1].ChdTag);
            Assert.Null(f.IndexFile.Items[1].Comment);
            Assert.NotNull(f.IndexFile.Items[1].FileName);
            Assert.Equal("0000000a", f.IndexFile.Items[1].FileName);
            Assert.Equal(IndexTrackBasicType.Unknown, f.IndexFile.Items[2].BasicType);
            Assert.Equal(0, f.IndexFile.Items[2].BlockIdx);
            Assert.Equal(0x0, f.IndexFile.Items[2].BlockSize);
            Assert.Equal(0, f.IndexFile.Items[2].Blocks);
            Assert.Equal(MediaType.Unknown, f.IndexFile.Items[2].ChdMediaType);
            Assert.Equal(CdSubType.None, f.IndexFile.Items[2].ChdSubType);
            Assert.False(f.IndexFile.Items[2].FileIsMissing);
            Assert.Equal(0x3f0L, f.IndexFile.Items[2].ImageOffset);
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
            Assert.Equal(0xef0L, f.IndexFile.Items[2].Size);
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
            Assert.Equal(0x12e0L, f.IndexFile.Items[3].ImageOffset);
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
            Assert.Equal(0x10000L, f.IndexFile.Items[3].Size);
            Assert.Equal(0x0, f.IndexFile.Items[3].SubSize);
            Assert.Equal(3, f.IndexFile.Items[3].TrackIndex);
            Assert.Equal(IndexTrackType.Unknown, f.IndexFile.Items[3].TrackType);
            Assert.Null(f.IndexFile.Items[3].ChdTag);
            Assert.Null(f.IndexFile.Items[3].Comment);
            Assert.NotNull(f.IndexFile.Items[3].FileName);
            Assert.Equal("0000000b", f.IndexFile.Items[3].FileName);
            Assert.Equal(1, f.IndexFile.Additional.Count);
            Assert.Equal(0xa50L, f.IndexFile.Additional[0].Size);
            Assert.NotNull(f.IndexFile.Additional[0].FileName);
            Assert.Equal("cetk.6", f.IndexFile.Additional[0].FileName);

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
            Assert.False(i.WriteImage);
            Assert.Equal("Scan-Image", i.Name);
            Assert.Equal(OutputType.Scan, i.OutputType);
            Assert.False(i.ReqChk);
            Assert.False(i.ReqPatch);
            Assert.Equal(TaskType.Scan, i.StepType);
            Assert.Equal(VerifyMethod.NoVerify, i.VerifyMethod);
            Assert.Null(i.VerifyChecksums);
            Assert.NotNull(i.Config);
            Assert.Equal("scan", i.Config);
            Assert.NotNull(i.ImageConfig);
            Assert.Equal("scan", i.ImageConfig);
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
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.nkit.yaml", r.FinalName);
            Assert.Null(r.ChkCompared);
            Assert.Null(r.InFileParts);
            Assert.NotNull(r.OutFileParts);
            Assert.Equal(1, r.OutFileParts.Length);
            Assert.Equal(0x112e0L, r.OutFileParts[0].Size);
            Assert.NotNull(r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.Equal("", r.OutFileParts[0].Checksums.ToString(true, true));
            Assert.NotNull(r.OutFileParts[0].FileName);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", r.OutFileParts[0].FileName);
            Assert.NotNull(r.ResultCrc);
            Assert.Equal(0xe8915095U, r.ResultCrc.Value);
            Assert.NotNull(r.ResultSize);
            Assert.Equal(0x112e0L, r.ResultSize.Value);
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
            Assert.Equal(ContainerType.TmdApp, t.ContainerType);
            Assert.Equal(SystemType.WiiU, t.System);
            Assert.Equal(TaskType.Scan, t.Task);
            Assert.Equal(0x112e0L, t.Size);
            Assert.Equal(0xe8915095U, t.CRC);
            Assert.Equal(0x366f29e6U, t.DecryptedCrc);
            Assert.Equal(VerifyResult.Unverified, t.VerifyResult);
            Assert.Equal(inPath.TrimEnd('\\', '/'), t.InFilePath.TrimEnd('\\', '/'));
            Assert.NotNull(t.OutPath);
            Assert.Equal("", t.OutPath);
            Assert.NotNull(t.Name);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", t.Name);
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
            Assert.Equal(Path.Combine(basePath, "JvvH_Pqa_Zpc_GvgyrVQ_Yvfg.nkit.yaml"), t.OutScanFilePath);
            Assert.False(t.HasEncryption);
            Assert.True(t.SupportsEncryption);
            Assert.False(t.ImageSkipped);
            Assert.NotNull(t.Key);
            Assert.Equal("75B928E955968EB352E6B19A9B2E4EBE", t.Key.ToHexString());
            Assert.NotNull(t.StepFiles);
            Assert.Equal(1, t.StepFiles.Count);
            Assert.Equal(0x112e0L, t.StepFiles[0].Size);
            Assert.False(t.StepFiles[0].IsIndex);
            Assert.True(t.StepFiles[0].IsImageName);
            Assert.NotNull(t.StepFiles[0].Checksums.ToString(true, true));
            Assert.Equal("", t.StepFiles[0].Checksums.ToString(true, true));
            Assert.NotNull(t.StepFiles[0].FileName);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", t.StepFiles[0].FileName);

            ////////////////////////////////////////
            // Result Scan
            ////////////////////////////////////////
            Assert.NotNull(t.Scan);
            Assert.NotNull(t.Scan.Name);
            Assert.Equal("JvvH_Pqa_Zpc_GvgyrVQ_Yvfg", t.Scan.Name);
            Assert.Equal(SystemType.WiiU, f.SystemType);
            Assert.Equal(0xe8915095U, t.Scan.Crc);
            Assert.Equal(0x366f29e6U, t.Scan.CrcDecrypted);
            Assert.Equal(0x112e0L, t.Scan.Size);
            Assert.Equal(16, t.Scan.VirtualFsTotalFileCount);
            Assert.Equal(12, t.Scan.VirtualFsTotalFoldersCount);
            Assert.NotNull(t.Scan.Properties["System"].ToXmlValue());
            Assert.Equal("WiiU", t.Scan.Properties["System"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Media"].ToXmlValue());
            Assert.Equal("CDN", t.Scan.Properties["Media"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Type"].ToXmlValue());
            Assert.Equal("Retail", t.Scan.Properties["Type"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["Size"].ToXmlValue());
            Assert.Equal("0000112E0", t.Scan.Properties["Size"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["CRC"].ToXmlValue());
            Assert.Equal("E8915095", t.Scan.Properties["CRC"].ToXmlValue());
            Assert.NotNull(t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal("366F29E6", t.Scan.Properties["DecryptedCRC"].ToXmlValue());
            Assert.Equal(4, t.Scan.Areas.Count);
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TmdVersion"].ToXmlValue());
            Assert.Equal("6", t.Scan.Areas[0].AreaInfo.Properties["TmdVersion"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["ContentHeaders"].ToXmlValue());
            Assert.Equal("4", t.Scan.Areas[0].AreaInfo.Properties["ContentHeaders"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["App"].ToXmlValue());
            Assert.Equal("00000009.app", t.Scan.Areas[0].AreaInfo.Properties["App"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.Equal("00000009", t.Scan.Areas[0].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleId"].ToXmlValue());
            Assert.Equal("5550556E65513555", t.Scan.Areas[0].AreaInfo.Properties["TitleId"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.Equal("Valid", t.Scan.Areas[0].AreaInfo.Properties["Signed"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[0].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[0].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[0].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[0].AreaInfo.Properties["MissingFiles"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[0].AreaInfo.Properties["MissingFiles"].ToXmlValue());
            Assert.Equal(AreaType.FstBlock, t.Scan.Areas[0].Type);
            Assert.Equal(0x4efe8817U, t.Scan.Areas[0].Crc);
            Assert.Equal(0x061cf0f3U, t.Scan.Areas[0].CrcDecrypted);
            Assert.Equal(0x160L, t.Scan.Areas[0].Size);
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.Equal("1", t.Scan.Areas[1].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["App"].ToXmlValue());
            Assert.Equal("0000000a.app", t.Scan.Areas[1].AreaInfo.Properties["App"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.Equal("0000000a", t.Scan.Areas[1].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[1].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[1].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000000", t.Scan.Areas[1].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[1].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[1].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[1].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[1].Type);
            Assert.Equal(0x3696ac7bU, t.Scan.Areas[1].Crc);
            Assert.Equal(0x24e9d05cU, t.Scan.Areas[1].CrcDecrypted);
            Assert.Equal(0x290L, t.Scan.Areas[1].Size);
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.Equal("2", t.Scan.Areas[2].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["App"].ToXmlValue());
            Assert.Equal("00000002.app", t.Scan.Areas[2].AreaInfo.Properties["App"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.Equal("00000002", t.Scan.Areas[2].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[2].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00008000", t.Scan.Areas[2].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000000", t.Scan.Areas[2].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[2].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[2].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[2].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[2].Type);
            Assert.Equal(0x5029ef34U, t.Scan.Areas[2].Crc);
            Assert.Equal(0xf08ff47dU, t.Scan.Areas[2].CrcDecrypted);
            Assert.Equal(0xef0L, t.Scan.Areas[2].Size);
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["Partition"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.Equal("3", t.Scan.Areas[3].AreaInfo.Properties["ContentIndex"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["App"].ToXmlValue());
            Assert.Equal("0000000b.app", t.Scan.Areas[3].AreaInfo.Properties["App"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.Equal("0000000b", t.Scan.Areas[3].AreaInfo.Properties["Filename"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.Equal("true", t.Scan.Areas[3].AreaInfo.Properties["Encrypted"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.Equal("00010000", t.Scan.Areas[3].AreaInfo.Properties["BlockSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.Equal("00000400", t.Scan.Areas[3].AreaInfo.Properties["HashSize"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.Equal("MissingH3", t.Scan.Areas[3].AreaInfo.Properties["HashRoot"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.Equal("CECEE288", t.Scan.Areas[3].AreaInfo.Properties["CommonKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.Equal("ECBB4B55", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyCrc"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["TitleKeyMissing"].ToXmlValue());
            Assert.NotNull(t.Scan.Areas[3].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal("", t.Scan.Areas[3].AreaInfo.Properties["SiTitleId"].ToXmlValue());
            Assert.Equal(AreaType.FileSystem, t.Scan.Areas[3].Type);
            Assert.Equal(0xfb5bbedeU, t.Scan.Areas[3].Crc);
            Assert.Equal(0x1bb6e297U, t.Scan.Areas[3].CrcDecrypted);
            Assert.Equal(0x10000L, t.Scan.Areas[3].Size);

            base.Complete();
        }
    }
}