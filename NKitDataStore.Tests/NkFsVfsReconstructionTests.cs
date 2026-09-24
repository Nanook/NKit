using Nanook.NKit;
using Nanook.NKit.Vfs;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for VFS reconstruction with multi-extent files.
    /// Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5
    /// </summary>
    public class NkFsVfsReconstructionTests
    {
        #region Single-Extent File: SplitParts is null, FsSize is individual size

        /// <summary>
        /// A single-extent file in NkFs should have SplitParts = null and FsSize equal to its individual size.
        /// Validates: Requirement 12.1 (single-extent case)
        /// </summary>
        [Fact]
        public void SingleExtentFile_SplitPartsIsNull_FsSizeIsIndividualSize()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("DATA");
            dir.AddFile("single.bin", 0x2000, 0x5000, 0xAAAABBBB, 0x1234);

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // File is at index 2 (root=0, dir=1, file=2)
            NkFsEntry entry = nkfs.GetEntry(2);
            NkFsFileItem fileItem = new NkFsFileItem(nkfs, 2, entry);

            // Single-extent: SplitParts should be null
            Assert.Null(fileItem.SplitParts);

            // FsSize should equal the individual file size (0x5000 = 20480)
            Assert.Equal(0x5000L, fileItem.FsSize);

            // FsOffset should be the file's disc offset
            Assert.Equal(0x2000L, fileItem.FsOffset);
        }

        #endregion

        #region 3-Extent File: SplitParts has 3 parts, FsSize is sum of sizes

        /// <summary>
        /// A 3-extent file in NkFs should have SplitParts with 3 parts and FsSize equal to the sum of all extent sizes.
        /// Validates: Requirements 12.1, 12.5
        /// </summary>
        [Fact]
        public void ThreeExtentFile_SplitPartsHas3Parts_FsSizeIsSumOfSizes()
        {
            FsYaml fsYaml = new FsYaml();
            FsYamlNode root = fsYaml.AddFileSystem(".", 0);
            FsYamlNode dir = root.AddDirectory("STREAM");
            dir.AddFile("video.m2ts", 0x1000, 0x4000, 0xAAAA, 0x0001); // size: 0x4000 = 16384
            dir.AddFile("video.m2ts", 0x6000, 0x3000, 0xBBBB, 0x0002); // size: 0x3000 = 12288
            dir.AddFile("video.m2ts", 0xA000, 0x2000, 0xCCCC, 0x0003); // size: 0x2000 = 8192

            NkFs nkfs = NkFs.FromFsYaml(fsYaml);

            // The first extent entry is at index 2 (root=0, dir=1, first extent=2)
            List<(int index, NkFsEntry entry)> children = nkfs.GetChildren(1).ToList();
            int firstExtentIndex = children[0].index;

            NkFsEntry entry = nkfs.GetEntry(firstExtentIndex);
            NkFsFileItem fileItem = new NkFsFileItem(nkfs, firstExtentIndex, entry);

            // Multi-extent: SplitParts should not be null
            Assert.NotNull(fileItem.SplitParts);

            // Should have 3 parts
            Assert.Equal(3, fileItem.SplitParts.Parts.Count);

            // FsSize should be sum of all extent sizes: 0x4000 + 0x3000 + 0x2000 = 0x9000 = 36864
            long expectedTotal = 0x4000L + 0x3000L + 0x2000L;
            Assert.Equal(expectedTotal, fileItem.FsSize);

            // Verify each part's properties
            IFsFilePart part0 = fileItem.SplitParts.Parts[0];
            Assert.Equal(0, part0.Index);
            Assert.Equal(0L, part0.OffsetInFile);
            Assert.Equal(0x4000L, part0.FsFile.FsSize);
            Assert.Equal(0x1000L, part0.FsFile.FsOffset);

            IFsFilePart part1 = fileItem.SplitParts.Parts[1];
            Assert.Equal(1, part1.Index);
            Assert.Equal(0x4000L, part1.OffsetInFile);
            Assert.Equal(0x3000L, part1.FsFile.FsSize);
            Assert.Equal(0x6000L, part1.FsFile.FsOffset);

            IFsFilePart part2 = fileItem.SplitParts.Parts[2];
            Assert.Equal(2, part2.Index);
            Assert.Equal(0x4000L + 0x3000L, part2.OffsetInFile);
            Assert.Equal(0x2000L, part2.FsFile.FsSize);
            Assert.Equal(0xA000L, part2.FsFile.FsOffset);

            // SplitParts.Size should also equal the total
            Assert.Equal(expectedTotal, fileItem.SplitParts.Size);

            // XxHash and Crc on combined parts should be 0 (not available)
            Assert.Equal(0UL, fileItem.SplitParts.XxHash);
            Assert.Equal(0U, fileItem.SplitParts.Crc);
        }

        #endregion

        #region ImageBuilder reads extents in order from correct offsets

        /// <summary>
        /// MultiExtentStream reads extents in order, with each extent sourced from its recorded image offset.
        /// Validates: Requirements 12.2, 12.3
        /// </summary>
        [Fact]
        public void MultiExtentStream_ReadsExtentsInOrder_FromCorrectOffsets()
        {
            // Create 3 extents with known sizes and offsets
            // Extent 0: offset=0x1000, size=100
            // Extent 1: offset=0x5000, size=200
            // Extent 2: offset=0x9000, size=150
            List<IFsFilePart> parts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 100)),
                new TestFsFilePart(1, 100L, new TestFsFile(0x5000, 200)),
                new TestFsFilePart(2, 300L, new TestFsFile(0x9000, 150))
            };

            TestImageReader mockReader = new TestImageReader(parts);
            using MultiExtentStream stream = new MultiExtentStream(mockReader, parts);

            // Total length should be 100 + 200 + 150 = 450
            Assert.Equal(450L, stream.Length);

            // Read the entire stream
            byte[] buffer = new byte[450];
            int bytesRead = stream.Read(buffer, 0, 450);
            Assert.Equal(450, bytesRead);

            // Verify extent 0 data: bytes should identify offset 0x1000, positions 0..99
            for (int i = 0; i < 100; i++)
                Assert.Equal(computeExpectedByte(0x1000, i), buffer[i]);

            // Verify extent 1 data: bytes should identify offset 0x5000, positions 0..199
            for (int i = 0; i < 200; i++)
                Assert.Equal(computeExpectedByte(0x5000, i), buffer[100 + i]);

            // Verify extent 2 data: bytes should identify offset 0x9000, positions 0..149
            for (int i = 0; i < 150; i++)
                Assert.Equal(computeExpectedByte(0x9000, i), buffer[300 + i]);
        }

        /// <summary>
        /// MultiExtentStream handles read operations spanning extent boundaries seamlessly.
        /// Validates: Requirement 12.2
        /// </summary>
        [Fact]
        public void MultiExtentStream_ReadSpanningExtentBoundary_SeamlessTransition()
        {
            List<IFsFilePart> parts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 50)),
                new TestFsFilePart(1, 50L, new TestFsFile(0x2000, 50))
            };

            TestImageReader mockReader = new TestImageReader(parts);
            using MultiExtentStream stream = new MultiExtentStream(mockReader, parts);

            // Seek to position 40 (within extent 0, 10 bytes from its end)
            stream.Position = 40;

            // Read 20 bytes — spanning the boundary between extent 0 and extent 1
            byte[] buffer = new byte[20];
            int bytesRead = stream.Read(buffer, 0, 20);
            Assert.Equal(20, bytesRead);

            // First 10 bytes from extent 0 (position 40..49 within extent 0)
            for (int i = 0; i < 10; i++)
                Assert.Equal(computeExpectedByte(0x1000, 40 + i), buffer[i]);

            // Last 10 bytes from extent 1 (position 0..9 within extent 1)
            for (int i = 0; i < 10; i++)
                Assert.Equal(computeExpectedByte(0x2000, i), buffer[10 + i]);
        }

        #endregion

        #region Shared-Sector UDF File Reads from Recorded Offset Without Conflict

        /// <summary>
        /// A shared-sector UDF file (e.g. .ssif) that overlaps with ISO9660 split extent regions
        /// reads from its recorded image offset without conflict.
        /// This simulates two files whose disc offsets overlap: an ISO9660 multi-extent file and a UDF .ssif file.
        /// Validates: Requirement 12.4
        /// </summary>
        [Fact]
        public void SharedSectorUdfFile_ReadsFromRecordedOffset_WithoutConflict()
        {
            // Simulate: ISO9660 multi-extent file has extents at offsets 0x1000 and 0x5000
            List<IFsFilePart> isoExtentParts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 0x3000)),
                new TestFsFilePart(1, 0x3000L, new TestFsFile(0x5000, 0x2000))
            };

            // Simulate: UDF .ssif file at offset 0x1000 (overlaps with first ISO9660 extent)
            // with size 0x5000 (covers the same disc range as both ISO9660 extents combined)
            List<IFsFilePart> udfParts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 0x5000))
            };

            // Both should be able to read their data independently from the same image reader
            TestImageReader mockReader = new TestImageReader(isoExtentParts);

            // Read ISO9660 multi-extent stream
            using MultiExtentStream isoStream = new MultiExtentStream(mockReader, isoExtentParts);
            byte[] isoBuffer = new byte[0x5000];
            int isoRead = isoStream.Read(isoBuffer, 0, 0x5000);
            Assert.Equal(0x5000, isoRead);

            // Read UDF single-extent stream (shares the same disc offset 0x1000)
            // The UDF reader knows the UDF part sizes so it serves the full extent
            TestImageReader udfReader = new TestImageReader(udfParts);
            using MultiExtentStream udfStream = new MultiExtentStream(udfReader, udfParts);
            byte[] udfBuffer = new byte[0x5000];
            int udfRead = udfStream.Read(udfBuffer, 0, 0x5000);
            Assert.Equal(0x5000, udfRead);

            // Both streams read successfully without error — the UDF file uses its
            // recorded offset (0x1000) and reads 0x5000 bytes contiguously.
            // Verify UDF stream data comes from offset 0x1000, positions 0..0x4FFF
            Assert.Equal(computeExpectedByte(0x1000, 0), udfBuffer[0]);
            Assert.Equal(computeExpectedByte(0x1000, 0x4FFF), udfBuffer[0x4FFF]);

            // The ISO stream reads extent 0 from 0x1000 and extent 1 from 0x5000
            // First 0x3000 bytes from offset 0x1000
            Assert.Equal(computeExpectedByte(0x1000, 0), isoBuffer[0]);
            Assert.Equal(computeExpectedByte(0x1000, 0x2FFF), isoBuffer[0x2FFF]);
            // Next 0x2000 bytes from offset 0x5000
            Assert.Equal(computeExpectedByte(0x5000, 0), isoBuffer[0x3000]);
            Assert.Equal(computeExpectedByte(0x5000, 0x1FFF), isoBuffer[0x4FFF]);
        }

        #endregion

        #region Invalid Byte Offset Throws ArgumentOutOfRangeException

        /// <summary>
        /// Setting Position to a value >= total size throws ArgumentOutOfRangeException.
        /// Validates: Requirement 12.5
        /// </summary>
        [Fact]
        public void MultiExtentStream_PositionBeyondTotalSize_ThrowsArgumentOutOfRange()
        {
            List<IFsFilePart> parts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 100)),
                new TestFsFilePart(1, 100L, new TestFsFile(0x2000, 200))
            };

            TestImageReader mockReader = new TestImageReader(parts);
            using MultiExtentStream stream = new MultiExtentStream(mockReader, parts);

            // Total size is 300. Position > 300 should throw.
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 301);

            // Position == total size (300) is allowed (like seeking to EOF)
            stream.Position = 300;
            Assert.Equal(300L, stream.Position);

            // Position == total size + large value should throw
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 1000);
        }

        /// <summary>
        /// Setting Position to a negative value throws ArgumentOutOfRangeException.
        /// Validates: Requirement 12.5
        /// </summary>
        [Fact]
        public void MultiExtentStream_NegativePosition_ThrowsArgumentOutOfRange()
        {
            List<IFsFilePart> parts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 100))
            };

            TestImageReader mockReader = new TestImageReader(parts);
            using MultiExtentStream stream = new MultiExtentStream(mockReader, parts);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -100);
        }

        /// <summary>
        /// Seek with SeekOrigin.Begin to a value > totalSize throws ArgumentOutOfRangeException.
        /// Validates: Requirement 12.5
        /// </summary>
        [Fact]
        public void MultiExtentStream_SeekBeyondEnd_ThrowsArgumentOutOfRange()
        {
            List<IFsFilePart> parts = new List<IFsFilePart>
            {
                new TestFsFilePart(0, 0L, new TestFsFile(0x1000, 100)),
                new TestFsFilePart(1, 100L, new TestFsFile(0x2000, 100))
            };

            TestImageReader mockReader = new TestImageReader(parts);
            using MultiExtentStream stream = new MultiExtentStream(mockReader, parts);

            // Seek beyond end
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(201, SeekOrigin.Begin));

            // Seek before begin
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Computes a deterministic expected byte for a given image offset and position within that extent.
        /// Uses a simple formula to create a unique byte value based on offset + position.
        /// </summary>
        private static byte computeExpectedByte(long imageOffset, int positionInExtent) => (byte)((imageOffset + positionInExtent) & 0xFF);

        /// <summary>
        /// A minimal IFsFile implementation for testing that provides FsOffset and FsSize.
        /// </summary>
        private class TestFsFile : IFsFile
        {
            public TestFsFile(long fsOffset, long fsSize)
            {
                FsOffset = fsOffset;
                FsSize = fsSize;
            }

            public string Name => "test.bin";
            public long FsSize { get; }
            public long FsOffset { get; }
            public IFsFolder Parent { get; set; }
            public string Path => "";
            public string FullName => Name;
            public bool IsMissing => false;
            public bool IsLastFile => false;
            public bool IsSystemFile => false;
            public int SplitIndex => 0;
            public IFsFileParts SplitParts => null;
            public ulong XxHash { get; set; }
            public uint Crc { get; set; }
            public uint GapCrc { get; set; }
            public long PostGapSize => 0;
            public long PostGapFsOffset => 0;
            public IFsFile Clone() => this;
        }

        /// <summary>
        /// A minimal IFsFilePart implementation for testing.
        /// </summary>
        private class TestFsFilePart : IFsFilePart
        {
            public TestFsFilePart(int index, long offsetInFile, IFsFile fsFile)
            {
                Index = index;
                OffsetInFile = offsetInFile;
                FsFile = fsFile;
            }

            public int Index { get; }
            public long OffsetInFile { get; }
            public IFsFile FsFile { get; }
        }

        /// <summary>
        /// A mock IImageReader that returns deterministic data streams based on image offset.
        /// Each byte at position P within a stream opened at offset O has value (O + P) & 0xFF.
        /// </summary>
        private class TestImageReader : IImageReader
        {
            private readonly List<IFsFilePart> _parts;

            public TestImageReader(List<IFsFilePart> parts)
            {
                _parts = parts;
            }

            public ImageRecord Image { get; } = new ImageRecord();
            public InfoRecord Info { get; } = new InfoRecord();

            public Stream OpenStream(long offsetStart)
            {
                // Find the part with this offset to determine size
                long size = 0;
                foreach (IFsFilePart part in _parts)
                {
                    if (part.FsFile.FsOffset == offsetStart)
                    {
                        size = part.FsFile.FsSize;
                        break;
                    }
                }

                // If not found in parts list, use a large default size to handle arbitrary offsets
                if (size == 0)
                    size = 0x100000; // 1 MB default

                return new DeterministicStream(offsetStart, size);
            }

            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public IEnumerable<OffsetRecord> GetOffsets() => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => Array.Empty<OffsetRecord>();
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public IEnumerable<AreaRecord> GetAreas() => Array.Empty<AreaRecord>();
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();

            public void Dispose() { }
        }

        /// <summary>
        /// A stream that produces deterministic bytes: byte at position P = (baseOffset + P) & 0xFF.
        /// Simulates reading data from a specific disc image offset.
        /// </summary>
        private class DeterministicStream : Stream
        {
            private readonly long _baseOffset;
            private readonly long _length;
            private long _position;

            public DeterministicStream(long baseOffset, long length)
            {
                _baseOffset = baseOffset;
                _length = length;
                _position = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => _position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int available = (int)Math.Min(count, _length - _position);
                for (int i = 0; i < available; i++)
                {
                    buffer[offset + i] = (byte)((_baseOffset + _position + i) & 0xFF);
                }
                _position += available;
                return available;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                _position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => _position + offset,
                    SeekOrigin.End => _length + offset,
                    _ => throw new ArgumentException()
                };
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        #endregion
    }
}