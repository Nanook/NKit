using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using Nanook.NKit.Steps.Shared;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for Xbox gap filtering in DataStoreXboxFormatter.
    ///
    /// Feature: nkds-iso-xbox-support
    /// Property 7: Xbox Gap Filtering
    /// **Validates: Requirements 1.6**
    /// </summary>
    public class XboxGapFilteringPropertyTests
    {
        #region Stub Implementations

        /// <summary>
        /// Minimal stub for ISectionData representing a gap range.
        /// </summary>
        private class StubSectionData : ISectionData
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
        }

        /// <summary>
        /// Minimal stub for ISectionItem representing a file+gap pair.
        /// </summary>
        private class StubSectionItem : ISectionItem
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
        /// Minimal stub for ISection representing a FileSystem section with gap items.
        /// </summary>
        private class StubSection : ISection
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
        /// Minimal stub for IFsFile.
        /// </summary>
        private class StubFsFile : IFsFile
        {
            public string Name { get; set; } = "test.bin";
            public string FullName { get; set; } = "/test.bin";
            public string Path { get; set; } = "/";
            public long FsOffset { get; set; }
            public long FsSize { get; set; } = 0x1000;
            public long PostGapSize { get; set; }
            public long PostGapFsOffset { get; set; }
            public bool IsMissing { get; set; }
            public bool IsSystemFile { get; set; }
            public bool IsLastFile { get; set; }
            public int SplitIndex { get; set; }
            public IFsFileParts SplitParts { get; set; }
            public uint Crc { get; set; }
            public ulong XxHash { get; set; }
            public uint GapCrc { get; set; }
            public IFsFolder Parent { get; set; }
            public IFsFile Clone() => this;
        }

        #endregion

        #region Test Data Model

        /// <summary>
        /// Represents a generated gap for testing purposes.
        /// </summary>
        private record GapSpec(long FsOffset, int Size, byte FillByte, DataType DataType)
        {
            /// <summary>
            /// Whether this gap should be persisted according to the Xbox filtering rule:
            /// persist if FillByte != 0 OR DataType != Fill.
            /// </summary>
            public bool ShouldPersist => FillByte != 0 || DataType != DataType.Fill;
        }

        #endregion

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 7: Xbox Gap Filtering.
        /// For any set of gap ranges within an Xbox FileSystem area, only gaps where the
        /// fill byte is non-zero OR the data type is not Fill are persisted. Gaps with
        /// fill byte 0x00 and data type Fill are NOT persisted.
        ///
        /// We generate arbitrary gap items with random fill bytes and data types, then verify
        /// that GetGapsForXbox returns only those gaps that satisfy the persistence condition.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxGapFiltering_OnlyNonDefaultGapsArePersisted(
            NonNegativeInt gapCountSeed,
            NonNegativeInt seed)
        {
            // Generate 1-15 gap items
            int gapCount = (gapCountSeed.Get % 15) + 1;
            Random rng = new Random(seed.Get);

            // Generate gap specs with varying fill bytes and data types
            List<GapSpec> gapSpecs = new List<GapSpec>();
            long currentFsOffset = 0x1000; // Start after some initial offset

            for (int i = 0; i < gapCount; i++)
            {
                int size = rng.Next(1, 20) * 0x800; // 1-20 sectors worth of gap
                byte fillByte = (byte)rng.Next(0, 256);
                // Choose between Fill (the Xbox default filler pattern) and other data types
                DataType dataType = rng.Next(0, 3) switch
                {
                    0 => DataType.Fill,
                    1 => DataType.NJunk,
                    _ => DataType.Other
                };

                gapSpecs.Add(new GapSpec(currentFsOffset, size, fillByte, dataType));
                currentFsOffset += size + 0x2000; // Leave space between gaps for files
            }

            // Build the section with items that have post-file gaps
            long sectionImageOffset = 0x10000;
            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };

            StubSection section = new StubSection
            {
                ImageOffset = sectionImageOffset,
                Type = AreaType.FileSystem,
                Size = currentFsOffset + 0x10000, // Ensure section is large enough
                FsSize = currentFsOffset + 0x10000,
                FsOffset = 0
            };

            // Create section items: each gap is a post-file gap on a file item
            long fileFsOffset = 0;
            foreach (GapSpec spec in gapSpecs)
            {
                StubSectionData fileData = new StubSectionData
                {
                    FsOffset = fileFsOffset,
                    FsSize = 0x1000,
                    IsFile = true
                };

                StubSectionData gapData = new StubSectionData
                {
                    FsOffset = spec.FsOffset,
                    FsSize = spec.Size,
                    FillByte = spec.FillByte,
                    DataType = spec.DataType,
                    Offset = spec.FsOffset,
                    ImageOffset = sectionImageOffset + spec.FsOffset
                };

                StubSectionItem item = new StubSectionItem
                {
                    File = fileData,
                    Gap = gapData,
                    GapInfo = null, // Single gap per item (no sub-gaps)
                    FsFile = new StubFsFile { FsOffset = fileFsOffset, FsSize = 0x1000 }
                };

                section.Items.Add(item);
                fileFsOffset = spec.FsOffset + spec.Size;
            }

            // Execute the method under test
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            // Verify: every gap that should be persisted IS in the result
            List<GapSpec> expectedPersisted = gapSpecs.Where(g => g.ShouldPersist).ToList();

            // Each expected gap should have a corresponding range in the result
            foreach (GapSpec expected in expectedPersisted)
            {
                long expectedImageOffset = sectionImageOffset + stride.CleanToOffset(expected.FsOffset, false);
                bool found = result.Any(r =>
                    r.ImageOffset == expectedImageOffset &&
                    r.FsOffset == expected.FsOffset &&
                    r.Size == expected.Size);
                if (!found)
                    return false;
            }

            // Verify: no gap that should NOT be persisted appears in the result
            List<GapSpec> expectedFiltered = gapSpecs.Where(g => !g.ShouldPersist).ToList();
            foreach (GapSpec filtered in expectedFiltered)
            {
                long filteredImageOffset = sectionImageOffset + stride.CleanToOffset(filtered.FsOffset, false);
                bool found = result.Any(r =>
                    r.ImageOffset == filteredImageOffset &&
                    r.FsOffset == filtered.FsOffset &&
                    r.Size == filtered.Size);
                if (found)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 7 (supplementary): Xbox Gap Filtering with GapInfo sub-gaps.
        /// When a section item has multiple sub-gaps (via GapInfo), each sub-gap is
        /// independently filtered: only those with non-zero fill byte or non-Fill
        /// data type are persisted.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxGapFiltering_SubGapsIndependentlyFiltered(
            NonNegativeInt subGapCountSeed,
            NonNegativeInt seed)
        {
            // Generate 2-8 sub-gaps within a single item
            int subGapCount = (subGapCountSeed.Get % 7) + 2;
            Random rng = new Random(seed.Get);

            long sectionImageOffset = 0x20000;
            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };

            // Generate sub-gap specs
            List<GapSpec> subGapSpecs = new List<GapSpec>();
            long currentFsOffset = 0x2000;

            for (int i = 0; i < subGapCount; i++)
            {
                int size = rng.Next(1, 10) * 0x800;
                byte fillByte = (byte)rng.Next(0, 256);
                DataType dataType = rng.Next(0, 3) switch
                {
                    0 => DataType.Fill,
                    1 => DataType.NJunk,
                    _ => DataType.Other
                };

                subGapSpecs.Add(new GapSpec(currentFsOffset, size, fillByte, dataType));
                currentFsOffset += size;
            }

            // Build section with a single item that has multiple sub-gaps via GapInfo
            StubSection section = new StubSection
            {
                ImageOffset = sectionImageOffset,
                Type = AreaType.FileSystem,
                Size = currentFsOffset + 0x10000,
                FsSize = currentFsOffset + 0x10000,
                FsOffset = 0
            };

            // The overall gap covers the full range
            StubSectionData overallGap = new StubSectionData
            {
                FsOffset = subGapSpecs[0].FsOffset,
                FsSize = subGapSpecs.Sum(g => g.Size),
                FillByte = subGapSpecs[0].FillByte,
                DataType = subGapSpecs[0].DataType,
                Offset = subGapSpecs[0].FsOffset,
                ImageOffset = sectionImageOffset + subGapSpecs[0].FsOffset
            };

            // Build GapInfo list with individual sub-gaps
            List<ISectionData> gapInfoList = subGapSpecs.Select(spec => (ISectionData)new StubSectionData
            {
                FsOffset = spec.FsOffset,
                FsSize = spec.Size,
                FillByte = spec.FillByte,
                DataType = spec.DataType,
                Offset = spec.FsOffset,
                ImageOffset = sectionImageOffset + spec.FsOffset
            }).ToList();

            StubSectionData fileData = new StubSectionData
            {
                FsOffset = 0,
                FsSize = 0x1000,
                IsFile = true
            };

            StubSectionItem item = new StubSectionItem
            {
                File = fileData,
                Gap = overallGap,
                GapInfo = gapInfoList,
                FsFile = new StubFsFile { FsOffset = 0, FsSize = 0x1000 }
            };

            section.Items.Add(item);

            // Execute the method under test
            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            // Verify: only sub-gaps that should be persisted appear in the result
            List<GapSpec> expectedPersisted = subGapSpecs.Where(g => g.ShouldPersist).ToList();
            List<GapSpec> expectedFiltered = subGapSpecs.Where(g => !g.ShouldPersist).ToList();

            // Each expected persisted sub-gap should be represented in the result
            foreach (GapSpec expected in expectedPersisted)
            {
                long expectedImageOffset = sectionImageOffset + stride.CleanToOffset(expected.FsOffset, false);
                bool found = result.Any(r =>
                    r.ImageOffset == expectedImageOffset &&
                    r.FsOffset == expected.FsOffset);
                if (!found)
                    return false;
            }

            // No filtered sub-gap should appear in the result (unless merged with a persisted neighbor)
            // For non-adjacent filtered gaps, they should definitely not appear
            foreach (GapSpec filtered in expectedFiltered)
            {
                long filteredImageOffset = sectionImageOffset + stride.CleanToOffset(filtered.FsOffset, false);
                // Check that no range starts exactly at this gap's offset with this gap's exact size
                bool foundExact = result.Any(r =>
                    r.ImageOffset == filteredImageOffset &&
                    r.FsOffset == filtered.FsOffset &&
                    r.Size == filtered.Size);
                if (foundExact)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 7 (supplementary): Non-FileSystem sections produce no gap ranges.
        /// GetGapsForXbox should return an empty list for any section that is not AreaType.FileSystem.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxGapFiltering_NonFileSystemSectionReturnsEmpty(NonNegativeInt typeSeed)
        {
            // Pick a non-FileSystem area type
            AreaType[] nonFsTypes = new[] { AreaType.ImageHeader, AreaType.Other, AreaType.PartitionTable, AreaType.PartitionHeader, AreaType.None };
            AreaType areaType = nonFsTypes[typeSeed.Get % nonFsTypes.Length];

            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };

            StubSection section = new StubSection
            {
                ImageOffset = 0x10000,
                Type = areaType,
                Size = 0x100000,
                FsSize = 0x100000,
                FsOffset = 0
            };

            // Add some items with gaps that would normally be persisted
            StubSectionData gapData = new StubSectionData
            {
                FsOffset = 0x1000,
                FsSize = 0x2000,
                FillByte = 0xFF, // Non-zero fill byte — would be persisted in FileSystem
                DataType = DataType.Other,
                Offset = 0x1000,
                ImageOffset = 0x11000
            };

            StubSectionItem item = new StubSectionItem
            {
                File = new StubSectionData { FsOffset = 0, FsSize = 0x800, IsFile = true },
                Gap = gapData,
                GapInfo = null,
                FsFile = new StubFsFile { FsOffset = 0, FsSize = 0x800 }
            };

            section.Items.Add(item);

            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            return result.Count == 0;
        }

        /// <summary>
        /// **Validates: Requirements 1.6**
        ///
        /// Property 7 (supplementary): All-default gaps produce empty result.
        /// When every gap has fill byte 0x00 and data type Fill, no gaps are persisted.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool XboxGapFiltering_AllDefaultGapsProduceEmptyResult(NonNegativeInt gapCountSeed)
        {
            int gapCount = (gapCountSeed.Get % 10) + 1;

            long sectionImageOffset = 0x10000;
            DataStride stride = new DataStride { SourceBlockSize = 0x8000, DataOffset = 0, DataLength = 0x8000 };

            StubSection section = new StubSection
            {
                ImageOffset = sectionImageOffset,
                Type = AreaType.FileSystem,
                Size = (long)(gapCount + 1) * 0x10000,
                FsSize = (long)(gapCount + 1) * 0x10000,
                FsOffset = 0
            };

            long fileFsOffset = 0;
            for (int i = 0; i < gapCount; i++)
            {
                long gapFsOffset = fileFsOffset + 0x1000;
                int gapSize = 0x2000;

                StubSectionData gapData = new StubSectionData
                {
                    FsOffset = gapFsOffset,
                    FsSize = gapSize,
                    FillByte = 0x00, // Zero fill byte
                    DataType = DataType.Fill, // Default Xbox filler pattern
                    Offset = gapFsOffset,
                    ImageOffset = sectionImageOffset + gapFsOffset
                };

                StubSectionItem item = new StubSectionItem
                {
                    File = new StubSectionData { FsOffset = fileFsOffset, FsSize = 0x1000, IsFile = true },
                    Gap = gapData,
                    GapInfo = null,
                    FsFile = new StubFsFile { FsOffset = fileFsOffset, FsSize = 0x1000 }
                };

                section.Items.Add(item);
                fileFsOffset = gapFsOffset + gapSize;
            }

            List<GapRange> result = DataStoreXboxFormatter.GetGapsForXbox(section, stride);

            return result.Count == 0;
        }
    }
}