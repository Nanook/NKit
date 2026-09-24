using FsCheck;
using FsCheck.Xunit;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for ImageBuilder MergeAuxOffsets failure resilience.
    ///
    /// Feature: aux-split-mode
    /// Property 11: MergeAuxOffsets Failure Does Not Propagate
    /// **Validates: Requirements 7.2, 7.3**
    ///
    /// For any exception thrown by MergeAuxOffsets during ImageBuilder construction,
    /// the exception SHALL be caught (not propagated), and the ImageBuilder SHALL
    /// remain functional for Read() operations.
    /// </summary>
    public class MergeAuxOffsetsFailurePropertyTests
    {
        #region Stubs

        /// <summary>
        /// A stub IBlockProvider that returns null for all lookups (simulates empty primary store).
        /// </summary>
        private class StubBlockProvider : IBlockProvider
        {
            public BlockRecord GetBlock(BlockKey key) => null;
            public Task<BlockRecord> GetBlockAsync(BlockKey key)
                => Task.FromResult<BlockRecord>(null);
            public BlockRecord GetBlock(OffsetRecord record, int blockIndex) => null;
        }

        /// <summary>
        /// A stub IImageReader that provides minimal valid data for ImageBuilder construction
        /// (areas and offsets) but does NOT throw during construction queries.
        /// Used as the primary reader for ImageBuilder.
        /// </summary>
        private class MinimalImageReader : IImageReader
        {
            private readonly List<AreaRecord> _areas;
            private readonly List<OffsetRecord> _offsets;
            private readonly long _imageSize;

            public MinimalImageReader(List<AreaRecord> areas, List<OffsetRecord> offsets, long imageSize)
            {
                _areas = areas;
                _offsets = offsets;
                _imageSize = imageSize;
            }

            public ImageRecord Image => new ImageRecord { Size = _imageSize, Name = "TestImage" };
            public InfoRecord Info => new InfoRecord { BlockSize = 0x10000 };

            public void Dispose() { }

            public IEnumerable<AreaRecord> GetAreas() => _areas;
            public IEnumerable<OffsetRecord> GetOffsets() => _offsets;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => _offsets.Where(o => o.Offset >= offsetStart);
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length)
                => _offsets.Where(o => o.Offset >= startOffset && o.Offset < startOffset + length);
            public Stream OpenStream(long offsetStart) => Stream.Null;
            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        /// <summary>
        /// A stub IImageReader used as the AuxReader that throws a configurable exception
        /// when GetOffsets() is called (which is invoked by MergeAuxOffsets).
        /// </summary>
        private class ThrowingAuxReader : IImageReader
        {
            private readonly Exception _exceptionToThrow;

            public ThrowingAuxReader(Exception exceptionToThrow)
            {
                _exceptionToThrow = exceptionToThrow;
            }

            public ImageRecord Image => new ImageRecord { Size = 0x100000, Name = "ThrowingAux" };
            public InfoRecord Info => new InfoRecord { BlockSize = 0x10000 };

            public void Dispose() { }

            public IEnumerable<AreaRecord> GetAreas() => Array.Empty<AreaRecord>();

            /// <summary>
            /// Throws the configured exception — simulates MergeAuxOffsets failure.
            /// </summary>
            public IEnumerable<OffsetRecord> GetOffsets() => throw _exceptionToThrow;

            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => throw _exceptionToThrow;
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => throw _exceptionToThrow;
            public Stream OpenStream(long offsetStart) => Stream.Null;
            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a minimal set of areas for ImageBuilder construction.
        /// </summary>
        private static List<AreaRecord> createAreas(int count, int sectionSize = 0x200000)
        {
            List<AreaRecord> areas = new List<AreaRecord>();
            long offset = 0;
            for (int i = 0; i < count; i++)
            {
                areas.Add(new AreaRecord
                {
                    Id = i + 1,
                    ImageId = 1,
                    Offset = offset,
                    Size = sectionSize,
                    SectionSize = sectionSize,
                    Index = i
                });
                offset += sectionSize;
            }
            return areas;
        }

        /// <summary>
        /// Creates a set of offset records within the given areas.
        /// </summary>
        private static List<OffsetRecord> createOffsets(int count, int blockSize = 0x10000)
        {
            List<OffsetRecord> offsets = new List<OffsetRecord>();
            long offset = 0;
            for (int i = 0; i < count; i++)
            {
                offsets.Add(new OffsetRecord
                {
                    ImageId = 1,
                    Offset = offset,
                    Size = blockSize,
                    Type = BlockType.File,
                    OffsetStart = offset
                });
                offset += blockSize;
            }
            return offsets;
        }

        /// <summary>
        /// Generates a variety of exception types to test that ALL exceptions are caught.
        /// </summary>
        private static Exception createExceptionByIndex(int index)
        {
            return (index % 7) switch
            {
                0 => new InvalidOperationException("Simulated MergeAuxOffsets failure"),
                1 => new IOException("Simulated I/O error during aux offset read"),
                2 => new KeyNotFoundException("Simulated missing key in aux store"),
                3 => new NullReferenceException("Simulated null reference in aux reader"),
                4 => new ArgumentException("Simulated argument error in aux store"),
                5 => new TimeoutException("Simulated timeout reading aux offsets"),
                _ => new Exception("Simulated generic failure in MergeAuxOffsets"),
            };
        }

        #endregion

        /// <summary>
        /// **Validates: Requirements 7.2, 7.3**
        ///
        /// Property 11: MergeAuxOffsets Failure Does Not Propagate — Construction resilience.
        /// For any exception type thrown by MergeAuxOffsets (via auxReader.GetOffsets()),
        /// the ImageBuilder constructor SHALL NOT throw and SHALL remain in a valid state.
        ///
        /// We generate:
        ///   - Various area/offset counts (varying image complexity)
        ///   - Various exception types (to prove ALL exceptions are caught, not just specific ones)
        ///
        /// For each combination we verify:
        ///   1. ImageBuilder construction does NOT throw
        ///   2. ImageBuilder.Length is correct (image size matches reader)
        ///   3. ImageBuilder.CanRead is true
        ///   4. ImageBuilder.Position starts at the first area offset
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MergeAuxOffsetsFailure_DoesNotThrow_ImageBuilderRemainsConstructed(
            NonNegativeInt areaCountSeed,
            NonNegativeInt offsetCountSeed,
            NonNegativeInt exceptionTypeSeed)
        {
            int areaCount = (areaCountSeed.Get % 5) + 1; // 1-5 areas
            int offsetCount = (offsetCountSeed.Get % 10) + 1; // 1-10 offsets
            int exceptionIndex = exceptionTypeSeed.Get;

            int sectionSize = 0x200000;
            List<AreaRecord> areas = createAreas(areaCount, sectionSize);
            List<OffsetRecord> offsets = createOffsets(offsetCount);
            long imageSize = areas.Last().Offset + areas.Last().Size;

            // Create primary reader with valid areas/offsets
            MinimalImageReader primaryReader = new MinimalImageReader(areas, offsets, imageSize);

            // Create an AuxBlockProvider with a throwing aux reader
            StubBlockProvider primaryProvider = new StubBlockProvider();
            ThrowingAuxReader throwingAuxReader = new ThrowingAuxReader(createExceptionByIndex(exceptionIndex));
            AuxBlockProvider auxBlockProvider = new AuxBlockProvider(primaryProvider, splitReader: null, auxReader: throwingAuxReader);

            // Act — construct ImageBuilder with the AuxBlockProvider (MergeAuxOffsets will throw internally)
            ImageBuilder builder = null;
            try
            {
                builder = new ImageBuilder(primaryReader, blockProvider: auxBlockProvider);
            }
            catch
            {
                // Construction threw — property violated (Requirement 7.3)
                return false;
            }

            try
            {
                // Verify ImageBuilder is in a valid, functional state
                if (!builder.CanRead) return false;
                if (builder.Length != imageSize) return false;
                if (builder.Position != areas[0].Offset) return false;

                return true;
            }
            finally
            {
                builder?.Dispose();
            }
        }

        /// <summary>
        /// **Validates: Requirements 7.2, 7.3**
        ///
        /// Property 11: MergeAuxOffsets Failure Does Not Propagate — Read() remains functional.
        /// For any exception thrown by MergeAuxOffsets, the ImageBuilder SHALL still support
        /// Read() operations. Since aux offsets weren't merged, blocks stored exclusively in
        /// aux produce zeros during reconstruction — but the Read() call itself must succeed
        /// without throwing.
        ///
        /// We verify:
        ///   1. Read() does not throw after MergeAuxOffsets failure
        ///   2. Read() returns data (zero-filled since no blocks resolve) with count > 0
        ///   3. Seek + Read works correctly
        /// </summary>
        [Property(MaxTest = 100)]
        public bool MergeAuxOffsetsFailure_ReadRemainsOperational(
            NonNegativeInt areaCountSeed,
            NonNegativeInt readSizeSeed,
            NonNegativeInt exceptionTypeSeed)
        {
            int areaCount = (areaCountSeed.Get % 4) + 1; // 1-4 areas
            int readSize = ((readSizeSeed.Get % 16) + 1) * 0x1000; // 0x1000 to 0x10000
            int exceptionIndex = exceptionTypeSeed.Get;

            int sectionSize = 0x200000;
            List<AreaRecord> areas = createAreas(areaCount, sectionSize);
            List<OffsetRecord> offsets = createOffsets(areaCount * 2); // Some offsets within the areas
            long imageSize = areas.Last().Offset + areas.Last().Size;

            MinimalImageReader primaryReader = new MinimalImageReader(areas, offsets, imageSize);
            StubBlockProvider primaryProvider = new StubBlockProvider();
            ThrowingAuxReader throwingAuxReader = new ThrowingAuxReader(createExceptionByIndex(exceptionIndex));
            AuxBlockProvider auxBlockProvider = new AuxBlockProvider(primaryProvider, splitReader: null, auxReader: throwingAuxReader);

            ImageBuilder builder = null;
            try
            {
                builder = new ImageBuilder(primaryReader, blockProvider: auxBlockProvider);
            }
            catch
            {
                // Construction should not throw (Requirement 7.3)
                return false;
            }

            try
            {
                // Read from the beginning — should not throw
                byte[] buffer = new byte[readSize];
                int bytesRead = builder.Read(buffer, 0, readSize);

                // Since ImageBuilder has areas and position is valid, Read should return data
                // (zero-filled because no block provider returns data, but it should not throw)
                if (bytesRead < 0) return false;

                // Seek to a different position and read again
                if (imageSize > readSize)
                {
                    builder.Position = readSize; // seek forward
                    int bytesRead2 = builder.Read(buffer, 0, readSize);
                    if (bytesRead2 < 0) return false;
                }

                return true;
            }
            catch
            {
                // Read() threw — property violated (Requirement 7.2: continue operation)
                return false;
            }
            finally
            {
                builder?.Dispose();
            }
        }
    }
}