using System.Diagnostics;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for MountRegistry per-type NkFs loading logic.
/// Replicates the LoadNkfs logic with mocks to verify:
/// - Multiple per-type files load into dictionary
/// - Fallback to unified file when no per-type files exist
/// - Corrupt per-type file is skipped with warning
/// - Mixed DataStore with both old and new format images
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 6.1, 6.3**
/// </summary>
public class MountRegistryPerTypeLoadingTests
{
    #region Test Infrastructure

    /// <summary>
    /// Minimal model item mirroring VfsModelItem's relevant fields for LoadNkfs.
    /// </summary>
    private class TestVfsModelItem
    {
        public ImageRecord ImageRecord { get; set; }
        public bool FileSystemNkfsLoaded { get; set; }
        public NkFs FileSystemNkfs { get; set; }
        public Dictionary<string, NkFs> FileSystemNkfsPerType { get; set; }
        public bool IsMultiFilesystem => FileSystemNkfsPerType != null
            && FileSystemNkfsPerType.Count > 1;
        public int DataStoreIndex { get; set; }
    }

    /// <summary>
    /// Mock DataStore that returns configured file records and file data.
    /// </summary>
    private class MockDataStore
    {
        private readonly List<FileRecord> _files;
        private readonly Dictionary<string, byte[]> _fileData;

        public MockDataStore(
            List<FileRecord> files,
            Dictionary<string, byte[]> fileData)
        {
            _files = files;
            _fileData = fileData;
        }

        public List<FileRecord> ListFiles() => _files;

        public byte[] ReadFile(GlobalImageKey key, string name) => _fileData.TryGetValue(name, out byte[] data) ? data : null;
    }

    /// <summary>
    /// Tracking mock for cache/model operations.
    /// </summary>
    private class MockModel
    {
        public int TouchNkfsCacheCount { get; private set; }
        public int TrackNkfsLoadedCount { get; private set; }
        private readonly Dictionary<long, object> _locks = new();

        public void TouchNkfsCache(long imageId) => TouchNkfsCacheCount++;
        public void TrackNkfsLoaded(long imageId) => TrackNkfsLoadedCount++;

        public object GetNkfsLoadLock(long imageId)
        {
            if (!_locks.TryGetValue(imageId, out object lockObj))
            {
                lockObj = new object();
                _locks[imageId] = lockObj;
            }
            return lockObj;
        }
    }

    /// <summary>
    /// Replicates the exact logic of MountRegistry.LoadNkfs() so we can verify
    /// the per-type loading behavior with mocks.
    /// </summary>
    private static void LoadNkfs(TestVfsModelItem item, MockDataStore dataStore, MockModel model)
    {
        if (item == null || item.FileSystemNkfsLoaded)
        {
            if (item?.FileSystemNkfsLoaded == true && item.ImageRecord != null)
                model.TouchNkfsCache(item.ImageRecord.Id);
            return;
        }

        object itemLock = model.GetNkfsLoadLock(item.ImageRecord.Id);
        lock (itemLock)
        {
            if (item.FileSystemNkfsLoaded)
            {
                model.TouchNkfsCache(item.ImageRecord.Id);
                return;
            }

            try
            {
                NkFs nkfs = null;
                GlobalImageKey key = new GlobalImageKey(
                    item.ImageRecord.SetName, item.ImageRecord.Id);

                // 1. Try per-type nkfs files
                List<(string typeName, byte[] data)> perTypeFiles = ProbePerTypeNkfs(dataStore, key);

                if (perTypeFiles.Count > 0)
                {
                    Dictionary<string, NkFs> dict = new Dictionary<string, NkFs>(
                        StringComparer.OrdinalIgnoreCase);
                    foreach ((string typeName, byte[] data) in perTypeFiles)
                    {
                        try
                        {
                            dict[typeName] = NkFs.FromBytes(data);
                        }
                        catch
                        {
                            Trace.WriteLine(
                                $"[LazyLoad] Corrupt filesystem.{typeName}.nkfs "
                                + $"for '{item.ImageRecord.Name}', skipping");
                        }
                    }

                    if (dict.Count > 0)
                    {
                        item.FileSystemNkfsPerType = dict;
                        item.FileSystemNkfs = dict.Count == 1
                            ? dict.Values.First() : null;
                    }
                }

                // 2. Fall back to unified filesystem.nkfs
                if (item.FileSystemNkfsPerType == null)
                {
                    byte[] nkfsData = null;
                    try
                    {
                        nkfsData = dataStore.ReadFile(key,
                            DataStore.FileSystemNkfsRootPath);
                    }
                    catch { }

                    if (nkfsData != null)
                    {
                        try
                        {
                            nkfs = NkFs.FromBytes(nkfsData);
                        }
                        catch
                        {
                            nkfs = null;
                        }
                    }

                    item.FileSystemNkfs = nkfs;
                }
            }
            catch
            {
                // On failure, mark as loaded to prevent infinite retries
            }

            item.FileSystemNkfsLoaded = true;

            if (item.FileSystemNkfs != null)
                model.TrackNkfsLoaded(item.ImageRecord.Id);
        }
    }

    /// <summary>
    /// Replicates MountRegistry.ProbePerTypeNkfs logic.
    /// </summary>
    private static List<(string typeName, byte[] data)> ProbePerTypeNkfs(
        MockDataStore dataStore, GlobalImageKey key)
    {
        List<(string, byte[])> results = new List<(string, byte[])>();
        foreach (FileRecord file in dataStore.ListFiles())
        {
            if (DataStore.TryExtractFsTypeName(file.Name, out string typeName))
            {
                byte[] data = dataStore.ReadFile(key, file.Name);
                if (data != null)
                    results.Add((typeName, data));
            }
        }
        return results;
    }

    /// <summary>
    /// Creates a minimal valid NkFs binary (empty root directory).
    /// </summary>
    private static byte[] CreateValidNkfsBytes()
    {
        // Build a minimal NkFs with just a root directory
        FsYamlNode root = FsYamlNode.CreateDirectory("root");
        root.AddFile("test.bin", 0x1000, 512, 0, 0);
        FsYaml fsYaml = new FsYaml();
        fsYaml.FileSystems.Add(root);
        NkFs nkfs = NkFs.FromFsYaml(fsYaml);
        return nkfs.ToBytes();
    }

    /// <summary>
    /// Creates corrupt (invalid) NkFs binary data.
    /// </summary>
    private static byte[] CreateCorruptNkfsBytes()
    {
        // Invalid magic number — will fail NkFs.FromBytes
        return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04,
                            0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C };
    }

    private static TestVfsModelItem CreateTestItem(long id = 1, string name = "TestImage.iso")
    {
        return new TestVfsModelItem
        {
            ImageRecord = new ImageRecord
            {
                Id = id,
                Name = name,
                SetName = "TestSet"
            },
            FileSystemNkfsLoaded = false,
            FileSystemNkfs = null,
            FileSystemNkfsPerType = null,
            DataStoreIndex = 0
        };
    }

    #endregion

    #region Test 1: Multiple per-type files loaded into dictionary

    [Fact]
    public void LoadNkfs_MultiplePerTypeFiles_LoadsIntoDictionary_IsMultiFilesystem()
    {
        // Arrange: two per-type nkfs files (iso9660 + joliet)
        byte[] iso9660Data = CreateValidNkfsBytes();
        byte[] jolietData = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = jolietData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = jolietData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem();

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.NotNull(item.FileSystemNkfsPerType);
        Assert.Equal(2, item.FileSystemNkfsPerType.Count);
        Assert.True(item.FileSystemNkfsPerType.ContainsKey("iso9660"));
        Assert.True(item.FileSystemNkfsPerType.ContainsKey("joliet"));
        Assert.True(item.IsMultiFilesystem);
        // When multiple types, FileSystemNkfs should be null
        Assert.Null(item.FileSystemNkfs);
    }

    #endregion

    #region Test 2: Fallback to unified file when no per-type files exist

    [Fact]
    public void LoadNkfs_NoPerTypeFiles_FallsBackToUnified_IsNotMultiFilesystem()
    {
        // Arrange: only unified filesystem.nkfs exists
        byte[] unifiedData = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length },
            new FileRecord { Name = "header.bin", Size = 100 }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem();

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.Null(item.FileSystemNkfsPerType);
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
    }

    #endregion

    #region Test 3: Corrupt per-type file is skipped with warning

    [Fact]
    public void LoadNkfs_OneCorruptPerTypeFile_SkippedOtherLoaded()
    {
        // Arrange: iso9660 is valid, joliet is corrupt
        byte[] iso9660Data = CreateValidNkfsBytes();
        byte[] corruptData = CreateCorruptNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = corruptData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = corruptData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem();

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: corrupt joliet skipped, iso9660 loaded
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.NotNull(item.FileSystemNkfsPerType);
        Assert.Equal(1, item.FileSystemNkfsPerType.Count);
        Assert.True(item.FileSystemNkfsPerType.ContainsKey("iso9660"));
        Assert.False(item.FileSystemNkfsPerType.ContainsKey("joliet"));
        // Single per-type → FileSystemNkfs set to that instance
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
    }

    #endregion

    #region Test 4: All per-type files corrupt → falls back to unified

    [Fact]
    public void LoadNkfs_AllPerTypeFilesCorrupt_FallsBackToUnified()
    {
        // Arrange: both per-type files are corrupt, unified exists
        byte[] corruptData = CreateCorruptNkfsBytes();
        byte[] unifiedData = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = corruptData.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = corruptData.Length },
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = corruptData,
            ["filesystem.joliet.nkfs"] = corruptData,
            ["filesystem.nkfs"] = unifiedData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem();

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: all per-type corrupt → dict empty → falls back to unified
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.Null(item.FileSystemNkfsPerType);
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
    }

    #endregion

    #region Test 5: Single per-type file → loaded, FileSystemNkfs set

    [Fact]
    public void LoadNkfs_SinglePerTypeFile_LoadedAndFileSystemNkfsSet()
    {
        // Arrange: only one per-type file (iso9660)
        byte[] iso9660Data = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem();

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: single per-type → dict has 1 entry, FileSystemNkfs set
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.NotNull(item.FileSystemNkfsPerType);
        Assert.Equal(1, item.FileSystemNkfsPerType.Count);
        Assert.True(item.FileSystemNkfsPerType.ContainsKey("iso9660"));
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
        // FileSystemNkfs should be the same instance as the dict value
        Assert.Same(
            item.FileSystemNkfsPerType["iso9660"],
            item.FileSystemNkfs);
    }

    #endregion

    #region Test: Mixed DataStore with both old and new format images

    [Fact]
    public void LoadNkfs_MixedDataStore_OldFormatImage_LoadsUnified()
    {
        // Arrange: old-format image with only unified filesystem.nkfs
        byte[] unifiedData = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length },
            new FileRecord { Name = "header.bin", Size = 64 }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem(id: 1, name: "OldImage.iso");

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: old format loads as unified
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.Null(item.FileSystemNkfsPerType);
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
    }

    [Fact]
    public void LoadNkfs_MixedDataStore_NewFormatImage_LoadsPerType()
    {
        // Arrange: new-format image with per-type files
        byte[] iso9660Data = CreateValidNkfsBytes();
        byte[] jolietData = CreateValidNkfsBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = jolietData.Length },
            new FileRecord { Name = "header.bin", Size = 64 }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = jolietData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem(id: 2, name: "NewImage.iso");

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: new format loads per-type
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.NotNull(item.FileSystemNkfsPerType);
        Assert.Equal(2, item.FileSystemNkfsPerType.Count);
        Assert.True(item.IsMultiFilesystem);
        Assert.Null(item.FileSystemNkfs);
    }

    /// <summary>
    /// Simulates a mixed DataStore scenario where two images are loaded
    /// independently — one old format, one new format — verifying both
    /// coexist correctly (Requirement 6.3).
    /// </summary>
    [Fact]
    public void LoadNkfs_MixedDataStore_BothFormatsCoexist()
    {
        // Old-format image
        byte[] unifiedData = CreateValidNkfsBytes();
        List<FileRecord> oldFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> oldFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };
        MockDataStore oldDataStore = new MockDataStore(oldFiles, oldFileData);

        // New-format image
        byte[] iso9660Data = CreateValidNkfsBytes();
        byte[] udfData = CreateValidNkfsBytes();
        List<FileRecord> newFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.udf.nkfs", Size = udfData.Length }
        };
        Dictionary<string, byte[]> newFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.udf.nkfs"] = udfData
        };
        MockDataStore newDataStore = new MockDataStore(newFiles, newFileData);

        MockModel model = new MockModel();
        TestVfsModelItem oldItem = CreateTestItem(id: 1, name: "OldGame.iso");
        TestVfsModelItem newItem = CreateTestItem(id: 2, name: "NewGame.iso");

        // Act
        LoadNkfs(oldItem, oldDataStore, model);
        LoadNkfs(newItem, newDataStore, model);

        // Assert: both formats coexist
        Assert.Null(oldItem.FileSystemNkfsPerType);
        Assert.NotNull(oldItem.FileSystemNkfs);
        Assert.False(oldItem.IsMultiFilesystem);

        Assert.NotNull(newItem.FileSystemNkfsPerType);
        Assert.Equal(2, newItem.FileSystemNkfsPerType.Count);
        Assert.True(newItem.IsMultiFilesystem);
        Assert.Null(newItem.FileSystemNkfs);
    }

    #endregion
}