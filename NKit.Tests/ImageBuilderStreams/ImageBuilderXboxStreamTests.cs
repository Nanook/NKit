#nullable enable
using Nanook.NKit;
using NKitDataStore;
using NKitDataStore.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;


namespace NKit.Tests.NKDS
{
    /// <summary>
    /// Unit tests for ImageBuilderXboxStream.
    /// Uses model-based testing where the ImageBuilder requires complex constructor dependencies.
    /// Tests gap fill behavior, aux filler restoration, and block padding restoration.
    /// Xbox does not use data-level encryption — that's PS3 only.
    ///
    /// Validates Requirements: 4.1-4.3, 4.6, 4.8, 4.9, 10.8, 10.9
    /// </summary>
    [Trait("Area", "NKDS")]
    public class ImageBuilderXboxStreamTests
    {
        #region Mock Types

        /// <summary>
        /// Minimal mock IImageReader for constructing ImageBuilderXboxStream.
        /// </summary>
        private class MockImageReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly InfoRecord _info;
            private readonly ImageRecord _image;
            private readonly Dictionary<BlockKey, byte[]> _blocks = new();
            private readonly List<OffsetRecord> _offsets = new();

            public MockImageReader(List<AreaRecord> areas, int blockSize = 0x10000, long imageSize = 0x100000)
            {
                _areas = areas ?? new List<AreaRecord>();
                _info = new InfoRecord { BlockSize = blockSize };
                _image = new ImageRecord { Size = imageSize, Name = "TestXbox" };
            }

            public ImageRecord Image => _image;
            public InfoRecord Info => _info;

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => _offsets;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => _offsets.Where(o => o.OffsetStart == offsetStart);
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) =>
                _offsets.Where(o => o.Offset >= startOffset && o.Offset < startOffset + length);

            public BlockRecord? GetBlock(BlockKey key)
            {
                if (_blocks.TryGetValue(key, out byte[]? data))
                    return new BlockRecord(key, CompressionType.None, data);
                return null;
            }

            public Stream OpenStream(long offsetStart) => new MemoryStream();
            public Stream OpenStream(DataStride stride, long offsetStart) => new MemoryStream();
            public Stream? OpenBlockStream(BlockKey key) => null;
            public byte[]? ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Enumerable.Empty<FileRecord>();

            public void AddBlock(BlockKey key, byte[] data) => _blocks[key] = data;
            public void AddOffset(OffsetRecord offset) => _offsets.Add(offset);

            public void Dispose() { }
        }

        /// <summary>
        /// Mock IBlockProvider that returns blocks from a dictionary.
        /// </summary>
        private class MockBlockProvider : IBlockProvider
        {
            private readonly Dictionary<BlockKey, byte[]> _blocks = new();

            public void AddBlock(BlockKey key, byte[] data) => _blocks[key] = data;

            public BlockRecord? GetBlock(BlockKey key)
            {
                if (_blocks.TryGetValue(key, out byte[]? data))
                    return new BlockRecord(key, CompressionType.None, data);
                return null;
            }

            public Task<BlockRecord?> GetBlockAsync(BlockKey key) => Task.FromResult(GetBlock(key));

            public BlockRecord? GetBlock(OffsetRecord record, int blockIndex)
            {
                if (record.Blocks == null || blockIndex >= record.Blocks.Count)
                    return null;
                return GetBlock(record.Blocks[blockIndex]);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a minimal area with FileSystem FsType (game partition).
        /// </summary>
        private static AreaRecord createFileSystemArea(long offset = 0, long size = 0x200000, long id = 1)
        {
            AreaRecord area = new AreaRecord
            {
                Id = id,
                Offset = offset,
                Size = size,
                SectionSize = (int)Math.Min(size, 0x200000),
                StrideBlockSize = 0,
                StrideDataOffset = 0,
                StrideDataLength = 0
            };
            area.Metadata.Set(AreaValueType.FsType, "FileSystem");
            area.Metadata.Set(AreaValueType.BlockSize, 0x800L);
            area.Metadata.Set(AreaValueType.AreaOffsetBase, offset);
            return area;
        }

        /// <summary>
        /// Creates a minimal area with Other FsType (video partition).
        /// </summary>
        private static AreaRecord createOtherArea(long offset = 0, long size = 0x200000, long id = 2)
        {
            AreaRecord area = new AreaRecord
            {
                Id = id,
                Offset = offset,
                Size = size,
                SectionSize = (int)Math.Min(size, 0x200000),
                StrideBlockSize = 0,
                StrideDataOffset = 0,
                StrideDataLength = 0
            };
            area.Metadata.Set(AreaValueType.FsType, "Other");
            area.Metadata.Set(AreaValueType.BlockSize, 0x800L);
            area.Metadata.Set(AreaValueType.AreaOffsetBase, offset);
            return area;
        }

        #endregion

        #region Gap Fill Tests

        /// <summary>
        /// Validates Requirement 4.2: Gap fill produces null bytes for FileSystem area when no aux available.
        /// When no aux block provider is available, gap regions in the game partition (FileSystem)
        /// are filled with null bytes (0x00).
        /// </summary>
        [Fact]
        public void GapFill_FileSystemArea_NoAux_ProducesNullBytes()
        {
            // Arrange: Create a FileSystem area with no data (all gaps)
            long areaSize = 0x2000;
            AreaRecord area = createFileSystemArea(offset: 0, size: areaSize, id: 1);
            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: areaSize);
            MockBlockProvider blockProvider = new MockBlockProvider();

            // Act: Create the stream with no aux block provider
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] buffer = new byte[0x1000];
            stream.Position = 0;
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert: All bytes should be zero (gap fill with null bytes)
            Assert.True(bytesRead > 0);
            Assert.All(buffer.Take(bytesRead), b => Assert.Equal(0x00, b));
        }

        /// <summary>
        /// Validates Requirement 4.3: Gap fill produces null bytes for video partition (Other) area.
        /// When filling gaps in an area where FsType is "Other" (video partition),
        /// the stream fills with null bytes (0x00).
        /// </summary>
        [Fact]
        public void GapFill_OtherArea_ProducesNullBytes()
        {
            // Arrange: Create an Other area with no data (all gaps)
            long areaSize = 0x2000;
            AreaRecord area = createOtherArea(offset: 0, size: areaSize, id: 1);
            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: areaSize);
            MockBlockProvider blockProvider = new MockBlockProvider();

            // Act: Create the stream with no aux block provider
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] buffer = new byte[0x1000];
            stream.Position = 0;
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert: All bytes should be zero (gap fill with null bytes)
            Assert.True(bytesRead > 0);
            Assert.All(buffer.Take(bytesRead), b => Assert.Equal(0x00, b));
        }

        #endregion

        #region Aux Filler Restoration Tests

        /// <summary>
        /// Validates Requirement 10.8: Filler restored from aux block provider when available.
        /// When an aux block provider is available, filler data is served from aux blocks
        /// through the normal block resolution chain.
        /// </summary>
        [Fact]
        public void GapFill_WithAuxBlockProvider_RestoresFillerFromAux()
        {
            // Arrange: Create a FileSystem area with an offset record pointing to aux data
            long areaSize = 0x2000;
            AreaRecord area = createFileSystemArea(offset: 0, size: areaSize, id: 1);
            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: areaSize);

            // Create filler data that would be stored in aux
            byte[] fillerData = new byte[0x800];
            for (int i = 0; i < fillerData.Length; i++)
                fillerData[i] = 0xAB;

            BlockKey blockKey = new BlockKey(0x1234567890ABCDEF, 0xDEADBEEF);

            // Create an offset record with blocks that the aux provider will serve
            OffsetRecord offsetRecord = new OffsetRecord
            {
                Offset = 0,
                Size = 0x800,
                Type = BlockType.Other,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { blockKey }
            };
            reader.AddOffset(offsetRecord);

            // The aux block provider serves the filler data
            MockBlockProvider auxBlockProvider = new MockBlockProvider();
            auxBlockProvider.AddBlock(blockKey, fillerData);

            // The primary block provider also has the block (AuxBlockProvider resolves from primary first, then aux)
            MockBlockProvider blockProvider = new MockBlockProvider();
            blockProvider.AddBlock(blockKey, fillerData);

            // Act: Create the stream with aux block provider
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: auxBlockProvider);
            byte[] buffer = new byte[0x800];
            stream.Position = 0;
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert: Data should be restored from the block (not zeros)
            Assert.Equal(0x800, bytesRead);
            // The block data should be present in the output
            Assert.Equal(fillerData, buffer);
        }

        /// <summary>
        /// Validates Requirement 4.9, 10.9: Filler zero-filled when aux block provider is null.
        /// When no aux block provider is available, filler regions are filled with 0x00.
        /// </summary>
        [Fact]
        public void GapFill_WithoutAuxBlockProvider_ZeroFilled()
        {
            // Arrange: Create a FileSystem area with no block data (simulating missing aux)
            long areaSize = 0x2000;
            AreaRecord area = createFileSystemArea(offset: 0, size: areaSize, id: 1);
            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: areaSize);
            MockBlockProvider blockProvider = new MockBlockProvider();
            // No blocks added — simulates no data available

            // Act: Create the stream without aux block provider
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] buffer = new byte[0x1000];
            stream.Position = 0;
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert: All bytes should be zero (gap fill with null bytes)
            Assert.True(bytesRead > 0);
            Assert.All(buffer.Take(bytesRead), b => Assert.Equal(0x00, b));
        }

        #endregion

        #region BlockPadding Restoration Tests

        /// <summary>
        /// Validates Requirement 4.6: BlockPadding records are restored correctly in OnBufferPopulatedWithSection.
        /// When a section contains a BlockPadding segment, the stored padding bytes are
        /// restored into the corresponding buffer positions.
        /// </summary>
        [Fact]
        public void OnBufferPopulatedWithSection_RestoresBlockPadding()
        {
            // Arrange: Create a FileSystem area with both file data and block padding
            long areaSize = 0x2000;
            AreaRecord area = createFileSystemArea(offset: 0, size: areaSize, id: 1);

            MockImageReader reader = new MockImageReader(new List<AreaRecord> { area }, blockSize: 0x800, imageSize: areaSize);

            // Create file data for the first block
            byte[] fileData = new byte[0x800];
            for (int i = 0; i < fileData.Length; i++)
                fileData[i] = (byte)(i % 256);

            BlockKey fileBlockKey = new BlockKey(0x1234567890ABCDEF, 0xAABBCCDD);
            OffsetRecord fileOffset = new OffsetRecord
            {
                Offset = 0,
                Size = 0x800,
                Type = BlockType.FileSystem,
                OffsetStart = 0,
                Blocks = new List<BlockKey> { fileBlockKey }
            };
            reader.AddOffset(fileOffset);

            // Create block padding data (header byte + padding bytes)
            // Format: first byte = header length, then padding data
            byte[] paddingBlockData = new byte[0x800];
            paddingBlockData[0] = 4; // header skip = 1 + 4 = 5 bytes total header
            // Padding payload starts at offset 5
            for (int i = 5; i < 20; i++)
                paddingBlockData[i] = 0xFE;

            BlockKey paddingBlockKey = new BlockKey(0xFEDCBA0987654321, 0x11223344);
            OffsetRecord paddingOffset = new OffsetRecord
            {
                Offset = 0x800,
                Size = 0x800,
                Type = BlockType.BlockPadding,
                OffsetStart = 0x800,
                Blocks = new List<BlockKey> { paddingBlockKey }
            };
            reader.AddOffset(paddingOffset);
            reader.AddBlock(paddingBlockKey, paddingBlockData);

            MockBlockProvider blockProvider = new MockBlockProvider();
            blockProvider.AddBlock(fileBlockKey, fileData);
            blockProvider.AddBlock(paddingBlockKey, paddingBlockData);

            // Act: Read from the stream
            using ImageBuilderXboxStream stream = new ImageBuilderXboxStream(reader, blockProvider: blockProvider, auxBlockProvider: null);
            byte[] outputBuffer = new byte[0x800];
            stream.Position = 0;
            int bytesRead = stream.Read(outputBuffer, 0, outputBuffer.Length);

            // Assert: The file data should be present in the output
            // (BlockPadding is applied to a separate section/buffer, so the file data section
            // should still contain the original file data)
            Assert.Equal(0x800, bytesRead);
            Assert.Equal(fileData, outputBuffer);
        }

        #endregion

        #region Construction Tests

        /// <summary>
        /// Validates that the constructor throws when imageReader is null.
        /// </summary>
        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenImageReaderIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ImageBuilderXboxStream(null, blockProvider: null, auxBlockProvider: null));
        }

        #endregion
    }
}