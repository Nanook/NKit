using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for FormatMappings.GetTargetFormats.
///
/// Feature: export-format-defaults, Property 2: Format options are exactly the defined mapping
/// **Validates: Requirements 3.5**
/// </summary>
public class FormatMappingsPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// GameCube/iso returns exactly ["iso", "rvz", "ciso", "wbfs"].
    /// </summary>
    [Fact]
    public void GameCube_Iso_ReturnsExactFormats()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("GameCube", "iso");

        Assert.Equal(new[] { "iso", "rvz", "ciso", "wbfs" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Wii/iso returns exactly ["iso", "rvz", "ciso", "wbfs"].
    /// </summary>
    [Fact]
    public void Wii_Iso_ReturnsExactFormats()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("Wii", "iso");

        Assert.Equal(new[] { "iso", "rvz", "ciso", "wbfs" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// WiiU/iso returns exactly ["iso", "wux", "app"].
    /// </summary>
    [Fact]
    public void WiiU_Iso_ReturnsExactFormats()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("WiiU", "iso");

        Assert.Equal(new[] { "iso", "wux", "app" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// WiiU/app returns exactly ["app"].
    /// </summary>
    [Fact]
    public void WiiU_App_ReturnsExactFormats()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("WiiU", "app");

        Assert.Equal(new[] { "app" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 9.1**
    ///
    /// Directories/folder returns exactly ["dir"].
    /// </summary>
    [Fact]
    public void Directories_Folder_ReturnsExactFormats()
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats("Directories", "folder");

        Assert.Equal(new[] { "dir" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Property 2 (legacy): Unknown (System, SourceFormat) pairs return a single-element list
    /// containing the source format (lowercased).
    /// For any system/sourceFormat pair that is NOT one of the known mappings,
    /// the result is a single-element list with the lowercased sourceFormat.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool UnknownPairs_ReturnSingleElementList_WithLowercasedSourceFormat(
        NonEmptyString systemWrapper, NonEmptyString formatWrapper)
    {
        string system = systemWrapper.Get;
        string sourceFormat = formatWrapper.Get;

        // Known pairs to exclude (all defined mappings)
        string normalizedSystem = system.ToLowerInvariant();
        string normalizedFormat = sourceFormat.ToLowerInvariant();

        if (IsDefinedPair(normalizedSystem, normalizedFormat))
            return true; // vacuously true for known pairs

        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, sourceFormat);

        return result.Count == 1 && result[0] == sourceFormat.ToLowerInvariant();
    }

    /// <summary>
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    ///
    /// Property 2: FormatMappings Fallback for Unknown Pairs
    /// For any (system, sourceFormat) pair that is NOT in the defined mapping set,
    /// GetTargetFormats returns a single-element list containing the lowercased sourceFormat.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Fallback_UnknownPairs_ReturnSingleElementWithLowercasedSourceFormat(
        NonEmptyString systemWrapper, NonEmptyString formatWrapper)
    {
        string system = systemWrapper.Get;
        string sourceFormat = formatWrapper.Get;

        string normalizedSystem = system.ToLowerInvariant();
        string normalizedFormat = sourceFormat.ToLowerInvariant();

        // Skip defined pairs — we only test the fallback for unknown pairs
        if (IsDefinedPair(normalizedSystem, normalizedFormat))
            return true; // vacuously true for known pairs

        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, sourceFormat);

        // Fallback must return exactly one element: the lowercased sourceFormat
        return result.Count == 1 && result[0] == normalizedFormat;
    }

    /// <summary>
    /// Returns true if the (system, sourceFormat) pair is in the defined mapping set.
    /// </summary>
    private static bool IsDefinedPair(string normalizedSystem, string normalizedFormat)
    {
        return (normalizedSystem, normalizedFormat) is
            // Original entries
            ("gamecube", "iso") or
            ("wii", "iso") or
            ("wiiu", "iso") or
            ("wiiu", "app") or
            ("directories", "folder") or
            // Xbox/Xbox360
            ("xbox", "iso") or
            ("xbox360", "iso") or
            // ISO9660 systems - ISO source
            ("ps1", "iso") or
            ("ps2", "iso") or
            ("saturn", "iso") or
            ("segacd", "iso") or
            ("psp", "iso") or
            ("cdi", "iso") or
            ("pcengine", "iso") or
            ("default", "iso") or
            // CUE sources
            ("default", "cue") or
            ("dreamcast", "cue") or
            ("ps1", "cue") or
            ("ps2", "cue") or
            ("saturn", "cue") or
            ("segacd", "cue") or
            ("cdi", "cue") or
            ("pcengine", "cue") or
            // GDI source
            ("dreamcast", "gdi");
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Property 2: Case-insensitivity — for any casing of a known system name,
    /// the result is identical to the canonical (lowercase) lookup.
    /// </summary>
    [Theory]
    [InlineData("GAMECUBE", "ISO")]
    [InlineData("gamecube", "iso")]
    [InlineData("GameCube", "Iso")]
    [InlineData("Gamecube", "ISO")]
    public void CaseInsensitivity_GameCube_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);

        Assert.Equal(new[] { "iso", "rvz", "ciso", "wbfs" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Case-insensitivity for Wii/iso.
    /// </summary>
    [Theory]
    [InlineData("WII", "ISO")]
    [InlineData("wii", "iso")]
    [InlineData("Wii", "Iso")]
    public void CaseInsensitivity_Wii_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);

        Assert.Equal(new[] { "iso", "rvz", "ciso", "wbfs" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Case-insensitivity for WiiU/iso.
    /// </summary>
    [Theory]
    [InlineData("WIIU", "ISO")]
    [InlineData("wiiu", "iso")]
    [InlineData("WiiU", "Iso")]
    public void CaseInsensitivity_WiiU_Iso(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);

        Assert.Equal(new[] { "iso", "wux", "app" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Case-insensitivity for WiiU/app.
    /// </summary>
    [Theory]
    [InlineData("WIIU", "APP")]
    [InlineData("wiiu", "app")]
    [InlineData("WiiU", "App")]
    public void CaseInsensitivity_WiiU_App(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);

        Assert.Equal(new[] { "app" }, result);
    }

    /// <summary>
    /// **Validates: Requirements 1.1, 9.1**
    ///
    /// Case-insensitivity for Directories/folder.
    /// </summary>
    [Theory]
    [InlineData("DIRECTORIES", "FOLDER")]
    [InlineData("directories", "folder")]
    [InlineData("Directories", "Folder")]
    [InlineData("Directories", "folder")]
    public void CaseInsensitivity_Directories_Folder(string system, string format)
    {
        IReadOnlyList<string> result = FormatMappings.GetTargetFormats(system, format);

        Assert.Equal(new[] { "dir" }, result);
    }
}