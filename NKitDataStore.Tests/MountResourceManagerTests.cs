using NKitDataStore.Binary;
using NKitDataStore.Interfaces;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Unit tests for MountResourceManager ref-counting, TTL disposal, and buffer cache pooling.
    /// Uses manual mock implementations — no real SQLite needed.
    /// </summary>
    public class MountResourceManagerTests
    {
        private const int _TestTtlMs = 100;
        private const int _ReaderCapacity = 32;

        #region Mock Implementations

        private class MockImageReader : IImageReader
        {
            public bool IsDisposed { get; private set; }
            public int DisposeCount;
            public ImageRecord Image { get; } = new ImageRecord();
            public InfoRecord Info { get; } = new InfoRecord();

            public void Dispose()
            {
                IsDisposed = true;
                Interlocked.Increment(ref DisposeCount);
            }

            public Stream OpenStream(long offsetStart) => Stream.Null;
            public Stream OpenStream(DataStride stride, long offsetStart) => Stream.Null;
            public IEnumerable<OffsetRecord> GetOffsets() => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsets(long offsetStart) => Array.Empty<OffsetRecord>();
            public IEnumerable<OffsetRecord> GetOffsetsInRange(long startOffset, long length) => Array.Empty<OffsetRecord>();
            public BlockRecord GetBlock(BlockKey key) => null;
            public Stream OpenBlockStream(BlockKey key) => null;
            public IEnumerable<AreaRecord> GetAreas() => Array.Empty<AreaRecord>();
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

        #region Ref-counting tests

        [Fact]
        public void AcquireReader_ReturnsSameInstance_ForSameKey()
        {
            // Arrange
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act
            IImageReader reader1 = manager.AcquireReader("set1", 42);
            IImageReader reader2 = manager.AcquireReader("set1", 42);

            // Assert
            Assert.Same(reader1, reader2);
            Assert.Equal(1, dataStore.OpenImageReaderCallCount);
        }

        [Fact]
        public void ReleaseReader_DoesNotDispose_WhileRefCountAboveZero()
        {
            // Arrange
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire twice (ref count = 2)
            manager.AcquireReader("set1", 42);
            manager.AcquireReader("set1", 42);

            // Act — release once (ref count = 1)
            manager.ReleaseReader("set1", 42);

            // Wait longer than TTL to ensure no premature disposal
            Thread.Sleep(_TestTtlMs + 50);

            // Assert — reader should NOT be disposed (ref count still > 0)
            Assert.False(mockReader.IsDisposed);
        }

        [Fact]
        public void Reader_DisposedAfterRefCountZero_AndTtlExpires()
        {
            // Arrange
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire once (ref count = 1)
            manager.AcquireReader("set1", 42);

            // Release (ref count = 0, TTL timer starts)
            manager.ReleaseReader("set1", 42);

            // Assert — not disposed immediately
            Assert.False(mockReader.IsDisposed);

            // Wait for TTL to expire
            Thread.Sleep(_TestTtlMs + 100);

            // Assert — disposed after TTL
            Assert.True(mockReader.IsDisposed);
        }

        [Fact]
        public void ReAcquire_DuringTtlWindow_CancelsDisposal_ReturnsSameInstance()
        {
            // Arrange
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire and release (starts TTL)
            manager.AcquireReader("set1", 42);
            manager.ReleaseReader("set1", 42);

            // Wait a bit but less than TTL
            Thread.Sleep(_TestTtlMs / 2);

            // Re-acquire during TTL window
            IImageReader reacquired = manager.AcquireReader("set1", 42);

            // Wait for original TTL to have expired
            Thread.Sleep(_TestTtlMs + 50);

            // Assert — same instance returned, not disposed
            Assert.Same(mockReader, reacquired);
            Assert.False(mockReader.IsDisposed);
            Assert.Equal(1, dataStore.OpenImageReaderCallCount);
        }

        #endregion

        #region Buffer cache tests

        [Fact]
        public void AcquireBufferCache_ReturnsSameCache_ForSameImageId()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Act
            ImageBufferCache cache1 = manager.AcquireBufferCache(100, 4096, 16);
            ImageBufferCache cache2 = manager.AcquireBufferCache(100, 4096, 16);

            // Assert
            Assert.Same(cache1, cache2);
        }

        [Fact]
        public void AcquireBufferCache_IncrementsRefCount_OnRepeatedCalls()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire 3 times
            ImageBufferCache cache = manager.AcquireBufferCache(100, 4096, 16);
            manager.AcquireBufferCache(100, 4096, 16);
            manager.AcquireBufferCache(100, 4096, 16);

            // Release twice (ref count should be 1)
            manager.ReleaseBufferCache(100);
            manager.ReleaseBufferCache(100);

            // Wait longer than TTL
            Thread.Sleep(_TestTtlMs + 100);

            // Assert — cache should still be alive (ref count = 1, no TTL started)
            ImageBufferCache cacheAgain = manager.AcquireBufferCache(100, 4096, 16);
            Assert.Same(cache, cacheAgain);
        }

        #endregion

        #region Thread-safety tests

        [Fact]
        public async Task ConcurrentAcquireReader_AllGetSameInstance_FactoryCalledOnce()
        {
            // Arrange
            int threadCount = 8;
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Act — all threads acquire the same reader concurrently
            Task<IImageReader>[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                return manager.AcquireReader("set1", 42);
            })).ToArray();

            barrier.Release(threadCount);
            IImageReader[] results = await Task.WhenAll(tasks);

            // Assert — all got the same instance, factory called exactly once
            Assert.All(results, r => Assert.Same(mockReader, r));
            Assert.Equal(1, dataStore.OpenImageReaderCallCount);
        }

        [Fact]
        public async Task ConcurrentAcquireBufferCache_AllGetSameInstance()
        {
            // Arrange
            int threadCount = 8;
            MockDataStore dataStore = new MockDataStore();
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Act — all threads acquire the same buffer cache concurrently
            Task<ImageBufferCache>[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                return manager.AcquireBufferCache(100, 4096, 16);
            })).ToArray();

            barrier.Release(threadCount);
            ImageBufferCache[] results = await Task.WhenAll(tasks);

            // Assert — all got the same instance
            ImageBufferCache firstCache = results[0];
            Assert.All(results, r => Assert.Same(firstCache, r));
        }

        [Fact]
        public async Task InterleavedAcquireRelease_NoDoubleDispose()
        {
            // Arrange
            int threadCount = 8;
            int iterationsPerThread = 50;
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Pre-acquire once to keep the reader alive throughout the test
            manager.AcquireReader("set1", 42);

            // Act — threads interleave acquire/release rapidly
            Task[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    manager.AcquireReader("set1", 42);
                    manager.ReleaseReader("set1", 42);
                }
            })).ToArray();

            barrier.Release(threadCount);
            await Task.WhenAll(tasks);

            // Release the initial acquire
            manager.ReleaseReader("set1", 42);

            // Wait for TTL to expire
            Thread.Sleep(_TestTtlMs + 100);

            // Assert — reader disposed exactly once (no double-dispose)
            Assert.Equal(1, mockReader.DisposeCount);
        }

        [Fact]
        public async Task ConcurrentRelease_WhenRefCountIsOne_ExactlyOneDisposal()
        {
            // Arrange
            int threadCount = 8;
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            using MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            SemaphoreSlim barrier = new SemaphoreSlim(0, threadCount);

            // Acquire N times so ref count = N
            for (int i = 0; i < threadCount; i++)
                manager.AcquireReader("set1", 42);

            // Release N-1 times so ref count = 1
            for (int i = 0; i < threadCount - 1; i++)
                manager.ReleaseReader("set1", 42);

            // Act — all threads try to release the last reference concurrently
            Task[] tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                barrier.Wait();
                manager.ReleaseReader("set1", 42);
            })).ToArray();

            barrier.Release(threadCount);
            await Task.WhenAll(tasks);

            // Wait for TTL to expire
            Thread.Sleep(_TestTtlMs + 100);

            // Assert — exactly one disposal occurred (no race causing double-dispose)
            Assert.Equal(1, mockReader.DisposeCount);
        }

        #endregion

        #region LRU eviction tests

        [Fact]
        public void ExceedingCapacity_EvictsOldestZeroRefEntry()
        {
            // Arrange — capacity of 3
            Dictionary<long, MockImageReader> readers = new Dictionary<long, MockImageReader>();
            MockDataStore dataStore = new MockDataStore(key =>
            {
                MockImageReader reader = new MockImageReader();
                readers[key.ImageId] = reader;
                return reader;
            });
            using MountResourceManager manager = new MountResourceManager(dataStore, readerCapacity: 3, ttlMs: _TestTtlMs);

            // Acquire 3 readers (fills capacity)
            manager.AcquireReader("set1", 1);
            manager.AcquireReader("set1", 2);
            manager.AcquireReader("set1", 3);

            // Release all to get ref count to 0 (eligible for eviction)
            manager.ReleaseReader("set1", 1);
            manager.ReleaseReader("set1", 2);
            manager.ReleaseReader("set1", 3);

            // Act — acquire a 4th reader, exceeding capacity
            manager.AcquireReader("set1", 4);

            // Assert — oldest zero-ref entry (imageId=1) should be evicted and disposed
            Assert.True(readers[1].IsDisposed, "Oldest zero-ref entry (imageId=1) should be evicted");
            Assert.False(readers[2].IsDisposed, "Second entry should not be evicted yet");
            Assert.False(readers[3].IsDisposed, "Third entry should not be evicted yet");
            Assert.False(readers[4].IsDisposed, "Newly acquired entry should not be evicted");
        }

        [Fact]
        public void ActiveEntries_NeverEvicted_EvenWhenOverCapacity()
        {
            // Arrange — capacity of 3
            Dictionary<long, MockImageReader> readers = new Dictionary<long, MockImageReader>();
            MockDataStore dataStore = new MockDataStore(key =>
            {
                MockImageReader reader = new MockImageReader();
                readers[key.ImageId] = reader;
                return reader;
            });
            using MountResourceManager manager = new MountResourceManager(dataStore, readerCapacity: 3, ttlMs: _TestTtlMs);

            // Acquire 3 readers and keep them active (ref count > 0)
            manager.AcquireReader("set1", 1);
            manager.AcquireReader("set1", 2);
            manager.AcquireReader("set1", 3);

            // Act — acquire a 4th reader, exceeding capacity
            // All existing entries have ref > 0, so none can be evicted
            manager.AcquireReader("set1", 4);

            // Assert — no entries evicted because all have ref count > 0
            Assert.False(readers[1].IsDisposed, "Active entry (imageId=1) should never be evicted");
            Assert.False(readers[2].IsDisposed, "Active entry (imageId=2) should never be evicted");
            Assert.False(readers[3].IsDisposed, "Active entry (imageId=3) should never be evicted");
            Assert.False(readers[4].IsDisposed, "Active entry (imageId=4) should never be evicted");
        }

        [Fact]
        public void Access_PromotesEntry_ToMruPosition()
        {
            // Arrange — capacity of 3
            Dictionary<long, MockImageReader> readers = new Dictionary<long, MockImageReader>();
            MockDataStore dataStore = new MockDataStore(key =>
            {
                MockImageReader reader = new MockImageReader();
                readers[key.ImageId] = reader;
                return reader;
            });
            using MountResourceManager manager = new MountResourceManager(dataStore, readerCapacity: 3, ttlMs: _TestTtlMs);

            // Acquire A, B, C in order (A is oldest/LRU)
            manager.AcquireReader("set1", 1); // A
            manager.AcquireReader("set1", 2); // B
            manager.AcquireReader("set1", 3); // C

            // Release all to make them eligible for eviction
            manager.ReleaseReader("set1", 1);
            manager.ReleaseReader("set1", 2);
            manager.ReleaseReader("set1", 3);

            // Re-acquire A — this promotes A to MRU position
            manager.AcquireReader("set1", 1);
            // Release A again so it's eligible for eviction but at MRU position
            manager.ReleaseReader("set1", 1);

            // Act — acquire D, exceeding capacity
            // LRU order should now be: B (oldest), C, A (most recent)
            // So B should be evicted, not A
            manager.AcquireReader("set1", 4);

            // Assert — B (imageId=2) evicted as the oldest, A was promoted
            Assert.True(readers[2].IsDisposed, "B (imageId=2) should be evicted as oldest LRU entry");
            Assert.False(readers[1].IsDisposed, "A (imageId=1) should NOT be evicted — it was promoted to MRU");
            Assert.False(readers[3].IsDisposed, "C (imageId=3) should not be evicted yet");
            Assert.False(readers[4].IsDisposed, "D (imageId=4) should not be evicted");
        }

        [Fact]
        public void EvictedReader_IsProperlyDisposed()
        {
            // Arrange — capacity of 3
            Dictionary<long, MockImageReader> readers = new Dictionary<long, MockImageReader>();
            MockDataStore dataStore = new MockDataStore(key =>
            {
                MockImageReader reader = new MockImageReader();
                readers[key.ImageId] = reader;
                return reader;
            });
            using MountResourceManager manager = new MountResourceManager(dataStore, readerCapacity: 3, ttlMs: _TestTtlMs);

            // Acquire 3 readers and release them
            manager.AcquireReader("set1", 1);
            manager.AcquireReader("set1", 2);
            manager.AcquireReader("set1", 3);
            manager.ReleaseReader("set1", 1);
            manager.ReleaseReader("set1", 2);
            manager.ReleaseReader("set1", 3);

            // Act — acquire 4th and 5th to trigger multiple evictions
            manager.AcquireReader("set1", 4);
            manager.AcquireReader("set1", 5);

            // Assert — evicted readers are properly disposed (Dispose called exactly once)
            Assert.True(readers[1].IsDisposed, "Evicted reader (imageId=1) should be disposed");
            Assert.Equal(1, readers[1].DisposeCount);
            Assert.True(readers[2].IsDisposed, "Evicted reader (imageId=2) should be disposed");
            Assert.Equal(1, readers[2].DisposeCount);
            // Remaining readers should not be disposed
            Assert.False(readers[3].IsDisposed);
            Assert.False(readers[4].IsDisposed);
            Assert.False(readers[5].IsDisposed);
        }

        #endregion

        #region Shutdown and disposal tests

        [Fact]
        public void Shutdown_DisposesAllReaders_RegardlessOfRefCount()
        {
            // Arrange
            MockImageReader reader1 = new MockImageReader();
            MockImageReader reader2 = new MockImageReader();
            int callCount = 0;
            MockDataStore dataStore = new MockDataStore(key =>
            {
                int c = Interlocked.Increment(ref callCount);
                return c == 1 ? reader1 : reader2;
            });
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire readers with different ref counts
            manager.AcquireReader("set1", 1); // ref count = 1
            manager.AcquireReader("set1", 1); // ref count = 2
            manager.AcquireReader("set1", 2); // ref count = 1

            // Act
            manager.Shutdown();

            // Assert — both readers disposed regardless of ref count
            Assert.True(reader1.IsDisposed);
            Assert.True(reader2.IsDisposed);
        }

        [Fact]
        public void Shutdown_DisposesAllBufferCaches()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire buffer caches
            ImageBufferCache cache1 = manager.AcquireBufferCache(100, 4096, 16);
            ImageBufferCache cache2 = manager.AcquireBufferCache(200, 4096, 16);

            // Verify caches have entries by adding something
            cache1.GetOrCreate(1, () => new BufferCacheEntry(4096, null!));
            cache2.GetOrCreate(2, () => new BufferCacheEntry(4096, null!));

            Assert.True(cache1.Count > 0);
            Assert.True(cache2.Count > 0);

            // Act
            manager.Shutdown();

            // Assert — buffer caches are disposed (cleared)
            Assert.Equal(0, cache1.Count);
            Assert.Equal(0, cache2.Count);
        }

        [Fact]
        public void AcquireReader_AfterShutdown_ThrowsObjectDisposedException()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            manager.Shutdown();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => manager.AcquireReader("set1", 42));
        }

        [Fact]
        public void AcquireBufferCache_AfterShutdown_ThrowsObjectDisposedException()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);
            manager.Shutdown();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => manager.AcquireBufferCache(100, 4096, 16));
        }

        [Fact]
        public void Shutdown_WhilePopulationInProgress_WaitingThreadsUnblock()
        {
            // Arrange
            MockDataStore dataStore = new MockDataStore();
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            // Acquire a buffer cache
            ImageBufferCache cache = manager.AcquireBufferCache(100, 4096, 16);

            // Create an entry that will never be signaled as ready (simulates population in progress)
            // The factory creates the entry but never calls SetReady()
            BufferCacheEntry populatingEntry = new BufferCacheEntry(4096, null!);
            cache.GetOrCreate(999, () => populatingEntry);

            // Start a thread that will block waiting for the same key
            ManualResetEventSlim waitingThreadStarted = new ManualResetEventSlim(false);
            ManualResetEventSlim waitingThreadCompleted = new ManualResetEventSlim(false);
            Exception threadException = null;

            Thread waitingThread = new Thread(() =>
            {
                try
                {
                    waitingThreadStarted.Set();
                    // This will find the existing not-ready entry and call WaitReady() which blocks
                    cache.GetOrCreate(999, () => new BufferCacheEntry(4096, null!));
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    waitingThreadCompleted.Set();
                }
            });
            waitingThread.Start();

            // Wait for the thread to start and begin waiting
            waitingThreadStarted.Wait(TimeSpan.FromSeconds(5));
            Thread.Sleep(50); // Give it time to enter WaitReady()

            // Act — Shutdown should dispose the cache, which signals all entries via SetFailed()
            manager.Shutdown();

            // Assert — the waiting thread should unblock within a reasonable timeout (not hang)
            bool completed = waitingThreadCompleted.Wait(TimeSpan.FromSeconds(5));
            Assert.True(completed, "Waiting thread should have unblocked after Shutdown, but it hung.");
        }

        [Fact]
        public void DoubleShutdown_IsSafe_NoOp()
        {
            // Arrange
            MockImageReader mockReader = new MockImageReader();
            MockDataStore dataStore = new MockDataStore(_ => mockReader);
            MountResourceManager manager = new MountResourceManager(dataStore, _ReaderCapacity, _TestTtlMs);

            manager.AcquireReader("set1", 42);

            // Act — call Shutdown twice
            manager.Shutdown();
            Exception exception = Record.Exception(() => manager.Shutdown());

            // Assert — no exception on second call, reader disposed exactly once
            Assert.Null(exception);
            Assert.Equal(1, mockReader.DisposeCount);
        }

        #endregion

        #region ImageBufferCache concurrency tests

        /// <summary>
        /// Validates: Requirements 3.2, 3.3
        /// Two threads request same buffer key simultaneously → factory called once, both get same entry.
        /// </summary>
        [Fact]
        public void TwoThreads_SameKey_FactoryCalledOnce_BothGetSameEntry()
        {
            // Arrange
            ImageBufferCache cache = new ImageBufferCache(imageId: 1, bufferSize: 4096, maxSize: 16);
            int factoryCallCount = 0;
            ManualResetEventSlim barrier = new ManualResetEventSlim(false);

            BufferCacheEntry factory()
            {
                Interlocked.Increment(ref factoryCallCount);
                BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                entry.SetReady();
                return entry;
            }

            BufferCacheEntry result1 = null;
            BufferCacheEntry result2 = null;

            // Act — two threads request the same key simultaneously
            Thread thread1 = new Thread(() =>
            {
                barrier.Wait();
                result1 = cache.GetOrCreate(42, factory);
            });
            Thread thread2 = new Thread(() =>
            {
                barrier.Wait();
                result2 = cache.GetOrCreate(42, factory);
            });

            thread1.Start();
            thread2.Start();

            // Release both threads at the same time
            barrier.Set();

            thread1.Join(TimeSpan.FromSeconds(5));
            thread2.Join(TimeSpan.FromSeconds(5));

            // Assert — factory called exactly once, both threads got the same entry
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.Same(result1, result2);
            Assert.Equal(1, factoryCallCount);
        }

        /// <summary>
        /// Validates: Requirements 3.4
        /// Factory throws → entry removed, subsequent request retries successfully.
        /// </summary>
        [Fact]
        public void FactoryThrows_EntryRemoved_SubsequentRequestRetries()
        {
            // Arrange
            ImageBufferCache cache = new ImageBufferCache(imageId: 1, bufferSize: 4096, maxSize: 16);
            int factoryCallCount = 0;

            // Act — first call throws (exception propagates to caller)
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            {
                cache.GetOrCreate(42, () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    throw new InvalidOperationException("Simulated factory failure");
                });
            });
            Assert.Equal("Simulated factory failure", ex.Message);
            Assert.Equal(1, factoryCallCount);

            // Second call succeeds — the failed entry was never inserted, so factory is called again
            BufferCacheEntry result = cache.GetOrCreate(42, () =>
            {
                Interlocked.Increment(ref factoryCallCount);
                BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                entry.SetReady();
                return entry;
            });

            // Assert — factory was called twice total (first threw, second succeeded)
            Assert.NotNull(result);
            Assert.True(result.IsReady);
            Assert.Equal(2, factoryCallCount);
        }

        /// <summary>
        /// Validates: Requirements 3.2, 3.3
        /// WaitReady timeout → stale entry removed, retry succeeds.
        /// Uses a short-lived entry that never signals ready to simulate timeout behavior.
        /// </summary>
        [Fact]
        [Trait("Category", "Slow")]
        public void WaitReadyTimeout_StaleEntryRemoved_RetrySucceeds()
        {
            // Arrange
            ImageBufferCache cache = new ImageBufferCache(imageId: 1, bufferSize: 4096, maxSize: 16);
            int factoryCallCount = 0;
            ManualResetEventSlim firstEntryCreated = new ManualResetEventSlim(false);

            // Thread 1: creates an entry but never calls SetReady() (simulates stuck population)
            BufferCacheEntry staleEntry = null;
            Thread thread1 = new Thread(() =>
            {
                staleEntry = cache.GetOrCreate(42, () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                    // Deliberately NOT calling entry.SetReady() — simulates stuck population
                    firstEntryCreated.Set();
                    return entry;
                });
            });
            thread1.Start();
            thread1.Join(TimeSpan.FromSeconds(5));
            firstEntryCreated.Wait(TimeSpan.FromSeconds(5));

            // Thread 2: requests the same key — will find the not-ready entry, wait, timeout,
            // remove it, and retry with a new factory call that succeeds
            BufferCacheEntry retryResult = null;
            Thread thread2 = new Thread(() =>
            {
                retryResult = cache.GetOrCreate(42, () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                    entry.SetReady();
                    return entry;
                });
            });
            thread2.Start();

            // This will take ~30s due to the WaitReady timeout
            bool completed = thread2.Join(TimeSpan.FromSeconds(35));

            // Assert
            Assert.True(completed, "Thread 2 should have completed after timeout + retry");
            Assert.NotNull(retryResult);
            Assert.True(retryResult!.IsReady);
            Assert.NotSame(staleEntry, retryResult); // Got a new entry, not the stale one
            Assert.Equal(2, factoryCallCount); // Factory called twice: once for stale, once for retry
        }

        /// <summary>
        /// Validates: Requirements 3.4
        /// SetFailed unblocks waiting thread which then retries.
        /// </summary>
        [Fact]
        public void SetFailed_UnblocksWaitingThread_WhichRetries()
        {
            // Arrange
            ImageBufferCache cache = new ImageBufferCache(imageId: 1, bufferSize: 4096, maxSize: 16);
            int factoryCallCount = 0;
            ManualResetEventSlim firstEntryCreated = new ManualResetEventSlim(false);
            ManualResetEventSlim waitingThreadStarted = new ManualResetEventSlim(false);

            // Thread 1: creates an entry but doesn't call SetReady yet
            BufferCacheEntry failedEntry = null;
            Thread thread1 = new Thread(() =>
            {
                failedEntry = cache.GetOrCreate(42, () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                    // Don't call SetReady — we'll call SetFailed later
                    firstEntryCreated.Set();
                    return entry;
                });
            });
            thread1.Start();
            thread1.Join(TimeSpan.FromSeconds(5));
            firstEntryCreated.Wait(TimeSpan.FromSeconds(5));

            // Thread 2: requests the same key — will find the not-ready entry and block in WaitReady
            BufferCacheEntry retryResult = null;
            Thread thread2 = new Thread(() =>
            {
                waitingThreadStarted.Set();
                retryResult = cache.GetOrCreate(42, () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    BufferCacheEntry entry = new BufferCacheEntry(4096, null!);
                    entry.SetReady();
                    return entry;
                });
            });
            thread2.Start();

            // Wait for thread 2 to start and enter WaitReady
            waitingThreadStarted.Wait(TimeSpan.FromSeconds(5));
            Thread.Sleep(100); // Give thread 2 time to enter WaitReady()

            // Act — signal failure on the first entry, which should unblock thread 2
            failedEntry!.SetFailed();

            // Assert — thread 2 should unblock quickly, retry, and get a new entry
            bool completed = thread2.Join(TimeSpan.FromSeconds(5));
            Assert.True(completed, "Waiting thread should have unblocked after SetFailed and retried");
            Assert.NotNull(retryResult);
            Assert.True(retryResult!.IsReady);
            Assert.False(retryResult.IsFailed);
            Assert.NotSame(failedEntry, retryResult); // Got a new entry, not the failed one
            Assert.Equal(2, factoryCallCount); // Factory called twice: once for failed, once for retry
        }

        #endregion
    }
}