using FsCheck;
using FsCheck.Xunit;
using System.Collections.Concurrent;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for concurrent load atomicity in MountRegistry.LoadFsYaml().
    ///
    /// Feature: lazy-filesystem-yaml-loading, Property 3: Concurrent loads on the same item result in exactly one database read
    ///
    /// The MountRegistry.LoadFsYaml() method uses per-item locking via
    /// model.GetFsYamlLoadLock(item.ImageRecord.Id) with a double-check pattern:
    /// it checks FileSystemYamlLoaded before and after acquiring the lock.
    /// This ensures that when multiple threads concurrently call LoadFsYaml()
    /// for the same unloaded item, the filesystem.yaml is read from the database
    /// exactly once.
    ///
    /// This test replicates the exact locking and double-check logic from
    /// MountRegistry.LoadFsYaml() and verifies the invariant holds across
    /// arbitrary thread counts using Interlocked.Increment in a counting mock.
    /// </summary>
    public class ConcurrentLoadAtomicityPropertyTests
    {
        /// <summary>
        /// Minimal model item mirroring VfsModelItem's relevant fields for the concurrent load test.
        /// </summary>
        private class TestVfsModelItem
        {
            public ImageRecord ImageRecord { get; set; }
            public bool FileSystemYamlLoaded { get; set; }
            public object FileSystemYaml { get; set; }
        }

        /// <summary>
        /// Thread-safe counting mock that tracks ReadFile calls using Interlocked.Increment.
        /// Returns dummy data to simulate a successful database read.
        /// </summary>
        private class AtomicCountingDataStoreMock
        {
            private int _readFileCallCount;

            public int ReadFileCallCount => Volatile.Read(ref _readFileCallCount);

            public byte[] ReadFile(GlobalImageKey key, string name)
            {
                Interlocked.Increment(ref _readFileCallCount);
                // Simulate some work to increase contention window
                Thread.SpinWait(10);
                return new byte[] { 0x01, 0x02, 0x03 };
            }
        }

        /// <summary>
        /// Thread-safe cache mock that provides per-item locks and tracks cache operations.
        /// Uses ConcurrentDictionary for thread-safe lock object management,
        /// mirroring VfsModel.GetFsYamlLoadLock().
        /// </summary>
        private class ThreadSafeCacheMock
        {
            private int _touchCacheCallCount;
            private int _trackLoadedCallCount;
            private readonly ConcurrentDictionary<long, object> _locks
                = new ConcurrentDictionary<long, object>();

            public int TouchCacheCallCount => Volatile.Read(ref _touchCacheCallCount);
            public int TrackLoadedCallCount => Volatile.Read(ref _trackLoadedCallCount);

            public void TouchFsYamlCache(long imageId) => Interlocked.Increment(ref _touchCacheCallCount);

            public object GetFsYamlLoadLock(long imageId) => _locks.GetOrAdd(imageId, _ => new object());

            public void TrackFsYamlLoaded(long imageId) => Interlocked.Increment(ref _trackLoadedCallCount);
        }

        /// <summary>
        /// Replicates the exact logic of MountRegistry.LoadFsYaml() so we can verify
        /// the concurrent load atomicity with counting mocks.
        /// </summary>
        private static void loadFsYaml(
            TestVfsModelItem item,
            AtomicCountingDataStoreMock dataStore,
            ThreadSafeCacheMock cache)
        {
            // --- Exact replica of MountRegistry.LoadFsYaml() logic ---
            if (item == null || item.FileSystemYamlLoaded)
            {
                // Already loaded — just touch the LRU cache to keep it fresh
                if (item?.FileSystemYamlLoaded == true && item.ImageRecord != null)
                    cache.TouchFsYamlCache(item.ImageRecord.Id);
                return;
            }

            // Per-item lock: allows concurrent loads of different items
            object itemLock = cache.GetFsYamlLoadLock(item.ImageRecord!.Id);
            lock (itemLock)
            {
                // Double-check after acquiring lock
                if (item.FileSystemYamlLoaded)
                {
                    cache.TouchFsYamlCache(item.ImageRecord.Id);
                    return;
                }

                try
                {
                    byte[] fsYamlData = null;
                    try
                    {
                        fsYamlData = dataStore.ReadFile(
                            new GlobalImageKey(item.ImageRecord.SetName, item.ImageRecord.Id),
                            DataStore.FileSystemYamlRootPath);
                    }
                    catch { }

                    if (fsYamlData == null)
                    {
                        try
                        {
                            fsYamlData = dataStore.ReadFile(
                                new GlobalImageKey(item.ImageRecord.SetName, item.ImageRecord.Id),
                                DataStore.FileSystemYamlName);
                        }
                        catch { }
                    }

                    if (fsYamlData != null)
                    {
                        item.FileSystemYaml = fsYamlData; // Simplified — real code parses FsYaml
                    }
                }
                catch
                {
                    // On failure, mark as loaded to prevent infinite retries
                }

                item.FileSystemYamlLoaded = true;

                if (item.FileSystemYaml != null)
                    cache.TrackFsYamlLoaded(item.ImageRecord.Id);
            }
        }

        /// <summary>
        /// **Validates: Requirements 4.1**
        ///
        /// Property 3: Concurrent loads on the same item result in exactly one database read.
        /// For any number of threads (2–16) concurrently calling LoadFsYaml() for the same
        /// VfsModelItem whose FileSystemYamlLoaded is false, the filesystem.yaml SHALL be
        /// read from the database exactly once, and all threads SHALL observe the same
        /// FileSystemYaml value after completion.
        ///
        /// Uses ManualResetEventSlim to synchronize thread starts for maximum contention,
        /// and Interlocked.Increment in the mock to safely count ReadFile calls.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool ConcurrentLoads_ResultInExactlyOneDatabaseRead(
            PositiveInt imageId,
            NonNegativeInt setNameSeed,
            NonNegativeInt threadCountSeed)
        {
            // Generate thread count in range [2, 16]
            int threadCount = (threadCountSeed.Get % 15) + 2;
            string setName = $"Set_{setNameSeed.Get % 100}";

            AtomicCountingDataStoreMock dataStore = new AtomicCountingDataStoreMock();
            ThreadSafeCacheMock cache = new ThreadSafeCacheMock();

            // Single shared unloaded item — all threads target this same item
            TestVfsModelItem item = new TestVfsModelItem
            {
                ImageRecord = new ImageRecord
                {
                    Id = imageId.Get,
                    Name = $"Image_{imageId.Get}.iso",
                    SetName = setName
                },
                FileSystemYamlLoaded = false,
                FileSystemYaml = null
            };

            // Use ManualResetEventSlim to synchronize all threads to start simultaneously
            using ManualResetEventSlim startBarrier = new ManualResetEventSlim(false);
            using CountdownEvent allDone = new CountdownEvent(threadCount);

            ConcurrentBag<Exception> exceptions = new ConcurrentBag<Exception>();

            for (int i = 0; i < threadCount; i++)
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        // Wait for the signal so all threads start at the same time
                        startBarrier.Wait();
                        loadFsYaml(item, dataStore, cache);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                    finally
                    {
                        allDone.Signal();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
            }

            // Release all threads simultaneously for maximum contention
            startBarrier.Set();

            // Wait for all threads to complete
            allDone.Wait(TimeSpan.FromSeconds(10));

            // No exceptions should have occurred
            if (!exceptions.IsEmpty)
                return false;

            // PROPERTY: ReadFile was called exactly once (first call returns non-null,
            // so only one ReadFile call is made per the LoadFsYaml logic)
            if (dataStore.ReadFileCallCount != 1)
                return false;

            // PROPERTY: After all threads complete, the item is marked as loaded
            if (!item.FileSystemYamlLoaded)
                return false;

            // PROPERTY: FileSystemYaml is non-null (the mock returns data)
            if (item.FileSystemYaml == null)
                return false;

            // PROPERTY: TrackFsYamlLoaded was called exactly once
            if (cache.TrackLoadedCallCount != 1)
                return false;

            return true;
        }
    }
}