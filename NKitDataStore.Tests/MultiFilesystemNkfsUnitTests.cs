using Nanook.NKit;
using Nanook.NKit.Steps.Shared;
using System.Reflection;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ResolveTargetFsType and TryExtractFsTypeName methods.
///
/// **Validates: Requirements 2.1, 2.2, 8.1, 8.2, 8.3**
/// </summary>
public class MultiFilesystemNkfsUnitTests
{
    #region ResolveTargetFsType Tests

    [Fact]
    public void ResolveTargetFsType_RockRidge_ReturnsIso9660()
    {
        string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.RockRidge);
        Assert.Equal("iso9660", result);
    }

    [Fact]
    public void ResolveTargetFsType_Cdxa_NullParent_ReturnsIso9660()
    {
        string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Cdxa, null);
        Assert.Equal("iso9660", result);
    }

    [Fact]
    public void ResolveTargetFsType_Cdxa_JolietParent_ReturnsJoliet()
    {
        string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Cdxa, FsType.Joliet);
        Assert.Equal("joliet", result);
    }

    [Fact]
    public void ResolveTargetFsType_Cdxa_UdfParent_ReturnsUdf()
    {
        string result = DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Cdxa, FsType.Udf);
        Assert.Equal("udf", result);
    }

    [Fact]
    public void ResolveTargetFsType_Iso9660_ReturnsIso9660() => Assert.Equal("iso9660", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Iso9660));

    [Fact]
    public void ResolveTargetFsType_Joliet_ReturnsJoliet() => Assert.Equal("joliet", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Joliet));

    [Fact]
    public void ResolveTargetFsType_Udf_ReturnsUdf() => Assert.Equal("udf", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Udf));

    [Fact]
    public void ResolveTargetFsType_ElTorito_ReturnsEltorito() => Assert.Equal("eltorito", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.ElTorito));

    [Fact]
    public void ResolveTargetFsType_Romeo_ReturnsRomeo() => Assert.Equal("romeo", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Romeo));

    [Fact]
    public void ResolveTargetFsType_System_ReturnsSystem() => Assert.Equal("system", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.System));

    [Fact]
    public void ResolveTargetFsType_Cdi_ReturnsCdi() => Assert.Equal("cdi", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Cdi));

    [Fact]
    public void ResolveTargetFsType_Other_ReturnsOther() => Assert.Equal("other", DataStoreIso9660Formatter.ResolveTargetFsType(FsType.Other));

    #endregion

    #region TryExtractFsTypeName Tests

    [Theory]
    [InlineData("filesystem.iso9660.nkfs", true, "iso9660")]
    [InlineData("filesystem.joliet.nkfs", true, "joliet")]
    [InlineData("filesystem.udf.nkfs", true, "udf")]
    [InlineData("filesystem.eltorito.nkfs", true, "eltorito")]
    [InlineData("filesystem.romeo.nkfs", true, "romeo")]
    [InlineData("filesystem.system.nkfs", true, "system")]
    [InlineData("filesystem.cdi.nkfs", true, "cdi")]
    public void TryExtractFsTypeName_ValidPerTypeNames_ExtractsCorrectly(string fileName, bool expectedResult, string expectedType)
    {
        bool result = DataStore.TryExtractFsTypeName(fileName, out string typeName);
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedType, typeName);
    }

    [Fact]
    public void TryExtractFsTypeName_UnifiedFormat_ReturnsFalse()
    {
        bool result = DataStore.TryExtractFsTypeName("filesystem.nkfs", out string typeName);
        Assert.False(result);
        Assert.Null(typeName);
    }

    [Fact]
    public void TryExtractFsTypeName_EmptyString_ReturnsFalse()
    {
        bool result = DataStore.TryExtractFsTypeName("", out string typeName);
        Assert.False(result);
        Assert.Null(typeName);
    }

    [Fact]
    public void TryExtractFsTypeName_NoDots_ReturnsFalse()
    {
        bool result = DataStore.TryExtractFsTypeName("other.txt", out string typeName);
        Assert.False(result);
        Assert.Null(typeName);
    }

    [Fact]
    public void TryExtractFsTypeName_SingleDotNoMatch_ReturnsFalse()
    {
        bool result = DataStore.TryExtractFsTypeName("filesystem.", out string typeName);
        Assert.False(result);
        Assert.Null(typeName);
    }

    [Theory]
    [InlineData("FILESYSTEM.ISO9660.NKFS", true, "iso9660")]
    [InlineData("Filesystem.Joliet.Nkfs", true, "joliet")]
    [InlineData("FILESYSTEM.UDF.NKFS", true, "udf")]
    public void TryExtractFsTypeName_CaseInsensitive_ExtractsLowercase(string fileName, bool expectedResult, string expectedType)
    {
        bool result = DataStore.TryExtractFsTypeName(fileName, out string typeName);
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedType, typeName);
    }

    [Theory]
    [InlineData("notfilesystem.iso9660.nkfs")]
    [InlineData("filesystem.iso9660.txt")]
    [InlineData("something.else.entirely")]
    public void TryExtractFsTypeName_NonMatchingPatterns_ReturnsFalse(string fileName)
    {
        bool result = DataStore.TryExtractFsTypeName(fileName, out string typeName);
        Assert.False(result);
        Assert.Null(typeName);
    }

    #endregion

    #region selectBestFsYaml Tests

    /// <summary>
    /// Helper to invoke the private static selectBestFsYaml method via reflection.
    /// </summary>
    private static FsYaml invokeSelectBestFsYaml(Dictionary<string, FsYaml> perTypeYaml)
    {
        MethodInfo method = typeof(DataStoreIso9660Formatter).GetMethod(
            "selectBestFsYaml",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (FsYaml)method!.Invoke(null, new object[] { perTypeYaml })!;
    }

    /// <summary>
    /// Helper to create a simple FsYaml with a named root filesystem entry.
    /// </summary>
    private static FsYaml createSimpleFsYaml(string rootName)
    {
        FsYaml fsYaml = new FsYaml();
        fsYaml.AddFileSystem(rootName, 0);
        return fsYaml;
    }

    [Fact]
    public void SelectBestFsYaml_SystemPlusIso9660_ReturnsIso9660()
    {
        // Arrange: dictionary has "system" and "iso9660"
        FsYaml iso9660 = createSimpleFsYaml("iso9660root");
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "system", system },
            { "iso9660", iso9660 }
        };

        // Act
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert: "iso9660" is in the priority list, so it should be selected (not "system")
        Assert.Same(iso9660, result);
    }

    [Fact]
    public void SelectBestFsYaml_SystemPlusJoliet_ReturnsJoliet()
    {
        // Arrange: dictionary has "system" and "joliet"
        FsYaml joliet = createSimpleFsYaml("jolietroot");
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "system", system },
            { "joliet", joliet }
        };

        // Act
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert: "joliet" is in the priority list, so it should be selected (not "system")
        Assert.Same(joliet, result);
    }

    [Fact]
    public void SelectBestFsYaml_SystemPlusUnknownType_ReturnsUnknownType()
    {
        // Arrange: dictionary has "system" and a type NOT in the priority list ("custom")
        FsYaml custom = createSimpleFsYaml("customroot");
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "system", system },
            { "custom", custom }
        };

        // Act: neither is in _fsPriority, so fallback loop should skip "system" and pick "custom"
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert
        Assert.Same(custom, result);
    }

    [Fact]
    public void SelectBestFsYaml_SystemPlusMultipleTypes_NeverReturnsSystem()
    {
        // Arrange: dictionary has "system", "iso9660", and "udf"
        FsYaml iso9660 = createSimpleFsYaml("iso9660root");
        FsYaml udf = createSimpleFsYaml("udfroot");
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "system", system },
            { "iso9660", iso9660 },
            { "udf", udf }
        };

        // Act: "udf" has highest priority in _fsPriority
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert: "udf" selected (highest priority), and never "system"
        Assert.Same(udf, result);
        Assert.NotSame(system, result);
    }

    [Fact]
    public void SelectBestFsYaml_OnlySystem_ReturnsFallbackToSystem()
    {
        // Arrange: dictionary has ONLY "system" — no other types exist
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "system", system }
        };

        // Act: priority loop finds nothing, fallback loop skips "system",
        // final fallback returns perTypeYaml.Values.First() which IS system
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert: when only "system" exists, it's selected as ultimate fallback
        Assert.Same(system, result);
    }

    [Fact]
    public void SelectBestFsYaml_SystemCaseInsensitive_SkipsSystemUpperCase()
    {
        // Arrange: "SYSTEM" (uppercase) + "iso9660"
        FsYaml iso9660 = createSimpleFsYaml("iso9660root");
        FsYaml system = createSimpleFsYaml("systemroot");
        Dictionary<string, FsYaml> dict = new Dictionary<string, FsYaml>
        {
            { "SYSTEM", system },
            { "iso9660", iso9660 }
        };

        // Act
        FsYaml result = invokeSelectBestFsYaml(dict);

        // Assert: case-insensitive skip means "SYSTEM" is still not selected
        Assert.Same(iso9660, result);
    }

    #endregion
}