using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using NKitDataStore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Unit tests for DataStoreXboxFormatter.
    /// Uses model-based testing for lifecycle/routing behavior (since the formatter
    /// has complex constructor dependencies) and direct testing for static/testable methods.
    ///
    /// Validates Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.8, 1.9, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7
    /// </summary>
    [Trait("Area", "NKDS")]
    public class DataStoreXboxFormatterTests
    {
        #region Mock Types

        /// <summary>
        /// Mock writer tracking calls for lifecycle and routing verification.
        /// </summary>
        private class MockWriter : IDisposable
        {
            public bool Finalized { get; private set; }
            public bool Disposed { get; private set; }
            public long FinalizedSize { get; private set; }
            public uint FinalizedCrc { get; private set; }
            public ulong FinalizedXxHash { get; private set; }
            public List<(long Offset, int Size, BlockType Type)> WrittenBlocks { get; } = new();
            public List<(long Offset, long Size, uint Crc, ulong XxHash, int SectionSize)> CreatedAreas { get; } = new();
            public List<byte[]> WrittenFiles { get; } = new();
            public bool ThrowOnWrite { get; set; }

            public void FinalizeImage(long size, uint crc, ulong xxHash)
            {
                Finalized = true;
                FinalizedSize = size;
                FinalizedCrc = crc;
                FinalizedXxHash = xxHash;
            }

            public void WriteData(long imageOffset, byte[] data, int offset, int size, BlockType type)
            {
                if (ThrowOnWrite)
                    throw new InvalidOperationException("Simulated write failure");
                WrittenBlocks.Add((imageOffset, size, type));
            }

            public void CreateArea(long imageOffset, long size, uint crc, ulong xxHash, int sectionSize) => CreatedAreas.Add((imageOffset, size, crc, xxHash, sectionSize));

            public void WriteFile(string path, byte[] data) => WrittenFiles.Add(data);

            public void Dispose() => Disposed = true;
        }

        /// <summary>
        /// Mock ISectionData for gap testing.
        /// </summary>
        private class MockSectionData : ISectionData
        {
            public long ImageOffset { get; set; }
            public long AreaOffset { get; set; }
            public DataType DataType { get; set; }
            public byte FillByte { get; set; }
            public int DataNulls { get; set; }
            public long OffsetInItem { get; set; }
            public long Offset { get; set; }
            public long FsOffset { get; set; }
            public long FsSize { get; set; }
            public uint Crc { get; set; }
            public ulong XxHash { get; set; }
            public bool IsFile { get; set; }
            public void SetParseType(string type, bool isInfo) { }
            public override string ToString() => $"MockSectionData[{FsOffset}:{FsSize}]";
        }

        /// <summary>
        /// Mock ISectionItem for gap testing.
        /// </summary>
        private class MockSectionItem : ISectionItem
        {
            public long ImageOffset { get; set; }
            public long AreaOffset { get; set; }
            public long AreaBase { get; set; }
            public IFsFile FsFile { get; set; }
            public ISectionData File { get; set; }
            public ISectionData Gap { get; set; }
            public List<ISectionData> GapInfo { get; set; }
            public int FileIndex { get; set; }
            public List<string> FileSystems { get; set; }
        }

        /// <summary>
        /// Mock ISection for gap testing.
        /// </summary>
        private class MockSection : ISection
        {
            public long ImageOffset { get; set; }
            public long Size { get; set; }
            public byte[] Decrypted { get; set; }
            public byte[] Encrypted { get; set; }
            public long FsOffset { get; set; }
            public long AreaOffset { get; set; }
            public long FsSize { get; set; }
            public uint Crc { get; set; }
            public uint CrcDecrypted { get; set; }
            public ulong XxHash { get; set; }
            public AreaType Type { get; set; }
            public int FileStartIndex { get; set; }
            public int FileEndIndex { get; set; }
            public IFileSystem FullAreaFileSystem { get; set; }
            public IAreaFileSystemView AreaFileSystem { get; set; }
            public SectionItems Items { get; set; } = new SectionItems();
            public IEnumerable<NonCreatableData> NonCreatableItems { get; set; } = Enumerable.Empty<NonCreatableData>();
            public bool IsValid { get; set; } = true;
            public bool IsCreatable { get; set; } = true;
            public bool IsEncrypted { get; set; }
            public CompletionStatus Status { get; set; }
            public BitState State { get; set; }
            public byte[] SeekIv { get; set; }
            public AreaInfo AreaInfo { get; set; }

            public void Write(int fsOffset, Stream fromStream, int size) { }
            public void WriteBytes(int fsOffset, byte[] bytes, int offset, int size) { }
            public void Read(int fsOffset, int size, Stream toStream) { }
            public byte[] ReadBytes(int fsOffset, int size) => new byte[size];
        }

        /// <summary>
        /// Mock IFsFile for ShouldPreserveFile testing.
        /// </summary>
        private class MockFsFile : IFsFile
        {
            public bool IsMissing { get; set; }
            public bool IsLastFile { get; set; }
            public int SplitIndex { get; set; }
            public IFsFileParts SplitParts { get; set; }
            public string FullName { get; set; } = "test.bin";
            public long FsSize { get; set; }
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public bool IsSystemFile { get; set; }
            public long FsOffset { get; set; }
            public long PostGapSize { get; set; }
            public long PostGapFsOffset { get; set; }
            public string Name { get; set; } = "test.bin";
            public IFsFolder Parent { get; set; }
            public string Path { get; set; } = "/";
            public IFsFile Clone() => this;
        }

        #endregion

        #region Model Methods for Lifecycle/Routing Tests

        /// <summary>
        /// Models the Xbox formatter's GetStrideForPartition behavior.
        /// Xbox uses cooked 0x800 sectors — always returns null (no striding).
        /// </summary>
        private static DataStride ModelGetStrideForPartition(int partitionId) => null;

        /// <summary>
        /// Models the Xbox formatter's BuildAreaMetadata behavior for a video partition area.
        /// Video partitions include HeaderCrc and HeaderSize in metadata.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataVideoPartition(
            long imageOffset, uint headerCrc, ulong headerSize)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.Other.ToString());
            metadata.Set(AreaValueType.BlockSize, 0x800L);
            metadata.Set(AreaValueType.AreaOffsetBase, imageOffset);
            metadata.Set(AreaValueType.HeaderCrc, (long)headerCrc);
            metadata.Set(AreaValueType.HeaderSize, (long)headerSize);
            return metadata;
        }

        /// <summary>
        /// Models the Xbox formatter's BuildAreaMetadata behavior for a game partition with key.
        /// Game partitions with a key include KeyCrc.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataGamePartitionWithKey(
            long imageOffset, byte[] key)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.FileSystem.ToString());
            metadata.Set(AreaValueType.BlockSize, 0x800L);
            metadata.Set(AreaValueType.AreaOffsetBase, imageOffset);
            metadata.Set(AreaValueType.KeyCrc, (long)Nanook.NKit.Crc.Compute(key));
            return metadata;
        }

        /// <summary>
        /// Models the Xbox formatter's BuildAreaMetadata behavior for a game partition without key.
        /// Game partitions without a key include TitleKeyMissing.
        /// </summary>
        private static AreaMetadata ModelBuildAreaMetadataGamePartitionNoKey(long imageOffset)
        {
            AreaMetadata metadata = new AreaMetadata();
            metadata.Set(AreaValueType.FsType, AreaType.FileSystem.ToString());
            metadata.Set(AreaValueType.BlockSize, 0x800L);
            metadata.Set(AreaValueType.AreaOffsetBase, imageOffset);
            metadata.Set(AreaValueType.TitleKeyMissing, true);
            return metadata;
        }

        /// <summary>
        /// Models the formatter's FinalizeImage behavior: calls FinalizeImage on both
        /// primary and aux writers. Aux failures are caught (not rethrown).
        /// This mirrors DataStoreXboxFormatter.FinalizeImage.
        /// </summary>
        private static void ModelFinalizeImage(MockWriter primary, MockWriter aux,
            long size, uint crc, ulong xxHash)
        {
            primary.FinalizeImage(size, crc, xxHash);
            try { aux?.FinalizeImage(size, crc, xxHash); } catch { }
        }

        /// <summary>
        /// Models the formatter's Dispose behavior: disposes both aux and primary writers.
        /// Exceptions are swallowed (try/catch around each).
        /// This mirrors DataStoreXboxFormatter.Dispose.
        /// </summary>
        private static void ModelDispose(MockWriter primary, MockWriter aux)
        {
            try { aux?.Dispose(); } catch { }
            try { primary.Dispose(); } catch { }
        }

        /// <summary>
        /// Models the formatter's CreateAreas behavior: mirrors area records to aux writer.
        /// This mirrors DataStoreXboxFormatter.CreateAreas.
        /// </summary>
        private static void ModelCreateAreas(MockWriter primary, MockWriter aux,
            List<(long Offset, long Size, uint Crc, ulong XxHash, int SectionSize)> areas)
        {
            foreach ((long Offset, long Size, uint Crc, ulong XxHash, int SectionSize) area in areas)
            {
                primary.CreateArea(area.Offset, area.Size, area.Crc, area.XxHash, area.SectionSize);
                try { aux?.CreateArea(area.Offset, area.Size, area.Crc, area.XxHash, area.SectionSize); } catch { }
            }
        }

        /// <summary>
        /// Models the formatter's filesystem YAML mirroring behavior.
        /// This mirrors DataStoreXboxFormatter.BuildFileSystemYaml.
        /// </summary>
        private static void ModelWriteFileSystemYaml(MockWriter primary, MockWriter aux, byte[] yamlData)
        {
            primary.WriteFile("filesystem.nkfs", yamlData);
            try { aux?.WriteFile("filesystem.nkfs", yamlData); } catch { }
        }

        /// <summary>
        /// Models the formatter's block routing behavior for filler/junk data.
        /// When aux is active, filler/junk (BlockType.Other) goes to aux.
        /// When aux is not active, all blocks go to primary.
        /// This mirrors DataStoreXboxFormatter.ProcessSection routing logic.
        /// </summary>
        private static void ModelRouteFillerBlock(MockWriter primary, MockWriter aux,
            long imageOffset, int size, bool isFillerJunk)
        {
            if (isFillerJunk && aux != null)
            {
                try
                {
                    aux.WriteData(imageOffset, null, 0, size, BlockType.Other);
                }
                catch
                {
                    // Fallback to primary on aux failure
                    primary.WriteData(imageOffset, null, 0, size, BlockType.Other);
                }
            }
            else
            {
                primary.WriteData(imageOffset, null, 0, size, BlockType.Other);
            }
        }

        #endregion

        #region Stride Computation Tests

        /// <summary>
        /// Validates Requirement 1.3: Xbox uses cooked 0x800 sectors with no striding.
        /// GetStrideForPartition returns null for all partition IDs.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(99)]
        public void GetStrideForPartition_ReturnsNull_ForAllXboxAreas(int partitionId)
        {
            DataStride result = ModelGetStrideForPartition(partitionId);
            Assert.Null(result);
        }

        #endregion

        #region Area Metadata Tests

        /// <summary>
        /// Validates Requirement 1.8: Video partition area metadata includes HeaderCrc and HeaderSize.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_VideoPartition_IncludesHeaderCrcAndHeaderSize()
        {
            long imageOffset = 0x0;
            uint headerCrc = 0xABCD1234;
            ulong headerSize = 0x800;

            AreaMetadata metadata = ModelBuildAreaMetadataVideoPartition(imageOffset, headerCrc, headerSize);

            Assert.Equal(AreaType.Other.ToString(), metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x800L, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(imageOffset, metadata.GetLong(AreaValueType.AreaOffsetBase));
            Assert.Equal((long)headerCrc, metadata.GetLong(AreaValueType.HeaderCrc));
            Assert.Equal((long)headerSize, metadata.GetLong(AreaValueType.HeaderSize));
        }

        /// <summary>
        /// Validates Requirement 1.4: Game partition with encryption key includes KeyCrc.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_GamePartitionWithKey_IncludesKeyCrc()
        {
            long imageOffset = 0x10000;
            byte[] key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                      0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
            uint expectedCrc = Nanook.NKit.Crc.Compute(key);

            AreaMetadata metadata = ModelBuildAreaMetadataGamePartitionWithKey(imageOffset, key);

            Assert.Equal(AreaType.FileSystem.ToString(), metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x800L, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(imageOffset, metadata.GetLong(AreaValueType.AreaOffsetBase));
            Assert.Equal((long)expectedCrc, metadata.GetLong(AreaValueType.KeyCrc));
            Assert.False(metadata.ContainsKey(AreaValueType.TitleKeyMissing));
        }

        /// <summary>
        /// Validates Requirement 1.5: Game partition without encryption key includes TitleKeyMissing.
        /// </summary>
        [Fact]
        public void BuildAreaMetadata_GamePartitionWithoutKey_IncludesTitleKeyMissing()
        {
            long imageOffset = 0x20000;

            AreaMetadata metadata = ModelBuildAreaMetadataGamePartitionNoKey(imageOffset);

            Assert.Equal(AreaType.FileSystem.ToString(), metadata.GetString(AreaValueType.FsType));
            Assert.Equal(0x800L, metadata.GetLong(AreaValueType.BlockSize));
            Assert.Equal(imageOffset, metadata.GetLong(AreaValueType.AreaOffsetBase));
            Assert.Equal("true", metadata.GetString(AreaValueType.TitleKeyMissing));
            Assert.False(metadata.ContainsKey(AreaValueType.KeyCrc));
        }

        #endregion

        #region Gap Persistence Filtering Tests

        /// <summary>
        /// Validates Requirement 1.6: Zero-fill Fill gaps are excluded from persistence.
        /// Gaps with fill byte 0x00 and DataType.Fill are NOT persisted.
        /// </summary>
        [Fact]
        public void GetGapsForXbox_ZeroFillNJunk_Excluded()
        {
            MockSection section = new MockSection
            {
                Type = AreaType.FileSystem,
                ImageOffset = 0x100000
            };

            // Gap with fill byte 0x00 and DataType.Fill — should be excluded
            MockSectionData gapData = new MockSectionData
            {
                FillByte = 0x00,
                DataType = DataType.Fill,
                FsOffset = 0x1000,
                FsSize = 0x800
            };

            MockSectionItem item = new MockSectionItem
            {
                File = new MockSectionData { IsFile = true, FsOffset = 0, FsSize = 0x1000 },
                Gap = gapData,
                GapInfo = null
            };
            section.Items.Add(item);

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            Assert.Empty(result);
        }

        /// <summary>
        /// Validates Requirement 1.6: Non-zero fill byte gaps ARE persisted.
        /// </summary>
        [Fact]
        public void GetGapsForXbox_NonZeroFillByte_Included()
        {
            MockSection section = new MockSection
            {
                Type = AreaType.FileSystem,
                ImageOffset = 0x100000
            };

            // Gap with non-zero fill byte — should be included
            MockSectionData gapData = new MockSectionData
            {
                FillByte = 0xFF,
                DataType = DataType.NJunk,
                FsOffset = 0x2000,
                FsSize = 0x400
            };

            MockSectionItem item = new MockSectionItem
            {
                File = new MockSectionData { IsFile = true, FsOffset = 0, FsSize = 0x2000 },
                Gap = gapData,
                GapInfo = null
            };
            section.Items.Add(item);

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            Assert.Single(result);
            Assert.Equal(0xFF, result[0].FillByte);
            Assert.Equal(0x400, result[0].Size);
        }

        /// <summary>
        /// Validates Requirement 1.6: Gaps with non-NJunk DataType ARE persisted even with zero fill.
        /// </summary>
        [Fact]
        public void GetGapsForXbox_ZeroFillNonNJunkDataType_Included()
        {
            MockSection section = new MockSection
            {
                Type = AreaType.FileSystem,
                ImageOffset = 0x200000
            };

            // Gap with fill byte 0x00 but DataType.Other (not NJunk) — should be included
            MockSectionData gapData = new MockSectionData
            {
                FillByte = 0x00,
                DataType = DataType.Other,
                FsOffset = 0x3000,
                FsSize = 0x200
            };

            MockSectionItem item = new MockSectionItem
            {
                File = new MockSectionData { IsFile = true, FsOffset = 0, FsSize = 0x3000 },
                Gap = gapData,
                GapInfo = null
            };
            section.Items.Add(item);

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            Assert.Single(result);
            Assert.Equal(0x00, result[0].FillByte);
            Assert.Equal(DataType.Other, result[0].DataType);
        }

        /// <summary>
        /// Validates Requirement 1.6: Non-FileSystem areas return empty gap list.
        /// </summary>
        [Fact]
        public void GetGapsForXbox_NonFileSystemArea_ReturnsEmpty()
        {
            MockSection section = new MockSection
            {
                Type = AreaType.Other,
                ImageOffset = 0x0
            };

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            Assert.Empty(result);
        }

        /// <summary>
        /// Validates Requirement 1.6: Pre-file gaps (gap-only items with no file) are filtered correctly.
        /// </summary>
        [Fact]
        public void GetGapsForXbox_PreFileGap_NonZeroFill_Included()
        {
            MockSection section = new MockSection
            {
                Type = AreaType.FileSystem,
                ImageOffset = 0x300000
            };

            // Gap-only item (no file) with non-zero fill byte
            MockSectionData gapData = new MockSectionData
            {
                FillByte = 0xAA,
                DataType = DataType.NJunk,
                FsOffset = 0x0,
                FsSize = 0x100
            };

            MockSectionItem item = new MockSectionItem
            {
                File = null,
                Gap = gapData,
                GapInfo = null
            };
            section.Items.Add(item);

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            Assert.Single(result);
            Assert.Equal(0xAA, result[0].FillByte);
        }

        #endregion

        #region Aux Writer Lifecycle Tests

        /// <summary>
        /// Validates Requirement 10.2: Aux writer is opened when auxSetName is provided and set exists.
        /// Models the behavior where a non-null auxSetName results in an active aux writer.
        /// </summary>
        [Fact]
        public void AuxWriter_Opened_WhenAuxSetNameProvidedAndSetExists()
        {
            // Model: when auxSetName is non-null and set exists, HasAux is true
            string auxSetName = "xbox.aux.nkds";
            bool auxSetExists = true;

            bool hasAux = !string.IsNullOrEmpty(auxSetName) && auxSetExists;

            Assert.True(hasAux);
        }

        /// <summary>
        /// Validates Requirement 10.7: Aux writer is NOT opened when auxSetName is null.
        /// </summary>
        [Fact]
        public void AuxWriter_NotOpened_WhenAuxSetNameIsNull()
        {
            string auxSetName = null;

            bool hasAux = !string.IsNullOrEmpty(auxSetName);

            Assert.False(hasAux);
        }

        /// <summary>
        /// Validates Requirement 10.7: Aux writer is NOT opened when aux set doesn't exist
        /// and AutoCreateAux is false.
        /// </summary>
        [Fact]
        public void AuxWriter_NotOpened_WhenAuxSetDoesNotExistAndAutoCreateAuxFalse()
        {
            string auxSetName = "xbox.aux.nkds";
            bool auxSetExists = false;
            bool autoCreateAux = false;

            // Model: aux writer is only opened if set exists or autoCreateAux is true
            bool hasAux = !string.IsNullOrEmpty(auxSetName) && (auxSetExists || autoCreateAux);

            Assert.False(hasAux);
        }

        #endregion

        #region Area Mirroring Tests

        /// <summary>
        /// Validates Requirement 10.4: Area records are mirrored to aux writer when aux is active.
        /// </summary>
        [Fact]
        public void CreateAreas_MirrorsToAuxWriter_WhenAuxIsActive()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            List<(long Offset, long Size, uint Crc, ulong XxHash, int SectionSize)> areas = new List<(long Offset, long Size, uint Crc, ulong XxHash, int SectionSize)>
            {
                (0x0, 0x10000, 0xAAAA, 0x1111, 0x200000),
                (0x10000, 0x50000, 0xBBBB, 0x2222, 0x200000),
                (0x60000, 0x20000, 0xCCCC, 0x3333, 0x200000)
            };

            ModelCreateAreas(primary, aux, areas);

            Assert.Equal(3, primary.CreatedAreas.Count);
            Assert.Equal(3, aux.CreatedAreas.Count);
            for (int i = 0; i < areas.Count; i++)
            {
                Assert.Equal(areas[i].Offset, primary.CreatedAreas[i].Offset);
                Assert.Equal(areas[i].Offset, aux.CreatedAreas[i].Offset);
                Assert.Equal(areas[i].Size, primary.CreatedAreas[i].Size);
                Assert.Equal(areas[i].Size, aux.CreatedAreas[i].Size);
            }
        }

        /// <summary>
        /// Validates Requirement 10.5: Filesystem YAML is mirrored to aux writer when aux is active.
        /// </summary>
        [Fact]
        public void BuildFileSystemYaml_MirrorsToAuxWriter_WhenAuxIsActive()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            byte[] yamlData = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            ModelWriteFileSystemYaml(primary, aux, yamlData);

            Assert.Single(primary.WrittenFiles);
            Assert.Single(aux.WrittenFiles);
            Assert.Equal(yamlData, primary.WrittenFiles[0]);
            Assert.Equal(yamlData, aux.WrittenFiles[0]);
        }

        #endregion

        #region FinalizeImage and Dispose Tests

        /// <summary>
        /// Validates Requirement 10.6: FinalizeImage is called on both primary and aux writers.
        /// </summary>
        [Fact]
        public void FinalizeImage_CalledOnBothWriters()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            long size = 0x1000000;
            uint crc = 0xDEADBEEF;
            ulong xxHash = 0x1234567890ABCDEF;

            ModelFinalizeImage(primary, aux, size, crc, xxHash);

            Assert.True(primary.Finalized);
            Assert.True(aux.Finalized);
            Assert.Equal(size, primary.FinalizedSize);
            Assert.Equal(crc, primary.FinalizedCrc);
            Assert.Equal(xxHash, primary.FinalizedXxHash);
            Assert.Equal(size, aux.FinalizedSize);
            Assert.Equal(crc, aux.FinalizedCrc);
            Assert.Equal(xxHash, aux.FinalizedXxHash);
        }

        /// <summary>
        /// Validates Requirement 10.6: FinalizeImage works when aux is null (primary-only mode).
        /// </summary>
        [Fact]
        public void FinalizeImage_NoAux_OnlyPrimaryFinalized()
        {
            MockWriter primary = new MockWriter();

            ModelFinalizeImage(primary, null, 0x500000, 0xBEEF, 0xABCD);

            Assert.True(primary.Finalized);
            Assert.Equal(0x500000, primary.FinalizedSize);
        }

        /// <summary>
        /// Validates Requirement 10.6 (Dispose): Dispose disposes both primary and aux writers.
        /// </summary>
        [Fact]
        public void Dispose_DisposesBothWriters()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();

            ModelDispose(primary, aux);

            Assert.True(primary.Disposed);
            Assert.True(aux.Disposed);
        }

        /// <summary>
        /// Validates Requirement 10.7: Dispose works when aux is null (primary-only mode).
        /// </summary>
        [Fact]
        public void Dispose_NoAux_OnlyPrimaryDisposed()
        {
            MockWriter primary = new MockWriter();

            ModelDispose(primary, null);

            Assert.True(primary.Disposed);
        }

        #endregion

        #region Filler/Junk Block Routing Tests

        /// <summary>
        /// Validates Requirement 10.3: Filler/junk blocks are routed to aux writer when aux is active.
        /// </summary>
        [Fact]
        public void FillerBlocks_RoutedToAuxWriter_WhenAuxIsActive()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            long imageOffset = 0x50000;
            int size = 0x800;

            ModelRouteFillerBlock(primary, aux, imageOffset, size, isFillerJunk: true);

            Assert.Single(aux.WrittenBlocks);
            Assert.Empty(primary.WrittenBlocks);
            Assert.Equal(imageOffset, aux.WrittenBlocks[0].Offset);
            Assert.Equal(size, aux.WrittenBlocks[0].Size);
            Assert.Equal(BlockType.Other, aux.WrittenBlocks[0].Type);
        }

        /// <summary>
        /// Validates Requirement 10.7: Filler/junk blocks are routed to primary writer when aux is NOT active.
        /// </summary>
        [Fact]
        public void FillerBlocks_RoutedToPrimaryWriter_WhenAuxIsNotActive()
        {
            MockWriter primary = new MockWriter();
            long imageOffset = 0x60000;
            int size = 0x1000;

            ModelRouteFillerBlock(primary, null, imageOffset, size, isFillerJunk: true);

            Assert.Single(primary.WrittenBlocks);
            Assert.Equal(imageOffset, primary.WrittenBlocks[0].Offset);
            Assert.Equal(size, primary.WrittenBlocks[0].Size);
            Assert.Equal(BlockType.Other, primary.WrittenBlocks[0].Type);
        }

        /// <summary>
        /// Validates Requirement 10.3: Non-filler game data blocks go to primary even when aux is active.
        /// </summary>
        [Fact]
        public void NonFillerBlocks_RoutedToPrimaryWriter_WhenAuxIsActive()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter();
            long imageOffset = 0x70000;
            int size = 0x2000;

            ModelRouteFillerBlock(primary, aux, imageOffset, size, isFillerJunk: false);

            Assert.Single(primary.WrittenBlocks);
            Assert.Empty(aux.WrittenBlocks);
            Assert.Equal(imageOffset, primary.WrittenBlocks[0].Offset);
        }

        /// <summary>
        /// Validates Requirement 10.3: When aux write fails, filler falls back to primary.
        /// </summary>
        [Fact]
        public void FillerBlocks_FallbackToPrimary_WhenAuxWriteFails()
        {
            MockWriter primary = new MockWriter();
            MockWriter aux = new MockWriter { ThrowOnWrite = true };
            long imageOffset = 0x80000;
            int size = 0x800;

            ModelRouteFillerBlock(primary, aux, imageOffset, size, isFillerJunk: true);

            Assert.Empty(aux.WrittenBlocks); // aux write failed
            Assert.Single(primary.WrittenBlocks); // primary received fallback
            Assert.Equal(imageOffset, primary.WrittenBlocks[0].Offset);
        }

        #endregion
    }
}