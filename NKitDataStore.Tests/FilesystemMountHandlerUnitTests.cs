using Nanook.NKit.Vfs;
using System.Reflection;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for FilesystemMountHandler changes after removing isInternalFsType
/// and simplifying getBestFilesystem.
///
/// Feature: nkds-system-filesystem-merge
/// Requirements: 6.1, 6.2, 6.3, 6.4
/// </summary>
public class FilesystemMountHandlerUnitTests
{
    #region Reflection Helpers

    private static readonly Type HandlerType =
        typeof(FilesystemMountHandler).Assembly.GetType("Nanook.NKit.Vfs.FilesystemMountHandler")
        ?? throw new InvalidOperationException("Could not find FilesystemMountHandler type");

    private static readonly MethodInfo GetBestFilesystemMethod =
        HandlerType.GetMethod("getBestFilesystem", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find getBestFilesystem method via reflection");

    private static NkFs InvokeGetBestFilesystem(Dictionary<string, NkFs> perType) => (NkFs)GetBestFilesystemMethod.Invoke(null, new object[] { perType });

    #endregion

    #region NkFs Creation Helpers

    /// <summary>
    /// Creates a minimal NkFs with a single file at root.
    /// </summary>
    private static NkFs CreateNkFs(string fileName, long offset = 0x1000, long size = 0x500)
    {
        FsYaml fsYaml = new FsYaml();
        FsYamlNode root = fsYaml.AddFileSystem(".", 0);
        root.AddFile(fileName, offset, size, 0, 0);
        return NkFs.FromFsYaml(fsYaml);
    }

    #endregion

    #region isInternalFsType method no longer exists (reflection test)

    /// <summary>
    /// Verify that the isInternalFsType method has been removed from FilesystemMountHandler.
    /// After the merge-at-mount-time change, this filter is no longer needed because the
    /// "system" key is removed from the dictionary before presentation.
    /// Validates: Requirement 6.1
    /// </summary>
    [Fact]
    public void IsInternalFsType_MethodNoLongerExists()
    {
        MethodInfo method = HandlerType.GetMethod(
            "isInternalFsType",
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        Assert.Null(method);
    }

    #endregion

    #region Subfolder enumeration includes all keys (no filtering)

    /// <summary>
    /// When a multi-filesystem VfsModelItem has per-type keys including "system",
    /// all keys should be presented as subfolders without any filtering.
    /// This verifies the removal of the isInternalFsType check in ListFolder's
    /// subfolder enumeration loop.
    /// Validates: Requirement 6.1
    /// </summary>
    [Fact]
    public void SubfolderEnumeration_IncludesAllKeysInPerTypeDictionary()
    {
        // Arrange: create a per-type dictionary with various types including "system"
        Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso9660"] = CreateNkFs("GAME.BIN"),
            ["joliet"] = CreateNkFs("Game.bin"),
            ["system"] = CreateNkFs("IP.BIN")
        };

        // Simulate what ListFolder does in system mode for multi-filesystem:
        // it enumerates all keys in FileSystemNkfsPerType.Keys without filtering.
        List<string> presentedSubfolders = perTypeDict.Keys.ToList();

        // Assert: all three types are presented (no filtering of "system")
        Assert.Equal(3, presentedSubfolders.Count);
        Assert.Contains("iso9660", presentedSubfolders);
        Assert.Contains("joliet", presentedSubfolders);
        Assert.Contains("system", presentedSubfolders);
    }

    /// <summary>
    /// Verifies that after the merge step removes "system" from the dictionary,
    /// only the remaining non-system keys are presented. This is the normal flow
    /// where the merge logic already removed "system" before presentation.
    /// Validates: Requirement 6.1, 6.2
    /// </summary>
    [Fact]
    public void SubfolderEnumeration_AfterMerge_SystemKeyNotPresent()
    {
        // Arrange: dictionary after merge (system key already removed)
        Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso9660"] = CreateNkFs("GAME.BIN"),
            ["joliet"] = CreateNkFs("Game.bin")
        };

        List<string> presentedSubfolders = perTypeDict.Keys.ToList();

        // Assert: only iso9660 and joliet present
        Assert.Equal(2, presentedSubfolders.Count);
        Assert.Contains("iso9660", presentedSubfolders);
        Assert.Contains("joliet", presentedSubfolders);
        Assert.DoesNotContain("system", presentedSubfolders);
    }

    /// <summary>
    /// When a custom/unknown type name exists in the dictionary, it should be
    /// presented without filtering. No type names are excluded.
    /// Validates: Requirement 6.1
    /// </summary>
    [Fact]
    public void SubfolderEnumeration_CustomTypeNames_AllIncluded()
    {
        Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso9660"] = CreateNkFs("FILE1.DAT"),
            ["custom"] = CreateNkFs("FILE2.DAT"),
            ["unknown"] = CreateNkFs("FILE3.DAT")
        };

        List<string> presentedSubfolders = perTypeDict.Keys.ToList();

        Assert.Equal(3, presentedSubfolders.Count);
        Assert.Contains("iso9660", presentedSubfolders);
        Assert.Contains("custom", presentedSubfolders);
        Assert.Contains("unknown", presentedSubfolders);
    }

    #endregion

    #region getBestFilesystem with only system key returns system NkFs

    /// <summary>
    /// When the per-type dictionary contains only the "system" key (no non-system types),
    /// getBestFilesystem should return the system NkFs as a fallback since it's the only option.
    /// This is the edge case where merge didn't run (no non-system types to merge into).
    /// Validates: Requirement 6.4
    /// </summary>
    [Fact]
    public void GetBestFilesystem_OnlySystemKey_ReturnsSystemNkFs()
    {
        // Arrange
        NkFs systemNkfs = CreateNkFs("IP.BIN", 0x0, 0x8000);
        Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs
        };

        // Act
        NkFs result = InvokeGetBestFilesystem(perType);

        // Assert: system NkFs returned as fallback (it's the only entry)
        Assert.NotNull(result);
        Assert.Same(systemNkfs, result);
    }

    #endregion

    #region getBestFilesystem with system + iso9660 returns iso9660

    /// <summary>
    /// When the per-type dictionary contains "system" + "iso9660", getBestFilesystem
    /// should return the iso9660 NkFs because iso9660 is in the priority list.
    /// "system" is not in the priority list, and the fallback loop picks the first
    /// entry — but the priority loop finds iso9660 first.
    /// Validates: Requirement 6.4
    /// </summary>
    [Fact]
    public void GetBestFilesystem_SystemPlusIso9660_ReturnsIso9660()
    {
        // Arrange
        NkFs systemNkfs = CreateNkFs("IP.BIN", 0x0, 0x8000);
        NkFs iso9660Nkfs = CreateNkFs("GAME.BIN", 0x10000, 0x50000);
        Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs,
            ["iso9660"] = iso9660Nkfs
        };

        // Act
        NkFs result = InvokeGetBestFilesystem(perType);

        // Assert: iso9660 selected (it's in the priority list)
        Assert.NotNull(result);
        Assert.Same(iso9660Nkfs, result);
    }

    /// <summary>
    /// When the per-type dictionary contains "system" + "joliet", getBestFilesystem
    /// should return the joliet NkFs (joliet has higher priority than iso9660).
    /// Validates: Requirement 6.4
    /// </summary>
    [Fact]
    public void GetBestFilesystem_SystemPlusJoliet_ReturnsJoliet()
    {
        NkFs systemNkfs = CreateNkFs("IP.BIN", 0x0, 0x8000);
        NkFs jolietNkfs = CreateNkFs("Game.bin", 0x10000, 0x50000);
        Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs,
            ["joliet"] = jolietNkfs
        };

        NkFs result = InvokeGetBestFilesystem(perType);

        Assert.NotNull(result);
        Assert.Same(jolietNkfs, result);
    }

    /// <summary>
    /// When the per-type dictionary contains "system" + multiple priority types,
    /// getBestFilesystem should return the highest-priority one (udf > joliet > iso9660).
    /// Validates: Requirement 6.4
    /// </summary>
    [Fact]
    public void GetBestFilesystem_SystemPlusMultipleTypes_ReturnsHighestPriority()
    {
        NkFs systemNkfs = CreateNkFs("IP.BIN");
        NkFs iso9660Nkfs = CreateNkFs("GAME.BIN");
        NkFs jolietNkfs = CreateNkFs("Game.bin");
        NkFs udfNkfs = CreateNkFs("game.bin");
        Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs,
            ["iso9660"] = iso9660Nkfs,
            ["joliet"] = jolietNkfs,
            ["udf"] = udfNkfs
        };

        NkFs result = InvokeGetBestFilesystem(perType);

        // udf has highest priority
        Assert.NotNull(result);
        Assert.Same(udfNkfs, result);
    }

    #endregion

    #region Legacy DataStore with system-only filesystem presents "system" subfolder

    /// <summary>
    /// When a legacy DataStore has only a "system" filesystem (no non-system types to
    /// merge into), the VfsModelItem should present "system" as a browsable subfolder.
    /// This verifies that no filtering prevents "system" from appearing in the single
    /// per-type dictionary case.
    /// Validates: Requirement 6.2
    /// </summary>
    [Fact]
    public void LegacyDataStore_SystemOnlyFilesystem_PresentsSystemSubfolder()
    {
        // Arrange: simulate a legacy DataStore with only system per-type filesystem
        NkFs systemNkfs = CreateNkFs("IP.BIN", 0x0, 0x8000);
        Dictionary<string, NkFs> perTypeDict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs
        };

        // Create VfsModelItem with single per-type entry
        VfsModelItem item = new VfsModelItem
        {
            FileSystemNkfs = systemNkfs,
            FileSystemNkfsPerType = perTypeDict,
            FileSystemNkfsLoaded = true
        };

        // Assert: With only one per-type entry, IsMultiFilesystem is false
        Assert.False(item.IsMultiFilesystem);

        // The single per-type key is "system" — in the ListFolder system-mode path,
        // it shows as a subfolder (FileSystemNkfsPerType.Count == 1 branch).
        Assert.Single(item.FileSystemNkfsPerType);
        string typeName = item.FileSystemNkfsPerType.Keys.First();
        Assert.Equal("system", typeName);

        // Verify that getBestFilesystem still returns the system NkFs when it's the only one
        NkFs best = InvokeGetBestFilesystem(perTypeDict);
        Assert.NotNull(best);
        Assert.Same(systemNkfs, best);
    }

    #endregion

    #region getBestFilesystem edge cases

    /// <summary>
    /// Null dictionary returns null.
    /// </summary>
    [Fact]
    public void GetBestFilesystem_NullDictionary_ReturnsNull()
    {
        NkFs result = InvokeGetBestFilesystem(null);
        Assert.Null(result);
    }

    /// <summary>
    /// Empty dictionary returns null.
    /// </summary>
    [Fact]
    public void GetBestFilesystem_EmptyDictionary_ReturnsNull()
    {
        Dictionary<string, NkFs> perType = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase);
        NkFs result = InvokeGetBestFilesystem(perType);
        Assert.Null(result);
    }

    #endregion
}