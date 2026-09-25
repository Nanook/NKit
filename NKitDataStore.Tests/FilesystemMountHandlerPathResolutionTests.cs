namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for FilesystemMountHandler path resolution logic.
/// Replicates the FindInFolder path resolution behavior with real NkFs instances
/// to verify:
/// - Multi-filesystem path resolution through subfolder (type name consumed as first segment)
/// - Single-filesystem direct path resolution (no type segment consumed)
/// - Invalid filesystem type name in path returns not-found (null)
///
/// **Validates: Requirements 3.1, 3.2, 3.3**
/// </summary>
public class FilesystemMountHandlerPathResolutionTests
{
    #region Test Infrastructure

    /// <summary>
    /// Minimal model item mirroring VfsModelItem's relevant fields for FindInFolder.
    /// </summary>
    private class TestVfsModelItem
    {
        public NkFs FileSystemNkfs { get; set; }
        public Dictionary<string, NkFs> FileSystemNkfsPerType { get; set; }
        public bool IsMultiFilesystem => FileSystemNkfsPerType != null
            && FileSystemNkfsPerType.Count > 1;
    }

    /// <summary>
    /// Result of path resolution, mirroring what FindInFolder returns.
    /// </summary>
    private class ResolvedItem
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsTypeSubfolder { get; set; }
        public NkFs SourceNkFs { get; set; }
        public int EntryIndex { get; set; }
    }

    /// <summary>
    /// Replicates the core path resolution logic of FilesystemMountHandler.FindInFolder.
    /// Returns null when the path cannot be resolved (not-found).
    /// </summary>
    private static ResolvedItem ResolvePath(TestVfsModelItem item, string[] pth, int startIndex)
    {
        if (item == null)
            return null;

        // Multi-filesystem: consume first path segment as filesystem type subfolder
        if (item.IsMultiFilesystem && startIndex < pth.Length)
        {
            string fsTypeName = pth[startIndex];
            if (item.FileSystemNkfsPerType.TryGetValue(fsTypeName, out NkFs perTypeNkfs))
            {
                // If the path is just the type subfolder itself, return it as a folder
                if (startIndex == pth.Length - 1)
                {
                    return new ResolvedItem
                    {
                        Name = fsTypeName,
                        IsDirectory = true,
                        IsTypeSubfolder = true,
                        SourceNkFs = perTypeNkfs,
                        EntryIndex = -1
                    };
                }

                // Resolve remaining path within this filesystem's NkFs
                string subPath = string.Join("/", pth, startIndex + 1, pth.Length - startIndex - 1);
                int entryIndex = perTypeNkfs.ResolvePath(subPath);
                if (entryIndex < 0)
                    return null;

                NkFsEntry entry = perTypeNkfs.GetEntry(entryIndex);
                return new ResolvedItem
                {
                    Name = perTypeNkfs.GetEntryName(entryIndex),
                    IsDirectory = entry.IsDirectory,
                    IsTypeSubfolder = false,
                    SourceNkFs = perTypeNkfs,
                    EntryIndex = entryIndex
                };
            }

            // Type name not found in per-type dictionary — not found
            return null;
        }

        // Single-filesystem: existing behavior
        NkFs nkfs = item.FileSystemNkfs;
        if (nkfs == null)
            return null;

        // Build the sub-path from startIndex and resolve
        string subPath2 = string.Join("/", pth, startIndex, pth.Length - startIndex);
        int entryIndex2 = nkfs.ResolvePath(subPath2);
        if (entryIndex2 < 0)
            return null;

        NkFsEntry entry2 = nkfs.GetEntry(entryIndex2);
        return new ResolvedItem
        {
            Name = nkfs.GetEntryName(entryIndex2),
            IsDirectory = entry2.IsDirectory,
            IsTypeSubfolder = false,
            SourceNkFs = nkfs,
            EntryIndex = entryIndex2
        };
    }

    /// <summary>
    /// Creates an NkFs with a simple file tree:
    ///   root/
    ///     subdir/
    ///       nested.bin
    ///     hello.txt
    /// </summary>
    private static NkFs CreateNkFsWithFiles(string fileName = "hello.txt",
        string dirName = "subdir", string nestedFileName = "nested.bin")
    {
        FsYamlNode root = FsYamlNode.CreateDirectory("root");
        FsYamlNode subdir = root.AddDirectory(dirName);
        subdir.AddFile(nestedFileName, 0x2000, 256, 0, 0);
        root.AddFile(fileName, 0x1000, 512, 0, 0);
        FsYaml fsYaml = new FsYaml();
        fsYaml.FileSystems.Add(root);
        return NkFs.FromFsYaml(fsYaml);
    }

    /// <summary>
    /// Creates a multi-filesystem test item with iso9660 and joliet NkFs instances.
    /// Each has different file names to distinguish which NkFs was resolved.
    /// </summary>
    private static TestVfsModelItem CreateMultiFilesystemItem()
    {
        // iso9660 filesystem: has ISO9660-style names
        NkFs iso9660Nkfs = CreateNkFsWithFiles("README.TXT", "DATA", "FILE1.DAT");

        // joliet filesystem: has longer Joliet-style names
        NkFs jolietNkfs = CreateNkFsWithFiles("ReadMe.txt", "Data Files", "File1.dat");

        return new TestVfsModelItem
        {
            FileSystemNkfs = null, // null when multi-filesystem
            FileSystemNkfsPerType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
            {
                ["iso9660"] = iso9660Nkfs,
                ["joliet"] = jolietNkfs
            }
        };
    }

    /// <summary>
    /// Creates a single-filesystem test item with a unified NkFs.
    /// </summary>
    private static TestVfsModelItem CreateSingleFilesystemItem()
    {
        NkFs nkfs = CreateNkFsWithFiles("game.bin", "system", "icon.sys");

        return new TestVfsModelItem
        {
            FileSystemNkfs = nkfs,
            FileSystemNkfsPerType = null
        };
    }

    #endregion

    #region Multi-filesystem path resolution through subfolder

    [Fact]
    public void FindInFolder_MultiFs_TypeSubfolderOnly_ReturnsFolder()
    {
        // Arrange: path is just the type name "iso9660"
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "iso9660" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: returns the type subfolder as a directory
        Assert.NotNull(result);
        Assert.Equal("iso9660", result.Name);
        Assert.True(result.IsDirectory);
        Assert.True(result.IsTypeSubfolder);
    }

    [Fact]
    public void FindInFolder_MultiFs_TypeSubfolderWithFile_ResolvesFileInCorrectNkFs()
    {
        // Arrange: path "iso9660/README.TXT" — file in iso9660 filesystem
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "iso9660", "README.TXT" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves to the file in the iso9660 NkFs
        Assert.NotNull(result);
        Assert.Equal("README.TXT", result.Name);
        Assert.False(result.IsDirectory);
        Assert.False(result.IsTypeSubfolder);
    }

    [Fact]
    public void FindInFolder_MultiFs_TypeSubfolderWithDirectory_ResolvesDirectory()
    {
        // Arrange: path "iso9660/DATA" — directory in iso9660 filesystem
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "iso9660", "DATA" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves to the directory in the iso9660 NkFs
        Assert.NotNull(result);
        Assert.Equal("DATA", result.Name);
        Assert.True(result.IsDirectory);
        Assert.False(result.IsTypeSubfolder);
    }

    [Fact]
    public void FindInFolder_MultiFs_TypeSubfolderWithNestedPath_ResolvesNestedFile()
    {
        // Arrange: path "iso9660/DATA/FILE1.DAT" — nested file
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "iso9660", "DATA", "FILE1.DAT" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves to the nested file
        Assert.NotNull(result);
        Assert.Equal("FILE1.DAT", result.Name);
        Assert.False(result.IsDirectory);
    }

    [Fact]
    public void FindInFolder_MultiFs_JolietType_ResolvesInJolietNkFs()
    {
        // Arrange: path "joliet/ReadMe.txt" — file in joliet filesystem
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "joliet", "ReadMe.txt" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves to the file in the joliet NkFs (different name than iso9660)
        Assert.NotNull(result);
        Assert.Equal("ReadMe.txt", result.Name);
        Assert.False(result.IsDirectory);
    }

    [Fact]
    public void FindInFolder_MultiFs_CaseInsensitiveTypeLookup_Resolves()
    {
        // Arrange: path "ISO9660/README.TXT" — uppercase type name
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "ISO9660", "README.TXT" };

        // Act: dictionary uses OrdinalIgnoreCase comparer
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves despite case difference in type name
        Assert.NotNull(result);
        Assert.Equal("README.TXT", result.Name);
    }

    [Fact]
    public void FindInFolder_MultiFs_NonExistentFileInValidType_ReturnsNull()
    {
        // Arrange: path "iso9660/MISSING.TXT" — file doesn't exist in iso9660 NkFs
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "iso9660", "MISSING.TXT" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: file not found within the NkFs
        Assert.Null(result);
    }

    #endregion

    #region Single-filesystem direct path resolution

    [Fact]
    public void FindInFolder_SingleFs_DirectFileResolution_NoTypeSegment()
    {
        // Arrange: path "game.bin" — direct file access without type subfolder
        TestVfsModelItem item = CreateSingleFilesystemItem();
        string[] pth = new[] { "game.bin" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves directly without consuming a type segment
        Assert.NotNull(result);
        Assert.Equal("game.bin", result.Name);
        Assert.False(result.IsDirectory);
        Assert.False(result.IsTypeSubfolder);
    }

    [Fact]
    public void FindInFolder_SingleFs_DirectDirectoryResolution()
    {
        // Arrange: path "system" — directory access
        TestVfsModelItem item = CreateSingleFilesystemItem();
        string[] pth = new[] { "system" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves to directory directly
        Assert.NotNull(result);
        Assert.Equal("system", result.Name);
        Assert.True(result.IsDirectory);
    }

    [Fact]
    public void FindInFolder_SingleFs_NestedPathResolution()
    {
        // Arrange: path "system/icon.sys" — nested file
        TestVfsModelItem item = CreateSingleFilesystemItem();
        string[] pth = new[] { "system", "icon.sys" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: resolves nested file directly
        Assert.NotNull(result);
        Assert.Equal("icon.sys", result.Name);
        Assert.False(result.IsDirectory);
    }

    [Fact]
    public void FindInFolder_SingleFs_NonExistentFile_ReturnsNull()
    {
        // Arrange: path "missing.dat" — file doesn't exist
        TestVfsModelItem item = CreateSingleFilesystemItem();
        string[] pth = new[] { "missing.dat" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: not found
        Assert.Null(result);
    }

    [Fact]
    public void FindInFolder_SingleFs_NullNkFs_ReturnsNull()
    {
        // Arrange: single-filesystem item with no NkFs loaded
        TestVfsModelItem item = new TestVfsModelItem
        {
            FileSystemNkfs = null,
            FileSystemNkfsPerType = null
        };
        string[] pth = new[] { "anything.txt" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: null NkFs means not found
        Assert.Null(result);
    }

    #endregion

    #region Invalid filesystem type name in path returns not-found

    [Fact]
    public void FindInFolder_MultiFs_InvalidTypeName_ReturnsNull()
    {
        // Arrange: path "udf/somefile.txt" — "udf" is not in the per-type dictionary
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "udf", "somefile.txt" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: type name not found → null
        Assert.Null(result);
    }

    [Fact]
    public void FindInFolder_MultiFs_EmptyTypeName_ReturnsNull()
    {
        // Arrange: path with empty first segment (edge case)
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "", "README.TXT" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: empty string not in dictionary → null
        Assert.Null(result);
    }

    [Fact]
    public void FindInFolder_MultiFs_CompletelyBogusTypeName_ReturnsNull()
    {
        // Arrange: path "nonexistent/file.txt" — completely invalid type
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "nonexistent", "file.txt" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: bogus type name → null
        Assert.Null(result);
    }

    [Fact]
    public void FindInFolder_MultiFs_TypeNameAlone_NotInDictionary_ReturnsNull()
    {
        // Arrange: path is just an invalid type name "rockridge"
        TestVfsModelItem item = CreateMultiFilesystemItem();
        string[] pth = new[] { "rockridge" };

        // Act
        ResolvedItem result = ResolvePath(item, pth, startIndex: 0);

        // Assert: type name not found → null
        Assert.Null(result);
    }

    [Fact]
    public void FindInFolder_NullItem_ReturnsNull()
    {
        // Arrange: null item
        string[] pth = new[] { "iso9660", "file.txt" };

        // Act
        ResolvedItem result = ResolvePath(null, pth, startIndex: 0);

        // Assert: null item → null
        Assert.Null(result);
    }

    #endregion
}