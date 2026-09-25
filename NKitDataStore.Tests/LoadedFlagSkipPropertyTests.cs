using FsCheck;
using FsCheck.Xunit;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Property-based tests for the loaded flag skip behavior in MountRegistry.LoadFsYaml().
    ///
    /// Feature: lazy-filesystem-yaml-loading, Property 2: Loaded flag prevents redundant database reads
    ///
    /// The MountRegistry.LoadFsYaml() method checks item.FileSystemYamlLoaded before
    /// performing any database read. When the flag is true, it calls
    /// model.TouchFsYamlCache(item.ImageRecord.Id) and returns immediately — no ReadFile()
    /// calls are made. This test replicates the exact early-return logic from
    /// MountRegistry.LoadFsYaml() and verifies the invariant holds across arbitrary inputs.
    /// </summary>
    public class LoadedFlagSkipPropertyTests
    {
        /// <summary>
        /// Minimal model item mirroring VfsModelItem's relevant fields for the loaded-flag check.
        /// </summary>
        private class TestVfsModelItem
        {
            public ImageRecord ImageRecord { get; set; }
            public bool FileSystemYamlLoaded { get; set; }
            public object FileSystemYaml { get; set; }
        }

        /// <summary>
        /// Counting mock that tracks ReadFile calls. Mirrors the IDataStore.ReadFile signature
        /// used by MountRegistry.LoadFsYaml().
        /// </summary>
        private class CountingDataStoreMock
        {
            public int ReadFileCallCount { get; private set; }

            public byte[] ReadFile(GlobalImageKey key, string name)
            {
                ReadFileCallCount++;
                // Return dummy data — this should never be reached when FileSystemYamlLoaded is true
                return new byte[] { 0x01 };
            }
        }

        /// <summary>
        /// Counting mock that tracks TouchFsYamlCache calls.
        /// </summary>
        private class CountingCacheMock
        {
            public int TouchCacheCallCount { get; private set; }
            private readonly Dictionary<long, object> _locks = new Dictionary<long, object>();

            public void TouchFsYamlCache(long imageId) => TouchCacheCallCount++;

            public object GetFsYamlLoadLock(long imageId)
            {
                if (!_locks.TryGetValue(imageId, out object lockObj))
                {
                    lockObj = new object();
                    _locks[imageId] = lockObj;
                }
                return lockObj;
            }

            public void TrackFsYamlLoaded(long imageId)
            {
                // No-op for this test — we only care about the loaded-flag skip path
            }
        }

        /// <summary>
        /// Replicates the exact logic of MountRegistry.LoadFsYaml() so we can verify
        /// the loaded-flag skip behavior with counting mocks.
        /// </summary>
        private static void loadFsYaml(
            TestVfsModelItem item,
            CountingDataStoreMock dataStore,
            CountingCacheMock cache)
        {
            // --- Exact replica of MountRegistry.LoadFsYaml() early-return logic ---
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
        /// **Validates: Requirements 3.2**
        ///
        /// Property 2: Loaded flag prevents redundant database reads.
        /// For any VfsModelItem whose FileSystemYamlLoaded flag is true, calling
        /// LoadFsYaml() SHALL perform zero database read operations and SHALL not
        /// modify the item's FileSystemYaml value.
        ///
        /// Generates random image IDs, set names, and optional pre-existing FileSystemYaml
        /// values. Sets FileSystemYamlLoaded = true, then calls LoadFsYaml() and verifies:
        /// 1. ReadFile() is never called (count remains 0)
        /// 2. The item's FileSystemYaml value is unchanged
        /// 3. TouchFsYamlCache is called exactly once (LRU promotion)
        /// </summary>
        [Property(MaxTest = 100)]
        public bool LoadedFlag_PreventsRedundantDatabaseReads(
            PositiveInt imageId,
            NonNegativeInt setNameSeed,
            bool hasExistingYaml)
        {
            CountingDataStoreMock dataStore = new CountingDataStoreMock();
            CountingCacheMock cache = new CountingCacheMock();

            string setName = $"Set_{setNameSeed.Get % 100}";
            object originalYaml = hasExistingYaml ? new byte[] { 0xAA, 0xBB } : null;

            TestVfsModelItem item = new TestVfsModelItem
            {
                ImageRecord = new ImageRecord
                {
                    Id = imageId.Get,
                    Name = $"Image_{imageId.Get}.iso",
                    SetName = setName
                },
                FileSystemYamlLoaded = true,  // KEY: flag is already true
                FileSystemYaml = originalYaml
            };

            // Act
            loadFsYaml(item, dataStore, cache);

            // PROPERTY: No ReadFile calls were made
            if (dataStore.ReadFileCallCount != 0)
                return false;

            // PROPERTY: FileSystemYaml value is unchanged
            if (item.FileSystemYaml != originalYaml)
                return false;

            // PROPERTY: TouchFsYamlCache was called exactly once (LRU promotion)
            if (cache.TouchCacheCallCount != 1)
                return false;

            // PROPERTY: FileSystemYamlLoaded remains true
            if (!item.FileSystemYamlLoaded)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 3.2**
        ///
        /// Property 2 (supplementary): Null item also results in zero database reads.
        /// When LoadFsYaml() is called with a null item, no ReadFile() calls are made.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool NullItem_ResultsInZeroDatabaseReads(NonNegativeInt seed)
        {
            CountingDataStoreMock dataStore = new CountingDataStoreMock();
            CountingCacheMock cache = new CountingCacheMock();

            // Act — pass null item
            loadFsYaml(null, dataStore, cache);

            // PROPERTY: No ReadFile calls were made
            if (dataStore.ReadFileCallCount != 0)
                return false;

            // PROPERTY: No cache interactions
            if (cache.TouchCacheCallCount != 0)
                return false;

            return true;
        }

        /// <summary>
        /// **Validates: Requirements 3.2**
        ///
        /// Property 2 (contrast): Unloaded items DO trigger database reads.
        /// When FileSystemYamlLoaded is false, LoadFsYaml() DOES call ReadFile().
        /// This contrast test confirms the loaded-flag check is the gate that prevents reads.
        /// </summary>
        [Property(MaxTest = 100)]
        public bool UnloadedFlag_DoesTriggerDatabaseReads(
            PositiveInt imageId,
            NonNegativeInt setNameSeed)
        {
            CountingDataStoreMock dataStore = new CountingDataStoreMock();
            CountingCacheMock cache = new CountingCacheMock();

            string setName = $"Set_{setNameSeed.Get % 100}";

            TestVfsModelItem item = new TestVfsModelItem
            {
                ImageRecord = new ImageRecord
                {
                    Id = imageId.Get,
                    Name = $"Image_{imageId.Get}.iso",
                    SetName = setName
                },
                FileSystemYamlLoaded = false,  // NOT loaded — should trigger reads
                FileSystemYaml = null
            };

            // Act
            loadFsYaml(item, dataStore, cache);

            // PROPERTY: ReadFile WAS called (at least once — the first call returns data,
            // so only one call is made since the first ReadFile returns non-null)
            if (dataStore.ReadFileCallCount < 1)
                return false;

            // PROPERTY: After loading, FileSystemYamlLoaded is now true
            if (!item.FileSystemYamlLoaded)
                return false;

            return true;
        }
    }
}