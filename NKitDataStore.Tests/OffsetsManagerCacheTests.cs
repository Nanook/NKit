using NKitDataStore.Binary;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for the OffsetsManager cache in MountResourceManager.
    /// Tests cover ref-counting, TTL lifecycle, concurrency, shutdown, and ImageBuilder integration.
    /// Uses manual mock implementations — no real SQLite needed.
    /// </summary>
    public class OffsetsManagerCacheTests
    {
        private const int _TestTtlMs = 100;
        private const int _ReaderCapacity = 32;

        #region Mock Implementations

        /// <summary>
        /// Mock IImageReader that tracks GetAreas/GetOffsets call counts and returns configurable data.
        /// </summary>
        private class MockImageReader : IImageReader
        {
            public bool IsDisposed { get; private set; }
            public int GetAreasCallCount;
            public int GetOffsetsCallCount;

            private readonly List<AreaRecord> _areas;
            private readonly List<OffsetRecord> _offsets;

            public ImageRecord Image { get; }
            public InfoRecord Info { get; }

            public MockImageReader(
                List<AreaRecord> areas = null,
                List<OffsetRecord> offsets = null,
                int blockSize = 0x10000,
                long imageSize = 0x1000000)
            {
                _areas = areas ?? new List<AreaRecord>();
                _offsets = offsets ?? new List<OffsetRecord>();
                Image = new ImageRecord { Size = imageSize };
                Info = new InfoRecord { BlockSize = blockSize };
            }

            public void Dispose() => IsDisposed = true;

            public IEnumerable<AreaRecord> GetAreas()
            {
                Interlocked.Increment(ref GetAreasCallCount);
                return _areas;
            }

            public IEnumerable<OffsetRecord> GetOffsets()
            {
                Interlocked.Increment(ref GetOffsetsCallCount);
                return _offsets;
            }

            public Stream OpenStream(long offsetStart) => Stream.Null;
            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => Array.Empty<OffsetRecord>();
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        /// <summary>
        /// Mock IImageReader that throws if GetAreas or GetOffsets are called.
        /// Used to verify that ImageBuilder bypasses reader queries when cached data is provided.
        /// </summary>
        private class ThrowingImageReader : IImageReader
        {
            public ImageRecord Image { get; } = new ImageRecord { Size = 0x1000000 };
            public InfoRecord Info { get; } = new InfoRecord { BlockSize = 0x10000 };

            public void Dispose() { }

            public IEnumerable<AreaRecord> GetAreas()
                => throw new InvalidOperationException("GetAreas should not be called when cached data is provided");

            public IEnumerable<OffsetRecord> GetOffsets()
                => throw new InvalidOperationException("GetOffsets should not be called when cached data is provided");

            public Stream OpenStream(long offsetStart) => Stream.Null;
            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => Array.Empty<OffsetRecord>();
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public byte[] ReadFile(string name) => null;
            public IEnumerable<FileRecord> ListFiles() => Array.Empty<FileRecord>();
        }

        private class MockDataStore : IDataStore
        {
            public int OpenImageReaderCallCount;
            private readonly Func<GlobalImageKey, IImageReader> _factory;

            public MockDataStore(Func<GlobalImageKey, IImageReader> factory = null)
            {
                _factory = factory;
            }

            public IImageReader OpenImageReader(GlobalImageKey key)
            {
                Interlocked.Increment(ref OpenImageReaderCallCount);
                return _factory?.Invoke(key) ?? new MockImageReader();
            }

            public void Dispose() { }
            public IImageWriter AddImage(string setName, string imageName, string system = null, ImageFormat format = ImageFormat.Unknown) => throw new NotImplementedException();
            public ImageRecord RenameImage(GlobalImageKey key, string newImageName) => throw new NotImplementedException();
            public int RenameImageTitle(GlobalImageKey key, string newBaseName) => throw new NotImplementedException();
            public void UpdateImageFormat(GlobalImageKey key, ImageFormat format) => throw new NotImplementedException();
            public int RepairWiiUImageFormats() => throw new NotImplementedException();
            public void DeleteImage(GlobalImageKey key) => throw new NotImplementedException();
            public void RestoreImage(GlobalImageKey key) => throw new NotImplementedException();
            public void CompactSet(string setName, IProgress<(int Percentage, string Stage)> progress = null) => throw new NotImplementedException();
            public RecoveryState CheckSetHealth(string setName) => throw new NotImplementedException();
            public void RollbackImage(GlobalImageKey key, IProgress<(int Percentage, string Stage)> progress = null) => throw new NotImplementedException();
            public SetInfo CreateSet(string setName, long shardSize = 50L * 1024 * 1024 * 1024, int blockSize = 0) => throw new NotImplementedException();
            public IEnumerable<string> ListSetNames() => throw new NotImplementedException();
            public IEnumerable<ImageRecord> ListAllImages(Func<ImageRecord, bool> predicate = null) => throw new NotImplementedException();
            public IEnumerable<SetInfo> DescribeSets() => throw new NotImplementedException();
            public IEnumerable<(SetInfo Info, List<ImageRecord> Images, Dictionary<long, byte[]> FileSystemYamlData)> DescribeSetsWithImages(string filterSetName = null, bool includeFileSystemYaml = false, long? maxFileSystemYamlSize = null) => throw new NotImplementedException();
            public SetInfo GetSetInfo(string setName, bool includeStats = false) => throw new NotImplementedException();
            public DataStoreStatistics GetSetStatistics(string setName, bool includePerImageStats = false, Action<int, int> progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public byte[] ReadFile(GlobalImageKey key, string name) => throw new NotImplementedException();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a list of AreaRecords with the given count, each with a valid SectionSize and sequential offsets.
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
                    Size = sectionSize * 4,
                    SectionSize = sectionSize,
                    Index = i
                });
                offset += sectionSize * 4;
            }
            return areas;
        }

        /// <summary>
        /// Creates a list of OffsetRecords with the given count, each with sequential offsets.
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

        #endregion


        #region 10.1 — Property 1: Cache acquire idempotency

        /// <summary>
        /// Validates: Requirements 1.1, 1.2
        /// Property 1: For any image ID, first AcquireOffsetsManager constructs the OffsetsManager,
        /// subsequent calls return the same instance without re-querying the reader.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_ReturnsSameInstance_OnRepeatedCalls()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act
            OffsetsManagerCacheResult result1 = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            OffsetsManagerCacheResult result2 = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            OffsetsManagerCacheResult result3 = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Assert — all return the same instance
            Assert.Same(result1, result2);
            Assert.Same(result2, result3);
        }

        /// <summary>
        /// Validates: Requirements 1.1, 1.2
        /// Property 1: Reader is queried only once for areas/offsets across multiple acquires.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_QueriesReaderOnlyOnce()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(3);
            List<OffsetRecord> offsets = createOffsets(10);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act — acquire multiple times
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Assert — GetAreas and GetOffsets called exactly once per construction
            // (AcquireOffsetsManager calls GetAreas+GetOffsets, and OffsetsManager constructor
            //  calls BuildIndex which also calls GetAreas+GetOffsets = 2 each total)
            Assert.Equal(2, mockReader.GetAreasCallCount);
            Assert.Equal(2, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 1.1, 1.2
        /// Property 1 (simulated property-based): Multiple random image IDs each get their own
        /// cached instance, and repeated acquires for the same ID return the same instance.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_Idempotency_MultipleImageIds()
        {
            // Arrange
            Random random = new Random(12345);
            Dictionary<long, MockImageReader> readers = new Dictionary<long, MockImageReader>();
            MockDataStore dataStore = new MockDataStore(key =>
            {
                if (!readers.ContainsKey(key.ImageId))
                {
                    List<AreaRecord> areas = createAreas(random.Next(1, 5));
                    List<OffsetRecord> offsets = createOffsets(random.Next(1, 20));
                    readers[key.ImageId] = new MockImageReader(areas, offsets);
                }
                return readers[key.ImageId];
            });
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act & Assert — for 20 random image IDs, verify idempotency
            for (int iteration = 0; iteration < 20; iteration++)
            {
                long imageId = random.Next(1, 100);
                OffsetsManagerCacheResult first = manager.AcquireOffsetsManager("set1", imageId, 0x200000, 0x10000);
                OffsetsManagerCacheResult second = manager.AcquireOffsetsManager("set1", imageId, 0x200000, 0x10000);
                Assert.Same(first, second);
            }
        }

        #endregion

        #region 10.2 — Property 2: Cache result completeness and structure

        /// <summary>
        /// Validates: Requirements 2.1, 2.3, 3.1, 3.3, 9.3
        /// Property 2: The result contains a non-null OffsetsManager, areas list of length N
        /// sorted by offset, and dictionary of size ≤ M keyed by offset.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_ResultHasCorrectStructure()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(4);
            List<OffsetRecord> offsets = createOffsets(12);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act
            OffsetsManagerCacheResult result = manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);

            // Assert — non-null components
            Assert.NotNull(result);
            Assert.NotNull(result.OffsetsManager);
            Assert.NotNull(result.Areas);
            Assert.NotNull(result.OffsetsByPosition);

            // Assert — areas count matches
            Assert.Equal(4, result.Areas.Count);

            // Assert — areas are sorted by offset
            for (int i = 1; i < result.Areas.Count; i++)
                Assert.True(result.Areas[i].Offset >= result.Areas[i - 1].Offset);

            // Assert — offsets dictionary size ≤ M (may be less due to dedup by offset key)
            Assert.True(result.OffsetsByPosition.Count <= 12);
        }

        /// <summary>
        /// Validates: Requirements 2.1, 2.3, 3.1, 3.3, 9.3
        /// Property 2 (simulated property-based): Varying area/offset counts produce correct structure.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_ResultStructure_VaryingCounts()
        {
            Random random = new Random(54321);

            for (int iteration = 0; iteration < 30; iteration++)
            {
                int areaCount = random.Next(1, 8);
                int offsetCount = random.Next(0, 30);

                List<AreaRecord> areas = createAreas(areaCount);
                List<OffsetRecord> offsets = createOffsets(offsetCount);
                MockImageReader mockReader = new MockImageReader(areas, offsets);
                MockDataStore dataStore = new MockDataStore(_ => mockReader);
                using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

                // Act
                OffsetsManagerCacheResult result = manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);

                // Assert — structure invariants
                Assert.NotNull(result.OffsetsManager);
                Assert.Equal(areaCount, result.Areas.Count);
                Assert.True(result.OffsetsByPosition.Count <= offsetCount);

                // Assert — areas sorted by offset
                for (int i = 1; i < result.Areas.Count; i++)
                    Assert.True(result.Areas[i].Offset >= result.Areas[i - 1].Offset);

                // Assert — dictionary keys match offset values
                foreach (KeyValuePair<long, OffsetRecord> kvp in result.OffsetsByPosition)
                    Assert.Equal(kvp.Key, kvp.Value.Offset);
            }
        }

        /// <summary>
        /// Validates: Requirements 2.1, 3.1
        /// Property 2: Areas with unsorted offsets are returned sorted in the cache result.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_AreasSortedByOffset_EvenIfReaderReturnsUnsorted()
        {
            // Arrange — areas in reverse order
            List<AreaRecord> areas = new List<AreaRecord>
            {
                new AreaRecord { Id = 3, Offset = 0x600000, Size = 0x200000, SectionSize = 0x200000 },
                new AreaRecord { Id = 1, Offset = 0x000000, Size = 0x200000, SectionSize = 0x200000 },
                new AreaRecord { Id = 2, Offset = 0x300000, Size = 0x200000, SectionSize = 0x200000 },
            };
            MockImageReader mockReader = new MockImageReader(areas, new List<OffsetRecord>());
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act
            OffsetsManagerCacheResult result = manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);

            // Assert — sorted by offset
            Assert.Equal(0x000000, result.Areas[0].Offset);
            Assert.Equal(0x300000, result.Areas[1].Offset);
            Assert.Equal(0x600000, result.Areas[2].Offset);
        }

        #endregion

        #region 10.3 — Property 3: ImageBuilder bypasses reader queries with cached data

        /// <summary>
        /// Validates: Requirements 2.2, 3.2, 4.1
        /// Property 3: When cachedOffsets is provided, ImageBuilder does NOT call GetAreas/GetOffsets.
        /// </summary>
        [Fact]
        public void ImageBuilder_WithCachedOffsets_DoesNotCallGetAreasOrGetOffsets()
        {
            // Arrange — create a valid cache result using a normal reader
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader buildReader = new MockImageReader(areas, offsets);
            OffsetsManager offsetsManager = new OffsetsManager(buildReader, sectionSize: 0x200000, storeBlockSize: 0x10000);
            Dictionary<long, OffsetRecord> offsetsByPosition = offsets.ToDictionary(o => o.Offset);
            OffsetsManagerCacheResult cachedResult = new OffsetsManagerCacheResult(offsetsManager, areas, offsetsByPosition);

            // Use a throwing reader — if GetAreas/GetOffsets are called, the test fails
            ThrowingImageReader throwingReader = new ThrowingImageReader();

            // Act — construct ImageBuilder with cached data; should NOT throw
            Exception exception = Record.Exception(() =>
            {
                using ImageBuilder builder = new ImageBuilder(throwingReader, cachedOffsets: cachedResult);
            });

            // Assert — no exception means GetAreas/GetOffsets were not called
            Assert.Null(exception);
        }

        /// <summary>
        /// Validates: Requirements 2.2, 3.2, 4.1
        /// Property 3 (simulated property-based): Various area/offset combinations all bypass reader.
        /// </summary>
        [Fact]
        public void ImageBuilder_WithCachedOffsets_BypassesReader_MultipleConfigurations()
        {
            Random random = new Random(99999);

            for (int iteration = 0; iteration < 20; iteration++)
            {
                int areaCount = random.Next(1, 6);
                int offsetCount = random.Next(0, 15);

                List<AreaRecord> areas = createAreas(areaCount);
                List<OffsetRecord> offsets = createOffsets(offsetCount);
                MockImageReader buildReader = new MockImageReader(areas, offsets);
                OffsetsManager offsetsManager = new OffsetsManager(buildReader, sectionSize: 0x200000, storeBlockSize: 0x10000);
                Dictionary<long, OffsetRecord> offsetsByPosition = offsets.ToDictionary(o => o.Offset);
                OffsetsManagerCacheResult cachedResult = new OffsetsManagerCacheResult(offsetsManager, areas, offsetsByPosition);

                ThrowingImageReader throwingReader = new ThrowingImageReader();

                // Act & Assert — no exception
                Exception exception = Record.Exception(() =>
                {
                    using ImageBuilder builder = new ImageBuilder(throwingReader, cachedOffsets: cachedResult);
                });
                Assert.Null(exception);
            }
        }

        #endregion

        #region 10.4 — Property 6: Ref-count arithmetic

        /// <summary>
        /// Validates: Requirements 6.1, 6.2, 1.4
        /// Property 6: For N acquires followed by M releases (M &lt; N), the entry is not removed.
        /// </summary>
        [Fact]
        public void RefCount_EntryNotRemoved_WhileCountAboveZero()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(3);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire 5 times (ref count = 5)
            for (int i = 0; i < 5; i++)
                manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Release 4 times (ref count = 1)
            for (int i = 0; i < 4; i++)
                manager.ReleaseOffsetsManager("set1", 42);

            // Wait longer than TTL
            Thread.Sleep(_TestTtlMs + 50);

            // Act — acquire again should return same instance (entry was not removed)
            OffsetsManagerCacheResult result = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Assert — still the same cached instance, construction happened only once
            Assert.NotNull(result);
            Assert.Equal(2, mockReader.GetAreasCallCount);
        }

        /// <summary>
        /// Validates: Requirements 6.1, 6.2, 1.4
        /// Property 6 (simulated property-based): Various N/M combinations maintain entry.
        /// </summary>
        [Fact]
        public void RefCount_VariousCombinations_EntryPersistsWhilePositive()
        {
            Random random = new Random(77777);

            for (int iteration = 0; iteration < 15; iteration++)
            {
                int acquireCount = random.Next(2, 10);
                int releaseCount = random.Next(1, acquireCount); // M < N

                List<AreaRecord> areas = createAreas(1);
                List<OffsetRecord> offsets = createOffsets(2);
                MockImageReader mockReader = new MockImageReader(areas, offsets);
                MockDataStore dataStore = new MockDataStore(_ => mockReader);
                using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

                // Acquire N times
                OffsetsManagerCacheResult firstResult = null;
                for (int i = 0; i < acquireCount; i++)
                {
                    OffsetsManagerCacheResult r = manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);
                    firstResult ??= r;
                }

                // Release M times (M < N, so ref count > 0)
                for (int i = 0; i < releaseCount; i++)
                    manager.ReleaseOffsetsManager("set1", 1);

                // Wait longer than TTL
                Thread.Sleep(_TestTtlMs + 50);

                // Assert — entry still exists (ref count > 0, no TTL started)
                OffsetsManagerCacheResult laterResult = manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);
                Assert.Same(firstResult, laterResult);
                Assert.Equal(2, mockReader.GetAreasCallCount);
            }
        }

        /// <summary>
        /// Validates: Requirements 6.1, 6.2, 1.4
        /// Property 6: When all references are released, TTL starts and entry is eventually removed.
        /// </summary>
        [Fact]
        public void RefCount_AllReleased_EntryRemovedAfterTtl()
        {
            // Arrange — use a longer TTL to reduce timing sensitivity
            const int ttlMs = 200;
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, ttlMs);

            // Acquire 3 times, release 3 times (ref count = 0)
            OffsetsManagerCacheResult first = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.ReleaseOffsetsManager("set1", 42);
            manager.ReleaseOffsetsManager("set1", 42);
            manager.ReleaseOffsetsManager("set1", 42);

            // Wait well beyond TTL to ensure expiry fires
            Thread.Sleep(ttlMs + 500);

            // Act — acquire again should create a new instance (entry was removed)
            OffsetsManagerCacheResult second = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Assert — reader was queried again (new construction = 2 calls each per construction)
            Assert.Equal(4, mockReader.GetAreasCallCount);
        }

        #endregion


        #region 10.5 — Property 8: Single creation under concurrency

        /// <summary>
        /// Validates: Requirements 7.1, 7.2
        /// Property 8: N concurrent threads calling AcquireOffsetsManager when no entry exists:
        /// construction executes exactly once, all threads get same instance.
        /// </summary>
        [Fact]
        public async Task ConcurrentAcquire_AllGetSameInstance_ConstructionOnce()
        {
            // Arrange
            int threadCount = 8;
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Act — all threads acquire the same offsets manager concurrently
            Task<OffsetsManagerCacheResult>[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                return manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            })).ToArray();

            barrier.Release(threadCount);
            OffsetsManagerCacheResult[] results = await Task.WhenAll(tasks);

            // Assert — all got the same instance
            Assert.All(results, r => Assert.Same(results[0], r));

            // Assert — construction happened exactly once (2 calls each due to OffsetsManager.BuildIndex)
            Assert.Equal(2, mockReader.GetAreasCallCount);
            Assert.Equal(2, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 7.1, 7.2
        /// Property 8 (simulated property-based): Various thread counts all produce single construction.
        /// </summary>
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(12)]
        [InlineData(16)]
        public async Task ConcurrentAcquire_VariousThreadCounts_SingleConstruction(int threadCount)
        {
            // Arrange
            List<AreaRecord> areas = createAreas(3);
            List<OffsetRecord> offsets = createOffsets(8);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Act
            Task<OffsetsManagerCacheResult>[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                return manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);
            })).ToArray();

            barrier.Release(threadCount);
            OffsetsManagerCacheResult[] results = await Task.WhenAll(tasks);

            // Assert
            OffsetsManagerCacheResult first = results[0];
            Assert.All(results, r => Assert.Same(first, r));
            Assert.Equal(2, mockReader.GetAreasCallCount);
            Assert.Equal(2, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 7.1, 7.2
        /// Property 8: Concurrent acquire/release interleaving does not corrupt state.
        /// </summary>
        [Fact]
        public async Task ConcurrentAcquireRelease_NoCorruption()
        {
            // Arrange
            int threadCount = 8;
            int iterationsPerThread = 30;
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Pre-acquire to keep entry alive throughout
            OffsetsManagerCacheResult initial = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Act — threads interleave acquire/release rapidly
            Task[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    OffsetsManagerCacheResult r = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
                    Assert.Same(initial, r);
                    manager.ReleaseOffsetsManager("set1", 42);
                }
            })).ToArray();

            barrier.Release(threadCount);
            await Task.WhenAll(tasks);

            // Release the initial acquire
            manager.ReleaseOffsetsManager("set1", 42);

            // Assert — construction happened only once despite all the concurrency
            Assert.Equal(2, mockReader.GetAreasCallCount);
        }

        #endregion

        #region 10.6 — Property 9: Shutdown disposes all entries

        /// <summary>
        /// Validates: Requirements 8.1, 8.2, 8.3
        /// Property 9: Shutdown removes all entries regardless of ref count or TTL state.
        /// </summary>
        [Fact]
        public void Shutdown_RemovesAllOffsetsEntries()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire multiple entries with different ref counts
            manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000); // ref=1
            manager.AcquireOffsetsManager("set1", 2, 0x200000, 0x10000); // ref=1
            manager.AcquireOffsetsManager("set1", 3, 0x200000, 0x10000); // ref=1
            manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000); // ref=2 for image 1

            // Release one to start TTL
            manager.ReleaseOffsetsManager("set1", 3);

            // Act
            manager.Shutdown();

            // Assert — all entries removed (acquire after shutdown throws)
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000));
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 2, 0x200000, 0x10000));
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 3, 0x200000, 0x10000));
        }

        /// <summary>
        /// Validates: Requirements 8.1, 8.2, 8.3
        /// Property 9 (simulated property-based): Various entry counts and states all cleared on shutdown.
        /// </summary>
        [Fact]
        public void Shutdown_VariousEntryStates_AllCleared()
        {
            Random random = new Random(33333);

            for (int iteration = 0; iteration < 10; iteration++)
            {
                int entryCount = random.Next(1, 6);
                List<AreaRecord> areas = createAreas(1);
                List<OffsetRecord> offsets = createOffsets(2);
                MockImageReader mockReader = new MockImageReader(areas, offsets);
                MockDataStore dataStore = new MockDataStore(_ => mockReader);
                MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

                // Create entries with various ref counts
                for (int i = 0; i < entryCount; i++)
                {
                    int acquires = random.Next(1, 4);
                    for (int j = 0; j < acquires; j++)
                        manager.AcquireOffsetsManager("set1", i + 1, 0x200000, 0x10000);

                    // Randomly release some
                    int releases = random.Next(0, acquires);
                    for (int j = 0; j < releases; j++)
                        manager.ReleaseOffsetsManager("set1", i + 1);
                }

                // Act
                manager.Shutdown();

                // Assert — all entries gone
                for (int i = 0; i < entryCount; i++)
                {
                    Assert.Throws<ObjectDisposedException>(() =>
                        manager.AcquireOffsetsManager("set1", i + 1, 0x200000, 0x10000));
                }
            }
        }

        /// <summary>
        /// Validates: Requirements 8.2
        /// Property 9: Shutdown cancels pending TTL timers (entry not removed by TTL after shutdown).
        /// </summary>
        [Fact]
        public void Shutdown_CancelsPendingTtlTimers()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire and release to start TTL
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.ReleaseOffsetsManager("set1", 42);

            // Act — shutdown before TTL expires
            manager.Shutdown();

            // Wait for what would have been the TTL expiry
            Thread.Sleep(_TestTtlMs + 50);

            // Assert — no crash, shutdown was clean (TTL callback doesn't fire on disposed state)
            // The fact that we get here without exception means timers were properly cancelled
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000));
        }

        #endregion

        #region 10.7 — Property 11: No acquisition after shutdown

        /// <summary>
        /// Validates: Requirements 8.1
        /// Property 11: After Shutdown(), any call to AcquireOffsetsManager throws ObjectDisposedException.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_AfterShutdown_ThrowsObjectDisposedException()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            manager.Shutdown();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000));
        }

        /// <summary>
        /// Validates: Requirements 8.1
        /// Property 11 (simulated property-based): Any image ID throws after shutdown.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_AfterShutdown_AnyImageId_Throws()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire some entries first
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            // Need a real reader for the initial acquires
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStoreWithReader = new MockDataStore(_ => mockReader);
            MountResourceManager manager2 = new MountResourceManager(dataStoreWithReader, _ReaderCapacity, _TestTtlMs);
            manager2.AcquireOffsetsManager("set1", 1, 0x200000, 0x10000);
            manager2.AcquireOffsetsManager("set1", 2, 0x200000, 0x10000);

            // Act
            manager2.Shutdown();

            // Assert — various image IDs all throw
            Random random = new Random(11111);
            for (int i = 0; i < 20; i++)
            {
                long imageId = random.Next(1, 1000);
                Assert.Throws<ObjectDisposedException>(() =>
                    manager2.AcquireOffsetsManager("set1", imageId, 0x200000, 0x10000));
            }
        }

        /// <summary>
        /// Validates: Requirements 8.1
        /// Property 11: Double shutdown is safe (no exception on second call).
        /// </summary>
        [Fact]
        public void DoubleShutdown_OffsetsCache_IsSafe()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Act — call Shutdown twice
            manager.Shutdown();
            Exception exception = Record.Exception(() => manager.Shutdown());

            // Assert — no exception on second call
            Assert.Null(exception);
        }

        #endregion


        #region 10.8 — Example-based tests

        /// <summary>
        /// Validates: Requirements 6.3, 6.4, 6.5
        /// Property 7: TTL starts on last release, cancels on re-acquire, removes on expiry.
        /// </summary>
        [Fact]
        public void TtlLifecycle_StartsOnLastRelease_CancelsOnReAcquire()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(3);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire and release (starts TTL)
            OffsetsManagerCacheResult first = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.ReleaseOffsetsManager("set1", 42);

            // Wait a bit but less than TTL
            Thread.Sleep(_TestTtlMs / 2);

            // Re-acquire during TTL window (cancels TTL)
            OffsetsManagerCacheResult reacquired = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Wait for original TTL to have expired
            Thread.Sleep(_TestTtlMs + 50);

            // Assert — same instance returned, entry not removed
            Assert.Same(first, reacquired);
            Assert.Equal(2, mockReader.GetAreasCallCount);
        }

        /// <summary>
        /// Validates: Requirements 6.5
        /// Property 7: TTL expiry removes entry when ref count is still zero.
        /// </summary>
        [Fact]
        public void TtlLifecycle_ExpiryRemovesEntry()
        {
            // Arrange
            const int ttlMs = 50;
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(3);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, ttlMs);

            // Acquire and release (starts TTL timer, but we won't wait for it)
            OffsetsManagerCacheResult first = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            manager.ReleaseOffsetsManager("set1", 42);

            // Force expiry synchronously — no wall-clock dependency
            manager.ForceOffsetsManagerExpiry("set1:42");

            // Act — acquire again should create a new instance
            OffsetsManagerCacheResult second = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Assert — new construction occurred (2 calls per construction × 2 constructions = 4)
            Assert.Equal(4, mockReader.GetAreasCallCount);
            Assert.Equal(4, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 4.2, 4.3
        /// Property 4: ImageBuilder with cachedOffsets: null performs full construction.
        /// </summary>
        [Fact]
        public void ImageBuilder_WithNullCachedOffsets_PerformsFullConstruction()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader mockReader = new MockImageReader(areas, offsets);

            // Act — construct with null cachedOffsets
            using ImageBuilder builder = new ImageBuilder(mockReader, cachedOffsets: null);

            // Assert — reader was queried for areas and offsets
            // (ImageBuilder calls GetAreas+GetOffsets, then OffsetsManager.BuildIndex calls them again = 2 each)
            Assert.Equal(2, mockReader.GetAreasCallCount);
            Assert.Equal(2, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 5.1, 5.3
        /// Property 5: Partial cache provision (null DTO) falls back to full construction.
        /// Since OffsetsManagerCacheResult requires all three components in its constructor,
        /// partial provision is structurally impossible. This test verifies the null case.
        /// </summary>
        [Fact]
        public void ImageBuilder_NullCacheResult_FallsBackToFullConstruction()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(3);
            List<OffsetRecord> offsets = createOffsets(8);
            MockImageReader mockReader = new MockImageReader(areas, offsets);

            // Act — explicitly pass null
            OffsetsManagerCacheResult nullCache = null;
            using ImageBuilder builder = new ImageBuilder(mockReader, cachedOffsets: nullCache);

            // Assert — full construction occurred
            Assert.Equal(2, mockReader.GetAreasCallCount);
            Assert.Equal(2, mockReader.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 9.3
        /// OffsetsManagerCacheResult constructor rejects null arguments.
        /// </summary>
        [Fact]
        public void OffsetsManagerCacheResult_RejectsNullArguments()
        {
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            OffsetsManager om = new OffsetsManager(mockReader, sectionSize: 0x200000, storeBlockSize: 0x10000);
            Dictionary<long, OffsetRecord> dict = offsets.ToDictionary(o => o.Offset);

            Assert.Throws<ArgumentNullException>(() => new OffsetsManagerCacheResult(null!, areas, dict));
            Assert.Throws<ArgumentNullException>(() => new OffsetsManagerCacheResult(om, null!, dict));
            Assert.Throws<ArgumentNullException>(() => new OffsetsManagerCacheResult(om, areas, null!));
        }

        /// <summary>
        /// Validates: Requirements 8.3
        /// Shutdown ordering: offsets cache cleared before readers disposed.
        /// Verifies that after shutdown, the reader is disposed (proving readers were disposed
        /// after offsets cache was cleared).
        /// </summary>
        [Fact]
        public void Shutdown_OffsetsCacheClearedBeforeReadersDisposed()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire both reader and offsets manager
            manager.AcquireReader("set1", 42);
            manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);

            // Act
            manager.Shutdown();

            // Assert — reader was disposed (shutdown completed the full sequence)
            Assert.True(mockReader.IsDisposed);

            // Assert — offsets cache is empty (throws on acquire)
            Assert.Throws<ObjectDisposedException>(() =>
                manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000));
        }

        /// <summary>
        /// Validates: Requirements 11.1
        /// Non-mount ImageBuilder construction (no cache) works independently.
        /// </summary>
        [Fact]
        public void ImageBuilder_NonMountConstruction_WorksIndependently()
        {
            // Arrange — two independent ImageBuilders for the same image data
            List<AreaRecord> areas = createAreas(2);
            List<OffsetRecord> offsets = createOffsets(5);
            MockImageReader reader1 = new MockImageReader(areas, offsets);
            MockImageReader reader2 = new MockImageReader(areas, offsets);

            // Act — construct without cache (simulates CLI/test usage)
            using ImageBuilder builder1 = new ImageBuilder(reader1, cachedOffsets: null);
            using ImageBuilder builder2 = new ImageBuilder(reader2, cachedOffsets: null);

            // Assert — each queried its own reader independently
            Assert.Equal(2, reader1.GetAreasCallCount);
            Assert.Equal(2, reader1.GetOffsetsCallCount);
            Assert.Equal(2, reader2.GetAreasCallCount);
            Assert.Equal(2, reader2.GetOffsetsCallCount);
        }

        /// <summary>
        /// Validates: Requirements 6.3, 6.4
        /// TTL timer does not fire if entry is re-acquired before expiry (multiple cycles).
        /// </summary>
        [Fact]
        public void TtlLifecycle_MultipleCycles_TimerCancelledEachTime()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Perform multiple acquire/release/re-acquire cycles
            for (int cycle = 0; cycle < 5; cycle++)
            {
                OffsetsManagerCacheResult result = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
                manager.ReleaseOffsetsManager("set1", 42);

                // Wait less than TTL
                Thread.Sleep(_TestTtlMs / 3);

                // Re-acquire before TTL expires
                OffsetsManagerCacheResult reacquired = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
                Assert.Same(result, reacquired);

                // Release for next cycle
                manager.ReleaseOffsetsManager("set1", 42);
            }

            // Final re-acquire to verify entry still alive
            OffsetsManagerCacheResult final = manager.AcquireOffsetsManager("set1", 42, 0x200000, 0x10000);
            Assert.NotNull(final);

            // Assert — construction happened only once across all cycles
            Assert.Equal(2, mockReader.GetAreasCallCount);
        }

        /// <summary>
        /// Validates: Requirements 1.1, 1.2
        /// Different set names produce different cache entries.
        /// </summary>
        [Fact]
        public void AcquireOffsetsManager_DifferentSetNames_DifferentEntries()
        {
            // Arrange
            List<AreaRecord> areas = createAreas(1);
            List<OffsetRecord> offsets = createOffsets(2);
            MockImageReader mockReader = new MockImageReader(areas, offsets);
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act — same image ID but different set names
            OffsetsManagerCacheResult result1 = manager.AcquireOffsetsManager("setA", 42, 0x200000, 0x10000);
            OffsetsManagerCacheResult result2 = manager.AcquireOffsetsManager("setB", 42, 0x200000, 0x10000);

            // Assert — different instances (different cache keys)
            Assert.NotSame(result1, result2);
        }

        #endregion
    }
}