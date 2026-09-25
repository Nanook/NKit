using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Preservation property tests for WiiU CDN title key discovery.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    ///
    /// Property 2: Preservation — Non-VFS-CDN Behavior Unchanged
    ///
    /// These tests verify that paths which ALREADY work on unfixed code continue
    /// to produce the same results after the fix is applied. They follow the
    /// observation-first methodology: observe behavior on unfixed code, then encode
    /// it as property-based tests that must always pass.
    ///
    /// Preservation scenarios:
    /// 1. NKitApp/standalone path (no cachedOffsets) where readAreaBytes succeeds
    /// 2. Non-CDN WiiU ISO images (disc-key-based encryption)
    /// 3. Wii/GameCube images (use ImageBuilderWiiStream, not ImageBuilderWiiUStream)
    /// </summary>
    [Collection("ImageBuilder Sequential Tests")]
    public class WiiUCdnTitleKeyPreservationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        public WiiUCdnTitleKeyPreservationTests(ITestOutputHelper output)
        {
            _output = output;
            // Register CodePagesEncodingProvider to simulate the NKitApp path
            // where SourceFile.cs registers it in its static constructor.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public void Dispose() { }

        #region Mock Readers

        /// <summary>
        /// Mock IImageReader that simulates the NKitApp/standalone path for a WiiU CDN image:
        /// - OpenStream SUCCEEDS (returns valid area data) — simulating standalone reader
        /// - GetAreas returns areas with valid AreaValueType.TitleKey metadata
        /// - No cachedOffsets passed to constructor (NKitApp path)
        /// </summary>
        private class StandaloneWorkingCdnMockReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly List<OffsetRecord> _offsets;
            private readonly string _titleKeyHex;
            private readonly Dictionary<BlockKey, BlockRecord> _blocks;
            private readonly byte[] _plaintextPattern;
            private readonly Dictionary<long, byte[]> _areaData;

            public ImageRecord Image { get; }
            public InfoRecord Info { get; }

            public StandaloneWorkingCdnMockReader(
                string titleKeyHex,
                long imageSize = 0x10000000,
                byte[] plaintextPattern = null)
            {
                _titleKeyHex = titleKeyHex;
                _plaintextPattern = plaintextPattern ?? GeneratePlaintextPattern();
                Image = new ImageRecord
                {
                    Id = 1,
                    Name = "TestCdnImage_Standalone",
                    Size = imageSize,
                    Format = ImageFormat.Cdn,
                    System = "WiiU"
                };
                Info = new InfoRecord
                {
                    BlockSize = 0x10000,
                    MaxOffsetBlocks = 32
                };

                _areas = BuildCdnAreas(titleKeyHex, imageSize);
                _areaData = BuildAreaData(_areas);
                _offsets = BuildOffsets(_areas, Info.BlockSize);
                _blocks = BuildBlocks(_offsets, _plaintextPattern, Info.BlockSize);
            }

            public byte[] PlaintextPattern => _plaintextPattern;

            private static byte[] GeneratePlaintextPattern()
            {
                byte[] pattern = new byte[0x10000];
                for (int i = 0; i < pattern.Length; i++)
                    pattern[i] = (byte)(0x41 + (i % 26));
                return pattern;
            }

            private static List<AreaRecord> BuildCdnAreas(string titleKeyHex, long imageSize)
            {
                var areas = new List<AreaRecord>();
                long offset = 0;

                // ImageHeader area with TitleKey metadata
                var imageHeaderMeta = new AreaMetadata();
                imageHeaderMeta.Set(AreaValueType.FsType, "ImageHeader");
                imageHeaderMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                imageHeaderMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 1,
                    ImageId = 1,
                    Offset = offset,
                    Size = 0x50000,
                    SectionSize = 0x200000,
                    Metadata = imageHeaderMeta
                });
                offset += 0x50000;

                // FstBlock area with TitleKey metadata
                var fstBlockMeta = new AreaMetadata();
                fstBlockMeta.Set(AreaValueType.FsType, "FstBlock");
                fstBlockMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                fstBlockMeta.Set(AreaValueType.ContentIndex, 0L);
                fstBlockMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 2,
                    ImageId = 1,
                    Offset = offset,
                    Size = 0x10000,
                    SectionSize = 0x200000,
                    Metadata = fstBlockMeta
                });
                offset += 0x10000;

                // FileSystem area - encrypted content
                var fsMeta = new AreaMetadata();
                fsMeta.Set(AreaValueType.FsType, "FileSystem");
                fsMeta.Set(AreaValueType.TitleKey, titleKeyHex);
                fsMeta.Set(AreaValueType.Encrypted, true);
                fsMeta.Set(AreaValueType.Partition, 0L);
                fsMeta.Set(AreaValueType.ContentIndex, 0L);
                fsMeta.Set(AreaValueType.BlockSize, (long)0x10000);
                fsMeta.Set(AreaValueType.HashSize, 0L);
                fsMeta.Set(AreaValueType.PartitionType, "Game");
                long fsSize = 0x200000;
                areas.Add(new AreaRecord
                {
                    Id = 3,
                    ImageId = 1,
                    Offset = offset,
                    Size = fsSize,
                    SectionSize = 0x200000,
                    Metadata = fsMeta
                });

                return areas;
            }

            /// <summary>
            /// Builds synthetic area data that OpenStream will return.
            /// For ImageHeader: a minimal valid header buffer.
            /// For FstBlock: a minimal FST buffer.
            /// </summary>
            private static Dictionary<long, byte[]> BuildAreaData(List<AreaRecord> areas)
            {
                var data = new Dictionary<long, byte[]>();
                foreach (var area in areas)
                {
                    // Create zero-filled data of the area's size
                    // The important thing is that OpenStream succeeds (returns non-null data)
                    byte[] areaBytes = new byte[area.Size];
                    data[area.Offset] = areaBytes;
                }
                return data;
            }

            private static List<OffsetRecord> BuildOffsets(List<AreaRecord> areas, int blockSize)
            {
                var offsets = new List<OffsetRecord>();
                foreach (var area in areas)
                {
                    int blockCount = (int)((area.Size + blockSize - 1) / blockSize);
                    var blocks = new List<BlockKey>();
                    for (int i = 0; i < blockCount; i++)
                        blocks.Add(new BlockKey((ulong)(area.Offset + i * blockSize), (uint)i));

                    offsets.Add(new OffsetRecord
                    {
                        ImageId = 1,
                        Offset = area.Offset,
                        Size = area.Size,
                        OffsetStart = area.Offset,
                        Type = BlockType.File,
                        Blocks = blocks
                    });
                }
                return offsets;
            }

            private static Dictionary<BlockKey, BlockRecord> BuildBlocks(
                List<OffsetRecord> offsets, byte[] plaintextPattern, int blockSize)
            {
                var blocks = new Dictionary<BlockKey, BlockRecord>();
                foreach (var offset in offsets)
                {
                    if (offset.Blocks == null) continue;
                    for (int i = 0; i < offset.Blocks.Count; i++)
                    {
                        var key = offset.Blocks[i];
                        int size = (int)Math.Min(blockSize, offset.Size - (long)i * blockSize);
                        byte[] data = new byte[size];
                        for (int j = 0; j < size; j++)
                            data[j] = plaintextPattern[j % plaintextPattern.Length];
                        blocks[key] = new BlockRecord(key, CompressionType.None, data);
                    }
                }
                return blocks;
            }

            public void Dispose() { }

            /// <summary>
            /// OpenStream SUCCEEDS — returns a MemoryStream with the area data.
            /// This simulates the NKitApp/standalone reader path where readAreaBytes works.
            /// </summary>
            public Stream OpenStream(long offsetStart)
            {
                if (_areaData.TryGetValue(offsetStart, out byte[] data))
                    return new MemoryStream(data);
                // For offsets not in area data, return empty stream
                return new MemoryStream(new byte[0]);
            }

            public Stream OpenStream(DataStride stride, long offsetStart)
            {
                return OpenStream(offsetStart);
            }

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => _offsets;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart)
                => _offsets.Where(o => o.OffsetStart == offsetStart);
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length)
                => _offsets.Where(o => o.Offset >= startOffset && o.Offset < startOffset + length);
            public BlockRecord? GetBlock(BlockKey key)
                => _blocks.TryGetValue(key, out var block) ? block : null;
            public Stream? OpenBlockStream(BlockKey key) => null;
            public byte[]? ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        #endregion

        #region Non-CDN WiiU ISO Mock Reader

        /// <summary>
        /// Mock IImageReader for a non-CDN WiiU ISO image.
        /// Format is Iso (not Cdn/App), so disc-key-based encryption applies.
        /// This path should be completely unaffected by the CDN title key fix.
        /// </summary>
        private class NonCdnWiiUIsoMockReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly List<OffsetRecord> _offsets;
            private readonly Dictionary<BlockKey, BlockRecord> _blocks;
            private readonly byte[] _plaintextPattern;
            private readonly Dictionary<long, byte[]> _areaData;

            public ImageRecord Image { get; }
            public InfoRecord Info { get; }

            public NonCdnWiiUIsoMockReader(long imageSize = 0x10000000)
            {
                _plaintextPattern = GeneratePlaintextPattern();
                Image = new ImageRecord
                {
                    Id = 2,
                    Name = "TestWiiUIso",
                    Size = imageSize,
                    Format = ImageFormat.Iso,
                    System = "WiiU"
                };
                Info = new InfoRecord
                {
                    BlockSize = 0x10000,
                    MaxOffsetBlocks = 32
                };

                _areas = BuildIsoAreas(imageSize);
                _areaData = BuildAreaData(_areas);
                _offsets = BuildOffsets(_areas, Info.BlockSize);
                _blocks = BuildBlocks(_offsets, _plaintextPattern, Info.BlockSize);
            }

            public byte[] PlaintextPattern => _plaintextPattern;

            private static byte[] GeneratePlaintextPattern()
            {
                byte[] pattern = new byte[0x10000];
                for (int i = 0; i < pattern.Length; i++)
                    pattern[i] = (byte)(0x30 + (i % 10)); // 0-9 repeating
                return pattern;
            }

            private static List<AreaRecord> BuildIsoAreas(long imageSize)
            {
                var areas = new List<AreaRecord>();
                long offset = 0;

                // ImageHeader area — NO TitleKey (ISO uses disc key, not title key)
                var imageHeaderMeta = new AreaMetadata();
                imageHeaderMeta.Set(AreaValueType.FsType, "ImageHeader");
                imageHeaderMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 1,
                    ImageId = 2,
                    Offset = offset,
                    Size = 0x50000,
                    SectionSize = 0x200000,
                    Metadata = imageHeaderMeta
                });
                offset += 0x50000;

                // Other area (gap/padding)
                var otherMeta = new AreaMetadata();
                otherMeta.Set(AreaValueType.FsType, "Other");
                otherMeta.Set(AreaValueType.Encrypted, false);
                areas.Add(new AreaRecord
                {
                    Id = 2,
                    ImageId = 2,
                    Offset = offset,
                    Size = 0x10000,
                    SectionSize = 0x200000,
                    Metadata = otherMeta
                });
                offset += 0x10000;

                return areas;
            }

            private static Dictionary<long, byte[]> BuildAreaData(List<AreaRecord> areas)
            {
                var data = new Dictionary<long, byte[]>();
                foreach (var area in areas)
                    data[area.Offset] = new byte[area.Size];
                return data;
            }

            private static List<OffsetRecord> BuildOffsets(List<AreaRecord> areas, int blockSize)
            {
                var offsets = new List<OffsetRecord>();
                foreach (var area in areas)
                {
                    int blockCount = (int)((area.Size + blockSize - 1) / blockSize);
                    var blocks = new List<BlockKey>();
                    for (int i = 0; i < blockCount; i++)
                        blocks.Add(new BlockKey((ulong)(area.Offset + i * blockSize), (uint)i));
                    offsets.Add(new OffsetRecord
                    {
                        ImageId = 2,
                        Offset = area.Offset,
                        Size = area.Size,
                        OffsetStart = area.Offset,
                        Type = BlockType.File,
                        Blocks = blocks
                    });
                }
                return offsets;
            }

            private static Dictionary<BlockKey, BlockRecord> BuildBlocks(
                List<OffsetRecord> offsets, byte[] plaintextPattern, int blockSize)
            {
                var blocks = new Dictionary<BlockKey, BlockRecord>();
                foreach (var offset in offsets)
                {
                    if (offset.Blocks == null) continue;
                    for (int i = 0; i < offset.Blocks.Count; i++)
                    {
                        var key = offset.Blocks[i];
                        int size = (int)Math.Min(blockSize, offset.Size - (long)i * blockSize);
                        byte[] data = new byte[size];
                        for (int j = 0; j < size; j++)
                            data[j] = plaintextPattern[j % plaintextPattern.Length];
                        blocks[key] = new BlockRecord(key, CompressionType.None, data);
                    }
                }
                return blocks;
            }

            public void Dispose() { }

            public Stream OpenStream(long offsetStart)
            {
                if (_areaData.TryGetValue(offsetStart, out byte[] data))
                    return new MemoryStream(data);
                return new MemoryStream(new byte[0]);
            }

            public Stream OpenStream(DataStride stride, long offsetStart)
                => OpenStream(offsetStart);

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => _offsets;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart)
                => _offsets.Where(o => o.OffsetStart == offsetStart);
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length)
                => _offsets.Where(o => o.Offset >= startOffset && o.Offset < startOffset + length);
            public BlockRecord? GetBlock(BlockKey key)
                => _blocks.TryGetValue(key, out var block) ? block : null;
            public Stream? OpenBlockStream(BlockKey key) => null;
            public byte[]? ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        #endregion

        #region Wii/GameCube Mock Reader
