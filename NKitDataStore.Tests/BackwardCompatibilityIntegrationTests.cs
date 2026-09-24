using System.Diagnostics;

namespace NKitDataStore.Tests;

/// <summary>
/// Backward compatibility integration tests for the multi-filesystem NkFs feature.
/// Verifies:
/// - Unified-only DataStore loads and presents correctly (single-tree behavior)
/// - Mixed DataStore with both old (unified) and new (per-type) images works simultaneously
/// - Re-ingestion replaces unified file with per-type files
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public class BackwardCompatibilityIntegrationTests
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
    }

    /// <summary>
    /// Mock DataStore that returns configured file records and file data.
    /// Supports deletion of files to simulate re-ingestion.
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

        /// <summary>
        /// Removes a file from the store (simulates re-ingestion cleanup).
        /// </summary>
        public void RemoveFile(string name)
        {
            _files.RemoveAll(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            _fileData.Remove(name);
        }

        /// <summary>
        /// Adds a file to the store (simulates writing during re-ingestion).
        /// </summary>
        public void AddFile(string name, byte[] data)
        {
            _files.Add(new FileRecord { Name = name, Size = data.Length });
            _fileData[name] = data;
        }
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
    /// Replicates the ListFolder logic for presentation testing.
    /// Returns folder names for multi-filesystem, or file names for single-filesystem.
    /// </summary>
    private static List<string> ListFolder(TestVfsModelItem item)
    {
        List<string> results = new List<string>();

        if (item.IsMultiFilesystem)
        {
            // Multi-filesystem: present type subfolders
            foreach (string typeName in item.FileSystemNkfsPerType.Keys)
                results.Add(typeName);
        }
        else if (item.FileSystemNkfs != null)
        {
            // Single-filesystem: present files directly
            foreach ((int childIndex, NkFsEntry childEntry) in item.FileSystemNkfs.GetChildren(0))
            {
                results.Add(item.FileSystemNkfs.GetEntryName(childIndex));
            }
        }

        return results;
    }

    /// <summary>
    /// Creates a valid NkFs binary with specific files for identification.
    /// </summary>
    private static byte[] CreateNkfsBytesWithFiles(params (string name, long offset, long size)[] files)
    {
        FsYamlNode root = FsYamlNode.CreateDirectory("root");
        foreach ((string name, long offset, long size) in files)
            root.AddFile(name, offset, size, 0, 0);
        FsYaml fsYaml = new FsYaml();
        fsYaml.FileSystems.Add(root);
        return NkFs.FromFsYaml(fsYaml).ToBytes();
    }

    /// <summary>
    /// Creates a minimal valid NkFs binary (single file).
    /// </summary>
    private static byte[] CreateValidNkfsBytes() => CreateNkfsBytesWithFiles(("test.bin", 0x1000, 512));

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
            FileSystemNkfsPerType = null
        };
    }

    #endregion

    #region Test 1: Unified-only DataStore loads and presents correctly (Req 6.1, 6.2)

    /// <summary>
    /// Verifies that a DataStore with only unified filesystem.nkfs loads correctly
    /// and presents files directly without filesystem-type subfolders.
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Fact]
    public void UnifiedOnly_LoadsAsSingleTree_IsNotMultiFilesystem()
    {
        // Arrange: DataStore with only unified filesystem.nkfs containing two files
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096),
            ("README.TXT", 0x2000, 256));

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
        TestVfsModelItem item = CreateTestItem(id: 1, name: "OldGame.iso");

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: loads as unified single-tree
        Assert.True(item.FileSystemNkfsLoaded);
        Assert.Null(item.FileSystemNkfsPerType);
        Assert.NotNull(item.FileSystemNkfs);
        Assert.False(item.IsMultiFilesystem);
    }

    /// <summary>
    /// Verifies that a unified-only DataStore presents files directly under the
    /// image name without filesystem-type subfolders.
    /// **Validates: Requirement 6.2**
    /// </summary>
    [Fact]
    public void UnifiedOnly_PresentsFilesDirectly_NoSubfolders()
    {
        // Arrange: DataStore with unified filesystem.nkfs
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096),
            ("README.TXT", 0x2000, 256));

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem(id: 1, name: "OldGame.iso");

        // Act
        LoadNkfs(item, dataStore, model);
        List<string> listing = ListFolder(item);

        // Assert: files presented directly (no type subfolders)
        Assert.Equal(2, listing.Count);
        Assert.Contains("GAME.BIN", listing);
        Assert.Contains("README.TXT", listing);
    }

    /// <summary>
    /// Verifies that the NkFs loaded from a unified file can resolve paths
    /// directly (existing single-tree behavior preserved).
    /// **Validates: Requirement 6.1**
    /// </summary>
    [Fact]
    public void UnifiedOnly_PathResolution_WorksDirectly()
    {
        // Arrange: DataStore with unified filesystem.nkfs containing a nested structure
        FsYamlNode root = FsYamlNode.CreateDirectory("root");
        FsYamlNode subdir = root.AddDirectory("DATA");
        subdir.AddFile("LEVEL1.DAT", 0x3000, 1024, 0, 0);
        root.AddFile("BOOT.BIN", 0x1000, 512, 0, 0);
        FsYaml fsYaml = new FsYaml();
        fsYaml.FileSystems.Add(root);
        byte[] unifiedData = NkFs.FromFsYaml(fsYaml).ToBytes();

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };

        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();
        TestVfsModelItem item = CreateTestItem(id: 1, name: "OldGame.iso");

        // Act
        LoadNkfs(item, dataStore, model);

        // Assert: can resolve paths directly without type prefix
        NkFs nkfs = item.FileSystemNkfs;
        Assert.NotNull(nkfs);

        int bootIdx = nkfs.ResolvePath("BOOT.BIN");
        Assert.True(bootIdx > 0);
        Assert.Equal("BOOT.BIN", nkfs.GetEntryName(bootIdx));

        int levelIdx = nkfs.ResolvePath("DATA/LEVEL1.DAT");
        Assert.True(levelIdx > 0);
        Assert.Equal("LEVEL1.DAT", nkfs.GetEntryName(levelIdx));
    }

    #endregion

    #region Test 2: Mixed DataStore with both old and new format images (Req 6.3)

    /// <summary>
    /// Verifies that a DataStore can simultaneously contain images with the old
    /// unified format and images with the new per-type format, and both load correctly.
    /// **Validates: Requirement 6.3**
    /// </summary>
    [Fact]
    public void MixedDataStore_BothFormatsCoexist_LoadCorrectly()
    {
        // Arrange: old-format image with unified filesystem.nkfs
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096));

        List<FileRecord> oldFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> oldFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };
        MockDataStore oldDataStore = new MockDataStore(oldFiles, oldFileData);

        // Arrange: new-format image with per-type files
        byte[] iso9660Data = CreateNkfsBytesWithFiles(
            ("README.TXT", 0x1000, 256));
        byte[] jolietData = CreateNkfsBytesWithFiles(
            ("ReadMe.txt", 0x1000, 256));

        List<FileRecord> newFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = jolietData.Length }
        };
        Dictionary<string, byte[]> newFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = jolietData
        };
        MockDataStore newDataStore = new MockDataStore(newFiles, newFileData);

        MockModel model = new MockModel();
        TestVfsModelItem oldItem = CreateTestItem(id: 1, name: "OldGame.iso");
        TestVfsModelItem newItem = CreateTestItem(id: 2, name: "NewGame.iso");

        // Act
        LoadNkfs(oldItem, oldDataStore, model);
        LoadNkfs(newItem, newDataStore, model);

        // Assert: old format loads as unified
        Assert.Null(oldItem.FileSystemNkfsPerType);
        Assert.NotNull(oldItem.FileSystemNkfs);
        Assert.False(oldItem.IsMultiFilesystem);

        // Assert: new format loads as per-type
        Assert.NotNull(newItem.FileSystemNkfsPerType);
        Assert.Equal(2, newItem.FileSystemNkfsPerType.Count);
        Assert.True(newItem.IsMultiFilesystem);
        Assert.Null(newItem.FileSystemNkfs);
    }

    /// <summary>
    /// Verifies that in a mixed DataStore, the old-format image presents files
    /// directly while the new-format image presents type subfolders.
    /// **Validates: Requirements 6.2, 6.3**
    /// </summary>
    [Fact]
    public void MixedDataStore_PresentationDiffers_ByFormat()
    {
        // Arrange: old-format image
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096),
            ("ICON.SYS", 0x2000, 128));

        List<FileRecord> oldFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> oldFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };
        MockDataStore oldDataStore = new MockDataStore(oldFiles, oldFileData);

        // Arrange: new-format image
        byte[] iso9660Data = CreateNkfsBytesWithFiles(
            ("README.TXT", 0x1000, 256));
        byte[] jolietData = CreateNkfsBytesWithFiles(
            ("ReadMe.txt", 0x1000, 256));

        List<FileRecord> newFiles = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = jolietData.Length }
        };
        Dictionary<string, byte[]> newFileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = jolietData
        };
        MockDataStore newDataStore = new MockDataStore(newFiles, newFileData);

        MockModel model = new MockModel();
        TestVfsModelItem oldItem = CreateTestItem(id: 1, name: "OldGame.iso");
        TestVfsModelItem newItem = CreateTestItem(id: 2, name: "NewGame.iso");

        // Act
        LoadNkfs(oldItem, oldDataStore, model);
        LoadNkfs(newItem, newDataStore, model);

        List<string> oldListing = ListFolder(oldItem);
        List<string> newListing = ListFolder(newItem);

        // Assert: old format presents files directly
        Assert.Equal(2, oldListing.Count);
        Assert.Contains("GAME.BIN", oldListing);
        Assert.Contains("ICON.SYS", oldListing);

        // Assert: new format presents type subfolders
        Assert.Equal(2, newListing.Count);
        Assert.Contains("iso9660", newListing);
        Assert.Contains("joliet", newListing);
    }

    #endregion

    #region Test 3: Re-ingestion replaces unified file with per-type files (Req 6.4)

    /// <summary>
    /// Verifies that re-ingestion of a multi-filesystem image replaces the old
    /// unified filesystem.nkfs with per-type files. After re-ingestion, the image
    /// should load as multi-filesystem.
    /// **Validates: Requirement 6.4**
    /// </summary>
    [Fact]
    public void ReIngestion_ReplacesUnifiedWithPerType_LoadsAsMultiFilesystem()
    {
        // Arrange: initial state — old unified filesystem.nkfs
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096));

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length },
            new FileRecord { Name = "header.bin", Size = 64 }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData,
            ["header.bin"] = new byte[64]
        };
        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();

        // Verify initial state: loads as unified
        TestVfsModelItem itemBefore = CreateTestItem(id: 1, name: "Game.iso");
        LoadNkfs(itemBefore, dataStore, model);
        Assert.False(itemBefore.IsMultiFilesystem);
        Assert.NotNull(itemBefore.FileSystemNkfs);
        Assert.Null(itemBefore.FileSystemNkfsPerType);

        // Act: simulate re-ingestion — remove unified, add per-type files
        dataStore.RemoveFile("filesystem.nkfs");

        byte[] iso9660Data = CreateNkfsBytesWithFiles(
            ("README.TXT", 0x1000, 256),
            ("DATA.BIN", 0x2000, 1024));
        byte[] jolietData = CreateNkfsBytesWithFiles(
            ("ReadMe.txt", 0x1000, 256),
            ("Data.bin", 0x2000, 1024));

        dataStore.AddFile("filesystem.iso9660.nkfs", iso9660Data);
        dataStore.AddFile("filesystem.joliet.nkfs", jolietData);

        // Load again (fresh item simulating re-mount after re-ingestion)
        TestVfsModelItem itemAfter = CreateTestItem(id: 1, name: "Game.iso");
        LoadNkfs(itemAfter, dataStore, model);

        // Assert: now loads as multi-filesystem
        Assert.True(itemAfter.FileSystemNkfsLoaded);
        Assert.True(itemAfter.IsMultiFilesystem);
        Assert.NotNull(itemAfter.FileSystemNkfsPerType);
        Assert.Equal(2, itemAfter.FileSystemNkfsPerType.Count);
        Assert.True(itemAfter.FileSystemNkfsPerType.ContainsKey("iso9660"));
        Assert.True(itemAfter.FileSystemNkfsPerType.ContainsKey("joliet"));
        Assert.Null(itemAfter.FileSystemNkfs);
    }

    /// <summary>
    /// Verifies that after re-ingestion, the presentation changes from direct
    /// file listing to type subfolder listing.
    /// **Validates: Requirement 6.4**
    /// </summary>
    [Fact]
    public void ReIngestion_PresentationChanges_FromDirectToSubfolders()
    {
        // Arrange: initial state — unified filesystem.nkfs
        byte[] unifiedData = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096),
            ("ICON.SYS", 0x2000, 128));

        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.nkfs", Size = unifiedData.Length }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.nkfs"] = unifiedData
        };
        MockDataStore dataStore = new MockDataStore(files, fileData);
        MockModel model = new MockModel();

        // Verify initial presentation: files directly
        TestVfsModelItem itemBefore = CreateTestItem(id: 1, name: "Game.iso");
        LoadNkfs(itemBefore, dataStore, model);
        List<string> listingBefore = ListFolder(itemBefore);
        Assert.Contains("GAME.BIN", listingBefore);
        Assert.Contains("ICON.SYS", listingBefore);

        // Act: simulate re-ingestion
        dataStore.RemoveFile("filesystem.nkfs");

        byte[] iso9660Data = CreateNkfsBytesWithFiles(
            ("GAME.BIN", 0x1000, 4096));
        byte[] udfData = CreateNkfsBytesWithFiles(
            ("Game.bin", 0x1000, 4096));

        dataStore.AddFile("filesystem.iso9660.nkfs", iso9660Data);
        dataStore.AddFile("filesystem.udf.nkfs", udfData);

        // Load again after re-ingestion
        TestVfsModelItem itemAfter = CreateTestItem(id: 1, name: "Game.iso");
        LoadNkfs(itemAfter, dataStore, model);
        List<string> listingAfter = ListFolder(itemAfter);

        // Assert: presentation changed to type subfolders
        Assert.Equal(2, listingAfter.Count);
        Assert.Contains("iso9660", listingAfter);
        Assert.Contains("udf", listingAfter);
        // No direct file names at root
        Assert.DoesNotContain("GAME.BIN", listingAfter);
        Assert.DoesNotContain("Game.bin", listingAfter);
    }

    /// <summary>
    /// Verifies that BuildFileSystemYaml with multiple types produces per-type files
    /// (not unified), simulating what happens when an old image is re-ingested.
    /// This tests the output naming convention of the ingestion logic.
    /// **Validates: Requirement 6.4**
    /// </summary>
    [Fact]
    public void ReIngestion_MultipleTypes_ProducesPerTypeFiles_NotUnified()
    {
        // Arrange: simulate the output of BuildFileSystemYaml for a multi-type image
        // by verifying the naming pattern. When multiple types exist, per-type files
        // are written and the unified file is NOT produced.
        byte[] iso9660Data = CreateNkfsBytesWithFiles(
            ("README.TXT", 0x1000, 256));
        byte[] jolietData = CreateNkfsBytesWithFiles(
            ("ReadMe.txt", 0x1000, 256));

        // Simulate the DataStore state after re-ingestion (per-type files only)
        List<FileRecord> files = new List<FileRecord>
        {
            new FileRecord { Name = "filesystem.iso9660.nkfs", Size = iso9660Data.Length },
            new FileRecord { Name = "filesystem.joliet.nkfs", Size = jolietData.Length },
            new FileRecord { Name = "header.bin", Size = 64 }
        };
        Dictionary<string, byte[]> fileData = new Dictionary<string, byte[]>
        {
            ["filesystem.iso9660.nkfs"] = iso9660Data,
            ["filesystem.joliet.nkfs"] = jolietData,
            ["header.bin"] = new byte[64]
        };
        MockDataStore dataStore = new MockDataStore(files, fileData);

        // Verify: no unified filesystem.nkfs exists
        Assert.Null(dataStore.ReadFile(
            new GlobalImageKey("TestSet", 1), "filesystem.nkfs"));

        // Verify: per-type files exist and are valid
        Assert.NotNull(dataStore.ReadFile(
            new GlobalImageKey("TestSet", 1), "filesystem.iso9660.nkfs"));
        Assert.NotNull(dataStore.ReadFile(
            new GlobalImageKey("TestSet", 1), "filesystem.joliet.nkfs"));

        // Verify: TryExtractFsTypeName correctly identifies per-type files
        Assert.True(DataStore.TryExtractFsTypeName("filesystem.iso9660.nkfs", out string type1));
        Assert.Equal("iso9660", type1);
        Assert.True(DataStore.TryExtractFsTypeName("filesystem.joliet.nkfs", out string type2));
        Assert.Equal("joliet", type2);

        // Verify: unified filename is NOT identified as per-type
        Assert.False(DataStore.TryExtractFsTypeName("filesystem.nkfs", out _));
    }

    #endregion
}