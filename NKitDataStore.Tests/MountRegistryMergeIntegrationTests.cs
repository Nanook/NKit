using Nanook.NKit.Vfs;
using System.Reflection;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for MountRegistry merge integration (isIso9660SystemType and mergeSystemFilesystem).
/// Uses reflection to invoke private static methods on the internal MountRegistry class.
///
/// Feature: nkds-system-filesystem-merge
/// Requirements: 5.1, 5.2, 5.3, 5.4, 2.1, 2.3
/// </summary>
public class MountRegistryMergeIntegrationTests
{
    #region Reflection Helpers

    private static readonly Type MountRegistryType = typeof(MountRegistry);

    private static readonly MethodInfo IsIso9660SystemTypeMethod =
        MountRegistryType.GetMethod("isIso9660SystemType", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find isIso9660SystemType method via reflection");

    private static readonly MethodInfo MergeSystemFilesystemMethod =
        MountRegistryType.GetMethod("mergeSystemFilesystem", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find mergeSystemFilesystem method via reflection");

    private static bool InvokeIsIso9660SystemType(string systemTypeName) => (bool)IsIso9660SystemTypeMethod.Invoke(null, new object[] { systemTypeName });

    private static void InvokeMergeSystemFilesystem(Dictionary<string, NkFs> perType) => MergeSystemFilesystemMethod.Invoke(null, new object[] { perType });

    #endregion

    #region NkFs Creation Helpers

    /// <summary>
    /// Creates a simple NkFs with one file at the root.
    /// </summary>
    private static NkFs CreateSimpleNkFs(string fileName, long offset, long size, bool isSystem = false)
    {
        FsYaml fsYaml = new FsYaml();
        FsYamlNode root = fsYaml.AddFileSystem(".", 0);
        root.AddFile(fileName, offset, size, 0, 0, isSystem: isSystem);
        return NkFs.FromFsYaml(fsYaml);
    }

    /// <summary>
    /// Creates an empty NkFs with just a root directory and no entries.
    /// </summary>
    private static NkFs CreateEmptyNkFs()
    {
        FsYaml fsYaml = new FsYaml();
        fsYaml.AddFileSystem(".", 0);
        return NkFs.FromFsYaml(fsYaml);
    }

    /// <summary>
    /// Recursively collects all file nodes from a FsYamlNode tree.
    /// </summary>
    private static void CollectFiles(FsYamlNode node, List<FsYamlNode> result)
    {
        if (node.IsFile)
        {
            result.Add(node);
            return;
        }
        if (node.Children != null)
            foreach (FsYamlNode child in node.Children)
                CollectFiles(child, result);
    }

    #endregion

    #region isIso9660SystemType — applicable types return true

    [Theory]
    [InlineData("Default")]
    [InlineData("Dreamcast")]
    [InlineData("PS1")]
    [InlineData("PS2")]
    [InlineData("PS3")]
    [InlineData("PSP")]
    [InlineData("SegaCD")]
    [InlineData("Saturn")]
    [InlineData("CDi")]
    [InlineData("PcEngine")]
    public void IsIso9660SystemType_ApplicableTypes_ReturnsTrue(string systemType) => Assert.True(InvokeIsIso9660SystemType(systemType));

    /// <summary>
    /// Verify case-insensitive matching for applicable types.
    /// Validates: Requirement 5.1
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("dreamcast")]
    [InlineData("ps1")]
    [InlineData("SEGACD")]
    [InlineData("pcengine")]
    public void IsIso9660SystemType_ApplicableTypes_CaseInsensitive_ReturnsTrue(string systemType) => Assert.True(InvokeIsIso9660SystemType(systemType));

    #endregion

    #region isIso9660SystemType — non-applicable types return false

    [Theory]
    [InlineData("GameCube")]
    [InlineData("Wii")]
    [InlineData("WiiU")]
    [InlineData("Xbox")]
    [InlineData("Xbox360")]
    [InlineData("NotSet")]
    public void IsIso9660SystemType_NonApplicableTypes_ReturnsFalse(string systemType) => Assert.False(InvokeIsIso9660SystemType(systemType));

    /// <summary>
    /// Null and empty strings should return false.
    /// Validates: Requirement 5.3
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsIso9660SystemType_NullOrEmpty_ReturnsFalse(string systemType) => Assert.False(InvokeIsIso9660SystemType(systemType));

    #endregion

    #region mergeSystemFilesystem — no system key (no-op)

    /// <summary>
    /// When the per-type dictionary does not contain a "system" key, the method
    /// should be a no-op and leave the dictionary unchanged.
    /// Validates: Requirement 2.1
    /// </summary>
    [Fact]
    public void MergeSystemFilesystem_NoSystemKey_DictionaryUnchanged()
    {
        Dictionary<string, NkFs> dict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso9660"] = CreateSimpleNkFs("GAME.BIN", 0x1000, 0x5000),
            ["joliet"] = CreateSimpleNkFs("GAME.BIN", 0x1000, 0x5000)
        };

        int originalCount = dict.Count;
        List<string> originalKeys = dict.Keys.ToList();

        InvokeMergeSystemFilesystem(dict);

        // Dictionary should be unchanged
        Assert.Equal(originalCount, dict.Count);
        Assert.Equal(originalKeys, dict.Keys.ToList());
    }

    #endregion

    #region mergeSystemFilesystem — only system key (retained)

    /// <summary>
    /// When "system" is the only key in the dictionary, the method should
    /// retain it since there are no non-system types to merge into.
    /// Validates: Requirement 2.3
    /// </summary>
    [Fact]
    public void MergeSystemFilesystem_OnlySystemKey_Retained()
    {
        NkFs systemNkfs = CreateSimpleNkFs("IP.BIN", 0x0, 0x8000, isSystem: true);
        Dictionary<string, NkFs> dict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["system"] = systemNkfs
        };

        InvokeMergeSystemFilesystem(dict);

        // "system" key should still be present
        Assert.Single(dict);
        Assert.True(dict.ContainsKey("system"));
    }

    #endregion

    #region Full merge flow: iso9660 + joliet + system → system removed, entries merged

    /// <summary>
    /// Full merge integration test:
    /// 1. Create a dictionary with "iso9660", "joliet", and "system" keys
    /// 2. Call mergeSystemFilesystem
    /// 3. Verify "system" key is removed
    /// 4. Verify iso9660 and joliet NkFs contain the merged system entries (with IsSystem=true)
    /// Validates: Requirements 2.1, 5.1
    /// </summary>
    [Fact]
    public void MergeSystemFilesystem_FullFlow_SystemRemovedAndEntriesMergedIntoBoth()
    {
        // Arrange: create system NkFs with a boot record entry
        FsYaml systemYaml = new FsYaml();
        FsYamlNode systemRoot = systemYaml.AddFileSystem(".", 0);
        systemRoot.AddFile("IP.BIN", 0x0, 0x8000, 0xDEAD, 0xBEEF, isSystem: true);
        FsYamlNode systemDir = systemRoot.AddDirectory("BOOT", isSystem: true);
        systemDir.AddFile("LOADER.BIN", 0x8000, 0x200, 0xCAFE, 0xBABE, isSystem: true);
        NkFs systemNkfs = NkFs.FromFsYaml(systemYaml);

        // Create iso9660 NkFs with a game file
        FsYaml isoYaml = new FsYaml();
        FsYamlNode isoRoot = isoYaml.AddFileSystem(".", 0);
        isoRoot.AddFile("GAME.BIN", 0x10000, 0x50000, 0x1111, 0x2222);
        NkFs isoNkfs = NkFs.FromFsYaml(isoYaml);

        // Create joliet NkFs with a different file
        FsYaml jolietYaml = new FsYaml();
        FsYamlNode jolietRoot = jolietYaml.AddFileSystem(".", 0);
        jolietRoot.AddFile("Game.bin", 0x10000, 0x50000, 0x1111, 0x2222);
        FsYamlNode jolietDir = jolietRoot.AddDirectory("Data");
        jolietDir.AddFile("readme.txt", 0x60000, 0x100, 0x3333, 0x4444);
        NkFs jolietNkfs = NkFs.FromFsYaml(jolietYaml);

        // Build dictionary
        Dictionary<string, NkFs> dict = new Dictionary<string, NkFs>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso9660"] = isoNkfs,
            ["joliet"] = jolietNkfs,
            ["system"] = systemNkfs
        };

        // Act
        InvokeMergeSystemFilesystem(dict);

        // Assert 1: "system" key is removed
        Assert.False(dict.ContainsKey("system"));
        Assert.Equal(2, dict.Count);
        Assert.True(dict.ContainsKey("iso9660"));
        Assert.True(dict.ContainsKey("joliet"));

        // Assert 2: iso9660 NkFs contains merged system entries
        FsYaml mergedIsoYaml = dict["iso9660"].ToFsYaml();
        FsYamlNode mergedIsoRoot = mergedIsoYaml.FileSystems[0];
        List<FsYamlNode> isoFiles = new List<FsYamlNode>();
        CollectFiles(mergedIsoRoot, isoFiles);

        // Should contain original GAME.BIN + system IP.BIN + system LOADER.BIN
        Assert.Contains(isoFiles, f => f.Name == "GAME.BIN" && !f.IsSystem);
        Assert.Contains(isoFiles, f => f.Name == "IP.BIN" && f.IsSystem && f.Offset == 0x0 && f.Size == 0x8000);
        Assert.Contains(isoFiles, f => f.Name == "LOADER.BIN" && f.IsSystem && f.Offset == 0x8000 && f.Size == 0x200);

        // Assert 3: joliet NkFs contains merged system entries
        FsYaml mergedJolietYaml = dict["joliet"].ToFsYaml();
        FsYamlNode mergedJolietRoot = mergedJolietYaml.FileSystems[0];
        List<FsYamlNode> jolietFiles = new List<FsYamlNode>();
        CollectFiles(mergedJolietRoot, jolietFiles);

        // Should contain original Game.bin + readme.txt + system IP.BIN + system LOADER.BIN
        Assert.Contains(jolietFiles, f => f.Name == "Game.bin" && !f.IsSystem);
        Assert.Contains(jolietFiles, f => f.Name == "readme.txt" && !f.IsSystem);
        Assert.Contains(jolietFiles, f => f.Name == "IP.BIN" && f.IsSystem && f.Offset == 0x0 && f.Size == 0x8000);
        Assert.Contains(jolietFiles, f => f.Name == "LOADER.BIN" && f.IsSystem && f.Offset == 0x8000 && f.Size == 0x200);
    }

    #endregion
}